param([string]$CompilerPath,[string]$PayloadDir,[string]$OutputDir,[switch]$SkipPublish,[switch]$InstallerQATest,[string]$MinWindowsVersion,[string]$OutputName)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$OutputDir) { $OutputDir = Join-Path (Split-Path $root -Parent) '有盼-安装包' }
if (!$PayloadDir) { $PayloadDir = Join-Path $root 'publish' }
if (!$CompilerPath) {
    $candidates = @(
        $env:ISCC,
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path (Split-Path (Split-Path $root -Parent) -Parent) 'work\installer-release\tools\InnoSetup\ISCC.exe')
    )
    $CompilerPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw '请安装 Inno Setup 6.7.3 或更新版本，并通过 -CompilerPath 指定 ISCC.exe。' }
if (!$SkipPublish) {
    & dotnet publish (Join-Path $root 'OffWorkWidget.WinUI.csproj') -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $PayloadDir
    if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
}
$PayloadDir = (Resolve-Path -LiteralPath $PayloadDir).Path
$vcRuntime = Join-Path $root 'prerequisites\vc-runtime-x64'
if (Test-Path -LiteralPath $vcRuntime) {
    Copy-Item -Path (Join-Path $vcRuntime '*.dll') -Destination $PayloadDir -Force
}
foreach ($required in @('OffWorkWidget.WinUI.exe','OffWorkWidget.WinUI.dll','App.xbf','OffWorkWidget.WinUI.pri','OffWorkWidget.WinUI.runtimeconfig.json','coreclr.dll','Microsoft.UI.Xaml.dll','CustomEffectRuntimeNative.dll','vcruntime140.dll','msvcp140.dll','assets\app-icon.ico','assets\polar-bear-idle.frames.br','assets\polar-bear-nap.frames.br','assets\polar-bear-start-work.frames.br')) {
    if (!(Test-Path -LiteralPath (Join-Path $PayloadDir $required))) { throw "发布包缺少 $required" }
}
# WinUI ships MUI files for every Windows language. Keep Chinese, English and the app assets only.
$keepDirs = @('assets','Microsoft.UI.Xaml','en-us','zh-CN','zh-TW')
Get-ChildItem -LiteralPath $PayloadDir -Directory | Where-Object {
    $_.Name -notin $keepDirs -and (Get-ChildItem -LiteralPath $_.FullName -Filter *.mui -File -Recurse -ErrorAction SilentlyContinue)
} | Remove-Item -Recurse -Force
# The VC++ runtime is app-local; the old redist payload is no longer installed.
$legacyRedist = Join-Path $PayloadDir 'prerequisites\VC_redist.x64.exe'
if (Test-Path -LiteralPath $legacyRedist) { Remove-Item -LiteralPath $legacyRedist -Force }
$version = (Get-Item -LiteralPath (Join-Path $PayloadDir 'OffWorkWidget.WinUI.exe')).VersionInfo.ProductVersion.Split('+')[0]
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$arguments = @("/DPayloadDir=$PayloadDir", "/DOutputDir=$OutputDir", "/DAppVersion=$version")
if ($InstallerQATest) { $arguments += '/DInstallerQATest' }
if ($MinWindowsVersion) { $arguments += "/DMinWindowsVersion=$MinWindowsVersion" }
if ($OutputName) { $arguments += "/DCustomOutputName=$OutputName" }
& $CompilerPath @arguments (Join-Path $PSScriptRoot 'OffWorkWidget.iss')
if ($LASTEXITCODE -ne 0) { throw '安装包编译失败。' }
if (!$InstallerQATest) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $OutputDir 'README.md') -Force
}
$name = if ($InstallerQATest) { 'OffWorkWidget-Installer-QA.exe' } elseif ($OutputName) { "$OutputName.exe" } else { "有盼-Setup-$version-x64.exe" }
$artifact = Join-Path $OutputDir $name
$hash = Get-FileHash -LiteralPath $artifact -Algorithm SHA256
[IO.File]::WriteAllText("$artifact.sha256", "$($hash.Hash)  $name`r`n", [Text.UTF8Encoding]::new($true))
Get-Item -LiteralPath $artifact | Select-Object FullName, Length
$hash | Select-Object Algorithm, Hash
