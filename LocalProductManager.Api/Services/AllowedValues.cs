namespace LocalProductManager.Api.Services;

public static class AllowedValues
{
    public static readonly HashSet<string> ListingPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "amazon",
        "walmart",
        "ebay",
        "etsy"
    };

    public static readonly HashSet<string> AmazonAsinTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "parent",
        "child"
    };

    public static readonly HashSet<string> AttributionPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "pinterest",
        "facebook",
        "instagram",
        "tiktok",
        "youtube",
        "shopify"
    };

    public static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
