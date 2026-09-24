param(
 [string]$Version,
 [string]$CertificateThumbprint,
 [string]$SignToolPath,
 [string]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
if (-not $Version -or [string]::IsNullOrWhiteSpace($Version)) {
 $Version = ([System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'VERSION'))).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Expected -Version as X.Y.Z, got: $Version" }
$fileVersion = "$Version.0"
if ($CertificateThumbprint) {
 if ($CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw 'Expected a certificate SHA-1 thumbprint (40 hex characters).' }
 if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) { throw 'Provide -SignToolPath pointing to signtool.exe from the Windows SDK.' }
 $cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop
 if (-not $cert.HasPrivateKey -or $cert.NotAfter -le (Get-Date) -or $cert.NotBefore -gt (Get-Date)) { throw 'A valid code-signing certificate with its private key is required.' }
 if (-not ($cert.EnhancedKeyUsageList | Where-Object { $_.ObjectId.Value -eq '1.3.6.1.5.5.7.3.3' })) { throw 'Certificate does not have the Code Signing EKU.' }
}
function Sign-ReleaseFile([string]$Path) {
 if (-not $CertificateThumbprint) { return }
 & $SignToolPath sign /sha1 $CertificateThumbprint /s My /fd SHA256 /tr $TimestampUrl /td SHA256 $Path
 if ($LASTEXITCODE -ne 0) { throw "Signing failed: $Path" }
 & $SignToolPath verify /pa /all /v $Path
 if ($LASTEXITCODE -ne 0) { throw "Signature verification failed: $Path" }
 $signature = Get-AuthenticodeSignature -LiteralPath $Path
 if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint -or -not $signature.TimeStamperCertificate) {
  throw "Expected a trusted, timestamped signature from the selected publisher: $Path"
 }
}
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'dist') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'obj') | Out-Null
$versionInfo = Join-Path $PSScriptRoot 'obj\VersionInfo.cs'
$versionInfoText = @"
using System.Reflection;
[assembly: AssemblyVersion("$fileVersion")]
[assembly: AssemblyFileVersion("$fileVersion")]
[assembly: AssemblyInformationalVersion("$Version")]
public static class BuildInfo {
 public const string Version = "$Version";
}
"@
[System.IO.File]::WriteAllText($versionInfo, $versionInfoText)
Push-Location $PSScriptRoot
try {
 $refs = @('/r:System.dll','/r:System.Core.dll','/r:System.Xml.dll','/r:System.Xml.Linq.dll','/r:System.Windows.Forms.dll',"/r:$framework\WPF\WindowsBase.dll","/r:$framework\WPF\PresentationCore.dll","/r:$framework\WPF\PresentationFramework.dll",'/r:System.Xaml.dll')
 & $compiler /nologo /target:winexe /win32manifest:src\app.manifest /win32icon:src\FlightBridge.ico /optimize+ /out:dist\FlightBridge.exe @refs /resource:src\FlightBridge.png,FlightBridge.png src\AssemblyInfo.cs obj\VersionInfo.cs src\Core.cs src\Library.cs src\AutoMigration.cs src\MigrationTransaction.cs src\AppLocalization.cs src\AutomaticApp.cs src\App.cs
 if ($LASTEXITCODE -ne 0) { throw 'App build failed' }
 & $compiler /nologo /target:exe /out:dist\CoreTests.exe /r:System.Core.dll /r:System.Xml.Linq.dll src\Core.cs src\Library.cs tests\CoreTests.cs
 if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
 & .\dist\CoreTests.exe
 if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
 & $compiler /nologo /target:exe /out:dist\AutoMigrationTests.exe /r:System.Core.dll /r:System.Xml.Linq.dll obj\VersionInfo.cs src\Core.cs src\AutoMigration.cs src\MigrationTransaction.cs tests\AutoMigrationTests.cs
 if ($LASTEXITCODE -ne 0) { throw 'Automatic migration test build failed' }
 & .\dist\AutoMigrationTests.exe
 if ($LASTEXITCODE -ne 0) { throw 'Automatic migration tests failed' }
 & $compiler /nologo /target:exe /out:dist\AppLocalizationTests.exe /r:System.Core.dll src\AppLocalization.cs tests\AppLocalizationTests.cs
 if ($LASTEXITCODE -ne 0) { throw 'App localization test build failed' }
 & .\dist\AppLocalizationTests.exe
 if ($LASTEXITCODE -ne 0) { throw 'App localization tests failed' }
 & $compiler /nologo /target:exe /out:dist\FlightBridge-Diagnostics.exe /r:System.Core.dll /r:System.Xml.Linq.dll obj\VersionInfo.cs src\Core.cs src\AutoMigration.cs scripts\Diagnostic.cs
 if ($LASTEXITCODE -ne 0) { throw 'Diagnostics build failed' }
 Sign-ReleaseFile '.\dist\FlightBridge.exe'
 & $compiler /nologo /target:winexe /win32manifest:src\app.manifest /win32icon:src\FlightBridge.ico /optimize+ /out:dist\FlightBridge-Setup.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Xml.Linq.dll /r:System.Core.dll /resource:dist\FlightBridge.exe,FlightBridge.exe /resource:README.md,README.md /resource:src\SetupLanguages.xml,SetupLanguages.xml src\AssemblyInfo.cs obj\VersionInfo.cs src\SetupLocalization.cs src\AppLocalization.cs src\DeviceCheck.cs src\Setup.cs
 if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
 Sign-ReleaseFile '.\dist\FlightBridge-Setup.exe'
 & $compiler /nologo /target:exe /out:dist\DeviceCheckTests.exe /r:System.Management.dll /r:System.Xml.Linq.dll /r:System.Core.dll /resource:src\SetupLanguages.xml,SetupLanguages.xml src\SetupLocalization.cs src\DeviceCheck.cs tests\DeviceCheckTests.cs
 if ($LASTEXITCODE -ne 0) { throw 'Device test build failed' }
 & .\dist\DeviceCheckTests.exe
 if ($LASTEXITCODE -ne 0) { throw 'Device tests failed' }
 & .\scripts\GenerateGuides.ps1
 & .\tests\InstallerTests.ps1
 Get-FileHash .\dist\FlightBridge.exe, .\dist\FlightBridge-Setup.exe -Algorithm SHA256 | Format-Table
} finally { Pop-Location }

