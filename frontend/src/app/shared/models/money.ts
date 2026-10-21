import { Money } from './api.models';

/**
 * Parses a price typed in pounds ("19.99", "38500") into integer minor units (1999, 3850000)
 * without going through floating point. Returns null for anything else.
 */
export function parseMinorUnits(text: string): number | null {
  const match = /^(\d{1,9})(?:\.(\d{1,2}))?$/.exec(text.trim());
  if (!match) {
    return null;
  }
  const pence = (match[2] ?? '').padEnd(2, '0');
  return Number(match[1]) * 100 + Number(pence);
}

/** Unit price × quantity, summed in integers. Assumes one currency (GBP), as the API does. */
export function sumMoney(items: readonly { price: Money; quantity: number }[]): Money | null {
  if (items.length === 0) {
    return null;
  }
  return {
    amountMinor: items.reduce((sum, i) => sum + i.price.amountMinor * i.quantity, 0),
    currency: items[0].price.currency,
  };
}
