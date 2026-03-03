# Feature History

> Comprehensive list of dotnet-inspect features organized by commit history. Use this to understand when features were added and find relevant commits for investigation.

**Legend:**
- ✓ Active feature
- ⚠️ Deprecated (still works, will be removed)
- ✗ Removed

## Commands

| Feature | Commit | Version | Status | Description |
|---------|--------|---------|--------|-------------|
| `dotnet-inspect` CLI | 6b773da | 0.1.0 | ✓ | Initial tool creation |
| `api` command | de505ec | 0.1.0 | ⚠️ | API surface extraction (deprecated in 0.5.0, use `type`/`member`) |
| `llmstxt` command | aaa289e | 0.1.0 | ✓ | Usage examples optimized for LLM consumption |
| `type` command | 208ebd2 | 0.1.3 | ✓ | Discover types (terse output, split from `api` in 0.5.0) |
| `diff` command | 0d3d24c | 0.1.7 | ✓ | Compare API surfaces between package versions |
| `samples` command | 916c845 | 0.1.8 | ✓ | Show sample code references via SourceLink |
| `platform` command | e516f96 | 0.2.0 | ✗ | Inspect platform assemblies (removed in 0.4.0, use `--platform` flag) |
| `find` command | ec02273 | 0.2.x | ✓ | Search for types across packages and assemblies |
| `extensions` command | 8f48d5e | 0.2.x | ✓ | Find extension methods for a type |
| `implements` command | 7a59766 | 0.1.x | ✓ | Find types implementing an interface |
| `cli` command | b2bb428 | 0.2.x | ✓ | Display CLI structure as API listing (now `--help -v`) |
| `skill` command | 385ba19 | 0.2.x | ✓ | Print SKILL.md for Claude Code |
| `demo` command | c378030 | 0.4.0 | ✓ | Run curated demo queries |
| `depends` command | c378030 | 0.4.0 | ✓ | Walk type dependency graphs upward |
| `member` command | 14f93c9 | 0.5.0 | ✓ | Inspect type members (split from `api`) |
| `package search` | — | 0.2.x | ✓ | Search NuGet for packages by keyword |

## Output Control

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| `-n` line limit | 826c64a | 0.1.0 | Limit output lines |
| `-v` verbosity levels | 826c64a | 0.1.0 | quiet/minimal/normal/detailed |
| `-s` section filtering | aaa289e | 0.1.0 | Include sections by name (glob-capable) |
| `-x` section exclusion | 9a9f154 | 0.2.x | Exclude sections by name |
| `--compact` JSON | e7297bb | 0.1.0 | Minified JSON output |
| `--json` output | e7297bb | 0.1.0 | JSON output format |
| `--oneline` output | — | 0.1.x | One result per line, columnar |
| `--no-header` | — | 0.2.x | Suppress column headers |
| `--signatures-only` | d718525 | 0.1.3 | Minimal output with signatures only |
| `--out` file output | 627a0a0 | 0.2.x | Write output to file |
| Tips system | 935a39b | 0.2.x | Contextual suggestions on stderr |
| `-T` tips verbosity | 0c679e4 | 0.2.x | Control tip output level |
| Redesigned tips UI | 381efa2 | 0.3.x | Single header with aligned entries |

