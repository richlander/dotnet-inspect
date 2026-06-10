# Vendored: managed ILAssembler from dotnet/runtime

This orphan branch (`vendor/ilassembler`) carries a fork of the managed IL
assembler from [dotnet/runtime](https://github.com/dotnet/runtime)
(`src/tools/ilasm/src/ILAssembler`). It exists because the tool is not yet
shipped as a binary or NuGet package, and `dotnet-inspect` uses its
`DocumentCompiler` as an in-memory baseline for IL round-trip tests
(disassemble → reassemble → compare).

The branch is intentionally disconnected from `main` history so the ~32k lines
of vendored and generated code never appear in the product history.

## Provenance

- Upstream: `https://github.com/dotnet/runtime`
- Path: `src/tools/ilasm/src/ILAssembler`
- Imported at: `24547a76ba95bee359f4c0b58dd98976973aa797` (main, 2026-06-09)
- License: MIT (.NET Foundation) — see `LICENSE.TXT` and the per-file headers

## Branch policy

The first commit is a **verbatim import** at the SHA above. Every change after
it is one logical commit, so the branch log doubles as the upstream
decomposition plan: `git log --oneline <import>..HEAD` lists the candidate
upstream PRs, and `git diff <import>..HEAD` is the full fork delta.

Do not mix unrelated changes in one commit. Build-glue commits (csproj/TFM
adjustments that only make sense in this repo) are prefixed `glue:` to mark
them as not-for-upstream.

## Consuming from main

`eng/restore-ilassembler.sh` on `main` materializes this branch at
`external/ILAssembler` (gitignored) via `git worktree add`. Edits made there
commit directly to this branch.

## Syncing with upstream

1. `git -C ~/git/runtime pull` and note the new SHA.
2. On this branch: delete `ILAssembler/`, re-copy from upstream, commit as
   `Import ILAssembler from dotnet/runtime @ <sha>` (verbatim only).
3. Cherry-pick the previous fork commits on top, dropping any that upstream
   has absorbed.

## Grammar changes

`gen/` contains the checked-in ANTLR-generated parser. To change `CIL.g4`,
regenerate via `gen/ilasm-generator.csproj` (uses Antlr4BuildTasks) and commit
the grammar and regenerated files together.
