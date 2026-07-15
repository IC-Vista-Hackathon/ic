# Virginia Payment Compliance — Internal Reference

Not legal advice. Verify with counsel before relying on any point below.

## 1. Autopay/Recurring Payment Authorization — Credit Card vs ACH
- No Virginia-specific restriction found requiring recurring/autopay to be funded via ACH/bank debit only (no FL-style utility/insurance carve-out identified).
- Not confirmed — verify with counsel if targeting a specific regulated vertical in VA.

## 2. Credit Card Surcharge / Convenience Fee Law
- Surcharging is currently permitted in Virginia; Code of Virginia Title 6.2, Ch. 4, Art. 3 (Credit Cards) and **§ 59.1-608** (Virginia Consumer Protection Act) govern mandatory fees/surcharges.
- As of **July 1, 2025**, amended VCPA requires upfront total-price disclosure: no supplier may advertise/display a price without clearly and conspicuously including all mandatory fees or surcharges.
- No VA statutory surcharge percentage cap; card network rules apply (surcharge may not exceed merchant's actual cost of acceptance, generally capped ~4% by network rule).
- Surcharges may not be applied to debit/prepaid cards.
- **Watch item:** HB 1519 (passed Apr 2024) would prohibit surcharges on electronic payment methods; required 2025 General Assembly reenactment to take effect — confirm current status/effective date with counsel before relying on it as settled law.
- Related fee statutes: § 4.1-240 (ABC/tax collection fee/CC data storage), § 22.1-116.1 (school payment CC fee), § 19.2-353.3 (court fines/costs CC/check fee).

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal
- Virginia Consumer Protection Act — general prohibited practices: **§ 59.1-200**.
- Automatic Renewal Offers and Continuous Service Offers — **Title 59.1, Chapter 17.8 (§§ 59.1-207.45 to 59.1-207.49)**:
  - § 59.1-207.45: definitions ("automatic renewal" = subscription/purchase agreement auto-renewing at end of definite term for subsequent term >1 month).
  - § 59.1-207.46: affirmative consent required before charging consumer's card/account for auto-renewal or continuous service; must disclose renewal/cancellation terms; free-trial cancellation disclosure required before consumer becomes obligated to pay; online offers require conspicuous online cancellation option.
  - Renewals extending contracts >12 months (after initial term >30 days) require 30–60 day pre-renewal notice with cancellation deadline/method and copy of offer terms.
  - § 59.1-207.49: violation constitutes a prohibited practice under the VCPA (enforcement/penalties).

## 4. Data Breach Notification — Payment Card Data
- **Code of Virginia § 18.2-186.6** (effective July 1, 2008), "Breach of personal information notification."
- Covers unencrypted/unredacted computerized personal information including SSN, driver's license/state ID, financial account and credit/debit card numbers plus required access codes/passwords.
- Trigger: unauthorized access/acquisition that compromises security/confidentiality and causes or is reasonably believed to cause identity theft/fraud.
- Notice to affected VA residents: without unreasonable delay.
- Notice to VA Attorney General: required if >1,000 residents notified, without unreasonable delay.
- Civil penalty: up to $150,000 per breach or series of related breaches from a single investigation, enforced by AG.

## 5. State Law Referencing/Incorporating PCI-DSS
- No Virginia statute found directly incorporating PCI-DSS compliance/liability provisions (unlike WA RCW 19.255.020).
- Not confirmed — verify with counsel.

## 6. State EFT/ACH-Specific Statutes Beyond Reg E
- **Code of Virginia § 8.4A-108** (UCC Art. 4A, Funds Transfers) — defers to federal Electronic Fund Transfer Act (15 U.S.C. § 1693 et seq.) where EFTA governs; VA Art. 4A applies to remittance transfers under 15 U.S.C. § 1693o-1 that are not themselves EFTA "electronic fund transfers"; EFTA governs in event of inconsistency.
- § 6.1-39.4:1 (banking) references bank EFT authority subject to EFTA/Reg E compliance.
- No broader VA-specific consumer EFT/ACH statute beyond these found.

## Sources
- [Code of Virginia — Article 3. Credit Cards](https://law.lis.virginia.gov/vacodefull/title6.2/chapter4/article3/)
- [Virginia Takes Step Towards Prohibiting Creditors from Charging Electronic Payment Surcharges — Consumer Financial Services Law Monitor](https://www.consumerfinancialserviceslawmonitor.com/2024/06/virginia-takes-step-towards-prohibiting-creditors-from-charging-electronic-payment-surcharges-on-credit-transactions/)
- [Code of Virginia § 59.1-608 (2025 Updates)](https://law.lis.virginia.gov/vacodeupdates/title59.1/section59.1-608/)
- [§ 22.1-116.1 — Code of Virginia](https://law.lis.virginia.gov/vacode/title22.1/chapter8/section22.1-116.1/)
- [§ 19.2-353.3 — Code of Virginia](https://law.lis.virginia.gov/vacode/title19.2/chapter21/section19.2-353.3/)
- [§ 59.1-207.46 — Code of Virginia](https://law.lis.virginia.gov/vacode/title59.1/chapter17.8/section59.1-207.46/)
- [Chapter 17.8 — Automatic Renewal Offers and Continuous Service Offers, Code of Virginia](https://law.lis.virginia.gov/vacode/title59.1/chapter17.8/)
- [§ 59.1-200. Prohibited practices — Code of Virginia](https://law.lis.virginia.gov/vacode/title59.1/chapter17/section59.1-200/)
- [§ 59.1-207.49. Enforcement; penalties — Code of Virginia](https://law.lis.virginia.gov/vacode/title59.1/chapter17.8/section59.1-207.49/)
- [§ 18.2-186.6. Breach of personal information notification — Code of Virginia](https://law.lis.virginia.gov/vacode/title18.2/chapter6/section18.2-186.6/)
- [Virginia — Summary of U.S. State Data Breach Notification Statutes, Davis Wright Tremaine](https://www.dwt.com/gcp/states/virginia)
- [§ 8.4A-108. Relationship to Electronic Fund Transfer Act — Code of Virginia](https://law.lis.virginia.gov/vacode/title8.4A/part1/section8.4A-108/)
- [Code of Virginia § 6.1-39.4:1 (2006)](https://law.justia.com/codes/virginia/2006/toc0601000/6.1-39.4c1.html)
