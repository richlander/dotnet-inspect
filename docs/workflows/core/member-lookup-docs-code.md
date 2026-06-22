---
id: member-lookup-docs-code
description: Look up type members with documentation and source code
commands: [member, source]
areas: [members, documentation, source, decompilation, nullability, overloads]
---

# Member Lookup with Docs and Code

> Drill into type members to see documentation, lowered C#, SourceLink-backed source, and IL. The `member` command shows docs by default; selected overloads expose implementation bodies through opt-in sections. Use overload addressing (`Name:N`) to target specific overloads.

## Preconditions

Named isolated session ensures reproducible results (no shared state, no NuGet cache).

```bash
export DOTNET_INSPECT_ISOLATED=member-lookup
```

```bash
dotnet-inspect cache clear
```

Prime the cache with test packages:

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

```bash
dotnet-inspect Microsoft.Extensions.Options@10.0.2 -v:q
```

```bash
dotnet-inspect System.Collections@4.3.0 -v:q
```

## 1. List all members of a type

> Goal: See all public members with their signatures.

### 1a. Default verbosity

```prompt
What members does the Command type in System.CommandLine have?
```

```bash
dotnet-inspect member --package System.CommandLine Command
```

```expect
# System.CommandLine.Command
## Constructors
## Properties
## Method Groups
```

```expect-not
Tips:
```

```query
grep -c '| ---- |'
```

### 1b. Quiet mode (heading only)

```bash
dotnet-inspect member --package System.CommandLine Command -v:q
```

```expect
# System.CommandLine.Command
Kind: class
Constructors: 1
Properties: 8
Methods: 10
```

```expect-not
## Constructors
## Properties
Tips:
```

### 1c. Detailed verbosity (full member tables)

```bash
dotnet-inspect member --package System.CommandLine Command -v:d
```

```expect
| Name | Signature | Description |
Represents a specific action
Initializes a new instance
```

```expect-not
Tips:
```

## 2. Filter members by name

> Goal: Show only members matching a specific name (including all overloads).

### 2a. Using positional member name

```prompt
Show me the SetAction method on Command in System.CommandLine.
```

```bash
dotnet-inspect member --package System.CommandLine Command SetAction
```

```expect
# System.CommandLine.Command
## Methods
SetAction
```

```expect-not
Add
Parse
Tips:
```

### 2b. Using `-m` flag with glob

```bash
dotnet-inspect member System.Text.Json JsonSerializer -m 'Deseri*' -v:q
```

```expect
# System.Text.Json.JsonSerializer
Kind: class
Methods: 103
```

```expect-not
## Methods
Tips:
```

## 3. View member implementation code

> Goal: When selecting a specific member, discover and select implementation sections: lowered C# (`Decompiled Source`), SourceLink-backed source (`Original Source`), and IL.

### 3a. Single member (no overloads)

```prompt
Show me the source code for Command.Add in System.CommandLine.
```

```bash
dotnet-inspect member --package System.CommandLine Command Add -S "Decompiled Source" -n 30
```

```expect
## Decompiled Source
```

```expect-not
Tips:
```

### 3b. Member with overloads (first overload)

```bash
dotnet-inspect member --package System.CommandLine Command SetAction:1 -S "Decompiled Source" -n 30
```

```expect
## Decompiled Source
public void SetAction(Action<ParseResult> action)
```

```expect-not
Tips:
```

## 4. Select specific overload from Member Index

> Goal: Use `Member Index` to see interactive `Name:N` selectors, durable
> `Name~digest` selectors, and the `Canonical Signature` used for digest
> computation. See [Member Index](../../design/member-index.md) for the digest
> contract.

### 4a. Show member index

```bash
dotnet-inspect member --package Microsoft.Extensions.Options OptionsFactory -S "Member Index" -n 25
```

```expect
## Member Index
| Selector | Stable | Canonical Signature |
.ctor:1
.ctor~...
```

### 4b. Select constructor overload

```bash
dotnet-inspect member --package Microsoft.Extensions.Options OptionsFactory .ctor:1 -S "Original Source" -n 30
```

```expect
## Original Source
public OptionsFactory(IEnumerable<IConfigureOptions<TOptions>> setups
```

```expect-not
Tips:
```

## 5. View constructors only

> Goal: Filter to constructors using `--ctor` shorthand.

```bash
dotnet-inspect member --package System.CommandLine Command --ctor -v:q
```

```expect
# System.CommandLine.Command
Kind: class
Constructors: 1
```

```expect-not
## Constructors
Tips:
```

## 7. Platform library members

> Goal: View members from platform assemblies (no `--package` needed for System.* in platform).

### 6a. List members

```bash
dotnet-inspect member System.Text.Json JsonSerializer -v:q
```

```expect
# System.Text.Json.JsonSerializer
Kind: class
Source: Platform
Methods: 103
```

### 6b. Filter to specific method

```bash
dotnet-inspect member System.Text.Json JsonSerializer Deserialize -S "Decompiled Source" -n 50
```

```expect
# System.Text.Json.JsonSerializer
## Methods
Deserialize
## Decompiled Source
```

```expect-not
Tips:
```

## 8. View IL disassembly

> Goal: See raw IL with resolved tokens, plus the mixed Decompiled Source view
> (C# with hidden-fact comments and the IL interleaved beneath each statement).

```bash
dotnet-inspect member --package System.CommandLine Command SetAction:2 -v:n -n 80
```

```expect
## Decompiled Source
// alloc.closure
// IL_0000: newobj
```

```expect
## Recovered IL
IL_0000: newobj
```

```expect-not
Tips:
```

## 9. Suppress documentation

> Goal: Use `--no-docs` to skip XML doc fetching for faster output.

```bash
dotnet-inspect member --package System.CommandLine Command -v:d --no-docs -n 30
```

```expect
## Constructors
| Name | Signature |
```

```expect-not
| Description |
```

## 10. Generic type members

> Goal: Address generic types using backtick notation or quoted names.

### 9a. Using backtick notation

```bash
dotnet-inspect member --package Microsoft.Extensions.Options 'OptionsFactory`1' -v:q
```

```expect
# Microsoft.Extensions.Options.OptionsFactory<TOptions>
Kind: class
Type Parameters: TOptions
Constructors: 2
Methods: 1
```

### 9b. Using quoted generic syntax

```bash
dotnet-inspect member --package System.Collections 'HashSet<T>' -v:q
```

```expect
# System.Collections.Generic.HashSet<T>
Kind: class
Type Parameters: T
```

## 11. Nullability annotations in signatures

> Goal: Member signatures include C# nullability annotations (`?` suffix) — verify they appear in shape and member views.

### 10a. Nullable parameters in shape view

```bash
dotnet-inspect type --package System.CommandLine Command --shape -n 10
```

```expect
void .ctor(string name, string? description = null)
CommandLineAction? Action { get; set; }
```

### 10b. Nullable return types in member view

```bash
dotnet-inspect member System.Text.Json JsonSerializer Deserialize -n 10
```

```expect
TValue?
```

## 12. Oneline output for scripting

> Goal: Get columnar output suitable for piping to other tools.

```bash
dotnet-inspect member --package System.CommandLine Command --table --no-headers -n 10
```

```expect
constructor
property
method
```

```expect-not
Tips:
| Name |
```

```query
wc -l | tr -d ' '
```
