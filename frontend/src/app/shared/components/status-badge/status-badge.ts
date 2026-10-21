import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { orderStatus } from '../../../../ui-kit/storefront.js';

/** Order status badge; label and colour come from the UI kit's single status map. */
@Component({
  selector: 'app-status-badge',
  template: `<span [class]="'sf-badge sf-badge-' + meta().tone">{{ meta().label }}</span>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusBadge {
  readonly status = input.required<string>();
  protected readonly meta = computed(() => orderStatus(this.status()));
}
