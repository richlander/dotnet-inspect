---
id: member-lookup-docs-code
description: Look up type members with documentation and source code
commands: [member, source]
areas: [members, documentation, source, decompilation, nullability, overloads]
---

# Member Lookup with Docs and Code

> Drill into type members to see documentation, lowered C#, checksum-verified
> PDB source, and IL. The `member` command shows docs by default; selected
> overloads expose implementation bodies through opt-in sections. Use overload
> addressing (`Name:N`) to target specific overloads.

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
dotnet-inspect member --package System.CommandLine@2.0.3 Command
```

```expect
# System.CommandLine.Command
## Constructors
## Properties
## Method Groups
```

```expect-stderr
Tips:
```

### 1b. Quiet mode (heading only)

```bash
dotnet-inspect member --package System.CommandLine@2.0.3 Command -v:q --tips q
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
dotnet-inspect member --package System.CommandLine@2.0.3 Command -v:d --tips q
```

```expect
| Name | Digest | Signature | Description |
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
dotnet-inspect member --package System.CommandLine@2.0.3 Command SetAction
```

```expect
# System.CommandLine.Command
## Methods
SetAction
```

```expect-not
| Add |
```

```expect-stderr
Tips:
```

```query
awk -F '|' '/^\| SetAction / { print $2 }' | sort -u
```

```expect
SetAction
```

### 2b. Using `-m` flag with glob

```bash
dotnet-inspect member System.Text.Json JsonSerializer -m 'Deseri*' -v:q --tips q
```

```expect
# System.Text.Json.JsonSerializer
Kind: class
Methods:
```

```expect-not
## Methods
Tips:
```

## 3. View member implementation code

> Goal: When selecting a specific member, discover and select implementation
> sections: raised C# (`Decompiled Source`), mixed C#+IL (`Annotated Source`),
> Portable-PDB-selected, checksum-verified source acquired locally or through SourceLink
> (`PDB Source`), and raw IL (`IL`). `-S @Source` selects those four evidence
> views.

### 3a. Single member (no overloads)

```prompt
Show me the source code for Command.Add in System.CommandLine.
```

```bash
dotnet-inspect member --package System.CommandLine@2.0.3 Command Add:1 -S "Decompiled Source" -n 30 --lines --tips q
```

```expect
## Decompiled Source
public void Add(System.CommandLine.Argument argument)
```

```expect-not
Tips:
```

### 3b. Member with overloads (first overload)

```bash
dotnet-inspect member --package System.CommandLine@2.0.3 Command SetAction:1 -S "Decompiled Source" -n 30 --lines --tips q
```

```expect
## Decompiled Source
public void SetAction(System.Action<System.CommandLine.ParseResult> action)
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
dotnet-inspect member --package Microsoft.Extensions.Options@10.0.2 OptionsFactory -S "Member Index" -n 25 --tips q
```

```expect
## Member Index
| Selector | Stable | Canonical Signature |
.ctor:1
.ctor~
```

### 4b. Select constructor overload

```bash
dotnet-inspect member --package Microsoft.Extensions.Options@10.0.2 OptionsFactory .ctor:1 -S "PDB Source" -n 30 --lines --tips q
```

```expect
## PDB Source
public OptionsFactory(IEnumerable<IConfigureOptions<TOptions>> setups
```

```expect-not
Tips:
```

## 5. View constructors only

> Goal: Filter to constructors using `--ctor` shorthand.

```bash
dotnet-inspect member --package System.CommandLine@2.0.3 Command --ctor -v:q --tips q
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
dotnet-inspect member System.Text.Json JsonSerializer -v:q --tips q
```

```expect
# System.Text.Json.JsonSerializer
Kind: class
Source: Platform
Methods:
```

### 6b. Filter to specific method

```bash
dotnet-inspect member System.Text.Json JsonSerializer Deserialize:1 -S "Decompiled Source" -n 50 --lines --tips q
```

```expect
# System.Text.Json.JsonSerializer
Deserialize
## Decompiled Source
```

```expect-not
Tips:
```

## 8. View IL disassembly

> Goal: See raw IL with resolved tokens, plus the mixed Annotated Source view
> (C# with hidden-fact comments and the IL interleaved beneath each statement).

```bash
dotnet-inspect member --package System.CommandLine@2.0.3 Command SetAction:2 -S "Annotated Source,IL" -n 80 --lines --tips q
```

```expect
## Annotated Source
public void SetAction
```

```expect
## IL
```

```expect
// IL_0000: ldarg.1
alloc.new(
IL_0015: call
System.CommandLine.Command::set_Action
```

```expect-not
Tips:
```

## 9. Project member identity columns

> Goal: Select only stable member identity columns for compact scripting output.

```bash
dotnet-inspect member --package System.CommandLine@2.0.3 Command \
  -S Methods --table --columns Name,Digest,Signature -n 5 --tips q
```

```expect
Name
Digest
Signature
```

```expect-not
Description
Tips:
```

## 10. Generic type members

> Goal: Address generic types using backtick notation or quoted names.

### 9a. Using backtick notation

```bash
dotnet-inspect member --package Microsoft.Extensions.Options@10.0.2 'OptionsFactory`1' -v:q --tips q
```

```expect
# Microsoft.Extensions.Options.OptionsFactory&lt;TOptions&gt;
Kind: class
Type Parameters: TOptions
Constructors: 2
Methods: 1
```

### 9b. Using quoted generic syntax

```bash
dotnet-inspect member --package System.Collections@4.3.0 'HashSet<T>' -v:q --tips q
```

```expect
# System.Collections.Generic.HashSet&lt;T&gt;
Kind: class
Type Parameters: T
```

## 11. Nullability annotations in signatures

> Goal: Member signatures include C# nullability annotations (`?` suffix) — verify they appear in shape and member views.

### 10a. Nullable parameters in shape view

```bash
dotnet-inspect type --package System.CommandLine@2.0.3 Command --shape -n 10 --tips q
```

```expect
void .ctor(string name, string? description = null)
CommandLineAction? Action { get; set; }
```

### 10b. Nullable return types in member view

```bash
dotnet-inspect member System.Text.Json JsonSerializer Deserialize -n 10 --tips q
```

```expect
TValue?
```

## 12. Table output for scripting

> Goal: Get columnar output suitable for piping to other tools.

```bash
dotnet-inspect member --package System.CommandLine@2.0.3 Command --table --no-headers -n 10 --tips q
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
