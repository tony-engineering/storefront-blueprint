import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Reconciliation } from './lab.models';

interface Slice {
  key: string;
  label: string;
  count: number;
  percent: number;
  color: string;
  dash: string;
  offset: number;
}

const R = 80;
const C = 2 * Math.PI * R;

const DEFS: readonly { key: keyof Reconciliation; label: string; color: string }[] = [
  { key: 'confirmed', label: 'Confirmed', color: 'var(--sf-success)' },
  { key: 'recovered', label: 'Recovered (silent success)', color: 'var(--sf-warning)' },
  { key: 'dropped', label: 'Dropped (lost)', color: 'var(--sf-danger)' },
];

/**
 * Where the keys ended up as a donut: confirmed, recovered (an order existed even though the generator
 * never got the reply) and dropped (no order — a real loss). The middle calls out the dropped share.
 */
@Component({
  selector: 'app-order-donut',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="sf-chart">
      <figcaption>Where the orders went</figcaption>
      <div class="sf-donut">
        <svg viewBox="0 0 220 220" role="img" [attr.aria-label]="ariaLabel()">
          <g transform="rotate(-90 110 110)">
            <circle cx="110" cy="110" [attr.r]="r" fill="none" class="sf-donut-track" stroke-width="26" />
            @for (slice of slices(); track slice.key) {
              <circle
                cx="110"
                cy="110"
                [attr.r]="r"
                fill="none"
                [attr.stroke]="slice.color"
                stroke-width="26"
                [attr.stroke-dasharray]="slice.dash"
                [attr.stroke-dashoffset]="slice.offset"
              />
            }
          </g>
          <text x="110" y="104" text-anchor="middle" class="sf-donut-big" [class.is-clean]="droppedPercent() === 0">
            {{ droppedPercent() }}%
          </text>
          <text x="110" y="130" text-anchor="middle" class="sf-donut-sub">dropped</text>
        </svg>
        <ul class="sf-legend sf-donut-legend">
          @for (slice of slices(); track slice.key) {
            <li class="sf-legend-item">
              <span class="sf-legend-swatch" [style.--sf-swatch]="slice.color"></span>
              {{ slice.label }} · {{ slice.count }} ({{ slice.percent }}%)
            </li>
          }
        </ul>
      </div>
    </figure>
  `,
  styles: `
    .sf-donut {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 1.5rem;
    }
    .sf-donut svg {
      width: 200px;
      height: 200px;
      flex: none;
    }
    .sf-donut-track {
      stroke: color-mix(in srgb, currentColor 12%, transparent);
    }
    .sf-donut-big {
      font-size: 44px;
      font-weight: 700;
      fill: var(--sf-danger);
    }
    .sf-donut-big.is-clean {
      fill: var(--sf-success);
    }
    .sf-donut-sub {
      font-size: 15px;
      fill: currentColor;
      opacity: 0.7;
    }
    .sf-donut-legend {
      display: grid;
      gap: 0.4rem;
    }
  `,
})
export class OrderDonut {
  readonly reconciliation = input.required<Reconciliation>();

  protected readonly r = R;

  protected readonly total = computed(() => {
    const rec = this.reconciliation();
    return rec.confirmed + rec.recovered + rec.dropped;
  });

  protected readonly droppedPercent = computed(() => {
    const total = this.total();
    return total === 0 ? 0 : Math.round((this.reconciliation().dropped / total) * 100);
  });

  protected readonly slices = computed<Slice[]>(() => {
    const rec = this.reconciliation();
    const total = this.total();
    let accLen = 0;
    return DEFS.filter((d) => rec[d.key] > 0).map((d) => {
      const count = rec[d.key];
      const fraction = total === 0 ? 0 : count / total;
      const len = fraction * C;
      const slice: Slice = {
        key: d.key,
        label: d.label,
        count,
        percent: Math.round(fraction * 100),
        color: d.color,
        dash: `${len} ${C}`,
        offset: -accLen,
      };
      accLen += len;
      return slice;
    });
  });

  protected readonly ariaLabel = computed(() => {
    const rec = this.reconciliation();
    return `Order outcomes: ${rec.confirmed} confirmed, ${rec.recovered} recovered, ${rec.dropped} dropped.`;
  });
}
