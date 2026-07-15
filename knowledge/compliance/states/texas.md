# Texas Payment Compliance Reference

Internal compliance reference only — not legal advice. Verify with counsel before acting.

## 1. Autopay/Recurring Payment Authorization — Card vs ACH
- No general Texas statute found restricting recurring/autopay funding source by payment type (no TX equivalent of Florida's ACH-only rule) — **Not confirmed — verify with counsel**.
- Insurance-specific: Tex. Ins. Code § 2210.2032 and related provisions require Texas FAIR Plan/windstorm association-type insurers to accept premium payment by credit card; associations may charge a card-use fee capped at actual cost recovery (not a funding-source restriction, just a fee-cap on card payment of premiums).
- No TX statute found mandating ACH-only for utility or insurance autopay.

## 2. Credit Card Surcharge / Convenience Fee Law — STATUS: STRUCK DOWN, ENFORCEMENT UNCERTAIN
- Tex. Bus. & Com. Code § 604A.0021 (recodified from Tex. Fin. Code § 339.001) prohibits sellers from imposing a surcharge on a buyer who elects to pay by credit card instead of cash/check.
- **Litigation history**: *Rowell v. Pettijohn* (5th Cir. 2016) → remanded → *Rowell/Roberts v. Paxton*, W.D. Tex. 2018 (Judge Lee Yeakel): the statute's surcharge ban was held **unconstitutional** as a First Amendment commercial-speech restriction (prohibiting merchants from *describing* a price differential as a "surcharge"), and the court **permanently enjoined** the State from enforcing it against the plaintiffs.
- **Current status is contested**: TX AG Opinion KP-0257 (2019) argues the injunction applies only to the specific parties/facts in that case, not a blanket statewide non-enforcement — meaning the statute remains on the books and TX has taken the position it may still be enforceable in other contexts. **Not confirmed — verify current enforcement posture with counsel before assuming surcharging is unrestricted statewide.**
- Common risk-mitigation alternatives used in TX (per practitioner guidance): uniform pricing, uniform "convenience fee" (rather than "surcharge"), cash discounting, no-fee card option, third-party payment processor pass-through.
- Debit card surcharging remains prohibited under federal/card-network rules regardless of TX statute status.

## 3. UDAP / Negative Option / Autorenewal
- Texas Deceptive Trade Practices–Consumer Protection Act (DTPA) — Tex. Bus. & Com. Code Chapter 17 (§ 17.41 et seq.); § 17.46 lists prohibited false/misleading/deceptive acts.
- **No dedicated Texas automatic-renewal/negative-option statute currently in force** — as of this research, Texas has historically had no ARL-style law. 2025 bills **HB 2859** and **SB 838** (89th Legislature) would newly require: written renewal notice 90–15 days before renewal for contracts ≥12 months, multiple cancellation methods (matching signup method), and advance notice for periodic-shipment goods. **Verify current enactment status with counsel** — as of this research these were introduced bills, not confirmed law.
- Until/unless such a bill is enacted, autorenewal practices in TX are governed generally by DTPA § 17.46 deceptive-practices standard only.

## 4. Data Breach Notification — Payment Card Data
- Tex. Bus. & Com. Code § 521.053 (Identity Theft Enforcement and Protection Act, Chapter 521).
- Covers "sensitive personal information" including account/credit/debit card number combined with any required security code, access code, or password permitting access to a financial account.
- Notification to affected individuals: without unreasonable delay, and not later than the **60th day** after the date the breach is determined.
- AG notification required if breach involves **250+ TX residents**, no later than the **30th day** after determination (tightened by 2023 amendment).
- Harm-threshold exception: notice not required if investigation determines unauthorized acquisition is not reasonably likely to result in harm (distinguishes TX from stricter no-harm-threshold states like CA).
- Substitute notice available if direct-notice cost exceeds $250,000 or affected population exceeds 500,000.

## 5. State PCI-DSS References
- Tex. Bus. & Com. Code § 521.052 — general "business duty to protect sensitive personal information" via reasonable procedures; does **not** name PCI-DSS specifically.
- No TX statute found that directly codifies or cross-references PCI-DSS (unlike NV/MN/WA-style statutes). **Confirmed absence based on search — verify with counsel.**
- § 521.052 excludes financial institutions as defined by 15 U.S.C. § 6809 (GLBA-regulated entities carved out).

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- No standalone Texas consumer EFT/ACH authorization statute beyond federal Reg E (12 CFR Part 205) identified in this search — TX relies on federal Reg E framework (written authorization for preauthorized transfers, notice for recurring debits every ≤60 days, etc.). **Not confirmed — verify with counsel.**
- 34 Tex. Admin. Code § 5.12 addresses EFT processing in a specific regulatory context (state agency payment processing) — not a general consumer EFT statute.

## Sources
- [Merchant Cost Consulting — Texas Surcharge Rules](https://merchantcostconsulting.com/lower-credit-card-processing-fees/texas-surcharge-laws/)
- [Texas State Law Library — Can a business charge a fee for using a credit card?](https://www.sll.texas.gov/faqs/credit-card-surcharge/)
- [Freeman Law — Surcharges on Credit Card and Debit Card Purchases in Texas](https://freemanlaw.com/surcharges-on-credit-card-and-debit-card-purchases-in-texas/)
- [Tex. Bus. & Com. Code § 604A.0021 (Justia)](https://law.justia.com/codes/texas/business-and-commerce-code/title-12/chapter-604a/section-604a-0021/)
- [Tex. Ins. Code § 2210.2032 — Premium Payment Methods](https://texas.public.law/statutes/tex._ins._code_section_2210.2032)
- [Texas DTPA — Bus. & Com. Code Chapter 17 (statutes.capitol.texas.gov)](https://statutes.capitol.texas.gov/Docs/BC/htm/BC.17.htm)
- [Texas State Law Library — Are auto-renewing contracts legal in Texas?](https://www.sll.texas.gov/faqs/auto-renewing-contracts/)
- [Texas Policy Research — HB 2859, 89th Legislature](https://www.texaspolicyresearch.com/bills/89th-legislature-hb-2859/)
- [BillTrack50 — TX SB838](https://www.billtrack50.com/billdetail/1789983)
- [Kelley Drye — Auto-Renewal Laws: 2025 Round Up](https://www.kelleydrye.com/viewpoints/blogs/ad-law-access/auto-renewal-laws-2025-round-up)
- [Tex. Bus. & Com. Code § 521.053 (Justia)](https://law.justia.com/codes/texas/business-and-commerce-code/title-11/subtitle-b/chapter-521/subchapter-b/section-521-053/)
- [Workplace Privacy Report — Texas Tightens State's Data Breach Notification Law (2023)](https://www.workplaceprivacyreport.com/2023/06/articles/data-breach-notification/texas-tightens-states-data-breach-notification-law/)
- [Tex. Bus. & Com. Code § 521.052 (FindLaw)](https://codes.findlaw.com/tx/business-and-commerce-code/bus-com-sect-521-052/)
