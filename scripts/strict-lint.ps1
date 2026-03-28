param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$BaselinePath = (Join-Path $PSScriptRoot "strict-baseline.json")
)

$ErrorActionPreference = "Stop"
Set-Location $RepoRoot

function Add-Failure {
    param(
        [hashtable]$Section,
        [string]$Message
    )

    $Section.Failures.Add($Message)
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

function Find-HttpPostWithoutAntiForgery {
    param([string]$ControllerPath)

    $violations = New-Object System.Collections.Generic.List[string]
    $lines = Get-Content -LiteralPath $ControllerPath
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        if ($line -notmatch '\[HttpPost\b') {
            continue
        }

        $hasAntiForgery = $line -match '\[ValidateAntiForgeryToken\]'
        for ($scan = $index + 1; $scan -lt [Math]::Min($lines.Count, $index + 8) -and -not $hasAntiForgery; $scan++) {
            if ($lines[$scan] -match '\[ValidateAntiForgeryToken\]') {
                $hasAntiForgery = $true
                break
            }

            if ($lines[$scan] -match '^\s*public\s+') {
                break
            }
        }

        if (-not $hasAntiForgery) {
            $violations.Add("${ControllerPath}:$($index + 1)")
        }
    }

    return $violations
}

function New-SectionResult {
    param([string]$Name)

    return @{
        Name = $Name
        Status = "PASS"
        Failures = (New-Object System.Collections.Generic.List[string])
    }
}

$architecture = New-SectionResult "Architecture"
$security = New-SectionResult "Security"
$concurrency = New-SectionResult "Concurrency"
$domain = New-SectionResult "Domain Rules"

# Architecture checks
$controllerFiles = Get-ChildItem -Path (Join-Path $RepoRoot "Controllers") -Filter *.cs -File
foreach ($controller in $controllerFiles) {
    $controllerText = Get-FileText -Path $controller.FullName
    if ($null -eq $controllerText) {
        Add-Failure -Section $architecture -Message "Unable to read controller file: $($controller.Name)."
        continue
    }

    if ($controllerText -match 'using\s+Vizora\.Data;' -or
        $controllerText -match '\bApplicationDbContext\b' -or
        $controllerText -match '\bDbContext\b' -or
        $controllerText -match '_context\.') {
        Add-Failure -Section $architecture -Message "Controller data-access violation: $($controller.Name) uses DbContext/data-layer patterns."
    }
}

$serviceBoundaryControllers = @(
    "AuthController.cs",
    "BudgetsController.cs",
    "CategoriesController.cs",
    "DashboardController.cs",
    "ReportsController.cs",
    "TransactionsController.cs"
)

foreach ($controllerName in $serviceBoundaryControllers) {
    $path = Join-Path $RepoRoot "Controllers/$controllerName"
    $content = Get-FileText -Path $path
    if ($null -eq $content) {
        Add-Failure -Section $architecture -Message "Missing expected controller: $controllerName."
        continue
    }

    if ($content -notmatch 'private\s+readonly\s+I[A-Za-z0-9_]+Service') {
        Add-Failure -Section $architecture -Message "Controller does not appear to use service-layer dependencies: $controllerName."
    }
}

# Security checks
$financeControllers = @(
    "BudgetsController.cs",
    "CategoriesController.cs",
    "DashboardController.cs",
    "ReportsController.cs",
    "SettingsController.cs",
    "TransactionsController.cs"
)

foreach ($controllerName in $financeControllers) {
    $path = Join-Path $RepoRoot "Controllers/$controllerName"
    $content = Get-FileText -Path $path
    if ($null -eq $content) {
        Add-Failure -Section $security -Message "Missing finance controller: $controllerName."
        continue
    }

    if ($content -notmatch '\[Authorize\]\s*public\s+class') {
        Add-Failure -Section $security -Message "Finance controller is missing [Authorize] class enforcement: $controllerName."
    }
}

