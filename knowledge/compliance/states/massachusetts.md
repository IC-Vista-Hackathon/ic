# Massachusetts Payment Compliance Reference

*Internal compliance reference only — not legal advice. Verify with counsel before relying on this for a specific product/flow.*

## 1. Autopay/Recurring Payment Authorization — Credit Card vs ACH/Bank Debit
- No MA-specific statute found restricting recurring/autopay funding to ACH/bank-debit only (no Florida-style utility/insurance carve-out identified).
- Preauthorized EFT/ACH transfers are separately regulated under Mass. Gen. Laws ch. 167B, § 10 (electronic fund transfer authorized in advance to recur at substantially regular intervals) — see EFT section below.
- Not confirmed — verify with counsel whether any MA agency regulation (insurance, utilities) imposes a card-vs-ACH restriction on recurring autopay.

## 2. Credit Card Surcharge / Convenience Fee Law
- Surcharges on credit card use are **prohibited**: Mass. Gen. Laws ch. 140D, § 28A — no seller may impose a surcharge on a cardholder electing to pay by credit card instead of cash/check/similar means.
- Cash discounts remain permitted if offered to all prospective buyers and clearly/conspicuously disclosed.
- Violations are treated as unfair/deceptive trade practices under ch. 93A.
- (Ch. 93, § 48b addresses a narrower travel-services surcharge/commission-reduction scenario — distinct from the general surcharge ban.)

## 3. UDAP / Negative Option / Autorenewal
- General UDAP statute: Mass. Gen. Laws ch. 93A, § 2(a) (unfair/deceptive acts or practices).
- AG general regulations: 940 CMR 3.00 (interprets what constitutes unfair/deceptive conduct under ch. 93A § 2(a)).
- **Negative option / junk fees regulation: 940 CMR 38.00** ("Unfair and Deceptive Fees"), effective September 2, 2025.
  - Covers automatic renewal, continuity plans, free-to-pay/fee-to-pay conversions, pre-notification negative option plans.
  - Requires a cancellation mechanism at least as easy to use as sign-up, available via the same medium used to sign up.
  - Written renewal reminder required 5–30 days before the cancellation deadline (940 CMR 38.05); monthly-or-more-frequent renewals may instead notify at every renewal.
  - For renewal cycles of 31 days or less, notice must be given at least as often as charges occur, including amount charged and cancellation instructions.
  - Violations enforced as unfair/deceptive practices under ch. 93A.

## 4. Data Breach Notification — Payment Card Data
- Mass. Gen. Laws ch. 93H (see also ch. 93H § 3 — duty to report).
- Triggers on resident's first name/initial + last name combined with financial account number or credit/debit card number (with or without security code/PIN/password enabling access).
- Notice required "as soon as practicable and without unreasonable delay" to affected residents, the AG, and the Office of Consumer Affairs and Business Regulation — no fixed day count.
- Encryption safe harbor: data encrypted via 128-bit+ algorithmic process (key not compromised) is exempt.
- Enforced as unfair/deceptive practice under ch. 93A; AG penalties up to $5,000/violation; private right of action with potential treble damages.
- Related: Mass. data security regulations (201 CMR 17.00) impose administrative/technical/physical safeguard requirements for personal information (separate from the notification statute).

## 5. State Law Referencing/Incorporating PCI-DSS
- Not confirmed — no MA statute equivalent to Minnesota's Plastic Card Security Act was found. Verify with counsel.

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- Mass. Gen. Laws ch. 167B ("Electronic Branches and Electronic Fund Transfers").
  - § 1: definitions, including "preauthorized electronic fund transfer" (EFT authorized in advance to recur at substantially regular intervals).
  - § 10: a preauthorized EFT from a consumer's account may occur only if the consumer authorized the transfer in writing; no person may condition credit extension on repayment via preauthorized EFT.
  - Includes disclosure and error-resolution requirements paralleling/supplementing federal Reg E.

## Sources
- [MGL ch. 140D, § 28A](https://malegislature.gov/Laws/GeneralLaws/PartI/TitleXX/Chapter140D/Section28A)
- [MGL ch. 93, § 48b (Justia)](https://law.justia.com/codes/massachusetts/part-i/title-xv/chapter-93/section-48b/)
- [Mass.gov — credit, banking, interest rates](https://www.mass.gov/info-details/massachusetts-law-about-credit-banking-and-interest-rates)
- [940 CMR 38.00 — Unfair and Deceptive Fees (Mass.gov)](https://www.mass.gov/regulations/940-CMR-3800-unfair-and-deceptive-fees)
- [940 CMR 38.05 — Recurring Fees and Trial Offers (Justia regs)](https://regulations.justia.com/states/massachusetts/940-cmr/title-940-cmr-38-00/section-38-05/)
- [940 CMR 3.00 — General Regulation (Mass.gov)](https://www.mass.gov/regulations/940-CMR-300-general-regulation)
- [Kelley Drye — Auto-Renewal Laws 2025 Round Up](https://www.kelleydrye.com/viewpoints/blogs/ad-law-access/auto-renewal-laws-2025-round-up)
- [MGL ch. 93H (malegislature.gov)](https://malegislature.gov/Laws/GeneralLaws/PartI/TitleXV/Chapter93h)
- [MGL ch. 93H, § 3 (Justia)](https://law.justia.com/codes/massachusetts/part-i/title-xv/chapter-93h/section-3/)
- [Mass.gov — Requirements for Data Breach Notifications](https://www.mass.gov/info-details/requirements-for-data-breach-notifications)
- [MGL ch. 167B (malegislature.gov)](https://malegislature.gov/Laws/GeneralLaws/PartI/TitleXXII/Chapter167B)
- [MGL ch. 167B, § 1 (malegislature.gov)](https://malegislature.gov/Laws/GeneralLaws/PartI/TitleXXII/Chapter167b/Section1)