## Type and Member Inspection

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| Member filter (`-m`) | 3dbf984 | 0.1.0 | Filter to specific members |
| `--docs` flag | 608cd77 | 0.1.0 | Include XML documentation |
| `--interfaces` flag | 6cb5a2e | 0.1.0 | Show implemented interfaces |
| `--ctor` flag | ea8d81d | 0.1.6 | Constructor details |
| `--shape` flag | 208ebd2 | 0.1.3 | Type shape diagram |
| `--unsafe` filter | 4b9ea52 | 0.1.6 | Filter to unsafe signatures |
| `--all` flag | 447d003 | 0.1.1 | Include hidden/obsolete members |
| C#-style generic syntax | ea8d81d | 0.1.6 | Use `List<T>` instead of backtick notation |
| Property mutability | 792319d | 0.1.7 | Show `{ get; set; }` vs `{ get; }` |
| Parameter names in signatures | 7d2021d | 0.1.3 | Include param names in method signatures |
| Enum values display | 217f15d | 0.2.x | Show enum member values |
| `--select` flag | fe320a2 | 0.3.x | Show Name:N addressing hints |
| `--index` option | d79f787 | 0.3.x | IL method body display |
| `--params` option | b8bcb4d | 0.3.x | Overload selection by parameter types |
| `-of` option | b8bcb4d | 0.3.x | Overload selection by return type |
| Name:N shorthand | bac332f | 0.3.1 | Target specific overload by index |
| C# source via SourceLink | b033660 | 0.3.x | Decompiled source in --index view |
| Custom Attributes section | 6bf2d17 | 0.3.x | Show type/member attributes |
| Nullability annotations | 596f75d | 0.5.0 | Show nullable reference types in signatures |
| Dotted syntax (`Type.Member`) | 14f93c9 | 0.5.0 | `-m JsonSerializer.Deserialize` |
| `--no-docs` flag | 14f93c9 | 0.5.0 | Suppress XML documentation |
| `-t` type filter | 6ae1c1a | 0.5.0 | Filter types by glob pattern |

## Package Inspection

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| Package caching | f3a7beb | 0.1.0 | Local cache for downloaded packages |
| `--tfm` flag | f3a7beb | 0.1.0 | Select target framework |
| `@version` syntax | f3a7beb | 0.1.0 | Inline version specification |
| `--versions` flag | — | 0.1.x | List available versions |
| `--version` flag | 5a1d7f9 | 0.1.x | Show/specify version |
| `--files` flag | e72ec62 | 0.2.x | List files in package |
| `--layout` flag | e72ec62 | 0.2.x | Tree view of package files |
| `--lib` scoping | 119f5d2 | 0.2.x | Filter to lib/ folder |
| `--tools` scoping | 119f5d2 | 0.2.x | Filter to tools/ folder |
| `--dependencies` flag | 9b7bc26 | 0.2.x | Package dependency tree |
| `--readme` flag | 627a0a0 | 0.2.x | Show README.md content |
| `--tfms` flag | — | 0.2.x | List target frameworks |
| Statistics section | 9a611a2 | 0.2.x | Package statistics |
| `--prerelease` flag | — | 0.2.x | Include prerelease versions |
| Wildcard version patterns | e2b58a9 | 0.3.x | e.g., `8.0.*`, `11.0.0-preview*` |
| `@latest` tag | a5e88fe | 0.5.0 | Resolve to latest version |
| Source field | 31475b0 | 0.5.0 | Show package source in output |

## Library Inspection

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| Native AOT detection | 1ff8b75 | 0.1.0 | Detect AOT vs CoreCLR binaries |
| RID-specific support | d234f64 | 0.1.0 | Handle runtime-specific packages |
| Symbol package support | 9ad5c92 | 0.1.7 | Download and inspect symbol packages |
| Windows PDB detection | 9ad5c92 | 0.1.7 | Identify Windows PDB format |
| `--source-link-audit` | 9549c7a | 0.1.7 | Full SourceLink verification |
| `--references` flag | 09c4473 | 0.1.x | Show library references |
| `--transitive` flag | 09c4473 | 0.1.x | Show transitive references |
| Extension Methods section | 7ee18e9 | 0.2.x | List extension methods in library |
| Unsafe Methods section | 8ab7a7d | 0.2.x | List unsafe methods |
| P/Invoke Methods section | 8ab7a7d | 0.2.x | List P/Invoke methods |
| Type Forwarders section | 881706e | 0.2.x | Show type forwarding |
| Resources section | 881706e | 0.2.x | List embedded resources |
| `--extract-resources` | — | 0.3.x | Extract embedded resources to directory |
| `--dependencies` flag | 9b7bc26 | 0.2.x | Library dependency tree |
| Builder field | b683bbd | 0.2.x | Microsoft-branded assembly detection |
| Product/Company/Copyright | 1c34cf6 | 0.2.x | Assembly metadata fields |

