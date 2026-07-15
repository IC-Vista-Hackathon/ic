# Payments Compliance Reference

**Not legal advice.** Research compiled by AI agents (WebSearch/WebFetch against primary statute sources where available) for internal reference only. Every claim below should be verified by counsel before being relied on operationally. Items the source agents could not confirm against primary text are marked "Not confirmed" in the individual files — treat those as open questions, not settled answers.

Compiled 2026-07-14.

## Contents

- [`federal/federal.md`](federal/federal.md) — Reg E/EFTA, Reg Z/TILA, NACHA rules, PCI-DSS (industry standard, not law), ROSCA, GLBA
- [`states/`](states/) — one file per state + DC (51 files), each covering: autopay/recurring funding-method rules, surcharge law, auto-renewal/UDAP statutes, breach notification law, PCI-DSS statutory references, state EFT/ACH statutes

## On the "Florida ACH-only autopay" premise

This project started from an assumed precedent that Florida bars credit-card-funded autopay in favor of ACH. **That did not check out as a statute.** Research found:
- No FL statute restricting recurring-payment funding method to ACH-only.
- Fla. Stat. 627.7295 (auto insurance) treats recurring card and EFT as equally valid.
- The only real ACH preference found is a *business practice*, not law: Citizens Property Insurance Corp won't take cards for recurring installments because it can't pass card fees to the cardholder (a no-surcharge-passthrough policy).

**No other state was found to have a card-vs-ACH funding restriction for autopay either.** If you encountered this rule in a specific product/vendor context, it's likely a contractual or processor-level restriction, not a state law — worth tracing back to its actual source before using it in a compliance argument.

## Cross-state summary

