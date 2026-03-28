param(
    [ValidateSet("Strict")]
    [string]$Mode = "Strict",
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$BaselinePath = (Join-Path $PSScriptRoot "strict-baseline.json")
)

$ErrorActionPreference = "Stop"
Set-Location $RepoRoot

function Write-BlockedMissingContext {
    Write-Output "[BLOCKED]"
    Write-Output ""
    Write-Output "Reason: Missing required context files"
    Write-Output "Action: Read all required documents before proceeding"
    exit 1
}

function Write-BlockedLint {
    param([string]$FailedCheck)

    Write-Output "[BLOCKED]"
    Write-Output ""
    Write-Output "Reason: Lint rule violation"
    Write-Output "Failed Check: $FailedCheck"
    Write-Output "Fix Required: Adjust plan before implementation"
    exit 1
}

function Write-BlockedGeneric {
    param(
        [string]$Reason,
        [string]$Action
    )

    Write-Output "[BLOCKED]"
    Write-Output ""
    Write-Output "Reason: $Reason"
    Write-Output "Action: $Action"
    exit 1
}

function Get-FileText {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return Get-Content -Raw -LiteralPath $Path
}

function Get-LiteralMatchCount {
    param(
        [string]$Content,
        [string]$Literal
    )

    if ([string]::IsNullOrEmpty($Content) -or [string]::IsNullOrEmpty($Literal)) {
        return 0
    }

    return [regex]::Matches(
        $Content,
        [regex]::Escape($Literal),
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    ).Count
}

function Test-AlignmentSpecs {
    param(
        [object[]]$Specs,
        [string]$Category
    )

    $errors = New-Object System.Collections.Generic.List[string]
    foreach ($spec in $Specs) {
        if ($null -eq $spec) {
            $errors.Add("$Category contains an empty spec entry.")
            continue
        }

        $targetPath = Join-Path $RepoRoot $spec.path
        $content = Get-FileText -Path $targetPath
        if ($null -eq $content) {
            $errors.Add("$Category spec '$($spec.id)' is missing file: $($spec.path).")
            continue
        }

        $minimumMatches = if ($null -ne $spec.minimumMatches) {
            [int]$spec.minimumMatches
        } else {
            1
        }

        $matchCount = 0
        foreach ($pattern in $spec.patterns) {
            $matchCount += Get-LiteralMatchCount -Content $content -Literal $pattern
        }

        if ($matchCount -lt $minimumMatches) {
            $errors.Add(
                "$Category spec '$($spec.id)' did not meet minimum match count. Required=$minimumMatches, Found=$matchCount."
            )
        }
    }

    return $errors
}

# Phase 0 - mandatory context load
$requiredContextFiles = @(
    @{ Label = "AGENTS.md"; Path = "AGENTS.md" },
    @{ Label = "project-context.md"; Path = "docs/project-context.md" },
    @{ Label = "architecture.md"; Path = "docs/architecture.md" },
    @{ Label = "security-guardrails.md"; Path = "docs/security-guardrails.md" },
    @{ Label = "domain-model.md"; Path = "docs/domain-model.md" }
)

foreach ($requiredFile in $requiredContextFiles) {
    $absolutePath = Join-Path $RepoRoot $requiredFile.Path
    if (-not (Test-Path -LiteralPath $absolutePath)) {
        Write-BlockedMissingContext
    }

    try {
        Get-Content -Raw -LiteralPath $absolutePath | Out-Null
    }
    catch {
        Write-BlockedMissingContext
    }
}

Write-Output "[CONTEXT LOAD STATUS]"
Write-Output ""
Write-Output "AGENTS.md: LOADED"
Write-Output "project-context.md: LOADED"
Write-Output "architecture.md: LOADED"
Write-Output "security-guardrails.md: LOADED"
Write-Output "domain-model.md: LOADED"
Write-Output ""
Write-Output "STATUS: OK"
Write-Output ""

# Phase 1 - repo audit alignment
if (-not (Test-Path -LiteralPath $BaselinePath)) {
    Write-BlockedGeneric -Reason "Repo audit alignment check failed" -Action "Create scripts/strict-baseline.json before proceeding."
}

$baselineRaw = Get-FileText -Path $BaselinePath
if ([string]::IsNullOrWhiteSpace($baselineRaw)) {
    Write-BlockedGeneric -Reason "Repo audit alignment check failed" -Action "Populate scripts/strict-baseline.json before proceeding."
}