## Platform Libraries

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| `--platform` flag | e516f96 | 0.2.x | Inspect platform assemblies |
| Platform ref packs | af9b7be | 0.3.x | Download ref packs from NuGet |
| `@version` for platform | 416afdc | 0.3.x | Pin platform library version |
| Platform routing | 41defca | 0.3.x | Auto-detect platform assemblies |
| SDK packs fallback | 10afb58 | 0.5.0 | Use SDK packs as search path |
| `--framework` flag | 9d2c939 | 0.3.x | Select runtime/aspnetcore/netstandard |

## Search and Discovery

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| `--filter` option | 447d003 | 0.1.1 | Filter results by pattern |
| `--project` scope | acef0e1 | 0.2.x | Search project dependencies |
| `--bin` scope | acef0e1 | 0.2.x | Search build output directory |
| `--extensions` scope | 55f2ecf | 0.2.x | Search Microsoft.Extensions.* packages |
| `--aspnetcore` scope | — | 0.2.x | Search Microsoft.AspNetCore.* packages |
| `--package-prefix` | — | 0.2.x | Search packages by ID prefix |
| `--reachable` flag | b635e4b | 0.2.x | Include reachable type extensions |
| `--hierarchy` flag | a98eb01 | 0.2.x | Show type hierarchy |
| `--dotnet` scope | b9c3263 | 0.3.x | Default platform + extensions scope |
| Fully qualified names | 8b1a64d | 0.5.0 | Use `System.Text.Json.JsonSerializer` as positional |

## Samples and Source

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| Sample linking | b6026dc | 0.1.8 | Link to code samples via XML docs |
| `--print` option | 177d867 | 0.1.8 | Print specific sample by number |
| Parallel batch fetching | 7f41248 | 0.1.8 | Optimize sample retrieval |
| Persistent disk cache | aac88cc | 0.1.8 | Cache source content |
| `--file` option | 1186bd7 | 0.4.4 | Read local .cs file directly |
| `--region` option | 1186bd7 | 0.4.4 | Extract specific #region |
| `--browsable-urls` | — | 0.2.x | Use /blob/ URLs for browser viewing |
| `--list` option | — | 0.2.x | List samples without fetching |

## NuGet Sources

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| `--source` flag | 7525d35 | 0.1.x | Custom NuGet source URL |
| `--add-source` flag | 7525d35 | 0.1.x | Add NuGet source |
| `--nugetconfig` flag | 7525d35 | 0.1.x | Path to nuget.config |
| Credential support | 759dcd5 | 0.5.0 | NuGet source authentication |

## Diff and Comparison

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| Version range syntax | 0d3d24c | 0.1.7 | `@8.0.0..9.0.0` |
| `--breaking` flag | — | 0.2.x | Show only breaking changes |
| `--additive` flag | — | 0.2.x | Show only additive changes |
| `--name-only` flag | — | 0.2.x | Show only type names |
| Platform diff | 623619f | 0.2.x | Compare platform versions |
| `-t` type filter | — | 0.3.x | Filter diff to specific types |

## Caching and Performance

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| Package caching | f3a7beb | 0.1.0 | Local package cache |
| Source content cache | aac88cc | 0.1.8 | Persistent disk cache |
| Version cache | 8ec280f | 0.3.x | 1-hour TTL for version resolution |
| SourceLink audit cache | 536c607 | 0.3.x | Cache audit results |
| HTTP retry logic | 71b9368 | 0.2.x | Exponential backoff |
| Concurrency limiting | f91e9a3 | 0.3.x | 16 parallel requests max |
| Cache-first resolution | 1e3e352 | 0.5.0 | Check cache before network |
| `--offline` mode | 1e3e352 | 0.5.0 | Disable network access |
| `cache clear` command | — | 0.2.x | Clear local cache |

