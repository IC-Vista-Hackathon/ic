# New Mexico — Payment Compliance Reference

> Internal reference only. Not legal advice. Verify with counsel before relying on any point below.

## 1. Autopay/Recurring Payment Funding Source Restrictions (Credit Card vs. ACH)

- No NM-specific statute found restricting recurring/autopay funding to ACH/bank debit only (no FL-style carve-out identified for any industry: utilities, insurance, etc.).
- NM utilities (e.g., NM Gas Co.) do differentiate fees by payment method (ACH free, card/other methods incur a fee) — this appears to be **business practice**, not a statutory mandate.
- Not confirmed — verify with counsel whether any NM PRC rule or industry-specific regulation (utilities, insurance) imposes a funding-source restriction on autopay.

## 2. Credit Card Surcharge / Convenience Fee Law

- New Mexico has **no general statute prohibiting credit card surcharges** by private businesses; no state-mandated surcharge cap below card network limits (network limits typically 4%).
- NMSA 1978 § 6-10-1.2 ("Payment methods authorized; fee") authorizes **state agencies and local governing bodies** to charge a uniform convenience fee to cover processing costs for credit card/electronic transfer payments — this section applies to government entities, not private merchants.
- Debit card surcharging is illegal nationwide (Reg II / network rules), not a distinct NM statute.
- No NM-specific surcharge disclosure statute identified for private-sector merchants — Not confirmed; verify with counsel on notice/disclosure requirements under general UPA principles.

## 3. NM Unfair Practices Act (UPA) — Recurring Billing / Negative Option / Autorenewal

- Citation: NMSA 1978 §§ 57-12-1 through 57-12-24 ("Unfair Practices Act").
  - § 57-12-1 — short title.
  - § 57-12-2 — definitions, incl. "unfair or deceptive trade practice" and "unconscionable trade practice."
- No autorenewal/negative-option-specific provision found within the general UPA (Ch. 57, Art. 12) itself.
- NM does have a **separate autorenewal statute specific to insurance service contracts**: NMSA 1978 § 59A-58-10.1 ("Automatic renewal; notice"), part of the Insurance Code, Service Contract Regulation (Art. 58):
  - Requires clear/conspicuous disclosure of auto-renewal terms at time of consent.
  - Requires cancellation notice 30–60 days before the non-renewal deadline, in writing (mail or email with consumer consent).
  - Implementing regulation: 12.2.11 NMAC ("Automatic Renewal of Service Contracts").
- This insurance-specific auto-renewal statute is narrower than a general consumer autorenewal law; scope is limited to service contracts under the Insurance Code — Not confirmed whether it extends to payment-processing/subscription billing generally. Verify applicability with counsel.
- General deceptive-recurring-billing conduct (e.g., failure to disclose material terms of a recurring charge) would likely be analyzed under the general UPA §§ 57-12-1/-2/-3 deceptive/unconscionable practice provisions — Not confirmed as applied; verify with counsel.

## 4. Data Breach Notification Law — Payment Card Data

- Citation confirmed: NMSA 1978 §§ 57-12C-1 through 57-12C-12, the "Data Breach Notification Act" (enacted 2017).
- "Personal identifying information" under the Act includes an account number, credit card number, or debit card number in combination with any required security code, access code, or password permitting access to a financial account (§ 57-12C-6 area — confirm exact subsection with counsel).
- Notification timing: most expedient time possible, not later than 45 calendar days after discovery of breach (§ 57-12C-6).
- Risk-of-harm exception: notification not required if investigation determines no significant risk of identity theft or fraud.
- Required notice content specified in § 57-12C-7 (name/contact of notifying person, types of PII involved, breach date/estimate, description of incident, credit bureau contact info, advice to monitor accounts/credit reports).
- Enforcement: NM Attorney General may bring action; civil penalties up to $25,000 per violation per NMSA 1978 § 57-12C-11 (confirm per-incident vs. per-violation calculation with counsel).

## 5. PCI-DSS Reference / Card Data Retention Requirements in NM Law

- Not confirmed — no NM statute found that expressly references or incorporates PCI-DSS as a legal requirement.
- NM data-security/disposal provisions (within the Data Breach Notification Act framework) require reasonable security procedures and require PII (which can include payment card data) to be shredded/erased/made unreadable when no longer reasonably needed for business purposes — a general "reasonably needed" standard, not a specific retention period and not a PCI-DSS citation.
- Verify with counsel whether any NM administrative rule (e.g., NMDFA payment card acceptance policy for state agencies) independently mandates PCI-DSS compliance — note NMDFA's own PCI contract-clause guidance applies to state agencies, not to private-sector payment processors generally.

## 6. NM EFT/ACH-Specific Statutes Beyond Federal Reg E

- No standalone New Mexico "state EFTA" found; NM statutes reference/defer to the federal Electronic Fund Transfer Act (15 U.S.C. § 1693 et seq. / Reg E).
- NMSA 1978 § 55-4A-108 (UCC Article 4A, Funds Transfers) — excludes from Article 4A any funds transfer governed in any part by the federal EFTA, with a stated exception in subsection (b); relevant to wholesale/wire transfers, not consumer ACH broadly.
- NMSA 1978 § 6-10-1.2 and § 6-10-63 — govern public-body acceptance of electronic fund transfers/credit card payments; government-payments context only, not a general consumer EFT statute.
- No NM-specific consumer ACH authorization/revocation statute beyond Reg E identified — Not confirmed; verify with counsel.

## Sources

- https://law.justia.com/codes/new-mexico/2006/nmrc/jd_57-12-1-130ef.html
- https://law.justia.com/codes/new-mexico/2006/nmrc/jd_57-12-2-cbf9.html
- https://www.cabq.gov/cable-franchise/documents/unfair-trade-practices_57-12-1-to-4.pdf
- https://law.justia.com/codes/new-mexico/chapter-57/article-12c/section-57-12c-1/
- https://law.justia.com/codes/new-mexico/chapter-57/article-12c/section-57-12c-6/
- https://law.justia.com/codes/new-mexico/chapter-57/article-12c/section-57-12c-7/
- https://www.dwt.com/gcp/states/new-mexico
- https://www.cliclaw.com/library/new-mexico-data-breach-notification-nm-stat-%C2%A7-57-12c-1-%C2%A7-57-12c-12/
- https://law.justia.com/codes/new-mexico/chapter-6/article-10/section-6-10-1-2/
- https://merchantcostconsulting.com/lower-credit-card-processing-fees/new-mexico-credit-card-surcharge-laws/
- https://www.getflexpoint.com/credit-card-surcharging-us-states/new-mexico
- https://www.nmgco.com/en/Pay_Electronically
- https://law.justia.com/codes/new-mexico/chapter-59a/article-58/section-59a-58-10-1/
- https://www.law.cornell.edu/regulations/new-mexico/title-12/chapter-2/part-11
- https://law.justia.com/codes/new-mexico/2021/chapter-55/article-4a/part-1/section-55-4a-108/
- https://www.nmdfa.state.nm.us/board-of-finance/payment-card-acceptance/
- https://www.nmdfa.state.nm.us/wp-content/uploads/2020/12/Milestone-Two-12_8-9-Managing-Service-providers-PCI__Recommended_Contract_Clauses.pdf
