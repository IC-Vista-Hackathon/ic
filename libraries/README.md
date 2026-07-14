# Libraries

Shared .NET libraries used across agents and services. Libraries use the `IC.<Capability>` naming
convention and are added to [`IC.slnx`](../IC.slnx).

`IC.Agentic.Orchestration` is the established framework-neutral orchestration library for the
Biller Experience. Microsoft Agent Framework and provider-specific types stay behind its public
abstractions.

Create further libraries only when a second consumer exists. Likely candidates from the original
design are:

- `IC.Domain.Models` for entity schemas from [`design/entities.md`](../design/entities.md)
- `IC.ServiceClients` for typed deterministic-service clients
- `IC.AgentTools` for tool definitions shared by AI Foundry agents

Public wire contracts belong under `contracts/`, not in implementation libraries.
