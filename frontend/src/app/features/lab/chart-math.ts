import { LabSample } from './lab.models';

/** Bottom to top. Matches the order of the legend. */
export const STACK_ORDER = [
  'Paid',
  'Pending',
  'PaymentRetrying',
  'PaymentUnknown',
  'PaymentFailed',
  'PaymentUnavailable',
  'NeedsAttention',
] as const;

export interface StackedBand {
  status: string;
  /** Lower edge per sample. */
  y0: number[];
  /** Upper edge per sample; y1 − y0 is the status's count. */
  y1: number[];
}

/** Piles the status counts of each sample on top of each other, in `order`. */
export function stackSamples(
  samples: readonly LabSample[],
  order: readonly string[] = STACK_ORDER,
): StackedBand[] {
  const running = samples.map(() => 0);
  return order.map((status) => {
    const y0 = [...running];
    const y1 = samples.map((s, i) => {
      running[i] += s.orders[status] ?? 0;
      return running[i];
    });
    return { status, y0, y1 };
  });
}

/** A round number at or above `value`, so the axis ends on a tidy tick (1, 2, 5 × 10ⁿ). */
export function niceMax(value: number): number {
  if (value <= 0) {
    return 1;
  }
  const power = 10 ** Math.floor(Math.log10(value));
  const fraction = value / power;
  const step = [1, 2, 5, 10].find((s) => fraction <= s) ?? 10;
  return step * power;
}

/** Index of the sample closest to `x` along an axis that maps `0…width` onto the samples. */
export function nearestIndex(x: number, width: number, count: number): number {
  if (count <= 1 || width <= 0) {
    return 0;
  }
  return Math.min(count - 1, Math.max(0, Math.round((x / width) * (count - 1))));
}
