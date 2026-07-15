# Federal Payment Notification Law — Compliance Reference

> Internal reference only — not legal advice. Verify with counsel before acting on any item below.

## 1. Federal Data Breach Notification

- **No general federal breach notification statute.** All 50 states, DC, and 3 territories have their own breach laws (as of 6/2026); compliance is state-by-state. Federal law only touches breach notice in specific sectors.
- **GLBA Safeguards Rule (16 CFR 314)** — amendment effective 5/13/2024 added mandatory breach notice for non-bank financial institutions (covers payments/fintech entities under FTC jurisdiction):
  - Trigger: "notification event" = unauthorized acquisition of unencrypted customer information affecting **≥500 consumers**.
  - Must notify **FTC** electronically via form at ftc.gov, "as soon as possible, and no later than 30 days after discovery."
  - Also requires a written incident response plan (314.4(h)) — separate from the notice trigger.
  - Not confirmed — verify with counsel: whether InvoiceCloud entities are in-scope "financial institutions" under GLBA's definition (nonbank companies significantly engaged in financial activities).
- **FTC Health Breach Notification Rule (16 CFR 318)** — scoped to vendors of personal health records / related apps not covered by HIPAA. Not applicable to standard payment processing; flag only if health-payment products (e.g., FSA/HSA-linked tools) are in scope. Not confirmed — verify applicability if such a product exists.
- **Card network rules (Visa/Mastercard/Amex/Discover) mandating breach reporting are contractual (merchant/acquirer agreements), not federal law.** Keep tracked separately from statutory obligations; PCI DSS + network rules impose their own notification/forensics timelines independent of GLBA/state law.
- **Failed/proposed federal bills**: multiple general federal breach-notification bills (e.g., various Data Security and Breach Notification Acts) have been introduced in Congress over the years without passage; none currently in force. Not confirmed — no active bill tracked as of this research; re-check if legislative status matters for a specific decision.

## 2. Regulation E (12 CFR 1005) — Electronic Fund Transfers

- **Initial disclosure** (1005.7): required at the time consumer contracts for EFT service, or before the first EFT — includes error-resolution notice substantially similar to Model Form A-3.
- **Change-in-terms / error-resolution notice** (1005.8): governs notice when terms change and annual/periodic error-resolution notice content.
- **Preauthorized transfer varying in amount** (1005.10(d)(1)): payee or institution must send **written notice ≥10 days before** the scheduled transfer date when amount varies from the prior transfer/authorized amount. Alternative: consumer can opt into notice only when amount falls outside a pre-agreed range (range must be one the consumer could reasonably anticipate, disclosed at authorization time). Institution not liable if payee fails to send the notice.
- **Error resolution timing** (1005.11):
  - Investigate and determine error status within **10 business days** of receiving notice of error; report results within 3 business days after completing investigation.
  - **20 business days** in place of 10, if the EFT in question occurred within 30 days after the first deposit to a new account.
  - If investigation can't complete in 10 (or 20) business days: up to **45 days** total allowed, provided provisional credit is given to the consumer within the original 10 (or 20) day window.
  - **90 days** (in place of 45) applies to certain transfer categories (e.g., new accounts, POS, and foreign-initiated transactions per 1005.11(c)(3)). Not confirmed — verify current sub-category triggers against the reg text before relying on the 90-day figure.

## 3. Regulation Z (12 CFR 1026) — Credit Cards

- **Periodic billing statement**: card issuer must mail/deliver statement **≥21 days** before the payment due date (1026.5 / .7 read with .9 reasonable-procedures standard).
- **Change-in-terms notice** (1026.9(c)): **45 days'** advance written notice required before a significant change in account terms (e.g., rate increases, fee increases) takes effect for open-end (not home-secured) plans.
  - Also **45 days'** notice specifically for penalty/delinquency rate increases, and for credit-limit-reduction-triggered over-limit fee/penalty rate impositions.
  - No advance-notice requirement when extending a grace period or reducing a charge (consumer-favorable changes).
