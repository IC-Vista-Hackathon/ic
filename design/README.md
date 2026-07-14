# IC — Design

Agent-native EBPP: a biller self-onboards through a chat agent, previews their own branded payer
portal on fake data, "buys" the platform, and publishes it live. A payer then uses the live site,
assisted by a payer-side agent team.

## Why

Three wins over today's model, where every biller gets a hand-built payer experience through a
months-long onboarding:

1. **Fully custom payer experiences.** Today's portals all follow the same general pattern.
   Config-driven rendering means each biller can have exactly what they want, no shared template
   constraint.
2. **Self-service onboarding.** No three-month setup engagement — a biller can configure and go
   live in about fifteen minutes, unassisted. Cuts sales cost and time-to-live dramatically.
3. **A new market segment.** Small billers (~$200-300 MRR) never cleared the bar to justify a
   three-month onboarding effort. Fifteen-minute self-service does — this opens up small
   businesses and firms as viable customers, not just easier sales for existing targets.

## Flow (from high-level architecture)

```
Biller opens onboarding webapp
  → Initial form (org name, website, bill type)
  → Onboarding Agent (chat-driven configuration; research/aesthetics/compliance agents assist)
  → Preview Payer Experience (live, fake data)
      → Payer agents: Bill Intelligence → Financial Planning → Policy → Execution
  → Ready to buy?
      no  → create Biller Account, save progress
      yes → purchase ("real" fake payment) → Biller Dashboard
              → Test Payer Experience | Edit Configuration | Go Live (publish)
  → Post-go-live: payer pays on the published site
```

## Docs

| File | Contents |
|---|---|
| [entities.md](entities.md) | Core data objects |
| [services.md](services.md) | Services and agents, who owns what |
| [contracts.md](contracts.md) | API + agent tool contracts |
| high-level-architecture.drawio | Source diagram |

## Principles

- **Agents configure; services execute.** Agents emit declarative config and tool calls; all money
  movement and persistence goes through deterministic services.
- **Config is the product.** The payer experience is rendered 100% from `BillerConfiguration` —
  no per-biller code.
- **Fake rails, real flows.** Payments are mocked, but every flow (preview, purchase, go-live,
  pay) is the genuine end-to-end path.
- **Isolation is a tier.** Shared infrastructure by default; dedicated deployment as a paid upgrade.
  Shared tier means a static per-biller bundle served from Blob Storage by one shared router
  workload, not a dedicated Deployment per biller — see `services.md`'s Payer Site Router and
  `README.md`'s "AKS publication model" for the current pivot in progress.

## Stretch

Email/SMS communications (receipts, reminders); mock biller website with the payer portal
seamlessly embedded.
