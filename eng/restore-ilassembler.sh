#!/usr/bin/env bash
# Materializes the vendor/ilassembler orphan branch at external/ILAssembler.
#
# The branch carries a fork of the managed ILAssembler from dotnet/runtime
# (see its README.md for provenance and policy). The IL round-trip tests
# reference it via ProjectReference. Because it is a git worktree, edits made
# under external/ILAssembler commit directly to the vendor branch.
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
dest="$root/external/ILAssembler"
branch="vendor/ilassembler"

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
