namespace Pronto.BillerExperience.Contracts.V1.Events;

public interface IBillerExperienceEvent
{
    string EventId { get; }
    string BillerId { get; }
    DateTimeOffset OccurredAt { get; }
}

public sealed record ExperienceApproved(
    string EventId,
    string BillerId,
    string Revision,
    string ApprovedBy,
    DateTimeOffset OccurredAt) : IBillerExperienceEvent;

public sealed record PublishingRequested(
    string EventId,
    string BillerId,
    string Revision,
    DateTimeOffset OccurredAt) : IBillerExperienceEvent;

public sealed record ExperiencePublished(
    string EventId,
    string BillerId,
    string Revision,
    Uri PublishedUrl,
    DateTimeOffset OccurredAt) : IBillerExperienceEvent;

public sealed record ExperiencePublishFailed(
    string EventId,
    string BillerId,
    string Revision,
    string FailureCode,
    DateTimeOffset OccurredAt) : IBillerExperienceEvent;
