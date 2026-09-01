---
id: lap-around
description: The big walkthrough — all the major commands in one doc
commands: [type, member, library, package, find, implements, depends, extensions, diff]
areas: [routing, resolution, output, discovery, inspection, source]
---

# Lap around `dotnet-inspect`

`dotnet-inspect` is for .NET what `docker inspect` and `kubectl describe` are for container land. The following walkthrough demonstrates a progression through the tool — three content sources, the drill-down from package to type to member to source code, and the cross-cutting discovery commands.

## Preconditions

```bash
export DOTNET_INSPECT_ISOLATED=lap-around
dotnet-inspect cache clear
dotnet build src/ILInspector.Metadata/ILInspector.Metadata.csproj \
  -c Release --nologo --verbosity quiet
dotnet-inspect Microsoft.Azure.SignalR@1.33.1 -v:q
dotnet-inspect Newtonsoft.Json@13.0.4 -v:q
dotnet-inspect Microsoft.Extensions.AI@10.8.3 -v:q
```

## 1. Three sources

The tool operates on three types of content. The quiet one-liner always tells you which.

Platform library (ships with the runtime, no network needed for cached versions):

```bash
dotnet-inspect System.Collections -v:q
```

```expect
Source: Platform
```

NuGet package (downloaded and cached from a feed):

```bash
dotnet-inspect Microsoft.Extensions.AI@10.8.3 -v:q
```

```expect
Source: NuGet
```

Local file:

```bash
dotnet-inspect artifacts/bin/ILInspector.Metadata/release/ILInspector.Metadata.dll -v:q
```

```expect
Source: File
```

```expect
TFM:
```

System.Text.Json ships in the platform *and* as a NuGet package. The tool lets you pick.

```bash
dotnet-inspect library System.Text.Json -v:q
```

```expect
Source: Platform
```

```bash
dotnet-inspect library --package System.Text.Json -v:q
```

```expect
Source: NuGet
```

The platform copy is typically a ref assembly (smaller, public surface only). The package copy is the full implementation.

## 2. Multi-library packages

Some packages ship more than one DLL. Microsoft.Azure.SignalR ships two libraries in a single package. The tool defaults to the primary library but lets you pick.

```bash
dotnet-inspect package Microsoft.Azure.SignalR@1.33.1 --path --tfm net8.0
```

```expect
Microsoft.Azure.SignalR.dll
Microsoft.Azure.SignalR.Common.dll
```

```bash
dotnet-inspect type Microsoft.Azure.SignalR@1.33.1 -v:q
```

```expect
Microsoft.Azure.SignalR.dll
```

Switch to the other library:

```bash
dotnet-inspect type Microsoft.Azure.SignalR@1.33.1 -v:q --library Microsoft.Azure.SignalR.Common.dll
```

```expect
Microsoft.Azure.SignalR.Common.dll
```

```expect-not
Microsoft.Azure.SignalR.dll |
```

## 3. Type forwarders

In modern .NET, many types in System.Collections are actually defined in
System.Private.CoreLib and *forwarded* from System.Collections. The forwarding
table identifies that implementation boundary, while the `type` command keeps
the forwarded types in the library's searchable public surface.

The library command exposes the forwarding table:

```bash
dotnet-inspect library System.Collections -v:d -S "Type Forwarders" -n 15
```

```expect
HashSet
Dictionary
List
```

HashSet, Dictionary, and List are all forwarders. The type command still makes
them addressable alongside types defined directly in the assembly:

```bash
dotnet-inspect type System.Collections
```

```expect
SortedSet
SortedDictionary
OrderedDictionary
System.Collections.Generic.Dictionary
```

The forwarding table is the authoritative place to distinguish those cases.

## 4. Drill-down: find → type → member → code

Find a type you've heard of:

```bash
dotnet-inspect find "MarkoutWriter" --package Markout@0.33.0 -v:q
```

