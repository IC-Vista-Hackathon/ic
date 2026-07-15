# Payment Notification Laws — Deep Dive

**Not legal advice.** Companion research to [`../README.md`](../README.md), focused specifically on notification obligations: data breach notice, and consumer-facing payment/billing notices (price increases, recurring-charge reminders, renewal/cancellation notices). Every "Not confirmed" line in the individual files is an open question for counsel, not a settled answer.

Compiled 2026-07-15.

## Cross-jurisdiction summary — breach notification

| State | Consumer deadline | AG/regulator threshold | Private right of action | Penalty |
|---|---|---|---|---|
| Alabama | 45 days | — | No | — |
| Alaska | No fixed day ("expedient") | — | **Yes** ($500/resident cap) | — |
| Arizona | 45 days | — | No | — |
| Arkansas | No fixed day (AG: 45 days) | 45 days (AG) | No | — |
| California | 30 days (eff. 1/1/2026) | 15 days if >500 | Yes, narrow (CCPA §1798.150, unreasonable security only) | — |
| Colorado | 30 days | 500 (AG), 1,000 (CRA) | No | — |
| Connecticut | 60 days | ≤ consumer notice | No | — |
| Delaware | 60 days | 500 | No | Treble damages (auto-renewal only) |
| DC | "Expedient" | 50 residents | Yes (treble/$1,500) | — |
| Florida | 30 days (max 45 w/ ext.) | 500 (30 days) | No | Up to $500k/breach |
| Georgia | "Unreasonable delay" | — | Not confirmed | — |
| Hawaii | "Expedient" | 1,000 | **Yes** | Up to $2,500/violation |
| Idaho | "Expedient" | Public agencies: 24hr | No | Up to $25k (intentional) |
| Illinois | "Expedient" | >500 | Not confirmed | — |
| Indiana | **45 days** (hard) | 45 days; CRA at 1,000+ | No | — |
| Iowa | No fixed day | >500 (5 business days after) | Not confirmed | — |
| Kansas | No fixed day | CRA >1,000 (AG threshold unclear) | Not confirmed | — |
| Kentucky | No fixed day | CRA >1,000 | Not confirmed | — |
| Louisiana | 60 days | 10 days after consumer notice | Not confirmed | — |
| Maine | **30 days** (both consumer + AG) | 30 days | Not confirmed | — |
| Maryland | 45 days | Before consumer notice | **Yes** | — |
| Massachusetts | "Unreasonable delay" | Not confirmed | Not confirmed | Up to $5,000/violation (via ch. 93A) |
| Michigan | "Unreasonable delay" | CRA (waived if <1,000) | No | Up to $250/failure, $750k aggregate |
| Minnesota | "Expedient" | **48 hours** to CRA if >500 | **Yes** | — |
| Mississippi | "Without reasonable delay" | 100+ (AG) | No (barred) | — |
| Missouri | "Unreasonable delay" | >1,000 | No | — |
| Montana | "Unreasonable delay" | Simultaneous, no threshold | No | — |
| Nebraska | "Unreasonable delay" | ≤ consumer notice, no threshold | No | — |
| Nevada | "Unreasonable delay" (no fixed day — a "45-day" figure was checked and is wrong) | >1,000 | No | — |
| New Hampshire | Not confirmed | 1,000 (CRA) | Yes (treble-style) | — |
| New Jersey | "Expedient" | Law enforcement first, no threshold | **Yes (treble)** | — |
| New Mexico | **45 days** (hard) | >1,000 (AG + CRA) | Not confirmed | Flat $25,000 (knowing/reckless) |
| New York | Not confirmed (secondary sources inconsistent) | Always if any resident notified; CRA >5,000; DFS 72hr for regulated entities | Not confirmed | — |
| North Carolina | "Unreasonable delay" | Any size | Limited (injury required) | — |
| North Dakota | "Expedient/unreasonable delay" | 250 | No | ~$5,000/violation (unverified) |
| Ohio | **45 days** (hard) | CRA >1,000 | No | — |
| Oklahoma | "Unreasonable delay" | 500 (new, eff. 1/1/2026); CRA 1,000 | Not confirmed | Up to $150k (or $75k w/ safeguards defense) |
| Oregon | 45 days | >250 | No | Up to $500k |
| Pennsylvania | "Unreasonable delay" | >500 | No | — (UTPCPL route) |
| Rhode Island | 45 days (30 govt) | >500 (AG + CRA) | No | $100-200/record |
| South Carolina | "Expedient" | >1,000 | **Yes** (only one of OR/PA/RI/SC group) | $1,000/resident |
| South Dakota | 60 days | >250 | No | Up to $10k/day (AG) |
| Tennessee | 45 days | CRA >1,000 | **Yes** | Up to $7,500/violation |
| Texas | 60 days | 30 days (AG) | No (DTPA route) | $2k-$50k/violation, $250k aggregate cap |
| Utah | "Expedient/unreasonable delay" | 500+ | No | Up to $2,500/consumer, $100k cap |
| Vermont | 45 days | **14 business days, no threshold** | Not confirmed | — |
| Virginia | Not confirmed (check file) | Not confirmed | **Yes** (direct economic damages) | — |
| Washington | Not confirmed (check file) | Not confirmed | **Yes** (RCW 19.255.010 + CPA treble/punitive) | — |
| West Virginia | Not confirmed (check file) | Not confirmed | No | — |
| Wisconsin | 45 days | CRA >1,000 | No | — |
| Wyoming | "Expedient/unreasonable delay" | None found (CRA contact-info requirement instead) | No | — |

