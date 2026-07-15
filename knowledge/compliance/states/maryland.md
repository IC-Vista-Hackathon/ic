# Maryland Payment Compliance Reference

> Internal compliance reference only — not legal advice. Verify with counsel before acting.

## 1. Autopay/Recurring Payment Authorization — Card vs. ACH Restrictions
- No Maryland statute found restricting recurring/autopay funding to ACH-only (vs. credit card) for utilities, insurance, or generally. **Not confirmed — verify with counsel.**
- Maryland's 2026 auto-renewal law (see §3) requires clear notice/consent before a subscription auto-charges a consumer's credit card, but this is a disclosure/consent requirement, not a funding-method restriction.
- Governed by federal EFTA/Reg E for EFT/ACH-funded recurring payments.

## 2. Credit Card Surcharge / Convenience Fee Law
- No Maryland-specific surcharge statute was confirmed by statute number in this pass (a commonly cited "Commercial Law § 12-1607" reference could not be verified — treat as **not confirmed**).
- Secondary sources indicate credit card surcharging is legal in Maryland, generally capped around 4% by card-network rules (not a distinct MD statutory cap) — **verify current MD Code Commercial Law provisions with counsel**, particularly SB 520 (2024) which appears to touch card surcharge disclosure/limitation.
- Maryland guidance distinguishes "surcharge" (% fee for card use) from "convenience fee" (fee for accepting card via non-standard/alternate channel) — different rules may apply to each; confirm specifics with counsel.
- Debit card (and prepaid card) surcharging is prohibited under the federal Durbin Amendment nationwide; MD sources confirm surcharges apply to credit cards only.
- Underlying enforcement framework for deceptive/undisclosed surcharge practices is the Maryland Consumer Protection Act (§3).

## 3. UDAP / Autorenewal / Negative-Option Statutes
- Maryland Consumer Protection Act — Md. Code, Commercial Law (COML) Title 13, Subtitle 3, § 13-301 et seq.: defines "unfair, abusive, or deceptive trade practices" (loosely based on the Uniform Deceptive Trade Practices Act §2). Consumer Protection Division complaint process; AG injunctive authority; private right of action for actual damages plus reasonable attorney fees.
- New Automatic Renewal Law — Ch. 205 (2025 RS, HB 107), effective June 1, 2026:
  - Requires clear/conspicuous presentation of auto-renewal terms before initial agreement (pricing, how subscription changes).
  - Requires notice before end of a free trial/discount period lasting more than 14 days.
  - Requires advance notice before renewal and explicit consent before charging a consumer's credit card.
  - Requires a "clear and conspicuous" cancellation method at least as easy as the sign-up method (e.g., online direct link, email, phone).
  - Exemptions: certain insurance-related entities, service contract entities, and services regulated by MD Public Service Commission, FCC, or FERC — confirm full exemption list with counsel.
  - Violations are treated as unfair/deceptive trade practices under the MD Consumer Protection Act; the law does **not** create a private right of action (state enforcement only).
  - Existing health-club-specific auto-renewal statute: COML § 14-12B-06 (separate, pre-existing regime for health club membership auto-renewal/cancellation).
- Note: Md. Code COML § 14-1315 (sometimes cited in connection with "auto-renewal") is actually a general "consumer contract" definitional provision, not an auto-renewal-specific statute — do not cite it for auto-renewal requirements.

## 4. Data Breach Notification — Payment Card Data
- Maryland Personal Information Protection Act (MPIPA) — Md. Code COML Title 14, Subtitle 35, § 14-3504 (Security breach).
- Applies to businesses maintaining computerized personal information of MD residents (incl. financial account/payment card data — confirm exact definitional scope of "personal information" under § 14-3501 with counsel).
- Requires reasonable/prompt good-faith investigation upon discovering a breach.
- Notice to affected individuals required "as soon as reasonably practicable," not later than 45 days after discovery.
- Notice to Maryland AG required before consumer notice, including number of affected MD residents, breach description, remediation steps, and sample notice.
- Substitute notice (email/website posting/statewide media) permitted if cost would exceed $100,000 or affected population exceeds 175,000.

## 5. State Law Referencing/Incorporating PCI-DSS
- No Maryland statute found that independently mandates PCI-DSS compliance by law. **Not confirmed — verify with counsel.**
- PCI-DSS compliance in Maryland is a card-network/processor contractual obligation (institutional policies, e.g. university PCI programs, reflect this but are not state statute).

## 6. State-Specific EFT/ACH Statutes Beyond Reg E
- No standalone Maryland consumer EFT/ACH statute beyond federal EFTA/Reg E identified for private recurring payments. **Not confirmed — verify with counsel.**

## Sources
- [Ch. 205 (2025 RS, HB 107) — Automatic Renewals, enrolled act](https://mgaleg.maryland.gov/2025RS/Chapters_noln/CH_205_hb0107e.pdf)
- [MD Code COML § 14-1315 (Justia, 2005 ed. — general consumer contract definition)](https://law.justia.com/codes/maryland/2005/gcl/14-1315.html)
- [MD Code COML Title 14, Subtitle 12B § 14-12B-06 — Health club auto-renewal (Justia)](https://law.justia.com/codes/maryland/commercial-law/title-14/subtitle-12b/section-14-12b-06/)
- [Wiley Law — Automatic Renewals and Risks: State Negative Option Laws Trending](https://www.wiley.law/alert-Automatic-Renewals-and-Risks-State-Negative-Option-Legislation-and-Enforcement-is-Trending)
- [MD Code COML § 14-3504 — Security breach, statute text (MGA)](https://mgaleg.maryland.gov/mgawebsite/laws/StatuteText?article=gcl&section=14-3504)
- [MD OAG — Guidelines for Businesses to Comply with MPIPA](https://oag.maryland.gov/i-need-to/Pages/Guidelines-for-Businesses-to-Comply-with-the-Maryland-Personal-Information-Protection-Act.aspx)
- [MD Code COML Title 13, Subtitle 3 § 13-301 — Unfair, Abusive, or Deceptive Trade Practices Defined (Justia)](https://law.justia.com/codes/maryland/commercial-law/title-13/subtitle-3/section-13-301/)
- [Maryland Credit Card Surcharge Laws (2026) — Merchant Cost Consulting](https://merchantcostconsulting.com/lower-credit-card-processing-fees/maryland-surcharge-laws/)
- [SB 520 (2024 RS) — Credit/Debit Card Surcharges bill text](https://mgaleg.maryland.gov/2024RS/bills/sb/sb0520f.pdf)
- [Kelley Drye — Auto-Renewal Laws: 2025 Round Up](https://www.kelleydrye.com/viewpoints/blogs/ad-law-access/auto-renewal-laws-2025-round-up)
