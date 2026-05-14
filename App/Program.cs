using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;

var config = LoadConfiguration();
var command = args.Length > 0 ? args[0] : "clean";

using var client = new HttpClient();

if (string.Equals(command, "download", StringComparison.OrdinalIgnoreCase))
{
    var downloadOptions = ReadDownloadOptions(args);
    await DownloadRouteAsync(client, config, downloadOptions.Route, downloadOptions.Folder);
}
else if (string.Equals(command, "clean", StringComparison.OrdinalIgnoreCase))
{
    await CleanConfiguredRouteAsync(client, config);
}
else
{
    throw new ArgumentException($"Unknown command '{command}'. Use 'clean' or 'download'.");
}

static async Task CleanConfiguredRouteAsync(HttpClient client, AppConfiguration config)
{
    Console.WriteLine($"Listing cleanup route: {config.Route}");

    List<WebDavItem> items = await ListDirectoryAsync(client, config, config.Route);
    int deleted = 0;

    foreach (WebDavItem item in items)
    {
        if (IsSamePath(item.Path, config.Route))
        {
            continue;
        }

        using var req = GetRequest(HttpMethod.Delete, config, item.Href);
        using var deleteResponse = await client.SendAsync(req);
        deleteResponse.EnsureSuccessStatusCode();

        deleted++;
        Console.WriteLine($"Deleted {deleted}: {item.Path}");
    }

    Console.WriteLine($"Cleanup finished. Deleted {deleted} item(s).");
}

static async Task DownloadRouteAsync(HttpClient client, AppConfiguration config, string sourceRoute, string destinationFolder)
{
    sourceRoute = NormalizeDirectoryPath(sourceRoute);
    Directory.CreateDirectory(destinationFolder);

    Console.WriteLine($"Listing source route: {sourceRoute}");
    List<WebDavItem> files = await ListFilesRecursiveAsync(client, config, sourceRoute);
    Console.WriteLine($"Found {files.Count} file(s).");

    for (int index = 0; index < files.Count; index++)
    {
        WebDavItem file = files[index];
        string relativePath = GetRelativeLocalPath(sourceRoute, file.Path);
        string destinationPath = Path.Combine(destinationFolder, relativePath);
        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        using var request = GetRequest(HttpMethod.Get, config, file.Href);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength ?? file.ContentLength;
        await using Stream remoteStream = await response.Content.ReadAsStreamAsync();
        await using var localStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        await CopyWithProgressAsync(remoteStream, localStream, totalBytes, index + 1, files.Count, relativePath);
    }

    Console.WriteLine($"Download finished. Saved files to '{destinationFolder}'.");
}

static async Task<List<WebDavItem>> ListFilesRecursiveAsync(HttpClient client, AppConfiguration config, string sourcePath)
{
    var files = new List<WebDavItem>();
    var directories = new Stack<string>();
    var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    directories.Push(sourcePath);
    visitedDirectories.Add(NormalizePath(sourcePath));

    while (directories.Count > 0)
    {
        string currentDirectory = directories.Pop();
        Console.WriteLine($"Scanning {currentDirectory}");

        List<WebDavItem> items = await ListDirectoryAsync(client, config, currentDirectory);
        foreach (WebDavItem item in items)
        {
            if (IsSamePath(item.Path, currentDirectory))
            {
                continue;
            }

            if (item.IsCollection)
            {
                string normalizedPath = NormalizePath(item.Path);
                if (visitedDirectories.Add(normalizedPath))
                {
                    directories.Push(NormalizeDirectoryPath(item.Path));
                }
            }
            else
            {
                files.Add(item);
            }
        }
    }

    return files;
}

static async Task<List<WebDavItem>> ListDirectoryAsync(HttpClient client, AppConfiguration config, string path)
{
    using var request = GetRequest(new HttpMethod("PROPFIND"), config, path);
    request.Headers.TryAddWithoutValidation("Depth", "1");

    using var response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var xml = new XmlDocument();
    xml.Load(await response.Content.ReadAsStreamAsync());

    var namespaceManager = new XmlNamespaceManager(xml.NameTable);
    namespaceManager.AddNamespace("d", "DAV:");

    var items = new List<WebDavItem>();
    XmlNodeList? responseNodes = xml.SelectNodes("//d:response", namespaceManager);
    if (responseNodes == null)
    {
        return items;
    }

    foreach (XmlNode responseNode in responseNodes)
    {
        string? href = responseNode.SelectSingleNode("d:href", namespaceManager)?.InnerText;
        if (string.IsNullOrWhiteSpace(href))
        {
            continue;
        }

        bool isCollection = responseNode.SelectSingleNode(".//d:resourcetype/d:collection", namespaceManager) != null;
        long? contentLength = ReadContentLength(responseNode, namespaceManager);
        string itemPath = GetPathFromHref(config, href);

        items.Add(new WebDavItem(href, itemPath, isCollection, contentLength));
    }

    return items;
}

