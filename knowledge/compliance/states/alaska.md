# Alaska — Payments Compliance Reference

_Internal compliance reference only — NOT legal advice. Verify with counsel before relying on this for a specific product/feature decision._

## 1. Autopay/Recurring Payment Authorization — Card vs ACH Restriction

- No Alaska-specific statute found restricting recurring/autopay funding source to ACH/bank debit vs. credit card (unlike Florida's utility/insurance ACH-only precedent).
- Insurance premiums: AS 21.36 (Trade Practices and Frauds) / Division of Insurance Bulletin 97-15 — payment of premiums by credit, charge, or debit card permitted, subject to conditions:
  - Card payment may not increase total premium cost to policyholder.
  - No discount may be offered to policyholders who don't use a card.
  - Card payment option must be made available to all insureds (not selective).
  - Policy may be canceled only by named insured, policyholder, or insurer — not by the card company; nonpayment/decline of the card charge does not itself trigger cancellation unless card is canceled or credit limit exceeded.
  - Exact codified statute section number for the card-payment rule — **Not confirmed — verify with counsel** (sourced from Bulletin 97-15; underlying AS 21.36 section not independently verified in this pass).
- General/non-insurance recurring billing (AS 45.45.920 free trial, AS 45.45.930 opt-out marketing — see Topic 3) do not dictate payment instrument (card vs. ACH).
- **Not confirmed — verify with counsel** whether any Regulatory Commission of Alaska (RCA) utility rule separately restricts card-funded autopay for regulated utilities.

## 2. Credit Card Surcharge / Convenience Fee Laws

- No general Alaska statute bans private-merchant credit card surcharging; Alaska never enacted (or has since had no active) a no-surcharge law of the type struck down/repealed post-*Expressions Hair Design v. Schneiderman*.
- Debit card surcharging remains prohibited under card network rules nationally (not Alaska-specific).
- Bank-issued credit card terms (service charges, annual fees, unsolicited-card liability) governed by AS 06.05.209 (state banks) — general credit card terms statute, not a surcharge cap/ban.
- Alaska has no state general sales tax, so surcharge amounts are not complicated by state sales-tax-on-surcharge treatment the way they can be in sales-tax states; local/municipal sales taxes may still apply depending on locality — **Not confirmed — verify with counsel** for surcharge tax treatment in specific municipalities (e.g., Anchorage vs. Juneau vs. Fairbanks/Ketchikan borough sales tax rules).
- Attorney-specific guidance exists (Alaska Bar Ethics Opinion 2014-1): lawyers may pass along card-processing surcharges to clients if reasonable and disclosed/consented to — persuasive only, not a general merchant statute.
- Merchants otherwise follow federal/network rules (surcharge notice, cap ~3-4% per network rules, disclosure at point of sale and on receipts) absent state law.

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal

- Alaska Unfair Trade Practices and Consumer Protection Act: AS 45.50.471–45.50.561.
- AS 45.50.471(b) enumerates unlawful acts/practices; relevant incorporations:
  - AS 45.50.471(b)(49) — violating AS 45.45.920 (free trial period) is an unlawful trade practice.
  - AS 45.50.471(b)(50) — violating AS 45.45.930 (opt-out marketing plans) is an unlawful trade practice.
- AS 45.45.920 (Free trial period): seller must clearly/conspicuously disclose material terms (restrictions, post-trial charges, cancellation rights) before the trial; must obtain express verifiable consent before providing trial goods/services; consumer may cancel any time during trial, and within 30 days after trial ends for full/partial refund.
- AS 45.45.930 (Opt-out marketing plans — Alaska's negative-option/autorenewal-style statute): seller may not charge under an opt-out plan without express verifiable consent obtained after disclosing: (1) material terms of the plan/goods or services offered, (2) that the buyer's account will be charged absent affirmative opt-out, (3) the date the charge will be submitted, (4) the specific steps to avoid the charge. Seller bears burden of proving consent was obtained.
- Home security automatic-renewal contracts have a dedicated statute (enacted via legislation amending the UTPCPA) requiring a separate disclosure document, 60-day pre-termination notice if no renewal signature, and disclosure of renewal price/length — narrower scope (home security only), not a general subscription auto-renewal law.
- No standalone comprehensive "Alaska Automatic Renewal Law" of the California/Vermont style was found covering all consumer subscriptions; coverage is via AS 45.45.920/.930 (free trial/opt-out marketing) plus general UTPCPA unfairness/deception provisions (AS 45.50.471(a)).
- Private right of action and AG enforcement both available under UTPCPA (AS 45.50.531 et seq. — **section numbers for remedies not independently re-verified this pass, standard structure per NCLC digest**).

## 4. Data Breach Notification — Payment Card Data

- Alaska Personal Information Protection Act: AS 45.48.010–.090 (Article 1, "Breach of Security Involving Personal Information").
- AS 45.48.010 — disclosure of breach required "in the most expedient time possible and without unreasonable delay" (no fixed day-count deadline in statute).
- AS 45.48.020 — permits delayed notification (e.g., law enforcement investigation needs).
- AS 45.48.030 — acceptable methods of notice.
- AS 45.48.040 — notification to certain other agencies (credit reporting agencies, etc., depending on breach size).
- AS 45.48.050 — exemption for certain employee/agent-caused breaches.
- AS 45.48.070 — risk-of-harm exemption: no notice required if, after investigation and written notice to the Alaska Attorney General, entity determines no reasonable likelihood of harm; determination must be documented and retained 5 years.
- AS 45.48.080 — penalties for noncompliance.
- AS 45.48.090 — definitions: "personal information" = name + one or more of SSN, driver's license/state ID number, financial account/credit card/debit card number combined with any required security code/access code/password permitting account access. Encrypted or redacted data is excluded from "personal information" unless the encryption key was also acquired/accessed (encryption safe-harbor).
- Notable: Alaska is among the states with a private right of action for consumers under this statute, in addition to AG enforcement (per DWT 50-state survey) — **exact private-right-of-action section — verify with counsel.**
- Directly applicable to payment card breaches: card number + required security/access code triggers notice obligation.

## 5. State Law Referencing/Incorporating PCI-DSS

- No Alaska statute mandates PCI-DSS compliance for private merchants generally; PCI-DSS is not codified into the UTPCPA or AS 45.48.
- State government side: Alaska Department of Revenue, Treasury Division maintains an internal "Payment Card Policy for the State of Alaska" and PCI credit-card-security policy requiring PCI-DSS compliance for state agencies accepting card payments — this is agency policy/contractual, not a statute of general applicability to private businesses.
- University of Alaska system likewise has an internal PCI-DSS compliance policy — again institutional policy, not statute.
- Conclusion: PCI-DSS obligations for Alaska-based/serving merchants flow from card network/acquirer contracts, not state law.

## 6. State-Specific EFT/ACH Statutes Beyond Federal Reg E

- Alaska has adopted UCC Article 4A-style funds-transfer provisions at AS 45.14 ("Funds Transfers"), including AS 45.14.107 (relationship to Federal Reserve regulations/operating circulars). This chapter governs wholesale wire-transfer-type funds transfers (bank-to-bank), not consumer EFT/ACH authorization — it operates alongside, and is largely displaced for consumer transactions by, federal Reg E per the statutory "firewall" between UCC 4A and EFTA-covered transfers.
- No standalone Alaska consumer EFT/ACH-authorization statute beyond federal Reg E was identified in this pass.
- **Not confirmed — verify with counsel** whether any Alaska Division of Banking and Securities regulation imposes additional consumer EFT disclosure/authorization requirements beyond Reg E.

## Sources

- https://law.justia.com/codes/alaska/title-45/chapter-50/article-3/section-45-50-471/
- https://law.justia.com/codes/alaska/title-45/chapter-45/article-13/section-45-45-920/
- https://law.justia.com/codes/alaska/title-45/chapter-45/article-13/section-45-45-930/
- https://law.justia.com/codes/alaska/title-45/chapter-48/article-1/
- https://law.justia.com/codes/alaska/title-45/chapter-48/article-1/section-45-48-090/
- https://law.justia.com/codes/alaska/title-45/chapter-48/article-1/section-45-48-040/
- https://www.dwt.com/gcp/states/alaska
- https://law.justia.com/codes/alaska/title-6/chapter-05/article-3/section-06-05-209/
- https://law.justia.com/codes/alaska/title-21/chapter-36/article-5/section-21-36-460/
- https://codes.findlaw.com/ak/title-21-insurance/ak-st-sect-21-36-220.html
- https://www.commerce.alaska.gov/web/portals/11/pub/Bulletins/B97-15.pdf
- https://www.commerce.alaska.gov/web/ins/Consumers/Rights/Policy.aspx
- https://merchantcostconsulting.com/lower-credit-card-processing-fees/alaska-credit-card-surcharge-laws/
- https://www.getflexpoint.com/credit-card-surcharging-us-states/alaska
- https://alaskabar.org/wp-content/uploads/2014-1.pdf
- https://treasury.dor.alaska.gov/docs/treasurydivisionlibraries/cash-management/credit-card-payment-info/pci-credit-card-security-policy.pdf
- https://treasury.dor.alaska.gov/docs/treasurydivisionlibraries/cash-management/credit-card-payment-info/soa-pci-policy.pdf
- https://www.alaska.edu/compliance/PCI.php
- https://law.justia.com/codes/alaska/title-45/chapter-14/article-1/section-45-14-107/
- https://www.akleg.gov/basis/Bill/Text/28?Hsid=SB0103A
- https://library.nclc.org/book/unfair-and-deceptive-acts-and-practices/alaska-stat-ssss-4550471-through-4550561-unfair-trade
