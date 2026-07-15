# Colorado — Payment Compliance Reference

Internal compliance reference only. Not legal advice — verify with counsel before relying on this for product/legal decisions.

## 1. Autopay/Recurring Payment Authorization — Card vs ACH Restrictions
- No Colorado-specific statute found mandating ACH-only funding for recurring/autopay or restricting credit card as a funding rail. **Not confirmed — verify with counsel.**
- Colorado's Automatic Renewal statute (C.R.S. § 6-1-732) governs consent/disclosure regardless of funding method (see §3); it does not restrict payment rail choice.
- Note: § 6-1-732 exempts public utilities, cable/telephone services, insurance companies, banks, credit unions, and airlines from its auto-renewal disclosure regime — but this is an exemption from disclosure rules, not evidence of an ACH-only funding mandate for those sectors.

## 2. Credit Card Surcharge / Convenience Fee Law
- C.R.S. § 5-2-212 (enacted via SB21-091, effective July 1, 2022) — Colorado now **expressly permits** surcharging (reversing a former near-ban), with conditions:
  - Surcharge capped at the lesser of 2% of the transaction total, or the merchant discount fee actually incurred processing the card transaction.
  - Surcharges may **not** be imposed on payments by cash, check, debit card, or gift card redemption — surcharging is limited to credit/charge card transactions.
  - Required point-of-sale/online disclosure: specific statutory signage language must be posted (or shown before checkout completion for online transactions) citing § 5-2-212 and the 2% cap.
  - Surcharge must be itemized as a separate line item on the receipt.

## 3. UDAP / Automatic Renewal / Negative-Option Statutes
- C.R.S. § 6-1-732 (Colorado Consumer Protection Act, Part 7) — enacted HB21-1239, effective Jan. 1, 2022; amended effective Aug. 6, 2025 with stricter disclosure/notice/cancellation requirements.
- Core requirements: clear-and-conspicuous disclosure of auto-renewal terms; advance notice of renewal 25–40 days before each renewal for certain contract types; simple, cost-effective, easy-to-use cancellation mechanism; specific trial-period-offer rules.
- Enforcement: **no private right of action** (unlike California's ARL) — enforced solely by the Colorado Attorney General under the Colorado Consumer Protection Act, with civil penalties up to $20,000 per violation plus injunctive relief/restitution authority.
- Exemptions: public utilities, cable/telephone, insurance, banks, credit unions, airlines (per statutory text — confirm current exemption list, since the law was amended in 2025).

## 4. Data Breach Notification — Payment Card Data
- C.R.S. § 6-1-716 — Colorado's security breach notification statute.
- Covered "personal information" includes a CO resident's account number or credit/debit card number **combined with any required security code, access code, or password** permitting account access.
- Trigger: "security breach" = unauthorized acquisition of unencrypted computerized data compromising security/confidentiality/integrity of personal information.
- Process: covered entity must conduct prompt good-faith investigation upon awareness of a possible breach; notify affected CO residents unless investigation determines misuse has not occurred and is not reasonably likely.
- Timing: notice within the most expedient time possible, without unreasonable delay, and **no later than 30 days** after determination that a breach occurred (one of the shorter statutory deadlines nationally).
- Large-scale breach (>1,000 CO residents): must also notify nationwide consumer reporting agencies of the anticipated notification date and approximate number of residents affected.

## 5. State Law Referencing/Incorporating PCI-DSS
- No Colorado statute expressly requires PCI-DSS compliance by name for general commerce.
- C.R.S. § 6-1-713 (destruction of PII) and § 6-1-713.5 (reasonable security procedures) require businesses to implement reasonable security practices/procedures for personal identifying information, scaled to business size/nature and type of PII — framework-agnostic, similar to CA's § 1798.81.5.
- PCI-DSS itself applies to CO merchants as a card-network contractual requirement, not as direct state statutory mandate. **Not confirmed whether any CO agency rule cross-references PCI-DSS by name — verify with counsel.**

## 6. State EFT/ACH-Specific Statutes (beyond federal Reg E)
- No Colorado-specific consumer EFT/ACH statute beyond incorporation of federal EFTA/Reg E was identified in this pass. **Not confirmed — verify with counsel** if a dedicated CO EFT statute exists outside tax/fee remittance contexts.

## Sources
- [C.R.S. § 5-2-212 (2024) — Justia](https://law.justia.com/codes/colorado/title-5/consumer-credit-code/article-2/part-2/section-5-2-212/)
- [Colorado Opens the Door to Surcharging — Arnall Golden Gregory](https://www.agg.com/news-insights/publications/colorado-opens-the-door-to-surcharging-five-key-takeaways/)
- [SB21-091 — Colorado General Assembly](https://leg.colorado.gov/bills/sb21-091)
- [C.R.S. § 6-1-732 — Justia](https://law.justia.com/codes/colorado/title-6/fair-trade-and-restraint-of-trade/article-1/part-7/section-6-1-732/)
- [HB21-1239 — Colorado General Assembly](https://leg.colorado.gov/bills/hb21-1239)
- [HB21-1239 FAQ — Colorado AG](https://coag.gov/app/uploads/2022/01/HB-21-1239-FAQ-Sheet_Final-120721-1.pdf)
- [C.R.S. § 6-1-716 (2024) — Justia](https://law.justia.com/codes/colorado/title-6/fair-trade-and-restraint-of-trade/article-1/part-7/section-6-1-716/)
- [Colorado's Consumer Data Protection Laws FAQ — Colorado AG](https://coag.gov/resources/data-protection-laws/)
