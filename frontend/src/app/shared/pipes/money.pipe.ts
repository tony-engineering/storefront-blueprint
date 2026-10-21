import { Pipe, PipeTransform } from '@angular/core';
import { formatMoney } from '../../../ui-kit/storefront.js';
import { Money } from '../models/api.models';

/** `{{ product.price | money }}` → "£38,500.00". */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(money: Money | null | undefined): string {
    return money ? formatMoney(money.amountMinor, money.currency) : '';
  }
}
