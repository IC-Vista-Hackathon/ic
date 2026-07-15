# Oregon — Payments Compliance Reference

_Internal compliance reference only — NOT legal advice. Verify with counsel before relying on this for a specific product/feature decision._

## 1. Autopay/Recurring Payment Authorization — Card vs ACH Restriction

- No Oregon-specific statute found restricting recurring/autopay funding source to ACH/bank debit vs. credit card (unlike Florida's utility/insurance ACH-only precedent).
- Oregon's Automatic Renewal Law (ORS 646A.293–646A.295) regulates *how* recurring/continuous-service billing must be authorized and disclosed, but does not dictate payment instrument (card vs. ACH).
- Utilities regulated by the Oregon PUC and insurers regulated under the Insurance Code are exempted from ORS 646A.295's automatic-renewal disclosure/consent requirements — but this exemption is about renewal-consent mechanics, not payment-method restriction.
- **Not confirmed — verify with counsel** whether any OR PUC rule or Dept. of Consumer & Business Services (insurance) rule separately restricts card-funded autopay for regulated utilities/insurers.

## 2. Credit Card Surcharge / Convenience Fee Laws

- No general Oregon statute prohibits merchant credit card surcharging. Oregon has no private-sector surcharge ban (unlike some states with no-surcharge statutes).
- Government-specific surcharge authority exists: ORS 825.502 (DMV/motor carrier taxes and fees) and ORS 802.112 (vehicle-related transactions) explicitly allow state agencies to add a surcharge to offset card-acceptance fees.
- Private merchants default to federal rules (Truth in Lending Act/Reg Z framework) and card network rules (e.g., Visa/Mastercard surcharge caps, notice requirements) since no state law overrides them.

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal

- Unlawful Trade Practices Act (UTPA): ORS 646.607 (unconscionable tactics — AG/DA enforcement only, no private right of action) and ORS 646.608 (enumerated deceptive practices — both AG/DA and private right of action).
- Private right of action under ORS 646.608: consumer may recover actual damages or $200 (whichever greater), plus punitive damages and attorney fees, for willful violations.
- Automatic Renewal / Continuous Service Law: ORS 646A.293 (definitions), ORS 646A.295 (prohibited actions; requirements).
  - Requires clear and conspicuous disclosure before consent: that the agreement continues until cancelled, cancellation policy, and recurring charge amount/timing.
  - Requires affirmative consent before charging card/bank/third-party payment account for the initial automatic-renewal or continuous-service term.
  - Requires an acknowledgment (retainable by consumer) with the offer terms and cancellation instructions.
  - Non-compliant shipped goods are deemed an unconditional gift to the consumer.
  - Exemptions: PUC/FCC/FERC-regulated utility services, insurance companies, banks/credit unions, service contract sellers, and certain franchise arrangements.

## 4. Data Breach Notification — Payment Card Data

- Oregon Consumer Information Protection Act (OCIPA): ORS 646A.600–646A.628, notice provisions at ORS 646A.604, definitions at ORS 646A.602.
- "Personal information" triggering notice includes name + financial account number, credit card number, or debit card number in combination with any required security code/access code/password.
- Notification to affected individuals required as soon as practicable, no later than 45 days after discovery.
- AG/state-agency notification required if breach affects 250+ Oregon residents, within the same 45-day window.
- Penalties: up to $1,000 per violation, up to $500,000 for a continuing violation (enforced by Oregon DOJ).

## 5. State Law Referencing/Incorporating PCI-DSS

- No Oregon statute directly mandates PCI-DSS compliance for private merchants.
- PCI-DSS is imposed contractually — Oregon state Treasury requires state agencies and third-party vendors handling card data to maintain PCI-DSS compliance under the Treasury Master Agreement for Merchant Card Services (see Treasury policy FIN 215), not via statute.
- OCIPA (data breach law) works alongside PCI-DSS obligations but does not codify PCI-DSS as a legal standard.

## 6. State-Specific EFT/ACH Statutes Beyond Federal Reg E

- No Oregon consumer-facing EFT/ACH statute beyond federal Reg E was identified.
- Oregon EFT statutes/rules located (ORS 293.462; OAR 150-316-0345; OAR 150-314-0310; OAR 459-005-0225; OAR 137-055-5035) govern *state government* payment/disbursement via EFT (tax payments, PERS, etc.) — not private consumer-to-business EFT/ACH authorization.

## Sources

- https://oregon.public.law/statutes/ors_825.502
- https://oregon.public.law/statutes/ors_802.112
- https://merchantcostconsulting.com/lower-credit-card-processing-fees/oregon-surcharge-laws/
- https://oregon.public.law/statutes/ors_646a.295
- https://oregon.public.law/statutes/ors_646a.293
- https://www.oregonlegislature.gov/bills_laws/ors/ors646a.html
- https://oregon.public.law/statutes/ors_646a.604
- https://oregon.public.law/statutes/ors_646a.602
- https://www.doj.state.or.us/consumer-protection/id-theft-data-breaches/data-breaches/
- https://dfr.oregon.gov/business/Documents/4117.pdf
- https://oregon.public.law/statutes/ors_646.607
- https://oregon.public.law/statutes/ors_646.608
- https://www.oregonlegislature.gov/lpro/Publications/BB2016TheUnlawfulTradePracticesAct.pdf
- https://www.oregon.gov/treasury/public-financial-services/Documents/Public-Financial-Services-Cash-Management/FIN215.pdf
- https://www.oregonlegislature.gov/bills_laws/ors/ors074A.html
