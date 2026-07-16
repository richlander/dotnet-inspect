---
id: interactive-repl
description: Explore a package contextually through types, members, and source
commands: [repl, package, type, member]
areas: [interactive, navigation, experimental]
---

# Contextual interactive inspection (experimental)

The `repl` command is an opt-in pilot. It leaves every existing CLI command and
output contract intact, while providing a stateful package → type → member
navigation layer using `Repl.Core 0.12.0-dev.3`.

The pilot deliberately delegates each action through the existing
`System.CommandLine` graph. This compatibility bridge avoids duplicating the
large command orchestration and preserves current parsing, diagnostics,
overload indexes, sections, and rendering. A future production implementation
can extract structured operations from the command layer after the interaction
model has been validated.

## 1. Drill from a package into decompiled member source

> Goal: Browse a real NuGet package without repeatedly spelling its package and
> type context.

```prompt
Open System.Text.Json interactively, inspect JsonSerializer.Serialize, and show
the first overload's decompiled source.
```

```bash
dotnet-inspect repl
```

```usage
> package System.Text.Json
[selected-package]> libraries
[selected-package]> types
[selected-package]> type System.Text.Json.JsonSerializer
[selected-package/selected-type]> members
[selected-package/selected-type]> member Serialize
[selected-package/selected-type/selected-member]> source 1
[selected-package/selected-type/selected-member]> back
[selected-package/selected-type]> back
[selected-package]> exit
```

```expect
System.Text.Json.JsonSerializer
Serialize
## Decompiled Source
```

Selecting a package, type, or member immediately executes its default `show`
operation. The selected value is stored only after exit code `0`; a failed
operation leaves the session at its previous prompt and can be retried. Prompts
show the selected levels, while the delegated output shows the actual values.

## 2. Supported contextual commands

| Prompt | Command delegated to the regular CLI |
| ------ | ------------------------------------ |
| root: `package P` | `package P --tips q` |
| package: `show` | `package P --tips q` |
| package: `libraries` | `package P -S Libraries --tips q` |
| package: `types` | `type --package P --tips q` |
| package: `type T` | `type --package P --tips q -- T` |
| type: `show` | `type --package P --tips q -- T` |
| type: `members` | `member --package P --tips q -- T` |
| type: `member M` | `member --package P --member=M --tips q -- T` |
| member: `show` | `member --package P --member=M --tips q -- T` |
| member: `source` | `member --package P --member=M -S "Decompiled Source" --tips q -- T` |
| member: `source N` | `member --package P --member=M --index N -S "Decompiled Source" --tips q -- T` |
| type/member: `back` | Navigate to the complete parent selection |
| any prompt: `help`, `exit` | Repl ambient commands |

`show` always reruns the delegated operation. Selection state changes only
after the delegated operation returns exit code `0`.

## 3. Current limitations

- This is a focused package → type → member pilot, not a parallel interactive
  grammar for every dotnet-inspect command.
- Repl `0.12.0-dev.3` rebuilds absolute navigation from space-split route
  strings. The pilot therefore keeps user selectors in session state and uses
  static `selected-*` scopes; `back` moves up one complete selected level.
- Interactive prompts and navigation are human-facing and do not define a new
  JSON or automation contract. Scripts should continue to invoke the regular
  CLI commands directly.
- Delegated output is capped at 10,000 lines or 1,000,000 characters per
  stdout/stderr stream. The Repl prints an explicit truncation diagnostic; use
  the equivalent regular CLI command when you need complete very large output.
- Ctrl+C cancellation is best effort while a delegated inspection is running.
  Existing command handlers do not all propagate cancellation through NuGet
  resolution and decompilation, so the prompt may recover only after that work
  finishes.
- `repl` is a reserved root verb. A package literally named `repl` remains
  inspectable with the explicit command `dotnet-inspect package repl`.
- The current Repl prerelease is not trim-clean (`IL2104` under full trim
  analysis with the current SDK). Untrimmed managed builds include the pilot.
  Trimmed and RID-specific NativeAOT publishes exclude Repl.Core and expose an
  unavailable stub with exit code `1`.
