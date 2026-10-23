// Type declarations for storefront.js, so it can be imported from strict
// TypeScript (Angular) without `allowJs`.

export type Tone = 'success' | 'warning' | 'danger' | 'info';

export type OrderStatus =
    | 'Pending'
    | 'Paid'
    | 'PaymentFailed'
    | 'PaymentRetrying'
    | 'PaymentUnavailable'
    | 'PaymentUnknown'
    | 'NeedsAttention';

export interface StatusMeta {
    readonly label: string;
    /** Chart colour, a CSS value such as `var(--sf-status-paid)`. */
    readonly color: string;
    readonly tone: Tone;
    readonly message: string;
}

export declare function formatMoney(minorUnits: number, currency?: string, locale?: string): string;

export declare const ORDER_STATUS: Readonly<Record<OrderStatus, StatusMeta>>;
export declare function orderStatus(status: OrderStatus | (string & {})): StatusMeta;

/** Chart colour of a status; neutral grey for an unknown one. */
export declare function orderStatusColor(status: OrderStatus | (string & {})): string;

export declare function openModal(target: string | HTMLDialogElement): HTMLDialogElement;
export declare function closeModal(target: string | HTMLDialogElement, returnValue?: string): void;
export declare function confirmModal(target: string | HTMLDialogElement): Promise<string>;

export interface ToastOptions {
    tone?: Tone;
    /** Milliseconds before auto-dismiss; 0 keeps it until dismissed. Default 4000. */
    duration?: number;
}
export interface ToastHandle {
    element: HTMLElement;
    dismiss(): void;
}
export declare function getToastRegion(): HTMLElement;
export declare function toast(message: string, options?: ToastOptions): ToastHandle;

export declare function setBusy(button: HTMLButtonElement, busy?: boolean, busyLabel?: string): void;
export declare function withBusy<T>(button: HTMLButtonElement, task: () => Promise<T>, busyLabel?: string): Promise<T>;

export declare function bindNavToggle(toggle: HTMLElement): () => void;
export declare function bindQty(root: HTMLElement): () => void;
export declare function initKit(root?: Document | HTMLElement): () => void;
