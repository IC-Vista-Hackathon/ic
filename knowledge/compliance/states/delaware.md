# Delaware — Payment Compliance Reference

Internal compliance reference only. Not legal advice — verify with counsel before relying on this for product/legal decisions.

## 1. Autopay/Recurring Payment Authorization — Card vs ACH Restrictions
- No Delaware-specific statute found restricting recurring/autopay funding to ACH-only or barring credit card as a funding method. **Not confirmed — verify with counsel.**
- Delaware's automatic renewal statute (6 Del. Code §§ 2731–2737, see §3) governs consent/disclosure regardless of funding rail; it does not restrict which payment method may be used to fund renewals.

## 2. Credit Card Surcharge / Convenience Fee Law
- **Delaware currently has no statute prohibiting or capping credit card surcharges.** Surcharging is permitted by default (subject to card-network rules and the federal Durbin Amendment framework), since no state law restricts it.
- Two legislative attempts to change this have **not** become law:
  - **HB 488** (2022) — would have banned surcharging outright (mirroring CA-style no-surcharge law). Stalled in committee; never enacted.
  - **SB 89** (2025, 153rd General Assembly) — would cap surcharges at the actual card-processing fee for transactions ≤ $1,500, and bar refusing card payment or surcharging for transactions > $1,500. **Status as of this research: still pending in Senate committee (not passed, not signed, no effective date).** Do not treat as current law — recheck bill status before relying on it.
- **Action item:** monitor SB 89's progress; if enacted, it would materially change DE surcharge practice for transaction-size-tiered merchants.

## 3. UDAP / Automatic Renewal / Negative-Option Statutes
- 6 Del. Code, Title 6, Chapter 27, Subchapter IV, §§ 2731–2737 — Delaware's automatic renewal / "evergreen clause" statute.
- § 2734 — sellers must disclose automatic renewal terms clearly and conspicuously at contract formation.
- Pre-renewal notice requirement applies specifically to contracts that renew for a period > 1 month **and** where the renewal extends the contract beyond 12 months from initiation: notice of each upcoming extension required no less than 30 and no more than 60 days before the cancellation deadline.
- Cancellation mechanism must be cost-effective, timely, and easy to use; consumers who entered a contract online must be able to cancel online (no forcing consumers offline to cancel).
- § 2731 provides the operative definitions (including the "automatic renewal provision" scope described above — note the 12-month/1-month thresholds narrow which contracts are covered relative to CA/CO/CT statutes).

## 4. Data Breach Notification — Payment Card Data
- 6 Del. Code Chapter 12B (§§ 12B-101 to 12B-104) — Computer Security Breaches.
- § 12B-101 — covered personal information includes account number, credit card number, or debit card number **combined with any required security code, access code, or password** permitting access to a resident's financial account.
- § 12B-102 — notification to affected DE residents required within **60 days** of determination of breach; if more than 500 DE residents affected, notice to the Delaware Attorney General required no later than the time notice is given to residents.
- Encryption safe harbor: notification not required for encrypted information unless the encryption key was also, or is reasonably believed to have been, acquired.
- § 12B-104 — enforcement solely by the Delaware Attorney General (no explicit private right of action in the statute); AG may bring action for appropriate damages/penalties and to recover direct economic damages from a violation.

## 5. State Law Referencing/Incorporating PCI-DSS
- No Delaware statute expressly names or incorporates PCI-DSS. § 12B-104 (general reasonable-security obligation under Chapter 12B) requires businesses that own/license/maintain personal information to implement reasonable procedures/practices to prevent unauthorized access, acquisition, use, modification, disclosure, or destruction — framework-agnostic (NIST, ISO, or PCI-DSS may all serve as evidence of reasonableness, but none is mandated by name).
- PCI-DSS compliance for DE merchants remains a card-network/acquirer contractual obligation, not a direct state statutory requirement.

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- No dedicated Delaware consumer EFT/ACH statute beyond federal EFTA/Reg E incorporation was identified in this pass. **Not confirmed — verify with counsel.**

## Sources
- [Merchant Cost Consulting — Delaware Credit Card Surcharge Laws (2026)](https://merchantcostconsulting.com/lower-credit-card-processing-fees/delaware-surcharge-laws/)
- [HB 488 — Delaware General Assembly Bill Detail](https://legis.delaware.gov/BillDetail/129825)
- [SB 89 — Delaware General Assembly Bill Detail](https://legis.delaware.gov/BillDetail/142049)
- [6 Del. Code § 2731 (2025) — Justia](https://law.justia.com/codes/delaware/title-6/chapter-27/subchapter-iv/section-2731/)
- [6 Del. Code § 2734 (2025) — Justia](https://law.justia.com/codes/delaware/title-6/chapter-27/subchapter-iv/section-2734/)
- [6 Del. Code § 12B-101 (2025) — Justia](https://law.justia.com/codes/delaware/title-6/chapter-12b/section-12b-101/)
- [6 Del. Code § 12B-102 (2025) — Justia](https://law.justia.com/codes/delaware/title-6/chapter-12b/section-12b-102/)
- [6 Del. Code Chapter 12B — Delaware Code Online](https://delcode.delaware.gov/title6/c012b/index.html)
