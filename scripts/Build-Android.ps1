param([switch]$ConnectedTests, [switch]$Offline, [string]$GradleUserHome)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$androidRoot = Join-Path $projectRoot 'android'
if (-not $GradleUserHome) { $GradleUserHome = Join-Path $projectRoot '.build\android-gradle' }
$argsList = @('-p', $androidRoot, '--gradle-user-home', $GradleUserHome, '--project-cache-dir', (Join-Path $projectRoot '.build\android-project'), '--no-daemon', '--max-workers', '2', 'testDebugUnitTest', 'lintDebug', 'assembleDebug')
if ($Offline) { $argsList += '--offline' }
if ($ConnectedTests) { $argsList += 'connectedDebugAndroidTest' }
& (Join-Path $androidRoot 'gradlew.bat') @argsList
if ($LASTEXITCODE -ne 0) { throw 'Android build or tests failed.' }
$config = Get-Content -LiteralPath (Join-Path $androidRoot 'app\build.gradle') -Raw
$version = [regex]::Match($config, "versionName '([^']+)'").Groups[1].Value
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid Android version.' }
$output = Join-Path $projectRoot "Packages\Android\$version"
New-Item -ItemType Directory -Path $output -Force | Out-Null
$apk = Join-Path $output "Listy-Android-$version.apk"
Copy-Item -LiteralPath (Join-Path $androidRoot 'app\build\outputs\apk\debug\app-debug.apk') -Destination $apk -Force
$sum = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash.ToLowerInvariant() + "  Listy-Android-$version.apk" + [Environment]::NewLine
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), $sum, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $androidRoot 'README.md') -Destination (Join-Path $output 'Instrukcja.md') -Force
Write-Host "APK: $apk"
