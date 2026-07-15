# Nevada — Payments Compliance Reference

*Internal compliance reference only — not legal advice. Verify with counsel before relying on any point below.*

## 1. Autopay/Recurring Payment Funding Source (Credit Card vs. ACH)
- No Nevada-specific statute identified restricting recurring/autopay funding source to ACH/bank debit for utilities/insurance (no FL-style carve-out confirmed).
- **NRS 97A.210** (Ch. 97A — Debt Evidenced by Credit Card) — card issuers may not restrict/prohibit merchants from offering a cash discount (relevant to surcharge/discount structuring, not a funding-source mandate).
- **Not confirmed — verify with counsel.**

## 2. Credit Card Surcharge / Convenience Fee Law
- No general statutory prohibition on merchant credit card surcharges; subject to federal/card-network caps (federal ceiling 4%; NV guidance flags scrutiny above ~1.5%, consistent with card-network limits, not a hard statutory cap).
- **NRS 97A.210** — issuers cannot bar cash-discount programs.
- Government/regulated-entity convenience fees: **NRS 622.233** (regulatory bodies), **NRS 706.88355** (motor carriers — contracts with issuers; regulated max convenience fee; certain issuer acts prohibited), **NRS 354.770** (local government), **NRS 1.113** (courts). These cap government convenience fees to no more than the fee the government body itself is charged by the card issuer/processor.

## 3. UDAP / Autorenewal / Negative-Option
- **Nevada Deceptive Trade Practices Act (NDTPA)** — NRS Chapter 598 (definitions at NRS 598.0915; enforcement via AG, civil penalties up to $2,500/willful violation, fee-shifting to prevailing claimant).
- **Automatic renewal contracts** — NRS 598.0903–598.0999 (esp. **598.0923**, per secondary source; **verify exact subsection with counsel**): it is a deceptive/unfair trade practice to (a) fail to disclose automatic-renewal contract terms clearly, (b) renew on other-than-month-to-month terms without consumer consent, or (c) renew/materially change terms without prior notice and a clear cancellation explanation.
- Narrower sector-specific auto-renewal ban: **NRS 598.940–598.966** — health club/dance studio contracts must state a fixed term and **may not include automatic renewal**; 3-business-day cancellation right after receiving the contract copy.

## 4. Data Breach Notification — Payment Card Data
- **NRS Chapter 603A** ("Security and Privacy of Personal Information").
- **NRS 603A.220** — breach disclosure to affected NV residents required "in the most expedient time possible and without unreasonable delay" following discovery.
- "Personal information" (NRS 603A) includes name + SSN, driver's license/ID/driver-authorization-card number, or **account/credit/debit card number combined with a required security code**, or medical/health-insurance ID number — unencrypted.
- Enforcement: AG, under the Deceptive Trade Practices Act (Ch. 598), for both notification failures and security-measure violations.

## 5. PCI-DSS Reference in Statute — CONFIRMED
- **NRS 603A.215** ("Security measures for data collector that accepts payment card; use of encryption; liability for damages; applicability") — **confirmed, Nevada is the first state to codify a PCI-DSS mandate.**
- Exact operative text (per Justia-hosted statute text): *"If a data collector doing business in this State accepts a payment card in connection with a sale of goods or services, the data collector shall comply with the current version of the Payment Card Industry (PCI) Data Security Standard..."* — compliance timing keyed to the PCI Security Standards Council's effective dates.
- **Safe harbor / liability shield**: *"A data collector shall not be liable for damages for a breach of the security of the system data if: (a) The data collector is in compliance with this section; and (b) The breach is not caused by the gross negligence or intentional misconduct of the data collector, its officers, employees or agents."*
- Practical implication for InvoiceCloud: NV-processed card transactions should map directly to this statute in any NV-specific compliance narrative — PCI-DSS compliance is a **codified legal requirement** in Nevada, not merely a contractual/card-brand obligation as in most other states.

## 6. State-Specific EFT/ACH Statutes
- No standalone Nevada consumer EFT/ACH statute beyond government/regulatory-body payment-acceptance provisions (NRS 354.770, 622.233, 360.092, 706.88355) and federal EFTA/Reg E baseline.
- **NRS Chapter 97A** governs credit-card-evidenced debt generally but is not an ACH-specific statute.

## Sources
- [NRS 603A.215 (2025)](https://law.justia.com/codes/nevada/chapter-603a/statute-603a-215/)
- [NRS 603A.220 (2025)](https://law.justia.com/codes/nevada/chapter-603a/statute-603a-220/)
- [NRS Chapter 603A — full text](https://www.leg.state.nv.us/nrs/nrs-603a.html)
- [NRS Chapter 598 — Deceptive Trade Practices](https://www.leg.state.nv.us/nrs/NRS-598.html)
- [NRS 598.0955 — Applicability](https://law.justia.com/codes/nevada/chapter-598/statute-598-0955/)
- [NRS 598.0915 — "Deceptive trade practice" defined](https://law.justia.com/codes/nevada/chapter-598/statute-598-0915/)
- [NRS 622.233](https://law.justia.com/codes/nevada/chapter-622/statute-622-233/)
- [NRS 706.88355](https://law.justia.com/codes/nevada/chapter-706/statute-706-88355/)
- [Demystifying NRS 598.0923 — Bourassa Law Group](https://www.blgwins.com/how-nevada-protects-consumers-from-deceptive-practices/)
- [InfoLawGroup — Nevada PCI/encryption law analysis](https://www.infolawgroup.com/insights/2010/03/articles/encryption/a-closer-look-at-the-pci-compliance-and-encryption-requirements-of-nevadas-security-of-personal-information-law)
