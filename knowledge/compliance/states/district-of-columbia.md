# District of Columbia — Payment Compliance Reference

Not legal advice. Internal compliance reference only — verify with counsel before acting.

## 1. Autopay/Recurring Payment — Credit Card vs ACH Restrictions
- No DC statute found restricting funding source (credit card vs ACH/bank debit) specifically for recurring/autopay setups. **Not confirmed — verify with counsel.**
- DC's Automatic Renewal Protections Act (DC Code Title 28A, Ch. 2, § 28A-203 et seq.) governs disclosure/consent/notice for auto-renewal generally but does not appear to restrict payment method/funding source.
- No FL-style ACH-only mandate for utility/insurance autopay identified for DC.

## 2. Credit Card Surcharge / Convenience Fee Laws
- No DC statute imposes a surcharge cap beyond federal/network rules (federal ceiling ~4%; Visa caps at 3%, Mastercard up to 4% if cost-justified).
- DC Consumer Protection Procedures Act (CPPA, DC Code §§ 28-3901–28-3913) applies to surcharge disclosure: surcharges (including credit card surcharges and Initiative 82-related fees) must be disclosed in a timely, prominent, and accurate fashion — not hidden, not in fine print, not disclosed only after ordering.
- DC Code § 42-1211 is a Recorder of Deeds document-recordation surcharge ($5/document) — unrelated to consumer credit card surcharging; do not cite it for payments compliance.
- DC Office of Attorney General has issued consumer alerts specifically on restaurant surcharge disclosure (oag.dc.gov) tied to Initiative 82 tipped-wage transition — relevant if InvoiceCloud has hospitality/restaurant billers in DC.
- Pending legislation may further regulate surcharge structure (e.g., limiting surcharge base to subtotal excluding tax/tip) — **status not confirmed, verify with counsel**.

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal
- **DC Consumer Protection Procedures Act (CPPA)** — DC Code §§ 28-3901 (definitions/purpose) through 28-3913; § 28-3904 = unfair or deceptive trade practices. Violation does not require proof consumer was actually misled/damaged. Interprets "unfair or deceptive" consistent with FTC Act § 5(a) (15 U.S.C. § 45(a)).
  - Enforcement: DC OAG (civil actions, restitution) + private right of action (treble damages or $1,500/violation, whichever greater, plus punitive damages and attorney's fees, injunctive relief).
- **Automatic Renewal Protections Act of 2018** — DC Code Title 28A, Ch. 2 (§ 28A-203 et seq.):
  - Requires clear/conspicuous disclosure of auto-renewal terms and cancellation procedure in the contract.
  - Free trial/gift offers require clear disclosure of post-trial price.
  - Contracts with initial term ≥12 months, auto-renewing for periods ≥1 month: written renewal notice required 30–60 days before renewal trigger, stating cost, cancel deadline, and how to get renewal/cancellation details.
  - Free trials ≥1 month: notify consumer 1–7 days before trial expiration; must obtain affirmative consent during final 7 days of trial before renewal charges apply.
  - Applies to any business offering an auto-renewing contract to a DC consumer, regardless of business location.
  - Violation voids the auto-renewal provision (contract terminates at end of then-current term) and is independently a CPPA violation.

## 4. Data Breach Notification — Payment Card Data
- **DC Code § 28-3851 (definitions)** and **§ 28-3852 (notification of security breach)**, Title 28, Ch. 38, Subch. II ("Consumer Security Breach Notification"), as amended by the Security Breach Protection Amendment Act of 2019/2020 (D.C. Law 23-98).
- "Personal information" expressly includes credit card number or debit card number, or any other number/code (identification number, security code, access code, password) that allows access to/use of an individual's financial or credit account.
- Notice to affected DC residents required in the most expedient time possible, without unreasonable delay.
- Notice to DC Office of Attorney General required if breach affects 50+ DC residents, in the most expedient manner, without unreasonable delay.
- 2019/2020 amendment added broader "personal information" definition and new data-security obligations (reasonable security safeguards) plus certain exemptions.
- Remedies: § 28-3852.02; violations may be treated as CPPA unfair/deceptive trade practices — treble damages or $1,500/violation, plus actual damages.

## 5. PCI-DSS References in Statute
- No DC statute found that names or incorporates PCI-DSS. DC's approach differs from Washington **State** (not DC) — WA State HB 1149 (2010) ties PCI-DSS compliance to a liability safe-harbor; that is a separate jurisdiction and should not be conflated with DC.
- DC's 2019/2020 breach-law amendment added a general "reasonable security safeguards" requirement, not a named framework. **Confirmed no PCI-DSS statutory reference found for DC.**

## 6. EFT/ACH-Specific Statutes Beyond Federal Reg E
- No DC-specific consumer EFT/ACH statute beyond federal Reg E identified in this pass. **Not confirmed — verify with counsel** if a dedicated DC EFT chapter exists outside the sources reviewed.

## Sources
- https://code.dccouncil.gov/us/dc/council/code/sections/47-3152
- https://www.getflexpoint.com/credit-card-surcharging-us-states/washington-dc-district-of-columbia
- https://code.dccouncil.gov/us/dc/council/code/sections/47-4402
- https://www.ncsl.org/financial-services/credit-or-debit-card-surcharges-statutes
- https://oag.dc.gov/release/consumer-alert-dc-restaurants-are-barred-charging
- https://code.dccouncil.gov/us/dc/council/code/sections/42-1211
- https://www.lawhelp.org/dc/resource/automatic-renewal-consumer-protection-legal-alert
- https://code.dccouncil.gov/us/dc/council/code/sections/28A-203
- https://law.justia.com/codes/district-of-columbia/title-28a/chapter-2/subchapter-i/
- https://code.dccouncil.gov/us/dc/council/code/titles/28A/chapters/2
- https://code.dccouncil.gov/us/dc/council/code/titles/28/chapters/39/
- https://code.dccouncil.gov/us/dc/council/code/sections/28-3901
- https://code.dccouncil.gov/us/dc/council/code/sections/28-3904
- https://code.dccouncil.gov/us/dc/council/code/sections/28-3852
- https://code.dccouncil.gov/us/dc/council/code/sections/28-3851
- https://code.dccouncil.gov/us/dc/council/code/sections/%5B28-3852.02%5D
- https://code.dccouncil.gov/us/dc/council/laws/23-98
- https://oag.dc.gov/about-oag/laws-legal-opinions/requirements-districts-data-breach-notification
- https://www.termsfeed.com/blog/washington-dc-security-breach-protection-2019-b23-0215/
- https://www.infolawgroup.com/insights/2010/03/articles/payment-card-breach-laws/faq-on-washington-states-pci-law
