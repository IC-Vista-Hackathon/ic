# South Carolina — Payments Compliance Reference

_Internal compliance reference only — NOT legal advice. Verify with counsel before relying on this for a specific product/feature decision._

## 1. Autopay/Recurring Payment Authorization — Card vs ACH Restriction

- No South Carolina statute found restricting recurring/autopay funding to ACH/bank debit vs. credit card (unlike Florida's utility/insurance ACH-only precedent).
- **Not confirmed — verify with counsel** for any SC Public Service Commission rule specific to utility autopay funding instrument.

## 2. Credit Card Surcharge / Convenience Fee Laws

- Credit card surcharging is legal in South Carolina. The historic 1976 blanket prohibition on surcharging was superseded by a 2013 amendment to S.C. Code § 39-1-100, which permits cash-discount practices; a 2013 bill (H.3477) that would have re-imposed a criminal surcharge ban (fines up to $500 / imprisonment up to 1 year) failed to pass.
- No state-imposed surcharge cap or disclosure statute beyond federal law and card network rules (Visa/Mastercard caps, notice requirements) was identified for private merchants.
- Government-specific provisions: S.C. Code § 14-1-214 (fines/fees/court costs by credit or debit card) and § 14-1-215 (clerks of court, registers of deeds, municipal court judges may accept card payments and impose a processing fee to offset administrative costs).
- A 2025-2026 session bill ("Sales Tax Swipe Fee Fairness Act," H.4613) is pending — **not yet law; verify current status before relying on it.**

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal

- South Carolina Unfair Trade Practices Act (SCUTPA): S.C. Code §§ 39-5-10 et seq. (definitions at § 39-5-10), patterned on FTC Act §5.
  - Prohibits unfair and deceptive acts/practices in trade or commerce.
  - Private right of action available; willful violations support treble damages and attorney fees.
- Automatic renewal statute: S.C. Code § 37-6-120 (Consumer Protection Code, general service contracts) and § 38-78-55 (insurance service contracts) — both amended by SB 434, signed into law 2024-05-20, effective same day.
  - Requires written/electronic notice of an automatic renewal provision 30–60 days before the cancellation deadline.
  - Notice must conspicuously disclose: (a) that the contract will auto-renew unless cancelled, (b) the renewal charge amount, (c) cancellation methods, including a toll-free number, email, mailing address (if directly billed), or another cost-effective/timely/easy-to-use cancellation mechanism.
  - Applies to renewals extending the contract beyond 6 months from initiation where the renewal term exceeds one month.
  - Scope limited to "service contracts": labor/personal services; transportation, hotel, restaurant, education, entertainment, recreation, physical-culture, hospital, funeral/cemetery privileges "and the like"; and insurance.
  - Also cross-reference S.C. Code § 44-79-60 (permissible contractual provisions, health club context) for a sector-specific analog.

## 4. Data Breach Notification — Payment Card Data

- S.C. Code § 39-1-90 (Trade and Commerce, Title 39, Chapter 1).
- Covered personal identifying information includes financial account number, credit card number, or debit card number in combination with any required security code, access code, or password permitting account access.
- Notification standard: "most expedient time possible and without unreasonable delay" — **no fixed day-count deadline** (unlike OR/PA/RI's 45-day/500-resident triggers).
- Enforcement: SC Attorney General does **not** have direct enforcement authority; enforcement rests with the SC Department of Consumer Affairs. Affected residents have a private right of action for damages, injunctive relief, and attorney fees/costs.

## 5. State Law Referencing/Incorporating PCI-DSS

- No direct statutory incorporation of PCI-DSS for general merchants was found.
- South Carolina Insurance Data Security Act, S.C. Code Title 38, Chapter 99 (effective 2019-01-01), imposes its own information-security-program requirements on insurance licensees (written InfoSec program by 2019-07-01; 72-hour cybersecurity-event notice to the Department of Insurance; third-party vendor security controls from 2020-07-01) — this is a NAIC Insurance Data Security Model Law implementation, not a PCI-DSS reference, and applies only to insurance licensees (exempt if fewer than 10 employees/contractors).

## 6. State-Specific EFT/ACH Statutes Beyond Federal Reg E

- **Not confirmed — verify with counsel.** No SC-specific consumer EFT/ACH statute beyond federal Reg E was identified in this pass.

## Sources

- https://www.scstatehouse.gov/sess120_2013-2014/bills/3477.htm
- https://law.justia.com/codes/south-carolina/title-14/chapter-1/section-14-1-214/
- https://merchantcostconsulting.com/lower-credit-card-processing-fees/south-carolina-surcharge-laws/
- https://www.ncsl.org/financial-services/credit-or-debit-card-surcharges-statutes
- https://www.scstatehouse.gov/sess126_2025-2026/bills/4613.htm
- https://law.justia.com/codes/south-carolina/title-37/chapter-6/section-37-6-120/
- https://law.justia.com/codes/south-carolina/title-38/chapter-78/section-38-78-55/
- https://doi.sc.gov/DocumentCenter/View/14744/2024-06-Legislative-Changes-to-SC-Code-Ann-Secs-38-78-55-37-6-120-38-78-20-and-37-1-301-relating-to-Service-Contracts
- https://law.justia.com/codes/south-carolina/title-44/chapter-79/section-44-79-60/
- https://law.justia.com/codes/south-carolina/title-39/chapter-1/section-39-1-90/
- https://sentra.io/learn/south-carolina-data-breach-notification-law-requirements
- https://www.scstatehouse.gov/code/t39c005.php
- https://law.justia.com/codes/south-carolina/title-39/chapter-5/section-39-5-10/
- https://www.scstatehouse.gov/code/t38c099.php
- https://law.justia.com/codes/south-carolina/title-38/chapter-99/