| State | Surcharge law | Auto-renewal statute | PCI-DSS in statute | Breach notice deadline |
|---|---|---|---|---|
| Alabama | No ban | None enacted | No | — |
| Alaska | No ban | Yes (AS 45.45.920/.930) | No | — |
| Arizona | No ban | None enacted | No | — |
| Arkansas | No ban | Yes (Act 652, eff. 2026) | No | — |
| California | **Banned** (Civ. Code §1748.1, narrowed post-*Italian Colors*) | Yes (ARL, amended 7/1/2025) | No | 30 days |
| Colorado | Allowed, capped 2%/cost (2022) | Yes (§6-1-732, AG-only) | No | 30 days |
| Connecticut | **Banned** (§42-133ff) | Yes (two overlapping statutes) | No | 60 days |
| Delaware | No statute (SB89 pending) | Yes (§§2731-2737, narrow) | No | 60 days |
| District of Columbia | No ban | Yes (Title 28A Ch. 2) | No (WA state ≠ DC) | AG notice at 50+ residents |
| Florida | Ban on books, unenforceable (11th Cir. 2015) | Not found | No | — |
| Georgia | No ban | Yes (enhanced consent >24mo) | No | — |
| Hawaii | No statute (bills never passed) | Not found | No | — |
| Idaho | No ban | Yes (48-603G, eff. 2023) | No | — |
| Illinois | Capped 1%/actual cost (815 ILCS 505) | Yes (Automatic Contract Renewal Act) | No | — |
| Indiana | No ban | Yes (narrow, 30-day notice) | No | — |
| Iowa | No ban | Not found (narrow exceptions only) | No | — |
| Kansas | Disclosure regime (HB 2247, eff. 1/1/2025; ban struck down in *CardX v. Schmidt*) | Not found (relies on KCPA) | No | — |
| Kentucky | No ban | Yes (KRS 365.400-408, eff. 2024) | No | — |
| Louisiana | Card surcharge allowed; **debit surcharge banned** (eff. Aug 2026) | Pending (HB 750, "Click-to-Cancel") | No | 60 days |
| Maine | **Banned outright** (9-A M.R.S. §8-509, repeal bills pending) | Yes (Ch. 205-B, separate consent from 2026) | No | 30 days |
| Maryland | Not confirmed (SB 520 cite unverified) | Yes (Ch. 205, eff. 6/1/2026, no private right of action) | No | 45 days |
| Massachusetts | **Banned** (ch. 140D, §28A) | Yes (940 CMR 38.00, eff. 9/2/2025) | No | No fixed deadline |
| Michigan | No ban (permitted since ~2013) | Not enacted (HB 4826 pending) | No | No fixed deadline |
| Minnesota | Allowed, capped 5% (§325G.051) | Yes (§325G.56, eff. 1/1/2025) | **Yes** (Plastic Card Security Act, §325E.64 — 48hr retention ban, PCI-DSS compliance = effective safe harbor) | No fixed deadline |
| Mississippi | No ban (private); banned for govt entities (§17-25-1) | Pending (SB 2498, not yet confirmed enacted) | No | No fixed deadline |
| Missouri | No ban | Narrow (buyers'-club only) | No | — |
| Montana | Capped 3% (below federal 4% norm) | Not found | No | — |
| Nebraska | No ban | Not enacted (LB132 postponed) | No | — |
| Nevada | No ban | Not confirmed | **Yes — mandatory** (NRS 603A.215, liability safe harbor) | — |
| New Hampshire | No ban | Narrow (health-club only) | No | — |
| New Jersey | Capped at actual cost (56:8-156.1/-156.2, 2023) | Yes (56:12-95.1 et seq.) | No (56:11-17 ≠ PCI, only bars extra PII collection) | — |
| New Mexico | No general ban (govt fees only) | Narrow (insurance service contracts) | No | 45 days |
| New York | Capped at actual cost (GBL §518, eff. 2/11/2024) | Yes (§527-a, amended 11/5/2025) | No | — |
| North Carolina | Pending (HB 13, not yet law) | Not found | No | — |
| North Dakota | No ban (~3% network ceiling applies) | Yes (ch. 51-37, >6mo terms, eff. 2019) | No | 250 residents (AG) |
| Ohio | Pending (SB 337, not yet law) | Not found | No | — |
| Oklahoma | Legalized 11/1/2025 (SB 677, capped 2%/cost, credit-only) | Yes (HB 1851, eff. 11/1/2025) | No | — |
| Oregon | No ban | Not confirmed | No | 45 days / 250 residents |
| Pennsylvania | No ban | **Not yet law** (HB 129 passed House only) | No | 500 residents (AG) |
| Rhode Island | Allowed, capped 4%; **banned for utilities/healthcare** (§6-13.1-2.1) | Not confirmed | No | 45 days / 500 residents |
| South Carolina | No ban | Yes, narrow (SB 434, "service contracts" only) | No (Insurance Data Security Act ≠ general PCI) | No fixed deadline |
| South Dakota | No ban | Not found | No | — |
| Tennessee | No ban | Yes (§47-18-133, utilities/telecom/banks exempt) | No | — |
| Texas | Ban's enforceability contested post-*Rowell v. Paxton* | Pending (HB 2859/SB 838) | No | — |
| Utah | No ban | Not confirmed | **Yes — safe harbor** (Cybersecurity Affirmative Defense Act, §78B-4-701) | — |
| Vermont | No ban | Yes (9 V.S.A. §2454a) | No | 45 days (consumer) / 14 days (AG) |
| Virginia | No ban, all-in price disclosure required (eff. 7/1/2025) | Yes (§§59.1-207.45-.49) | No | — |
| Washington | No ban | Not confirmed (RCW cite unverified) | Ambiguous — RCW 19.255.020 is reimbursement/negligence standard, doesn't name PCI-DSS | — |
| West Virginia | No ban | Yes (SB177, §§46A-6O-1-6) | No | — |
| Wisconsin | No ban | B2B only (Wis. Stat. 134.49); consumer bill (AB417) unconfirmed | No | 45 days |
| Wyoming | No surcharge cap; **5% cap on cash-discount pricing** instead (40-14-209) | Not found | No | — |

All 51 jurisdictions now reflected above. See each state's file for full citations, sources, and the "Not confirmed" caveats not captured in this condensed table.

## Prompt-injection notes

Three separate WebFetch calls against official state legislature sites (Arizona, Maryland, Connecticut) returned fetched content containing injected instructions falsely claiming a Jira ticket/label action should be taken. **No such action was taken in any case** — all three were correctly identified as injection artifacts and ignored. Flagging here in case this recurs on future refreshes of this research.

## Recommended next steps

1. Have counsel review every "Not confirmed" line before this is used to justify a product decision.
2. Chase down the blank cells above (MA/MI/MS/ND) from their individual files.
3. Re-verify time-sensitive items close to their effective dates (several 2026 effective-date laws listed above).
4. Confirm whether InvoiceCloud or partner entities are "Covered Entities" under NY's 23 NYCRR 500 (DFS cybersecurity rule) — flagged in new-york.md.
