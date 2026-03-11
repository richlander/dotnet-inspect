# v0.5.0 Release Notes

49 commits since v0.4.4. Full feature history: `dotnet-inspect --help` or `dotnet-inspect llmstxt`.

## Breaking Changes

- **`-n` is now line limiting** (like `head -n`), not result limiting. Use `-t`/`-m` to limit results by type/member count. (#209)
- **`-t` is now type filter** (was tips verbosity). Tips moved to `-T`. (#209)
- **`api` command deprecated** — split into `type` (discovery, terse) and `member` (inspection, docs on by default). `api` still works with deprecation warning. (#193)
- **`cache --clean`/`--clear` deprecated** — replaced by `cache clear` subcommand. (#213)
- **All emoji and non-ASCII removed** from tool output. (#211)
- **Member verbosity changed** — source hidden at minimal; `-v:q` shows summary tables; default shows full signatures + docs. (#229)

## New Features

### Commands and flags

- `member` command — dedicated member inspection with docs on by default, dotted syntax (`-m Type.Member`), `--no-docs`, `--show-index` (#193)
- `--flavor` flag — report CoreCLR or NativeAOT binary flavor (#220)
- `--oneline` on package/router — purpose-built one-line views (#214)
- `-m` on `type` command — member filter shortcut (#214)
- Fully qualified type names as positional arguments (#207)
- `-n N` / `-N` line limiting on every command — replaces piping through `head` (#205)

### API inspection

- Nullability annotations in signatures (#191)
- Compact minimal verbosity — one row per symbol with overload counts (#210)
- Generic type arity matching — prefer `Option<T>` over `Option` (#223)
- All overloads shown when member filter matches multiple (#228)
- Type forwarder following — resolve forwarded types unconditionally (#235)
- Diff tip shown when viewing package types (#224)

### Package and version

- `@latest` tag — `package Foo@latest` always resolves from network (#200)
- Source field in package output (#202)
- NuGet source credential support (#192)
- Dual-purpose `--version` — show latest or specify version (#192)
- Version pinning validation — error on nonexistent versions (#196)
- Multi-source version cache fix (#195)

### Caching and performance

- Cache-first version resolution — offline-capable default output (#194)
- `--offline` mode via environment variable (#194, #215)
- Cache isolation — `DOTNET_INSPECT_ISOLATED` for named sessions (#215, #216)
- `cache clear` subcommand (#213)
- Metadata cache switched from JSON to markdown field format (#198)
- Skip pack downloads for bare-name resolution (#222)
- Hot path optimizations for version, package, and library queries (#219)
- SDK packs as fallback search path for platform resolution (#204)

### Output and tips

- Tips suppressed when line/result limiting or section selection is used (#203, #217)
- Tips added to platform router path (#206)

### LLM guidance

- SKILL.md rewritten — Quick Decision Tree, Key Patterns (`diff --oneline`, `--shape`), Output Limiting section
- llms.txt reordered — LLM Usage Guide moved to top, Common Mistakes section added
- Consolidated SKILL.md to single file with version front matter
- Lap-around narrative workflow added to getting-started

## Bug Fixes

- Generic types with matching arity preferred in TypeMatcher.Lookup (#223)
- All overloads shown when member filter matches multiple (#228)
- Type forwarders followed unconditionally (#235)
- Version cache fixed for multi-source nuget configs (#195)
- `--oneline` fixed with purpose-built views (#214)
- ConsoleCapture race condition in parallel tests (#208)

## Internal

- Extract SetAction lambdas into dedicated parser classes (#234)
- Extract CommandLineBuilder into focused modules (#233)
- Extract scope resolution to ScopeResolver service (#221)
- Convert commands to Markout serializer pattern (#188)
- Repo skills for workflow operations (#231)
