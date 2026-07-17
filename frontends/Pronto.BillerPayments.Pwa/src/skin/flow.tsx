import type {
  BatchReviewProps,
  CartProps,
  InvoiceSelectListProps,
  PaymentPlanChooserProps,
  SelectableInvoice,
} from './contract';

// AI-EDITABLE SKIN FLOW STRUCTURE (feature F3).
// Presentational only. No fetch, no payment logic, no fee/total math — the stable core
// (App.tsx / provider.ts) computes every money value and hands these components fully
// preformatted view models plus callbacks. Keep the exported component names and their
// prop contracts (contract.ts) intact; the core composes these and the build/typecheck
// gate enforces the signatures. Restyle and restructure freely; never move money.

// The selectable list of open invoices a payer can add to / remove from the batch.
export function InvoiceSelectList({ heading, invoices, onToggle, onSelectAll, onClearAll, allSelected }: InvoiceSelectListProps) {
  return (
    <section className="card" aria-label={heading} data-testid="invoice-select">
      <div className="select-head">
        <h2>{heading}</h2>
        <button type="button" className="link" data-testid={allSelected ? 'clear-all' : 'select-all'} onClick={allSelected ? onClearAll : onSelectAll}>
          {allSelected ? 'Clear all' : 'Select all'}
        </button>
      </div>
      <ul className="invoice-list" role="list">
        {invoices.map(invoice => (
          <li key={invoice.id}>
            <label className="check invoice-option" data-testid={`invoice-option-${invoice.id}`}>
              <input
                type="checkbox"
                checked={invoice.selected}
                onChange={() => onToggle(invoice.id)}
                aria-label={`Pay ${invoice.typeLabel ? `${invoice.typeLabel} — ` : ''}${invoice.description}, ${invoice.amountLabel}`}
              />
              <span className="invoice-body">
                <span className="invoice-title">
                  {invoice.typeLabel && <span className="bill-type" data-testid="bill-type">{invoice.typeLabel}</span>}
                  <strong>{invoice.description}</strong>
                </span>
                <small>Due {invoice.dueDateLabel}</small>
                {invoice.note && <small className={invoice.noteEmphasis ? 'bill-note-strong' : 'bill-note'}>{invoice.note}</small>}
              </span>
              <span className="invoice-right">
                {invoice.statusColor && (
                  <span className={`status-dot status-${invoice.statusColor}`} title={invoice.statusLabel} aria-label={invoice.statusLabel} />
                )}
                <strong>{invoice.amountLabel}</strong>
              </span>
            </label>
          </li>
        ))}
      </ul>
    </section>
  );
}

// The running cart summary: line items + subtotal / fee / total (all preformatted).
export function Cart({ summary, onRemove, emptyText }: CartProps) {
  if (summary.count === 0) {
    return <section className="card cart" aria-label="Your cart" data-testid="cart"><p className="card-copy">{emptyText}</p></section>;
  }
  return (
    <section className="card cart" aria-label="Your cart" data-testid="cart">
      <h2>Your cart ({summary.count})</h2>
      <ul className="cart-lines" role="list">
        {summary.lines.map(line => (
          <li className="cart-line" key={line.id} data-testid={`cart-line-${line.id}`}>
            <span>
              {line.typeLabel && <span className="bill-type">{line.typeLabel}</span>}
              {line.label}
            </span>
            <span className="cart-line-right">
              <strong>{line.amountLabel}</strong>
              {onRemove && (
                <button type="button" className="link" data-testid={`cart-remove-${line.id}`} onClick={() => onRemove(line.id)} aria-label={`Remove ${line.label} from cart`}>
                  Remove
                </button>
              )}
            </span>
          </li>
        ))}
      </ul>
      <dl>
        <div><dt>Subtotal</dt><dd data-testid="cart-subtotal">{summary.subtotalLabel}</dd></div>
        <div><dt>Service fee</dt><dd data-testid="cart-fee">{summary.feeLabel ?? 'Calculated at checkout'}</dd></div>
        <div className="total"><dt>Total</dt><dd data-testid="cart-total">{summary.totalLabel}</dd></div>
      </dl>
    </section>
  );
}

