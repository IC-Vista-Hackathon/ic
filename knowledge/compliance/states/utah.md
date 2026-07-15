# Utah Payment Compliance Reference

Internal compliance reference only — not legal advice. Verify with counsel before acting.

## 1. Autopay/Recurring Payment Authorization — Card vs ACH
- No Utah statute found restricting recurring/autopay funding source by payment type (no UT equivalent of Florida's ACH-only rule) — **Not confirmed — verify with counsel**.
- Utah Consumer Sales Practices Act (Utah Code § 13-11-4(2)(b)) treats failure to disclose full price/terms of a transaction as a deceptive act, which applies to recurring-billing enrollment disclosures generally, not a funding-source restriction specifically.
- Utah Consumer Credit Code (Title 70C) governs credit-based recurring obligations generally; no ACH-vs-card carve-out identified.

## 2. Credit Card Surcharge / Convenience Fee Law
- Utah previously banned surcharges under former Utah Code § 13-38a-302 for roughly one year; that prohibition was allowed to **expire in 2014** and has not been reinstated.
- Surcharging is currently **legal** in Utah with no state-specific cap beyond federal/card-network limits (network rules effectively cap around 3-4%); state law imposes only disclosure/accurate-labeling requirements — fees must be clearly disclosed before purchase, and a fee labeled as a "credit card processing charge" must actually reflect processing cost.
- Enforcement: Utah Division of Consumer Protection + AG, under Utah Consumer Sales Practices Act; penalties up to **$10,000 per violation** for knowing deceptive surcharge practices (per secondary-source guidance) — **verify exact statutory cite/amount with counsel**.

## 3. UDAP / Negative Option / Autorenewal
- Utah Consumer Sales Practices Act (CSPA) — Utah Code Title 13, Chapter 11 (§ 13-11-1 et seq.).
- § 13-11-4 — deceptive acts/practices, including nondisclosure of full transaction price/terms; applied by regulators/practitioners to inadequately-disclosed recurring-billing/negative-option enrollment.
- § 13-11-19 — private right of action for consumers.
- No standalone dedicated Utah "automatic renewal law" (distinct multi-section ARL like CA) was identified in this search — recurring-billing disclosure issues are addressed through the general CSPA deceptive-practices framework. **Not confirmed — verify with counsel whether a dedicated autorenewal statute has since been enacted.**

## 4. Data Breach Notification — Payment Card Data
- Protection of Personal Information Act — Utah Code Title 13, Chapter 44 (§ 13-44-101 et seq.), in effect since 2006; significantly updated by **SB 98 (2024)**, effective May 1, 2024.
- § 13-44-202 — disclosure of system security breach; "personal information" includes financial account or credit/debit card number combined with any required security code, access code, or password.
- Notification standard: no fixed-day statutory deadline — "without unreasonable delay" following a good-faith investigation into likelihood of misuse; new AG and Utah Cyber Center notification obligations added by the 2024 amendment.
- Penalties: up to **$2,500 per consumer** violation, capped at **$100,000 in the aggregate** for related violations; enforceable only by the Utah Attorney General (no private right of action).

## 5. State PCI-DSS References
- **Utah explicitly references PCI-DSS in statute** — Cybersecurity Affirmative Defense Act, Utah Code Title 78B, Chapter 4, Part 7 (§ 78B-4-701 et seq.).
- Provides an **affirmative defense** against certain data-breach-related tort claims (e.g., failure-to-implement-reasonable-security claims) for entities that create, maintain, and reasonably comply with a written cybersecurity program conforming to an industry-recognized framework.
- Where the breached data is governed by PCI-DSS, **reasonable compliance with PCI-DSS itself qualifies** for the affirmative defense (statute defines "PCI data security standard" = PCI-DSS directly).
- Defense unavailable if the entity had actual notice of a threat/hazard, failed to act within a reasonable time, and that failure led to the breach.
- This is a genuine safe-harbor incentive relevant to payments compliance — worth highlighting to counsel/security teams as a driver for maintaining documented PCI-DSS compliance in Utah specifically.

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- No standalone Utah consumer EFT/ACH authorization statute beyond federal Reg E identified in this search. **Not confirmed — verify with counsel.**
- Utah Consumer Credit Code (Title 70C) governs credit terms/disclosures generally but no EFT/ACH-specific consumer-authorization provisions surfaced.

## Sources
- [Utah DCP — Surcharges and Fees](https://commerce.utah.gov/dcp/education/surcharges-and-fees/)
- [Justia — 2012 Utah Code § 13-38a-302 (former surcharge ban)](https://law.justia.com/codes/utah/2012/title-13/article-38a/section-302/)
- [NCSL — Credit or Debit Card Surcharges Statutes Summary](https://www.ncsl.org/financial-services/credit-or-debit-card-surcharges-statutes)
- [Utah Code Title 13, Chapter 11 — Utah Consumer Sales Practices Act (Justia)](https://law.justia.com/codes/utah/title-13/chapter-11/)
- [Utah Code § 13-11-19 — Actions by consumer (Justia)](https://law.justia.com/codes/utah/title-13/chapter-11/section-19/)
- [Utah Code Chapter 44 — Protection of Personal Information Act (PDF)](https://le.utah.gov/xcode/Title13/Chapter44/C13-44_1800010118000101.pdf)
- [Utah Code § 13-44-202 — Disclosure of System Security Breach](https://le.utah.gov/xcode/Title13/Chapter44/13-44-S202.html)
- [DWT — Utah Data Breach Notification Statute Summary](https://www.dwt.com/gcp/states/utah)
- [Utah Code Title 78B Ch. 4 Part 7 — Cybersecurity Affirmative Defense Act (PDF)](https://le.utah.gov/xcode/Title78B/Chapter4/C78B-4-P7_2021050520210505.pdf)
- [Justia — Utah Code § 78B-4-701 — Definitions](https://law.justia.com/codes/utah/2021/title-78b/chapter-4/part-7/section-701/)
- [Armstrong Teasdale — Outlier or Pioneer? Utah Reconsiders a Cybersecurity Safe Harbor](https://www.armstrongteasdale.com/thought-leadership/outlier-or-pioneer-utah-reconsiders-a-cybersecurity-safe-harbor/)