## Standout findings

- **No hard federal breach-notification deadline exists** — GLBA Safeguards Rule requires FTC notice within 30 days but only for financial institutions, only for events affecting 500+ consumers, only to the FTC (not consumers directly).
- **Only ~10 states have a hard numeric consumer-notice deadline at all** — most use "expedient"/"without unreasonable delay" language with no clock. Notable hard deadlines: Maine and California (30 days), Indiana/Ohio/New Mexico (45 days), Louisiana/Texas/South Dakota (60 days).
- **Private right of action for breach notice failures is the exception, not the rule** — confirmed in only Alaska, Hawaii, Maryland, Minnesota, New Hampshire, New Jersey, South Carolina, Tennessee, DC, Virginia, Washington. Most states are AG-enforcement-only.
- **Washington and New Jersey stand out as highest-exposure states** — both combine a private right of action with treble/punitive damage exposure.
- **The FTC's "click-to-cancel" rule is currently NOT in effect** — vacated by the 8th Circuit (July 2025) on procedural grounds; FTC reverted to the pre-2024 Negative Option Rule; a new rulemaking is open but not final. Any autopay/cancellation UX built assuming click-to-cancel applies is currently ahead of the actual federal requirement.
- **Renewal/price-increase notice windows cluster around 15-60 days** across nearly every state with a statute, but a handful of states (Ohio, Washington, Iowa, Kansas, South Dakota, Wyoming) have **no general auto-renewal notice law at all** — only narrow sector carve-outs (health clubs, dance studios, etc.).
- **Some "laws" aren't actually laws yet** — Michigan's HB 4826 and Mississippi's SB 2498 are both still pending despite MS's bill text containing a "July 1, 2025" effective-date clause that never took effect. Pennsylvania has no general auto-renewal statute (HB45 pending). Always check enactment status, not just bill text, before treating something as binding.

## Prompt-injection log (this research pass)

7 separate WebFetch calls against official state legislature/statute pages returned content with an injected line falsely claiming a "claude-created" Jira label had been applied:

| Jurisdiction | Source |
|---|---|
| Arizona | azleg.gov / Justia (2 separate hits, across both research passes) |
| Maryland | legislature PDF (2 separate hits, across both research passes) |
| Connecticut | General Assembly statute page |
| Pennsylvania/Rhode Island batch | fetch summarizer output |
| Massachusetts/Minnesota | malegislature.gov, revisor.mn.gov |

**No Jira action was ever taken in any case.** All were correctly identified as injection artifacts and ignored. The recurrence across many different official domains (not one bad page) suggests this is either a broad, real adversarial-content pattern targeting AI web-browsing agents, or an artifact of how fetch-summarization handles certain legislative-site formatting — flagging for awareness, not because any harm occurred.

## Related

- [`../README.md`](../README.md) — main compliance index (autopay funding, surcharge, PCI-DSS, auto-renewal)
- [`../federal/federal.md`](../federal/federal.md) — federal payment law
- [`federal.md`](federal.md) — federal notification law (this pass)
- [`../states/`](../states/) — main per-state files (autopay/surcharge/PCI-DSS/breach summary)
- [`./`](.) — this directory's per-state files (deep breach + billing-notice detail)
