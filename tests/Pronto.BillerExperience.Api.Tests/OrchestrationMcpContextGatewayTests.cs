using System.Text.Json;
using Pronto.BillerExperience.Api.Infrastructure.Mcp;
using Pronto.BillerExperience.Contracts.V1.AgentContext;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class OrchestrationMcpContextGatewayTests
{
    [Theory]
    [InlineData("candidateArtifact", AgentContextEntryKind.CandidateArtifact)]
    [InlineData("unresolvedQuestion", AgentContextEntryKind.UnresolvedQuestion)]
    public void DeserializeSnapshotAcceptsMcpStringEnums(string kind, AgentContextEntryKind expected)
    {
        using var document = JsonDocument.Parse($$"""
            {
              "billerId":"biller-1",
              "runId":"run-1",
              "version":1,
              "goal":"Build a payment experience",
              "entries":[{
                "entryId":"entry-1",
                "kind":"{{kind}}",
                "agentId":"biller-research",
                "scope":"research",
                "content":"Supported conclusion",
                "sources":["https://example.com/evidence"],
                "external":true,
                "createdAt":"2026-07-15T00:00:00Z"
              }],
              "updatedAt":"2026-07-15T00:00:00Z"
            }
            """);

        var snapshot = OrchestrationMcpContextGateway.DeserializeSnapshot(document.RootElement);

        Assert.Equal(expected, Assert.Single(snapshot.Entries).Kind);
    }

    [Fact]
    public void DeserializeSnapshotRejectsNumericEnums()
    {
        using var document = JsonDocument.Parse("""
            {"billerId":"biller-1","runId":"run-1","version":1,"goal":"goal","entries":[{"entryId":"entry-1","kind":1,"agentId":"agent","scope":"research","content":"fact","sources":[],"external":false,"createdAt":"2026-07-15T00:00:00Z"}],"updatedAt":"2026-07-15T00:00:00Z"}
            """);

        Assert.Throws<JsonException>(() =>
            OrchestrationMcpContextGateway.DeserializeSnapshot(document.RootElement));
    }
}
