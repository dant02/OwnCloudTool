using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml;

var config = LoadConfiguration();

using var client = new HttpClient();

// Request contents of a folder.
using var request = GetRequest(new HttpMethod("PROPFIND"), config.Host + config.Route, config.Credentials);
using var response = await client.SendAsync(request);

var xml = new XmlDocument();
xml.Load(await response.Content.ReadAsStreamAsync());

var mng = new XmlNamespaceManager(xml.NameTable);
mng.AddNamespace("d", "DAV:");

List<string> paths = [];
var nodes = xml.SelectNodes("//d:href", mng);
if (nodes != null)
{
    foreach (XmlNode node in nodes)
    {
        paths.Add(node.InnerText);
    }
}

foreach (string path in paths)
{
    if (string.Equals(path, config.Route, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    // Request deletion of each returned item except the folder itself.
    using var req = GetRequest(HttpMethod.Delete, config.Host + path, config.Credentials);
    using var deleteResponse = await client.SendAsync(req);
}

static AppConfiguration LoadConfiguration()
{
    string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (!File.Exists(configPath))
    {
        throw new FileNotFoundException($"Missing configuration file '{configPath}'.");
    }

    string json = File.ReadAllText(configPath);
    RootConfiguration? root = JsonSerializer.Deserialize<RootConfiguration>(
        json,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

    OwnCloudConfiguration? configuration = root?.OwnCloud;
    if (configuration == null)
    {
        throw new ArgumentException("Missing 'OwnCloud' configuration section in appsettings.json.");
    }

    string? credentials = Environment.GetEnvironmentVariable("OWNCLOUD_CREDENTIALS");
    if (!string.IsNullOrWhiteSpace(credentials))
    {
        configuration.Credentials = credentials;
    }

    CheckConfig(nameof(configuration.Host), configuration.Host);
    CheckConfig(nameof(configuration.Route), configuration.Route);
    CheckConfig(nameof(configuration.Credentials), configuration.Credentials);

    return new AppConfiguration(configuration.Host!, configuration.Route!, configuration.Credentials!);
}

static void CheckConfig(string key, string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new ArgumentException($"Missing required configuration value '{key}'.");
    }
}

static HttpRequestMessage GetRequest(HttpMethod httpMethod, string requestUri, string credentials)
{
    var request = new HttpRequestMessage(httpMethod, requestUri);
    request.Headers.Authorization =
        new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
    return request;
}

internal sealed record AppConfiguration(string Host, string Route, string Credentials);

internal sealed class RootConfiguration
{
    public OwnCloudConfiguration? OwnCloud { get; set; }
}

internal sealed class OwnCloudConfiguration
{
    public string? Host { get; set; }

    public string? Route { get; set; }

    public string? Credentials { get; set; }
}
