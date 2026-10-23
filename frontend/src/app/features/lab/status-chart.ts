import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { orderStatus, orderStatusColor } from '../../../ui-kit/storefront.js';
import { nearestIndex, niceMax, STACK_ORDER, stackSamples } from './chart-math';
import { LabSample } from './lab.models';

const W = 600;
const H = 220;
const PAD = { left: 44, right: 12, top: 10, bottom: 26 };
const PLOT_W = W - PAD.left - PAD.right;
const PLOT_H = H - PAD.top - PAD.bottom;

/** Orders by status over time, as a stacked area. Plain SVG, drawn from the kit's chart classes. */
@Component({
  selector: 'app-status-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="sf-chart">
      <figcaption>Orders by status</figcaption>
      <svg
        class="sf-chart-svg"
        [attr.viewBox]="'0 0 ' + w + ' ' + h"
        role="img"
        aria-label="Stacked area chart of orders by status over time"
        (pointermove)="move($event)"
        (pointerleave)="hover.set(null)"
      >
        <g class="sf-chart-grid">
          @for (tick of yTicks(); track tick.value) {
            <line
              [attr.x1]="pad.left"
              [attr.x2]="w - pad.right"
              [attr.y1]="tick.y"
              [attr.y2]="tick.y"
            />
          }
        </g>
        @for (band of bands(); track band.status) {
          <path [attr.d]="band.path" [attr.fill]="band.color" fill-opacity="0.9" />
        }
        <g class="sf-chart-axis">
          @for (tick of yTicks(); track tick.value) {
            <text [attr.x]="pad.left - 6" [attr.y]="tick.y + 4" text-anchor="end">
              {{ tick.value }}
            </text>
          }
          <text [attr.x]="pad.left" [attr.y]="h - 6">0 s</text>
          <text [attr.x]="w - pad.right" [attr.y]="h - 6" text-anchor="end">{{ lastT() }} s</text>
        </g>
        @if (hoverX(); as x) {
          <line
            [attr.x1]="x"
            [attr.x2]="x"
            [attr.y1]="pad.top"
            [attr.y2]="h - pad.bottom"
            stroke="currentColor"
            stroke-opacity="0.4"
          />
        }
      </svg>
      @if (hovered(); as s) {
        <div class="sf-chart-tooltip" [style.left.%]="tooltipLeft()">
          <strong>{{ s.t }} s</strong>
          @for (row of tooltipRows(); track row.status) {
            <div>{{ row.label }}: {{ row.count }}</div>
          }
        </div>
      }
      <ul class="sf-legend">
        @for (item of legend(); track item.status) {
          <li class="sf-legend-item">
            <span class="sf-legend-swatch" [style.--sf-swatch]="item.color"></span>{{ item.label }}
          </li>
        }
      </ul>
    </figure>
  `,
})
export class StatusChart {
  readonly samples = input.required<readonly LabSample[]>();

  protected readonly w = W;
  protected readonly h = H;
  protected readonly pad = PAD;
  protected readonly hover = signal<number | null>(null);

  protected readonly lastT = computed(() => this.samples().at(-1)?.t ?? 0);

  private readonly yMax = computed(() =>
    niceMax(Math.max(1, ...this.samples().map((s) => s.ordersTotal))),
  );

  private x(i: number, count: number): number {
    return PAD.left + (count <= 1 ? 0 : (i / (count - 1)) * PLOT_W);
  }

  private y(value: number): number {
    return PAD.top + PLOT_H - (value / this.yMax()) * PLOT_H;
  }

  protected readonly yTicks = computed(() => {
    const max = this.yMax();
    return [0, 0.25, 0.5, 0.75, 1].map((f) => ({ value: Math.round(max * f), y: this.y(max * f) }));
  });

  protected readonly bands = computed(() => {
    const samples = this.samples();
    if (samples.length < 2) {
      return [];
    }
    return stackSamples(samples)
      .filter((band) => band.y1.some((v, i) => v > band.y0[i]))
      .map((band) => {
        const n = samples.length;
        const top = band.y1.map((v, i) => `${this.x(i, n)},${this.y(v)}`);
        const bottom = band.y0.map((v, i) => `${this.x(i, n)},${this.y(v)}`).reverse();
        return {
          status: band.status,
          color: orderStatusColor(band.status),
          path: `M${top.join(' L')} L${bottom.join(' L')} Z`,
        };
      });
  });

  /** Only statuses that occurred, so the legend stays short. */
  protected readonly legend = computed(() => {
    const seen = new Set(
      this.samples().flatMap((s) => Object.keys(s.orders).filter((k) => s.orders[k] > 0)),
    );
    return STACK_ORDER.filter((status) => seen.has(status)).map((status) => ({
      status,
      label: orderStatus(status).label,
      color: orderStatusColor(status),
    }));
  });

  protected readonly hovered = computed(() => {
    const i = this.hover();
    return i === null ? null : (this.samples()[i] ?? null);
  });

  protected readonly hoverX = computed(() => {
    const i = this.hover();
    return i === null ? null : this.x(i, this.samples().length);
  });

  protected readonly tooltipLeft = computed(() => {
    const x = this.hoverX() ?? 0;
    // Keep the box inside the figure: flip to the left side of the cursor on the right half.
    const percent = (x / W) * 100;
    return percent > 60 ? percent - 28 : percent + 2;
  });

  protected readonly tooltipRows = computed(() => {
    const s = this.hovered();
    if (!s) {
      return [];
    }
    return Object.entries(s.orders)
      .filter(([, count]) => count > 0)
      .map(([status, count]) => ({ status, label: orderStatus(status).label, count }));
  });

  protected move(event: PointerEvent): void {
    const svg = event.currentTarget as SVGSVGElement;
    const rect = svg.getBoundingClientRect();
    const x = ((event.clientX - rect.left) / rect.width) * W - PAD.left;
    this.hover.set(nearestIndex(x, PLOT_W, this.samples().length));
  }
}
