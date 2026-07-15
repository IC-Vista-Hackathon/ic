using Pronto.PayerAccount.Api.Accounts;
using Pronto.PayerAccount.Api.Storage;
using System.Diagnostics;
using System.Net.Mail;
using Pronto.PayerAccount.Contracts.V1.Payers;
using Pronto.ServiceDefaults.Errors;
using Pronto.ServiceDefaults.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Pronto.PayerAccount.Api.Controllers;

[ApiController]
[Route("payers")]
[Authorize]
public sealed partial class PayersController : ControllerBase
{
    private readonly IPayerStore store;
    private readonly IAccountOwnershipVerifier ownership;
    private readonly ILogger<PayersController> logger;

    public PayersController(
        IPayerStore store, IAccountOwnershipVerifier ownership, ILogger<PayersController> logger)
    {
        this.store = store;
        this.ownership = ownership;
        this.logger = logger;
    }

    [HttpPost]
    [Authorize(Policy = ServiceAuthorization.PayersWrite)]
    public async Task<ActionResult<PayerResponse>> Register(
        RegisterPayerRequest request, CancellationToken cancellationToken)
    {
        BillerClaims.RequireBillerAccess(User, request.BillerId);
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
        {
            throw ServiceException.BadRequest("validation_failed", "name and email are required");
        }

        if (!IsValidEmail(request.Email))
        {
            throw ServiceException.BadRequest("invalid_email", "email is not a valid address");
        }

        // Preferences are optional at registration (Policy Agent may send them with opt-in);
        // the same autopay/payment-day/channel rules apply as on PATCH.
        var preferences = request.Preferences
            ?? new PayerPreferences(Autopay: false, Paperless: false, Channels: [], PaymentDay: null);
        ValidatePreferences(preferences, request.Phone, request.Email);

        var accountNumbers = NormalizeAccounts(request.AccountNumbers);
        await VerifyOwnershipAsync(request.BillerId, accountNumbers, cancellationToken).ConfigureAwait(false);

        var payer = new PayerResponse(
            PayerId: Guid.NewGuid().ToString(),
            BillerId: request.BillerId,
            Name: request.Name,
            Email: request.Email,
            Phone: request.Phone,
            AccountNumbers: accountNumbers,
            Preferences: preferences);
        var stored = await store.AddAsync(payer, cancellationToken).ConfigureAwait(false);
        LogPayerRegistered(logger, stored.PayerId, stored.BillerId, stored.AccountNumbers.Count, Activity.Current?.TraceId.ToString());

        return Created($"/payers/{stored.PayerId}?biller_id={stored.BillerId}", stored);
    }

