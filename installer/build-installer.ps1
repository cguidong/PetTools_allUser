param([string]$CompilerPath,[string]$PayloadDir,[string]$OutputDir,[switch]$SkipPublish,[switch]$InstallerQATest)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$OutputDir) { $OutputDir = Join-Path (Split-Path $root -Parent) '下班倒计时-安装包' }
if (!$PayloadDir) { $PayloadDir = Join-Path $root 'publish' }
if (!$CompilerPath) {
    $candidates = @($env:ISCC, (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'), (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
    $CompilerPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw '请安装 Inno Setup 6.7.3 或更新版本，并通过 -CompilerPath 指定 ISCC.exe。' }
if (!$SkipPublish) {
    & dotnet publish (Join-Path $root 'OffWorkWidget.WinUI.csproj') -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $PayloadDir
    if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
}
$PayloadDir = (Resolve-Path -LiteralPath $PayloadDir).Path
foreach ($required in @('OffWorkWidget.WinUI.exe','OffWorkWidget.WinUI.dll','App.xbf','OffWorkWidget.WinUI.pri','OffWorkWidget.WinUI.runtimeconfig.json','coreclr.dll','Microsoft.UI.Xaml.dll','CustomEffectRuntimeNative.dll','assets\app-icon.ico','prerequisites\VC_redist.x64.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $PayloadDir $required))) { throw "发布包缺少 $required" }
}
$redist = Join-Path $PayloadDir 'prerequisites\VC_redist.x64.exe'
$signature = Get-AuthenticodeSignature -LiteralPath $redist
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'VC++ 运行库微软签名验证失败。' }
if ([version](Get-Item -LiteralPath $redist).VersionInfo.ProductVersion -lt [version]'14.51.36247.0') { throw 'VC++ 运行库版本过低。' }
$version = (Get-Item -LiteralPath (Join-Path $PayloadDir 'OffWorkWidget.WinUI.exe')).VersionInfo.ProductVersion.Split('+')[0]
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$arguments = @("/DPayloadDir=$PayloadDir", "/DOutputDir=$OutputDir", "/DAppVersion=$version")
if ($InstallerQATest) { $arguments += '/DInstallerQATest' }
& $CompilerPath @arguments (Join-Path $PSScriptRoot 'OffWorkWidget.iss')
if ($LASTEXITCODE -ne 0) { throw '安装包编译失败。' }
$name = if ($InstallerQATest) { 'OffWorkWidget-Installer-QA.exe' } else { "下班倒计时-Setup-$version-x64.exe" }
$artifact = Join-Path $OutputDir $name
$hash = Get-FileHash -LiteralPath $artifact -Algorithm SHA256
[IO.File]::WriteAllText("$artifact.sha256", "$($hash.Hash)  $name`r`n", [Text.UTF8Encoding]::new($true))
Get-Item -LiteralPath $artifact | Select-Object FullName, Length
$hash | Select-Object Algorithm, Hash