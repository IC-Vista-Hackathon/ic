# IC — Design

Agent-native EBPP: a biller self-onboards through a chat agent, previews their own branded payer
portal on fake data, "buys" the platform, and publishes it live. A payer then uses the live site,
assisted by a payer-side agent team.

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

## Stretch

Email/SMS communications (receipts, reminders); mock biller website with the payer portal
seamlessly embedded.
