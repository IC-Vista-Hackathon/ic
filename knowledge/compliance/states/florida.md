# Florida — Payment Compliance Reference

*Internal reference only — not legal advice. Verify with counsel before relying on any point below.*

## 1. Autopay/Recurring Payment Funding Method (Credit Card vs ACH)

- **No blanket statutory ban on credit-card-funded autopay found.** Searched Fla. Stat. Title XXXVII (Insurance) Ch. 627 Part XV (Premium Finance Companies, ss. 627.826–627.849) and related sections; found no statute restricting premium finance or recurring insurance-premium payments to ACH/EFT only.
- Fla. Stat. § 627.7295 (motor vehicle insurance contracts) explicitly contemplates **both** "automatic electronic funds transfer payment plan" and "recurring credit card or debit card agreement" as acceptable recurring-payment methods for waiving certain minimum-down-payment/binder rules — i.e., credit card is treated as a valid recurring funding method here, not excluded.
- Fla. Stat. § 627.848 (premium finance agreement cancellation-on-default) contains no payment-method restriction — covers only cancellation notice mechanics.
- Practical (non-statutory) friction exists: **Citizens Property Insurance Corporation** (state-created insurer) does not accept credit cards for its multi-installment recurring payment plans and states it is "prohibited from passing card company fees back to the cardholder," pushing recurring payments toward ACH/bank draft. This appears to be a no-surcharge-passthrough practice/policy, not a generally-applicable statute banning CC autopay for insurance statewide.
- **Not confirmed — verify with counsel:** whether any FL Office of Insurance Regulation rule, PSC rule, or narrower premium-finance provision (e.g., specific line-of-business bulletins) mandates ACH-only for a specific bill type. Nothing found in Ch. 627 Part XV or general statutes search confirms a blanket credit-card exclusion; do not assume one exists without further targeted research (e.g., OIR bulletins, Fla. Admin. Code 69O-136).

## 2. Credit Card Surcharge / Convenience Fee Law

- Fla. Stat. § 501.0117 prohibits sellers/lessors from imposing a surcharge for credit card use (vs. cash/check), with exceptions (tariffed charges, certain tuition convenience fees, cash-discount framing).
- **Status: statute held unconstitutional.** The Eleventh Circuit (*Dana's R.R. Supply v. Attorney General, Florida*, 2015) struck down § 501.0117 as an unconstitutional restriction on speech (regulating how price differences are labeled). The statute remains on the books but is not enforceable as written per that ruling — verify current enforcement posture with counsel, as this is a fast-moving area nationally.

## 3. UDAP / Recurring Billing / Auto-Renewal

- **FDUTPA** — Fla. Stat. § 501.204: declares "unfair methods of competition, unconscionable acts or practices, and unfair or deceptive acts or practices" unlawful; construed with deference to FTC Act §5 interpretations.
- **Auto-renewal statute** — Fla. Stat. § 501.165 (Automatic renewal of service contracts):
  - Applies to renewal provisions extending a service contract >1 month where total term exceeds 6 months.
  - Requires clear/conspicuous disclosure of the auto-renewal term.
  - For contracts ≥12 months with renewal periods >1 month: written/electronic renewal notice required 30–60 days before the cancellation deadline.
  - Cancellation must be permitted "in the same manner, and by the same means" the consumer used to accept.
  - Violation renders the auto-renewal provision void/unenforceable.

## 4. Data Breach Notification (Payment Card Data)

- **Florida Information Protection Act (FIPA)** — Fla. Stat. § 501.171 (effective 2014).
- Covers unauthorized access to unencrypted "personal information," which includes payment/financial account numbers in combination with security code/access code/password.
- Notice to affected individuals: "as expeditiously as practicable," no later than **30 days** after determination of breach.
- Civil penalties: from $1,000/day, escalating; can reach up to $500,000 per breach if notice is withheld >180 days.
- Enforced by the Florida Attorney General; no private right of action.

## 5. PCI-DSS Statutory References

- No standalone Florida statute found that expressly mandates PCI-DSS compliance by name. PCI-DSS is a card-network/contractual requirement, not a codified Florida legal obligation, based on research performed.
- **Not confirmed — verify with counsel:** whether any FL agency rule (e.g., DFS/OIR) or contractual state-payment-processor requirement cross-references PCI-DSS.

## 6. State-Specific EFT/ACH Statutes (Beyond Reg E)

- No Florida-specific consumer EFT/ACH statute beyond federal Reg E/EFTA was identified in this research pass. Fla. Admin. Code Ch. 61-15 governs EFT for state agency payments (state-as-payor context), not a consumer-protection EFT statute.

## Sources

- [Fla. Stat. 627.7295 (2020), The Florida Senate](https://www.flsenate.gov/laws/statutes/2020/627.7295)
- [Fla. Stat. 627.848 (2024), Online Sunshine](https://www.leg.state.fl.us/statutes/index.cfm?App_mode=Display_Statute&URL=0600-0699/0627/Sections/0627.848.html)
- [Fla. Stat. Ch. 627 Part XV (Premium Finance Companies), Justia](https://law.justia.com/codes/florida/title-xxxvii/chapter-627/part-xv/)
- [Fla. Stat. 501.0117, Online Sunshine](https://www.leg.state.fl.us/statutes/index.cfm?App_mode=Display_Statute&URL=0500-0599%2F0501%2FSections%2F0501.0117.html)
- [Florida's Credit Card Surcharge Prohibition Found Unconstitutional — Buchanan Ingersoll & Rooney](https://www.bipc.com/floridas-credit-card-surcharge-prohibition-found-unconstitutional)
- [Fla. Stat. 501.165, Online Sunshine](https://www.leg.state.fl.us/statutes/index.cfm?App_mode=Display_Statute&URL=0500-0599%2F0501%2FSections%2F0501.165.html)
- [Fla. Stat. 501.204 discussion — Florida Bar Journal](https://www.floridabar.org/the-florida-bar-journal/per-se-violations-of-the-florida-deceptive-and-unfair-trade-practices-act/)
- [Fla. Stat. 501.171 (FIPA), Online Sunshine](https://www.leg.state.fl.us/statutes/index.cfm?App_mode=Display_Statute&URL=0500-0599%2F0501%2FSections%2F0501.171.html)
- [Florida — Summary of U.S. State Data Breach Notification Statutes, Davis Wright Tremaine](https://www.dwt.com/gcp/states/florida)
- [Citizens Property Insurance Corporation — Payments](https://www.citizensfla.com/payments)
- [Citizens Property Insurance Corporation — Residential Payment Plans](https://www.citizensfla.com/-/residential-payment-plans)