foreach ($controller in $controllerFiles) {
    $violations = Find-HttpPostWithoutAntiForgery -ControllerPath $controller.FullName
    foreach ($violation in $violations) {
        Add-Failure -Section $security -Message "Missing [ValidateAntiForgeryToken] for [HttpPost]: $violation."
    }
}

$userScopedServiceFiles = @(
    "Services/TransactionService.cs",
    "Services/CategoryService.cs",
    "Services/BudgetService.cs",
    "Services/RecurringTransactionService.cs",
    "Services/TransactionImportService.cs",
    "Services/TransactionReportService.cs",
    "Services/FinanceAnalyticsService.cs",
    "Services/FinancialInsightsService.cs",
    "Services/AuditService.cs"
)

foreach ($servicePath in $userScopedServiceFiles) {
    $fullPath = Join-Path $RepoRoot $servicePath
    $content = Get-FileText -Path $fullPath
    if ($null -eq $content) {
        Add-Failure -Section $security -Message "Missing expected service file: $servicePath."
        continue
    }

    if ($content -notmatch 'GetRequiredUserId\s*\(') {
        Add-Failure -Section $security -Message "Missing required user-context enforcement in $servicePath."
    }
}

$sensitiveLogMatches = Select-String -Path (Join-Path $RepoRoot "Controllers/*.cs"), (Join-Path $RepoRoot "Services/*.cs") `
    -Pattern 'Log\w+\(.*(password|token|connectionstring)' -CaseSensitive:$false -ErrorAction SilentlyContinue
if ($sensitiveLogMatches) {
    foreach ($match in $sensitiveLogMatches) {
        Add-Failure -Section $security -Message "Potential sensitive logging pattern: $($match.Path):$($match.LineNumber)."
    }
}

# Concurrency checks
$rowVersionModels = @(
    "Models/Transaction.cs",
    "Models/Category.cs",
    "Models/Budget.cs",
    "Models/RecurringTransaction.cs"
)

foreach ($modelPath in $rowVersionModels) {
    $fullPath = Join-Path $RepoRoot $modelPath
    $content = Get-FileText -Path $fullPath
    if ($null -eq $content) {
        Add-Failure -Section $concurrency -Message "Missing expected model file: $modelPath."
        continue
    }

    if ($content -notmatch '\[Timestamp\]' -or $content -notmatch 'RowVersion') {
        Add-Failure -Section $concurrency -Message "Missing row-version concurrency token in $modelPath."
    }
}

$serviceConcurrencyRequirements = @{
    "Services/TransactionService.cs" = @("DbUpdateConcurrencyException", "OriginalValue = transaction.RowVersion")
    "Services/CategoryService.cs" = @("DbUpdateConcurrencyException", "OriginalValue = category.RowVersion")
    "Services/BudgetService.cs" = @("DbUpdateConcurrencyException", "OriginalValue = request.RowVersion")
    "Services/RecurringTransactionService.cs" = @("DbUpdateConcurrencyException", "OriginalValue = recurringTransaction.RowVersion")
}

foreach ($entry in $serviceConcurrencyRequirements.GetEnumerator()) {
    $fullPath = Join-Path $RepoRoot $entry.Key
    $content = Get-FileText -Path $fullPath
    if ($null -eq $content) {
        Add-Failure -Section $concurrency -Message "Missing expected service file: $($entry.Key)."
        continue
    }

    foreach ($requiredPattern in $entry.Value) {
        if ($content -notmatch [regex]::Escape($requiredPattern)) {
            Add-Failure -Section $concurrency -Message "Missing concurrency safeguard '$requiredPattern' in $($entry.Key)."
        }
    }
}

$conflictControllers = @(
    "Controllers/TransactionsController.cs",
    "Controllers/CategoriesController.cs",
    "Controllers/BudgetsController.cs"
)

foreach ($controllerPath in $conflictControllers) {
    $fullPath = Join-Path $RepoRoot $controllerPath
    $content = Get-FileText -Path $fullPath
    if ($null -eq $content) {
        Add-Failure -Section $concurrency -Message "Missing expected controller file: $controllerPath."
        continue
    }

    if ($content -notmatch 'ModalUiState\.Conflict') {
        Add-Failure -Section $concurrency -Message "Missing explicit stale-data conflict handling in $controllerPath."
    }
}

# Domain checks
$forbiddenTerms = @("Product", "Products", "Order", "Orders", "OrderItem", "OrderItems", "Sales", "Inventory", "Stock", "SKU")
$scanRoots = @("Controllers", "Models", "Services", "Views", "Data", "Migrations")
$allowSqlByRegex = '(?i)\b(ORDER|GROUP|PARTITION)\s+BY\b'

foreach ($scanRoot in $scanRoots) {
    $rootPath = Join-Path $RepoRoot $scanRoot
    if (-not (Test-Path -LiteralPath $rootPath)) {
        Add-Failure -Section $domain -Message "Missing expected scan root: $scanRoot."
        continue
    }

    $files = Get-ChildItem -Path $rootPath -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @(".cs", ".cshtml", ".razor", ".html") }

    foreach ($file in $files) {
        $matches = Select-String -Path $file.FullName -Pattern '\b(Product|Products|Order|Orders|OrderItem|OrderItems|Sales|Inventory|Stock|SKU)\b' -CaseSensitive:$false -ErrorAction SilentlyContinue
        foreach ($match in $matches) {
            if ($match.Line -match $allowSqlByRegex) {
                continue
            }

            Add-Failure -Section $domain -Message "Forbidden domain term '$($match.Matches[0].Value)' found at $($match.Path):$($match.LineNumber)."
        }
    }
}

$validationRequirements = @{
    "Models/TransactionUpsertViewModel.cs" = @("[Required]", "[Range(typeof(decimal), ""0.01"", ""999999999"")]")
    "Models/CategoryUpsertViewModel.cs" = @("[Required]", "[StringLength(100)]")
    "Models/BudgetUpsertViewModel.cs" = @("[Required]", "[Range(typeof(decimal), ""0.01"", ""999999999"")]")
    "Services/TransactionService.cs" = @("Amount must be greater than 0 and within supported limits.")
    "Services/BudgetService.cs" = @("Planned amount must be greater than 0 and within supported limits.")
    "Services/CategoryService.cs" = @("A category with this name and type already exists.")
}

foreach ($entry in $validationRequirements.GetEnumerator()) {
    $fullPath = Join-Path $RepoRoot $entry.Key
    $content = Get-FileText -Path $fullPath
    if ($null -eq $content) {
        Add-Failure -Section $domain -Message "Missing validation source file: $($entry.Key)."
        continue
    }

    foreach ($requiredLiteral in $entry.Value) {
        if ((Get-LiteralMatchCount -Content $content -Literal $requiredLiteral) -lt 1) {
            Add-Failure -Section $domain -Message "Expected validation pattern not found in $($entry.Key): $requiredLiteral"
        }
    }
}

foreach ($section in @($architecture, $security, $concurrency, $domain)) {
    if ($section.Failures.Count -gt 0) {
        $section.Status = "FAIL"
    }
}

$overallStatus = if (
    $architecture.Status -eq "PASS" -and
    $security.Status -eq "PASS" -and
    $concurrency.Status -eq "PASS" -and
    $domain.Status -eq "PASS"
) {
    "PASS"
} else {
    "FAIL"
}

[pscustomobject]@{
    Architecture = [pscustomobject]@{
        Status = $architecture.Status
        Failures = $architecture.Failures
    }
    Security = [pscustomobject]@{
        Status = $security.Status
        Failures = $security.Failures
    }
    Concurrency = [pscustomobject]@{
        Status = $concurrency.Status
        Failures = $concurrency.Failures
    }
    DomainRules = [pscustomobject]@{
        Status = $domain.Status
        Failures = $domain.Failures
    }
    OverallStatus = $overallStatus
}
