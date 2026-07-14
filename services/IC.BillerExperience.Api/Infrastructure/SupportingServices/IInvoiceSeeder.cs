namespace IC.BillerExperience.Api.Infrastructure.SupportingServices;

public interface IInvoiceSeeder
{
    ValueTask SeedAsync(string billerId, string billType, CancellationToken cancellationToken);
}

public sealed class NullInvoiceSeeder : IInvoiceSeeder
{
    public ValueTask SeedAsync(string billerId, string billType, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
