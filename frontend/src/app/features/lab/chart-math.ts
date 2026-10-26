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

/** The charts show a fixed window of this many seconds, so they stay readable and stop growing. */
export const WINDOW_SECONDS = 60;

/**
 * The time span the x-axis covers: always {@link WINDOW_SECONDS} wide. A short run shows 0…60 (data
 * fills from the left); once the run passes 60 s the window rolls to end at the latest second.
 */
export function windowBounds(
  samples: readonly LabSample[],
  seconds = WINDOW_SECONDS,
): { start: number; end: number } {
  const lastT = samples[samples.length - 1]?.t ?? 0;
  const start = Math.max(0, lastT - seconds);
  return { start, end: start + seconds };
}

/** Index of the first sample inside the current window (its `t` ≥ the window start). */
export function windowStartIndex(
  samples: readonly LabSample[],
  seconds = WINDOW_SECONDS,
): number {
  if (samples.length === 0) {
    return 0;
  }
  const { start } = windowBounds(samples, seconds);
  const index = samples.findIndex((s) => s.t >= start);
  return index < 0 ? 0 : index;
}

/** Index of the sample whose `t` is closest to `seconds` (both axes run in whole seconds). */
export function nearestByTime(samples: readonly { t: number }[], seconds: number): number {
  let best = 0;
  let bestDistance = Infinity;
  samples.forEach((s, i) => {
    const distance = Math.abs(s.t - seconds);
    if (distance < bestDistance) {
      bestDistance = distance;
      best = i;
    }
  });
  return best;
}

/** Upper edges (ms) of the latency histogram. Mirrors LoadLab `Metrics.LatencyBoundsMs`. */
export const LATENCY_BOUNDS_MS = [10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000] as const;

/** Row labels for the heatmap, one per bucket (fast → slow), including the final overflow bucket. */
export function latencyBucketLabels(): string[] {
  const label = (ms: number) => (ms >= 1000 ? `${ms / 1000} s` : `${ms} ms`);
  return [...LATENCY_BOUNDS_MS.map(label), `> ${label(LATENCY_BOUNDS_MS[LATENCY_BOUNDS_MS.length - 1])}`];
}

const HEAT_STOPS: readonly [number, readonly [number, number, number]][] = [
  [0.0, [38, 24, 68]],
  [0.18, [40, 70, 180]],
  [0.38, [30, 180, 200]],
  [0.55, [70, 200, 110]],
  [0.7, [205, 215, 60]],
  [0.85, [240, 160, 50]],
  [1.0, [150, 30, 25]],
];

/** Maps a 0…1 intensity to a heatmap colour (deep blue → cyan → green → yellow → red). */
export function heatColor(t: number): string {
  const x = Math.min(1, Math.max(0, t));
  for (let i = 1; i < HEAT_STOPS.length; i++) {
    const [t1, c1] = HEAT_STOPS[i];
    if (x <= t1) {
      const [t0, c0] = HEAT_STOPS[i - 1];
      const f = t1 === t0 ? 0 : (x - t0) / (t1 - t0);
      const ch = (k: 0 | 1 | 2) => Math.round(c0[k] + (c1[k] - c0[k]) * f);
      return `rgb(${ch(0)}, ${ch(1)}, ${ch(2)})`;
    }
  }
  return 'rgb(150, 30, 25)';
}

/** The HTTP reply classes stacked bottom → top, with the kit colour token for each. */
export const HTTP_CLASSES = [
  { key: '2xx', label: '2xx ok', color: 'var(--sf-success)' },
  { key: '4xx', label: '4xx declined/retry', color: 'var(--sf-warning-border)' },
  { key: '5xx', label: '5xx server error', color: 'var(--sf-danger)' },
  { key: 'other', label: 'other', color: 'var(--sf-text-light)' },
] as const;

/** Which class an HTTP status code belongs to (gives the band its colour). */
export function httpClass(code: string): string {
  if (code.startsWith('2')) return '2xx';
  if (code.startsWith('4')) return '4xx';
  if (code.startsWith('5')) return '5xx';
  return 'other';
}

/** The kit colour token for a status code's class. */
export function httpClassColor(code: string): string {
  return HTTP_CLASSES.find((c) => c.key === httpClass(code))?.color ?? 'var(--sf-text-light)';
}

/**
 * Replies received during each second, by exact status code. `replies` on a sample is cumulative,
 * so this is the difference from the previous sample.
 */
export function repliesPerSecond(samples: readonly LabSample[]): Record<string, number>[] {
  return samples.map((sample, i) => {
    const previous = samples[i - 1]?.replies ?? {};
    const perCode: Record<string, number> = {};
    for (const [code, total] of Object.entries(sample.replies)) {
      const delta = total - (previous[code] ?? 0);
      if (delta !== 0) {
        perCode[code] = delta;
      }
    }
    return perCode;
  });
}
