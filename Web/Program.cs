using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.HttpOverrides;
using OwnCloudTool.Core;
using OwnCloudTool.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<JobStore>();
builder.Services.AddHttpClient<WebDavClient>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost
        | ForwardedHeaders.XForwardedPrefix;
});
var app = builder.Build();

app.UseForwardedHeaders();

string webUser = Environment.GetEnvironmentVariable("OWNCLOUDTOOL_WEB_USERNAME") ?? "admin";
string? webPassword = Environment.GetEnvironmentVariable("OWNCLOUDTOOL_WEB_PASSWORD");
if (string.IsNullOrWhiteSpace(webPassword))
    throw new InvalidOperationException("Set OWNCLOUDTOOL_WEB_PASSWORD before starting the web application.");

app.Use(async (context, next) =>
{
    string? authorization = context.Request.Headers.Authorization;
    bool valid = authorization?.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) == true && IsValid(authorization[6..], webUser, webPassword);
    if (!valid)
    {
        context.Response.Headers.WWWAuthenticate = "Basic realm=OwnCloudTool";
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/", (HttpRequest request, JobStore store) =>
{
    OwnCloudConfiguration config = LoadConfiguration();
    string pathBase = request.PathBase.Value ?? string.Empty;
    string jobs = string.Join(string.Empty, store.GetJobs().Select(job => $"<tr><td>{Html(job.Type)}</td><td>{Html(job.Status)}</td><td>{Html(job.LastMessage ?? job.Error ?? "-")}</td><td>{job.StartedAt.LocalDateTime:g}</td></tr>"));
    return Results.Content($$"""
        <!doctype html><html><head><title>OwnCloudTool</title><meta http-equiv="refresh" content="5"><style>body{font-family:system-ui;max-width:900px;margin:2rem auto}fieldset{margin:1rem 0;padding:1rem}input{min-width:28rem}button{padding:.5rem 1rem}table{border-collapse:collapse;width:100%}td,th{padding:.5rem;border-bottom:1px solid #ddd;text-align:left}.warning{color:#9b2c2c}</style></head><body>
        <h1>OwnCloudTool</h1><p>Server: <code>{{Html(config.Host)}}</code><br>Cleanup route: <code>{{Html(config.Route)}}</code></p>
        <fieldset><legend class="warning">Cleanup (permanently deletes items)</legend><form method="post" action="{{Html(pathBase)}}/jobs/clean"><button type="submit">Start cleanup</button></form></fieldset>
        <fieldset><legend>Download</legend><form method="post" action="{{Html(pathBase)}}/jobs/download"><label>WebDAV route <input name="route" required></label><br><label>Server folder <input name="folder" required></label><br><button type="submit">Start download</button></form></fieldset>
        <h2>Jobs</h2><table><tr><th>Type</th><th>Status</th><th>Latest detail</th><th>Started</th></tr>{{jobs}}</table></body></html>
        """, "text/html");
});

app.MapPost("/jobs/clean", (JobStore store) =>
{
    StartJob(store, "Cleanup", async (client, report)
        => await client.CleanConfiguredRouteAsync(report));
    return Results.LocalRedirect("~/");
});

app.MapPost("/jobs/download", async (HttpRequest request, JobStore store) =>
{
    IFormCollection form = await request.ReadFormAsync();
    string? route = form["route"];
    string? folder = form["folder"];
    if (string.IsNullOrWhiteSpace(route) || string.IsNullOrWhiteSpace(folder))
        return Results.BadRequest("Both route and folder are required.");

    StartJob(store, "Download", async (client, report) => await client.DownloadRouteAsync(route, folder, report));
    return Results.LocalRedirect("~/");
});
app.Run();

void StartJob(JobStore store, string type, Func<WebDavClient, Action<string>, Task> operation)
{
    WebJob job = store.Create(type);
    _ = Task.Run(async () =>
    {
        try
        {
            using var httpClient = new HttpClient();
            var client = new WebDavClient(httpClient, LoadConfiguration());
            await operation(client, job.Report);
            job.Complete();
        }
        catch (Exception exception)
        {
            job.Fail(exception);
        }
    });
}

OwnCloudConfiguration LoadConfiguration()
    => OwnCloudConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

static bool IsValid(string encoded, string username, string password)
{
    try
    {
        string value = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        byte[] expected = Encoding.UTF8.GetBytes($"{username}:{password}");
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), expected);
    }
    catch (FormatException)
    {
        return false;
    }
}

static string Html(string value) => HtmlEncoder.Default.Encode(value);
