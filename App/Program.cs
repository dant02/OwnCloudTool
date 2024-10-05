using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;

string? host, route, credentials;
host = route = credentials = null;

bool ReadArg(string key, int index, out string? value)
{
    bool result = string.Equals("-" + key, args[index], StringComparison.OrdinalIgnoreCase) && args.Length > index + 1;
    value = result ? args[index + 1] : null;
    return result;
}

void CheckArg(string key, string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new ArgumentException($"Missing required '-{key}' argument");
    }
}

HttpRequestMessage GetRequest(HttpMethod httpMethod, string requestUri)
{
    var request = new HttpRequestMessage(httpMethod, requestUri);
    request.Headers.Authorization
        = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials!)));
    return request;
}

for (int i = 0; i < args.Length; i++)
{
    if (ReadArg(nameof(host), i, out string? value))
    {
        host = value;
    }

    if (ReadArg(nameof(route), i, out value))
    {
        route = value;
    }

    if (ReadArg(nameof(credentials), i, out value))
    {
        credentials = value;
    }
}

CheckArg(nameof(host), host);
CheckArg(nameof(route), route);
CheckArg(nameof(credentials), credentials);

using var client = new HttpClient();

// request contents of a folder
using var request = GetRequest(new HttpMethod("PROPFIND"), host + route);
using var response = await client.SendAsync(request);

var xml = new XmlDocument();
xml.Load(response.Content.ReadAsStream());

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
    if (string.Equals(path, route, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    // request to delete the file
    using var req = GetRequest(HttpMethod.Delete, host + path);
    using var deleteReponse = await client.SendAsync(req);
}