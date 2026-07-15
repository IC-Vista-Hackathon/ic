# New York — Payment Compliance Reference

> Internal reference only. NOT legal advice. Verify all items with counsel before relying on them.

## 1. Autopay/Recurring Payment Funding Method (Credit Card vs. ACH)

- No NY-specific statute found restricting recurring/autopay payments to ACH-only (unlike FL's utility/insurance autopay ACH restriction referenced as precedent).
- NY investor-owned utilities (regulated by NY PSC) commonly structure "AutoPay" as bank-account (ACH) draft only, with credit/debit card treated as a separate, fee-bearing payment channel (e.g., National Fuel, NYSEG). This appears to be utility/vendor business practice, not a PSC-mandated legal restriction — **not confirmed as a legal requirement; verify with counsel.**
- No evidence found of a PSC rule or NY statute prohibiting card-funded recurring/autopay generally, in any industry.
- **Gap: Not confirmed — verify with counsel** whether any NY PSC tariff or regulation formally restricts utility autopay to ACH.

## 2. Credit Card Surcharge / Convenience Fee Law — GBL § 518

- **Citation:** NY General Business Law Article 29-A, § 518, "Credit card surcharge notice requirement." Current version effective **February 11, 2024** (amended after litigation).
- Requires clear and conspicuous posting of the total price inclusive of any surcharge for credit card use.
- **Cap:** surcharge may not exceed the amount charged to the merchant by the credit card company for that transaction (i.e., pass-through of actual card-acceptance cost, not a fixed statutory 2%/4% cap).
- Final price inclusive of surcharge cannot exceed the posted price.
- Compliant methods: (a) single posted price already inclusive of surcharge, or (b) two-tier pricing — cash price and credit price both posted/tagged so the customer sees both before checkout.
- Civil penalty: up to $500 per violation.
- **Litigation history:** *Expressions Hair Design v. Schneiderman*, 581 U.S. ___ (2017) — SCOTUS held the prior surcharge-ban formulation regulated speech (First Amendment), remanded to 2nd Circuit, which certified a question to the NY Court of Appeals. NY Court of Appeals (Oct. 23, 2018) held a merchant complies if it simply displays the higher (credit-inclusive) price to card users, without labeling it a "surcharge." Case dismissed Jan. 2019. GBL § 518 was subsequently rewritten by the legislature into its current disclosure-based form (effective Feb. 11, 2024).

## 3. UDAP / Consumer Protection — GBL §§ 349, 350, and Automatic Renewal Law § 527-a

- **GBL § 349** — "Deceptive acts or practices unlawful" (Article 22-A). Private right of action; actual damages or $50, whichever greater; treble damages up to $1,000 for willful/knowing violations; attorney's fees to prevailing plaintiff.
- **GBL § 350** — false advertising, companion statute in same Article.
- **Dec. 2025 amendment:** NY enacted the "FAIR Business Practices Act," expanding § 349 to cover "unfair" and "abusive" acts/practices in addition to "deceptive" — but only the NY AG (not private plaintiffs) may bring claims on the unfair/abusive prongs. Private plaintiffs remain limited to the "deceptive" prong.
- **Automatic Renewal / Negative Option — GBL § 527-a** (the operative automatic-renewal disclosure/cancellation statute; note some sources cite § 527 as the surrounding general provision, with § 527-a as the amended automatic-renewal-specific section — **confirm exact section numbering split with counsel**):
  - Amendment effective **Dec. 13, 2023**: for auto-renewal terms of one year or longer, business must notify consumer 15–45 days before the cancellation deadline, with instructions on how to cancel.
  - Further amendment effective **Nov. 5, 2025**: added advance-notice and affirmative-consent requirements for price increases on auto-renewing subscriptions, including a right to cancel and receive a prorated refund if the consumer does not consent to a price increase.
  - Enforcement example: NY AG settled with Equinox for $600,000 (June 2025) over cancellation-difficulty/renewal-disclosure allegations.
  - A separate "Click to Cancel Act" was found referenced as proposed (not yet confirmed enacted) — **Not confirmed — verify with counsel** on current status.

## 4. Data Breach Notification — SHIELD Act (GBL §§ 899-aa, 899-bb) and State Technology Law § 208

- **GBL § 899-aa** — "Notification; person without valid authorization has acquired private information." Amended by the SHIELD Act, effective **Oct. 23, 2019**: added 30-day-ish reasonable notification timing standard, DFS-regulated-entity notice carve-out, and expanded "private information" definition.
- **GBL § 899-bb** — "Data security protections," added by SHIELD Act, effective **March 21, 2020**: requires reasonable administrative, technical, and physical safeguards; applies broadly to any business handling NY residents' private information (not just NY-based businesses).
- **"Private information" definition includes payment card data**: credit/debit card number alone (if it can be used to access the account without additional identifiers), or account number/card number in combination with any required security code, access code, or password permitting access to a financial account.
- **NY State Technology Law § 208** — parallel notification obligation for state entities (not private businesses); breaches must be reported to NY AG, Dept. of State Division of Consumer Protection, and ITS Enterprise Information Security Office. Relevant to InvoiceCloud only if acting as a subcontractor/vendor to a NY state entity — **verify applicability with counsel.**

## 5. PCI-DSS Incorporation / Card Data Retention — 23 NYCRR 500 and Truncation

- **23 NYCRR Part 500** (DFS Cybersecurity Regulation, effective March 1, 2017, amended 2023): applies to "Covered Entities" — persons/organizations operating under a license, registration, charter, certificate, or permit under NY Banking, Insurance, or Financial Services Law (e.g., NY-licensed/chartered banks, insurers, money transmitters).
  - Does **not** directly regulate payment processors/merchants unless they independently hold a NY DFS license/charter (e.g., as a licensed money transmitter).
  - Covered Entities that use third-party processors must impose vendor cybersecurity requirements on them under § 500.11 (third-party service provider policy) — i.e., PCI/security obligations may flow down contractually even where the processor itself isn't a Covered Entity.
  - **Not confirmed** whether InvoiceCloud or its bank/processor partners are themselves DFS Covered Entities — verify with counsel/compliance based on current licensing.
- No NY statute found that expressly references or incorporates "PCI-DSS" by name.
- **Card receipt truncation**: federal FACTA (15 U.S.C. § 1681c(g)) already requires truncation to last 5 digits and no expiration date on receipts, nationwide. **No NY-specific truncation statute layered on top was confirmed** in this search — treat FACTA as the controlling truncation rule unless counsel identifies a NY-specific provision.

## 6. NY EFT/ACH-Specific Statutes Beyond Reg E

- **NY UCC Article 4-A** (Funds Transfers) governs wholesale/wire funds transfers and its interaction with the federal EFT Act; amended by NY Senate Bill S7493 to clarify Article 4-A's relationship to Reg E and remittance transfers.
- **NY Electronic Signatures and Records Act (ESRA)**, State Technology Law §§ 301–309: governs validity of electronic signatures/records in NY generally; DFS guidance notes insurers may not compel electronic transactions without insured/vendor consent — a possible analog for consent-to-electronic-payment requirements, but this is insurance-sector guidance, not a payments-specific statute.
- No additional NY-specific consumer ACH/EFT statute (beyond Reg E incorporation and UCC 4-A for wholesale transfers) was confirmed. **Not confirmed — verify with counsel** for any NY banking-law provisions specific to consumer ACH debits.

## Sources

- https://www.nysenate.gov/legislation/laws/GBS/518
- https://law.justia.com/codes/new-york/gbs/article-29-a/518/
- https://www3.erie.gov/consumerprotection/nys-general-business-law-ss518-changes-february-11-2024
- https://katten.com/new-york-will-soon-require-merchants-to-provide-additional-credit-card-surcharge-disclosures
- https://en.wikipedia.org/wiki/Expressions_Hair_Design_v._Schneiderman
- https://www.forbes.com/sites/wlf/2018/10/31/expressions-hair-design-speech-case-back-on-track-after-detour-to-ny-state-court/
- https://www.nysenate.gov/legislation/laws/GBS/527-A
- https://www.nysenate.gov/legislation/laws/GBS/527
- https://www.pollockcohen.com/media/blog/2024-10-28-new-yorks-new-auto-renewal-law
- https://www.kelleydrye.com/viewpoints/blogs/ad-law-access/ny-quietly-amends-automatic-renewal-law
- https://katten.com/new-york-passes-revised-automatic-renewal-law
- https://ag.ny.gov/press-release/2021/consumer-alert-attorney-general-james-issues-warning-against-marketing-schemes
- https://www.nysenate.gov/legislation/laws/GBS/349
- https://www.dlapiper.com/en/insights/publications/2026/03/new-york-enacts-the-fair-business-practices-act-key-considerations-for-businesses
- https://www.nysenate.gov/legislation/laws/GBS/899-AA
- https://www.nysenate.gov/legislation/laws/GBS/899-BB
- https://www.bpslaw.com/three-key-changes-breach-notification-law/
- https://www.nysenate.gov/legislation/laws/STT/208
- https://its.ny.gov/breach-notification-and-incident-reporting
- https://www.dfs.ny.gov/system/files/documents/2023/12/rf23_nycrr_part_500_amend02_20231101.pdf
- https://www.law.cornell.edu/regulations/new-york/title-23/chapter-I/part-500
- https://www.saltycloud.com/blog/what-is-23-nycrr-500/
- https://www.nysenate.gov/legislation/bills/2011/S7493/amendment/a
- https://www.daeryunlaw.com/us/insights/law-firm-new-york-city-on-electronic-financial-transactions-act
- https://www.getflexpoint.com/credit-card-surcharging-us-states/new-york
