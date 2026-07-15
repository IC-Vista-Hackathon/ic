# New Hampshire Payment Compliance — Internal Reference

Not legal advice. Verify with counsel before relying on any point below.

## 1. Autopay/Recurring Payment Authorization — Credit Card vs ACH
- No NH-specific restriction found requiring recurring/autopay to be funded via ACH/bank debit only (no FL-style utility/insurance carve-out identified).
- NH Banking Dept. materials reviewed (RSA 361-A retail installment sales FAQ) do not address recurring-payment funding source.
- Not confirmed — verify with counsel if targeting a specific regulated vertical (e.g., insurance, utilities, municipal billing) in NH.

## 2. Credit Card Surcharge / Convenience Fee Law
- No NH statute prohibits or restricts credit card surcharging; surcharging is legal in NH.
- A 2013 bill (HB682) would have banned surcharges outright — it was not enacted, reinforcing that NH has no surcharge-ban statute.
- Debit card surcharging is prohibited — this is a card-network/federal rule applied nationwide, not an NH-specific statute.
- No NH-specific surcharge cap statute identified; absent state law, surcharges are governed by federal Truth in Lending Act guidance (commonly cited ceiling ~4%) and card network rules (Visa caps at 3%) — confirm current network caps separately, they are not NH law.
- NH RSA 80:52-c addresses electronic payment of certain state/local fees — municipal context, not general merchant surcharge law; confirm applicability if relevant to municipal billing.

## 3. UDAP / Consumer Protection — Recurring Billing, Negative Option, Autorenewal
- Consumer Protection Act: **RSA 358-A**, "Regulation of Business Practices for Consumer Protection."
  - **RSA 358-A:2** — core "Acts Unlawful" provision (unfair or deceptive acts/practices in trade or commerce); general catch-all under which recurring-billing or negative-option practices would likely be evaluated.
  - **RSA 358-A:10** — private right of action; **RSA 358-A:4** — AG enforcement.
- RSA 358-A:2 itself does not contain a general, freestanding automatic-renewal/negative-option provision in the text reviewed (its enumerated unlawful acts cover items like false advertising, gift certificates, etc.).
- NH does have a targeted automatic-renewal statute for health club memberships: **RSA 358-I** (length of membership contracts) and **RSA 358-S:5** ("Length of Membership Contract; Automatic Renewal; Required Membership Options") — narrow vertical, not general consumer contracts.
- No general/omnibus NH auto-renewal statute (comparable to CA ARL or VT 9 V.S.A. §2454a) was confirmed for consumer contracts broadly.
- Not confirmed — whether a payments/billing autopay-renewal scenario at InvoiceCloud would fall under RSA 358-A:2's general UDAP catch-all vs. a specific carve-out; verify with counsel, especially given the NH Law Library itself could not point to a single controlling autorenewal statute outside 358-I/358-S.

## 4. Data Breach Notification — Payment Card Data
- Right to Privacy chapter: **RSA 359-C**, notification requirement at **RSA 359-C:20** ("Notification of Security Breach Required").
- "Personal information" definition — **RSA 359-C:19, IV** — includes name + account number, credit card number, or debit card number in combination with any required security code/access code/password permitting access to a financial account (i.e., payment card data alone, without the access code, is generally not sufficient to trigger the definition — confirm exact wording with counsel).
- Notification timing: "as soon as possible" — no fixed statutory day-count deadline, but duty to act promptly after determining misuse has occurred/is likely/cannot be ruled out.
- Notice to NH Attorney General required; if >1,000 NH residents affected, must also notify consumer reporting agencies.
- Enforcement: AG Consumer Protection and Antitrust Bureau (administrative) plus private right of action.

## 5. State Law Referencing/Incorporating PCI-DSS
- Not confirmed — no NH statute found that codifies or explicitly references PCI-DSS compliance requirements (unlike, e.g., Nevada/Minnesota-style card-data statutes).
- No NH statute found imposing a specific payment-card-data retention time limit (contrast with Minnesota's 48-hour post-authorization retention limit for certain card data) — verify with counsel.
- NH's other data-security statute activity (per secondary sources) is concentrated in health-information privacy, not general payment-card PCI codification.

## 6. State EFT/ACH-Specific Statutes Beyond Reg E
- No NH consumer-facing EFT/ACH statute beyond federal Reg E was confirmed.
- NH does have an EFT statute, but it is tax-administration focused: **RSA 21-J:3, XXI** with implementing rule **Rev 2500** (NH Dept. of Revenue Administration) governing ACH debit/credit for state tax payments — not a general consumer EFT/ACH law.
- Not confirmed — whether any provision in NH Title XXXV (banking) or Title XXXVI (money transmitters, RSA 399-G) imposes additional ACH-specific consumer requirements; verify with counsel/NH Banking Dept. if handling NH-domiciled ACH origination at scale.

## Sources
- [RSA 361-A FAQ — New Hampshire Banking Department](https://www.banking.nh.gov/rsa-361-faq)
- [Guide to Credit Card Surcharging Laws in New Hampshire — Flexpoint](https://www.getflexpoint.com/credit-card-surcharging-us-states/new-hampshire)
- [New Hampshire Credit Card Surcharge Laws — Merchant Cost Consulting](https://merchantcostconsulting.com/lower-credit-card-processing-fees/new-hampshire-surcharge-laws/)
- [New Hampshire HB682 (2013) — LegiScan](https://legiscan.com/NH/text/HB682/id/891164)
- [RSA 80:52-c — Justia](https://law.justia.com/codes/new-hampshire/title-v/chapter-80/section-80-52-c/)
- [RSA Chapter 358-A — NH General Court Table of Contents](https://gc.nh.gov/rsa/html/xxxi/358-a/358-a-mrg.htm)
- [RSA Chapter 358-A (2025) — Justia](https://law.justia.com/codes/new-hampshire/title-xxxi/chapter-358-a/)
- [Automatic Renewal Clauses — NH Law About... (NH Courts Law Library)](https://courts-state-nh-us.libguides.com/automaticrenewal)
- [RSA 358-S:5 — NH General Court](https://www.gc.nh.gov/rsa/html/XXXI/358-S/358-S-5.htm)
- [RSA 359-C:20 — NH General Court](https://gc.nh.gov/rsa/html/XXXI/359-C/359-C-20.htm)
- [RSA 359-C:20 (2025) — Justia](https://law.justia.com/codes/new-hampshire/title-xxxi/chapter-359-c/section-359-c-20/)
- [Security Breach Notifications — New Hampshire Department of Justice](https://www.doj.nh.gov/citizens/consumer-protection-antitrust-bureau/security-breach-notifications)
- [RSA 359-C:19 — NH General Court](https://gc.nh.gov/rsa/html/XXXI/359-C/359-C-19.htm)
- [New Hampshire Cybersecurity Laws You Should Know (2026) — PivIT Strategy](https://pivitstrategy.com/new-hampshire-cybersecurity-laws-you-should-know-2026/)
- [Nevada and New Hampshire Add Data Security and Privacy Laws — Kelley Drye](https://www.kelleydrye.com/viewpoints/client-advisories/nevada-and-new-hampshire-add-data-security-and-privacy-laws)
- [Rev 2500 Electronic Transfers and Filing — NH Dept. of Revenue Administration](https://www.revenue.nh.gov/sites/g/files/ehbemt736/files/documents/rev-2500-initial-proposal-text-revised.pdf)
- [Rev 2500 — NH General Court](https://gc.nh.gov/rules/state_agencies/rev2500.html)
