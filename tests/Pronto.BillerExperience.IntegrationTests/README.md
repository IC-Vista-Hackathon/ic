# Pronto.BillerExperience.IntegrationTests

In-process integration tests that boot real service hosts with
`WebApplicationFactory<Program>` and exercise cross-layer flows (controllers, wire
policy, stores). They run in CI on every PR via `dotnet test Pronto.slnx` — no external
infrastructure required.

Current coverage: Invoice API seed → account-lookup flow and health endpoints
(`InvoiceApiIntegrationTests`). End-to-end Cosmos DB, workflow, and AKS publication
tests will grow here.

The deployed functional equivalent (health + Invoice seed/lookup against a live
cluster) is `scripts/smoke-test.sh`, run by the nonprod and prod deploy workflows.
