using System.Net.Http.Headers;
using System.Text;
using System.Xml;

namespace OwnCloudTool.Core;

public sealed class WebDavClient(HttpClient httpClient, OwnCloudConfiguration configuration)
{
    public async Task<int> CleanConfiguredRouteAsync(Action<string>? report = null, CancellationToken cancellationToken = default)
    {
        report?.Invoke($"Listing cleanup route: {configuration.Route}");
        List<WebDavItem> items = await ListDirectoryAsync(configuration.Route, cancellationToken);
        int deleted = 0;
        foreach (WebDavItem item in items.Where(item => !IsSamePath(item.Path, configuration.Route)))
        {
            using var request = CreateRequest(HttpMethod.Delete, item.Href);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            report?.Invoke($"Deleted {++deleted}: {item.Path}");
        }
        report?.Invoke($"Cleanup finished. Deleted {deleted} item(s).");
        return deleted;
    }

    public async Task<int> DownloadRouteAsync(string sourceRoute, string destinationFolder, Action<string>? report = null, CancellationToken cancellationToken = default)
    {
        sourceRoute = NormalizeDirectoryPath(sourceRoute);
        Directory.CreateDirectory(destinationFolder);
        report?.Invoke($"Listing source route: {sourceRoute}");
        List<WebDavItem> files = await ListFilesRecursiveAsync(sourceRoute, report, cancellationToken);
        report?.Invoke($"Found {files.Count} file(s).");
        for (int index = 0; index < files.Count; index++)
        {
            WebDavItem file = files[index];
            string relativePath = GetRelativeLocalPath(sourceRoute, file.Path);
            string destinationPath = Path.Combine(destinationFolder, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var request = CreateRequest(HttpMethod.Get, file.Href);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using Stream remote = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var local = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await CopyWithProgressAsync(remote, local, response.Content.Headers.ContentLength ?? file.ContentLength, index + 1, files.Count, relativePath, report, cancellationToken);
        }
        report?.Invoke($"Download finished. Saved files to '{destinationFolder}'.");
        return files.Count;
    }

    private static async Task CopyWithProgressAsync(Stream source, Stream destination, long? total, int index, int count, string path, Action<string>? report, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920]; long copied = 0; int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken); copied += read;
            string progress = total is > 0 ? $"{copied}/{total} bytes ({copied * 100 / total}%)" : $"{copied} bytes";
            report?.Invoke($"Downloading {index}/{count}: {path} - {progress}");
        }
    }

    private static string GetRelativeLocalPath(string root, string item)
    { string normalizedRoot = NormalizeDirectoryPath(root); string normalizedItem = NormalizePath(item); string relative = normalizedItem.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ? normalizedItem[normalizedRoot.Length..] : Path.GetFileName(normalizedItem); return Path.Combine(relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(SanitizePathSegment).ToArray()); }

    private static bool IsSamePath(string first, string second) => string.Equals(NormalizePath(first), NormalizePath(second), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDirectoryPath(string path) => NormalizePath(path).TrimEnd('/') + "/";

    private static string NormalizePath(string path)
    {
        string normalized = Uri.UnescapeDataString(path).Replace('\\', '/').TrimEnd('/');
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }

    private static string SanitizePathSegment(string segment)
        => string.Concat(segment.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private HttpRequestMessage CreateRequest(HttpMethod method, string pathOrHref)
    {
        var request = new HttpRequestMessage(method, GetRequestUri(pathOrHref));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(configuration.Credentials)));
        return request;
    }

    private string GetPathFromHref(string href) => Uri.UnescapeDataString(GetRequestUri(href).AbsolutePath);

    private Uri GetRequestUri(string pathOrHref)
        => Uri.TryCreate(pathOrHref, UriKind.Absolute, out Uri? absolute)
        ? absolute
        : new Uri(new Uri(configuration.Host.EndsWith('/') ? configuration.Host : configuration.Host + "/"), pathOrHref);

    private async Task<List<WebDavItem>> ListDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(new HttpMethod("PROPFIND"), path);
        request.Headers.TryAddWithoutValidation("Depth", "1");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var xml = new XmlDocument();
        xml.Load(await response.Content.ReadAsStreamAsync(cancellationToken));

        var manager = new XmlNamespaceManager(xml.NameTable);
        manager.AddNamespace("d", "DAV:");

        XmlNodeList? nodes = xml.SelectNodes("//d:response", manager);
        if (nodes is null)
            return [];

        var items = new List<WebDavItem>();
        foreach (XmlNode node in nodes)
        {
            string? href = node.SelectSingleNode("d:href", manager)?.InnerText;
            if (string.IsNullOrWhiteSpace(href))
                continue;

            bool isCollection = node.SelectSingleNode(".//d:resourcetype/d:collection", manager) is not null;
            string? length = node.SelectSingleNode(".//d:getcontentlength", manager)?.InnerText;
            items.Add(new WebDavItem(href, GetPathFromHref(href), isCollection, long.TryParse(length, out long parsed) ? parsed : null));
        }
        return items;
    }

    private async Task<List<WebDavItem>> ListFilesRecursiveAsync(string sourcePath, Action<string>? report, CancellationToken cancellationToken)
    {
        var files = new List<WebDavItem>(); var directories = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormalizePath(sourcePath) };
        directories.Push(sourcePath);
        while (directories.Count > 0)
        {
            string current = directories.Pop();
            report?.Invoke($"Scanning {current}");
            foreach (WebDavItem item in await ListDirectoryAsync(current, cancellationToken))
            {
                if (IsSamePath(item.Path, current)) continue;
                if (item.IsCollection)
                {
                    if (visited.Add(NormalizePath(item.Path)))
                        directories.Push(NormalizeDirectoryPath(item.Path));
                }
                else files.Add(item);
            }
        }
        return files;
    }

    private sealed record WebDavItem(string Href, string Path, bool IsCollection, long? ContentLength);
}