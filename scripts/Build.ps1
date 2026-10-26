param([switch]$Portable)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $projectRoot 'src\Listy.App\Listy.App.csproj'
$testsProject = Join-Path $projectRoot 'tests\Listy.Tests\Listy.Tests.csproj'
& dotnet run --project $testsProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Testy nie przeszły.' }
if ($Portable) {
    & dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $projectRoot 'Program')
} else {
    & dotnet build $appProject -c Release
}
if ($LASTEXITCODE -ne 0) { throw 'Budowanie nie powiodło się.' }
