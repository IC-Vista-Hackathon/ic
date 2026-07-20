# Pronto — Functional Requirements & Acceptance Criteria

Status: **living document**. This is the shared definition of what Pronto's biller-onboarding
experience is supposed to *do*, written so it can be checked automatically. Every requirement has
acceptance criteria expressed as Gherkin, and each Gherkin scenario maps to a functional test in
`tests/Pronto.Functional.Tests` that runs against a **deployed** environment (nonprod by default).

When behavior changes, change the requirement here **first**, then the test. A requirement without
a test is an aspiration; a test without a requirement is a mystery.

## How to read this

- **FR-n** — a numbered functional requirement.
- **Acceptance** — Gherkin scenarios. `@known-gap` tags a behavior Pronto is *supposed* to have but
  does **not** satisfy today; its test is written to fail now and pass once the feature is fixed
  (see `docs/pronto-functional-testing-policy.md`).
- **Test** — the class/method in `tests/Pronto.Functional.Tests` that enforces it.

Product context and architecture live in `README.md`, `CLAUDE.md`, and `design/`. The load-bearing
principle for these requirements: **agents research and configure; deterministic services own
persistence, validation, state, and money movement.** Requirements below are written against the
observable HTTP/UI contract, not internal implementation.

---

## FR-1 — A biller can start onboarding and lands in billing discovery

Creating a biller opens an onboarding session in the `collecting_information` state with a first
discovery question, and returns an initial draft revision the Studio can render.

```gherkin
Feature: Start onboarding

  Scenario: Creating a biller opens billing discovery and returns a draft
    When I create a biller "Functional Test Co" of type "utility"
    Then the response includes a biller_id
    And the onboarding session state is "collecting_information"
    And the session has a current discovery question
    And a draft revision in state "draft" is returned for that biller
```

Test: `OnboardingBootstrapTests.CreatingBillerOpensDiscoveryAndReturnsDraft`,
`OnboardingBootstrapTests.DraftIsRetrievableAfterCreation`

---

## FR-2 — Onboarding seeds demo invoices for the payer preview

A newly created biller has demo invoices seeded (against the demo payer account) so the previewed
payer experience shows a realistic bill list.

```gherkin
Feature: Seed preview data

  Scenario: Creating a biller seeds demo invoices
    When I create a biller
    Then at least one demo invoice exists for the seeded demo account
```

Test: `OnboardingBootstrapTests.CreatingBillerSeedsDemoInvoices`

---

## FR-6 — Demo invoices are agentic, relevant to the biller, not a hard-coded template

The seeded demo invoices must reflect what the biller actually bills for — derived agentically from
the biller's type, name, website, and discovered billing categories. They must **not** be a fixed,
hand-authored set keyed on `bill_type`.

