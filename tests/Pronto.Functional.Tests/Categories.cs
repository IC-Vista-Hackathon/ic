namespace Pronto.Functional.Tests;

/// <summary>
/// Trait categories used to select functional tests in CI (see
/// docs/pronto-functional-testing-policy.md).
///
/// <list type="bullet">
/// <item><see cref="Functional"/> — every test in this project; drives a deployed environment.</item>
/// <item><see cref="KnownGap"/> — additionally marks a behavior Pronto is expected to have but
/// does NOT yet satisfy. These are written to FAIL today and pass once the feature is fixed; the
/// nonprod pipeline runs them in a non-blocking step until then.</item>
/// </list>
///
/// Gate (blocking):     <c>dotnet test --filter "Category=functional&amp;Category!=known-gap"</c>
/// Known gaps (report): <c>dotnet test --filter "Category=known-gap"</c>
/// </summary>
public static class Categories
{
    public const string Name = "Category";
    public const string Functional = "functional";
    public const string KnownGap = "known-gap";
}
