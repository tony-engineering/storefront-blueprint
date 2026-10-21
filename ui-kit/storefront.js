/**
 * Storefront UI Kit · v1.0 — behaviour helpers for storefront.css
 *
 * A dependency-free ES module. Use it in one of two ways:
 *
 *   1. Plain HTML pages: call initKit() once. It wires up every element that
 *      carries a data-sf-* attribute (nav toggle, quantity steppers, modal
 *      openers) and returns a cleanup function.
 *
 *   2. Angular (or any framework): import only the helpers you need and call
 *      them from your components. Every bind* function returns a cleanup
 *      function to call from ngOnDestroy. Don't call initKit() there, because
 *      Angular owns the DOM.
 *
 * Types for TypeScript consumers live in storefront.d.ts.
 */

/* ------------------------------------------------------------------------ */
/* Money                                                                     */
/* ------------------------------------------------------------------------ */

const moneyFormatters = new Map();

/**
 * Format an integer amount in minor units (pence, cents) as currency.
 * The API stores money as integer minor units, so the UI never does
 * floating-point arithmetic on prices: sum integers, format at the edge.
 *
 *   formatMoney(3850000)         → "£38,500.00"
 *   formatMoney(1999, 'EUR', 'pt-PT') → "19,99 €"
 *
 * @param {number} minorUnits  integer, e.g. 3850000 for £38,500.00
 * @param {string} [currency='GBP']  ISO 4217 code
 * @param {string} [locale='en-GB']
 * @returns {string}
 */
export function formatMoney(minorUnits, currency = 'GBP', locale = 'en-GB') {
    if (!Number.isSafeInteger(minorUnits)) {
        throw new TypeError(`formatMoney expects an integer amount in minor units, got ${minorUnits}`);
    }
    const key = `${locale}|${currency}`;
    let formatter = moneyFormatters.get(key);
    if (!formatter) {
        formatter = new Intl.NumberFormat(locale, { style: 'currency', currency });
        moneyFormatters.set(key, formatter);
    }
    // Intl knows each currency's minor-unit exponent (GBP 2, JPY 0, BHD 3).
    const exponent = formatter.resolvedOptions().maximumFractionDigits;
    return formatter.format(minorUnits / 10 ** exponent);
}

/* ------------------------------------------------------------------------ */
/* Order status → UI                                                         */
/* ------------------------------------------------------------------------ */

/**
 * Display metadata for each order status the API returns. Mirrors the order
 * state machine in ARCHITECTURE.md. `tone` maps straight onto the kit's
 * classes: `sf-badge-${tone}`, `sf-alert-${tone}`, toast({ tone }).
 */
export const ORDER_STATUS = Object.freeze({
    Pending: Object.freeze({
        label: 'Processing',
        tone: 'info',
        message: 'We are processing your payment.',
    }),
    Paid: Object.freeze({
        label: 'Paid',
        tone: 'success',
        message: 'Payment received. Your order is confirmed.',
    }),
    PaymentFailed: Object.freeze({
        label: 'Payment declined',
        tone: 'danger',
        message: 'Your payment was declined and nothing was charged. Please try another card.',
    }),
    PaymentRetrying: Object.freeze({
        label: 'Retrying payment',
        tone: 'warning',
        message: 'The payment provider is not responding. We have not charged you and are retrying automatically; this page updates by itself.',
    }),
    PaymentUnavailable: Object.freeze({
        label: 'Payment unavailable',
        tone: 'danger',
        message: 'The payment provider is not available at the moment. You have not been charged. Please try again later.',
    }),
    PaymentUnknown: Object.freeze({
        label: 'Confirming payment',
        tone: 'warning',
        message: 'We are confirming your payment with the bank. Do not pay again; this page updates automatically.',
    }),
    NeedsAttention: Object.freeze({
        label: 'Needs attention',
        tone: 'danger',
        message: 'We could not confirm your payment. Our team will contact you. Please do not pay again.',
    }),
});

