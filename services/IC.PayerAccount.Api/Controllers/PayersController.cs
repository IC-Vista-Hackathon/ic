using IC.PayerAccount.Api.Storage;
using IC.PayerAccount.Contracts.V1.Payers;
using IC.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Mvc;

namespace IC.PayerAccount.Api.Controllers;

[ApiController]
[Route("payers")]
public sealed class PayersController : ControllerBase
{
    private readonly IPayerStore store;

    public PayersController(IPayerStore store)
    {
        this.store = store;
    }

    [HttpPost]
    public ActionResult<PayerResponse> Register(RegisterPayerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
        {
            throw ServiceException.BadRequest("validation_failed", "name and email are required");
        }

        var payer = new PayerResponse(
            PayerId: Guid.NewGuid().ToString(),
            BillerId: request.BillerId,
            Name: request.Name,
            Email: request.Email,
            Phone: request.Phone,
            AccountNumbers: request.AccountNumbers,
            Preferences: new PayerPreferences(
                Autopay: false,
                Paperless: false,
                Channels: [],
                PaymentDay: null));
        store.Add(payer);

        return Created($"/payers/{payer.PayerId}?billerId={payer.BillerId}", payer);
    }

    [HttpGet("{payerId}")]
    public ActionResult<PayerResponse> Get(string payerId, [FromQuery] string billerId)
        => store.Find(billerId, payerId)
            ?? throw ServiceException.NotFound("not_found", $"payer {payerId} not found");

    /// <summary>
    /// Null = unchanged. payment_day is inert while autopay is off (design/entities.md);
    /// enabling autopay requires a payment day already set or supplied here.
    /// </summary>
    [HttpPatch("{payerId}/preferences")]
    public ActionResult<PayerPreferences> UpdatePreferences(
        string payerId, [FromQuery] string billerId, UpdatePayerPreferencesRequest request)
    {
        var payer = store.Find(billerId, payerId)
            ?? throw ServiceException.NotFound("not_found", $"payer {payerId} not found");

        if (request.PaymentDay is < 1 or > 28)
        {
            throw ServiceException.BadRequest("invalid_payment_day", "paymentDay must be between 1 and 28");
        }

        var preferences = payer.Preferences;
        var updated = new PayerPreferences(
            Autopay: request.Autopay ?? preferences.Autopay,
            Paperless: request.Paperless ?? preferences.Paperless,
            Channels: request.Channels ?? preferences.Channels,
            PaymentDay: request.PaymentDay ?? preferences.PaymentDay);

        if (updated.Autopay && updated.PaymentDay is null)
        {
            throw ServiceException.BadRequest(
                "autopay_requires_payment_day",
                "enabling autopay requires a paymentDay (1-28) already set or supplied in this request");
        }

        store.Update(payer with { Preferences = updated });
        return updated;
    }
}
