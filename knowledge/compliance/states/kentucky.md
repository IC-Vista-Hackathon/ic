# Kentucky Payment Compliance Reference

> Internal compliance reference only — not legal advice. Verify with counsel before acting.

## 1. Autopay/Recurring Payment Authorization — Card vs. ACH Restrictions
- No Kentucky statute found restricting recurring/autopay funding to ACH-only (vs. credit card) for utilities, insurance, or generally. **Not confirmed — verify with counsel.**
- Governed by federal EFTA/Reg E for EFT/ACH-funded recurring payments.
- KRS 365.400–365.408 (automatic renewal law, see §3) regulates disclosure/consent but does not restrict payment funding method.

## 2. Credit Card Surcharge / Convenience Fee Law
- No Kentucky statute currently restricts or bans credit card surcharging; surcharging is legal.
- HB 259 (2013RS) would have capped surcharges at lesser of 3% or cost of acceptance and HB 256 (2013RS) would have banned surcharges outright — both died in the Senate, never enacted. Confirm no successor bill has since passed.
- Absent state law, surcharge practice defaults to federal/card-network rules (surcharge caps generally ~4%, disclosure requirements).
- Debit card surcharging is prohibited nationwide under the federal Durbin Amendment (Dodd-Frank), applies in Kentucky too.

## 3. UDAP / Autorenewal / Negative-Option Statutes
- Kentucky Consumer Protection Act, KRS 367.170: prohibits "unfair, false, misleading, or deceptive acts or practices" in trade/commerce; "unfair" construed as "unconscionable." Broad enough to reach undisclosed auto-renewal, hidden fees, unauthorized billing. Private right of action under KRS 367.220; AG enforcement under KRS 367.190.
- Automatic Renewal / Continuous Service Offers law — KRS 365.400 (definitions) through 365.408, enacted via 2023 SB 30, effective January 1, 2024:
  - Requires clear/conspicuous disclosure of auto-renewal or continuous-service terms before purchase (continuation until cancelled, cancellation policy, recurring charge amount, possible changes, renewal term length).
  - Requires consumer's affirmative consent before charging.
  - Requires acknowledgment of terms/cancellation method sent to consumer.
  - Requires an easy cancellation mechanism, including online cancellation if the service was purchased online.
  - First-violation remedy: prorated refund from start of most recent term to correction date.
  - Exemptions: entities regulated by Kentucky Public Service Commission or FCC, among others — confirm full exemption list with counsel.

## 4. Data Breach Notification — Payment Card Data
- KRS 365.732 (enacted 2014, effective 2015).
- Covers "personal information": first name/initial + last name combined with account number, credit/debit card number plus required security code/access code/password.
- Applies to any person/business conducting business in KY and owning/licensing computerized personal information; HIPAA-covered and GLBA-covered financial institutions exempt (follow federal frameworks instead).
- Notice required in "most expedient time possible and without unreasonable delay," subject to law-enforcement delay and scope investigation.
- Safe harbor for encrypted/redacted data.

## 5. State Law Referencing/Incorporating PCI-DSS
- No Kentucky statute found that independently mandates PCI-DSS compliance by law. **Not confirmed — verify with counsel.**
- PCI-DSS compliance in KY is driven by card-network/processor contracts, not state statute.

## 6. State-Specific EFT/ACH Statutes Beyond Reg E
- No standalone Kentucky consumer EFT/ACH statute beyond federal EFTA/Reg E identified for private recurring payments. **Not confirmed — verify with counsel.**
- KRS 41.167 and KRS 131.155 address electronic funds transfer only in state-government payment/tax contexts, not private consumer recurring billing.
- KRS Chapter 286.11 (Money Transmission) regulates money transmitters, not ACH/EFT consumer protections directly.

## Sources
- [KRS 367.170 — Unlawful acts](https://apps.legislature.ky.gov/law/statutes/statute.aspx?id=34914)
- [KRS Chapter 367 — Consumer Protection](https://apps.legislature.ky.gov/law/statutes/chapter.aspx?id=39092)
- [KRS 367.467 — Applicability of remedies](https://apps.legislature.ky.gov/law/statutes/statute.aspx?id=34967)
- [KRS 365.402 — Automatic renewal/continuous service requirements](https://apps.legislature.ky.gov/law/statutes/statute.aspx?id=54220)
- [2023 SB 30 (Ch. 81) — enrolled act text](https://apps.legislature.ky.gov/law/acts/23RS/documents/0081.pdf)
- [KRS 365.732 — Notification of security breach (FindLaw)](https://codes.findlaw.com/ky/title-xxix-commerce-and-trade/ky-rev-st-sect-365-732/)
- [KRS 365.732 official text](https://apps.legislature.ky.gov/law/statutes/statute.aspx?id=43326)
- [13RS HB 259 — surcharge bill (died)](https://apps.legislature.ky.gov/record/13RS/hb259/SCS1.doc)
- [13RS HB 256 — surcharge ban bill (died)](https://apps.legislature.ky.gov/record/13rs/hb256.html)
- [KRS 41.167 — Electronic funds transfers (Justia)](https://law.justia.com/codes/kentucky/chapter-41/section-41-167/)
- [KRS 131.155 — Tax payments by EFT](https://apps.legislature.ky.gov/law/statutes/statute.aspx?id=28110)
- [KRS Chapter 286.11 — Money transmission licensing](https://apps.legislature.ky.gov/law/statutes/statute.aspx?id=14946)
