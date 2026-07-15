# Tennessee Payment Compliance Reference

Internal compliance reference only — not legal advice. Verify with counsel before acting.

## 1. Autopay/Recurring Payment Authorization — Card vs ACH
- No Tennessee-specific statute found restricting recurring/autopay funding source by payment type (no TN equivalent of Florida's ACH-only rule) — **Not confirmed — verify with counsel**.
- Tenn. Code Ann. § 47-18-133 — Automatic Renewal of Subscription Services: requires affirmative consent to auto-renewal/continuous-service terms before charging a consumer's credit or debit card, or third-party-held account; if renewal charge occurs >60 days after consent, requires clear/conspicuous notice before charging.
  - Remedy for non-compliant charge: refund within 7 days of consumer request.
  - Exemptions: banks, credit unions, TN Dept. of Financial Institutions licensees, and entities regulated by TN Public Utilities Commission, FCC, or FERC (i.e., utility/telecom autopay largely carved out of this particular statute).
  - Note: SB 302 ("Tennessee Consumer Protection and Subscription Renewal Act", introduced 2025) would amend/replace this provision — check current enactment status before relying on § 47-18-133 as-is.

## 2. Credit Card Surcharge / Convenience Fee Law
- No general TN statute caps or bans private-merchant credit card surcharges; surcharging is legal, but must be clearly disclosed before the sale under the TN Consumer Protection Act (Tenn. Code Ann. § 47-18-104, unfair/deceptive acts).
- Debit card surcharging is prohibited — this is a **federal** (card network / Durbin Amendment-adjacent) rule applied nationwide, not a distinct TN statute.
- Tenn. Code Ann. § 55-2-113 — authorizes the Dept. of Safety/Motor Vehicles Commissioner to impose a surcharge/convenience fee on card payments to offset processor costs; fee is "voluntary" once elected and non-refundable (state-agency-specific, not general merchant law).

## 3. UDAP / Negative Option / Autorenewal
- Tennessee Consumer Protection Act of 1977 — Tenn. Code Ann. Title 47, Chapter 18, Part 1 (§§ 47-18-101 to 47-18-135).
- § 47-18-104 — general unfair/deceptive acts prohibition; private right of action under § 47-18-109(a)(1).
- § 47-18-133 — automatic renewal/negative-option billing consent requirements (see §1 above).
- Legislative activity: SB 302 (2025) proposed narrower/updated auto-renewal rules — **verify current status with counsel** before relying on existing § 47-18-133 language alone.

## 4. Data Breach Notification — Payment Card Data
- Tenn. Code Ann. § 47-18-2107 (Identity Theft Deterrence Act); amended 2016/2017.
- "Personal information" includes name + account/credit/debit card number combined with any required security code, access code, or password permitting access to a financial account.
- Notification deadline: **45 days** from discovery/notification of breach (firm deadline post-2016 amendment).
- If more than 1,000 residents notified, must also notify nationwide consumer reporting agencies without unreasonable delay.
- GLBA-regulated financial institutions and HIPAA-covered entities are exempt from this TN statute (their federal breach regimes control instead).

## 5. State PCI-DSS References
- No TN statute found that codifies or cross-references PCI-DSS (unlike NV/MN/WA). PCI-DSS remains contractual (card brand/acquirer agreements) in Tennessee. **Confirmed absence based on search — verify with counsel.**
- Separate note: TN Dept. of Commerce & Insurance cybersecurity-event reporting (250+ TN residents affected) and annual compliance certification applies to insurance-sector entities specifically, not general merchants — do not conflate with PCI-DSS.

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- No TN-specific consumer EFT/ACH authorization statute beyond federal Reg E identified in this search. **Not confirmed — verify with counsel.**
- Tenn. Code Ann. Title 45 (Banks and Financial Institutions) governs money transmitters/financial institutions generally (e.g., § 45-5-403) but no distinct EFT consumer-authorization statute surfaced.

## Sources
- [Tenn. Code § 47-18-133 — Automatic renewal of subscription services (Justia)](https://law.justia.com/codes/tennessee/title-47/chapter-18/part-1/section-47-18-133/)
- [Husch Blackwell — Changes Proposed to TN Automatic Renewal and Continuous Services Statute](https://www.huschblackwell.com/newsandinsights/changes-proposed-to-the-tennessee-automatic-renewal-and-continuous-services-statute)
- [Cobalt LLP — Q1 2025 Auto Renewal Legislative Update](https://www.cobaltlaw.com/q1-2025-auto-renewal-legislative-update)
- [Tenn. Code § 55-2-113 (Justia)](https://law.justia.com/codes/tennessee/title-55/chapter-2/section-55-2-113/)
- [Tenn. Code § 47-18-104 — Unfair or deceptive acts prohibited (Justia)](https://law.justia.com/codes/tennessee/title-47/chapter-18/part-1/section-47-18-104/)
- [TN Consumer Protection Act Ch. 2 — AG Office guide (PDF)](https://www.tn.gov/content/dam/tn/attorneygeneral/documents/consumer/militaryguide/chapter02.pdf)
- [Tenn. Code § 47-18-2107 — Release of personal consumer information (Justia)](https://law.justia.com/codes/tennessee/title-47/chapter-18/part-21/section-47-18-2107/)
- [DWT — Tennessee Data Breach Notification Statute Summary](https://www.dwt.com/gcp/states/tennessee)
- [TN Dept. of Commerce & Insurance — Laws](https://www.tn.gov/commerce/regboards/pps/rules-and-laws/laws.html)
- [Merchant Cost Consulting — Tennessee Surcharge Laws](https://merchantcostconsulting.com/lower-credit-card-processing-fees/tennessee-surcharge-laws/)
