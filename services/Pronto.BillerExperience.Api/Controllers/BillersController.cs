using System.Diagnostics;
using System.Text.Json;
using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Deployments;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Pronto.BillerExperience.Api.Controllers;

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

    [HttpPost("{billerId}/purchase")]
    [ProducesResponseType<BillerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BillerResponse>> AdvancePurchase(
        string billerId,
        [FromBody] AdvanceBillerPurchaseRequest request,
        CancellationToken cancellationToken) =>
        Ok(await onboarding.AdvancePurchaseAsync(billerId, request, cancellationToken));

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
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.WriteAsync("retry: 2000\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
        var previousState = string.Empty;
        var lastSequence = long.TryParse(Request.Headers["Last-Event-ID"].FirstOrDefault(), out var parsedSequence)
            ? parsedSequence
            : 0;
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
                foreach (var item in activity.Where(item => item.Sequence > lastSequence).OrderBy(item => item.Sequence))
                {
                    var payload = JsonSerializer.Serialize(item, jsonOptions.Value.JsonSerializerOptions);
                    await Response.WriteAsync($"id: {item.Sequence}\nevent: agent_activity\ndata: {payload}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    lastSequence = item.Sequence;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogStreamDisconnected(logger, billerId, Activity.Current?.TraceId.ToString());
        }
        catch (Exception exception)
        {
            var traceId = Activity.Current?.TraceId.ToString();
            LogStreamError(logger, billerId, traceId, exception);
            if (!Response.HttpContext.RequestAborted.IsCancellationRequested)
            {
                var payload = JsonSerializer.Serialize(new
                {
                    code = "agent_stream_failed",
                    message = "Agent activity is temporarily unavailable.",
                    trace_id = traceId,
                    retryable = true
                }, jsonOptions.Value.JsonSerializerOptions);
                await Response.WriteAsync($"event: stream_error\ndata: {payload}\n\n", CancellationToken.None);
                await Response.Body.FlushAsync(CancellationToken.None);
            }
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
