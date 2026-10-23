import { nearestIndex, niceMax, stackSamples } from './chart-math';
import { LabSample } from './lab.models';

const sample = (orders: Record<string, number>): LabSample => ({
  t: 0,
  sent: 0,
  rps: 0,
  inFlight: 0,
  p50: 0,
  p95: 0,
  errors: 0,
  replies: {},
  orders,
  ordersTotal: Object.values(orders).reduce((a, b) => a + b, 0),
});

describe('stackSamples', () => {
  it('piles each status on top of the one below it', () => {
    const bands = stackSamples(
      [sample({ Paid: 2, Pending: 3 }), sample({ Paid: 5, PaymentFailed: 1 })],
      ['Paid', 'Pending', 'PaymentFailed'],
    );

    expect(bands.map((b) => b.status)).toEqual(['Paid', 'Pending', 'PaymentFailed']);
    expect(bands[0]).toEqual({ status: 'Paid', y0: [0, 0], y1: [2, 5] });
    expect(bands[1]).toEqual({ status: 'Pending', y0: [2, 5], y1: [5, 5] });
    expect(bands[2]).toEqual({ status: 'PaymentFailed', y0: [5, 5], y1: [5, 6] });
  });

  it('ends at the total of every sample', () => {
    const samples = [sample({ Paid: 1, Pending: 1 }), sample({ Paid: 4, PaymentUnknown: 2 })];

    const top = stackSamples(samples).at(-1)!.y1;

    expect(top).toEqual(samples.map((s) => s.ordersTotal));
  });

  it('gives empty bands for no samples', () => {
    expect(stackSamples([]).every((b) => b.y0.length === 0 && b.y1.length === 0)).toBe(true);
  });
});

describe('niceMax', () => {
  it.each([
    [0, 1],
    [1, 1],
    [3, 5],
    [7, 10],
    [120, 200],
    [301, 500],
  ])('rounds %d up to %d', (value, expected) => {
    expect(niceMax(value)).toBe(expected);
  });
});

describe('nearestIndex', () => {
  it('maps a position along the plot to a sample', () => {
    expect(nearestIndex(0, 100, 11)).toBe(0);
    expect(nearestIndex(50, 100, 11)).toBe(5);
    expect(nearestIndex(100, 100, 11)).toBe(10);
    expect(nearestIndex(-20, 100, 11)).toBe(0);
    expect(nearestIndex(500, 100, 11)).toBe(10);
  });
});
