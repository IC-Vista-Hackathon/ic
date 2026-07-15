# Washington Payment Compliance — Internal Reference

Not legal advice. Verify with counsel before relying on any point below.

## 1. Autopay/Recurring Payment Authorization — Credit Card vs ACH
- No Washington-specific restriction found requiring recurring/autopay to be funded via ACH/bank debit only (no FL-style utility/insurance carve-out identified).
- Not confirmed — verify with counsel if targeting a specific regulated vertical in WA.

## 2. Credit Card Surcharge / Convenience Fee Law
- **RCW 19.200.010** (per secondary sources) requires surcharge notice at point of entry, point of sale, and on receipts; notice must state exact percentage/dollar amount and that it applies only to credit card transactions.
- Surcharge may not exceed merchant's actual cost of card acceptance; secondary sources describe a ~4% cap tied to average discount rate (confirm exact statutory cap language directly against RCW 19.200 before relying on it — primary text not independently verified in this pass).
- Surcharges limited to credit cards; debit/prepaid card surcharges prohibited (card network universal ban).
- Enforcement/failure to disclose treated as a deceptive practice under the Washington Consumer Protection Act, RCW 19.86 (AG Consumer Protection Division authority).
- Related public-sector fee statutes: RCW 36.29.190 (counties), RCW 87.03.277 (irrigation districts), RCW 19.158.100.
- Not confirmed — pull full RCW 19.200 text directly for exact cap/disclosure language before using in a customer-facing policy.

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal
- Consumer Protection Act, **RCW 19.86**; core prohibition **RCW 19.86.020** (unfair methods of competition/unfair or deceptive acts or practices unlawful).
- Remedies: **RCW 19.86.090** — private civil action, actual damages, treble damages up to $25,000; civil penalty up to $7,500 per violation.
- Automatic renewal / continuous service legislation: WA has enacted automatic-renewal disclosure/notice requirements (see SB 5507/SB 6437/HB 1441 legislative history) requiring either signed disclosure form or conspicuous in-contract disclosure with customer initials, plus written renewal notice 15–60 days before the decline-renewal deadline for contracts with initial term >1 year; failure renders the auto-renewal provision unenforceable.
- **Not confirmed — exact codified RCW chapter/section number for this automatic-renewal law was not conclusively verified in this pass** (one secondary source references RCW 19.345); pull the enacted chapter directly from apps.leg.wa.gov before citing a specific section to a client or in a compliance filing.

## 4. Data Breach Notification — Payment Card Data
- **Chapter 19.255 RCW**, "Personal Information — Notice of Security Breaches."
- **RCW 19.255.010**: general breach notice/definitions/rights and remedies.
- **RCW 19.255.020**: Liability of processors, businesses, and vendors — see Item 5 below (this is the PCI-adjacent liability provision, distinct from the general consumer notice duty in .010).

## 5. State Law Referencing/Incorporating PCI-DSS
- **RCW 19.255.020** — confirmed liability provision (title: "Liability of processors, businesses, and vendors"):
  - If a "processor" or "business" fails to take reasonable care to guard against unauthorized access to account information in its possession/control, and that failure is the proximate cause of a breach, the processor/business is **liable to a financial institution** for reimbursement of reasonable actual costs of reissuing credit/debit cards to WA-resident cardholders to mitigate current/future damages — even without physical injury from the breach.
  - "Business" = entity that processes more than **6 million** credit/debit card transactions annually and provides/sells goods or services to WA residents.
  - "Processor" = entity other than a "business" that directly processes/transmits account information for/on behalf of another as part of a payment processing service.
  - Remedies are cumulative with other legal rights/remedies; damages may be reduced by amounts the financial institution recovers from a card company for reissuance costs.
  - The statute's operative standard is "reasonable care to guard against unauthorized access" — it does not use the literal phrase "PCI-DSS" in the text as summarized by secondary sources reviewed; **confirm exact statutory text (whether PCI-DSS compliance is an explicit safe harbor/defense) directly against the RCW before representing this as a PCI-DSS safe-harbor statute** — some other states (e.g., Nevada, Minnesota) have explicit PCI-DSS safe-harbor language and WA may be conflated with those in commentary.

## 6. State EFT/ACH-Specific Statutes Beyond Reg E
- No standalone WA consumer EFT/ACH statute beyond federal Reg E identified in this pass.
- Not confirmed — verify with counsel.

## Sources
- [RCW 19.255.020 — Washington State Legislature](https://app.leg.wa.gov/rcw/default.aspx?cite=19.255.020)
- [Chapter 19.255 RCW — Washington State Legislature](https://app.leg.wa.gov/rcw/default.aspx?cite=19.255)
- [RCW 19.255.020 — Justia](https://law.justia.com/codes/washington/title-19/chapter-19-255/section-19-255-020/)
- [Washington State Credit Card Surcharge Law — LegalClarity](https://legalclarity.org/washington-state-credit-card-surcharge-law-what-businesses-must-know/)
- [RCW 36.29.190 — Justia](https://law.justia.com/codes/washington/title-36/chapter-36-29/section-36-29-190/)
- [Washington State Credit Card Surcharge Laws — Merchant Cost Consulting](https://merchantcostconsulting.com/lower-credit-card-processing-fees/washington-surcharge-laws/)
- [Chapter 19.86 RCW — Washington State Legislature](https://app.leg.wa.gov/rcw/default.aspx?cite=19.86)
- [RCW 19.86.020 — Washington State Legislature](https://app.leg.wa.gov/rcw/default.aspx?cite=19.86.020)
- [RCW 19.86.090 — Washington State Legislature](https://app.leg.wa.gov/rcw/default.aspx?cite=19.86.090)
- [AN ACT Relating to the use of automatic renewal provisions — HB 1441 text, WA Legislature](https://lawfilesext.leg.wa.gov/biennium/2023-24/Pdf/Bills/House%20Bills/1441.pdf)
- [SB 5507 — Washington State Legislature](https://app.leg.wa.gov/billsummary?BillNumber=5507&Year=2017)
