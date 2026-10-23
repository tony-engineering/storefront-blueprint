import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { nearestIndex, niceMax } from './chart-math';
import { LabSample } from './lab.models';

const W = 600;
const H = 220;
const PAD = { left: 52, right: 44, top: 24, bottom: 26 };
const PLOT_W = W - PAD.left - PAD.right;
const PLOT_H = H - PAD.top - PAD.bottom;

/** Replies per second (bars, right axis) and p95 latency (line, left axis). */
@Component({
  selector: 'app-latency-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="sf-chart">
      <figcaption>Replies per second and p95 latency</figcaption>
      <svg
        class="sf-chart-svg"
        [attr.viewBox]="'0 0 ' + w + ' ' + h"
        role="img"
        aria-label="Replies per second and 95th percentile latency over time"
        (pointermove)="move($event)"
        (pointerleave)="hover.set(null)"
      >
        <g class="sf-chart-grid">
          @for (tick of ticks(); track $index) {
            <line
              [attr.x1]="pad.left"
              [attr.x2]="w - pad.right"
              [attr.y1]="tick.y"
              [attr.y2]="tick.y"
            />
          }
        </g>
        @for (bar of bars(); track $index) {
          <rect
            [attr.x]="bar.x"
            [attr.y]="bar.y"
            [attr.width]="bar.w"
            [attr.height]="bar.h"
            fill="var(--sf-status-pending)"
          />
        }
        <path [attr.d]="line()" fill="none" stroke="var(--sf-primary)" stroke-width="2" />
        <g class="sf-chart-axis">
          @for (tick of ticks(); track $index) {
            <text [attr.x]="pad.left - 6" [attr.y]="tick.y + 4" text-anchor="end">
              {{ tick.ms }}
            </text>
            <text [attr.x]="w - pad.right + 6" [attr.y]="tick.y + 4">{{ tick.rps }}</text>
          }
          <text [attr.x]="pad.left" [attr.y]="h - 6">0 s</text>
          <text [attr.x]="w - pad.right" [attr.y]="h - 6" text-anchor="end">{{ lastT() }} s</text>
          <text [attr.x]="pad.left - 6" [attr.y]="pad.top - 12" text-anchor="end">ms</text>
          <text [attr.x]="w - pad.right + 6" [attr.y]="pad.top - 12">/s</text>
        </g>
      </svg>
      @if (hovered(); as s) {
        <div class="sf-chart-tooltip" [style.left.%]="tooltipLeft()">
          <strong>{{ s.t }} s</strong>
          <div>p95: {{ s.p95 }} ms</div>
          <div>p50: {{ s.p50 }} ms</div>
          <div>Replies: {{ s.rps }}/s</div>
        </div>
      }
      <ul class="sf-legend">
        <li class="sf-legend-item">
          <span class="sf-legend-swatch" style="--sf-swatch: var(--sf-primary)"></span>p95 latency
          (left)
        </li>
        <li class="sf-legend-item">
          <span class="sf-legend-swatch" style="--sf-swatch: var(--sf-status-pending)"></span
          >Replies per second (right)
        </li>
      </ul>
    </figure>
  `,
})
export class LatencyChart {
  readonly samples = input.required<readonly LabSample[]>();

  protected readonly w = W;
  protected readonly h = H;
  protected readonly pad = PAD;
  protected readonly hover = signal<number | null>(null);

  protected readonly lastT = computed(() => this.samples().at(-1)?.t ?? 0);

  private readonly msMax = computed(() =>
    niceMax(Math.max(1, ...this.samples().map((s) => s.p95))),
  );
  private readonly rpsMax = computed(() =>
    niceMax(Math.max(1, ...this.samples().map((s) => s.rps))),
  );

  private x(i: number): number {
    const n = this.samples().length;
    return PAD.left + (n <= 1 ? 0 : (i / (n - 1)) * PLOT_W);
  }

  private y(fraction: number): number {
    return PAD.top + PLOT_H - fraction * PLOT_H;
  }

  protected readonly ticks = computed(() =>
    [0, 0.5, 1].map((f) => ({
      y: this.y(f),
      ms: Math.round(this.msMax() * f),
      rps: Math.round(this.rpsMax() * f),
    })),
  );

  protected readonly bars = computed(() => {
    const samples = this.samples();
    const w = Math.max(2, (PLOT_W / Math.max(samples.length, 1)) * 0.7);
    return samples.map((s, i) => {
      const top = this.y(s.rps / this.rpsMax());
      return { x: this.x(i) - w / 2, y: top, w, h: this.y(0) - top };
    });
  });

  protected readonly line = computed(() => {
    const samples = this.samples();
    return samples.length < 2
      ? ''
      : 'M' + samples.map((s, i) => `${this.x(i)},${this.y(s.p95 / this.msMax())}`).join(' L');
  });

  protected readonly hovered = computed(() => {
    const i = this.hover();
    return i === null ? null : (this.samples()[i] ?? null);
  });

  protected readonly tooltipLeft = computed(() => {
    const i = this.hover() ?? 0;
    const percent = (this.x(i) / W) * 100;
    return percent > 60 ? percent - 28 : percent + 2;
  });

  protected move(event: PointerEvent): void {
    const rect = (event.currentTarget as SVGSVGElement).getBoundingClientRect();
    const x = ((event.clientX - rect.left) / rect.width) * W - PAD.left;
    this.hover.set(nearestIndex(x, PLOT_W, this.samples().length));
  }
}
