using System.Security.Claims;
using Pronto.ServiceDefaults.Errors;
using Pronto.ServiceDefaults.Security;
using Xunit;

namespace Pronto.Payment.Api.Tests;

/// <summary>Unit coverage for the shared tenant (biller) claim validation helper.</summary>
public sealed class BillerClaimsTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));

    [Fact]
    public void MatchingBillerClaimIsAllowed()
    {
        var user = Principal(new Claim(ServiceClaims.BillerId, "biller-1"));
        BillerClaims.RequireBillerAccess(user, "biller-1");
    }

    [Fact]
    public void MismatchedBillerClaimIsForbidden()
    {
        var user = Principal(new Claim(ServiceClaims.BillerId, "biller-1"));
        var error = Assert.Throws<ServiceException>(() => BillerClaims.RequireBillerAccess(user, "biller-2"));
        Assert.Equal("biller_forbidden", error.Code);
    }

    [Fact]
    public void MissingBillerClaimIsForbidden()
    {
        var user = Principal();
        Assert.Throws<ServiceException>(() => BillerClaims.RequireBillerAccess(user, "biller-1"));
    }

    [Fact]
    public void CrossBillerRoleSpansAnyBiller()
    {
        var user = Principal(new Claim("roles", ServiceClaims.CrossBillerRole));
        BillerClaims.RequireBillerAccess(user, "biller-1");
        BillerClaims.RequireBillerAccess(user, "biller-2");
    }

    [Fact]
    public void BlankBillerIsBadRequest()
    {
        var user = Principal(new Claim("roles", ServiceClaims.CrossBillerRole));
        var error = Assert.Throws<ServiceException>(() => BillerClaims.RequireBillerAccess(user, " "));
        Assert.Equal("invalid_biller", error.Code);
    }

    [Fact]
    public void HasGrantMatchesSpaceDelimitedScopeClaim()
    {
        var user = Principal(new Claim("scp", "payments:write payers:write"));
        Assert.True(user.HasGrant("payments:write"));
        Assert.False(user.HasGrant("invoices:seed"));
    }
}
