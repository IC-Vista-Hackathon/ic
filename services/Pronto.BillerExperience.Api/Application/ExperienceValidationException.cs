using Pronto.BillerExperience.Contracts.V1.Experiences;

namespace Pronto.BillerExperience.Api.Application;

public sealed class ExperienceValidationException(
    string message,
    IReadOnlyList<ComplianceFinding> findings) : Exception(message)
{
    public IReadOnlyList<ComplianceFinding> Findings { get; } = findings;
}
