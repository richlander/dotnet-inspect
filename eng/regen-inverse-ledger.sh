#!/usr/bin/env bash
set -euo pipefail

# Find the repository root
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Run DecompilerHarness to regenerate the ledger
dotnet run --project "$REPO_ROOT/tools/DecompilerHarness" -c Release -- \
    --emit-inverse-ledger "$REPO_ROOT/docs/design/inverse-ledger.generated.md"

echo "Ledger regenerated at docs/design/inverse-ledger.generated.md"
