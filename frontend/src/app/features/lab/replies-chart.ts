import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import {
  httpClass,
  httpClassColor,
  nearestByTime,
  niceMax,
  repliesPerSecond,
  windowBounds,
  windowStartIndex,
  WINDOW_SECONDS,
} from './chart-math';
import { LabSample } from './lab.models';

// Large coordinate space on purpose (see status-chart.ts): CSS-sized text reads as "zoomed out".
const W = 1600;
const H = 700;
const PAD = { left: 108, right: 32, top: 28, bottom: 64 };
const PLOT_W = W - PAD.left - PAD.right;
const PLOT_H = H - PAD.top - PAD.bottom;

const CLASS_ORDER = ['2xx', '4xx', '5xx', 'other'];

interface CodeStyle {
  code: string;
  color: string;
  opacity: number;
  total: number;
}

/** Replies received each second, stacked by exact HTTP status code (retries included), over a rolling 60 s window. */
@Component({
  selector: 'app-replies-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="sf-chart">
      <figcaption>Replies per second by HTTP code</figcaption>
      <svg
        class="sf-chart-svg"
        [attr.viewBox]="'0 0 ' + w + ' ' + h"
        role="img"
        aria-label="Stacked bar chart of replies per second by HTTP status code"
        (pointermove)="move($event)"
        (pointerleave)="hover.set(null)"
      >
        <g class="sf-chart-grid">
          @for (tick of yTicks(); track tick.value) {
            <line [attr.x1]="pad.left" [attr.x2]="w - pad.right" [attr.y1]="tick.y" [attr.y2]="tick.y" />
          }
        </g>
        @for (seg of segments(); track seg.key) {
          <rect
            [attr.x]="seg.x"
            [attr.y]="seg.y"
            [attr.width]="seg.w"
            [attr.height]="seg.h"
            [attr.fill]="seg.color"
            [attr.fill-opacity]="seg.opacity"
          />
        }
        <g class="sf-chart-axis">
          @for (tick of yTicks(); track tick.value) {
            <text [attr.x]="pad.left - 8" [attr.y]="tick.y + 4" text-anchor="end">{{ tick.value }}</text>
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
      @if (hovered(); as rows) {
        <div class="sf-chart-tooltip" [style.left.%]="tooltipLeft()">
          <strong>{{ hoveredT() }} s</strong>
          @for (row of rows; track row.code) {
            <div>{{ row.code }}: {{ row.count }}</div>
          }
          @if (rows.length === 0) {
            <div>no replies</div>
          }
        </div>
      }
      <ul class="sf-legend">
        @for (item of codes(); track item.code) {
          <li class="sf-legend-item">
            <span
              class="sf-legend-swatch"
              [style.--sf-swatch]="item.color"
              [style.opacity]="item.opacity"
            ></span>
            {{ item.code }} · {{ item.total }}
          </li>
        }
      </ul>
    </figure>
  `,
})
export class RepliesChart {
  readonly samples = input.required<readonly LabSample[]>();

  protected readonly w = W;
  protected readonly h = H;
  protected readonly pad = PAD;
  protected readonly hover = signal<number | null>(null);

  /** The samples inside the current 60 s window. */
  private readonly view = computed(() => {
    const samples = this.samples();
    return samples.slice(windowStartIndex(samples));
  });

  /** The fixed-width (60 s) time span of the x-axis; rolls once the run passes 60 s. */
  private readonly bounds = computed(() => windowBounds(this.samples()));
  protected readonly windowStart = computed(() => this.bounds().start);
  protected readonly windowEnd = computed(() => this.bounds().end);

  // Deltas are computed over the full run, then clipped to the window, so the first visible second is
  // a real per-second count, not the cumulative total up to that point.
  private readonly perSecond = computed(() => {
    const samples = this.samples();
    return repliesPerSecond(samples).slice(windowStartIndex(samples));
  });

  /** Every code that occurred in the whole run, ordered by class then number, each with a shade. */
  protected readonly codes = computed<CodeStyle[]>(() => {
    const cumulative = this.samples().at(-1)?.replies ?? {};
    const present = Object.keys(cumulative).filter((code) => cumulative[code] > 0);
    present.sort(
      (a, b) =>
        CLASS_ORDER.indexOf(httpClass(a)) - CLASS_ORDER.indexOf(httpClass(b)) || Number(a) - Number(b),
    );
    const seenInClass: Record<string, number> = {};
    return present.map((code) => {
      const cls = httpClass(code);
      const rank = seenInClass[cls] ?? 0;
      seenInClass[cls] = rank + 1;
      return {
        code,
        color: httpClassColor(code),
        opacity: Math.max(0.45, 1 - rank * 0.28),
        total: cumulative[code],
      };
    });
  });

  private readonly yMax = computed(() =>
    niceMax(Math.max(1, ...this.perSecond().map((p) => Object.values(p).reduce((a, b) => a + b, 0)))),
  );

  private xt(t: number): number {
    return PAD.left + ((t - this.bounds().start) / WINDOW_SECONDS) * PLOT_W;
  }

  private y(value: number): number {
    return PAD.top + PLOT_H - (value / this.yMax()) * PLOT_H;
  }

  protected readonly yTicks = computed(() => {
    const max = this.yMax();
    return [0, 0.25, 0.5, 0.75, 1].map((f) => ({ value: Math.round(max * f), y: this.y(max * f) }));
  });

  protected readonly segments = computed(() => {
    const perSecond = this.perSecond();
    const view = this.view();
    const codes = this.codes();
    if (perSecond.length === 0) {
      return [];
    }
    // One bar per second, so the width is fixed regardless of how much of the window is filled.
    const barW = Math.max(1, (PLOT_W / WINDOW_SECONDS) * 0.85);
    const out: { key: string; x: number; y: number; w: number; h: number; color: string; opacity: number }[] = [];
    perSecond.forEach((perCode, i) => {
      let base = 0;
      const cx = this.xt(view[i]?.t ?? 0);
      for (const style of codes) {
        const count = perCode[style.code] ?? 0;
        if (count <= 0) {
          continue;
        }
        const top = base + count;
        out.push({
          key: `${i}-${style.code}`,
          x: cx - barW / 2,
          y: this.y(top),
          w: barW,
          h: Math.max(0.5, this.y(base) - this.y(top)),
          color: style.color,
          opacity: style.opacity,
        });
        base = top;
      }
    });
    return out;
  });

  protected readonly hoveredT = computed(() => {
    const i = this.hover();
    return i === null ? 0 : (this.view()[i]?.t ?? 0);
  });

  protected readonly hovered = computed(() => {
    const i = this.hover();
    if (i === null) {
      return null;
    }
    const perCode = this.perSecond()[i] ?? {};
    return this.codes()
      .filter((style) => (perCode[style.code] ?? 0) > 0)
      .map((style) => ({ code: style.code, count: perCode[style.code] }));
  });

  protected readonly hoverX = computed(() => {
    const i = this.hover();
    return i === null ? null : this.xt(this.view()[i]?.t ?? 0);
  });

  protected readonly tooltipLeft = computed(() => {
    const x = this.hoverX() ?? 0;
    const percent = (x / W) * 100;
    return percent > 60 ? percent - 24 : percent + 2;
  });

  protected move(event: PointerEvent): void {
    const svg = event.currentTarget as SVGSVGElement;
    const rect = svg.getBoundingClientRect();
    const localX = Math.min(Math.max(((event.clientX - rect.left) / rect.width) * W - PAD.left, 0), PLOT_W);
    const seconds = this.bounds().start + (localX / PLOT_W) * WINDOW_SECONDS;
    this.hover.set(nearestByTime(this.view(), seconds));
  }
}
