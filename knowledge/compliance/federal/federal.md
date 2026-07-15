# U.S. Federal Payments Compliance Reference

> **Not legal advice.** Informational research for internal compliance reference only. Review by qualified counsel required before relying on any of this for policy, product, or contractual decisions.

---

## 1. Regulation E (EFTA, 12 CFR Part 1005) — Recurring EFT/ACH Authorization

Governs electronic fund transfers from **consumer bank/deposit accounts** (not credit cards).

- **Written authorization required** — preauthorized EFTs from a consumer account may be authorized "only by a writing signed or similarly authenticated" by the consumer; issuer must give the consumer a copy. (12 CFR § 1005.10(b))
  - Electronic signature is acceptable but must comply with the E-SIGN Act.
  - A payee cannot sign on the consumer's behalf based on only oral authorization.
  - The account-holding financial institution is not liable if a third-party payee fails to obtain/document authorization — the payee is in violation, not the bank.
- **Right to stop payment** — consumer may stop a preauthorized transfer by notifying the institution orally or in writing at least 3 business days before the scheduled transfer date. Institution may require written confirmation of an oral stop-payment order within 14 days; oral order lapses if confirmation isn't provided. (§ 1005.10(c))
- **Notice requirements** — institutions must provide a summary of the stop-payment right and procedure. (§ 1005.10(c))
- **Error resolution (§ 1005.11)** — consumer must notify of an error within 60 days of the statement first reflecting it; institution must investigate within 10 business days (extendable to 45 calendar days under conditions); burden of proof that a transfer was authorized is on the institution.
- **Unauthorized-transfer liability (§ 1005.6)** — institution may withhold provisional credit of at most $50 pending investigation if it has a reasonable basis to believe the transfer was unauthorized and has met § 1005.6(a) requirements.
- **Scope limit:** Reg E does **not** apply to credit card transactions — those fall under Reg Z instead.

---

## 2. NACHA Operating Rules — ACH Debit Authorization (Contractual, Not Federal Statute)

NACHA rules are a **private-sector contractual rulebook** administered by Nacha (National Automated Clearing House Association), binding on ACH network participants via agreement — not federal law. They typically require more than Reg E's floor and are the practical operative standard for ACH originators.

- **Authorization by transaction type** — Originators must obtain authorization appropriate to entry type (e.g., WEB for internet-initiated consumer debits, TEL for phone-initiated, PPD for recurring consumer debits); recurring/preauthorized debits require the Reg E-equivalent "signed or similarly authenticated" writing.
- **Record retention** — Originator must retain the signed/authenticated authorization for **2 years following termination or revocation** of the authorization; oral authorizations require either an audio recording or written confirmation sent to the receiver before settlement, retained for 2 years from the date of authorization.
- **2026 rule changes:**
  - Standardized Company Entry Descriptions — Originators must use "PURCHASE" for e-commerce consumer debits, "PAYROLL" for payroll (effective March 20, 2026).
  - Account validation now required for first-time use of a consumer account number in a WEB debit (and any new account, even for existing payers), effective March 2026 — a proven prior-payment history on that account number satisfies validation.
  - Expanded fraud-monitoring obligation: network participants must implement risk-based processes to detect fraudulent/unauthorized outgoing ACH entries.
- **Risk/authorization rules** generally require Originators to have commercially reasonable fraud-detection systems for WEB debits.

---

## 3. Regulation Z (TILA, 12 CFR Part 1026) — Credit Card Recurring Billing & Disputes

Governs open-end consumer credit, including credit cards.

- **No Reg-Z equivalent to Reg E's "written authorization" mandate** for recurring credit card charges. Recurring-charge consent/disclosure obligations for cards come primarily from **card network rules** (Visa/Mastercard), not directly from Reg Z — see Section 6.
- **Billing error resolution (§ 1026.13)** — consumer must notify the creditor of a billing error within 60 days of the first statement reflecting it; creditor must acknowledge within 30 days and resolve within two complete billing cycles (not to exceed 90 days); consumer need not pay, and creditor may not attempt to collect, the disputed amount during resolution; prohibited collection actions include lawsuits, liens, attachment.
- **Unauthorized-use liability cap (§ 1026.12)** — cardholder liability for unauthorized use is capped at the lesser of $50 or the amount obtained before notification to the issuer.
- **Billing-dispute "claims and defenses" right (§ 1026.12(c))** — cardholder may assert against the card issuer claims/defenses related to a merchant's failure to resolve a dispute over goods/services (subject to dollar/distance thresholds under TILA's underlying statute).

