# OwnCloudTool

[![Build](https://github.com/dant02/OwnCloudTool/actions/workflows/build.yml/badge.svg)](https://github.com/dant02/OwnCloudTool/actions/workflows/build.yml)

Small .NET 8 console utility for cleaning an ownCloud WebDAV folder, such as an ownCloud trash-bin route.

## Current Behavior

The application currently:

- accepts `-host`, `-route`, and `-credentials` command-line arguments
- sends a WebDAV `PROPFIND` request to `host + route`
- reads `DAV:href` entries from the XML response
- sends an HTTP `DELETE` request for each returned path except the route itself

Example target route:

```text
/remote.php/dav/trash-bin/example-user/
```

## Run Locally

Example:

```powershell
dotnet run --project App -- `
  -host https://example.com `
  -route /remote.php/dav/trash-bin/example-user/ `
  -credentials username:password
```

Arguments:

- `-host`: base server URL, for example `https://example.com`
- `-route`: WebDAV route to inspect and clean
- `-credentials`: Basic auth credentials in the form `username:password`

The Visual Studio launch profile in [App/Properties/launchSettings.json](App/Properties/launchSettings.json) contains a sample command line for local debugging.

## Build And Publish

Build the solution:

```powershell
dotnet build OwnCloudTool.sln
```

Publish both supported release targets with the helper script:

```powershell
build.bat
```

That script currently creates:

- Windows release: `win-x64`
- Linux release: `linux-x64`

You can also publish individual targets manually:

```powershell
dotnet publish App/App.csproj -c Release -r win-x64 --self-contained false
dotnet publish App/App.csproj -c Release -r linux-x64 --self-contained false
```

## CI

GitHub Actions builds publishable release artifacts on every `push` and `pull_request`.

The workflow currently:

- restores for both `win-x64` and `linux-x64`
- publishes a Windows release artifact named `owncloudtool-win-x64`
- publishes a Linux release artifact named `owncloudtool-linux-x64`

Workflow file:

- [.github/workflows/build.yml](.github/workflows/build.yml)

## Run Hourly On Ubuntu With systemd

1. Publish the Linux release.

```bash
dotnet publish App/App.csproj -c Release -r linux-x64 --self-contained false
```

2. Copy the published files to the server, for example:

```bash
/opt/owncloudtool/
```

3. Create `/etc/systemd/system/owncloudtool.service`:

```ini
[Unit]
Description=OwnCloud cleanup tool

[Service]
Type=oneshot
WorkingDirectory=/opt/owncloudtool
ExecStart=/opt/owncloudtool/App -host https://example.com -route /remote.php/dav/trash-bin/example-user/ -credentials username:password
```

4. Create `/etc/systemd/system/owncloudtool.timer`:

```ini
[Unit]
Description=Run OwnCloud cleanup hourly

[Timer]
OnCalendar=hourly
Persistent=true

[Install]
WantedBy=timers.target
```

5. Reload `systemd` and enable the timer:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now owncloudtool.timer
```

6. Check timer status and logs:

```bash
systemctl status owncloudtool.timer
systemctl list-timers --all
journalctl -u owncloudtool.service
```

## Current Limitations

- delete operations are real and irreversible
- there is no dry-run mode
- there are no confirmation prompts
- HTTP response handling is still minimal
- credentials are currently passed on the command line

## Warning

Double-check the `-route` value before running this tool. It is intended for cleanup and will attempt to delete every returned entry under the target WebDAV route except the route itself.

Passing credentials on the command line is convenient but not ideal for production servers because process arguments may be visible to other users or logs. Consider moving credentials to a safer mechanism before long-term unattended use.
