param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'OffWorkWidget.WinUI.csproj'
if ($Publish) {
    $destination = Join-Path $PSScriptRoot 'publish'
    & dotnet publish $project -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $destination
} else {
    & dotnet build $project -c Release -p:Platform=x64 -r win-x64
}
if ($LASTEXITCODE -ne 0) { throw 'WinUI 3 build failed.' }