## IL and Decompilation

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| IL disassembler | 5aae39f | 0.3.x | Foundation for IL analysis |
| IL method body display | d79f787 | 0.3.x | Show IL in --index view |
| Decompiler foundation | dde4d16 | 0.3.x | DotnetInspector.Decompiler library |
| Lowered C# section | b033660 | 0.3.x | Decompiled C# faithful to IL |
| IL (Annotated) section | — | 0.3.x | IL with stack state annotations |

## Demo and Documentation

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| `demo` command | c378030 | 0.4.0 | Curated demo queries |
| `demo list` | c378030 | 0.4.0 | List all demos |
| `--feeling-lucky` | 07d1037 | 0.4.1 | Random demo selection |
| 16 curated demos | 59e4487 | 0.4.2 | Expanded demo coverage |

## Plugin and Integration

| Feature | Commit | Version | Description |
|---------|--------|---------|-------------|
| Copilot plugin | f5bcf7f | 0.1.8 | GitHub Copilot integration |
| Claude plugin | f5bcf7f | 0.1.8 | Claude Code integration |
| `skill` command | 385ba19 | 0.2.x | Print skill definition |
| Native AOT support | 5c74387 | 0.1.8 | PublishAot=true |

## Deprecated and Removed Features

### `api` Command (Deprecated in 0.5.0)

The `api` command was the original all-in-one command for type and member inspection. In 0.5.0, it was split into two focused commands:

| Original | Replacement | Reason |
|----------|-------------|--------|
| `api --package Foo` | `type --package Foo` | Type discovery (terse, no docs) |
| `api --package Foo Bar` | `member Bar --package Foo` | Member inspection (docs on by default) |
| `api --package Foo Bar -m Baz` | `member Bar --package Foo -m Baz` | Member filtering |

The `api` command still works but shows a deprecation warning.

### `platform` Command (Removed in 0.4.0)

The standalone `platform` command was removed in favor of the `--platform` flag on other commands:

| Original | Replacement |
|----------|-------------|
| `platform System.Text.Json` | `type --platform System.Text.Json` |
| `platform list` | (use `--platform` flag with search commands) |

Commit: af5ecd0 "Simplify scope flags and remove platform command (#180)"

### `--discover` Flag (Replaced in 0.2.x)

The `--discover` flag for section discovery was replaced with bare `-s`:

| Original | Replacement |
|----------|-------------|
| `--discover` | `-s` (alone, no value) |

Commit: 8d12a0b "Replace --discover with bare -s for section discovery (#100)"

### `--fields-only` Flag (Removed)

The `--fields-only` flag was replaced by section filtering:

| Original | Replacement |
|----------|-------------|
| `--fields-only` | `-x:Methods,Properties` |

### Flag Renames

| Original | New | Version | Commit |
|----------|-----|---------|--------|
| `-t` (tips) | `-T` | 0.2.x | 0c679e4 |
| (none) | `-t` (type filter) | 0.5.0 | 6ae1c1a |

## Version History

| Version | Date | Key Changes |
|---------|------|-------------|
| 0.1.0 | — | Initial release with `api`, `package`, `library` commands |
| 0.1.3 | — | `type` command, `--signatures-only`, parameter names |
| 0.1.6 | — | `--unsafe`, `--ctor`, C#-style generic syntax |
| 0.1.7 | — | `diff` command, symbol package support |
| 0.1.8 | — | `samples` command, Copilot/Claude plugins, Native AOT |
| 0.2.x | — | `find`, `extensions`, `platform`, tips system, section pipeline |
| 0.3.x | — | IL disassembly, `--select`, `--params`, platform ref packs, caching |
| 0.3.1 | — | Name:N shorthand for member targeting |
| 0.4.0 | — | `demo`, `depends` commands, removed `platform` command |
| 0.5.0 | — | Split `api` into `type`/`member`, nullability, `@latest`, `-n`/`-t`/`-m` redesign |
