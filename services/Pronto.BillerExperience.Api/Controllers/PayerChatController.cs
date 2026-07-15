using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Application.PayerChat;

namespace Pronto.BillerExperience.Api.Controllers;

/// <summary>
/// Payer-facing entry point for the live portal's agent pipeline. One POST is one turn through
/// Bill Intelligence → Financial Planning (validated before Policy). Payers are unauthenticated
/// (guest pay), so this endpoint is anonymous; identity/authorization for money movement lives in
/// the downstream Execution stage and its MCP capability, not here.
/// </summary>
[ApiController]
[Route("billers/{billerId}/payer-chat")]
public sealed partial class PayerChatController(
    PayerChatService payerChat,
    ILogger<PayerChatController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<PayerChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PayerChatResponse>> Post(
        string billerId,
        [FromBody] PayerChatRequest request,
        CancellationToken cancellationToken)
    {
        var response = await payerChat.ProcessTurnAsync(billerId, request, cancellationToken);
        LogTurn(logger, billerId, response.Artifacts.PaymentPlan.Method, Activity.Current?.TraceId.ToString());
        return Ok(response);
    }

    [LoggerMessage(3005, LogLevel.Information, "Payer-chat turn served for biller {BillerId}: method {Method}, trace {TraceId}")]
    private static partial void LogTurn(ILogger logger, string billerId, string method, string? traceId);
}
