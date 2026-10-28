# OwnCloudTool

[![Build](https://github.com/dant02/OwnCloudTool/actions/workflows/build.yml/badge.svg)](https://github.com/dant02/OwnCloudTool/actions/workflows/build.yml)

Cross-platform .NET 10 utility and LAN web dashboard for managing an ownCloud WebDAV folder, such as an ownCloud trash-bin route.

## Current Behavior

The application currently:

- loads settings from `appsettings.json`
- reads `Host` and `Route` from the `OwnCloud` configuration section
- uses `OWNCLOUD_CREDENTIALS` as an environment-variable override for credentials when present
- supports a default `clean` command that deletes items from the configured `Route`
- supports a `download` command that recursively downloads files from a WebDAV route to a local folder
- shows console progress while scanning folders and downloading files
- provides an authenticated web dashboard that displays active and completed jobs

Example target route:

```text
/remote.php/dav/trash-bin/example-user/
```

## Configuration

The application expects `appsettings.json` next to the executable. A sample file is included in the project at [App/appsettings.json](App/appsettings.json).

Example:

```json
{
  "OwnCloud": {
    "Host": "https://example.com",
    "Route": "/remote.php/dav/trash-bin/example-user/",
    "Credentials": ""
  }
}
```

Configuration values:

- `OwnCloud:Host`: base server URL, for example `https://example.com`
- `OwnCloud:Route`: WebDAV route to inspect and clean
- `OwnCloud:Credentials`: Basic auth credentials in the form `username:password`

If the `OWNCLOUD_CREDENTIALS` environment variable is set, it overrides `OwnCloud:Credentials` from the file. That is the recommended way to provide secrets on a server.

The Visual Studio launch profiles in [App/Properties/launchSettings.json](App/Properties/launchSettings.json) contain sample `OWNCLOUD_CREDENTIALS` values for local debugging.

## Run Locally

1. Update [App/appsettings.json](App/appsettings.json) with your server `Host` and `Route`.
2. Set credentials either in the config file or in the `OWNCLOUD_CREDENTIALS` environment variable.
3. Run one of the supported commands.

Clean the configured `OwnCloud:Route`:

```powershell
dotnet run --project App
```

You can also call the command explicitly:

```powershell
dotnet run --project App -- clean
```

Recursively download files from a WebDAV route to a local folder:

```powershell
dotnet run --project App -- download `
  -route /remote.php/dav/files/example-user/Documents/ `
  -folder C:\Temp\owncloud-download
```

Example PowerShell session using an environment variable:

```powershell
$env:OWNCLOUD_CREDENTIALS = "username:password"
dotnet run --project App -- download `
  -route /remote.php/dav/files/example-user/Documents/ `
  -folder C:\Temp\owncloud-download
```

The `download` command scans the source route recursively, creates matching local folders, downloads files with `GET`, and prints per-file byte progress in the console.

## Build And Publish

Build the solution:

```powershell
dotnet build OwnCloudTool.slnx
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

The published output includes `appsettings.json`.

## CI

GitHub Actions builds publishable release artifacts on every `push` and `pull_request`.

The workflow currently:

- restores the app project
- publishes a Windows release artifact named `owncloudtool-win-x64`
- publishes a Linux release artifact named `owncloudtool-linux-x64`
- publishes web-dashboard artifacts for both Windows and Linux

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

3. Update `/opt/owncloudtool/appsettings.json` with the real `Host` and `Route`.

4. Create `/etc/systemd/system/owncloudtool.service`:

```ini
[Unit]
Description=OwnCloud cleanup tool

[Service]
Type=oneshot
WorkingDirectory=/opt/owncloudtool
Environment=OWNCLOUD_CREDENTIALS=username:password
ExecStart=/opt/owncloudtool/App
```

5. Create `/etc/systemd/system/owncloudtool.timer`:

```ini
[Unit]
Description=Run OwnCloud cleanup hourly

[Timer]
OnCalendar=hourly
Persistent=true

[Install]
WantedBy=timers.target
```

6. Reload `systemd` and enable the timer:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now owncloudtool.timer
```

7. Check timer status and logs:

```bash
systemctl status owncloudtool.timer
systemctl list-timers --all
journalctl -u owncloudtool.service
```

## Current Limitations

- delete operations are real and irreversible
- there is no dry-run mode
- there are no confirmation prompts
- credentials still use Basic auth
- downloads overwrite existing local files with the same path

## Warning

Double-check the configured `Route` value before running this tool. It is intended for cleanup and will attempt to delete every returned entry under the target WebDAV route except the route itself.

## Web Dashboard

The `Web` project is an ASP.NET Core application that uses the shared `Core` library. It shows the configured server and cleanup route, active job progress, completed jobs, and failures. Cleanup and download actions run in the background, so the page remains responsive.

The dashboard is protected with HTTP Basic authentication. Before starting it, set these environment variables:

- `OWNCLOUD_CREDENTIALS`: ownCloud Basic-auth credentials (`username:password`)
- `OWNCLOUDTOOL_WEB_PASSWORD`: a strong password for dashboard access (required)
- `OWNCLOUDTOOL_WEB_USERNAME`: dashboard user name (optional; defaults to `admin`)

Run locally:

```bash
export OWNCLOUD_CREDENTIALS='username:password'
export OWNCLOUDTOOL_WEB_PASSWORD='use-a-long-random-password'
dotnet run --project Web
```

The web app reads the same `appsettings.json` format as the CLI. It only stores job history in memory; restarting the service clears that history.

### Ubuntu with Apache

Publish the Linux web project and copy its output to `/opt/owncloudtool-web`:

```bash
dotnet publish Web/Web.csproj -c Release -r linux-x64 --self-contained false -o ./publish/web-linux-x64
```

Install [Deployment/owncloudtool-web.service](Deployment/owncloudtool-web.service) as `/etc/systemd/system/owncloudtool-web.service`, replace its example credentials, then enable it:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now owncloudtool-web.service
```

Enable Apache's proxy modules, copy [Deployment/owncloudtool-web.apache.conf](Deployment/owncloudtool-web.apache.conf) into an enabled site, set `ServerName` to the LAN DNS name or server address, then reload Apache:

```bash
sudo a2enmod proxy proxy_http
sudo a2ensite owncloudtool-web
sudo systemctl reload apache2
```

The service binds only to `127.0.0.1:5080`; Apache is the only LAN-facing component. Configure HTTPS on the Apache virtual host before exposing it outside a trusted network.

### Windows

Publish `Web/Web.csproj` with runtime `win-x64`, set the same environment variables, and host the resulting `Web.exe` as a Windows Service. IIS may be used as a reverse proxy, but it is not required for a LAN-only deployment if Kestrel is bound to a suitable private-network address.
