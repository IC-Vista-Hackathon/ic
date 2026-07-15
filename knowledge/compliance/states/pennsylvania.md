# Pennsylvania — Payments Compliance Reference

_Internal compliance reference only — NOT legal advice. Verify with counsel before relying on this for a specific product/feature decision._

## 1. Autopay/Recurring Payment Authorization — Card vs ACH Restriction

- No Pennsylvania-specific statute found restricting recurring/autopay funding to ACH/bank debit vs. credit card (unlike Florida's utility/insurance ACH-only precedent).
- **Not confirmed — verify with counsel** whether PA PUC regulations for regulated utilities impose any card-vs-ACH distinction for autopay enrollment.

## 2. Credit Card Surcharge / Convenience Fee Laws

- No general PA statute prohibits merchant credit card surcharging; surcharging is legal, subject to federal law (Reg Z-adjacent) and card network rules (Visa/Mastercard caps, disclosure requirements).
- 12 Pa.C.S. Chapter 58 ("Transparent Payment Fees") — recently enacted provisions requiring disclosure of payment surcharges to consumers when credit cards are used. **Confirm current chapter/section numbering and effective date with counsel** — legislative text located via PA General Assembly site but full chapter text not independently verified in this pass.
- 34 Pa. Code § 231.113 addresses credit card/processing fees in a wage-payment/labor context (not general consumer surcharging) — do not conflate with retail surcharge rules.

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal

- Unfair Trade Practices and Consumer Protection Law (UTPCPL): 73 P.S. §§ 201-1 – 201-9.2.
  - Prohibits "unfair methods of competition and unfair or deceptive acts or practices"; 73 P.S. § 201-2(4) enumerates ~21 specific unfair/deceptive acts.
  - Private right of action: 73 P.S. § 201-9.2 — consumer may recover actual damages or $100 (whichever greater); court may award up to treble damages plus attorney fees.
  - Enforced by PA Attorney General, District Attorneys, and private plaintiffs.
- **No enacted PA automatic-renewal/negative-option statute as of this research (2026-07-14).** House Bill 129 (amending UTPCPL to require renewal reminders, express consent, and simplified cancellation for negative-option subscriptions) passed the PA House (passed 2025-07-01) but has **not been signed into law** — track its status before relying on it. Earlier attempts (HB 750, HB 1780, HB 2511, HB 1369) also did not become law.
- Absent a PA-specific autorenewal statute, negative-option/autorenewal practices are policed via general UTPCPL deception theory and federal ROSCA/FTC Click-to-Cancel framework (federal, not state).

## 4. Data Breach Notification — Payment Card Data

- Breach of Personal Information Notification Act (BPINA): 73 P.S. §§ 2301–2329.
  - Substantially amended by Act 33 of 2024 (signed 2024-06-28, effective 2024-09-26).
  - Covered "personal information" includes financial account/payment card number plus any required security code, access code, or password.
  - Notification to affected PA residents required "without unreasonable delay" after determination of breach.
  - AG notification required when breach affects more than 500 PA residents, given at the same time as consumer notice.
  - Encryption/redaction safe harbor applies unless the encryption key was also compromised.

## 5. State Law Referencing/Incorporating PCI-DSS

- No PA statute directly mandates or incorporates PCI-DSS. PA is not among the small set of states (e.g., Nevada, Washington, Minnesota) that reference PCI-DSS in statute.
- PCI-DSS compliance in PA remains a contractual obligation (card brand/acquirer agreements), not a state legal mandate.

## 6. State-Specific EFT/ACH Statutes Beyond Federal Reg E

- No PA consumer EFT/ACH statute beyond federal Reg E was identified in this pass. **Not confirmed — verify with counsel** for any PA Title 7 (Banks and Banking) provisions specific to EFT/ACH consumer protections.

## Sources

- https://www.getflexpoint.com/credit-card-surcharging-us-states/pennsylvania
- https://merchantcostconsulting.com/lower-credit-card-processing-fees/pennsylvania-surcharge-laws/
- https://www.legis.state.pa.us/cfdocs/legis/LI/consCheck.cfm?txtType=HTM&ttl=15&div=0&chpt=1&sctn=33&subsctn=0
- https://www.law.cornell.edu/regulations/pennsylvania/34-Pa-Code-SS-231-113
- https://www.attorneygeneral.gov/wp-content/uploads/2018/02/Unfair_Trade_Practices_Consumer_Protection_Law.pdf
- https://www.lampmanlaw.com/consumer-protection/unfair-trade-practices.html
- https://www.consumerlawpa.com/pennsylvania-law-protects-you-from-stubborn-auto-renewing-subscriptions-even-when-federal-law-does-not/
- https://pahouse.com/InTheNews/NewsRelease/?id=124404
- https://www.pahouse.com/InTheNews/NewsRelease/?id=135868
- https://penncapital-star.com/briefs/pa-house-committee-approves-legislation-to-make-ending-online-subscriptions-easier/
- https://consumerfsblog.com/2024/07/pennsylvania-amends-data-breach-notification-law/
- https://www.dwt.com/gcp/states/pennsylvania
- https://www.wolfbaldwin.com/articles/miscellaneous-articles/pennsylvanias-breach-of-personal-information-notification-act/
