#!/usr/bin/env bash
# Materializes the vendor/authored-source-corpus orphan branch at
# external/authored-source-corpus.
#
# The branch carries the vendored authored-source correspondence corpus: JSONL
# where each row is a real method identity plus a checksum-verified authored
# member body captured through SourceLink at harvest time (see the branch
# README.md for provenance and licensing policy). The offline benchmark run-mode
# (`decompiler-harness --benchmark-authored-corpus`) consumes it, so no network
# access is required at benchmark time. It lives on an orphan branch so the
# harvested third-party source snapshots never enter main's history.
#
# Because it is a git worktree, edits made under external/authored-source-corpus
# commit directly to the vendor branch.
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
dest="$root/external/authored-source-corpus"
branch="vendor/authored-source-corpus"

if [ -d "$dest" ]; then
    echo "Already restored: $dest"
    exit 0
fi

if ! git -C "$root" rev-parse --verify --quiet "$branch" > /dev/null; then
    echo "Local branch '$branch' not found; fetching from origin..."
    git -C "$root" fetch origin "$branch:$branch"
fi

git -C "$root" worktree add "$dest" "$branch"
echo "Restored $branch at $dest"
