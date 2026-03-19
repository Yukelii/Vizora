#!/usr/bin/env bash
set -euo pipefail

readonly FORBIDDEN_REGEX='\b(Product|Products|Order|Orders|OrderItem|OrderItems|Sales|Inventory|Stock|SKU)\b'
readonly GREP_FALLBACK_REGEX='(^|[^[:alnum:]_])(Product|Products|Order|Orders|OrderItem|OrderItems|Sales|Inventory|Stock|SKU)([^[:alnum:]_]|$)'
readonly SQL_SYNTAX_ALLOWLIST_REGEX='(^|[^[:alnum:]_])(ORDER[[:space:]]+BY|GROUP[[:space:]]+BY|PARTITION[[:space:]]+BY)([^[:alnum:]_]|$)'
readonly SCAN_DIRS=("Controllers" "Models" "Services" "Data")
readonly RG_EXCLUDE_GLOBS=(
    "--glob" "!**/*.sql"
    "--glob" "!**/*.cshtml"
    "--glob" "!**/*.razor"
    "--glob" "!**/*.html"
    "--glob" "!**/*.css"
    "--glob" "!**/*.js"
    "--glob" "!**/*.ts"
    "--glob" "!**/*.jsx"
    "--glob" "!**/*.tsx"
)
readonly GREP_EXCLUDES=(
    "--exclude=*.sql"
    "--exclude=*.cshtml"
    "--exclude=*.razor"
    "--exclude=*.html"
    "--exclude=*.css"
    "--exclude=*.js"
    "--exclude=*.ts"
    "--exclude=*.jsx"
    "--exclude=*.tsx"
)

for dir in "${SCAN_DIRS[@]}"; do
    if [[ ! -d "$dir" ]]; then
        echo "Domain guard configuration error: directory '$dir' was not found."
        exit 2
    fi
done

matches=""
if command -v rg >/dev/null 2>&1; then
    matches="$(rg -n -i --no-heading -e "$FORBIDDEN_REGEX" "${RG_EXCLUDE_GLOBS[@]}" "${SCAN_DIRS[@]}" || true)"
else
    matches="$(grep -R -n -i -E "$GREP_FALLBACK_REGEX" "${GREP_EXCLUDES[@]}" "${SCAN_DIRS[@]}" || true)"
fi

matches="$(printf '%s\n' "$matches" | grep -Evi "$SQL_SYNTAX_ALLOWLIST_REGEX" || true)"

if [[ -n "$matches" ]]; then
    cat <<'EOF'
Domain Guard Violation Detected

Vizora is a finance platform and must not introduce sales-domain concepts.
Forbidden term(s) were detected in source directories.

See docs/domain-model.md and docs/security-guardrails.md.
EOF
    echo
    echo "$matches"
    exit 1
fi

echo "Domain guard passed: no forbidden sales-domain terms found."
