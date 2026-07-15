# Arizona — Payment Compliance Reference

> Informational only. Not legal advice. Verify all citations and applicability with counsel before relying on this document.

## 1. Autopay/Recurring Payment Authorization — Credit Card vs ACH

- No Arizona-specific statute found restricting recurring/autopay funding source to ACH/bank debit only (no analog to Florida's utility/insurance ACH-only autopay restriction identified).
- Searched: Arizona Corporation Commission utility rules (A.A.C. Title 14, e.g. R14-2-203, R14-2-303 — electric/gas service rules), A.R.S. Title 20 (Insurance) premium payment provisions. No credit-card-specific autopay ban surfaced in either.
- Insurance premium autopay: A.R.S. Title 20 requires consumer consent before charging credit card/debit card/third-party account for premium billing generally; no funding-method restriction found.
- **Not confirmed — verify with counsel** that no such restriction exists in ACC rules, tariffs, or agency guidance not indexed by general web search (utility tariffs can impose payment-method rules outside statute).

## 2. Credit Card Surcharge / Convenience Fee Laws

- Arizona has no general statutory ban on merchant credit card surcharging.
- Arizona never enacted (per available sources) a general no-surcharge law comparable to those struck down/repealed in other states post-*Expressions Hair Design v. Schneiderman* (2017) / *Italian Colors*-adjacent litigation; no repeal history found because no ban existed to repeal.
- State-government-specific surcharge provisions exist, not general-merchant bans:
  - A.R.S. § 35-142 — governs state agencies accepting credit cards and imposing "service fee or surcharge" to recover processing cost.
  - A.R.S. § 12-118 — authorizes courts to accept credit card payments and impose a "convenience fee."
- General private-sector surcharging in Arizona is governed by card network rules (Visa/Mastercard surcharge caps, disclosure, debit-card surcharge prohibition) and federal law, not state statute.

## 3. UDAP / Consumer Protection — Autorenewal, Negative Option, Recurring Billing

- Arizona Consumer Fraud Act: A.R.S. § 44-1521 et seq. (Title 44, Ch. 10, Art. 7).
  - § 44-1521 — definitions.
  - § 44-1522 — declares unlawful: deception, deceptive/unfair act or practice, fraud, false pretense, false promise, misrepresentation, concealment/suppression/omission of material fact in connection with sale/advertisement of merchandise; applies regardless of actual reliance/damage.
  - Enforced by Arizona Attorney General; also supports private right of action.
- **Dedicated automatic-renewal statute: Arizona does NOT currently have one in force.**
  - HB 2951 (57th Legislature, 2nd Regular Session, 2026) would have added Title 44, Ch. 9, Art. 27 — automatic renewal/subscription contract requirements (affirmative consent, clear disclosure of recurring charge to payment method, easy cancellation matching sign-up method, renewal reminders 25–40 days pre-renewal, exemptions for Title 40 public service corporations, Title 20 insurance entities, Title 6 financial institutions).
  - Status: passed House (3/5/2026), crossed to Senate, **died** (last action ~6/14/2026, session adjourned sine die without enactment). Not law as of this writing (2026-07-14).
  - Until/unless re-introduced and enacted, negative-option/autorenewal practices in Arizona are policed only via the general Consumer Fraud Act (§ 44-1522) and FTC Act/ROSCA at the federal level — no AZ-specific autorenewal disclosure/cancellation mandate currently in force.
  - A.R.S. § 20-826 (Title 20, health service corporation subscription contracts) imposes cancellation/non-renewal notice rules (45-day advance written notice with reasons) but is narrow to medical/hospital service corporation subscriber contracts — not a general consumer autorenewal law.
- **Verify with counsel**: confirm current session status of HB 2951 or successor bill before relying on "no autorenewal statute" conclusion; legislative status can change each session.

## 4. Data Breach Notification — Payment Card Data

- A.R.S. § 18-552 (Title 18, Information Technology) — "Notification of security system breaches; requirements; enforcement; confidentiality; civil penalty; preemption; exceptions."
  - Note: this section was previously cited/numbered as § 18-545; content was renumbered to § 18-552 (definitions now cross-referenced at § 18-551).
  - Trigger: unauthorized acquisition of unencrypted/unredacted computerized "personal information."
  - Notification deadline: within 45 days of determining a breach occurred.
  - Large breach (>1,000 individuals affected): notify the three largest nationwide consumer reporting agencies, the Arizona Attorney General, and the Director of the Arizona Department of Homeland Security.
  - Civil penalty: AG may impose up to the lesser of $10,000 per affected individual or total economic loss to affected individuals; overall cap $500,000 per breach or related series of breaches.
  - "Personal information" definition (cross-referenced, § 18-551) — **not independently confirmed in this pass whether it explicitly enumerates payment card number + security code/expiration as a category; standard state-law pattern includes it. Verify with counsel** against current § 18-551 text.

## 5. Arizona Law Referencing/Incorporating PCI-DSS

- No Arizona statute found that directly incorporates, mandates, or references PCI-DSS as a legal compliance requirement for private-sector merchants.
- PCI-DSS compliance in Arizona is contractual (card network/acquirer requirements), not a state-law mandate.
- State government has internal PCI compliance policy (AZ Treasury, General Accounting Office) for state agencies handling card data — administrative/contractual policy, not a generally applicable statute.
- A breach of unencrypted cardholder data can independently trigger A.R.S. § 18-552 breach-notification obligations regardless of PCI-DSS status.

## 6. State EFT/ACH-Specific Statutes Beyond Federal Reg E

- No Arizona-specific consumer EFT/ACH statute beyond federal Electronic Fund Transfer Act (15 U.S.C. § 1693 et seq. / Reg E) confirmed.
- Arizona statutes reference and generally defer to the federal EFT Act rather than layering additional consumer EFT protections (e.g., certain Title 47 UCC provisions expressly state they don't apply to transfers governed by the federal EFT Act — see A.R.S. § 47-4A108 "Relationship to electronic fund transfer act").
- Arizona has an "Electronic Transactions Act" framework in Title 44 for electronic signatures/records (UETA-based), which is about e-signature/e-record validity, not payment-specific EFT/ACH regulation.
- **Not confirmed — verify with counsel** whether Arizona Department of Financial Institutions (DIFI) rules impose money-transmitter-level ACH origination requirements relevant to a payments company (DIFI licensing under A.R.S. Title 6 may apply depending on business model — out of scope of this pass; flag for follow-up if company originates ACH as a money transmitter in AZ).

---

## Sources

- [Arizona Consumer Fraud Act — A.R.S. § 44-1521/1522 (Deceptive Patterns overview)](https://www.deceptive.design/laws/arizona-consumer-fraud-act-a-r-s-ss-44-1521)
- [A.R.S. § 44-1522 — Justia](https://law.justia.com/codes/arizona/title-44/section-44-1522/) (verify against azleg.gov)
- [A.R.S. § 20-826 — Justia](https://law.justia.com/codes/arizona/title-20/section-20-826/)
- [HB 2951 (57th Leg., 2nd Reg. Session) — House Engrossed text, azleg.gov](https://www.azleg.gov/legtext/57leg/2R/bills/hb2951h.pdf)
- [HB 2951 — LegiScan bill tracking/status](https://legiscan.com/AZ/text/HB2951/id/3387437)
- [HB 2951 — Arizona Capitol Times coverage](https://azcapitoltimes.com/news/2026/03/05/remember-to-cancel-house-approves-legislation-to-prevent-unwanted-subscription-renewals/)
- [HB 2951 — BillTrack50](https://www.billtrack50.com/billdetail/1950408)
- [A.R.S. § 18-545/§18-552 — Justia (2017 version, renumbering note)](https://law.justia.com/codes/arizona/2017/title-18/section-18-545/)
- [A.R.S. § 18-552 — azleg.gov](https://www.azleg.gov/ars/18/00552.htm)
- [Arizona AG — Data Breach Notification FAQ](https://www.azag.gov/consumer/data-breach/faq)
- [Arizona credit card surcharge laws overview — Merchant Cost Consulting](https://merchantcostconsulting.com/lower-credit-card-processing-fees/arizona-surcharge-laws/)
- [A.R.S. § 12-118 — Justia (court convenience fee)](https://law.justia.com/codes/arizona/title-12/section-12-118/)
- [A.R.S. § 35-142 (state agency surcharge) — azleg.gov](https://www.azleg.gov/viewdocument/?docName=https://www.azleg.gov/ars/35/00101.htm)
- [AZ Treasury — PCI Compliance policy](https://www.aztreasury.gov/pci-compliance)
- [AZ General Accounting Office — PCI DSS / Visa Operating Regulations](https://gao.az.gov/news/payment-card-industry-data-security-standard-pci-dss-and-visa-operating-regulations)
- [A.R.S. § 47-4A108 — Justia (relationship to federal EFT Act)](https://law.justia.com/codes/arizona/2021/title-47/section-47-4a108/)
