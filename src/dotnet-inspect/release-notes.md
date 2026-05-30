# Release Notes

## v0.8.1

### Skill guidance

- Embedded `dotnet-inspect skill` guidance now includes a compact Modern .NET / preview workflow for runtime async classification, runtime-pack/platform assemblies, extension properties, and implementation-lowering inspection.

## v0.8.0

### Highlights

- `source --il-offset` maps MethodDef token + IL offset pairs to source file locations, with Markdown, oneline, and JSON output.
- `--count` returns a single integer row count when exactly one table section is selected.
- `library -S "Async*"` lists async methods and classifies them as runtime async or classic state-machine async.
- Platform assembly resolution is SemVer/prerelease-aware and resolves runtime-only assemblies such as `System.Private.CoreLib`.
- `type` and `member` discovery now default to effective `-D` output; use `--schema` for the static schema.
- Obsolete members are shown by default with an obsolete marker and message when available.

### Improvements

- `-S`, `--columns`, and `--fields` accept semicolon-separated lists in addition to comma-separated lists.
- Effective discovery and field projection are wired through API type/member routing and markdown output.
- Package-backed `type`, `member`, and related commands preserve package/library context more reliably for multi-library packages.
- NuGet configs containing local folder or `file://` sources no longer block later HTTP feeds during package resolution.
- Assembly public key tokens are computed from the full public key using the ECMA-335 SHA-1 algorithm.

### Documentation

- README.md is now a concise capability inventory.
- SKILL.md is now workflow-oriented for agents: upgrade triage, find-to-member drill-in, source/IL lookup, platform release notes, package/library audit, structured queries, and relationship exploration.
