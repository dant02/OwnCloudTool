# OwnCloudTool

Small .NET 8 console utility for cleaning an ownCloud WebDAV folder.

## What It Does

The current implementation:

- Accepts `-host`, `-route`, and `-credentials` command-line arguments.
- Sends a WebDAV `PROPFIND` request to `host + route`.
- Reads all returned `DAV:href` entries from the XML response.
- Sends an HTTP `DELETE` request for each returned path except the route itself.

This makes the tool useful for clearing folders such as an ownCloud trash-bin route.

## Usage

```powershell
dotnet run --project App -- `
  -host https://example.com `
  -route /remote.php/dav/trash-bin/example-user/ `
  -credentials username:password
```

Arguments:

- `-host`: Base server URL, for example `https://example.com`
- `-route`: WebDAV route to inspect and clean
- `-credentials`: Basic auth credentials in the form `username:password`

## Build

```powershell
dotnet build OwnCloudTool.sln
```

To publish a release build:

```powershell
build.bat
```

## Warning

This tool performs real delete operations. It does not currently support dry-run mode, confirmation prompts, or detailed safety checks, so make sure the `-route` value points to the intended folder before running it.