# Publish into a human-facing release folder and a flat dist/release/ with stable names.
$distRoot = Join-Path $PSScriptRoot 'dist'
$releaseName = "Flight Bridge $Version - CURRENT"
$release = Join-Path $distRoot $releaseName
$portable = Join-Path $release 'Portable (no installation)'
$developer = Join-Path $distRoot '_Developer files - do not install'
$flatRelease = Join-Path $distRoot 'release'
$archiveRoot = Join-Path $distRoot 'Archive - old versions'
Get-ChildItem -LiteralPath $distRoot -Directory -ErrorAction SilentlyContinue |
 Where-Object { $_.Name -like 'Flight Bridge * - CURRENT' -and $_.Name -ne $releaseName } |
 ForEach-Object {
  New-Item -ItemType Directory -Force -Path $archiveRoot | Out-Null
  $dest = Join-Path $archiveRoot ($_.Name -replace ' - CURRENT$','')
  if (-not (Test-Path -LiteralPath $dest)) { Move-Item -LiteralPath $_.FullName -Destination $dest }
 }
if (Test-Path -LiteralPath $release) { Remove-Item -LiteralPath $release -Recurse -Force }
if (Test-Path -LiteralPath $flatRelease) { Remove-Item -LiteralPath $flatRelease -Recurse -Force }
New-Item -ItemType Directory -Force -Path $release,$portable,$developer,$flatRelease | Out-Null
$appExe = Join-Path $distRoot 'FlightBridge.exe'
$setupExe = Join-Path $distRoot 'FlightBridge-Setup.exe'
Move-Item -LiteralPath $setupExe -Destination (Join-Path $release 'FlightBridge-Setup.exe') -Force
Move-Item -LiteralPath $appExe -Destination (Join-Path $portable 'FlightBridge.exe') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $release 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $portable 'README.md') -Force
$zip = Join-Path $release 'FlightBridge-portable.zip'
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -Force
Copy-Item -LiteralPath (Join-Path $portable 'FlightBridge.exe') -Destination (Join-Path $flatRelease 'FlightBridge.exe') -Force
Copy-Item -LiteralPath (Join-Path $release 'FlightBridge-Setup.exe') -Destination (Join-Path $flatRelease 'FlightBridge-Setup.exe') -Force
Copy-Item -LiteralPath $zip -Destination (Join-Path $flatRelease 'FlightBridge-portable.zip') -Force
$shaLines = foreach ($name in @('FlightBridge.exe','FlightBridge-portable.zip','FlightBridge-Setup.exe')) {
 $hash = (Get-FileHash -LiteralPath (Join-Path $flatRelease $name) -Algorithm SHA256).Hash.ToLowerInvariant()
 '{0}  {1}' -f $hash,$name
}
$shaLines | Set-Content -LiteralPath (Join-Path $flatRelease 'SHA256.txt') -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $flatRelease 'SHA256.txt') -Destination (Join-Path $release 'SHA256.txt') -Force
foreach ($name in @('CoreTests.exe','AutoMigrationTests.exe','AppLocalizationTests.exe','DeviceCheckTests.exe','FlightBridge-Diagnostics.exe')) {
 $source = Join-Path $distRoot $name
 if (Test-Path -LiteralPath $source) { Move-Item -LiteralPath $source -Destination (Join-Path $developer $name) -Force }
}
