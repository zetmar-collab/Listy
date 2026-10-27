param([switch]$Portable)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $projectRoot 'src\Listy.App\Listy.App.csproj'
$testsProject = Join-Path $projectRoot 'tests\Listy.Tests\Listy.Tests.csproj'
$artifactsPath = Join-Path $projectRoot '.build'
& dotnet run --project $testsProject -c Release --artifacts-path $artifactsPath
if ($LASTEXITCODE -ne 0) { throw 'Testy nie przeszły.' }
& dotnet run --project (Join-Path $projectRoot 'tests\Listy.UiTests\Listy.UiTests.csproj') -c Release --artifacts-path $artifactsPath
if ($LASTEXITCODE -ne 0) { throw 'Testy interfejsu nie przeszły.' }
if ($Portable) {
    & dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false --artifacts-path $artifactsPath -o (Join-Path $projectRoot 'Program')
} else {
    & dotnet build $appProject -c Release --artifacts-path $artifactsPath
}
if ($LASTEXITCODE -ne 0) { throw 'Budowanie nie powiodło się.' }
if ($Portable) {
    $exePath = Join-Path $projectRoot 'Program\Listy.exe'
    $exeHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText((Join-Path $projectRoot 'Program\SHA256SUMS.txt'), ($exeHash + '  Listy.exe' + [Environment]::NewLine), [System.Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $projectRoot 'Program\THIRD-PARTY-NOTICES.txt')
}
