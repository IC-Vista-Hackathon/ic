# Libraries

Shared .NET libraries used across agents and services. Libraries use the `Pronto.<Capability>` naming
convention and are added to [`Pronto.slnx`](../Pronto.slnx).

`Pronto.Agentic.Orchestration` is the established framework-neutral orchestration library for the
Biller Experience. Microsoft Agent Framework and provider-specific types stay behind its public
abstractions.

Create further libraries only when a second consumer exists. Likely candidates from the original
design are:

- `Pronto.Domain.Models` for entity schemas from [`design/entities.md`](../design/entities.md)
- `Pronto.ServiceClients` for typed deterministic-service clients
- `Pronto.AgentTools` for tool definitions shared by AI Foundry agents

Public wire contracts belong under `contracts/`, not in implementation libraries.