// The batch review: one line per invoice with amount/fee/total and, during and after
// settlement, a per-invoice status. Purely presentational — the core owns settlement.
export function BatchReview({ heading, lines, totalLabel, consentText }: BatchReviewProps) {
  return (
    <section className="card" aria-label={heading} data-testid="batch-review">
      <h2>{heading}</h2>
      <ul className="batch-lines" role="list">
        {lines.map(line => (
          <li className={`batch-line batch-line-${line.status}`} key={line.id} data-testid={`batch-line-${line.id}`}>
            <span className="batch-line-desc">
              {line.typeLabel && <span className="bill-type">{line.typeLabel}</span>}
              <strong>{line.label}</strong>
              {line.status !== 'pending' && (
                <small className="batch-line-status" data-testid={`batch-status-${line.id}`}>
                  {line.status === 'paid' ? 'Paid' : 'Not charged'}{line.statusMessage ? ` — ${line.statusMessage}` : ''}
                </small>
              )}
            </span>
            <span className="batch-line-amounts">
              <small>{line.amountLabel} · {line.feeLabel}</small>
              <strong>{line.totalLabel}</strong>
            </span>
          </li>
        ))}
      </ul>
      <dl>
        <div className="total"><dt>Total</dt><dd data-testid="batch-total">{totalLabel}</dd></div>
      </dl>
      <p className="consent">{consentText}</p>
    </section>
  );
}

// Amount-entry + installment-plan chooser (feature F4). Lets an eligible biller's payer
// choose to pay a partial amount or enroll in an installment plan instead of the full
// balance. Presentational only: the core owns eligibility, parsing, validation and every
// money label; this renders the controls and calls the provided callbacks.
export function PaymentPlanChooser({
  allowPartial,
  allowInstallments,
  mode,
  onModeChange,
  fullLabel,
  amountValue,
  onAmountChange,
  amountHint,
  amountError,
  installmentOptions,
  selectedInstallmentCount,
  onInstallmentCountChange,
}: PaymentPlanChooserProps) {
  if (!allowPartial && !allowInstallments) return null;
  return (
    <fieldset className="plan-chooser" data-testid="plan-chooser">
      <legend>How much would you like to pay?</legend>
      <label className="check">
        <input type="radio" name="plan-mode" data-testid="plan-mode-full" checked={mode === 'full'} onChange={() => onModeChange('full')} />
        <span><strong>{fullLabel}</strong></span>
      </label>

      {allowPartial && (
        <label className="check">
          <input type="radio" name="plan-mode" data-testid="plan-mode-partial" checked={mode === 'partial'} onChange={() => onModeChange('partial')} />
          <span><strong>Pay a different amount</strong><small>{amountHint}</small></span>
        </label>
      )}
      {allowPartial && mode === 'partial' && (
        <div className="plan-amount">
          <label>Amount
            <input
              type="text"
              inputMode="decimal"
              data-testid="partial-amount-input"
              value={amountValue}
              onChange={event => onAmountChange(event.target.value)}
              aria-invalid={amountError ? true : undefined}
              aria-describedby={amountError ? 'plan-amount-error' : undefined}
            />
          </label>
          {amountError && <small id="plan-amount-error" className="bill-note-strong" data-testid="plan-amount-error" role="alert">{amountError}</small>}
        </div>
      )}

      {allowInstallments && (
        <label className="check">
          <input type="radio" name="plan-mode" data-testid="plan-mode-installment" checked={mode === 'installment'} onChange={() => onModeChange('installment')} />
          <span><strong>Split into installments</strong><small>Spread the balance across scheduled payments.</small></span>
        </label>
      )}
      {allowInstallments && mode === 'installment' && (
        <ul className="installment-options" role="list">
          {installmentOptions.map(option => (
            <li key={option.count}>
              <label className="check">
                <input
                  type="radio"
                  name="installment-count"
                  data-testid={`installment-option-${option.count}`}
                  checked={selectedInstallmentCount === option.count}
                  onChange={() => onInstallmentCountChange(option.count)}
                />
                <span>{option.label}</span>
              </label>
            </li>
          ))}
        </ul>
      )}
    </fieldset>
  );
}

export type { SelectableInvoice };
