namespace Nyautomator.Modules.Govee;

public sealed class GoveeModuleOptions
{
    /// <summary>
    /// Govee Open API key issued by the Govee Developer Portal. Stored through module secret storage.
    /// </summary>
    public string? ApiKey { get; set; }

    public static GoveeModuleOptions CreateDefault() => new();

    public string? GetEffectiveApiKey()
        => string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim();
}