/**
 * Look up display metadata for a status. Unknown statuses (e.g. a new one
 * added to the API before the UI knows about it) degrade to a neutral badge
 * instead of throwing.
 *
 * @param {string} status
 * @returns {{label: string, tone: 'success'|'warning'|'danger'|'info', message: string}}
 */
export function orderStatus(status) {
    return Object.hasOwn(ORDER_STATUS, status)
        ? ORDER_STATUS[status]
        : { label: String(status), tone: 'info', message: '' };
}

/* ------------------------------------------------------------------------ */
/* Modal (native <dialog class="sf-modal">)                                  */
/* ------------------------------------------------------------------------ */

const backdropBound = new WeakSet();

function resolveDialog(target) {
    const el = typeof target === 'string'
        ? document.getElementById(target.replace(/^#/, ''))
        : target;
    if (!(el instanceof HTMLDialogElement)) {
        throw new TypeError(`Expected a <dialog> element or its id, got ${target}`);
    }
    return el;
}

function onBackdropClick(event) {
    const dialog = event.currentTarget;
    // Clicks on the ::backdrop are reported with the dialog itself as target.
    if (event.target !== dialog || dialog.hasAttribute('data-sf-static')) return;
    const r = dialog.getBoundingClientRect();
    const inside = event.clientX >= r.left && event.clientX <= r.right
                && event.clientY >= r.top && event.clientY <= r.bottom;
    if (!inside) dialog.close('dismiss');
}

/**
 * Open a <dialog class="sf-modal"> as a modal. Clicking the backdrop closes it
 * (returnValue "dismiss") unless the dialog has a data-sf-static attribute.
 *
 * @param {string|HTMLDialogElement} target  element or id
 * @returns {HTMLDialogElement}
 */
export function openModal(target) {
    const dialog = resolveDialog(target);
    if (!backdropBound.has(dialog)) {
        dialog.addEventListener('click', onBackdropClick);
        backdropBound.add(dialog);
    }
    if (!dialog.open) {
        dialog.returnValue = '';
        dialog.showModal();
    }
    return dialog;
}

/**
 * @param {string|HTMLDialogElement} target
 * @param {string} [returnValue]
 */
export function closeModal(target, returnValue) {
    const dialog = resolveDialog(target);
    if (dialog.open) dialog.close(returnValue);
}

/**
 * Open a modal and resolve with its returnValue once it closes. Buttons inside
 * <form method="dialog"> set the value: <button value="confirm">.
 * Esc resolves with "" and a backdrop click with "dismiss".
 *
 *   if (await confirmModal('confirm-delete') === 'confirm') { … }
 *
 * @param {string|HTMLDialogElement} target
 * @returns {Promise<string>}
 */
export function confirmModal(target) {
    const dialog = openModal(target);
    return new Promise((resolve) => {
        dialog.addEventListener('close', () => resolve(dialog.returnValue), { once: true });
    });
}

/* ------------------------------------------------------------------------ */
/* Toasts                                                                    */
/* ------------------------------------------------------------------------ */

const MAX_TOASTS = 4;
let toastRegion = null;

/**
 * Get (or create) the toast container. It is an aria-live region, so screen
 * readers announce new toasts. initKit() creates it up front, because some
 * screen readers ignore a live region that is created at the same moment as
 * its first message.
 *
 * @returns {HTMLElement}
 */
export function getToastRegion() {
    if (toastRegion?.isConnected) return toastRegion;
    toastRegion = document.querySelector('.sf-toast-region');
    if (!toastRegion) {
        toastRegion = document.createElement('div');
        toastRegion.className = 'sf-toast-region';
        toastRegion.setAttribute('aria-live', 'polite');
        document.body.append(toastRegion);
    }
    return toastRegion;
}

/**
 * Show a short, non-blocking message. The text is inserted with textContent,
 * so it is safe to pass API error messages straight in.
 *
 * @param {string} message
 * @param {{tone?: 'success'|'warning'|'danger'|'info', duration?: number}} [options]
 *        duration in ms; 0 keeps the toast until dismissed. Hovering pauses it.
 * @returns {{element: HTMLElement, dismiss: () => void}}
 */
export function toast(message, { tone = 'info', duration = 4000 } = {}) {
    const region = getToastRegion();

    const el = document.createElement('div');
    el.className = `sf-toast sf-toast-${tone}`;
    if (tone === 'danger') el.setAttribute('role', 'alert');

    const text = document.createElement('span');
    text.className = 'sf-toast-text';
    text.textContent = message;

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'sf-toast-close';
    close.setAttribute('aria-label', 'Dismiss notification');
    close.textContent = '×';

    el.append(text, close);

    let timer = 0;
    const start = () => { if (duration > 0) timer = setTimeout(dismiss, duration); };
    const stop = () => clearTimeout(timer);

    function dismiss() {
        stop();
        if (!el.isConnected || el.classList.contains('is-leaving')) return;
        el.classList.add('is-leaving');
        setTimeout(() => el.remove(), 200);
    }

    close.addEventListener('click', dismiss);
    el.addEventListener('mouseenter', stop);
    el.addEventListener('mouseleave', start);

    region.append(el);
    const live = region.querySelectorAll('.sf-toast:not(.is-leaving)');
    if (live.length > MAX_TOASTS) live[0].querySelector('.sf-toast-close')?.click();

    start();
    return { element: el, dismiss };
}

/* ------------------------------------------------------------------------ */
/* Busy buttons (prevents double submits)                                    */
/* ------------------------------------------------------------------------ */

const busyState = new WeakMap();

/**
 * Put a button into (or out of) a busy state: disabled, aria-busy, spinner.
 * Optionally swaps its label while busy, e.g. "Placing order…". Restoring
 * puts back the original label and the disabled state it had before.
 *
 * @param {HTMLButtonElement} button
 * @param {boolean} [busy=true]
 * @param {string} [busyLabel]
 */
export function setBusy(button, busy = true, busyLabel) {
    const saved = busyState.get(button);
    if (busy) {
        if (saved) return; // already busy
        busyState.set(button, {
            disabled: button.disabled,
            nodes: busyLabel ? [...button.childNodes] : null,
        });
        button.disabled = true;
        button.setAttribute('aria-busy', 'true');
        button.classList.add('is-loading');
        if (busyLabel) button.replaceChildren(document.createTextNode(busyLabel));
    } else {
        if (!saved) return; // not busy
        busyState.delete(button);
        button.disabled = saved.disabled;
        button.removeAttribute('aria-busy');
        button.classList.remove('is-loading');
        if (saved.nodes) button.replaceChildren(...saved.nodes);
    }
}

/**
 * Run an async task with the button busy, and always restore it afterwards,
 * even if the task throws.
 *
 *   await withBusy(submitBtn, () => api.placeOrder(cart), 'Placing order…');
 *
 * @template T
 * @param {HTMLButtonElement} button
 * @param {() => Promise<T>} task
 * @param {string} [busyLabel]
 * @returns {Promise<T>}
 */
export async function withBusy(button, task, busyLabel) {
    setBusy(button, true, busyLabel);
    try {
        return await task();
    } finally {
        setBusy(button, false);
    }
}

/* ------------------------------------------------------------------------ */
/* Mobile nav toggle                                                         */
/* ------------------------------------------------------------------------ */

/**
 * Wire a .sf-nav-toggle button to the menu named by its aria-controls.
 * Toggles .is-open + aria-expanded, closes on Esc and after a link is chosen.
 *
 * @param {HTMLElement} toggle
 * @returns {() => void} cleanup
 */
export function bindNavToggle(toggle) {
    const menu = document.getElementById(toggle.getAttribute('aria-controls') ?? '');
    if (!menu) return () => {};

    const isOpen = () => toggle.getAttribute('aria-expanded') === 'true';
    const set = (open) => {
        toggle.setAttribute('aria-expanded', String(open));
        menu.classList.toggle('is-open', open);
    };

    const onToggle = () => set(!isOpen());
    const onKey = (e) => {
        if (e.key === 'Escape' && isOpen()) { set(false); toggle.focus(); }
    };
    const onMenuClick = (e) => { if (e.target.closest('a')) set(false); };

    set(false);
    toggle.addEventListener('click', onToggle);
    document.addEventListener('keydown', onKey);
    menu.addEventListener('click', onMenuClick);

    return () => {
        toggle.removeEventListener('click', onToggle);
        document.removeEventListener('keydown', onKey);
        menu.removeEventListener('click', onMenuClick);
    };
}

/* ------------------------------------------------------------------------ */
/* Quantity stepper                                                          */
/* ------------------------------------------------------------------------ */

/**
 * Wire a .sf-qty stepper. Respects the input's min/max/step, clamps typed
 * values, disables −/+ at the limits and fires bubbling `input` + `change`
 * events so forms and frameworks see the new value.
 *
 * @param {HTMLElement} root  the .sf-qty element
 * @returns {() => void} cleanup
 */
export function bindQty(root) {
    const input = root.querySelector('input');
    if (!input) return () => {};
    const dec = root.querySelector('[data-sf-qty-step="-1"]');
    const inc = root.querySelector('[data-sf-qty-step="1"]');

    const min = () => (input.min === '' ? -Infinity : Number(input.min));
    const max = () => (input.max === '' ? Infinity : Number(input.max));

    const sync = () => {
        const v = Number(input.value);
        if (dec) dec.disabled = input.disabled || v <= min();
        if (inc) inc.disabled = input.disabled || v >= max();
    };

    const emit = () => {
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
    };

    const onClick = (e) => {
        const btn = e.target.closest('[data-sf-qty-step]');
        if (!btn || !root.contains(btn) || btn.disabled) return;
        if (btn.dataset.sfQtyStep === '1') input.stepUp(); else input.stepDown();
        sync();
        emit();
    };

    const onChange = () => {
        const v = Math.round(Number(input.value));
        const clamped = Number.isFinite(v) ? Math.min(max(), Math.max(min(), v)) : Math.max(min(), 1);
        if (String(clamped) !== input.value) input.value = String(clamped);
        sync();
    };

    root.addEventListener('click', onClick);
    input.addEventListener('change', onChange);
    input.addEventListener('input', sync);
    sync();

    return () => {
        root.removeEventListener('click', onClick);
        input.removeEventListener('change', onChange);
        input.removeEventListener('input', sync);
    };
}

/* ------------------------------------------------------------------------ */
/* Auto-wiring for plain HTML pages                                          */
/* ------------------------------------------------------------------------ */

/**
 * Wire up everything inside `root` that uses data-sf-* attributes:
 *   [data-sf-nav-toggle]           → bindNavToggle
 *   [data-sf-qty]                  → bindQty
 *   [data-sf-modal-open="<id>"]    → opens that <dialog> on click
 *
 * @param {Document|HTMLElement} [root=document]
 * @returns {() => void} cleanup
 */
export function initKit(root = document) {
    const cleanups = [];

    root.querySelectorAll('[data-sf-nav-toggle]').forEach((el) => cleanups.push(bindNavToggle(el)));
    root.querySelectorAll('[data-sf-qty]').forEach((el) => cleanups.push(bindQty(el)));

    const onClick = (e) => {
        const opener = e.target.closest?.('[data-sf-modal-open]');
        if (opener) openModal(opener.getAttribute('data-sf-modal-open'));
    };
    root.addEventListener('click', onClick);
    cleanups.push(() => root.removeEventListener('click', onClick));

    getToastRegion();

    return () => cleanups.forEach((fn) => fn());
}
