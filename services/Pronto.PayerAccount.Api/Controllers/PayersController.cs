using Pronto.PayerAccount.Api.Storage;
using System.Diagnostics;
using Pronto.PayerAccount.Contracts.V1.Payers;
using Pronto.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Pronto.PayerAccount.Api.Controllers;

[ApiController]
[Route("payers")]
public sealed partial class PayersController : ControllerBase
{
    private readonly IPayerStore store;
    private readonly ILogger<PayersController> logger;

    public PayersController(IPayerStore store, ILogger<PayersController> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<PayerResponse>> Register(
        RegisterPayerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
        {
            throw ServiceException.BadRequest("validation_failed", "name and email are required");
        }

        // Preferences are optional at registration (Policy Agent may send them with opt-in);
        // the same autopay/payment-day rules apply as on PATCH.
        var preferences = request.Preferences
            ?? new PayerPreferences(Autopay: false, Paperless: false, Channels: [], PaymentDay: null);
        ValidatePreferences(preferences);

        var payer = new PayerResponse(
            PayerId: Guid.NewGuid().ToString(),
            BillerId: request.BillerId,
            Name: request.Name,
            Email: request.Email,
            Phone: request.Phone,
            AccountNumbers: request.AccountNumbers,
            Preferences: preferences);
        await store.AddAsync(payer, cancellationToken).ConfigureAwait(false);
        LogPayerRegistered(logger, payer.PayerId, payer.BillerId, payer.AccountNumbers.Count, Activity.Current?.TraceId.ToString());

        return Created($"/payers/{payer.PayerId}?biller_id={payer.BillerId}", payer);
    }

    [HttpGet("{payerId}")]
    public async Task<ActionResult<PayerResponse>> Get(
        string payerId, [FromQuery(Name = "biller_id")] string billerId, CancellationToken cancellationToken)
        => await store.FindAsync(billerId, payerId, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("not_found", $"payer {payerId} not found");

    [HttpGet]
    public async Task<ActionResult<PayerResponse>> FindByAccount(
        [FromQuery(Name = "biller_id")] string billerId,
        [FromQuery(Name = "account_number")] string accountNumber,
        CancellationToken cancellationToken)
    {
        var payer = await store.FindByAccountAsync(billerId, accountNumber, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("not_found", $"no payer is registered for account {accountNumber}");
        return Ok(payer);
    }

    /// <summary>
    /// Null = unchanged. payment_day is inert while autopay is off (design/entities.md);
    /// enabling autopay requires a payment day already set or supplied here.
    /// </summary>
    [HttpPatch("{payerId}/preferences")]
    public async Task<ActionResult<PayerPreferences>> UpdatePreferences(
        string payerId,
        [FromQuery(Name = "biller_id")] string billerId,
        UpdatePayerPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        var payer = await store.FindAsync(billerId, payerId, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("not_found", $"payer {payerId} not found");

        var preferences = payer.Preferences;
        var updated = new PayerPreferences(
            Autopay: request.Autopay ?? preferences.Autopay,
            Paperless: request.Paperless ?? preferences.Paperless,
            Channels: request.Channels ?? preferences.Channels,
            PaymentDay: request.PaymentDay ?? preferences.PaymentDay);
        ValidatePreferences(updated);

        await store.UpdateAsync(payer with { Preferences = updated }, cancellationToken).ConfigureAwait(false);
        LogPreferencesUpdated(logger, payerId, billerId, updated.Autopay, updated.Paperless, Activity.Current?.TraceId.ToString());
        return updated;
    }

    private static void ValidatePreferences(PayerPreferences preferences)
    {
        if (preferences.PaymentDay is < 1 or > 28)
        {
            throw ServiceException.BadRequest("invalid_payment_day", "payment_day must be between 1 and 28");
        }

        if (preferences.Autopay && preferences.PaymentDay is null)
        {
            throw ServiceException.BadRequest(
                "autopay_requires_payment_day",
                "enabling autopay requires a payment_day (1-28) already set or supplied in this request");
        }
    }

    [LoggerMessage(5100, LogLevel.Information, "Registered payer {PayerId} for biller {BillerId} with {AccountCount} accounts; trace {TraceId}")]
    private static partial void LogPayerRegistered(ILogger logger, string payerId, string billerId, int accountCount, string? traceId);
    [LoggerMessage(5101, LogLevel.Information, "Updated preferences for payer {PayerId}, biller {BillerId}; autopay {Autopay}, paperless {Paperless}; trace {TraceId}")]
    private static partial void LogPreferencesUpdated(ILogger logger, string payerId, string billerId, bool autopay, bool paperless, string? traceId);
}