```expect
MarkoutWriter
Markout@0.33.0
```

See its members:

```bash
dotnet-inspect member Markout@0.33.0 MarkoutWriter -v:q
```

```expect
# Markout.MarkoutWriter
Kind: class
Source: NuGet
```

For code, use `-S "Member Index"` to see the full addressing table (the durable `~digest` selector is also shown in the default member table's Digest column), then drill into a specific overload:

```bash
dotnet-inspect member --package Microsoft.Extensions.Options@10.0.10 OptionsFactory -S "Member Index"
```

```expect
.ctor:1
.ctor:2
Create
```

```bash
dotnet-inspect member --package Microsoft.Extensions.Options@10.0.10 OptionsFactory .ctor:1 -S "Decompiled Source"
```

```expect
## Decompiled Source
```

```expect
```csharp
```

Selected overloads expose implementation sections through the same detailed/enabled and explicit-only split as other commands:

| Section | What it shows |
| ------- | ------------- |
| **Decompiled Source** | Raised C# reconstructed from IL; enabled at normal verbosity |
| **Annotated Source** | Mixed view with hidden-fact comments and IL interleaved beneath each statement; opt-in via `-S "Annotated Source"` |
| **PDB Source** | Checksum-matched source selected by Portable PDB evidence, when available; enabled at detailed verbosity |
| **IL** | Raw IL disassembly with resolved tokens; enabled at normal verbosity |
| **Facts** | Structured Research overlay table (member, IL offset, C# line, anchor, category/id/detail) for one method; opt-in via `-S "Facts"` / `--tsv` |

## 5. Source URLs

SourceLink URLs are exposed on the command you are already using.

```bash
dotnet-inspect type JsonSerializer --platform System.Text.Json -S "Source Files" --table
dotnet-inspect type JsonSerializer --platform System.Text.Json -S "Source Files" --urls --json-array
dotnet-inspect type JsonSerializer --platform System.Text.Json -S "Source Files" --print --row 1
dotnet-inspect type JsonSerializer --platform System.Text.Json -S "Source Files" --print --row 1 --jsonl
```

```expect
JsonSerializer.Helpers.cs
```

Fetch selected member source text when source content is the desired artifact:

```bash
dotnet-inspect member JsonSerializer --platform System.Text.Json Serialize:1 -S "PDB Source" --bare -n 20 --lines
dotnet-inspect member JsonSerializer --platform System.Text.Json -m Serialize -S "Source Locations" --print --row 1
```

```expect
public static partial class JsonSerializer
```

For stack-trace style diagnostics, `library --il-offset` maps a MethodDef token plus IL offset to coordinate-scoped sections such as `Context: Source Location`, `Context: Member`, `Context: Instruction`, `Context: Exception`, `Context: Callsite`, and `Context: Return Address`.

```bash
dotnet-inspect library --platform System.Text.Json --il-offset 0x06000001+0x0 --json
```

```expect
"token": "0x6000001"
"line":
```

## 6. Package anatomy

### TFMs, files, and layout

```bash
dotnet-inspect package Microsoft.Azure.SignalR@1.33.1 --tfms
```

```expect
net8.0
```

The tool can inspect its own package:

```bash
dotnet-inspect package dotnet-inspect@0.16.0 --layout --tools
```

```expect
tools
DotnetToolSettings.xml
```

### Dependencies vary by TFM

```bash
dotnet-inspect package System.Text.Json@10.0.2 -S Dependencies --tree
```

```expect
No additional dependencies for net
```

```bash
dotnet-inspect package System.Text.Json@10.0.2 --tfm net9.0 -S Dependencies --tree
```

```expect
System.IO.Pipelines
System.Text.Encodings.Web
```

### Embedded resources

There are resources in some assemblies that can be extracted with `dotnet-inspect`:

```bash
dotnet-inspect library --package System.Text.Json@10.0.2 -S Resour*
```

```expect
FxResources.System.Text.Json.SR.resources
ILLink.Substitutions.xml
```

```setup
rm -rf artifacts/workflow/lap-resources
```

```bash
dotnet-inspect library --package System.Text.Json@10.0.2 --extract-resources artifacts/workflow/lap-resources 2>&1
```

```expect
Extracted 2 resource(s)
```

### Vulnerability check

Quick way to check if a pinned version has known CVEs:

```bash
dotnet-inspect package System.Text.Json@8.0.0 -v:d -S Vulnerabilities
```

```expect
## Vulnerabilities
CVE-2024-30105
GHSA-8g4q-xg66-9fp4
```

## 7. Preview packages from alternate feeds

The tool supports NuGet sources. This is useful for dogfooding preview builds.

```bash
dotnet-inspect package System.Text.Json --version '11.0.0-preview*' --add-source 'https://dnceng.pkgs.visualstudio.com/public/_packaging/dotnet11/nuget/v3/index.json' -v:q --prerelease
```

```expect
preview
Source: NuGet
```

```expect
TFM: net11.0
```

Source: <https://github.com/dotnet/runtime/blob/main/docs/project/dogfooding.md>

## 8. Discovery commands

### Find

Multi-pattern search across all platform frameworks:

```bash
dotnet-inspect find "Diction*" -v:q
```

```expect
Dictionary
System.Collections.Generic
```

Search across cached extension packages with `--extensions`:

```bash
dotnet-inspect find "Chat*" --extensions -v:q
```

```expect
ChatMessage
```

### Implements

Who extends Stream?

```bash
dotnet-inspect implements Stream -v:q
```

```expect
BufferedStream
FileStream
MemoryStream
CryptoStream
```

### Depends

The `depends` command walks the type dependency graph upward — the inverse of `implements`.

```bash
dotnet-inspect depends "INumber<TSelf>"
```

```expect
INumberBase<TSelf>
IComparable
IModulusOperators
IAdditionOperators
```

The generic math hierarchy is particularly compelling here — `INumber<TSelf>` fans out into `INumberBase<TSelf>` and all the operator interfaces, showing the full diamond inheritance with de-duplication.

### Extensions across packages

Search for extensions targeting a type across multiple packages:

```bash
dotnet-inspect extensions IDistributedApplicationBuilder \
  --package Aspire.Hosting@13.4.6 \
  --package Aspire.Hosting.Redis@13.4.6 \
  --package Aspire.Hosting.PostgreSQL@13.4.6 \
  --tfm net8.0 -v:q
```

```expect
IDistributedApplicationBuilder
```

### Package search

```bash
dotnet-inspect package search 'Azure.AI' --take 5
```

```expect
Azure.AI.OpenAI
```

## 9. Diff

There is a built-in diff facility:

```bash
dotnet-inspect diff System.CommandLine@2.0.0-beta4.22272.1..2.0.3 -v:q | head -6
```

```expect
breaking
additive
```

```expect
across
```

## 10. JSON + pipelines

The `--json` flag turns any command into structured data for pipelines. This is the primary scripting surface.

```bash
dotnet-inspect library Aspire.Hosting@13.4.6 -S Ex* --json | python3 -c "
import json, sys
d = json.load(sys.stdin)
types = [e['extended_type'] for e in d.get('extension_methods', [])]
counts = {}
for t in types:
    counts[t] = counts.get(t, 0) + 1
for t, c in sorted(counts.items(), key=lambda x: -x[1])[:3]:
    print(f'  {t}: {c}')
"
```

```expect
IResourceBuilder<T>
IResource
IDistributedApplicationBuilder
```

This is a real workflow — find the most popular extension points in a package, then use `extensions` to see what component packages hang off them.

## 11. Self-inspection

The tool can inspect its own package.

```bash
dotnet-inspect package dotnet-inspect@0.16.0 -v:q
```

```expect
Type: Tool v2
```