    [HttpGet("{payerId}")]
    public async Task<ActionResult<PayerResponse>> Get(
        string payerId, [FromQuery(Name = "biller_id")] string billerId, CancellationToken cancellationToken)
    {
        BillerClaims.RequireBillerAccess(User, billerId);
        return await store.FindAsync(billerId, payerId, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("not_found", $"payer {payerId} not found");
    }

    [HttpGet]
    public async Task<ActionResult<PayerResponse>> FindByAccount(
        [FromQuery(Name = "biller_id")] string billerId,
        [FromQuery(Name = "account_number")] string accountNumber,
        CancellationToken cancellationToken)
    {
        BillerClaims.RequireBillerAccess(User, billerId);
        var payer = await store.FindByAccountAsync(billerId, accountNumber, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("not_found", $"no payer is registered for account {accountNumber}");
        return Ok(payer);
    }

    /// <summary>
    /// Null = unchanged. payment_day is inert while autopay is off (design/entities.md);
    /// enabling autopay requires a payment day already set or supplied here. Concurrent PATCHes
    /// are merged under optimistic concurrency in the store so none are lost.
    /// </summary>
    [HttpPatch("{payerId}/preferences")]
    [Authorize(Policy = ServiceAuthorization.PayersWrite)]
    public async Task<ActionResult<PayerPreferences>> UpdatePreferences(
        string payerId,
        [FromQuery(Name = "biller_id")] string billerId,
        UpdatePayerPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        BillerClaims.RequireBillerAccess(User, billerId);

        var updated = await store.UpdatePreferencesAsync(
            billerId,
            payerId,
            payer =>
            {
                var merged = new PayerPreferences(
                    Autopay: request.Autopay ?? payer.Preferences.Autopay,
                    Paperless: request.Paperless ?? payer.Preferences.Paperless,
                    Channels: request.Channels ?? payer.Preferences.Channels,
                    PaymentDay: request.PaymentDay ?? payer.Preferences.PaymentDay);
                ValidatePreferences(merged, payer.Phone, payer.Email);
                return merged;
            },
            cancellationToken).ConfigureAwait(false);

        LogPreferencesUpdated(logger, payerId, billerId, updated.Autopay, updated.Paperless, Activity.Current?.TraceId.ToString());
        return updated;
    }

    /// <summary>
    /// Link additional biller accounts to an existing payer. Idempotent: accounts already linked
    /// to this payer are ignored; an account already linked to a different payer returns 409.
    /// <c>POST /payers/{payerId}/accounts?biller_id=</c>.
    /// </summary>
    [HttpPost("{payerId}/accounts")]
    [Authorize(Policy = ServiceAuthorization.PayersWrite)]
    public async Task<ActionResult<PayerResponse>> LinkAccounts(
        string payerId,
        [FromQuery(Name = "biller_id")] string billerId,
        LinkAccountsRequest request,
        CancellationToken cancellationToken)
    {
        BillerClaims.RequireBillerAccess(User, billerId);

        var accountNumbers = NormalizeAccounts(request.AccountNumbers);
        if (accountNumbers.Count == 0)
        {
            throw ServiceException.BadRequest("validation_failed", "at least one account_number is required");
        }

        // Fail fast with 404 before calling the ownership backend for a payer that does not exist.
        _ = await store.FindAsync(billerId, payerId, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("not_found", $"payer {payerId} not found");

        await VerifyOwnershipAsync(billerId, accountNumbers, cancellationToken).ConfigureAwait(false);

        var updated = await store.LinkAccountsAsync(billerId, payerId, accountNumbers, cancellationToken)
            .ConfigureAwait(false);
        LogAccountsLinked(logger, payerId, billerId, updated.AccountNumbers.Count, Activity.Current?.TraceId.ToString());
        return Ok(updated);
    }

    private async Task VerifyOwnershipAsync(
        string billerId, IReadOnlyList<string> accountNumbers, CancellationToken cancellationToken)
    {
        foreach (var account in accountNumbers)
        {
            var owned = await ownership.IsOwnedAsync(billerId, account, cancellationToken).ConfigureAwait(false);
            if (!owned)
            {
                LogOwnershipRejected(logger, billerId, account, Activity.Current?.TraceId.ToString());
                throw ServiceException.BadRequest(
                    "account_not_owned",
                    $"account {account} is not a known account for this biller and cannot be linked");
            }
        }
    }

    private static void ValidatePreferences(PayerPreferences preferences, string? phone, string email)
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

        if (preferences.Channels.Contains(NotificationChannel.Sms) && string.IsNullOrWhiteSpace(phone))
        {
            throw ServiceException.BadRequest(
                "sms_channel_requires_phone",
                "the sms notification channel requires a phone number on the payer");
        }

        if (preferences.Channels.Contains(NotificationChannel.Email) && !IsValidEmail(email))
        {
            throw ServiceException.BadRequest(
                "email_channel_requires_valid_email",
                "the email notification channel requires a valid email address on the payer");
        }
    }

    private static bool IsValidEmail(string? email)
        => !string.IsNullOrWhiteSpace(email)
            && MailAddress.TryCreate(email.Trim(), out var parsed)
            && string.Equals(parsed.Address, email.Trim(), StringComparison.Ordinal);

    private static List<string> NormalizeAccounts(IReadOnlyList<string> accounts) => accounts
        .Select(account => account.Trim())
        .Where(account => account.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    [LoggerMessage(5100, LogLevel.Information, "Registered payer {PayerId} for biller {BillerId} with {AccountCount} accounts; trace {TraceId}")]
    private static partial void LogPayerRegistered(ILogger logger, string payerId, string billerId, int accountCount, string? traceId);
    [LoggerMessage(5101, LogLevel.Information, "Updated preferences for payer {PayerId}, biller {BillerId}; autopay {Autopay}, paperless {Paperless}; trace {TraceId}")]
    private static partial void LogPreferencesUpdated(ILogger logger, string payerId, string billerId, bool autopay, bool paperless, string? traceId);
    [LoggerMessage(5102, LogLevel.Information, "Linked accounts for payer {PayerId}, biller {BillerId}; now {AccountCount} accounts; trace {TraceId}")]
    private static partial void LogAccountsLinked(ILogger logger, string payerId, string billerId, int accountCount, string? traceId);
    [LoggerMessage(5103, LogLevel.Warning, "Rejected account link for biller {BillerId}, account {AccountNumber}: not owned; trace {TraceId}")]
    private static partial void LogOwnershipRejected(ILogger logger, string billerId, string accountNumber, string? traceId);
}
