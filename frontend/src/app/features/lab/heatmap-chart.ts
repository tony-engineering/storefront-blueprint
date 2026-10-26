import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import {
  heatColor,
  latencyBucketLabels,
  nearestByTime,
  windowBounds,
  windowStartIndex,
  WINDOW_SECONDS,
} from './chart-math';
import { LabSample } from './lab.models';

// Large coordinate space on purpose: CSS-sized (11px) text and 1px strokes render small, so the chart
// reads as "zoomed out" while still filling its row (see status-chart.ts).
const W = 1600;
const H = 700;
const PAD = { left: 130, right: 32, top: 28, bottom: 64 };
const PLOT_W = W - PAD.left - PAD.right;
const PLOT_H = H - PAD.top - PAD.bottom;

const LABELS = latencyBucketLabels();
const ROWS = LABELS.length;

/** Replies per second across the full latency distribution, as a heatmap (time × latency bucket, colour = count). */
@Component({
  selector: 'app-heatmap-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="sf-chart">
      <figcaption>Latency heatmap (replies per second by latency)</figcaption>
      <svg
        class="sf-chart-svg"
        [attr.viewBox]="'0 0 ' + w + ' ' + h"
        role="img"
        aria-label="Heatmap of reply latency over time"
        (pointermove)="move($event)"
        (pointerleave)="hover.set(null)"
      >
        @for (cell of cells(); track cell.key) {
          <rect
            [attr.x]="cell.x"
            [attr.y]="cell.y"
            [attr.width]="cell.w"
            [attr.height]="cell.h"
            [attr.fill]="cell.fill"
            [attr.fill-opacity]="cell.opacity"
          />
        }
        <g class="sf-chart-axis">
          @for (row of rowTicks(); track row.label) {
            <text [attr.x]="pad.left - 8" [attr.y]="row.y" text-anchor="end">{{ row.label }}</text>
          }
          <text [attr.x]="pad.left" [attr.y]="h - 8">{{ windowStart() }} s</text>
          <text [attr.x]="w - pad.right" [attr.y]="h - 8" text-anchor="end">{{ windowEnd() }} s</text>
        </g>
        @if (hoverX(); as x) {
          <line
            [attr.x1]="x"
            [attr.x2]="x"
            [attr.y1]="pad.top"
            [attr.y2]="h - pad.bottom"
            stroke="currentColor"
            stroke-opacity="0.5"
          />
        }
      </svg>
      @if (hovered(); as s) {
        <div class="sf-chart-tooltip" [style.left.%]="tooltipLeft()">
          <strong>{{ s.t }} s</strong>
          @for (row of tooltipRows(); track row.label) {
            <div>{{ row.label }}: {{ row.count }}</div>
          }
          @if (tooltipRows().length === 0) {
            <div>no replies</div>
          }
        </div>
      }
      <div class="sf-heat-scale" aria-hidden="true">
        <span>0</span>
        <span class="sf-heat-bar" [style.background]="legendGradient()"></span>
        <span>{{ maxCount() }}</span>
      </div>
    </figure>
  `,
  styles: `
    .sf-heat-scale {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-top: 0.75rem;
      font-size: 0.8rem;
      color: var(--sf-text-muted);
      font-variant-numeric: tabular-nums;
    }
    .sf-heat-bar {
      flex: 1;
      height: 0.6rem;
      border-radius: 0.2rem;
    }
  `,
})
export class HeatmapChart {
  readonly samples = input.required<readonly LabSample[]>();

  protected readonly w = W;
  protected readonly h = H;
  protected readonly pad = PAD;
  protected readonly hover = signal<number | null>(null);

  /** The last 60 s of samples drawn in the chart. */
  protected readonly view = computed(() => {
    const samples = this.samples();
    return samples.slice(windowStartIndex(samples));
  });

  /** The fixed-width (60 s) time span of the x-axis; rolls once the run passes 60 s. */
  private readonly bounds = computed(() => windowBounds(this.samples()));
  protected readonly windowStart = computed(() => this.bounds().start);
  protected readonly windowEnd = computed(() => this.bounds().end);

  protected readonly maxCount = computed(() =>
    Math.max(1, ...this.view().flatMap((s) => s.latencyBuckets ?? [])),
  );

  /** One cell per whole second, so the column width is fixed however full the window is. */
  private readonly cw = (PLOT_W / WINDOW_SECONDS);

  private xt(t: number): number {
    return PAD.left + ((t - this.bounds().start) / WINDOW_SECONDS) * PLOT_W;
  }

  /** One rectangle per (second, bucket). Fastest bucket at the bottom, slowest on top. */
  protected readonly cells = computed(() => {
    const samples = this.view();
    const max = this.maxCount();
    const denom = Math.log1p(max);
    const cw = this.cw;
    const ch = PLOT_H / ROWS;
    const inset = Math.min(1, cw * 0.08);
    const out: { key: string; x: number; y: number; w: number; h: number; fill: string; opacity: number }[] = [];
    samples.forEach((sample) => {
      const buckets = sample.latencyBuckets ?? [];
      for (let b = 0; b < ROWS; b++) {
        const count = buckets[b] ?? 0;
        const intensity = count > 0 && denom > 0 ? Math.log1p(count) / denom : 0;
        out.push({
          key: `${sample.t}-${b}`,
          x: this.xt(sample.t) + inset,
          y: PAD.top + (ROWS - 1 - b) * ch,
          w: Math.max(0.5, cw - inset * 2),
          h: Math.max(0.5, ch - 1),
          fill: count > 0 ? heatColor(intensity) : 'var(--sf-border)',
          opacity: count > 0 ? 1 : 0.12,
        });
      }
    });
    return out;
  });

  protected readonly rowTicks = computed(() => {
    const ch = PLOT_H / ROWS;
    // LABELS is fast → slow; the chart draws slow on top, so reverse for top-to-bottom rows.
    return [...LABELS].reverse().map((label, rowFromTop) => ({
      label,
      y: PAD.top + rowFromTop * ch + ch / 2 + 4,
    }));
  });

  protected readonly legendGradient = computed(
    () =>
      `linear-gradient(to right, ${[0, 0.25, 0.5, 0.75, 1]
        .map((t) => `${heatColor(t)} ${t * 100}%`)
        .join(', ')})`,
  );

  protected readonly hovered = computed(() => {
    const i = this.hover();
    return i === null ? null : (this.view()[i] ?? null);
  });

  protected readonly hoverX = computed(() => {
    const i = this.hover();
    if (i === null) {
      return null;
    }
    return this.xt(this.view()[i]?.t ?? 0) + this.cw / 2;
  });

  protected readonly tooltipLeft = computed(() => {
    const x = this.hoverX() ?? 0;
    const percent = (x / W) * 100;
    return percent > 60 ? percent - 24 : percent + 2;
  });

  protected readonly tooltipRows = computed(() => {
    const s = this.hovered();
    if (!s) {
      return [];
    }
    return (s.latencyBuckets ?? [])
      .map((count, b) => ({ label: LABELS[b], count }))
      .filter((row) => row.count > 0)
      .reverse();
  });

  protected move(event: PointerEvent): void {
    const svg = event.currentTarget as SVGSVGElement;
    const rect = svg.getBoundingClientRect();
    const localX = Math.min(Math.max(((event.clientX - rect.left) / rect.width) * W - PAD.left, 0), PLOT_W);
    const seconds = this.bounds().start + (localX / PLOT_W) * WINDOW_SECONDS;
    this.hover.set(nearestByTime(this.view(), seconds));
  }
}
