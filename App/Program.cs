using OwnCloudTool.Core;

var configuration = OwnCloudConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
var command = args.Length > 0 ? args[0] : "clean";
using var httpClient = new HttpClient();
var webDav = new WebDavClient(httpClient, configuration);

if (string.Equals(command, "clean", StringComparison.OrdinalIgnoreCase))
{
    await webDav.CleanConfiguredRouteAsync(Console.WriteLine);
}
else if (string.Equals(command, "download", StringComparison.OrdinalIgnoreCase))
{
    var (Route, Folder) = ReadDownloadOptions(args);
    await webDav.DownloadRouteAsync(Route, Folder, Console.WriteLine);
}
else
{
    throw new ArgumentException($"Unknown command '{command}'. Use 'clean' or 'download'.");
}

static (string Route, string Folder) ReadDownloadOptions(string[] arguments)
{
    string? route = null;
    string? folder = null;
    for (int index = 1; index < arguments.Length - 1; index++)
    {
        if (arguments[index] is "-route" or "--route") route = arguments[++index];
        else if (arguments[index] is "-folder" or "--folder") folder = arguments[++index];
    }

    return !string.IsNullOrWhiteSpace(route) && !string.IsNullOrWhiteSpace(folder)
        ? (route, folder)
        : throw new ArgumentException("The download command requires -route and -folder.");
}
