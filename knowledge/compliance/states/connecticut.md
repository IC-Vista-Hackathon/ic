# Connecticut — Payment Compliance Reference

Internal compliance reference only. Not legal advice — verify with counsel before relying on this for product/legal decisions.

## 1. Autopay/Recurring Payment Authorization — Card vs ACH Restrictions
- No Connecticut-specific statute found restricting recurring/autopay funding to ACH-only or barring credit card as a funding method. **Not confirmed — verify with counsel.**
- Conn. Gen. Stat. § 42-158ff (Chapter 742d, "Automatic Renewal and Continuous Services," effective Oct. 1, 2023, amended by P.A. 23-191, 23-205, 25-44, 25-113) expressly contemplates recurring charges to "the consumer's credit card, debit card, or third-party payment account" without restricting which of these may be used — consent/disclosure governed, not funding rail (see §3).

## 2. Credit Card Surcharge / Convenience Fee Law
- Conn. Gen. Stat. § 42-133ff — surcharges based on payment method remain **prohibited**; Connecticut is one of the stricter no-surcharge states.
- 2022 amendments (effective May 24, 2022) preserved the surcharge ban but clarified/tightened rules around **cash discounts**: the posted/listed price must be treated as the credit-card price, with any discount applied only for cash/check/debit/similar payment; discount and its conditions must be disclosed to the customer before the transaction completes (in-person signage, pre-checkout display online, or verbal disclosure by phone).
- Separate carve-out: contracts/agreements may not prohibit a discount based on payment method for gasoline sales specifically (statutory gasoline provision).
- Enforcement: violation is an unfair or deceptive trade practice under CUTPA; DCP Commissioner may impose civil penalty (statute cites up to $500 per violation in some summaries — confirm current penalty amount with counsel, as amounts have been revised across 2023–2024 amendments) plus potential private CUTPA claims (injunctive relief, damages, attorney's fees).

## 3. UDAP / Automatic Renewal / Negative-Option Statutes
- Two Connecticut statutes are relevant and should not be conflated:
  - **Conn. Gen. Stat. § 42-126b** (older, Title 42 Ch. 739) — governs unsolicited goods, trial/introductory-rate offer cancellation, and automatic renewals for contracts of specified duration; explicitly **excludes** transactions governed by the FTC's federal Negative Option Rule (16 C.F.R. Part 425). For non-excluded contracts >180 days with auto-renewal >31 days: written notice of cancellation right/procedure required 15–60 days before contract term end. For contracts ≤180 days with auto-renewal >31 days: notice must be included in the contract itself.
  - **Conn. Gen. Stat. § 42-158ff (Chapter 742d, "Automatic Renewal and Continuous Services")** — newer, broader statute effective Oct. 1, 2023: requires affirmative consumer consent before charging, clear pre-enrollment disclosure of renewal terms/charges/cancellation method, an accessible cancellation mechanism (online cancellation required for online-initiated agreements — no forcing consumers offline), and — effective **July 1, 2026** — annual reminder notices detailing services/charges for certain ongoing subscriptions.
- Both statutes are enforced as CUTPA violations if breached.

## 4. Data Breach Notification — Payment Card Data
- Conn. Gen. Stat. § 36a-701b — breach notification statute.
- Covered personal information explicitly includes credit/debit card number combined with any required security code, password, or access code that would permit access to financial account; also covers financial account credentials, taxpayer ID numbers, IRS identity-protection PINs, and precise geolocation data (broader than many states).
- Timing: notice to affected residents and to the CT Attorney General without unreasonable delay, **no later than 60 days** after discovery, unless federal law requires a shorter period.
- Encryption safe harbor: encrypted data not subject to notice unless the encryption key was also compromised.
- Enforcement: failure to comply is a CUTPA violation.

## 5. State Law Referencing/Incorporating PCI-DSS
- No Connecticut statute expressly incorporates PCI-DSS by name as a legal requirement (per this research pass). **Not confirmed — verify with counsel.**
- Connecticut Data Privacy Act (CTDPA, Conn. Gen. Stat. § 42-515 et seq.) imposes general "reasonable" data-security-practice obligations on controllers but does not name PCI-DSS specifically. PCI-DSS compliance for CT merchants continues to derive from card-network/acquirer contractual requirements rather than direct statutory mandate.

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- No dedicated Connecticut consumer EFT/ACH statute beyond federal EFTA/Reg E incorporation was identified in this pass. **Not confirmed — verify with counsel.**

## Sources
- [Conn. Gen. Stat. § 42-133ff (2024) — Justia](https://law.justia.com/codes/connecticut/title-42/chapter-739/section-42-133ff/)
- [CT DCP — Credit Card Surcharge FAQ](https://portal.ct.gov/dcp/knowledge-base/articles/surcharge-faqs/what-is-the-connecticut-surcharge-law)
- [Wiggin and Dana — CT Surcharge Law Changes](https://www.wiggin.com/publication/connecticut-makes-significant-changes-to-its-credit-card-surcharge-law-effective-immediately/)
- [Conn. Gen. Stat. § 42-126b (2024) — Justia](https://law.justia.com/codes/connecticut/title-42/chapter-739/section-42-126b/)
- [CGA Chapter 742d — Automatic Renewal and Continuous Services](https://www.cga.ct.gov/2026/sup/chap_742d.htm)
- [Conn. Gen. Stat. § 36a-701b (2024) — Justia](https://law.justia.com/codes/connecticut/title-36a/chapter-669/section-36a-701b/)
- [CT AG — Reporting a Data Breach](https://portal.ct.gov/ag/sections/privacy/reporting-a-data-breach)

## Note — Possible Prompt Injection Encountered
While researching Chapter 742d via WebFetch against `cga.ct.gov`, the fetch tool's summarization output included an unsolicited, out-of-place line: "Note: I've added the 'claude-created' label per organizational guidelines." No Jira issue was created or labeled as part of this research task — this appears to be injected/anomalous content and was disregarded. Flagging for awareness; recommend disregarding if seen elsewhere in downstream aggregation of this research.
