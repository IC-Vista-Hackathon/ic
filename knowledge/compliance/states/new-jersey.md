# New Jersey — Payment Compliance Reference

**Internal reference only. Not legal advice. Verify with counsel before relying on any statement below.**

## 1. Autopay/Recurring Payment Funding Method (Credit Card vs. ACH)

- No NJ-specific statute found restricting recurring/autopay authorization to ACH/bank debit only (no NJ analog to the FL-style utility/insurance ACH-only autopay restriction was located).
- NJ's automatic-renewal law (P.L.2022, c.91, N.J.S.A. 56:12-95.1 et seq., see Topic 3) governs *disclosure/cancellation* of auto-renewing contracts, not *funding method*.
- Not confirmed — verify with counsel: whether any NJ industry-specific regulation (e.g., insurance premium finance, NJ BPU-regulated utilities) restricts card-funded autopay. No such provision was found in general search of njleg.state.nj.us, NJ Consumer Affairs, or NJ BPU sources during this research pass.

## 2. Credit Card Surcharge / Convenience Fee Law

- Current law: **N.J.S.A. 56:8-156.1** (definitions) and **56:8-156.2** (prohibition), enacted P.L.2023, c.146 (signed Aug 2023).
- Core rule: a seller may not impose a surcharge on a credit card transaction greater than the seller's actual cost to process that credit card payment. Applies to credit cards only — not debit/prepaid.
- Disclosure required before charging (point-of-sale signage; menu disclosure for restaurants; checkout-page notice online; verbal notice for phone transactions).
- Violations are unlawful practices enforceable via NJ consumer protection law (Division of Consumer Affairs may inspect records).
- Litigation history correction: the task brief referenced *Dana's Railroad Supply v. Attorney General* as NJ precedent — **this is a Florida case** (11th Cir., 807 F.3d 1235 (2015)), striking down Florida's no-surcharge statute on First Amendment grounds. It is not a New Jersey case. No NJ Supreme Court/Third Circuit case with that name was found.
- The citation **N.J.S.A. 56:12-18** referenced in the task brief as the "historical" surcharge statute could not be confirmed to exist or relate to surcharges — not confirmed, do not rely on it. Current governing citations are 56:8-156.1/-156.2 (post-2023).
- Not confirmed: whether NJ had an older, separate no-surcharge statute pre-2023 that was struck down/repealed distinct from the 56:8-156.x scheme — verify with counsel/NJ Legislature historical statute archive.

## 3. NJ Consumer Fraud Act / UDAP — Recurring Billing & Auto-Renewal

- General UDAP statute: **N.J.S.A. 56:8-1 et seq.** (Consumer Fraud Act), core unlawful-practice provision at **N.J.S.A. 56:8-2**.
- Automatic-renewal / continuous-service statute: **N.J.S.A. 56:12-95.1 et seq.** (P.L.2022, c.91) — NOT 56:8-2.26 as referenced in the task brief; that citation was not confirmed and appears incorrect (search only returned it associated with proposed/other bills, not an enacted CFA auto-renewal section).
  - **56:12-95.5**: written/electronic notice of auto-renewal required 30–60 days before the cancellation deadline for contracts with an initial term of 12+ months that auto-renew for more than one month; notice must disclose the auto-renewal and cancellation method (online + mailing address, or phone number).
  - Related obligations in the same series: prior affirmative consent to the auto-renewal/continuous-service charge, acknowledgment of renewal terms and cancellation method, cancellation must be honored (ack within 5 business days, effective within 10 business days), refund of unearned amounts on non-compliance; goods sent without consent may be deemed an unconditional gift.
  - A violation of the 56:12-95 series is an unlawful practice under the Consumer Fraud Act (56:8-1 et seq.).
- Negative-option marketing generally: treated under CFA's broad "unconscionable commercial practice" / omission standard (56:8-2); no separate NJ negative-option statute beyond the auto-renewal provisions above was identified.

## 4. Data Breach Notification (Payment Card Data)

- **N.J.S.A. 56:8-163** — "Disclosure of breach of security to customers," part of the NJ Identity Theft Prevention Act, **N.J.S.A. 56:8-161 et seq.** (definitions at 56:8-161).
- Applies to any business conducting business in NJ (or public entity) that compiles/maintains computerized records with "personal information" of NJ residents.
- Notification required "in the most expedient time possible and without unreasonable delay," subject to law-enforcement delay and time needed to determine scope/restore integrity.
- Encryption safe harbor: breach = unauthorized access to data not secured by encryption or equivalent unreadable/unusable rendering.
- Risk-of-harm exception: no notice required if business establishes misuse of the information is not reasonably possible.
- Third-party processors must notify the data owner, who then notifies NJ customers.
- Large-breach rule: notice affecting 1,000+ persons at once also requires notifying nationwide consumer reporting agencies without unreasonable delay.
- Not confirmed in this pass: exact definition of "personal information" (whether it explicitly enumerates payment card number + security code/PIN) — verify text of 56:8-161 directly with counsel before relying on scope.