static long? ReadContentLength(XmlNode responseNode, XmlNamespaceManager namespaceManager)
{
    string? value = responseNode.SelectSingleNode(".//d:getcontentlength", namespaceManager)?.InnerText;
    return long.TryParse(value, out long contentLength) ? contentLength : null;
}

static async Task CopyWithProgressAsync(
    Stream source,
    Stream destination,
    long? totalBytes,
    int fileIndex,
    int fileCount,
    string relativePath)
{
    var buffer = new byte[81920];
    long copiedBytes = 0;
    int read;

    WriteDownloadProgress(fileIndex, fileCount, relativePath, copiedBytes, totalBytes);

    while ((read = await source.ReadAsync(buffer)) > 0)
    {
        await destination.WriteAsync(buffer.AsMemory(0, read));
        copiedBytes += read;
        WriteDownloadProgress(fileIndex, fileCount, relativePath, copiedBytes, totalBytes);
    }

    Console.WriteLine();
}

static void WriteDownloadProgress(int fileIndex, int fileCount, string relativePath, long copiedBytes, long? totalBytes)
{
    string progress = totalBytes is > 0
        ? $"{copiedBytes}/{totalBytes} bytes ({copiedBytes * 100 / totalBytes}%)"
        : $"{copiedBytes} bytes";

    Console.Write($"\rDownloading {fileIndex}/{fileCount}: {relativePath} - {progress}");
}

static DownloadOptions ReadDownloadOptions(string[] args)
{
    string? route = null;
    string? folder = null;

    for (int i = 1; i < args.Length; i++)
    {
        if (TryReadOption(args, i, "route", out string? value))
        {
            route = value;
            i++;
            continue;
        }

        if (TryReadOption(args, i, "folder", out value))
        {
            folder = value;
            i++;
        }
    }

    CheckConfig("route", route);
    CheckConfig("folder", folder);

    return new DownloadOptions(route!, folder!);
}

static bool TryReadOption(string[] args, int index, string key, out string? value)
{
    bool isMatch =
        string.Equals("-" + key, args[index], StringComparison.OrdinalIgnoreCase) ||
        string.Equals("--" + key, args[index], StringComparison.OrdinalIgnoreCase);

    bool hasValue = args.Length > index + 1;
    value = isMatch && hasValue ? args[index + 1] : null;
    return isMatch && hasValue;
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
        throw new ArgumentException($"Missing required value '{key}'.");
    }
}

static HttpRequestMessage GetRequest(HttpMethod httpMethod, AppConfiguration config, string pathOrHref)
{
    var request = new HttpRequestMessage(httpMethod, GetRequestUri(config.Host, pathOrHref));
    request.Headers.Authorization =
        new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(config.Credentials)));
    return request;
}

static Uri GetRequestUri(string host, string pathOrHref)
{
    if (Uri.TryCreate(pathOrHref, UriKind.Absolute, out Uri? absoluteUri))
    {
        return absoluteUri;
    }

    var baseUri = new Uri(host.EndsWith('/') ? host : host + "/");
    return new Uri(baseUri, pathOrHref);
}

static string GetPathFromHref(AppConfiguration config, string href)
{
    Uri uri = GetRequestUri(config.Host, href);
    return Uri.UnescapeDataString(uri.AbsolutePath);
}

static bool IsSamePath(string first, string second)
{
    return string.Equals(NormalizePath(first), NormalizePath(second), StringComparison.OrdinalIgnoreCase);
}

static string NormalizeDirectoryPath(string path)
{
    string normalizedPath = NormalizePath(path);
    return normalizedPath.EndsWith('/') ? normalizedPath : normalizedPath + "/";
}

static string NormalizePath(string path)
{
    string normalizedPath = Uri.UnescapeDataString(path).Replace('\\', '/');

    if (!normalizedPath.StartsWith('/'))
    {
        normalizedPath = "/" + normalizedPath;
    }

    return normalizedPath.TrimEnd('/');
}

static string GetRelativeLocalPath(string rootPath, string itemPath)
{
    string normalizedRoot = NormalizeDirectoryPath(rootPath);
    string normalizedItem = NormalizePath(itemPath);

    string relativePath = normalizedItem.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
        ? normalizedItem[normalizedRoot.Length..]
        : Path.GetFileName(normalizedItem);

    string[] segments = relativePath
        .Split('/', StringSplitOptions.RemoveEmptyEntries)
        .Select(SanitizePathSegment)
        .ToArray();

    return Path.Combine(segments);
}

static string SanitizePathSegment(string segment)
{
    foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
    {
        segment = segment.Replace(invalidCharacter, '_');
    }

    return segment;
}

internal sealed record AppConfiguration(string Host, string Route, string Credentials);

internal sealed record DownloadOptions(string Route, string Folder);

internal sealed record WebDavItem(string Href, string Path, bool IsCollection, long? ContentLength);

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
