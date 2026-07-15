# Wisconsin — Payment Compliance Reference

Not legal advice. Internal compliance reference only — verify with counsel before acting.

## 1. Autopay/Recurring Payment — Credit Card vs ACH Restrictions
- No Wisconsin statute found restricting funding source (credit card vs ACH/bank debit) for recurring/autopay billing generally.
- Wis. Stat. § 425.105 and § 422.209(4)/422.413 (Wisconsin Consumer Act) govern credit transactions/collection practices but do not impose a card-vs-ACH funding restriction.
- Wis. Stat. § 85.14 addresses DOT electronic payment mechanisms (state agency-specific, not a general recurring-billing restriction).
- No FL-style ACH-only mandate identified for utility/insurance autopay. **Not confirmed — verify with counsel.**

## 2. Credit Card Surcharge / Convenience Fee Laws
- Surcharging is legal in Wisconsin; no state statute prohibits merchant surcharges (unlike some other states).
- 2013 SB 213 would have banned surcharging — did not pass.
- Federal rules apply (surcharge capped effectively ~4% under card brand/federal frameworks; card networks like Visa cap at 3%); debit card surcharging remains prohibited nationwide.
- Disclosure requirements apply: surcharge must be disclosed at point of entry/homepage and checkout, disclosed before transaction completion, itemized on receipt, and receipt must state surcharge doesn't apply to debit and doesn't exceed actual processing cost.
- Wis. Stat. § 423.401 (Wisconsin Consumer Act) — referenced in surcharge guidance; review directly for consumer-transaction-specific surcharge conditions.
- State agency convenience fees governed separately by Wisconsin DOA policy (doa.wi.gov).

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal
- **Wis. Stat. § 100.18** — Fraudulent Representations (Deceptive Trade Practices Act/WDTPA): broad prohibition on untrue/deceptive/misleading advertising or sales representations. Elements: (1) representation to public to induce obligation, (2) untrue/deceptive/misleading, (3) causes pecuniary loss.
- **Wis. Stat. § 100.195** — dedicated negative-option/continuing-services statute (distinct from § 100.18). **Verify exact text/requirements with counsel** — not independently fetched.
- **Wis. Stat. § 134.49** — Automatic renewal notice law for *business* contracts (B2B, not consumer): requires notice of auto-renewal/extension provisions for contracts with initial term >1 year, auto-renewing for terms >1 year. Notice must be given 15–60 days before renewal deadline. Non-compliance: contract unenforceable at renewal, contract terminates at end of current term; damages up to 2x actual damages + attorney fees.
- **AB417** (proposed, session 2023) — would create Wis. Stat. § 134.495 for *consumer* contract automatic renewals, requiring clear/conspicuous renewal-offer terms retainable by consumer. Status: proposed, not confirmed enacted — **verify current status with counsel**.

## 4. Data Breach Notification — Payment Card Data
- **Wis. Stat. § 134.98** — Wisconsin data breach notification law.
- Covers "personal information" including financial account number (credit/debit card account number) plus any security code, access code, or password permitting account access.
- Notification to affected individuals required within a reasonable time, not to exceed 45 days after entity learns of unauthorized acquisition.
- Breaches affecting >1,000 individuals also require notice to nationwide consumer reporting agencies without unreasonable delay.
- Enforced by Wisconsin Attorney General; civil penalties available.
- Related: Wis. Stat. § 601.954(2)(e) (insurance-sector specific provision) — **verify scope with counsel**.

## 5. PCI-DSS References in Statute
- No Wisconsin statute found that mandates or directly incorporates PCI-DSS compliance.
- § 134.98 requires reasonable security generally but does not name PCI-DSS as a required framework.
- **Not confirmed — verify with counsel** whether any DATCP rule or insurance-sector regulation (Ch. 601) references PCI-DSS.

## 6. EFT/ACH-Specific Statutes Beyond Federal Reg E
- No Wisconsin-specific EFT/ACH consumer-protection statute beyond federal Reg E identified in this pass.
- Wisconsin Consumer Act (Chs. 421–427) governs consumer credit transactions broadly; may have EFT-adjacent provisions in Ch. 422/423 — **not independently confirmed, verify with counsel**.

## Sources
- https://docs.legis.wisconsin.gov/document/statutes/423.401
- https://docs.legis.wisconsin.gov/statutes/statutes/425/I/105?view=section
- https://docs.legis.wisconsin.gov/document/statutes/134.98 (via https://docs.legis.wisconsin.gov/statutes/statutes/134/98)
- https://docs.legis.wisconsin.gov/document/statutes/601.954(2)(e)
- https://docs.legis.wisconsin.gov/statutes/statutes/100/18
- https://docs.legis.wisconsin.gov/document/statutes/134.49 (referenced via Quarles/DeWitt summaries)
- https://docs.legis.wisconsin.gov/2023/related/proposals/ab417
- https://docs.legis.wisconsin.gov/statutes/statutes/421/iii/301/30/c
- https://docs.legis.wisconsin.gov/statutes/statutes/422/ii/209/4
- https://docs.legis.wisconsin.gov/statutes/statutes/422/IV/413
- https://law.justia.com/codes/wisconsin/chapter-85/section-85-14/
- https://doa.wi.gov/Pages/StateFinances/Convenience-Fees-.aspx
- https://datcp.wi.gov/Documents/IDTheftDataBreach607.pdf
- https://www.quarles.com/newsroom/publications/wisconsins-automatic-renewal-law
- https://www.wisbar.org/NewsPublications/InsideTrack/Pages/Article.aspx?Volume=4&Issue=3&ArticleID=7989
- https://merchantcostconsulting.com/lower-credit-card-processing-fees/wisconsin-credit-card-surcharge-laws/
