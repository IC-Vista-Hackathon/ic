# Rhode Island — Payments Compliance Reference

_Internal compliance reference only — NOT legal advice. Verify with counsel before relying on this for a specific product/feature decision._

## 1. Autopay/Recurring Payment Authorization — Card vs ACH Restriction

- No Rhode Island statute found restricting recurring/autopay funding to ACH/bank debit vs. credit card generally.
- Notable RI-specific wrinkle: R.I. surcharge law (see §2 below) **prohibits utility companies and healthcare providers from surcharging credit card transactions** — this is a surcharge ban, not a funding-instrument ban, but it has the practical effect of making card-funded recurring utility/healthcare payments cost-neutral to the payer vs. ACH in RI (opposite mechanism from Florida's ACH-only approach).
- **Not confirmed — verify with counsel** for any RI PUC-specific rule mandating ACH for utility autopay enrollment.

## 2. Credit Card Surcharge / Convenience Fee Laws

- R.I. Gen. Laws § 6-13.1-2.1 (enacted via S0925/S2738) governs credit card surcharges: permits surcharging generally, capped at actual processing cost and not exceeding 4%.
- Requires disclosure of the surcharge amount before the transaction completes, plus clear point-of-sale notice.
- **Government entities, utility companies, and healthcare providers are explicitly prohibited from surcharging credit card transactions.**
- Separately, R.I. Gen. Laws § 8-15-9.1 addresses payment by credit card (court/judicial context) and § 35-21-1 addresses credit card payments to state/local government (consistent with the government surcharge prohibition above).
- Violation penalty: up to $500 per violation.

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal

- Deceptive Trade Practices Act / Unfair Trade Practices and Consumer Protection Act: R.I. Gen. Laws §§ 6-13.1-1 through 6-13.1-11 (core prohibition at § 6-13.1-2).
  - AG enforcement (injunction, restitution, receiver) plus private right of action: consumer may recover actual damages or $200 (whichever greater) for ascertainable loss from a deceptive practice.
- Automatic renewal / continuous service statute: enacted via S2273 (2024), effective 2025-01-01 — requires notice to consumer before contract engagement plus a reminder notice before renewal charge, detailing amount due, and cancellation information.
  - Follow-on bill S2768 (RI online cancellation parity — "click to cancel" for online-originated automatic renewals) — **confirm current enactment status with counsel**, as this tracked through the 2026 session and may not yet be final law.
  - **Exact codified section number for the 2024 automatic-renewal law not independently confirmed in this pass** — verify against the current R.I. General Laws index (likely within Title 6) before citing to counsel/regulators.

## 4. Data Breach Notification — Payment Card Data

- Identity Theft Protection Act: R.I. Gen. Laws §§ 11-49.3-1 et seq.; notification duty at § 11-49.3-4.
- Covered personal information includes account number, credit or debit card number in combination with any required security code, access code, password, or PIN permitting access to a financial account.
- Notice to affected RI residents required in the most expedient time possible, no later than 45 calendar days after confirmation of breach and ability to determine notification contents.
- If more than 500 residents affected: must also notify the RI Attorney General and major consumer reporting agencies (timing, distribution, content, count).
- Required notice contents: description of incident, data types involved, date/date range, remediation services offered, and security-freeze/police-report information.

## 5. State Law Referencing/Incorporating PCI-DSS

- **Not confirmed — verify with counsel.** No RI statute directly incorporating or mandating PCI-DSS was identified in this pass.

## 6. State-Specific EFT/ACH Statutes Beyond Federal Reg E

- **Not confirmed — verify with counsel.** No RI-specific consumer EFT/ACH statute beyond federal Reg E was identified in this pass.

## Sources

- https://law.justia.com/codes/rhode-island/title-8/chapter-8-15/section-8-15-9-1/
- https://law.justia.com/codes/rhode-island/title-35/chapter-35-21/section-35-21-1/
- https://merchantcostconsulting.com/lower-credit-card-processing-fees/rhode-island-surcharge-laws/
- https://www.theridirectory.com/blog/is-it-permissible-for-rhode-island-retailers-to-impose-a-credit-card-surcharge-on-customers/
- https://webserver.rilegislature.gov/BillText21/SenateText21/S0925.pdf
- https://webserver.rilegislature.gov/BillText22/SenateText22/S2738.pdf
- https://webserver.rilegislature.gov/Statutes/TITLE6/6-13.1/INDEX.htm
- https://webserver.rilegislature.gov/Statutes/TITLE6/6-13.1/6-13.1-2.htm
- https://law.justia.com/codes/rhode-island/title-6/chapter-6-13-1/
- https://webserver.rilegislature.gov/BillText24/SenateText24/S2273.pdf
- https://legiscan.com/RI/bill/S2273/2024
- https://legiscan.com/RI/bill/S2768/2026
- https://law.justia.com/codes/rhode-island/title-11/chapter-11-49-3/section-11-49-3-4/
- https://webserver.rilegislature.gov/Statutes/TITLE11/11-49.3/11-49.3-4.htm
- https://www.dwt.com/gcp/states/rhode-island