- **Billing error dispute rights** (1026.13):
  - Consumer must send written notice within **60 days** after the creditor transmitted the first statement reflecting the alleged error.
  - Creditor must acknowledge in writing within **30 days** of receiving the notice (unless resolved within that window).
  - Creditor must resolve within **2 complete billing cycles, not to exceed 90 days**, after receiving the notice.
  - Consumer not required to pay (and creditor can't collect) the disputed amount while pending.

## 4. ROSCA (15 U.S.C. §8401 et seq., primarily §8403)

- Applies to negative-option / recurring-charge sales effected **on the Internet**.
- Before obtaining billing information: seller must **clearly and conspicuously disclose all material terms** of the negative-option feature.
- Before charging: must obtain consumer's **express informed consent** — an affirmative action unambiguously manifesting agreement to the specific terms after clear disclosure. A pre-checked box does **not** satisfy this.
- Must provide a **simple mechanism** for the consumer to stop recurring charges (cancel).
- High FTC enforcement priority for subscription/auto-renewal/free-trial billing flows.

## 5. FTC "Click-to-Cancel" Negative Option Rule — Status as of research date (2026-07-14)

- 2024 amendments (broader "click-to-cancel" rule) were **vacated by the Eighth Circuit on 7/8/2025** — FTC found to have skipped a required Section 22 preliminary regulatory analysis after the rule's cost impact exceeded $100M/year.
- As of 2026, the 2024 amendments are **not in effect**. In **February 2026** the FTC formally reverted the Negative Option Rule to its **pre-2024 version** (the older, narrower "negative option" rule, not the vacated click-to-cancel version).
- FTC has **restarted rulemaking**: submitted a draft ANPRM to OIRA (announced 1/30/2026), published in the Federal Register as "Rule Concerning the Use of Prenotification Negative Option Plans" — comment period ended **4/13/2026**.
- **Practical implication**: no federal "click-to-cancel" mandate currently in force; ROSCA (§4 above) and the FTC Act's general unfair/deceptive practices authority remain the live federal enforcement tools for cancellation-friction complaints. Not confirmed — verify current rule text and any new final rule before assuming no obligations apply; this is an active/moving rulemaking.

## 6. NACHA Operating Rules — Notification of Change (NOC)

- NACHA rules are **private network rules**, not federal law — binding via ODFI/RDFI participation agreements, not statute.
- **Notification of Change (NOC)**: zero-dollar entry sent by the RDFI to the ODFI/Originator flagging incorrect account/routing info that needs correction.
- Originator must implement the NOC-requested change within **6 banking days** of receipt, or **before initiating the next entry** to that receiver's account, whichever is later.
- Recent minor rule change: Originators now have discretion to apply NOC changes to a **Single (One-Time) Entry** regardless of SEC code (previously more restrictive).
- Return notification timing: Not confirmed — verify current return-entry timeframes (standard vs. unauthorized-return windows) directly against the current Nacha Operating Rules book before relying on a specific day count; not independently verified in this pass.

## Sources

- [16 CFR Part 314 — Standards for Safeguarding Customer Information (eCFR)](https://www.ecfr.gov/current/title-16/chapter-I/subchapter-C/part-314)
- [FTC Safeguards Rule amendment — Federal Register](https://www.federalregister.gov/documents/2023/11/13/2023-24412/standards-for-safeguarding-customer-information)
- [FTC Safeguards Rule notification requirement — Covington & Burling summary](https://www.cov.com/en/news-and-insights/insights/2023/11/ftc-finalizes-new-notification-requirement-for-glba-safeguards-rule)
- [12 CFR 1005.7 — Initial disclosures (eCFR)](https://www.ecfr.gov/current/title-12/chapter-X/part-1005/subpart-A/section-1005.7)
- [12 CFR 1005.10 — Preauthorized transfers (CFPB)](https://www.consumerfinance.gov/rules-policy/regulations/1005/10/)
- [12 CFR 1005.11 — Procedures for resolving errors (CFPB)](https://www.consumerfinance.gov/rules-policy/regulations/1005/11/)
- [12 CFR 1026.9 — Subsequent disclosure requirements (CFPB)](https://www.consumerfinance.gov/rules-policy/regulations/1026/9/)
- [12 CFR 1026.13 — Billing error resolution (CFPB)](https://www.consumerfinance.gov/rules-policy/regulations/1026/13/)
- [15 U.S.C. §8403 — Negative option marketing on the Internet (Cornell LII)](https://www.law.cornell.edu/uscode/text/15/8403)
- [FTC Negative Option Rule text](https://www.ftc.gov/system/files/ftc_gov/pdf/p064202_negative_option_rule.pdf)
- [Federal Register — Negative Option Rule (2024 amendments)](https://www.federalregister.gov/documents/2024/11/15/2024-25534/negative-option-rule)
- [Federal Register — Prenotification Negative Option Plans ANPRM (2026)](https://www.federalregister.gov/documents/2026/03/13/2026-04952/rule-concerning-the-use-of-prenotification-negative-option-plans)
- [Crowell & Moring — Eighth Circuit vacatur of click-to-cancel rule, FTC revival efforts](https://www.crowell.com/en/insights/client-alerts/clicking-all-the-right-boxes-ftc-moves-to-revive-click-to-cancel-rule-following-eighth-circuit-vacatur)
- [Federal Register — Revision of Negative Option Rule reverting to pre-2024 version (2/12/2026)](https://www.federalregister.gov/documents/2026/02/12/2026-02866/revision-of-the-negative-option-rule-withdrawal-of-the-cars-rule-removal-of-the-non-compete-rule-to)
- [FTC Health Breach Notification Rule, 16 CFR 318 (eCFR)](https://www.ecfr.gov/current/title-16/chapter-I/subchapter-C/part-318)
- [Nacha — Minor Rules Topics](https://www.nacha.org/rules/minor-rules-topics-2)
- [Nacha Operating Rules Updates and Reminders 2026 (AccessUnited)](https://www.accessunited.com/assets/files/44hKQQ5j)