> **Resolved (issue #1).** Onboarding now derives biller-relevant demo line items from the biller's
> name, website, and vertical on the Biller Experience side (`DeterministicSeedInvoiceGenerator`) and
> passes them to the Invoice service, which persists them (`SeedInvoicesRequest.invoices`). The
> hand-authored `bill_type` `insurance`/`other` branches in
> `Pronto.Invoice.Api/Seeding/FakeInvoiceFactory.cs` were removed, so an apparel store onboarded as
> `other` no longer gets HOA invoices and two unrelated billers no longer get identical sets.

```gherkin
Feature: Agentic demo invoices
  Scenario: Seeded invoices are relevant to the biller
    Given an online apparel store onboarded as bill type "other"
    When I read its seeded demo invoices
    Then no invoice mentions "HOA", "special assessment", or the Christmas-fine joke

  Scenario: Two unrelated billers do not get identical invoices
    Given an apparel store and a parks district, both bill type "other"
    When I read each biller's seeded demo invoices
    Then the two invoice sets are not identical
```

Tests: `AgenticInvoiceSeedingTests.SeededInvoicesAreRelevantToBillerNotFixedTemplate`,
`AgenticInvoiceSeedingTests.TwoUnrelatedBillersDoNotGetIdenticalSeededInvoices`

Boundary note: the agent chooses/curates *content*; the deterministic Invoice service still owns
persistence, validation, and account scoping. Seeded data must stay demo-only and must not touch
real payment state.

---

## FR-3 — The research agent scrapes the biller's real website for brand evidence

When a reachable HTTPS biller website is supplied, the biller research agent runs to completion and
returns first-party, **cited** brand evidence: at minimum organization/display name, dominant/primary
colors, a logo or wordmark URL, tagline, and tone/style. Research is bounded and safe (same-origin
HTTPS only, SSRF-guarded); off-domain or unsafe targets are rejected.

> **Enforced.** `BrandEvidenceExtractor` crawls the same-origin site and pulls colors, logo/wordmark,
> theme-color, favicon, and typography, and the research event is recorded on the activity feed.
> Example sites that yield brand evidence: `https://www.happypantsnyc.com`,
> `https://thankful-sea-0d2febf0f.7.azurestaticapps.net`.

```gherkin
Feature: Biller website research
  Scenario: Research runs to completion for a reachable biller site
    Given a biller with website "https://www.happypantsnyc.com"
    When the onboarding orchestration runs
    Then the agent activity includes a research event that completed or degraded
    And it is not absent, skipped, or failed
```

Test: `BrandResearchTests.ResearchAgentRunsToCompletionForReachableSite`

Failure visibility: research failures must be diagnosable — the activity feed must show a research
event with a status and error code, not silence. Partial/degraded results are kept (degraded), not
discarded. Unsafe/off-domain targets stay rejected.

---

## FR-4 — Researched brand evidence flows into the previewed draft

Once research succeeds, the draft the biller previews reflects the supported brand evidence: the
logo pulled from the site is present, and colors are the researched brand colors rather than a
generic default.

> **Enforced.** `ResearchBrandApplicator` maps the extracted evidence onto the draft, so the
> previewed brand carries the site's logo and researched colors rather than a generic default.

```gherkin
Feature: Brand evidence reaches the draft
  Scenario: Onboarding derives brand identity from the biller site
    Given a biller with website "https://www.happypantsnyc.com"
    When the onboarding orchestration runs
    Then the draft brand has a logo asset derived from the site
    And the draft primary color is not the generic default "#085368"
```

Test: `BrandResearchTests.OnboardingDerivesBrandIdentityFromBillerSite`

---

## FR-5 — Brand is researched before it is presented (no fabricated branding first)

The initial user experience must not present fabricated branding as if it were research-backed. The
onboarding bootstrap draft (returned by `POST /billers`, before any research or chat) must **not**
assert a specific brand color or design brief. Brand stays unset / clearly "not yet researched"
until the research agent produces evidence; only then does the preview show it. If research fails,
the UI communicates that the brand is unverified rather than silently using invented values.

> **Enforced.** `BillerOnboardingService.CreateInitialDefinition` no longer fabricates a brand color,
> font, or design brief at creation time: the bootstrap draft (returned by `POST /billers`, before
> research) asserts no brand claim, and branding appears only once research produces evidence.

```gherkin
Feature: Research before presentation
  Scenario: Bootstrap draft asserts no brand color before research
    When I create a biller with a website but have not run research
    Then the bootstrap draft has no brand primary color
    And it is not the fabricated default "#085368"

  Scenario: Bootstrap draft fabricates no design brief before research
    When I create a biller with a website but have not run research
    Then the bootstrap draft has no design brief
```

Tests: `PrematureBrandingTests.BootstrapDraftDoesNotAssertBrandColorBeforeResearch`,
`PrematureBrandingTests.BootstrapDraftDoesNotFabricateDesignBriefBeforeResearch`

Note: exact "unbranded" representation (null vs. an explicit `researched: false` marker) is a design
choice; the test currently asserts absence and should be updated in lock-step when that shape is
decided. The invariant that must hold is: **no brand claim before evidence.**

---

## FR-7 — Billing discovery accepts valid category answers and updates a typed profile

Required billing questions are collected; valid answers update a typed billing profile and advance
the session. The model may interpret/phrase answers, but server-owned discovery rules determine
readiness, and valid category responses are not silently rejected.

> **Observed risk.** A plausible category answer ("Online apparel orders") was rejected in a nonprod
> probe with "I couldn't safely map that to a list of billing categories," leaving the session in
> `collecting_information`. Capturing this as a stable test needs a confirmed set of accepted phrasings;
> tracked here so a discovery-acceptance test is added once the expected mapping rules are pinned down.

Test: _to be added_ (see testing policy — requirement defined before test).

---

## FR-8 — Approval and publish stay gated by deterministic services

User approval applies only to the resulting draft and cannot bypass workflow gates: publish requires
a passing compliance check enforced server-side, and only the payment path (post explicit payer
confirmation) moves money. Agents cannot set readiness, approve, or publish.

The server-side publish gate is a **deterministic compliance suite** that emits a signed, auditable
attestation (F8): a compliant revision publishes and the 202 response carries an `attestation` with
`passed: true`, a signature, and per-checker results; a revision that violates a hard checker is
blocked at publish with a 422 problem-details response whose `findings` name the failing checker —
even when it passed the advisory approval review. The color-contrast checker is a good black-box
probe: a valid-hex but sub-WCAG-AA palette is accepted at approval yet blocked at publish.

```gherkin
Feature: Deterministic publish gate with signed attestation
  Scenario: A compliant revision publishes with a verifiable attestation
    Given an approved revision whose palette clears WCAG AA and whose fees are disclosed
    When I publish it
    Then the response is 202 Accepted
    And it carries a compliance attestation with passed=true, a signature, and checker results

  Scenario: A hard checker blocks publish after an advisory-clean approval
    Given a revision with a valid-hex but sub-WCAG-AA brand color that approval accepts
    When I publish it
    Then the response is 422 with a finding on "brand.primary_color"
```

Tests: `CompliancePublishGateTests.PublishProducesSignedAttestationForCompliantRevision`,
`CompliancePublishGateTests.PublishIsBlockedByHardComplianceChecker`

---

## FR-9 — Control plane and browser telemetry are healthy

The deployed API answers liveness/readiness, and the public browser-telemetry config endpoint
returns a usable shape (this backs the PWA/Studio observability smoke tests).

```gherkin
Feature: Deployed health
  Scenario: Liveness and readiness are OK
    Then GET /api/health/live returns 200
    And GET /api/health/ready returns 200

  Scenario: Telemetry config is exposed
    Then GET /api/public/telemetry returns a connection string and sampling percentage
```

Tests: `HealthAndConfigTests.ApiLivenessAndReadinessProbesReturnOk`,
`HealthAndConfigTests.PublicTelemetryConfigExposesExpectedShape`

---

## FR-10 — Studio preview runs the shipped bundle against an isolated, seeded, resettable tenant

The Studio preview renders the same built payer PWA as production, but scoped to an isolated
`preview-{billerId}` partition seeded with synthetic demo data (F2). Provisioning seeds that
partition; the served preview config is the current draft with its `biller_id` rewritten to the
preview tenant (so every downstream service call targets the isolated partition); and reset
re-seeds deterministically — a repeat converges on the same seed set rather than accumulating.

```gherkin
Feature: Real-services Studio preview
  Scenario: Provisioning seeds an isolated preview tenant
    Given a biller
    When I provision its preview
    Then I get a preview tenant id of "preview-{billerId}" with a seeded demo account
    And the preview partition has demo invoices for that account

  Scenario: The served preview config is scoped to the preview tenant
    When I fetch the preview config
    Then its biller_id is the preview tenant, not the live biller

  Scenario: Reset is deterministic
    Given a provisioned, seeded preview
    When I reset it
    Then the preview account's seeded invoice count is unchanged (re-seed converges)
```

Tests: `StudioPreviewTests.ProvisioningSeedsAnIsolatedPreviewTenant`,
`StudioPreviewTests.PreviewConfigServesTheDraftScopedToThePreviewTenant`,
`StudioPreviewTests.ResetIsDeterministicAndDoesNotAccumulateSeedData`

---

## Cross-cutting testability requirements

- **Nonprod, never prod.** Functional tests target the nonprod gateway; they must never run against
  `pronto.eastus2.cloudapp.azure.com`.
- **Unique, isolated test data.** Each test biller uses a unique random slug and is purged after the
  run via the nonprod-only maintenance endpoints on each service that owns data it created:
  `DELETE /api/internal/test-data` (biller-experience) and `DELETE /invoices/internal/test-data`
  (seeded demo invoices).
- **Hard vs. degraded.** Tests distinguish outright failures from degraded/advisory research.
- **Diagnostics.** Failures surface the request URI, status code, and response body; research checks
  read the activity feed (agent id, status, error code).
- **Bounded network behavior.** Network-dependent checks poll with bounded timeouts and fail with a
  clear message rather than hanging.

## Known defects surfaced while writing these tests

- **Activity endpoint 404s on an empty run.** `GET /api/billers/{id}/activity` returns 404 when a run
  has zero recorded agent events, even though `GET /api/billers/{id}/session` returns 200 for the same
  run. It should return an empty activity snapshot. The functional client tolerates this today; fix
  and then tighten the client. (Diagnosability, supports FR-3.)
