# North Carolina — Payments Compliance Reference

*Internal compliance reference only — not legal advice. Verify with counsel before acting.*

## 1. Autopay / Recurring Payment Funding Source Restrictions
- No NC statute found restricting recurring/autopay funding to ACH-only vs. credit card (unlike FL's utility/insurance ACH-only rule).
- General autorenewal disclosure statute (G.S. 75-41, below) applies regardless of funding method — no card-vs-ACH distinction in the text.
- Not confirmed — verify with counsel whether any NC agency rule (e.g., NC Utilities Commission for regulated utilities) imposes a funding-method restriction for autopay outside G.S. 75-41's exemptions.

## 2. Credit Card Surcharge / Convenience Fee Law
- Currently no NC statute prohibits credit card surcharging; debit/prepaid card surcharging is barred under federal card network rules applied nationwide, not a distinct NC statute.
- **Pending change**: House Bill 13 (2025-2026 session), "Charges for Credit, Charge & Debit Cards" — would cap surcharges at 2% of transaction, require point-of-sale/online/verbal disclosure, bar surcharging if merchant accepts only credit cards, civil penalty up to $500/violation. Proposed effective date 1/1/2026 — confirm current enactment status before relying on it.
- State government merchant-card convenience fee rules published by NC Office of the State Controller (OSC), applicable to state agency payment processing, not private merchants generally.

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal
- **G.S. 75-1.1** — general UDAP statute: bars "unfair methods of competition" and "unfair or deceptive acts or practices" in commerce. Treble damages + attorney's fees available (broadly used against billing/consumer-transaction misconduct). 4-year statute of limitations (G.S. 75-16.2).
- **G.S. 75-41** — Automatic Renewal Clauses:
  - Requires clear and conspicuous disclosure of: (1) the auto-renewal clause, (2) any terms changing on renewal, (3) cancellation method.
  - For terms exceeding 60 days: written notice required 15–45 days before renewal date.
  - Violation renders the auto-renewal clause void and unenforceable.
  - Exemptions: insurers (Ch. 58), banks/trust cos./thrifts/credit unions, and entities regulated by FCC or NC Utilities Commission.
- House Bill 188 (2025-2026 session) proposes further amendments to auto-renewal contract rules — check enactment status.

## 4. Data Breach Notification — Payment Card Data
- **N.C. Gen. Stat. § 75-65** (Identity Theft Protection Act, Ch. 75 Art. 2A): requires notice to affected NC residents "without unreasonable delay" after discovery of a security breach involving personal information.
- Credit/debit card numbers are covered "personal information" when combined with a resident's name.
- If notice goes to >1,000 persons at once, must also notify the NC AG Consumer Protection Division and nationwide consumer reporting agencies without unreasonable delay.
- Violation of the notification statute is enforced as a UDAP violation under G.S. § 75-1.1 by the Attorney General.

## 5. PCI-DSS References in State Law
- No NC statute directly mandates or references PCI-DSS compliance.
- Payment card data handling instead falls under the general Identity Theft Protection Act (G.S. § 75-60 et seq.) data security/breach provisions and UDAP (§75-1.1) — PCI-DSS itself remains a contractual/card-network obligation, not a state-law mandate.

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- **Chapter 25, Article 4A** — "Funds Transfers" (NC's enactment of UCC Article 4A) governs wholesale wire/funds transfers between financial institutions — not consumer EFT/Reg E territory.
- **Chapter 53, Article 16B** — additional funds-transfer provisions applicable to NC-chartered financial institutions.
- NCDOR EFT rules (17 NCAC 01C) govern ACH credit/debit for state tax remittance only — not general consumer payments.
- No NC-specific consumer EFT statute broader than federal Reg E was identified. Not confirmed — verify with counsel.

## Sources
- [NC House Bill 13 Summary](https://dashboard.ncleg.gov/api/Services/BillSummary/2025/H13-SMTM-61(CSTMf-16)-v-4)
- [NCSL — Credit or Debit Card Surcharges Statutes](https://www.ncsl.org/financial-services/credit-or-debit-card-surcharges-statutes)
- [NC OSC — Convenience Fee/Surcharge Rules](https://www.osc.nc.gov/documents/ecommerce/merchant-cards/conveniencefeesurchargerules/open)
- [G.S. 75-41 — Automatic Renewal Clauses (PDF)](https://www.ncleg.net/EnactedLegislation/Statutes/PDF/BySection/Chapter_75/GS_75-41.pdf)
- [NC House Bill 188 (2025)](https://www.ncleg.gov/Sessions/2025/Bills/House/PDF/H188v1.pdf)
- [G.S. 75-65 — Protection from Security Breaches](https://www.ncleg.net/enactedlegislation/statutes/html/bysection/chapter_75/gs_75-65.html)
- [G.S. 75-1.1 — Methods of Competition, Acts and Practices Regulated (PDF)](https://www.ncleg.gov/EnactedLegislation/Statutes/PDF/BySection/Chapter_75/GS_75-1.1.pdf)
- [NC General Statutes Chapter 25, Article 4A — Funds Transfers (PDF)](https://www.ncleg.gov/EnactedLegislation/Statutes/PDF/ByArticle/Chapter_25/Article_4A.pdf)
- [NC General Statutes Chapter 53, Article 16B (PDF)](https://www.ncleg.net/EnactedLegislation/Statutes/PDF/ByArticle/Chapter_53/Article_16B.pdf)
- [NCDOR — Electronic Funds Transfer](https://www.ncdor.gov/file-pay/electronic-funds-transfer)
