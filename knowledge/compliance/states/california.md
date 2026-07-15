# California — Payment Compliance Reference

Internal compliance reference only. Not legal advice — verify with counsel before relying on this for product/legal decisions.

## 1. Autopay/Recurring Payment Authorization — Card vs ACH Restrictions
- No CA-specific statute found restricting recurring/autopay funding to ACH-only or barring credit card as a funding method (unlike FL's utility/insurance ACH-only autopay rule). **Not confirmed — verify with counsel.**
- CA's Automatic Renewal Law (Bus. & Prof. Code §§ 17600–17606, "CARL") governs *consent and disclosure* for recurring charges regardless of funding method (see §3), not which payment rail may be used.
- Example seen in practice (not a general state mandate): CA FAIR Plan (residual property insurer) autopay allows credit card, debit card, or ACH; ACH has no processing fee while card is subject to a 3.5% processing fee — this is a program policy, not a statutory funding restriction.

## 2. Credit Card Surcharge / Convenience Fee Law
- Civil Code § 1748.1 — prohibits retailers from imposing a surcharge on a cardholder who elects to pay by credit card instead of cash/check; discounts for cash payment are permitted if offered to all buyers.
- Exception: charges for card payment made by an electrical, gas, or water corporation approved by the CPUC under Pub. Util. Code § 755 are exempt from § 1748.1.
- Enforceability caveat: In *Italian Colors Restaurant v. Becerra* (9th Cir., Jan. 2018), the court held the no-surcharge law's application to certain merchants' "single-sticker" pricing disclosures violated First Amendment commercial speech protections. CA AG's public guidance states it will generally not enforce § 1748.1 against merchants similarly situated to the *Italian Colors* plaintiffs (i.e., those disclosing a card price and a cash price at point of sale). **Verify current enforcement posture with counsel** — the statute is still on the books but its enforceability is narrowed by case law.
- Willful violation liability: 3x actual damages + attorney's fees if surcharge not refunded within 30 days of written demand.

## 3. UDAP / Automatic Renewal / Negative-Option Statutes
- Bus. & Prof. Code §§ 17600–17606 (California Automatic Renewal Law, "CARL" / "ARL") — key requirements:
  - Clear and conspicuous disclosure of automatic renewal/continuous service terms, in visual proximity to the consent request, before the transaction.
  - Affirmative consent from the consumer before charging.
  - Acknowledgment provided to consumer including the terms, cancellation instructions, and (for free trials converting to paid) how to cancel before being charged.
  - Easy cancellation mechanism.
  - Amendments effective July 1, 2025 apply to contracts entered, amended, or extended on/after that date — expanded requirements (SB 655 / related 2024 amendments).
  - Exemptions: entities regulated by CPUC, FCC, FERC, or the CA Department of Insurance.
- Broader UDAP overlay: Unfair Competition Law (Bus. & Prof. Code § 17200 et seq.) and Consumers Legal Remedies Act (Civ. Code § 1750 et seq.) are commonly pled alongside CARL violations in recurring-billing litigation.

## 4. Data Breach Notification — Payment Card Data
- Civil Code § 1798.82 (businesses) and § 1798.29 (state agencies) — CA's breach notification statute.
- "Personal information" triggering notification includes first name/initial + last name combined with an unencrypted (or encrypted-but-key-compromised) financial account, credit, or debit card number **combined with any required security code, access code, or password** that would permit access to the account.
- Notification to affected CA residents required "in the most expedient time possible and without unreasonable delay," and no later than 30 calendar days after discovery (per current statutory text — confirm latest amendment language with counsel, this section is amended frequently).
- Breaches affecting more than 500 CA residents require submission of a sample notification to the CA Attorney General.

## 5. State Law Referencing/Incorporating PCI-DSS
- No CA statute expressly mandates PCI-DSS compliance by name. Civil Code § 1798.81.5 requires businesses owning/licensing/maintaining CA residents' personal information to implement "reasonable security procedures and practices appropriate to the nature of the information" — this is framework-agnostic (PCI-DSS, ISO 27001/27002, NIST CSF, or CIS Controls can all serve as evidence of reasonableness); PCI-DSS compliance is not itself a statutory safe harbor or requirement.
- § 1798.81.5 creates a private right of action for consumers whose PI is compromised due to failure to maintain reasonable security.

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- No general California "state EFT act" for consumer transactions was found beyond incorporation of federal EFTA/Reg E protections (per CA Dept. of Consumer Affairs Legal Guide CR-6).
- CA does have sector-specific EFT mandates unrelated to consumer payments companies: e.g., mandatory EFT for state tax/fee remittance above $10,000/month threshold (Rev. & Tax Code EFT program), and a new EFT payment mandate for alcoholic beverage wholesaler-retailer transactions effective 2026 (Bus. & Prof. Code, Div. 9, Ch. 15 tied-house rules). Not directly applicable to typical biller/payer recurring consumer billing.

## Sources
- [Civil Code § 1748.1 — Justia](https://law.justia.com/codes/california/code-civ/division-3/part-4/title-1-3/section-1748-1/)
- [CA AG — Credit Card Surcharges guidance](https://oag.ca.gov/consumers/general/credit-card-surcharges)
- [Bus. & Prof. Code §§ 17600–17606 — Justia](https://law.justia.com/codes/california/2009/bpc/17600-17606.html)
- [Bus. & Prof. Code § 17600 — CA Legislative Information](https://leginfo.legislature.ca.gov/faces/codes_displaySection.xhtml?sectionNum=17600.&lawCode=BPC)
- [2025 CARL Amendments summary — Scherer Smith & Kenny](https://sfcounsel.com/2025-amendments-to-californias-automatic-renewal-law-what-your-business-needs-to-know/)
- [Civil Code § 1798.82 — CA Legislative Information](https://leginfo.legislature.ca.gov/faces/codes_displaySection.xhtml?lawCode=CIV&sectionNum=1798.82)
- [Civil Code § 1798.29 — CA Legislative Information](https://leginfo.legislature.ca.gov/faces/codes_displaySection.xhtml?sectionNum=1798.29&lawCode=CIV)
- [CA AG — Data Security Breach Reporting](https://oag.ca.gov/privacy/databreach/reporting)
- [Civil Code § 1798.81.5 — CA Legislative Information](https://leginfo.legislature.ca.gov/faces/codes_displaySection.xhtml?sectionNum=1798.81.5&lawCode=CIV)
- [CA DCA Legal Guide CR-6 — Electronic Fund Transfers](https://www.dca.ca.gov/publications/legal_guides/cr_6.shtml)
- [CA FAIR Plan Autopay Program Guidelines](https://www.cfpnet.com/wp-content/uploads/2026/04/Autopay-Program-Guidelines-and-Enrollment-2.pdf)
