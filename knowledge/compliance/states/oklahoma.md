# Oklahoma — Payments Compliance Reference

*Internal compliance reference only — not legal advice. Verify with counsel before acting.*

## 1. Autopay / Recurring Payment Funding Source Restrictions
- No consumer-facing Oklahoma statute found restricting recurring/autopay funding to ACH-only vs. credit card, comparable to FL's utility/insurance rule.
- **Adjacent but distinct**: **Okla. Stat. tit. 36, § 36-1219.6** — for health insurance plans issued/amended/renewed on/after 1/1/2020, bars health plans from restricting **provider** payment methods to credit-card-only, and bars insurers from charging a fee solely to transmit ACH payments to a provider without provider consent. This governs insurer→provider payments, not consumer premium autopay — do not conflate.
- Not confirmed — verify with counsel whether any OK Insurance Department rule restricts consumer premium autopay funding method.

## 2. Credit Card Surcharge / Convenience Fee Law
- **Historical rule (repealed)**: Okla. Stat. tit. 14A, § 2-417 formerly barred sellers from surcharging credit/debit card use vs. cash/check. Repealed.
- **Current law — SB 677, effective 11/1/2025**, amends Okla. Stat. tit. 14A, § 2-211 to formally permit credit card surcharging, subject to:
  - Surcharge capped at the **lesser of 2% of transaction value or actual cost of acceptance**.
  - Surcharges only on **credit cards** (not debit), and only where the consumer has a genuine alternative payment method available.
  - Disclosure required at point-of-entry AND point-of-sale (in-person); on homepage AND checkout page (online); verbally (phone transactions).
  - Applies broadly to "any person, entity, or retailer doing business in this state in any sales, service, or lease transaction including... consumer credit sales."

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal
- **Okla. Stat. tit. 15, §§ 751–763** — Oklahoma Consumer Protection Act: prohibits 32 enumerated unlawful practices in consumer transactions (broadly defined to include advertising/sale/purchase of services, property, intangibles for personal/household/business purposes). § 752 defines unfair/deceptive practices. Courts directed to follow FTC Act §5(a)(1) interpretations. § 761.1 addresses liability/remedies under the Act.
- **Okla. Stat. tit. 15, § 15-222** — pre-existing automatic renewal statute specific to **rental of goods or rental-related services**.
- **New — HB 1851, "Oklahoma Fair Renewal Act," effective 11/1/2025** — creates new **§ 15-773.4** and related provisions, applying broadly to automatic renewal contracts:
  - Requires clear/conspicuous presentation of auto-renewal terms (recurring charge amount, renewal term length, any minimum purchase obligation) before consumer agrees.
  - Requires an easy, accessible cancellation method (e.g., direct online cancellation link, or in-person option if service used in person).
  - Requires notice of material term changes, and a reminder notice 15–45 days before renewal.
  - Confirm final codified section numbers and enrolled bill text — this was newly effective as of 11/1/2025.

## 4. Data Breach Notification — Payment Card Data
- **Oklahoma Security Breach Notification Act — Okla. Stat. tit. 24, §§ 161–166**:
  - Requires notice to affected residents when unencrypted/unredacted personal information was, or is reasonably believed to have been, accessed/acquired by an unauthorized person.
  - **"Personal information" (as amended, effective 1/1/2026)** includes credit/debit card number combined with any required expiration date, security code, access code, or password permitting access to the resident's financial account — directly covers payment card data.
  - Defines "reasonable safeguards" (risk assessments, layered technical/physical defenses, employee training, incident response plan) as an affirmative-defense standard.
  - **Civil penalties**: up to $150,000 per breach; capped at $75,000 (plus actual damages) if reasonable safeguards were NOT in place but notice was still given. Entities with reasonable safeguards may assert that as an affirmative defense.
  - Confirm enrolled/current text given the recent (Nov 2025) amendment described in secondary sources — cite directly from Title 24 statute text before relying on specific dollar figures.

## 5. PCI-DSS References in State Law
- No Oklahoma statute directly incorporates or mandates PCI-DSS compliance (unlike MN, NV, WA, which have written PCI-DSS obligations into statute).
- Oklahoma lacks a general consumer data privacy/protection act historically; the **Oklahoma Consumer Data Privacy Act (OKCDPA)** — SB 546, signed 3/20/2026, effective 1/1/2027 — is a forthcoming comprehensive privacy law; confirm whether it references payment card security standards once in effect.
- Oklahoma State Treasurer publishes PCI-DSS guidance for state agencies accepting card payments (oklahoma.gov/treasurer) — internal state-government policy, not a statutory private-sector mandate.
- Oklahoma Financial Privacy Act restricts disclosure of customer financial records to government agencies absent consent/subpoena — a financial-privacy statute, not a PCI-DSS incorporation.

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- No general Oklahoma consumer EFT/ACH statute beyond federal Reg E was identified.
- § 36-1219.6 (above) is the one Oklahoma statute located that specifically regulates ACH payment fees — but scoped to health-insurer-to-provider payments, not general consumer EFT.
- Not confirmed — verify with counsel for further OK Banking Department rules.

## Sources
- [Bass Berry — Oklahoma Formally Allows Credit Card Surcharges](https://www.bassberry.com/news/bedlam-no-more-oklahoma-formally-allows-credit-card-surcharges/)
- [Okla. Stat. tit. 14A, § 2-417 (2024, pre-repeal text) — Justia](https://law.justia.com/codes/oklahoma/title-14a/section-14a-2-417/)
- [AGG — Oklahoma Officially Legalizes Surcharging Starting Nov 1, 2025](https://www.agg.com/news-insights/publications/oklahoma-officially-legalizes-surcharging-starting-november-1-2025/)
- [HB 1851 (2025-26), Engrossed — Oklahoma Fair Renewal Act (PDF)](https://www.oklegislature.gov/cf_pdf/2025-26%20ENGR/hB/HB1851%20ENGR.PDF)
- [Okla. Stat. tit. 15, § 15-222 — Automatic Renewal, Rental of Goods (Justia)](https://law.justia.com/codes/oklahoma/title-15/section-15-222/)
- [Okla. Stat. tit. 24, § 24-163 — Duty to Provide Notice of Breach (Justia)](https://law.justia.com/codes/oklahoma/title-24/section-24-163/)
- [Troutman — Oklahoma Amends Data Breach Notification Statute](https://www.troutmanprivacy.com/2025/11/oklahoma-amends-data-breach-notification-statute/)
- [NCLC — Okla. Stat. tit. 15, §§ 751-763 Consumer Protection Act](https://library.nclc.org/book/unfair-and-deceptive-acts-and-practices/okla-stat-tit-15-ssss-751-through-763-consumer)
- [Okla. Stat. tit. 15, § 15-753 — Unlawful Practices (Justia)](https://law.justia.com/codes/oklahoma/title-15/section-15-753/)
- [Okla. Stat. tit. 36, § 36-1219.6 — Methods of Payments to Providers (Justia)](https://law.justia.com/codes/oklahoma/title-36/section-36-1219-6/)
- [Hunton — Oklahoma Enacts Comprehensive Consumer Privacy Law (OKCDPA)](https://www.hunton.com/privacy-and-cybersecurity-law-blog/oklahoma-enacts-comprehensive-consumer-privacy-law)
- [Oklahoma State Treasurer — PCI DSS Guidance](https://oklahoma.gov/treasurer/banking/merchant-credit-card-processing/payment-card-industries-data-security-standards.html)
