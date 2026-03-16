# Usability Improvements

Findings from observing LLM sessions using dotnet-inspect to migrate a project from System.CommandLine v1 (beta) to v2. Two separate sessions were recorded, both attempting the same task. The tool was effective but several friction points caused unnecessary round-trips and reasoning spirals.

## Context

People are building skills on top of dotnet-inspect (e.g., [davidfowl/dotnet-skillz](https://github.com/davidfowl/dotnet-skillz), [richlander/dotnet-skills](https://github.com/richlander/dotnet-skills)). Changes must be backward-compatible — existing skill docs and invocation patterns should continue to work. Where new behavior is added, it should be additive (new options, new defaults for omitted arguments) rather than changing the meaning of existing flags.

LLMs will always thrash a bit — they try things, get errors, and adapt. The tool should be resilient to this by accepting reasonable inputs gracefully and guiding users toward better queries via tips on stderr.

## Issues Found

### 1. Missing `params` and default values in signatures (Critical)

**Impact:** Caused a 250+ line reasoning spiral in one session. The LLM could not determine from `void .ctor(string name, string[] aliases)` whether it could call `new Option<bool>("--verbose")` without the second parameter.

**What happened:**
- LLM saw `void .ctor(string name, string[] aliases)` from `type` command
- Spent 150+ lines debating whether `aliases` is `params`, has a default, or is required
- Eventually guessed and wrote speculative code
- User interrupted before code was written

**What signatures should show:**

```csharp
void .ctor(string name, params string[] aliases)
void Parse(IReadOnlyList<string> args, ParserConfiguration? configuration = null)
```

The .NET metadata has this information:
- `params` → `System.ParamArrayAttribute` custom attribute on the parameter
- Default values → `ParameterAttributes.HasDefault` flag + constant value from metadata
- Optional → `ParameterAttributes.Optional` flag

This is the single highest-impact improvement. It directly enables correct code generation for constructors and method calls.

### 2. ~~`--terse` referenced in docs but doesn't exist~~ (FIXED)

**Resolution:** Replaced `--terse`, `--grouped`, `--name-only`, `--signatures-only`, and `--stat` with a unified `--oneline` flag across `api`, `find`, `diff`, and `implements` commands. Uses `OneLineWriter` (a `MarkoutWriter` subclass) for docker-style columnar output. Added `--no-header` to suppress column headers.

### 3. `diff` type filter uses exact match, not globs

**Impact:** `diff "*" --package Pkg@v1..v2` silently returns "0 types changed" because `*` is matched literally against type names.

The `find` command supports glob patterns. The `api` command's `--filter` supports globs. But `diff`'s `-t` type filter (and the positional type argument) does exact string matching. An LLM that learns glob patterns from `find` naturally tries `*` with `diff`.

**Current behavior:**

```bash
diff "*" --package Markout@0.1.8..0.2.0    # → "0 types changed" (silently wrong)
diff --package Markout@0.1.8..0.2.0         # → shows all changes (correct)
```

**Fix:** Support glob matching in the diff type filter, consistent with `find` and `api --filter`.

### 4. `diff` output truncates at 10 members

Lines 338-345 in `DiffCommand.cs` cap added/removed members at 10, then show "... and N more." For an LLM trying to understand the full change set for a migration, this truncation forces additional round-trips to inspect individual types.

**Fix:** Remove the truncation limit, or make it configurable (e.g., respect `-n`), or only truncate at lower verbosity levels.

### 5. `-s` (section include) crashes

`dotnet-inspect System.Text.Json -s :1` throws `InvalidOperationException: Cannot set both IncludeSections and ExcludeSections` because `GetExcludeSections()` always returns a non-null set from verbosity mapping, even when `-s` is specified.

**Fix:** Return `null` from `GetExcludeSections()` when `options.IncludeSections` is set.

### 6. Too many round-trips for migration workflows

Both sessions required 12-15 tool calls to understand the System.CommandLine v1→v2 migration. The typical flow was:

1. `diff Command --package ...` — see what changed (1 type)
2. `find "*Handler*,*Option*,*Argument*"` — discover types
3. `type Option<T>` — see new constructor shape
4. `type Argument<T>` — same
5. `type RootCommand` — same
6. `type Command` — same
7. `type ParseResult` — figure out invocation
8. `find "*Invoke*"` — search for invocation patterns
9. `type CommandLineParser` — more exploration
10. `type ParserConfiguration` — more exploration
11. `type InvocationConfiguration` — more exploration

Much of this could have been avoided if:
- `diff --package Pkg@v1..v2` (no type) had worked → one call shows all changes
- Signatures included `params`/defaults → no need to inspect individual types for constructor shapes
- Tips guided the LLM toward efficient next steps

### 7. Tips are underutilized

Currently tips only fire in three places:
- After `package` → suggests `api`
- After `audit` → suggests `--sourcelink`
- After `api` → suggests `--docs`

Tips are the primary mechanism for guiding LLMs toward efficient next steps without changing stdout output (which would break skill parsers). More tip opportunities:

- After `diff` with a specific type → suggest omitting the type to see all changes
- After `find` → suggest `type` or `api` for the found types
- After `type` with many overloads → suggest `-m Name` to drill in
- After any error → suggest the correct syntax or a related command
- After `diff` → suggest `type` for types that changed significantly

## Compatibility Considerations

### What must not change

- Existing stdout output format for all commands (skills parse this)
- Existing option names and their behavior
- Command names and argument positions
- JSON output schema

### Safe changes

- **Adding new options** (e.g., `--oneline` for columnar output)
- **Adding new tips on stderr** (skills ignore stderr)
- **Enriching signatures** with params/defaults (more information in the same format)
- **Making commands more lenient** (accepting `*` as glob in diff, optional type argument)
- **Removing truncation limits** (more output is fine; less would break)

### The ilspy-decompile precedent

David Fowler's [ilspy-decompile skill](https://github.com/davidfowl/dotnet-skillz) wraps `ilspycmd` — a separate tool. Our skill wraps `dotnet-inspect`. Both are consumed via `dnx`. The key lesson: skill authors pin to known-good invocation patterns from SKILL.md. We should version SKILL.md carefully and document breaking changes in GitHub releases.

## Priority Order

1. **Show `params` and default values in signatures** — eliminates the worst LLM spiral
2. ~~**Fix `--terse`**~~ — resolved: unified as `--oneline`
3. **Fix `-s` crash** — straightforward bug
4. **Support globs in diff type filter** — consistency with find/api
5. **Remove diff member truncation** — easy, high value for migrations
6. **Expand tips** — guide LLMs toward fewer round-trips
7. **Update SKILL.md** — reflect all changes