try {
    $baseline = $baselineRaw | ConvertFrom-Json
}
catch {
    Write-BlockedGeneric -Reason "Repo audit alignment check failed" -Action "Fix JSON syntax in scripts/strict-baseline.json."
}

if ($null -eq $baseline.repoAlignment) {
    Write-BlockedGeneric -Reason "Repo audit alignment check failed" -Action "Add repoAlignment section to scripts/strict-baseline.json."
}

$gitCheck = git rev-parse --is-inside-work-tree 2>$null
if ($LASTEXITCODE -ne 0 -or "$gitCheck".Trim() -ne "true") {
    Write-BlockedGeneric -Reason "Repo audit alignment check failed" -Action "Run strict preflight from inside the Git repository."
}

$alignmentErrors = New-Object System.Collections.Generic.List[string]

$securityAlignmentErrors = Test-AlignmentSpecs -Specs $baseline.repoAlignment.securityFindings -Category "securityFindings"
if ($securityAlignmentErrors) {
    foreach ($errorMessage in $securityAlignmentErrors) {
        $alignmentErrors.Add($errorMessage)
    }
}

$saasAlignmentErrors = Test-AlignmentSpecs -Specs $baseline.repoAlignment.saasGaps -Category "saasGaps"
if ($saasAlignmentErrors) {
    foreach ($errorMessage in $saasAlignmentErrors) {
        $alignmentErrors.Add($errorMessage)
    }
}

$hygieneAlignmentErrors = Test-AlignmentSpecs -Specs $baseline.repoAlignment.repoHygiene -Category "repoHygiene"
if ($hygieneAlignmentErrors) {
    foreach ($errorMessage in $hygieneAlignmentErrors) {
        $alignmentErrors.Add($errorMessage)
    }
}

if ($null -eq $baseline.repoAlignment.dependencyState -or
    $null -eq $baseline.repoAlignment.dependencyState.vulnerablePackages -or
    $null -eq $baseline.repoAlignment.dependencyState.outdatedPackagesAcknowledged) {
    $alignmentErrors.Add("dependencyState is incomplete in strict baseline.")
}

if ($null -eq $baseline.repoAlignment.testBuildStatus -or
    [string]::IsNullOrWhiteSpace($baseline.repoAlignment.testBuildStatus.testCommand) -or
    $baseline.repoAlignment.testBuildStatus.baselinePassed -lt 1 -or
    $baseline.repoAlignment.testBuildStatus.baselineFailed -lt 0) {
    $alignmentErrors.Add("testBuildStatus is incomplete in strict baseline.")
}

if ($alignmentErrors.Count -gt 0) {
    Write-BlockedGeneric `
        -Reason "Repo audit alignment check failed" `
        -Action "Update scripts/strict-baseline.json or reconcile repository drift before proceeding."
}

Write-Output "[REPO ALIGNMENT CHECK]"
Write-Output ""
Write-Output "- Repo state understood"
Write-Output "- Security findings acknowledged"
Write-Output "- SaaS gaps acknowledged"
Write-Output "- Test/build status acknowledged"
Write-Output ""
Write-Output "STATUS: OK"
Write-Output ""

# Phase 2 - lint checks
$lintScriptPath = Join-Path $RepoRoot "scripts/strict-lint.ps1"
if (-not (Test-Path -LiteralPath $lintScriptPath)) {
    Write-BlockedLint -FailedCheck "Architecture"
}

$lintResult = & $lintScriptPath -RepoRoot $RepoRoot -BaselinePath $BaselinePath
if ($null -eq $lintResult) {
    Write-BlockedLint -FailedCheck "Architecture"
}

if ($lintResult.Architecture.Status -ne "PASS") {
    Write-BlockedLint -FailedCheck "Architecture"
}

if ($lintResult.Security.Status -ne "PASS") {
    Write-BlockedLint -FailedCheck "Security"
}

if ($lintResult.Concurrency.Status -ne "PASS") {
    Write-BlockedLint -FailedCheck "Concurrency"
}

if ($lintResult.DomainRules.Status -ne "PASS") {
    Write-BlockedLint -FailedCheck "Domain Rules"
}

Write-Output "[LINT CHECK]"
Write-Output ""
Write-Output "Architecture: PASS"
Write-Output "Security: PASS"
Write-Output "Concurrency: PASS"
Write-Output "Domain Rules: PASS"
Write-Output ""
Write-Output "STATUS: OK"
