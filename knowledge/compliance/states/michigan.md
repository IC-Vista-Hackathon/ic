# Michigan Payment Compliance Reference

*Internal compliance reference only — not legal advice. Verify with counsel before relying on this for a specific product/flow.*

## 1. Autopay/Recurring Payment Authorization — Credit Card vs ACH/Bank Debit
- No MI-specific statute found restricting recurring/autopay funding to ACH/bank-debit only (no Florida-style utility/insurance carve-out identified).
- Not confirmed — verify with counsel whether any MI agency (insurance, utility PSC) rule imposes a card-vs-ACH restriction on recurring autopay.

## 2. Credit Card Surcharge / Convenience Fee Law
- Michigan has **no statute prohibiting** credit card surcharges (ban effectively removed following card-network antitrust litigation; surcharging has been permitted since ~2013).
- Debit card surcharging remains illegal (as under federal card network rules generally).
- Disclosure required: brick-and-mortar merchants must post notice at store entrance and point of sale; online sellers must disclose on the first web page where credit card payment is mentioned.
- Businesses that surcharge credit cards must remit 6% MI sales tax on the surcharge amount.
- Special signage rules apply to gas stations advertising cash vs. credit price differentials.
- Relevant statutory references: MCL Act 379 of 1984; MCL 445.1854 (referenced in search but not independently verified against full text — confirm exact surcharge-disclosure citation with counsel).

## 3. UDAP / Negative Option / Autorenewal
- General UDAP statute: Michigan Consumer Protection Act (MCPA), MCL 445.903 (Act 331 of 1976) — unfair, unconscionable, or deceptive methods/acts/practices in trade or commerce.
- **Important limitation**: *Smith v. Globe Life Ins. Co.*, 460 Mich. 446 (1999) — MI Supreme Court held the MCPA does not apply where "the general transaction is specifically authorized by law" under another regulatory scheme, effectively exempting many regulated industries (insurance, financial services, utilities) from MCPA claims regardless of whether the specific misconduct itself was authorized. This significantly narrows MCPA's reach for payments/financial-services conduct — confirm applicability to your specific product line with counsel.
- **Automatic renewal / negative option**: Michigan currently has **no enacted automatic-renewal statute**. HB 4826 (introduced Aug. 27, 2025) would require: clear/conspicuous disclosure (≥14-pt type) of auto-renewal terms, term length/pricing, cancellation procedure and that cancellation may occur up to the day before renewal; pre-renewal electronic notice for terms > 2 months; ban on cancellation fees; easy cancellation mechanism with prompt confirmation; voidability for non-compliant contracts. Exemptions proposed for regulated telecom/broadband and insurance contracts. **Not yet law — status pending as of search date; do not treat as binding.**

## 4. Data Breach Notification — Payment Card Data
- Michigan Identity Theft Protection Act, MCL 445.63, 445.72 (enacted 2006).
- Covered "personal information" includes financial account/demand deposit number, or credit/debit card number, combined with any required security code, access code, or password permitting account access.
- Notice required "without unreasonable delay" to all affected MI residents.
- No notice required if entity determines the breach has not/is not likely to cause substantial loss, injury, or identity theft.
- Civil fine: up to $250 per failure to notify (MCL 445.72(13)); aggregate liability for multiple violations from the same breach capped at $750,000.

## 5. State Law Referencing/Incorporating PCI-DSS
- Not confirmed — no MI statute equivalent to Minnesota's Plastic Card Security Act was found. Verify with counsel.

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- Michigan Electronic Funds Transfers Act, MCL Chapter 488 (Act 322 of 1978).
  - Covers EFT terminal customer account statements, unauthorized-use liability, error notification/resolution, security options, and terminal access.
  - Search sources did not surface a specific preauthorized-ACH provision text (comparable to MA ch. 167B §10) — confirm exact section with counsel if a specific written-authorization requirement is needed.

## Sources
- [Michigan.gov — Credit and Debit Card Surcharges](https://www.michigan.gov/consumerprotection/protect-yourself/consumer-alerts/shopping/credit-debit-card-surcharges)
- [MCL Act 379 of 1984 (legislature.mi.gov)](https://www.legislature.mi.gov/Laws/MCL?objectName=mcl-act-379-of-1984)
- [MCL 445.1854 (legislature.mi.gov)](https://www.legislature.mi.gov/Laws/MCL?objectName=mcl-445-1854)
- [MCL 445.903 — Michigan Consumer Protection Act (legislature.mi.gov)](https://www.legislature.mi.gov/Laws/MCL?objectName=MCL-445-903)
- [Smith v. Globe Life Ins. Co. (Justia)](https://law.justia.com/cases/michigan/supreme-court/1999/110065-6.html)
- [Michigan Bar Journal — "What's left after Smith v Globe?"](https://www.michbar.org/file/barjournal/article/documents/pdf4article619.pdf)
- [Miller Canfield — Michigan Reintroduces Automatic Renewal Law](https://www.millercanfield.com/resources-Michigan-Reintroduces-Automatic-Renewal-Law.html)
- [HB 4826 of 2025 (legislature.mi.gov)](https://www.legislature.mi.gov/Bills/Bill?ObjectName=2025-HB-4826)
- [MCL 445.72 (legislature.mi.gov)](https://www.legislature.mi.gov/Laws/MCL?objectName=mcl-445-72)
- [DWT — Michigan data breach notification summary](https://www.dwt.com/gcp/states/michigan)
- [MCL Chapter 488 — Electronic Funds Transfers (legislature.mi.gov)](https://www.legislature.mi.gov/Laws/MCL?objectName=mcl-act-322-of-1978)
