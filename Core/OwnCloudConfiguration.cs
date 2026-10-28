using System.Text.Json;

namespace OwnCloudTool.Core;

public sealed record OwnCloudConfiguration(string Host, string Route, string Credentials)
{
    private static readonly JsonSerializerOptions fOpts = new() { PropertyNameCaseInsensitive = true };

    public static OwnCloudConfiguration Load(string configurationPath)
    {
        if (!File.Exists(configurationPath))
            throw new FileNotFoundException($"Missing configuration file '{configurationPath}'.");

        RootConfiguration? root = JsonSerializer.Deserialize<RootConfiguration>(File.ReadAllText(configurationPath), fOpts);
        OwnCloudSettings? settings = root?.OwnCloud;
        string? credentials = Environment.GetEnvironmentVariable("OWNCLOUD_CREDENTIALS") ?? settings?.Credentials;
        return new OwnCloudConfiguration(
            Required("Host", settings?.Host),
            Required("Route", settings?.Route),
            Required("Credentials", credentials));
    }

    private static string Required(string key, string? value) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Missing required value '{key}'.");

    private sealed class RootConfiguration
    {
        public OwnCloudSettings? OwnCloud { get; set; }
    }
    private sealed class OwnCloudSettings
    {
        public string? Host { get; set; }
        public string? Route { get; set; }
        public string? Credentials { get; set; }
    }
}