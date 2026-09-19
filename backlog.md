# Backlog

## Smarter defaults for agent and human users

The tool's defaults were designed for human terminal use, but most callers are now agents. Two ideas to reduce guidance overhead by making the defaults better:

### Auto-detect agent context and default to table output

If the tool detects it's being called by an agent (piped output, non-TTY, or an env var), default to `--table` output. Humans at a terminal keep the current full output. This matches how agents actually work — scan a compact list first, then drill into specific items. Every agent transcript shows this pattern. The win: shorter SKILL.md guidance because the tool does the right thing by default.

### Document comma behavior in `find` for generic types

`dotnet-inspect find "Dictionary<TKey,TValue>"` splits on the comma and treats it as two patterns: `Dictionary<TKey` and `TValue>`. The comma is the multi-pattern delimiter (`find String,Int32`). Users should use glob syntax (`Dictionary*`) instead. Consider documenting this in help text or escaping commas inside `<>` angle brackets.

### Diff: suggest replacements for removed members

When `diff` reports "Member 'AddOption' was removed", it could also suggest likely replacements from the new version. We already have the ability to detect semantic distance neighbors — use that to find the closest match in the new API surface (e.g. `AddOption` → `Options` property or `Add` method). An agent doing a migration currently needs a follow-up `member --table` call to discover what replaced the removed API. A "possible replacement" hint in the diff output would save that round-trip. Observed in a real agent transcript migrating System.CommandLine beta → 2.0.3.