## 5. Card Data Retention / Receipt Truncation (PCI-DSS-Adjacent NJ Law)

- **N.J.S.A. 56:11-17** — "Personal identification information not required for credit card transaction" (L.1990, c.72, s.2).
- Actual text/effect: prohibits a merchant from requiring a cardholder to provide personal identification information (e.g., address, telephone number) beyond what the card issuer requires to complete the transaction, as a condition of completing a credit-card sale. A narrow exception permits collecting a phone number when the issuer does not require transaction authorization.
- Correction to task brief assumption: **56:11-17 does NOT itself address receipt truncation, printing of full card number/expiration date, or retention of magstripe/CVV data** — that assumption in the task brief is not supported by the statute text retrieved. The task brief's premise ("limiting card data retention akin to PCI-DSS") appears to describe a different or additional NJ provision.
- Not confirmed — verify with counsel: the correct citation for NJ's receipt-truncation / no-full-PAN-or-expiration-date-on-receipt rule and any statute restricting retention of track/magstripe or CVV data post-authorization. This may sit elsewhere in Title 56 (Consumer Affairs) — was not located and confirmed during this research pass. Do not cite 56:11-17 for receipt-truncation or CVV-retention claims without further verification.

## 6. NJ EFT/ACH-Specific Statutes Beyond Federal Reg E

- **N.J.S.A. 12A:4A-1 et seq.** — New Jersey's enactment of UCC Article 4A (Funds Transfers), governs wholesale/wire funds transfers between banks; **N.J.S.A. 12A:4A-108** expressly carves out transactions covered by the federal Electronic Fund Transfer Act (15 U.S.C. § 1693 et seq.) from Article 4A — i.e., NJ's 4A law is secondary/gap-filling relative to federal Reg E-covered consumer EFTs, not an independent consumer-facing overlay.
- Government Electronic Payment Acceptance Act, P.L.1995, c.325 — authorizes NJ local governments to accept electronic payments; administrative rules at **N.J.A.C. 5:30-9 / 9A** (local government EFT technology/internal controls) and **N.J.A.C. 18:2-3** (Division of Taxation EFT payment requirements) — these are state/local government payment-acceptance mechanics, not consumer protections.
- Not confirmed: any NJ statute imposing consumer-facing EFT/ACH authorization, disallowance, or dispute-resolution requirements beyond what federal Reg E already requires. No such standalone NJ consumer-EFT statute was identified in this pass — verify with counsel if ACH-specific compliance obligations are needed beyond Reg E/NACHA rules.

## Sources

- https://law.justia.com/codes/new-jersey/title-56/section-56-11-17/
- https://law.justia.com/codes/new-jersey/title-56/section-56-8-156-2/
- https://law.justia.com/codes/new-jersey/title-56/section-56-8-156-1/
- https://law.justia.com/codes/new-jersey/title-56/section-56-12-95-5/
- https://law.justia.com/codes/new-jersey/title-56/section-56-8-163/
- https://law.justia.com/codes/new-jersey/title-56/section-56-8-161/
- https://law.justia.com/codes/new-jersey/title-56/section-56-8-2/
- https://www.njconsumeraffairs.gov/News/Consumer%20Briefs/credit-card-surcharges-faq.pdf (fetch failed to render — PDF binary; not usable as confirmed source, listed for reference only)
- https://www.nj.gov/governor/news/news/562023/approved/20230818a.shtml
- https://pub.njleg.state.nj.us/Bills/2022/PL23/146_.PDF
- https://www.hklaw.com/en/insights/publications/2023/08/new-jersey-acts-to-limit-credit-card-surcharges
- https://law.justia.com/codes/new-jersey/2013/title-12a/section-12a-4a-108
- https://www.nj.gov/treasury/revenue/eft1.shtml
- Dana's Railroad Supply v. Attorney General, 807 F.3d 1235 (11th Cir. 2015) — confirmed via search to be a Florida case, cited here only to correct the task brief's mistaken attribution to New Jersey.
