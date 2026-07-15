# Washington — Notification Law Reference

**Disclaimer:** Internal compliance reference only. Not legal advice. Verify with counsel before relying on any item below, especially anything marked "Not confirmed."

## 1. Data Breach Notification

- **Statute:** RCW 19.255.010 (Personal information — Notice of security breaches); parallel provision RCW 42.56.590 applies to state/local agencies.
- **Trigger:** Unauthorized acquisition of data that compromises the security, confidentiality, or integrity of unsecured "personal information."
- **Consumer notice deadline:** "Most expedient time possible, without unreasonable delay, and **no more than 30 calendar days** after the breach was discovered," subject to law-enforcement delay.
- **AG notice:** Required if the breach affects **more than 500 Washington residents**, within the **same 30-day** window.
- **Payment card number + security code covered as "personal information":** **Yes, explicitly** — "account number or credit or debit card number, in combination with any required security code, access code, or password."
- **Encryption safe harbor:** **Yes** — no notice required if the personal information was encrypted, or if account information was encrypted/otherwise secured at the time of breach; entities certified compliant with PCI-DSS may also rely on this in relevant circumstances. No-notice exception also applies where breach is **not reasonably likely to cause harm**.
- **Penalties:** Enforced via the Washington Consumer Protection Act (ch. 19.86 RCW) — AG civil action; per-violation CPA penalties apply (standard CPA penalty framework, not a bespoke breach-specific cap).
- **Private right of action:** **Yes** — RCW 19.255.010 expressly allows a consumer injured by a violation to bring a civil action for damages; treble/punitive damages up to $1,000 available for willful CPA violations, actual damages otherwise. Washington is one of the few states with an explicit statutory private right of action for breach notice failures.

## 2. Consumer-Facing Payment/Billing Notices

- **No dedicated general consumer automatic-renewal/subscription statute currently in force.** HB 1441 (2023–24, would have regulated auto-renewal in **business** equipment/services contracts) **did not pass** ("Introduced — Dead," Jan. 2024). Do not cite a Washington-specific consumer auto-renewal statute as currently in effect — **not confirmed / does not appear to exist** as of this review.
- Subscription/auto-renewal practices are instead policed under the general **Consumer Protection Act (ch. 19.86 RCW)** as unfair/deceptive acts; the WA AG has actively surveyed consumers and signaled enforcement interest in "pre-checked box" dark patterns and undisclosed auto-enrollment, but this is enforcement posture, not a specific notice-timing statute.
- **Telephone/door-to-door sales cancellation notice:** RCW 19.158 (Commercial Telephone Solicitation Act) requires written confirmation of a phone-initiated sale, including cancellation address/rights, with a **3-business-day** right to cancel after receiving written confirmation — relevant only if a sale/enrollment originates from a telephone solicitation.
- **Insurance-specific renewal/cancellation notices** (RCW 48.18.290, .2901, .292) exist but are insurance-policy-specific, not general consumer billing/autopay law — flag only if selling/administering insurance products.
- No Washington-specific statute was found mandating pre-price-increase notice or a formal cancellation-confirmation notice for general recurring consumer billing. **Not confirmed — verify with counsel**, and monitor for newly introduced legislation (WA has repeatedly attempted auto-renewal bills in recent sessions).

## 3. State EFT/ACH Notice Requirements Beyond Reg E

- RCW 62A.4A-108 (Washington's UCC Article 4A) **excludes** funds transfers governed by the federal EFTA from Article 4A — consumer preauthorized/recurring EFTs fall to federal Reg E, not a distinct Washington regime.
- No Washington-specific consumer statute imposing notice-of-preauthorized-transfer or change-in-terms obligations beyond Reg E (12 CFR 1005.10) was identified.
- **Not confirmed — verify with counsel** if any Washington Automated Financial Transactions provisions (ch. 19.200 RCW) apply to your specific ACH origination model; that chapter was not fully reviewed and may be narrowly scoped (state agency financial transactions).

## Sources

- [RCW 19.255.010 — Notice of security breaches](https://apps.leg.wa.gov/Rcw/default.aspx?cite=19.255.010)
- [Chapter 19.255 RCW](https://app.leg.wa.gov/rcw/default.aspx?cite=19.255&full=true)
- [Foster Garvey — The Washington State Data Breach Notification Act (PDF)](https://www.foster.com/assets/htmldocuments/pdfs/WSDataBreach.pdf)
- [WA HB 1441 (2023-24) bill summary — died in committee](https://app.leg.wa.gov/billsummary?BillNumber=1441&Year=2023)
- [RCW 19.158.120 — Cancellation of purchases, telephone solicitation](https://app.leg.wa.gov/rcw/default.aspx?cite=19.158.120)
- [WA AG — Cancellation Rights](https://www.atg.wa.gov/cancellation-rights)
- [RCW 62A.4A-108 — Relationship to Electronic Fund Transfer Act](https://app.leg.wa.gov/RCW/default.aspx?cite=62A.4A-108)
- [Regulatory Oversight — WA AG surveys consumers on auto-renewal](https://www.regulatoryoversight.com/2022/10/washington-ag-surveys-consumers-about-auto-renewal-subscription-services-with-enforcement-actions-likely-to-follow/)
