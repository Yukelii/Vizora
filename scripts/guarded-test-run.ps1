param(
    [string]$AppProjectPath = "vizora.csproj",
    [string]$TestProjectPath = "tests/Vizora.Tests/Vizora.Tests.csproj",
    [int]$HangTimeoutSeconds = 60,
    [int]$SlowTestThresholdSeconds = 10,
    [int]$HighMemoryThresholdMb = 512
)

$ErrorActionPreference = "Stop"

function Get-TestProjectName {
    param([string]$TestName)

    if ([string]::IsNullOrWhiteSpace($TestName)) {
        return "Unknown"
    }

    $parts = $TestName.Split(".")
    if ($parts.Length -ge 2) {
        return "$($parts[0]).$($parts[1])"
    }

    return $parts[0]
}

function Add-MarkdownSection {
    param(
        [System.Text.StringBuilder]$Builder,
        [string]$Title
    )

    [void]$Builder.AppendLine("## $Title")
    [void]$Builder.AppendLine()
}

function Quote-ProcessArgument {
    param([string]$Value)

    if ($null -eq $Value) {
        return ""
    }

    if ($Value -match '[\s"]') {
        return '"' + ($Value -replace '"', '\"') + '"'
    }

    return $Value
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
Set-Location $repoRoot

$diagnosticsDir = Join-Path $repoRoot "diagnostics"
$resultsDir = Join-Path $diagnosticsDir "TestResults"
$diagLogPath = Join-Path $diagnosticsDir "test-run.log"
$consoleLogPath = Join-Path $diagnosticsDir "test-console.log"
$reportPath = Join-Path $diagnosticsDir "test-performance-report.md"

New-Item -ItemType Directory -Path $diagnosticsDir -Force | Out-Null
New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
Remove-Item $diagLogPath, $consoleLogPath, $reportPath -ErrorAction SilentlyContinue

Write-Host "Building application project: $AppProjectPath"
$buildStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
& dotnet build $AppProjectPath
$appBuildExitCode = $LASTEXITCODE
if ($appBuildExitCode -ne 0) {
    $buildStopwatch.Stop()
    throw "dotnet build failed for application project with exit code $appBuildExitCode."
}

Write-Host "Building test project: $TestProjectPath"
& dotnet build $TestProjectPath
$testBuildExitCode = $LASTEXITCODE
$buildStopwatch.Stop()

if ($testBuildExitCode -ne 0) {
    throw "dotnet build failed for test project with exit code $testBuildExitCode."
}

$testArgs = @(
    "test", $TestProjectPath,
    "--no-build",
    "--blame-hang",
    "--blame-hang-timeout", "${HangTimeoutSeconds}s",
    "--diag", $diagLogPath,
    "--results-directory", $resultsDir,
    "--logger", "trx;LogFileName=test-results.trx",
    "-v", "normal"
)

$processStartInfo = New-Object System.Diagnostics.ProcessStartInfo
$processStartInfo.FileName = "dotnet"
$processStartInfo.Arguments = ($testArgs | ForEach-Object { Quote-ProcessArgument $_ }) -join " "
$processStartInfo.UseShellExecute = $false
$processStartInfo.RedirectStandardOutput = $true
$processStartInfo.RedirectStandardError = $true
$processStartInfo.CreateNoWindow = $true

$testProcess = New-Object System.Diagnostics.Process
$testProcess.StartInfo = $processStartInfo

$logAction = {
    if ([string]::IsNullOrWhiteSpace($EventArgs.Data)) {
        return
    }

    $line = "$(Get-Date -Format o) $($EventArgs.Data)"
    Add-Content -Path $Event.MessageData.LogPath -Value $line
    Write-Host $line
}

$stdoutEvent = Register-ObjectEvent -InputObject $testProcess -EventName OutputDataReceived -Action $logAction -MessageData @{ LogPath = $consoleLogPath }
$stderrEvent = Register-ObjectEvent -InputObject $testProcess -EventName ErrorDataReceived -Action $logAction -MessageData @{ LogPath = $consoleLogPath }

Write-Host "Running guarded test execution with --blame-hang timeout ${HangTimeoutSeconds}s."
$testStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$maxWorkingSetBytes = 0L

$null = $testProcess.Start()
$testProcess.BeginOutputReadLine()
$testProcess.BeginErrorReadLine()

while (-not $testProcess.WaitForExit(1000)) {
    if ($testProcess.WorkingSet64 -gt $maxWorkingSetBytes) {
        $maxWorkingSetBytes = $testProcess.WorkingSet64
    }
}

$testProcess.WaitForExit()

if ($testProcess.WorkingSet64 -gt $maxWorkingSetBytes) {
    $maxWorkingSetBytes = $testProcess.WorkingSet64
}

$testStopwatch.Stop()
$testExitCode = $testProcess.ExitCode

Unregister-Event -SourceIdentifier $stdoutEvent.Name -ErrorAction SilentlyContinue
Unregister-Event -SourceIdentifier $stderrEvent.Name -ErrorAction SilentlyContinue
Remove-Job -Id $stdoutEvent.Id -Force -ErrorAction SilentlyContinue
Remove-Job -Id $stderrEvent.Id -Force -ErrorAction SilentlyContinue

$trxFile = Get-ChildItem -Path $resultsDir -Recurse -Filter "test-results.trx" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

$totalTests = 0
$executedTests = 0
$passedTests = 0
$failedTests = 0
$slowTests = @()

if ($null -ne $trxFile) {
    [xml]$trxXml = Get-Content -Path $trxFile.FullName
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($trxXml.NameTable)
    $namespaceManager.AddNamespace("trx", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

    $countersNode = $trxXml.SelectSingleNode("//trx:ResultSummary/trx:Counters", $namespaceManager)
    if ($null -ne $countersNode) {
        $totalTests = [int]$countersNode.total
        $executedTests = [int]$countersNode.executed
        $passedTests = [int]$countersNode.passed
        $failedTests = [int]$countersNode.failed
    }

    $resultNodes = $trxXml.SelectNodes("//trx:UnitTestResult", $namespaceManager)
    foreach ($node in $resultNodes) {
        $durationRaw = $node.duration
        if ([string]::IsNullOrWhiteSpace($durationRaw)) {
            continue
        }

        $duration = [TimeSpan]::Zero
        $parsedDuration = [TimeSpan]::TryParse(
            $durationRaw,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$duration)

        if (-not $parsedDuration) {
            try {
                $duration = [System.Xml.XmlConvert]::ToTimeSpan($durationRaw)
            }
            catch {
                continue
            }
        }
        if ($duration.TotalSeconds -gt $SlowTestThresholdSeconds) {
            $slowTests += [PSCustomObject]@{
                Name = [string]$node.testName
                DurationSeconds = [Math]::Round($duration.TotalSeconds, 2)
                Project = Get-TestProjectName -TestName ([string]$node.testName)
            }
        }
    }
}

$sequenceFiles = Get-ChildItem -Path $resultsDir -Recurse -Filter "Sequence.xml" -ErrorAction SilentlyContinue
$hangDetected = $false

if ($sequenceFiles.Count -gt 0) {
    $hangDetected = $true
}

if (-not $hangDetected -and (Test-Path $consoleLogPath)) {
    $hangIndicators = Select-String -Path $consoleLogPath -Pattern "blame-hang", "test host process crashed", "timed out" -SimpleMatch -ErrorAction SilentlyContinue
    if ($hangIndicators) {
        $hangDetected = $true
    }
}

$hangingTests = @()
if ($sequenceFiles.Count -gt 0) {
    foreach ($sequence in $sequenceFiles) {
        try {
            [xml]$sequenceXml = Get-Content -Path $sequence.FullName
            $testNodes = $sequenceXml.SelectNodes("//Test")
            if ($null -ne $testNodes -and $testNodes.Count -gt 0) {
                $lastNode = $testNodes[$testNodes.Count - 1]
                $hangingTests += [PSCustomObject]@{
                    Name = [string]$lastNode.Name
                    SequenceFile = $sequence.FullName
                }
            }
        }
        catch {
            # Keep report generation resilient if sequence files are malformed.
        }
    }
}

$rootCauseFindings = @()
if ($hangDetected) {
    $testFiles = Get-ChildItem -Path (Join-Path $repoRoot "tests") -Recurse -Filter "*.cs" -ErrorAction SilentlyContinue

    $patterns = @(
        @{ Label = "Possible infinite loop"; Regex = "while\s*\(\s*true\s*\)" },
        @{ Label = "Possible deadlock (Task.Result)"; Regex = "\.Result\b" },
        @{ Label = "Possible deadlock (Task.Wait)"; Regex = "\.Wait\s*\(" },
        @{ Label = "Fire-and-forget task"; Regex = "Task\.Run\s*\(" },
        @{ Label = "Direct database provider usage in test"; Regex = "UseNpgsql|UseSqlServer|UseMySql" }
    )

    foreach ($pattern in $patterns) {
        foreach ($file in $testFiles) {
            $matches = Select-String -Path $file.FullName -Pattern $pattern.Regex -ErrorAction SilentlyContinue
            foreach ($match in $matches) {
                $rootCauseFindings += [PSCustomObject]@{
                    Pattern = $pattern.Label
                    File = $file.FullName
                    Line = $match.LineNumber
                    Snippet = $match.Line.Trim()
                }
            }
        }
    }
}

$peakMemoryMb = [Math]::Round(($maxWorkingSetBytes / 1MB), 2)
$highMemoryFindings = @()
if ($peakMemoryMb -gt $HighMemoryThresholdMb) {
    $highMemoryFindings += [PSCustomObject]@{
        Scope = "dotnet test process"
        PeakMemoryMb = $peakMemoryMb
        ThresholdMb = $HighMemoryThresholdMb
    }
}

$reportBuilder = New-Object System.Text.StringBuilder
[void]$reportBuilder.AppendLine("# Test Performance Report")
[void]$reportBuilder.AppendLine()
[void]$reportBuilder.AppendLine("- Generated (UTC): $(Get-Date -Format u)")
[void]$reportBuilder.AppendLine("- Application project: $AppProjectPath")
[void]$reportBuilder.AppendLine("- Test project: $TestProjectPath")
[void]$reportBuilder.AppendLine("- Build duration: $([Math]::Round($buildStopwatch.Elapsed.TotalSeconds, 2))s")
[void]$reportBuilder.AppendLine("- Test execution duration: $([Math]::Round($testStopwatch.Elapsed.TotalSeconds, 2))s")
[void]$reportBuilder.AppendLine("- Total tests discovered: $totalTests")
[void]$reportBuilder.AppendLine("- Executed: $executedTests | Passed: $passedTests | Failed: $failedTests")
[void]$reportBuilder.AppendLine("- Slow test threshold: ${SlowTestThresholdSeconds}s")
[void]$reportBuilder.AppendLine("- Hang detected: $hangDetected")
[void]$reportBuilder.AppendLine("- Peak test runner working set: ${peakMemoryMb} MB")
[void]$reportBuilder.AppendLine("- Diagnostic log: diagnostics/test-run.log")
[void]$reportBuilder.AppendLine()

Add-MarkdownSection -Builder $reportBuilder -Title "Slow Tests (> $SlowTestThresholdSeconds s)"
if ($slowTests.Count -eq 0) {
    [void]$reportBuilder.AppendLine("No slow tests detected.")
    [void]$reportBuilder.AppendLine()
}
else {
    [void]$reportBuilder.AppendLine("| Test | Duration (s) | Project |")
    [void]$reportBuilder.AppendLine("| --- | ---: | --- |")
    foreach ($test in $slowTests | Sort-Object DurationSeconds -Descending) {
        [void]$reportBuilder.AppendLine("| $($test.Name) | $($test.DurationSeconds) | $($test.Project) |")
    }
    [void]$reportBuilder.AppendLine()
}

Add-MarkdownSection -Builder $reportBuilder -Title "High Memory Usage"
if ($highMemoryFindings.Count -eq 0) {
    [void]$reportBuilder.AppendLine("No high-memory runs detected above ${HighMemoryThresholdMb} MB.")
    [void]$reportBuilder.AppendLine()
}
else {
    [void]$reportBuilder.AppendLine("| Scope | Peak Memory (MB) | Threshold (MB) |")
    [void]$reportBuilder.AppendLine("| --- | ---: | ---: |")
    foreach ($item in $highMemoryFindings) {
        [void]$reportBuilder.AppendLine("| $($item.Scope) | $($item.PeakMemoryMb) | $($item.ThresholdMb) |")
    }
    [void]$reportBuilder.AppendLine()
}

Add-MarkdownSection -Builder $reportBuilder -Title "Long Setup Time Candidates"
if ($slowTests.Count -eq 0) {
    [void]$reportBuilder.AppendLine("No long setup time candidates detected from test durations.")
    [void]$reportBuilder.AppendLine()
}
else {
    [void]$reportBuilder.AppendLine("Tests listed in the slow-test section are candidates for long setup/runtime investigation.")
    [void]$reportBuilder.AppendLine()
}

Add-MarkdownSection -Builder $reportBuilder -Title "Hanging Tests"
if ($hangingTests.Count -eq 0) {
    [void]$reportBuilder.AppendLine("No hanging tests detected.")
    [void]$reportBuilder.AppendLine()
}
else {
    [void]$reportBuilder.AppendLine("| Test | Sequence File |")
    [void]$reportBuilder.AppendLine("| --- | --- |")
    foreach ($item in $hangingTests) {
        [void]$reportBuilder.AppendLine("| $($item.Name) | $($item.SequenceFile) |")
    }
    [void]$reportBuilder.AppendLine()
}

if ($hangDetected) {
    Add-MarkdownSection -Builder $reportBuilder -Title "Automatic Root Cause Inspection"
    if ($rootCauseFindings.Count -eq 0) {
        [void]$reportBuilder.AppendLine("No common hang-pattern matches found in current test files.")
        [void]$reportBuilder.AppendLine()
    }
    else {
        [void]$reportBuilder.AppendLine("| Pattern | File | Line | Snippet |")
        [void]$reportBuilder.AppendLine("| --- | --- | ---: | --- |")
        foreach ($finding in $rootCauseFindings) {
            $safeSnippet = $finding.Snippet.Replace("|", "\|")
            [void]$reportBuilder.AppendLine("| $($finding.Pattern) | $($finding.File) | $($finding.Line) | $safeSnippet |")
        }
        [void]$reportBuilder.AppendLine()
    }
}

Set-Content -Path $reportPath -Value $reportBuilder.ToString()

Write-Host ""
Write-Host "Guarded test run completed."
Write-Host "Diagnostics log: $diagLogPath"
Write-Host "Performance report: $reportPath"
Write-Host "Total tests: $totalTests"
Write-Host "Slow tests: $($slowTests.Count)"
Write-Host "Hang detected: $hangDetected"

if ($testExitCode -ne 0) {
    Write-Error "dotnet test failed with exit code $testExitCode."
    exit $testExitCode
}
