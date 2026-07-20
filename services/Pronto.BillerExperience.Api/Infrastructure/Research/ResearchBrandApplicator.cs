using System.Text.RegularExpressions;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Research;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

/// <summary>
/// Deterministically folds researched, cited brand evidence onto a draft experience definition.
/// This is the "services execute" half of the split: the agent produces typed facts, and this maps
/// them onto the config's brand model. It only fills brand tokens that are still unset, so an
/// explicit biller/chat choice is never overwritten by research, and it never invents a value the
/// evidence did not supply.
/// </summary>
public static partial class ResearchBrandApplicator
{
    public static BillerExperienceDefinition Apply(
        BillerExperienceDefinition definition,
        BillerRecord biller,
        BillerResearchResponse research)
    {
        // An explicit biller-supplied brand choice is authoritative: it overrides the generated
        // draft (a model may re-emit different colors/font) and researched evidence, so a biller's
        // manual selection is never silently lost on the way to preview/publish.
        definition = ApplyExplicitBrand(definition, biller.Brand);

        if (research.Facts.Count == 0)
        {
            return definition;
        }

        var evidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in research.Facts.Where(fact => !string.IsNullOrWhiteSpace(fact.Value)))
        {
            if (!evidence.ContainsKey(fact.Name))
            {
                evidence[fact.Name] = fact.Value.Trim();
            }
        }

        var brand = definition.Brand;
        var primary = IsBlank(brand.PrimaryColor)
            ? NormalizeHex(Lookup(evidence, BrandEvidenceFacts.PrimaryColor)) ?? brand.PrimaryColor
            : brand.PrimaryColor;
        var secondary = IsBlank(brand.SecondaryColor)
            ? NormalizeHex(Lookup(evidence, BrandEvidenceFacts.SecondaryColor)) ?? brand.SecondaryColor
            : brand.SecondaryColor;

        // A biller site often exposes a single brand hue. Rather than fabricate an unrelated second
        // color (or leave the draft unpublishable — the compliance gate requires two valid colors),
        // derive the secondary as a shade of the researched/explicit primary so it stays on-brand.
        if (IsBlank(secondary) && DeriveSecondary(primary) is { } derived)
        {
            secondary = derived;
        }

        var logo = IsBlank(brand.LogoAssetId)
            ? SameOriginLogo(Lookup(evidence, BrandEvidenceFacts.LogoUrl), biller.Website) ?? brand.LogoAssetId
            : brand.LogoAssetId;
        var font = IsBlank(brand.FontFamily)
            ? Lookup(evidence, BrandEvidenceFacts.FontFamily) ?? brand.FontFamily
            : brand.FontFamily;

        var updatedBrand = brand with
        {
            PrimaryColor = primary,
            SecondaryColor = secondary,
            LogoAssetId = logo,
            FontFamily = font
        };

        // Keep the PWA theme color aligned with the researched brand color when it was unset.
        var pwa = IsBlank(definition.Pwa.ThemeColor) && !IsBlank(primary)
            ? definition.Pwa with { ThemeColor = primary }
            : definition.Pwa;

        var brief = definition.Brief ?? BuildBrief(biller, evidence, updatedBrand);

