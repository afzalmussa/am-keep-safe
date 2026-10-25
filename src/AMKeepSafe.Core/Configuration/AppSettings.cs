using System.Net.Mail;
using System.Text.Json;

namespace AMKeepSafe.Core.Configuration;

/// <summary>
/// Values that can change without touching code. Edit
/// src/AMKeepSafe.Core/Configuration/appsettings.json and rebuild.
/// An empty or invalid value means "not set up yet" and the matching button is hidden.
/// </summary>
public sealed record AppSettings
{
    private const string ResourceName = "AMKeepSafe.Core.appsettings.json";

    public const string DefaultReportEmail = "contact@amsoftware.uk";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Where the "Get Pro" button sends people. Must be https.</summary>
    public string? CheckoutUrl { get; init; }

    /// <summary>Where "Send to AM Software" addresses its email.</summary>
    public string? ReportEmail { get; init; }

    /// <summary>Which Lemon Squeezy store and product a licence key must belong to.</summary>
    public LemonSqueezySettings? LemonSqueezy { get; init; }

    public bool HasCheckoutUrl => IsUsableUrl(CheckoutUrl);

    /// <summary>The configured report address, or AM Software's contact address if it's missing or not a valid email address.</summary>
    public string ReportEmailOrDefault => IsUsableEmail(ReportEmail) ? ReportEmail!.Trim() : DefaultReportEmail;

    /// <summary>Loads the settings file built into the app.</summary>
    public static AppSettings LoadEmbedded()
    {
        using var stream = typeof(AppSettings).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded settings file '{ResourceName}' is missing.");
        return Load(stream);
    }

    public static AppSettings Load(Stream json)
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A broken settings file must never stop the app opening. Fall back to "nothing set up".
            return new AppSettings();
        }
    }

    /// <summary>Only absolute https addresses are allowed, so a typo can't open something unexpected.</summary>
    public static bool IsUsableUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrEmpty(uri.Host);

    public static bool IsUsableEmail(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        try
        {
            var parsed = new MailAddress(address.Trim());
            // MailAddress accepts "Name <a@b>" too. Only a bare address is wanted here.
            return parsed.Address == address.Trim() && parsed.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// A licence key only turns on Pro if Lemon Squeezy says it was sold by this store for this product.
/// Without this check, a key for any other Lemon Squeezy product (AM Speed Up, for example) would work.
/// These ids aren't secret.
/// </summary>
public sealed record LemonSqueezySettings
{
    public long StoreId { get; init; }

    public long ProductId { get; init; }

    /// <summary>Optional. When set, the key must also be for this variant.</summary>
    public long? VariantId { get; init; }

    public bool IsConfigured => StoreId > 0 && ProductId > 0;
}
