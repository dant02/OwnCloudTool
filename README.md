# OwnCloudTool

[![Build](https://github.com/dant02/OwnCloudTool/actions/workflows/build.yml/badge.svg)](https://github.com/dant02/OwnCloudTool/actions/workflows/build.yml)

Small .NET 8 console utility for cleaning an ownCloud WebDAV folder, such as an ownCloud trash-bin route.

## Current Behavior

The application currently:

- loads settings from `appsettings.json`
- reads `Host` and `Route` from the `OwnCloud` configuration section
- uses `OWNCLOUD_CREDENTIALS` as an environment-variable override for credentials when present
- supports a default `clean` command that deletes items from the configured `Route`
- supports a `download` command that recursively downloads files from a WebDAV route to a local folder
- shows console progress while scanning folders and downloading files

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

The published output includes `appsettings.json`.

## CI

GitHub Actions builds publishable release artifacts on every `push` and `pull_request`.

The workflow currently:

- restores the app project
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
