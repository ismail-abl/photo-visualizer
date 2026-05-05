Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'PhotoApp/PhotoApp.csproj'
$exe = Join-Path $root 'PhotoApp/bin/Release/net10.0/win-x64/publish/PhotoApp.exe'

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

& $exe