---

## 4. TILA / Regulation Z Interplay With Recurring Credit Card Billing

- TILA (15 U.S.C. § 1601 et seq.) is the enabling statute; Regulation Z is the CFPB's implementing regulation. Recurring credit card billing is treated as ordinary open-end credit extensions subject to periodic statement, billing-error, and unauthorized-use protections above — there is **no separate federal "recurring charge" authorization regime** for cards analogous to Reg E § 1005.10.
- Practical consequence: **credit-card-funded autopay is governed federally through dispute/error-resolution and liability-cap protections (after the fact)**, while **ACH-funded autopay is governed federally through an upfront written-authorization mandate (before the fact)** under Reg E. This is a real, verified federal asymmetry — not an omission in this research.

---

## 5. Card Network Rules for Recurring/Stored-Credential Transactions (Contractual, Not Federal Law)

Applicable to credit-card-funded autopay specifically; imposed by Visa/Mastercard via merchant agreements, not government regulation.

- **Stored Credential Framework** (effective Oct 2018, Visa amendment Apr 2020) — merchants must identify whether a transaction is cardholder-initiated (CIT) or merchant-initiated (MIT), and its frequency (one-time vs. recurring/scheduled), using standardized indicators in the authorization message.
- **Separate consent requirement** — Visa/Mastercard require merchants to obtain customer consent to store payment credentials, and that consent be presented separately from general terms and conditions (not bundled).
- **Transaction identifier / trace ID linkage** — Visa Transaction Identifier (TID, up to 15 digits) or Mastercard Trace ID (Financial Network Code + Banknet Reference Number + Settlement Date) from the initial CIT must be captured and reused on all subsequent MITs; missing/incorrect linkage risks data-integrity fines and authorization declines.

---

## 6. Federal-Specific Restrictions: Card-Funded vs. ACH-Funded Autopay

**Explicitly:** there is no dedicated federal statute or regulation governing "autopay" as such for either rail. Federal protection is assembled from the general frameworks above, and the two rails are **not symmetric**:

| | ACH-funded autopay | Card-funded autopay |
|---|---|---|
| Governing federal framework | Reg E / EFTA | Reg Z / TILA |
| Upfront authorization mandate | Yes — signed/similarly authenticated writing (§ 1005.10(b)) | No federal mandate; consent requirement comes from card network rules only |
| Revocation mechanism | Federal right, ≥3 business days' notice (§ 1005.10(c)) | No direct Reg Z equivalent; consumer relies on dispute rights (§ 1026.13) and issuer/network cancellation processes |
| Unauthorized-use liability | Reg E error-resolution regime, provisional credit up to $50 withholding | TILA $50 cap (§ 1026.12) |

If a product uses both rails, compliance controls should not be assumed portable between them — the ACH side needs a documented written authorization; the card side needs network-compliant stored-credential consent and dispute-handling readiness.

---

## 7. PCI-DSS — Industry Standard, Not Federal Law

- PCI-DSS is issued by the **PCI Security Standards Council (PCI SSC)**, a body formed by the major card networks (Visa, Mastercard, American Express, Discover, JCB). It is **not** a government law or regulation. It binds merchants/processors through **card brand operating rules and merchant/acquirer agreements** — contractual, not statutory, force.
- **State laws that reference or incorporate PCI-DSS by statute (verified, not exhaustive):**
  - **Nevada** — NRS § 603A.215: mandates that businesses accepting payment cards comply with the then-current PCI-DSS; compliant entities get a liability shield. Nevada is generally cited as the first state to require full PCI-DSS compliance by statute.
  - **Minnesota** — Minn. Stat. § 325E.64 ("Plastic Card Security Act"): does not name PCI-DSS directly but prohibits retaining certain payment card data (magnetic stripe/CVV/PIN) more than 48 hours after transaction authorization — substantively overlaps with PCI-DSS storage prohibitions.
  - **Washington** — 2010 Wash. Sess. Laws ch. 1055 (chaptered) / RCW provisions on data breach: incorporates PCI-DSS compliance as a liability shield (not a compliance mandate) — compliant entities are shielded from certain breach-related liability, non-compliant entities are not required to comply but lose the shield.
  - This list should be re-verified against current statutory text before citing externally; state privacy/security law changes frequently.

