# Minnesota Payment Compliance Reference

*Internal compliance reference only — not legal advice. Verify with counsel before relying on this for a specific product/flow.*

## 1. Autopay/Recurring Payment Authorization — Credit Card vs ACH/Bank Debit
- No MN-specific statute found restricting recurring/autopay funding to ACH/bank-debit only (no Florida-style utility/insurance carve-out identified).
- Not confirmed — verify with counsel whether any MN agency rule (PUC/Commerce Dept) imposes a card-vs-ACH restriction on recurring autopay.

## 2. Credit Card Surcharge / Convenience Fee Law
- Minn. Stat. § 325G.051 — surcharging is **permitted** subject to conditions:
  - Surcharge capped at 5% of purchase price.
  - Must be disclosed orally at time of in-person sale **and** via conspicuously posted sign; for e-commerce/mobile, disclosed at point of sale/order summary/checkout page; for phone sales, disclosed orally.
  - A seller/lessor that issues its own proprietary credit/charge card may not surcharge use of that card.
  - Civil penalty: up to $500 per violation, plus refund of the surcharge to each affected buyer.
- **2025 update**: Minn. Stat. § 325D.44 (Deceptive Trade Practices Act) amended effective Jan. 1, 2025 — mandatory fees/surcharges, including card surcharges, must now be **included in the advertised price** (can't be added as a hidden line-item at checkout even if disclosed per §325G.051's separate disclosure regime); confirm interplay between §325G.051 and the amended §325D.44 with counsel before finalizing surcharge UX.

## 3. UDAP / Negative Option / Autorenewal
- General consumer fraud statute: Minn. Stat. § 325F.69 (Prevention of Consumer Fraud Act) — prohibits fraud, unfair/unconscionable practice, false pretense/promise, misrepresentation, misleading or deceptive practice in connection with sale of merchandise (broadly defined to include services/intangibles); also separately reaches solicitations for payment for merchandise/services not yet ordered.
- **Automatic renewal law**: Minn. Stat. § 325G.56 et seq. (enacted via S.F. 4097, effective **January 1, 2025**) — MN's first ARL.
  - Applies broadly to "indefinite subscription agreements" — both auto-renewal (fixed term, auto-renews) and continuous-service (runs until cancelled) structures; reaches categories often excluded elsewhere (health clubs, buying clubs, social referral services).
  - Requires clear disclosure of cancellation policy, recurring charges, and minimum purchase obligations before enrollment, plus affirmative consumer consent before first charge.
  - Requires an annual renewal reminder for indefinite/continuously-renewing memberships with cancellation instructions.
  - Cancellation must be easy to use, cost-effective, and timely; no unfair/abusive retention tactics or unsolicited "save" offers absent consumer's affirmative agreement to hear them.
  - See also §§ 325G.57, 325G.59 for related requirements — pull exact text before drafting a compliance checklist.

## 4. Data Breach Notification — Payment Card Data
- Minn. Stat. § 325E.61 (Minnesota data breach notification law).
- Covered "personal information": account number, credit card number, or debit card number combined with any required security code, access code, or password permitting access to a financial account (name + at least one such data element required to trigger the duty).
- Notice must be made "in the most expedient time possible and without unreasonable delay" to affected residents.
- If breach affects 500+ people, entity must notify the major nationwide consumer reporting agencies within 48 hours.
- Enforced by the MN Attorney General; **no private right of action**.

## 5. State Law Referencing/Incorporating PCI-DSS
- **Minnesota Plastic Card Security Act (PCSA), Minn. Stat. § 325E.64** — confirmed. First-in-nation state law incorporating a PCI-DSS-derived data retention limit (enacted 2007).
  - Prohibits any person/entity conducting business in MN that accepts an "access device" in connection with a transaction from retaining: (a) card security code data (CVV2 etc.), (b) the PIN verification code number, or (c) the full contents of any track of magnetic-stripe data — **subsequent to authorization** of the transaction, or (for PIN debit) **subsequent to 48 hours after** authorization.
  - Liability extends to the business's payment-card "service provider" (third party storing/processing/transmitting card data on the business's behalf) if that provider retains Protected Consumer Data beyond the 48-hour window.
  - PCI DSS itself mandates immediate destruction (no retention window) post-authorization, so full PCI DSS compliance functions as an effective safe harbor against PCSA exposure.
  - Liability under the PCSA is triggered when a business/service provider that violated the retention rule then suffers a breach exposing customers' personal information; the statute gives financial institutions a statutory right to reimbursement/indemnity and a private right of enforcement.

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- Minn. Stat. § 47.61 et seq. — "Electronic Funds Transfer Facilities" — defines electronic financial terminals and financial institutions eligible to operate them; permits terminal-based disbursement of funds under a preauthorized credit agreement. Primarily a terminal/institution-access statute rather than a consumer-authorization statute — confirm no additional preauthorized-debit consent provision exists elsewhere in Ch. 47 before relying on this alone.

## Sources
- [Minn. Stat. § 325G.051 (revisor.mn.gov)](https://www.revisor.mn.gov/statutes/cite/325g.051)
- [Austin Area Chamber — MN 2025 Credit Card Surcharge Law](https://www.austincoc.com/2025/01/29/a-sign-wont-cut-it-anymore-minnesotas-2025-credit-card-surcharge-law/)
- [Minn. Stat. § 325F.69 (revisor.mn.gov)](https://www.revisor.mn.gov/statutes/cite/325F.69)
- [Minn. Stat. § 325G.56 (Justia)](https://law.justia.com/codes/minnesota/chapters-324-342/chapter-325g/section-325g-57/)
- [ProsperStack — Minnesota Automatic Renewal Law compliance guide](https://prosperstack.com/blog/minnesota-automatic-renewal-law/)
- [Minn. Stat. § 325E.61 (revisor.mn.gov)](https://www.revisor.mn.gov/statutes/cite/325e.61)
- [Minn. Stat. § 325E.64 — Plastic Card Security Act (revisor.mn.gov)](https://www.revisor.mn.gov/statutes/cite/325E.64)
- [Fryberger Law Firm — The Minnesota Plastic Card Security Act](https://fryberger.com/articles/the-minnesota-plastic-card-security-act/)
- [InfoLawGroup — Minnesota's "Plastic Card Security Act"](https://www.infolawgroup.com/insights/2007/06/articles/privacy-law/minnesotas-plastic-card-security-act)
- [Minn. Stat. § 47.61 (revisor.mn.gov)](https://www.revisor.mn.gov/statutes/cite/47.61)
