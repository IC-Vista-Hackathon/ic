using System.Diagnostics;
using System.Text.Json;
using IC.BillerExperience.Api.Application;
using IC.BillerExperience.Api.Infrastructure;
using IC.BillerExperience.Contracts.V1.Billers;
using IC.BillerExperience.Contracts.V1.Deployments;
using IC.BillerExperience.Contracts.V1.Experiences;
using IC.BillerExperience.Contracts.V1.Onboarding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IC.BillerExperience.Api.Controllers;

[ApiController]
[Route("billers")]
public sealed partial class BillersController(
    BillerOnboardingService onboarding,
    ILogger<BillersController> logger,
    IOptions<JsonOptions> jsonOptions) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<OnboardingBootstrapResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OnboardingBootstrapResponse>> Create(
        [FromBody] CreateBillerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await onboarding.CreateAsync(request, cancellationToken);
        LogControllerSuccess(logger, nameof(Create), result.Biller.BillerId, Activity.Current?.TraceId.ToString());
        return CreatedAtAction(nameof(Get), new { billerId = result.Biller.BillerId },
            new OnboardingBootstrapResponse(result.Biller, result.Session, result.Draft));
    }

    [HttpGet("{billerId}")]
    [ProducesResponseType<BillerResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BillerResponse>> Get(string billerId, CancellationToken cancellationToken) =>
        Ok(await onboarding.GetBillerAsync(billerId, cancellationToken));

    [HttpGet("{billerId}/session")]
    [ProducesResponseType<OnboardingSessionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OnboardingSessionResponse>> GetSession(string billerId, CancellationToken cancellationToken) =>
        Ok(await onboarding.GetSessionAsync(billerId, cancellationToken));

    [HttpPost("{billerId}/chat")]
    [ProducesResponseType<OnboardingChatResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OnboardingChatResponse>> Chat(
        string billerId,
        [FromBody] SendOnboardingMessageRequest request,
        CancellationToken cancellationToken) =>
        Ok(await onboarding.SendMessageAsync(billerId, request, cancellationToken));

    [HttpGet("{billerId}/config")]
    [ProducesResponseType<ExperienceRevisionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ExperienceRevisionResponse>> GetConfiguration(
        string billerId,
        CancellationToken cancellationToken) =>
        Ok(await onboarding.GetDraftAsync(billerId, cancellationToken));

    [HttpPatch("{billerId}/config")]
    [ProducesResponseType<ExperienceRevisionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ExperienceRevisionResponse>> UpdateConfiguration(
        string billerId,
        [FromBody] UpdateExperienceRequest request,
        CancellationToken cancellationToken) =>
        Ok(await onboarding.UpdateDraftAsync(billerId, request, cancellationToken));

    [HttpPost("{billerId}/config/approve")]
    [ProducesResponseType<ExperienceRevisionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ExperienceRevisionResponse>> ApproveConfiguration(
        string billerId,
        [FromBody] ApproveExperienceRequest request,
        CancellationToken cancellationToken) =>
        Ok(await onboarding.ApproveAsync(billerId, request, cancellationToken));

    [HttpPost("{billerId}/config/publish")]
    [ProducesResponseType<DeploymentStatusResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<DeploymentStatusResponse>> PublishConfiguration(
        string billerId,
        [FromBody] PublishExperienceRequest request,
        CancellationToken cancellationToken)
    {
        var deployment = await onboarding.PublishAsync(billerId, request, cancellationToken);
        return Accepted(deployment);
    }

    [HttpGet("{billerId}/deployments/{deploymentId}")]
    [ProducesResponseType<DeploymentStatusResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DeploymentStatusResponse>> GetDeployment(
        string billerId,
        string deploymentId,
        CancellationToken cancellationToken) =>
        Ok(await onboarding.GetDeploymentAsync(billerId, deploymentId, cancellationToken));

    [HttpGet("{billerId}/events")]
    public async Task StreamEvents(string billerId, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        var previousState = string.Empty;
        var sentActivityIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            for (var index = 0; index < 240 && !cancellationToken.IsCancellationRequested; index++)
            {
                var (session, activity) = await onboarding.GetSessionActivityAsync(billerId, cancellationToken);
                var state = session.State.ToString();
                if (!string.Equals(previousState, state, StringComparison.Ordinal))
                {
                    var payload = JsonSerializer.Serialize(session, jsonOptions.Value.JsonSerializerOptions);
                    await Response.WriteAsync($"event: workflow\ndata: {payload}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    previousState = state;
                }
                foreach (var item in activity.Where(item => sentActivityIds.Add(item.EventId)))
                {
                    var payload = JsonSerializer.Serialize(item, jsonOptions.Value.JsonSerializerOptions);
                    await Response.WriteAsync($"event: agent_activity\ndata: {payload}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogStreamDisconnected(logger, billerId, Activity.Current?.TraceId.ToString());
        }
        catch (Exception exception)
        {
            LogStreamError(logger, billerId, Activity.Current?.TraceId.ToString(), exception);
            throw;
        }
    }

    [LoggerMessage(1100, LogLevel.Information, "Controller action {Action} completed for biller {BillerId}; trace {TraceId}")]
    private static partial void LogControllerSuccess(ILogger logger, string action, string billerId, string? traceId);

    [LoggerMessage(1101, LogLevel.Debug, "SSE client disconnected for biller {BillerId}; trace {TraceId}")]
    private static partial void LogStreamDisconnected(ILogger logger, string billerId, string? traceId);

    [LoggerMessage(1199, LogLevel.Error, "SSE stream failed for biller {BillerId}; trace {TraceId}")]
    private static partial void LogStreamError(ILogger logger, string billerId, string? traceId, Exception exception);
}

public sealed record OnboardingBootstrapResponse(
    BillerResponse Biller,
    OnboardingSessionResponse Session,
    ExperienceRevisionResponse Draft);
