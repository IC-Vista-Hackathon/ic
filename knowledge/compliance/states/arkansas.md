# Arkansas Payment Compliance Reference

> Internal compliance reference only — not legal advice. Verify with counsel before acting.

## 1. Autopay/Recurring Payment Authorization — Card vs. ACH Restrictions
- No Arkansas statute found restricting recurring/autopay funding to ACH-only for utilities, insurance, or generally (no Florida-style card-autopay carve-out identified). **Not confirmed — verify with counsel.**
- Arkansas Public Service Commission / insurance regulations governing utility and insurance billing did not surface any ACH-only autopay mandate in this search.
- Recurring/autopay authorization generally governed by federal EFTA/Reg E (for ACH/bank-debit) and card network rules (for credit card autopay) — no additional Arkansas-specific overlay found.

## 2. Credit Card Surcharge / Convenience Fee Law
- Arkansas has no general statutory ban on credit card surcharging; no evidence of a repealed/struck-down no-surcharge statute (unlike some states post-*Expressions Hair Design*/*Italian Colors* litigation).
- Arkansas Attorney General guidance: surcharge fees must be disclosed to the consumer prior to sale; distinguishes surcharge fees (% based, credit card only) from convenience fees (flat, alternative channel) and service fees (restricted to certain merchant categories, e.g. education/government).
- No state-set numeric surcharge cap identified; AG guidance points to federal/card-network limits (Visa ~3%, general practice ≤4%).
- Debit card surcharging remains prohibited — this is a federal-law (Reg II / network rule) restriction applicable nationwide, not an Arkansas-specific statute.
- Ark. Code Ann. § 4-115-101 — regulates credit card *processor* contract disclosures/fees charged to merchants (contract term, termination fee cap of $50, monthly minimum fee cap); does **not** address consumer-facing surcharging.
- Ark. Code Ann. § 16-13-706 — allows a third-party (not the court itself) to charge a service/convenience fee when a court-ordered fine/cost is paid by credit or debit card; narrow, government-fee-specific context, not general commercial surcharge law.

## 3. UDAP / Autorenewal / Negative-Option Statutes
- Arkansas Deceptive Trade Practices Act (ADTPA) — Ark. Code Ann. § 4-88-101 et seq. General prohibition on deceptive/unfair/unconscionable trade practices; AG and private right of action.
- Arkansas passed a dedicated automatic-renewal/continuous-service statute: **HB1820 (2025), enacted as Act 652 of 2025**, adding new sections to Ark. Code Ann. Title 4, Chapter 86 (new subchapter, approx. §§ 4-86-101 et seq. — exact final section numbers **not independently confirmed against the official Code reprint; verify with counsel**).
  - Effective January 1, 2026.
  - Requires clear/conspicuous, affirmative disclosure of material auto-renewal/continuous-service terms before charging.
  - Requires affirmative consumer consent (no passive/pre-checked acceptance); explicit "negative option feature" consent required separately, retainable by consumer.
  - Requires a cancellation mechanism at least as easy as sign-up (e.g., online cancel if online enrollment).
  - Requires 30-days'-advance notice and fresh consent for material term changes (e.g., price increases, reduced service).
  - Violations = deceptive trade practice under ADTPA; AG enforcement + private right of action; damages up to $500/violation statutory, plus actual damages and attorney's fees.
- Prior/narrower Arkansas auto-renewal law: Ark. Code Ann. § 4-86-106 — automatic renewal of professional home security contracts prohibited beyond initial term (contracts after 8/1/2003); narrow sector-specific predecessor, distinct from the new 2025 general statute.

## 4. Data Breach Notification — Payment Card Data
- Arkansas Personal Information Protection Act (PIPA) — Ark. Code Ann. § 4-110-101 et seq.
- Notification trigger: unauthorized acquisition of unencrypted "personal information" (includes name + payment card/financial account number, per typical PIPA definition) of an Arkansas resident — Ark. Code Ann. § 4-110-105.
- Timing: disclosure "in the most expedient time and manner possible and without unreasonable delay," subject to law-enforcement delay and scope-investigation needs.
- AG notification required for breaches affecting 1,000+ individuals, same time as consumer notice or within 45 days of harm-likelihood determination, whichever first.
- No notification required if entity reasonably determines no likelihood of harm following investigation.
- Substitute notice (email + website + statewide media) permitted if cost >$250,000, affected population >500,000, or insufficient contact info.
- 5-year retention requirement for breach determination records; AG may request within 30 days.
- Encrypted data generally exempt from the notification trigger.

## 5. State Law Referencing/Incorporating PCI-DSS
- No Arkansas statute found that mandates or directly incorporates PCI-DSS compliance. PIPA (§ 4-110-101 et seq.) requires only "reasonable security" generally, not a named PCI-DSS standard. **Not confirmed — verify with counsel.**
- PCI-DSS applies to Arkansas merchants only as a contractual/card-network requirement (via merchant/processor agreements), not as state law.

## 6. State-Specific EFT/ACH Statutes Beyond Reg E
- No standalone Arkansas consumer EFT/ACH statute beyond federal EFTA/Reg E identified for general recurring-payment authorization. **Not confirmed — verify with counsel.**
- Ark. Code Ann. § 4-4A-108 — addresses relationship between Arkansas UCC Article 4A (funds transfers) and the federal Electronic Fund Transfer Act (scoping/preemption provision, not an independent consumer EFT regime).
- Arkansas Uniform Money Services Act — Ark. Code Ann. § 23-55-101 et seq. — licenses/regulates money transmitters and related money services businesses operating in Arkansas; relevant to entities facilitating EFT/ACH transfers as a service, not a consumer-authorization rule.
- Public-sector EFT statutes exist (e.g., Ark. Code Ann. § 14-59-105, § 14-24-121 — municipal electronic funds transfer/prenumbered check provisions; § 26-19-101 et seq. — mandatory EFT for large state tax remitters) — these are government-payment-specific, not general consumer payment-compliance rules.

## Sources
- [Ark. Code Ann. § 4-110-105 — Disclosure of security breaches (Justia)](https://law.justia.com/codes/arkansas/title-4/subtitle-7/chapter-110/section-4-110-105/)
- [Arkansas Personal Information Protection Act PDF](https://arkansasosd.com/wp-content/uploads/Personal-Information-Protection-Act.pdf)
- [Ark. Code Ann. § 4-115-101 — Credit card processing service disclosures/prohibitions (Justia)](https://law.justia.com/codes/arkansas/title-4/subtitle-7/chapter-115/section-4-115-101/)
- [Ark. Code Ann. § 16-13-706 — Credit or debit card payments (Justia)](https://law.justia.com/codes/arkansas/title-16/subtitle-2/chapter-13/subchapter-7/section-16-13-706/)
- [Credit Cards — Arkansas Attorney General](https://arkansasag.gov/divisions/public-protection/finances/credit-cards/)
- [Arkansas Credit Card Surcharge Laws — Merchant Cost Consulting](https://merchantcostconsulting.com/lower-credit-card-processing-fees/arkansas-credit-card-surcharge-laws/)
- [Ark. Code Ann. § 4-88-101 — Applicability of chapter, ADTPA (Justia)](https://law.justia.com/codes/arkansas/title-4/subtitle-7/chapter-88/subchapter-1/section-4-88-101/)
- [Ark. Code Ann. § 4-86-106 (2019) — Automatic renewal of home security contracts prohibited (Justia)](https://law.justia.com/codes/arkansas/2019/title-4/subtitle-7/chapter-86/section-4-86-106/)
- [Arkansas HB1820 (2025) Bill Detail — Arkansas State Legislature](https://arkleg.state.ar.us/Bills/Detail?id=HB1820&ddBienniumSession=2025%2F2025R)
- [HB1820 engrossed text PDF — arkleg.state.ar.us](https://arkleg.state.ar.us/Home/FTPDocument?path=%2FBills%2F2025R%2FPublic%2FHB1820.pdf)
- [Act 652 of 2025 (HB1820) PDF — webftp.blr.arkansas.gov](https://webftp.blr.arkansas.gov/Home/FTPDocument?path=ACTS/2025R/Public/Searchable/ACT652.pdf)
- [CitizenPortal — Legislature tightens regulations on automatic renewal contracts in HB1820](https://www.citizenportal.ai/articles/3098479/Arkansas/Legislature-tightens-regulations-on-automatic-renewal-contracts-in-HB1820)
- [Ark. Code Ann. § 4-4A-108 (2017) — Relationship to Electronic Fund Transfer Act (Justia)](https://law.justia.com/codes/arkansas/2017/title-4/subtitle-1/chapter-4a/part-1/section-4-4a-108/)
- [Arkansas Cybersecurity Compliance overview — manageditservices.ai](https://manageditservices.ai/blog/arkansas-data-privacy-law)
