# Storefront UI Kit

A small CSS + JS kit for the tiny-house demo storefront. No dependencies, no build step.

The look (square corners, midnight indigo + golden bronze, Tenor Sans headings, Montserrat body) comes from the free [Storefront HTML/CSS UI kit](https://www.code-generator.net/ui-kits/storefront/) on code-generator.net. It has been trimmed to what this store needs and adapted for tiny houses. That page does not state a licence, so check its terms before using the kit beyond this demo.

| File | What it is |
|---|---|
| `storefront.css` | Tokens, base styles and every component. Classes are prefixed `sf-`; states use `is-*`. |
| `storefront.js` | ES module with small behaviour helpers: money formatting, order-status display, modal, toast, busy buttons, nav toggle, quantity stepper. |
| `storefront.d.ts` | Types for `storefront.js`, so strict TypeScript (Angular) can import it. |
| `demo.html` | Every component on one page, with tiny-house content and working interactions. |

## Preview

```bash
npx serve ui-kit          # or: python3 -m http.server -d ui-kit
```

Open `/demo.html`. Opening the file directly also works for the styles, but browsers block JS modules on `file://`, so the interactive parts need a local server.

## Plain HTML

```html
<link rel="stylesheet" href="storefront.css">
<script type="module">
  import { initKit } from './storefront.js';
  initKit(); // wires every data-sf-* element: nav toggle, qty steppers, modal openers
</script>
```

## Angular

1. **CSS.** When the Angular app is scaffolded, move this folder to `frontend/src/ui-kit/` and list the stylesheet first in `angular.json`:

   ```json
   "styles": ["src/ui-kit/storefront.css", "src/styles.css"]
   ```

   For production, replace the `@import` at the top of `storefront.css` with `<link rel="preconnect">` + `<link>` tags in `index.html`.

2. **JS.** Import only the helpers you need. Types come from `storefront.d.ts`.

   ```ts
   import { formatMoney, orderStatus } from '../ui-kit/storefront.js';

   @Pipe({ name: 'money' })
   export class MoneyPipe implements PipeTransform {
     transform(minorUnits: number, currency = 'GBP'): string {
       return formatMoney(minorUnits, currency);
     }
   }
   ```

   ```ts
   // order-details.component.ts
   protected status = computed(() => orderStatus(this.order().status));
   ```

   ```html
   <span class="sf-badge" [class]="'sf-badge-' + status().tone">{{ status().label }}</span>
   ```

   Don't call `initKit()` in Angular; Angular owns the DOM. Bind state in templates instead (for example `[disabled]="placing()"` and `[class.is-loading]="placing()"` on the submit button). Every CSS state works without the JS. The `bind*` helpers return a cleanup function for `ngOnDestroy` if you do use them.

## Components

| Need | Classes |
|---|---|
| Page layout | `sf-container`, `sf-section`, `sf-section-head`, `sf-eyebrow`, `sf-split` (content + sidebar), `sf-stack`, `sf-cluster` |
| Header | `sf-topnav`, `sf-topstrip`, `sf-nav-main`, `sf-nav-links`, `sf-nav-link` (`.active` or `aria-current="page"`), `sf-nav-toggle`, `sf-cart-btn`, `sf-cart-count` |
| Hero | `sf-hero`, `sf-hero-kicker`, `sf-hero-title`, `sf-hero-desc`, `sf-hero-actions`, `sf-hero-trust`, `sf-trust-item` |
| Product list | `sf-product-grid`, `sf-product-card`, `sf-product-media` (takes an `<img>`), `sf-product-badge`, `sf-product-cat`, `sf-product-title`, `sf-product-desc`, `sf-product-specs`, `sf-product-price-row` |
| Prices | `sf-price`, `sf-price-sale`, `sf-price-original` |
| Buttons | `sf-btn` + `sf-btn-primary` / `-accent` / `-outline` / `-ghost` / `-danger`; `sf-btn-sm` / `-lg` / `-block` / `-icon`; `is-loading` |
| Forms | `sf-form-grid` (+ `sf-span-2`), `sf-form-group`, `sf-label`, `sf-input`, `sf-select`, `sf-textarea`, `sf-field-hint`, `sf-field-error`, `sf-checkbox-label`, `sf-checkbox`, `sf-qty` |
| Status | `sf-badge` + `-sale` / `-new` / `-success` / `-warning` / `-danger` / `-info` / `-default`; `sf-alert` + `-success` / `-warning` / `-danger` / `-info` |
| Cart, admin, order lines | `sf-panel` (`-head`, `-body`, `-foot`), `sf-table-scroll`, `sf-table` (`is-num`, `is-actions`), `sf-prod-cell`, `sf-prod-thumb`, `sf-summary`, `sf-summary-row`, `sf-summary-total` |
| Order details | `sf-timeline`, `sf-timeline-item` (`is-done` / `is-current` / `is-failed`) |
| Dialogs | `<dialog class="sf-modal">` with `sf-modal-head`, `-body`, `-foot` |
| Misc | `sf-empty`, `sf-footer`, `sf-logo`, `sf-divider`, `sf-visually-hidden`, `sf-text-*` utilities |

## JS helpers

| Helper | Use |
|---|---|
| `formatMoney(minorUnits, currency?, locale?)` | Formats integer pence as `£38,500.00`. Throws on non-integers to catch a pounds/pence mix-up early. |
| `ORDER_STATUS`, `orderStatus(status)` | Label, tone and customer message for `Pending`, `Paid`, `PaymentFailed`, `PaymentRetrying`, `PaymentUnavailable`, `PaymentUnknown`, `NeedsAttention`. Unknown statuses fall back to a neutral badge. |
| `openModal`, `closeModal`, `confirmModal` | Native `<dialog>`. `await confirmModal('id')` resolves with the clicked button's `value`. |
| `toast(message, { tone, duration })` | Live-region notification. Text is set with `textContent`, so API messages are safe to pass in. |
| `setBusy`, `withBusy` | Disables a button and shows a spinner while a request runs, to prevent double submits. |
| `bindNavToggle`, `bindQty`, `initKit` | DOM wiring for plain HTML pages. Each returns a cleanup function. |

## Theming

Everything reads CSS variables from `:root` at the top of `storefront.css`. For example, to soften the corners and change the accent:

```css
:root {
  --sf-radius: 8px;
  --sf-radius-sm: 4px;
  --sf-accent: #3F7D58;
  --sf-accent-hover: #2F6145;
  --sf-accent-ink: #2F6145;
}
```

## Changes from the original kit

- **Trimmed.** Kept the navbar, promo hero, product cards, buttons, form controls, badges, order table, status feed, modal and footer. Dropped the filter sidebar, split hero, metrics cards, login card, toggle switch, star ratings, wishlist and search.
- **Added for the store's flows.** Alerts and toasts for payment outcomes, an empty state, a quantity stepper, a danger button, warning and info badges, a busy-button state, panels, a cart summary, and a mobile menu (the original hid the nav links below 992px with no way to reach them).
- **Accessibility.** The accent button uses dark text, because white on bronze is 2.5:1. Bronze text uses a darker `--sf-accent-ink` (4.9:1). `--sf-text-light` is darkened to 4.7:1. Focus rings are visible. Modals use native `<dialog>` for focus trapping and Esc. Reduced-motion preferences are respected.
- **Cleanups.** Removed the `!important` padding overrides, fixed the invalid `color: #sf-bg` declaration, prefixed the utilities (`.text-muted` became `.sf-text-muted`) so they cannot clash with framework classes, and renamed the hero, timeline and table classes to describe what they are.
