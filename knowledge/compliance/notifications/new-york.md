# New York — Notification Laws (Internal Compliance Reference)

> **Not legal advice.** Internal reference only — verify all obligations with counsel before acting. Items not independently confirmed against primary sources are flagged below.

## 1. Data Breach Notification Law

**Statute:** SHIELD Act, N.Y. Gen. Bus. Law §§ 899-aa (notification) and 899-bb (data security program requirement).

- **Trigger:** Unauthorized acquisition (or reasonable belief thereof) of computerized data by a person without valid authorization, compromising the security, confidentiality, or integrity of "private information" of a NY resident. Good-faith employee access for business purposes is not a breach if the data isn't misused/further disclosed.
- **Deadline to notify affected consumers:** "In the most expedient time possible and without unreasonable delay," but no later than **30 days** after discovery (per 2025 amendment referenced in secondary sources — **confirm current statutory deadline text directly against § 899-aa before relying on the 30-day figure**, as some summaries describe this as a proposed/amended standard rather than settled long-standing text).
- **State AG / regulator notification:** If any NY residents are notified, must also notify the NY Attorney General, NYS Division of State Police, NYS Department of State, and NYS Department of Financial Services (DFS) — timing, content, and distribution of notices plus approximate number of affected persons. No minimum size threshold for this notice.
- **Consumer reporting agency (CRA) notification:** Required if **more than 5,000** NY residents are notified at one time (timing/content/distribution + approximate number affected).
- **Exemption determination filing:** If a business determines notification is not required (e.g., no reasonable likelihood of harm) and the incident affects **over 500** NY residents, it must provide its written determination to the NY AG within 10 days of that determination.
- **"Private information" — payment card coverage:** Yes, explicitly covered. Definition includes "account number, credit or debit card number, together with any required security code, access code, password or other information that would permit access to an individual's financial account," OR the account/card number alone "if circumstances exist in which such number could be used to access an individual's financial account without additional identifying information, security code, access code, or password."
- **Encryption safe harbor:** Yes. Encrypted data elements (data-at-rest and in-transit) fall outside the "private information" definition **unless** the encryption key was also accessed/acquired. Note: the § 899-bb reasonable-safeguards obligation applies regardless of encryption status.
- **Penalties:** Civil penalty for failure to provide timely notification — greater of $5,000 or $20 per instance of failed notification, capped at $250,000 (enforced by NY AG).
- **Private right of action:** Not confirmed — secondary sources describe AG enforcement only; no PRA identified in the reviewed text. **Verify with counsel.**

### DFS-Regulated Entities — 23 NYCRR 500
For entities licensed/regulated by DFS (banks, insurers, money transmitters, mortgage lenders, etc.) — separate and additional to GBL 899-aa:
- **§ 500.17(a):** Must notify DFS Superintendent **no later than 72 hours** after determining a "Cybersecurity Incident" has occurred (at the entity, an affiliate, or a third-party service provider) — clock starts at determination, not incident start or full investigation.
- Reportable incidents include those requiring notice to any government/self-regulatory/supervisory body, those with reasonable likelihood of materially harming normal operations, or ransomware deployed in a material part of information systems.
- **Ransomware extortion payments:** separate notice to DFS Superintendent within **24 hours** of payment.

## 2. Consumer-Facing Payment/Billing Notices

**Statute:** N.Y. Gen. Bus. Law §§ 527 and 527-a (general automatic-renewal / "click to cancel" law); amendments effective **November 5, 2025**.

- **Renewal notice (contracts ≥ 1 year):** Business must notify consumer of an automatic renewal or continuous-service charge **at least 15 days, but not more than 45 days, before the cancellation deadline**, via the method the consumer selected, with cancellation instructions. (This 15–45 day window traces to the December 13, 2023 amendment to § 527-a and was carried forward.)
- **Material change / price-increase notice (new, effective 11/5/2025):** After enrollment, business must give advance notice of any material change to offer terms, including price changes. Notice must be clear and conspicuous, sent **5–30 days prior** to the effective date, via the consumer's selected communication method.
- **Price increases for existing subscribers:** Business must either (a) obtain the consumer's affirmative consent to the higher price before charging it, or (b) allow the consumer to cancel for a prorated refund.
- **Cancellation:** Consumers who enrolled online must be able to cancel online ("click to cancel"); no required standalone "cancellation confirmation notice" text was confirmed in reviewed sources — **verify with counsel.**
- **Penalties:** Civil penalties up to $100 per violation (single) / $500 (multiple violations, single act or incident); "knowing violations" up to $500 (single) / $1,000 (multiple).
- Business-to-business auto-renewal contracts are separately governed by N.Y. Gen. Oblig. Law § 5-903, not § 527-a.

## 3. State-Specific EFT/ACH Notice Requirements (Beyond Federal Reg E)

- No New York-specific statutory notice requirement for preauthorized EFT/recurring debits beyond federal Regulation E (12 CFR Part 1005) was identified in the sources reviewed.
- **Not confirmed — verify with counsel** before concluding no gap-filling state requirement exists (state banking law was not exhaustively reviewed).

## Sources

- [N.Y. Gen. Bus. Law § 899-aa — NY Senate](https://www.nysenate.gov/legislation/laws/GBS/899-AA)
- [New York Data Breach Notification Law — DataBreachCost.com](https://databreachcost.com/breach-notification-laws/new-york)
- [New York Data Breach Notification Laws — Recording Law](https://www.recordinglaw.com/us-laws/data-privacy-laws/new-york-data-privacy-laws/data-breach-notification/)
- [Three Key Changes to Breach Notification Law — Bleakley Platt](https://www.bpslaw.com/three-key-changes-breach-notification-law/)
- [New York SHIELD Act — New York Security Authority](https://newyorksecurityauthority.com/newyork-shield-act-cybersecurity-obligations)
- [23 NYCRR 500 (PDF) — NY DFS](https://www.dfs.ny.gov/system/files/documents/2023/03/23NYCRR500_0.pdf)
- [NYDFS Part 500 in 2025 — Beyond Identity](https://www.beyondidentity.com/resource/nydfs-part-500-in-2025-key-deadlines-new-requirements-and-compliance-strategies)
- [N.Y. Gen. Bus. Law § 527-A — NY Senate](https://www.nysenate.gov/legislation/laws/GBS/527-A)
- [New York and Colorado Update Auto-Renewing Subscription Requirements — Perkins Coie](https://perkinscoie.com/insights/update/new-york-and-colorado-update-auto-renewing-subscription-requirements)
- [New York's New Automatic Renewal Law — kr.law](https://kr.law/news/article-detail/new-yorks-new-automatic-renewal-law-how-it-compares-to-california)
- [New York's Enhanced Automatic Renewal Law — Kirkland & Ellis](https://www.kirkland.com/publications/kirkland-alert/2021/02/new-york-enhanced-automatic-renewal-law)
- [12 CFR Part 1005 (Regulation E) — eCFR](https://www.ecfr.gov/current/title-12/chapter-X/part-1005)
