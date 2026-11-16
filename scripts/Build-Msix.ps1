param(
    [string]$CertificateThumbprint,
    [string]$PackageVersion,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$sdk = Get-ChildItem -LiteralPath $sdkRoot -Directory |
    Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) -and (Test-Path (Join-Path $_.FullName 'x64\signtool.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Install Windows SDK with MakeAppx and SignTool.' }
$makeappx = Join-Path $sdk.FullName 'x64\makeappx.exe'
$signtool = Join-Path $sdk.FullName 'x64\signtool.exe'
$manifestPath = Join-Path $projectRoot 'packaging\AppxManifest.xml'
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$publisher = $manifest.Package.Identity.Publisher
if (-not $PackageVersion) {
    [xml]$project = Get-Content (Join-Path $projectRoot 'src\Listy.App\Listy.App.csproj') -Raw
    $PackageVersion = "$($project.Project.PropertyGroup.Version).0"
}
if ($PackageVersion -notmatch '^\d+\.\d+\.\d+\.0$' -or ([version]$PackageVersion).Major -eq 0) {
    throw 'Store package version must have four components, a positive major version and final component 0.'
}
$manifest.Package.Identity.Version = $PackageVersion
$certificates = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date) -and
    ($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })
})
if ($CertificateThumbprint) { $certificates = @($certificates | Where-Object Thumbprint -eq $CertificateThumbprint) }
$certificate = $certificates | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $certificate) { throw "No valid code-signing certificate with private key found in CurrentUser\My for $publisher." }

$artifacts = Join-Path $projectRoot '.build'
if (-not $SkipTests) {
    foreach ($testProject in @('tests\Listy.Tests\Listy.Tests.csproj', 'tests\Listy.UiTests\Listy.UiTests.csproj')) {
        & dotnet run --project (Join-Path $projectRoot $testProject) -c Release --artifacts-path $artifacts
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $testProject" }
    }
}
# Fresh staging avoids carrying obsolete files into later packages. No private keys are exported.
$stage = Join-Path $artifacts ('msix-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $stage 'App'
$assets = Join-Path $stage 'Assets'
New-Item -ItemType Directory -Path $app, $assets -Force | Out-Null
& dotnet publish (Join-Path $projectRoot 'src\Listy.App\Listy.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false --artifacts-path $artifacts -o $app
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
$manifest.Save((Join-Path $stage 'AppxManifest.xml'))
Copy-Item (Join-Path $projectRoot 'LICENSE') $app
Copy-Item (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt') $app

Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Image]::FromFile((Join-Path $projectRoot 'src\Listy.App\Assets\Listy.png'))
try {
    foreach ($entry in @{StoreLogo=50; Square44x44Logo=44; Square150x150Logo=150}.GetEnumerator()) {
        $bitmap = [System.Drawing.Bitmap]::new($entry.Value, $entry.Value)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.DrawImage($source, 0, 0, $entry.Value, $entry.Value)
            $bitmap.Save((Join-Path $assets ($entry.Key + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }

$output = Join-Path $projectRoot "Packages\$PackageVersion"
New-Item -ItemType Directory -Path $output -Force | Out-Null
$store = Join-Path $output "Listy_${PackageVersion}_x64_Store.msix"
$test = Join-Path $output "Listy_${PackageVersion}_x64_Test.msix"
& $makeappx pack /d $stage /p $store /o
if ($LASTEXITCODE -ne 0) { throw 'MSIX manifest validation or packing failed.' }
Copy-Item -LiteralPath $store -Destination $test -Force
& $signtool sign /fd SHA256 /s My /sha1 $certificate.Thumbprint $test
if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
& $signtool verify /pa /v $test
if ($LASTEXITCODE -ne 0) { throw 'MSIX signature verification failed.' }
Export-Certificate -Cert $certificate -FilePath (Join-Path $output 'Listy-Test.cer') -Type CERT -Force | Out-Null
$lines = Get-ChildItem $output -File | Where-Object Extension -in '.msix', '.cer' | Sort-Object Name | ForEach-Object {
    (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), [string[]]$lines, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\README.md') -Destination (Join-Path $output 'Instrukcja.md') -Force
Write-Host "Packages: $output"
Write-Host "Signing certificate: $($certificate.Thumbprint) (expires $($certificate.NotAfter.ToString('yyyy-MM-dd')))"
