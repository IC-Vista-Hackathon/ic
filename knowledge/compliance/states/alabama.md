# Alabama Payment Compliance Reference

> Internal compliance reference only — not legal advice. Verify with counsel before acting.

## 1. Autopay/Recurring Payment Authorization — Card vs. ACH Restrictions
- No Alabama statute found restricting recurring/autopay funding to ACH-only for utilities/insurance or generally. **Not confirmed — verify with counsel.**
- No Alabama analog to Florida-style card-autopay restriction identified.
- General credit-card-authorization statutes found are government-payment-acceptance statutes (Ala. Code § 11-103-1, § 41-1-60, § 45-37-73.01), not consumer recurring-billing funding-method restrictions.
- Governed by federal EFTA/Reg E for EFT/ACH-funded recurring payments; no state overlay identified.

## 2. Credit Card Surcharge / Convenience Fee Law
- No general Alabama statute banning or restricting merchant credit card surcharging identified — no Italian Colors/Expressions Hair Design-era surcharge ban ever appears to have existed at state level for private merchants. **Not confirmed exhaustively — verify with counsel.**
- Government-context surcharge statutes exist but apply only to government entities accepting payments, not general commerce:
  - Ala. Code § 41-1-60 — state agencies may accept credit/charge/debit cards; may impose surcharge/convenience fee to offset discount/administrative fees charged to state government (not to exceed that cost); payment of the fee is deemed voluntary and non-refundable once elected.
  - Ala. Code § 11-103-1 — parallel authorization for local governmental entities to accept credit card payment and impose offsetting surcharge/convenience fee.
  - Ala. Code § 45-37-73.01 — local (Jefferson County) regulation of credit card payments.
- Debit card surcharging remains prohibited under federal law (Durbin Amendment/Reg II), applies nationwide regardless of state law.
- Effective surcharge cap driven by federal/card-network rules (~3–4%), not Alabama statute.
- Clear pre-transaction disclosure expected under card network rules.
- Note: Alabama SB221 (2026) excludes credit card processing fees from the sales/use tax base — separate tax-treatment issue, not a surcharge-legality statute.

