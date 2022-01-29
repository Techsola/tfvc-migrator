$ErrorActionPreference = 'Stop'

# Options
$configuration = 'Release'
$artifactsDir = Join-Path (Resolve-Path .) 'artifacts'
$binDir = Join-Path $artifactsDir 'Bin'
$testResultsDir = Join-Path $artifactsDir 'Test results'
$logsDir = Join-Path $artifactsDir 'Logs'

$dotnetArgs = @(
    '--configuration', $configuration
    '/p:ContinuousIntegrationBuild=' + ($env:CI -or $env:TF_BUILD)
)

# Build
dotnet build /bl:$logsDir\build.binlog @dotnetArgs
if ($LastExitCode) { exit 1 }

# Publish
Remove-Item -Recurse -Force $binDir -ErrorAction Ignore

dotnet publish src\TfvcMigrator --no-build --output $binDir /bl:$logsDir\publish.binlog @dotnetArgs
if ($LastExitCode) { exit 1 }

# Test
Remove-Item -Recurse -Force $testResultsDir -ErrorAction Ignore

dotnet test --no-build --logger trx --results-directory $testResultsDir /bl:$logsDir\test.binlog @dotnetArgs
if ($LastExitCode) { exit 1 }
