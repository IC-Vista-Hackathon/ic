using Pronto.Payment.Api.Clients;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.Payment.Api.Tests;

/// <summary>Test validator that rejects any payer account id not in its allow-list.</summary>
public sealed class FakePayerAccountValidator : IPayerAccountValidator
{
    private readonly HashSet<string> valid = new(StringComparer.Ordinal);

    public void Allow(string payerAccountId) => valid.Add(payerAccountId);

    public Task ValidateAsync(string billerId, string? payerAccountId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(payerAccountId) && !valid.Contains(payerAccountId))
        {
            throw ServiceException.NotFound(
                "payer_account_not_found", $"payer account {payerAccountId} not found for this biller.");
        }

        return Task.CompletedTask;
    }
}