        return definition with { Brand = updatedBrand, Pwa = pwa, Brief = brief };
    }

    // The creative brief is derived from researched evidence (never invented at creation time). It is
    // only produced once there is at least one supporting brand signal, and every string it carries
    // is grounded in a fact or the biller's own supplied details.
    private static DesignBrief? BuildBrief(
        BillerRecord biller,
        IReadOnlyDictionary<string, string> evidence,
        ExperienceBrand brand)
    {
        var tagline = Lookup(evidence, BrandEvidenceFacts.Tagline);
        var displayName = Lookup(evidence, BrandEvidenceFacts.DisplayName);
        var hasSignal = !IsBlank(brand.LogoAssetId) || !IsBlank(brand.PrimaryColor)
            || tagline is not null || displayName is not null;
        if (!hasSignal)
        {
            return null;
        }

        var keywords = new List<string>();
        foreach (var word in KeywordRegex().Split(displayName ?? biller.Name))
        {
            if (word.Length > 2 && !keywords.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                keywords.Add(word.ToLowerInvariant());
            }
        }
        if (!string.IsNullOrWhiteSpace(biller.BillType))
        {
            keywords.Add(biller.BillType.ToLowerInvariant());
        }

        var assets = new List<BrandAsset>();
        if (!IsBlank(brand.LogoAssetId) && Uri.TryCreate(brand.LogoAssetId, UriKind.Absolute, out var logoUri))
        {
            assets.Add(new BrandAsset("logo", logoUri, $"{biller.Name} logo"));
        }

        var palette = new List<string>();
        if (!IsBlank(brand.PrimaryColor)) palette.Add($"primary {brand.PrimaryColor}");
        if (!IsBlank(brand.SecondaryColor)) palette.Add($"secondary {brand.SecondaryColor}");
        if (!IsBlank(brand.FontFamily)) palette.Add($"typeface {brand.FontFamily}");

        return new DesignBrief(
            VoiceAndTone: tagline ?? "Grounded in the biller's own published site copy.",
            VisualStyle: palette.Count > 0
                ? $"Brand tokens sourced from the biller site: {string.Join(", ", palette)}."
                : "Visual identity sourced from the biller's own site.",
            BrandKeywords: keywords,
            Assets: assets,
            ReferenceUrl: biller.Website);
    }

    // Overlays a biller's explicitly-supplied brand tokens onto the draft. Only non-blank values are
    // applied, so an explicit color/font selection wins while unset tokens still fall through to
    // researched evidence below.
    private static BillerExperienceDefinition ApplyExplicitBrand(
        BillerExperienceDefinition definition, BillerBrand? chosen)
    {
        if (chosen is null)
        {
            return definition;
        }

        var brand = definition.Brand with
        {
            PrimaryColor = IsBlank(chosen.PrimaryColor) ? definition.Brand.PrimaryColor : chosen.PrimaryColor,
            SecondaryColor = IsBlank(chosen.SecondaryColor) ? definition.Brand.SecondaryColor : chosen.SecondaryColor,
            FontFamily = IsBlank(chosen.FontFamily) ? definition.Brand.FontFamily : chosen.FontFamily,
            LogoAssetId = IsBlank(chosen.LogoAssetId) ? definition.Brand.LogoAssetId : chosen.LogoAssetId,
        };
        return definition with { Brand = brand };
    }

    private static string? Lookup(IReadOnlyDictionary<string, string> evidence, string name) =>
        evidence.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static string? NormalizeHex(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.Trim();
        if (!text.StartsWith('#'))
        {
            return null;
        }

        var digits = text[1..];
        if (digits.Length == 3 && digits.All(Uri.IsHexDigit))
        {
            return $"#{digits[0]}{digits[0]}{digits[1]}{digits[1]}{digits[2]}{digits[2]}".ToLowerInvariant();
        }

        return digits.Length == 6 && digits.All(Uri.IsHexDigit) ? $"#{digits.ToLowerInvariant()}" : null;
    }

    // Produces an on-brand companion color from a single primary: light primaries are darkened and
    // dark primaries are lightened by ~25%, so the two are always visibly distinct. Returns null for
    // anything that isn't a valid hex color.
    private static string? DeriveSecondary(string? primary)
    {
        if (NormalizeHex(primary) is not { } hex)
        {
            return null;
        }

        var red = Convert.ToInt32(hex.Substring(1, 2), 16);
        var green = Convert.ToInt32(hex.Substring(3, 2), 16);
        var blue = Convert.ToInt32(hex.Substring(5, 2), 16);
        var lighten = (red + green + blue) / 3 < 128;

        static int Shift(int channel, bool lighten) => lighten
            ? channel + (int)Math.Round((255 - channel) * 0.25)
            : (int)Math.Round(channel * 0.75);

        return $"#{Shift(red, lighten):x2}{Shift(green, lighten):x2}{Shift(blue, lighten):x2}";
    }

    // Only a first-party, absolute HTTPS logo on the biller's own host is accepted as a brand asset
    // reference — the same same-origin guard the crawler applies, enforced again here in case the
    // evidence came from a less-trusted (e.g. Foundry) research agent.
    private static string? SameOriginLogo(string? value, Uri? website)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        if (website is not null && !uri.Host.Equals(website.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex KeywordRegex();
}