## 3. UDAP / Autorenewal / Negative-Option Statutes
- Alabama Deceptive Trade Practices Act (ADTPA) — Ala. Code §§ 8-19-1 through 8-19-15. General UDAP framework; catchall provision at § 8-19-5 for "unconscionable, false, misleading or deceptive act[s] or practice[s] in the conduct of trade or commerce" could reach undisclosed/deceptive autorenewal or negative-option billing.
- Private right of action under § 8-19-10 (greater of actual damages or $100, up to treble damages in court's discretion); AG/district attorney enforcement under § 8-19-8; willful continuous violation is a Class 1 misdemeanor.
- No standalone Alabama automatic-renewal/negative-option statute exists as of this research. Alabama is commonly listed among states without a specific auto-renewal law.
- HB405 (proposed) would have required clear disclosure of auto-renewal terms and pre-renewal notice for renewal terms >1 month, with exemptions for financial institutions/utilities, and would void non-compliant auto-renewal clauses — **did not appear to have been enacted; verify current session status with counsel.**
- Absent a specific statute, deceptive autorenewal practices are addressed only via the ADTPA's general UDAP catchall and federal FTC ROSCA/Negative Option Rule.

## 4. Data Breach Notification — Payment Card Data
- Alabama Data Breach Notification Act of 2018 — Ala. Code §§ 8-38-1 through 8-38-12 (Act 2018-396, effective June 1, 2018).
- "Sensitive personally identifying information" (§ 8-38-2) includes name + financial account number (incl. credit/debit card number) in combination with any security code, access code, password, expiration date, or PIN needed to access the account or conduct a transaction — directly covers payment card data.
- Encryption/redaction safe harbor: data that is truncated, encrypted, secured, or otherwise rendered unusable does not trigger notification, unless the encryption key/credential is known or reasonably believed to have also been breached (§ 8-38-2).
- Covered entities must conduct a good-faith, prompt investigation upon discovery of a breach (§ 8-38-4).
- Individual notification: as expeditiously as possible, without unreasonable delay, and no later than 45 days after determination that a breach occurred and poses substantial harm (§ 8-38-5). Substitute notice permitted if affected individuals exceed 100,000. Law-enforcement delay provision available on written request.
- Attorney General notification required (§ 8-38-6) if the number of Alabama individuals notified exceeds 1,000; written notice due within the same 45-day window; must include synopsis of the breach and approximate number of affected individuals; AG-submitted info can be marked confidential/exempt from public-records disclosure.

## 5. State Law Referencing/Incorporating PCI-DSS
- No Alabama statute found that references, incorporates, or mandates PCI-DSS compliance. **Not confirmed — verify with counsel.**
- Unlike Nevada/Washington/Minnesota (which reference PCI-DSS or create related safe harbors), Alabama does not appear to have codified PCI-DSS into state law.
- PCI-DSS compliance in Alabama remains a contractual obligation (card network/acquiring bank agreements), not a statutory one.

## 6. State-Specific EFT/ACH Statutes Beyond Reg E
- No standalone Alabama consumer EFT/ACH statute beyond federal EFTA/Reg E identified for general recurring payments. **Not confirmed — verify with counsel.**
- Alabama Monetary Transmission Act — Ala. Code Title 8, Chapter 7A — governs money transmitter licensing (including virtual/fiat currency transmission since 2017 amendment); regulates transmitters as businesses, not consumer EFT/ACH authorization or error-resolution rights.
- Alabama Uniform Electronic Transactions Act — Ala. Code Title 8, Chapter 1A — governs validity of electronic records/signatures generally, not payment-specific ACH rules.

## Sources
- [Ala. Code § 11-103-1 — Authorization of Payment by Credit Cards (Justia)](https://law.justia.com/codes/alabama/title-11/title-3/chapter-103/section-11-103-1/)
- [Ala. Code § 41-1-60 — Acceptance of Credit Card Payment (Justia)](https://law.justia.com/codes/alabama/title-41/chapter-1/article-4/section-41-1-60/)
- [Ala. Code § 41-1-60 (FindLaw)](https://codes.findlaw.com/al/title-41-state-government/al-code-sect-41-1-60/)
- [Ala. Code § 45-37-73.01 — Regulation of Credit Card Payments (Justia)](https://law.justia.com/codes/alabama/title-45/chapter-37/article-7/part-4/section-45-37-73-01/)
- [Alabama Credit Card Surcharge Laws (2026) — Merchant Cost Consulting](https://merchantcostconsulting.com/lower-credit-card-processing-fees/alabama-surcharge-laws/)
- [Credit Card Surcharge Laws by State (2026) — Merchant Cost Consulting](https://merchantcostconsulting.com/lower-credit-card-processing-fees/credit-card-surcharge-laws-by-state/)
- [Alabama Excludes Credit Card Fees from Sales Tax Base 2026 — TaxCloud](https://taxcloud.com/sales-tax-radar/alabama-credit-card-fees-excluded-sales-tax-2026/)
- [Ala. Code §§ 8-19-1 through 8-19-15 — Deceptive Trade Practices Act (Justia)](https://law.justia.com/codes/alabama/title-8/chapter-19/)
- [Alabama Deceptive Trade Practices Act summary — CLIClaw](https://www.cliclaw.com/library/us-state-laws/alabama/alabama-deceptive-trade-practices-act-ala-code-%C2%A78-19-1-et-seq)
- [Auto-Renewals Laws by State chart](https://cdn2.hubspot.net/hubfs/547412/Subscription%20Business/Auto%20Renewals%20Laws%20VG2.pdf)
- [BillTrack50 — AL HB405](https://www.billtrack50.com/billdetail/856562)
- [Ala. Code Title 8, Chapter 38 — Data Breach Notification Act of 2018 (Justia)](https://law.justia.com/codes/alabama/title-8/chapter-38/)
- [Ala. Code § 8-38-2 — Definitions (Justia)](https://law.justia.com/codes/alabama/title-8/chapter-38/section-8-38-2/)
- [Ala. Code § 8-38-4 — Investigation (Justia)](https://law.justia.com/codes/alabama/title-8/chapter-38/section-8-38-4/)
- [Ala. Code § 8-38-5 — Notice to Individuals (Justia)](https://law.justia.com/codes/alabama/title-8/chapter-38/section-8-38-5/)
- [Ala. Code § 8-38-6 — Notice to Attorney General (Justia)](https://law.justia.com/codes/alabama/title-8/chapter-38/section-8-38-6/)
- [Alabama Attorney General — Data Breach Notification](https://www.alabamaag.gov/data-breach-notification/)
- [Alabama Data Breach Notification Act of 2018 (Act 2018-396, PDF)](https://www.alabamaag.gov/wp-content/uploads/2023/08/Act-2018-396.pdf)
- [Alabama Monetary Transmission Act, Title 8 Ch. 7A (LawServer)](https://www.lawserver.com/law/state/alabama/al-code/alabama_code_title_8_chapter_7a)
- [Alabama Uniform Electronic Transactions Act, Title 8 Ch. 1A (LawServer)](https://www.lawserver.com/law/state/alabama/al-code/alabama_code_title_8_chapter_1a)