---

## 8. GLBA — Data Security Baseline Touching Payment Data

- Gramm-Leach-Bliley Act §§ 501, 505(b)(2), implemented by the FTC's **Safeguards Rule, 16 CFR Part 314**, for financial institutions under FTC jurisdiction (state member banks/credit unions have parallel rules from their own regulators).
- Requires a written, comprehensive information security program (administrative, technical, physical safeguards) scaled to size/complexity/data sensitivity, covering customer information including bank/credit card account numbers.
- **2021 amendment (effective 2023)** added specificity: access controls/least-privilege, encryption of customer data at rest and in transit, multi-factor authentication, and either continuous monitoring or annual penetration testing plus semiannual vulnerability scanning.
- GLBA is a **baseline security-program obligation**, distinct from and layered on top of PCI-DSS (contractual) and state breach-notification statutes.

---

## 9. FTC — ROSCA (Restore Online Shoppers' Confidence Act, 15 U.S.C. § 8401 et seq.)

Directly applicable to **autopay/subscription enrollment and cancellation** where a "negative option" feature is used online.

- **Three mandatory elements before charging a consumer** for an internet negative-option sale:
  1. Clear and conspicuous disclosure of all material terms before obtaining billing information.
  2. Express informed consent to the negative-option terms before the first charge (cannot be inferred from silence, pre-checked boxes, or passive acceptance).
  3. A simple cancellation mechanism — at least as easy as enrollment (the basis for the FTC's "click-to-cancel" enforcement posture; a 2024 FTC Negative Option Rule addressing this was finalized and has been subject to ongoing legal/regulatory developments into 2026 — verify current status before relying on it).
- **Enforcement**: FTC civil actions for redress, disgorgement, and civil penalties (statutory max adjusts annually; recent figure cited around $43,792/violation — confirm current adjusted amount before citing externally).
- Applies regardless of payment rail (ACH or card) whenever the enrollment is an internet negative-option/auto-renewal structure — this is the primary federal law governing the *enrollment and cancellation experience* for autopay, as distinct from Reg E/Reg Z which govern the *payment authorization and dispute mechanics*.

---

## Sources

- [12 CFR § 1005.10 – Preauthorized transfers (CFPB)](https://www.consumerfinance.gov/rules-policy/regulations/1005/10/)
- [12 CFR § 1005.10 (eCFR)](https://www.ecfr.gov/current/title-12/chapter-X/part-1005/subpart-A/section-1005.10)
- [12 CFR § 1005.10 (Cornell LII)](https://www.law.cornell.edu/cfr/text/12/1005.10)
- [Comment for 1005.10 Preauthorized Transfers (CFPB)](https://www.consumerfinance.gov/rules-policy/regulations/1005/Interp-10)
- [12 CFR § 1005.11 – Procedures for resolving errors (CFPB)](https://www.consumerfinance.gov/rules-policy/regulations/1005/11/)
- [12 CFR § 1005.11 (Cornell LII)](https://www.law.cornell.edu/cfr/text/12/1005.11)
- [12 CFR § 1005.6 – Liability of consumer for unauthorized transfers (eCFR)](https://www.ecfr.gov/current/title-12/chapter-X/part-1005/subpart-A/section-1005.6)
- [12 CFR Part 1005 – Electronic Fund Transfers (Regulation E) (CFPB)](https://www.consumerfinance.gov/rules-policy/regulations/1005/)
- [Nacha – Supplementing Fraud Detection Standards for WEB Debits](https://www.nacha.org/rules/supplementing-fraud-detection-standards-web-debits)
- [Nacha – Risk Management Topics: Company Entry Descriptions](https://www.nacha.org/rules/risk-management-topics-company-entry-descriptions)
- [Nacha – New Rules index](https://www.nacha.org/newrules)
- [Think Nacha Doesn't Apply to You? The 2026 WEB Debit Rule (Validifi)](https://validifi.com/think-nacha-doesnt-apply-to-you-think-again-the-2026-web-debit-rule-impacts-everyone/)
- [2026 Nacha Rule Changes (Regions Bank)](https://www.regions.com/insights/commercial/article/2026-nacha-rule-changes)
- [Proof of Authorization and NACHA requirements (Forte Support)](https://support.forte.net/support/solutions/articles/11000119211-proof-of-authorization-and-nacha-requirements)
- [Nacha Rules and Compliance: A Guide for Businesses (Stripe)](https://stripe.com/resources/more/nacha-rules-explained)
- [12 CFR § 1026.13 – Billing error resolution (CFPB)](https://www.consumerfinance.gov/rules-policy/regulations/1026/13/)
- [12 CFR § 1026.13 (Cornell LII)](https://www.law.cornell.edu/cfr/text/12/1026.13)
- [12 CFR § 1026.12 – Special credit card provisions (CFPB)](https://www.consumerfinance.gov/rules-policy/regulations/1026/12/)
- [12 CFR § 1026.12 (Cornell LII)](https://www.law.cornell.edu/cfr/text/12/1026.12)
- [12 CFR Part 1026 – Truth in Lending (Regulation Z) (CFPB)](https://www.consumerfinance.gov/rules-policy/regulations/1026/)
- [CFPB – I was asked to sign an "ACH authorization"...](https://www.consumerfinance.gov/ask-cfpb/i-was-asked-to-sign-an-ach-authorization-to-allow-electronic-access-to-my-account-to-repay-a-payday-loan-what-is-that-en-1569/)
- [Visa and Mastercard Stored Credential Transaction Framework Mandate (CardPointe)](https://support.cardpointe.com/compliance/visa-stored-credential-transaction-framework-mandate/)
- [Boosting Authorization Success: Visa NTI and Mastercard Trace ID Mandates (MRC)](https://merchantriskcouncil.org/learning/resource-center/member-news/blog/2025/boosting-authorization-success-a-practical-guide-to-visa-nti-and-mastercard-trace-id-mandates)
- [Visa Stored Credential Transaction Framework (PDF)](https://usa.visa.com/content/dam/VCOM/global/support-legal/documents/stored-credential-transaction-framework-vbs-10-may-17.pdf)
- [NRS Chapter 603A – Security and Privacy of Personal Information (Nevada Legislature)](https://www.leg.state.nv.us/nrs/nrs-603a.html)
- [Nevada Updates Encryption Law and Mandates PCI DSS Compliance (Hunton, PDF)](https://www.hunton.com/media/legal/2072_nevada_updates_encryption_law.pdf)
- [The Minnesota Plastic Card Security Act (Fryberger Law Firm)](https://fryberger.com/articles/the-minnesota-plastic-card-security-act/)
- [FAQ on Washington State's PCI Law (InfoLawGroup)](https://www.infolawgroup.com/insights/2010/03/articles/payment-card-breach-laws/faq-on-washington-states-pci-law)
- [PCI DSS – Wikipedia (background/context only)](https://en.wikipedia.org/wiki/Payment_Card_Industry_Data_Security_Standard)
- [eCFR :: 16 CFR Part 314 – Standards for Safeguarding Customer Information](https://www.ecfr.gov/current/title-16/chapter-I/subchapter-C/part-314)
- [Gramm-Leach-Bliley Act (FTC)](https://www.ftc.gov/business-guidance/privacy-security/gramm-leach-bliley-act)
- [Safeguards Rule (FTC)](https://www.ftc.gov/legal-library/browse/rules/safeguards-rule)
- [Restore Online Shoppers' Confidence Act (FTC statute page)](https://www.ftc.gov/legal-library/browse/statutes/restore-online-shoppers-confidence-act)
- [PUBLIC LAW 111–345 — Restore Online Shoppers' Confidence Act (Congress.gov, PDF)](https://www.congress.gov/111/plaws/publ345/PLAW-111publ345.pdf)
- [Federal Register – Negative Option Rule (2024)](https://www.federalregister.gov/documents/2024/11/15/2024-25534/negative-option-rule)
- [FTC's "Click-to-Cancel" Rule Gets New Life (Goodwin, 2026)](https://www.goodwinlaw.com/en/insights/publications/2026/02/alerts-practices-ba-ftcs-click-to-cancel-rule-gets-new-life)
- [How to Comply w/ FTC ROSCA (FTC Defense Lawyer)](https://ftcdefenselawyer.com/restore-online-shoppers-confidence-act-rosca/)
