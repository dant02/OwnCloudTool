:: build release versions

dotnet publish OwnCloudTool.sln -c Release -r win-x64 --self-contained false
dotnet publish OwnCloudTool.sln -c Release -r linux-x64 --self-contained false
