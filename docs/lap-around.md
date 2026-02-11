# Lap around `dotnet-inspect`

`dotnet-inspect` exposes a lot of functionality. It for .NET what `docker inspect` and `kubectl describe` are for container land. They are the inspiration, after 10+ years of .NET + cloud progress.

The following "lap around" walkthough demonstrates a set of highlights and also a natural progression of using the commands. It was also used as a "test case" for the tool maintainer.

It operates on three types of content:

- Platform libraries, such `System.Collections`
- Packages, such as [Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI)
- Paths, such `path-to/library.dll` or `path-to/library.nupkg`

There are multiple ways to run the tool:

- `dnx dotnet-inspect -y --`                       # Will launch the latest version of the tool from nuget.org
- `dotnet-inspect`                                 # Assumes `dotnet tool install -g dotnet-inspect`
- `dotnet inspect`                                 # Assumes `dotnet tool install -g dotnet-inspect`
- `dotnet run --project src/dotnet-inspect`        # Assumes running from repo root

Notes:

- This document will use `dotnet-inspect`.
- The tool requires .NET 10+ SDK to be installed.
- This output is driven by [markout](https://github.com/richlander/markout).

## Basic operation

The tool enables inspecting various resources by typing content like `Microsoft.Extensions.AI` as an argument.

Default verbosity, targeting a platform library:

```bash
$ dotnet-inspect Microsoft.Extensions.AI
# Microsoft.Extensions.AI (10.3.0)

Utilities for working with generative AI components.

Version: 10.3.0 | Type: Library | TFM: net10.0 | Updated: 2026-02-10

## Package

| Property | Value |
| -------- | ----- |
| Version | 10.3.0 |
| Type | Library |
| TFM | net10.0 |
| Updated | 2026-02-10 |
| Authors | Microsoft |
| Owners | dotnetframework, Microsoft |
| License | MIT |
| Repository | https://github.com/dotnet/extensions |
| Verified | yes |
| Content | lib |
| Target Frameworks | 5 |
| Readme | yes |

Tips:
package Microsoft.Extensions.AI -v:d    # detailed metadata
library Microsoft.Extensions.AI         # inspect library
api --package Microsoft.Extensions.AI   # view public API surface
```

Quiet verbosity, targeting local assets:

```bash
$ dotnet-inspect artifacts/bin/DotnetInspector.Metadata/debug/DotnetInspector.Metadata.dll -v:q
# DotnetInspector.Metadata.dll

Name: DotnetInspector.Metadata | Version: 1.0.0 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 183.0 KB | Source: File | Modified: 2026-02-11
$ dotnet-inspect artifacts/packages/dotnet-inspect.0.2.0.nupkg -v:q
# dotnet-inspect (0.2.0)

Version: 0.2.0 | Type: Tool v2 | TFM: net10.0
$ dotnet-inspect artifacts/packages/dotnet-inspect.linux-x64.0.2.0.nupkg -v:q
# dotnet-inspect.linux-x64 (0.2.0)

Version: 0.2.0 | Type: Tool v2
```

Note: The intent is that the quiet view always include high-value information. The quiet views are uniform across the tool, as much as possible.

Detailed verbosity, targeting a platform library:

```bash
$ dotnet-inspect System.Text.Json -v:d | wc -l
61
$ dotnet-inspect System.Text.Json -v:d | grep '##'  #sections!
## Library Info
## Symbols
## Extension Methods
## Custom Attributes
## Type Forwarders
```

The detailed verbosity is intended to offer all the relevant information that anyone would want, with no concern for length. Section selection is the antidote to that problem. As suggested, this system is based on object serialization. The set of objects to serialize are known a priori and can be filtered via the CLI.

Section selection:

```bash
$ dotnet-inspect System.Text.Json -s
Library Info
Symbols
Extension Methods
Custom Attributes
Type Forwarders
$ dotnet-inspect System.Text.Json -s "Extension Methods" | head -n 12
# System.Text.Json.dll

Name: System.Text.Json | Version: 10.0.3 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 80.8 KB | Source: Platform (runtime) | Modified: 2026-01-26

## Extension Methods

| Name | Kind | Extended Type | Class |
| ---- | ---- | ------------- | ----- |
| Deserialize (5 overloads) | method | System.Text.Json.JsonDocument | System.Text.Json.JsonSerializer |
| Deserialize (5 overloads) | method | System.Text.Json.JsonElement | System.Text.Json.JsonSerializer |
| GetJsonSchemaAsNode | method | System.Text.Json.JsonSerializerOptions | System.Text.Json.Schema.JsonSchemaExporter |
| Deserialize (5 overloads) | method | System.Text.Json.Nodes.JsonNode | System.Text.Json.JsonSerializer |
```

Convenience globbing:

```bash
$ dotnet-inspect System.Text.Json -s Exten* | head -10
# System.Text.Json.dll

Name: System.Text.Json | Version: 10.0.3 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 80.8 KB | Source: Platform (runtime) | Modified: 2026-01-26

## Extension Methods

| Name | Kind | Extended Type | Class |
| ---- | ---- | ------------- | ----- |
| Deserialize (5 overloads) | method | System.Text.Json.JsonDocument | System.Text.Json.JsonSerializer |
| Deserialize (5 overloads) | method | System.Text.Json.JsonElement | System.Text.Json.JsonSerializer |
```

The tool uses globbing wherever it can for convenience.

## Tool help

The tool offers help via multiple commands.

```bash
dotnet-inspect                                     # No args launch; basic tool help
dotnet-inspect cli                                 # CLI args explorer -- tree view; single level)
dotnet-inspect cli -v:d                            # CLI args explorer -- tree view; deep view, all levels)
dotnet-inspect cli -v:q                            # CLI args explorer -- oneliner
dotnet-inspect cli api                             # Explore args for a particular agument (this is full depth; does't go deeper)
dotnet-inspect llmstxt                             # Prints llmstxt, intended for LLMs
dotnet-inspect skill                               # Prints SKILL.md for anyone that wants it
dontet-inspect cache                               # How to clean the cache
```

## Tips

Tips are offered throughout the CLI to offer suggestions on what to do next, written to `stderr`.

```bash
dotnet-inspect -T:d                                 # Opt for more tips
dotnet-inspect -T                                   # Silence tips via tips arg (-T defaults to to -T:q)
dotnet-inspect -v:q                                 # Silence tips via quiet verbosity
DOTNET_INSPECT_TIPS=quiet dotnet-inspect            # Silence tips via ENV
dotnet-inspect 2>/dev/null                          # Redirect tips to /dev/null
```

Tips are contextual suggestions written to `stderr` to guide the next step.

For example:

```bash
$ dotnet-inspect | grep Tips
Tip: package <package>   # inspect a NuGet package
Tip: llmstxt             # complete usage examples
Tip: -T:d                # show more tips per command
```

Note: We'll wait on feedback on whether these tips are a good idea.

## Bare Names Policy

Bare names, as demonstrated above, use a router to pick the best asset. It deserves an explanation.

A bare name is like:

```bash
dotnet-inspect Microsoft.Extensions.AI
dotnet-inspect Microsoft.Extensions.AI.dll
dotnet-inspect Microsoft.Extensions.AI.nupkg
```

That is as opposed to a value contextualized by a command kind (like `package`):

```bash
dotnet-inspect package Microsoft.Extensions.AI
```

In that case, `package` is a command and `Microsoft.Extensions.AI` is a position argument. In the first example, the package is positional as the only argument, therefore a "bare name".

Bare name policy / rationale:

- Libraries like `System.Text.Json` and `Microsoft.AspNetCore` ship in both platform-land and package-land.
- This tool assigns a **platform orientation** to those libraries when used as bare names.
- We recently shipped [package pruning](https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/nu1510-pruned-references), which (in effect) yanks package references back into the platform.
- It is better to provide a consistent platform view for all platform libraries than a patchwork (... and for this one, the package is better ...; nope).
- Bare names like [System.Runtime](https://www.nuget.org/packages/System.Runtime) resolve to packages last published in the 1870s (OK, 2019).
- In order to provide a good experience, the tool downloads Ref and Runtime packages for any version. It's actually a better overall experience.

The router only applies special routing logic to `System.` and `Microsoft.` bare names (for the most part). The router applies to `Microsoft.` names like `Microsoft.AspNetCore` not `Microsoft.Extension.AI` or `Microsoft.Data.SqlClient`. This latter two names are firmly in package-land. This determination is done by hit tests on downloaded packs not hard-coded lists. This also means that the tool always download Microsoft built content for `System.Text.Json` and friends, even in environments (like Ubuntu) that provide other builds. It's still possible to inspect those files with the tool, just not with bare names.

## Find

You can search for types in any scope. The default is platform runtime assemblies (not `aspnetcore`).

```bash=
$ dotnet-inspect find "Chat*,Diction*" -v:q --framework runtime --framework aspnetcore --package Microsoft.Extensions.AI
# Find Results

## Chat*

Matches: 6
| Type | Namespace | Kind | Library | Source |
| ---- | --------- | ---- | ------- | ------ |
| ChatClientBuilderServiceCollectionExtensions | Microsoft.Extensions.DependencyInjection | class | Microsoft.Extensions.AI | Microsoft.Extensions.AI@10.3.0 |
| ChatClientBuilder | Microsoft.Extensions.AI | class | Microsoft.Extensions.AI | Microsoft.Extensions.AI@10.3.0 |
| ChatClientBuilderChatClientExtensions | Microsoft.Extensions.AI | class | Microsoft.Extensions.AI | Microsoft.Extensions.AI@10.3.0 |
| ChatClientStructuredOutputExtensions | Microsoft.Extensions.AI | class | Microsoft.Extensions.AI | Microsoft.Extensions.AI@10.3.0 |
| ChatResponse`1 | Microsoft.Extensions.AI | class | Microsoft.Extensions.AI | Microsoft.Extensions.AI@10.3.0 |
| ChatClientBuilderToolReductionExtensions | Microsoft.Extensions.AI | class | Microsoft.Extensions.AI | Microsoft.Extensions.AI@10.3.0 |

## Diction*

Matches: 5
| Type | Namespace | Kind | Library | Source |
| ---- | --------- | ---- | ------- | ------ |
| Dictionary`2 | System.Collections.Generic | class | System.Collections | runtime@10.0.3 |
| DictionaryBase | System.Collections | class | System.Collections.NonGeneric | runtime@10.0.3 |
| DictionaryEntry | System.Collections | struct | System.Runtime | runtime@10.0.3 |
| DictionaryModelBinder`2 | Microsoft.AspNetCore.Mvc.ModelBinding.Binders | class | Microsoft.AspNetCore.Mvc.Core | aspnetcore@10.0.3 |
| DictionaryModelBinderProvider | Microsoft.AspNetCore.Mvc.ModelBinding.Binders | class | Microsoft.AspNetCore.Mvc.Core | aspnetcore@10.0.3 |
```

## API Lists

The `api` command loads and queries libraries for API metadata, including type and member lists. This command supports bare names as well.

Inspect APIs from multiple sources.

```bash
dotnet-inspect api System.Text.Json@10.0.2              # Lists System.Text.Json types from platform at a specific version
dotnet-inspect api --platform System.Text.Json          # Lists System.Text.Json from the platform (equivalent, modulo version difference)
dotnet-inspect api --package System.Text.Json@10.0.2    # Lists System.Text.Json types from platform at a specific version
dotnet-inspect api Newtonsoft.Json                      # Lists Newtonsoft.Json types
dotnet-inspect api --library artifacts/bin/DotnetInspector.Metadata/debug/DotnetInspector.Metadata.dll     # Inspect library directly
```

Type lists:

```bash
dotnet-inspect api System.Text.Json                     # Lists all public types in kind-specific sections (Classes, Enums, ...)
dotnet-inspect api System.Text.Json -s "Classes"        # Lists a specific section, excluding all others
dotnet-inspect api System.Text.Json JsonS*              # List public types that match a specific glob
dotnet-inspect api System.Text.Json --docs              # Show docs for types
```

Notes:

- `--docs` can be slow on type scope (result is cached for second used)
- `--docs` is enabled for member scope; can be disabled with `--docs false`

Member lists:

```bash
dotnet-inspect api System.Text.Json JsonSerializer               # Lists all public members 
dotnet-inspect api System.Text.Json JsonSerializer Deserialize   # Lists all overloads for a name
dotnet-inspect api System.Text.Json JsonSerializer Deserial*     # Lists all members matching glob
dotnet-inspect api System.Text.Json JsonSerializer Deserial      # "Did you mean: ..."
```

Packages with multiple libraries:

```bash
$ dotnet-inspect package Microsoft.Azure.SignalR --tfms
net8.0
netstandard2.0
$ dotnet-inspect package Microsoft.Azure.SignalR --files --tfm net8.0 | grep dll
Microsoft.Azure.SignalR.Common.dll
Microsoft.Azure.SignalR.dll
$ dotnet-inspect api Microsoft.Azure.SignalR -v:q
# Microsoft.Azure.SignalR

Library: Microsoft.Azure.SignalR.dll | Types: 9 | Methods: 20 | Properties: 23 | Source: NuGet | Version: 1.32.0 | TFM: net8.0
$ dotnet-inspect api Microsoft.Azure.SignalR -v:q --library Microsoft.Azure.SignalR.Common.dll
# Microsoft.Azure.SignalR

Library: Microsoft.Azure.SignalR.Common.dll | Types: 38 | Methods: 36 | Properties: 18 | Source: NuGet | Version: 1.32.0
```

The `--shape` flag enables printing a diagram for a type, with quite a lot of type information packed in:

```bash=
$ dotnet-inspect api System.Collections "HashSet<T>" --shape
# System.Collections.Generic.HashSet`1

Kind: class | Library: System.Collections

├─ Inherits
│  └─ System.Object
├─ Implements
│  ├─ System.Collections.Generic.ICollection<T>
│  ├─ System.Collections.Generic.IEnumerable<T>
│  ├─ System.Collections.IEnumerable
│  ├─ System.Collections.Generic.IReadOnlyCollection<T>
│  ├─ System.Collections.Generic.ISet<T>
│  ├─ System.Collections.Generic.IReadOnlySet<T>
│  ├─ System.Runtime.Serialization.IDeserializationCallback
│  └─ System.Runtime.Serialization.ISerializable
├─ Type Parameters
│  └─ T
├─ Constructors (6)
│  ├─ void .ctor()
│  ├─ void .ctor(System.Collections.Generic.IEnumerable<T> collection)
│  ├─ void .ctor(System.Collections.Generic.IEnumerable<T> collection, System.Collections.Generic.IEqualityComparer<T> comparer)
│  ├─ void .ctor(System.Collections.Generic.IEqualityComparer<T> comparer)
│  ├─ void .ctor(int capacity)
│  └─ void .ctor(int capacity, System.Collections.Generic.IEqualityComparer<T> comparer)
├─ Properties (3)
│  ├─ int Capacity { get; }
│  ├─ System.Collections.Generic.IEqualityComparer<T> Comparer { get; }
│  └─ int Count { get; }
└─ Methods (27)
   ├─ bool Add(T item)
   ├─ void Clear()
   ├─ bool Contains(T item)
   ├─ void CopyTo(T[] array)
   ├─ void CopyTo(T[] array, int arrayIndex)
   ├─ void CopyTo(T[] array, int arrayIndex, int count)
   ├─ System.Collections.Generic.IEqualityComparer<System.Collections.Generic.HashSet<T>> CreateSetComparer()
   ├─ int EnsureCapacity(int capacity)
   ├─ void ExceptWith(System.Collections.Generic.IEnumerable<T> other)
   ├─ AlternateLookup<T, TAlternate> GetAlternateLookup()
   ├─ Enumerator<T> GetEnumerator()
   ├─ void IntersectWith(System.Collections.Generic.IEnumerable<T> other)
   ├─ bool IsProperSubsetOf(System.Collections.Generic.IEnumerable<T> other)
   ├─ bool IsProperSupersetOf(System.Collections.Generic.IEnumerable<T> other)
   ├─ bool IsSubsetOf(System.Collections.Generic.IEnumerable<T> other)
   ├─ bool IsSupersetOf(System.Collections.Generic.IEnumerable<T> other)
   ├─ void OnDeserialization(object sender)
   ├─ bool Overlaps(System.Collections.Generic.IEnumerable<T> other)
   ├─ bool Remove(T item)
   ├─ int RemoveWhere(System.Predicate<T> match)
   ├─ bool SetEquals(System.Collections.Generic.IEnumerable<T> other)
   ├─ void SymmetricExceptWith(System.Collections.Generic.IEnumerable<T> other)
   ├─ void TrimExcess()
   ├─ void TrimExcess(int capacity)
   ├─ bool TryGetAlternateLookup(ref AlternateLookup<T, TAlternate> lookup)
   ├─ bool TryGetValue(T equalValue, ref T actualValue)
   └─ void UnionWith(System.Collections.Generic.IEnumerable<T> other)
```

BTW: `HashSet<T>` is quite underrated.

There are a few libraries with samples. This a capability to develop more in future.

```bash=
$ dotnet-inspect api Markout MarkoutWriter --samples -v:q
# Markout.MarkoutWriter (Markout 0.5.1)

Kind: class | Modifiers: sealed | Library: Markout | Package: Markout | Version: 0.5.1 | Source: NuGet | Samples: 5 available | Constructors: 6 | Properties: 6 | Methods: 27
$ dotnet-inspect api Newtonsoft.Json JObject --samples -v:q
# Newtonsoft.Json.Linq.JObject (Newtonsoft.Json 13.0.4)

Kind: class | Base: Newtonsoft.Json.Linq.JContainer | Library: Newtonsoft.Json | Package: Newtonsoft.Json | Version: 13.0.4 | Source: NuGet | TFM: net6.0 | Samples: 1 available | Constructors: 4 | Properties: 3 | Methods: 22 | Events: 2
```

You can also print detailed member pages, with code, using the following technique (to correctly address the member).

Member docs normally show docs. You can opt them into the `--select` mode, which adds a `Select` column to the member tables. This column includes the argument that is needed for the detailed member doc to correctly identify/address that member.

```bash
$ dotnet-inspect api --package Microsoft.Extensions.Options OptionsFactory --select
# Microsoft.Extensions.Options.OptionsFactory`1 (Microsoft.Extensions.Options 10.0.3)

Kind: class | Type Parameters: TOptions : class | Library: Microsoft.Extensions.Options | Package: Microsoft.Extensions.Options | Version: 10.0.3 | Source: NuGet | TFM: net10.0

## Constructors

| Name | Signature | Select |
| ---- | --------- | ------ |
| .ctor | `void .ctor(System.Collections.Generic.IEnumerable<Microsoft.Extensions.Options.IConfigureOptions<TOptions>>, System.Collections.Generic.IEnumerable<Microsoft.Extensions.Options.IPostConfigureOptions<TOptions>>)` | `-m .ctor --params IEnumerable<Microsoft.Extensions.Options.IConfigureOptions<TOptions>>,IEnumerable<Microsoft.Extensions.Options.IPostConfigureOptions<TOptions>>` |
| .ctor | `void .ctor(System.Collections.Generic.IEnumerable<Microsoft.Extensions.Options.IConfigureOptions<TOptions>>, System.Collections.Generic.IEnumerable<Microsoft.Extensions.Options.IPostConfigureOptions<TOptions>>, System.Collections.Generic.IEnumerable<Microsoft.Extensions.Options.IValidateOptions<TOptions>>)` | `-m .ctor --params IEnumerable<Microsoft.Extensions.Options.IConfigureOptions<TOptions>>,IEnumerable<Microsoft.Extensions.Options.IPostConfigureOptions<TOptions>>,IEnumerable<Microsoft.Extensions.Options.IValidateOptions<TOptions>>` |

## Methods

| Name | Signature | Select |
| ---- | --------- | ------ |
| Create | `TOptions Create(string)` | `-m Create` |
```

For create method, there is just one, so we can use the member name and an index. It will print a member document.

```bash
dotnet-inspect api --package Microsoft.Extensions.Options OptionsFactory -m Create --index 1
dotnet-inspect api --package Microsoft.Extensions.Options OptionsFactory -m Create --index 1 | head -6
# Microsoft.Extensions.Options.OptionsFactory`1 (Microsoft.Extensions.Options 10.0.3)

Kind: class | Type Parameters: TOptions : class | Library: Microsoft.Extensions.Options | Package: Microsoft.Extensions.Options | Version: 10.0.3 | Source: NuGet | TFM: net10.0

## Methods
```

The constructors require more care:

```bash
dotnet-inspect api --package Microsoft.Extensions.Options OptionsFactory -m .ctor --params "IEnumerable<Microsoft.Extensions.Options.IConfigureOptions<TOptions>>,IEnumerable<Microsoft.Extensions.Options.IPostConfigureOptions<TOptions>>"
```

## Samples

We can look at the samples with the `samples` command.

It is possible to list all the samples that are available.

```bash=
$ dotnet-inspect samples Markout MarkoutWriter --list
# Samples: Markout (Markout)

- Markout.MarkoutWriter - Section filtering examples: https://github.com/richlander/markout/raw/2c478fca37eb318a98fd04ac94b5f2b340abbd25/samples/Serialization/SectionFiltering.cs
- Markout.MarkoutWriter - Basic writer usage: https://github.com/richlander/markout/raw/2c478fca37eb318a98fd04ac94b5f2b340abbd25/samples/Serialization/WriterUsage.cs
- Markout.MarkoutWriter - Table output: https://github.com/richlander/markout/raw/2c478fca37eb318a98fd04ac94b5f2b340abbd25/samples/Serialization/WriterUsage.cs
- Markout.MarkoutWriter - Tree output: https://github.com/richlander/markout/raw/2c478fca37eb318a98fd04ac94b5f2b340abbd25/samples/Serialization/WriterUsage.cs
- Markout.MarkoutWriter - Direct writer usage examples: https://github.com/richlander/markout/raw/2c478fca37eb318a98fd04ac94b5f2b340abbd25/samples/Serialization/WriterUsage.cs
$dotnet-inspect samples Newtonsoft.Json JObject --list
# Samples: Newtonsoft.Json (Newtonsoft.Json)

- Newtonsoft.Json.Linq.JObject - Parsing a JSON Object from Text: https://github.com/JamesNK/Newtonsoft.Json/raw/4e13299d4b0ec96bd4df9954ef646bd2d1b5bf2a/Src/Newtonsoft.Json.Tests/Documentation/LinqToJsonTests.cs
```

And then print them to console:

````bash=
dotnet-inspect samples Newtonsoft.Json JObject
# Samples: Newtonsoft.Json (Newtonsoft.Json)

## 1. Newtonsoft.Json.Linq.JObject - Parsing a JSON Object from Text

```csharp
string json = @"{
  CPU: 'Intel',
  Drives: [
    'DVD read/writer',
    '500 gigabyte hard drive'
  ]
}";

JObject o = JObject.Parse(json);
```
````

## Library

The `library` command enables querying library metdata (beyond API lists).


```bash=
$ dotnet-inspect library System.Text.Json
# System.Text.Json.dll

Name: System.Text.Json | Version: 10.0.3 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 80.8 KB | Source: Platform (runtime) | Modified: 2026-01-26

## Library Info

| Property | Value |
| -------- | ----- |
| Name | System.Text.Json |
| Version | 10.0.3 |
| Informational Version | 10.0.3+c2435c3e0f46de784341ac3ed62863ce77e117b4 |
| Assembly Version | 10.0.0.0 |
| Target Framework | .NETCoreApp,Version=v10.0 |
| Architecture | AnyCPU |
| Compilation | CoreCLR |
| Product | Microsoft® .NET |
| Company | Microsoft Corporation |
| Copyright | © Microsoft Corporation. All rights reserved. |
| Signed | Yes |
| Public Key Token | 1d05d9bed22b38cb |
| Deterministic | ✓ |
| Reproducible | ✓ |
| File Size | 80.8 KB |
| Types | 85 |
| Methods | 1,086 |
| Source | Platform (runtime) |
| Modified | 2026-01-26 |
```

Switch to package:

```bash=
$ dotnet-inspect library --package System.Text.Json -v:q
# System.Text.Json.dll (net10.0)

Name: System.Text.Json | Version: 10.0.3 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 634.3 KB | Source: NuGet | Modified: 2026-01-26
```

Lots of sections to check out:

```bash=
$ dotnet-inspect library --package System.Text.Json -s
Library Info
Symbols
Extension Methods
Resources
Custom Attributes
Type Forwarders
```

You can see direct dependencies:

```bash=
$ dotnet-inspect library System.Text.Json --references -s Lib*
# System.Text.Json.dll

Name: System.Text.Json | Version: 10.0.3 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 80.8 KB | Source: Platform (runtime) | Modified: 2026-01-26

## Library Info

| Property | Value |
| -------- | ----- |
| Name | System.Text.Json |
| Version | 10.0.3 |
| Informational Version | 10.0.3+c2435c3e0f46de784341ac3ed62863ce77e117b4 |
| Assembly Version | 10.0.0.0 |
| Target Framework | .NETCoreApp,Version=v10.0 |
| Architecture | AnyCPU |
| Compilation | CoreCLR |
| Product | Microsoft® .NET |
| Company | Microsoft Corporation |
| Copyright | © Microsoft Corporation. All rights reserved. |
| Signed | Yes |
| Public Key Token | 1d05d9bed22b38cb |
| Deterministic | ✓ |
| Reproducible | ✓ |
| File Size | 80.8 KB |
| Types | 85 |
| Methods | 1,086 |
| Source | Platform (runtime) |
| Modified | 2026-01-26 |

## Library References

| Name | Version | Public Key Token |
| ---- | ------- | ---------------- |
| System.Collections | 10.0.0.0 | b03f5f7f11d50a3a |
| System.Collections.Concurrent | 10.0.0.0 | b03f5f7f11d50a3a |
| System.IO.Pipelines | 10.0.0.0 | cc7b13ffcd2ddd51 |
| System.Memory | 10.0.0.0 | cc7b13ffcd2ddd51 |
| System.Runtime | 10.0.0.0 | b03f5f7f11d50a3a |
| System.Text.Encodings.Web | 10.0.0.0 | cc7b13ffcd2ddd51 |
```

You can also see a visual display of all dependencies:

```bash=
$ dotnet-inspect library Microsoft.Extensions.AI.OpenAI --dependencies
# Microsoft.Extensions.AI.OpenAI.dll

Library: Microsoft.Extensions.AI.OpenAI | Version: 10.3.0.0 | TFM: net10.0

├─ Microsoft.Extensions.AI.Abstractions 10.3.0.0
├─ OpenAI 2.8.0.0
├─ System.ClientModel 1.8.1.0
├─ System.Collections 10.0.0.0 [Microsoft Corporation]
│  └─ System.Runtime 10.0.0.0 [Microsoft Corporation]
├─ System.Drawing.Primitives 10.0.0.0 [Microsoft Corporation]
│  ├─ System.ComponentModel.Primitives 10.0.0.0 [Microsoft Corporation]
│  │  ├─ System.Collections.NonGeneric 10.0.0.0 [Microsoft Corporation]
│  │  ├─ System.ComponentModel 10.0.0.0 [Microsoft Corporation]
│  │  └─ System.ObjectModel 10.0.0.0 [Microsoft Corporation]
│  └─ System.Numerics.Vectors 10.0.0.0 [Microsoft Corporation]
├─ System.Linq 10.0.0.0 [Microsoft Corporation]
├─ System.Memory 10.0.0.0 [Microsoft Corporation]
├─ System.Memory.Data 8.0.0.1
├─ System.Runtime.InteropServices 10.0.0.0 [Microsoft Corporation]
├─ System.Text.Encodings.Web 10.0.0.0 [Microsoft Corporation]
├─ System.Text.Json 10.0.0.0 [Microsoft Corporation]
│  ├─ System.Collections.Concurrent 10.0.0.0 [Microsoft Corporation]
│  └─ System.IO.Pipelines 10.0.0.0 [Microsoft Corporation]
└─ System.Text.RegularExpressions 10.0.0.0 [Microsoft Corporation]
   └─ System.Reflection.Emit.ILGeneration 10.0.0.0 [Microsoft Corporation]
      ├─ System.Diagnostics.StackTrace 10.0.0.0 [Microsoft Corporation]
      └─ System.Reflection.Primitives 10.0.0.0 [Microsoft Corporation]
```

Note: dependencies are displayed once at their most shallow introduction.

There is some source link related functionality, including an audit.

```bash=
$ dotnet-inspect library --package System.Text.Json -s Sy*
# System.Text.Json.dll (net10.0)

Name: System.Text.Json | Version: 10.0.3 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 634.3 KB | Source: NuGet | Modified: 2026-01-26

## Symbols

| Property | Value |
| -------- | ----- |
| PDB Format | Portable |
| PDB Location | Symbol Package |
| Symbol Server | msdl.microsoft.com |
| PDB Path | /_/src/runtime/artifacts/obj/System.Text.Json/Release/net10.0/System.Text.Json.pdb |
| SourceLink | ✓ |
| Builder | Microsoft |
$ dotnet-inspect library --package System.Text.Json --source-link-audit -s Miss*
# System.Text.Json.dll (net10.0)

Name: System.Text.Json | Version: 10.0.3 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 634.3 KB | Source: NuGet | Modified: 2026-01-26

## Missing Sources

- `/_/src/runtime/artifacts/obj/System.Text.Json/Release/net10.0/.NETCoreApp,Version=v10.0.AssemblyAttributes.cs`
- `/_/src/runtime/artifacts/obj/System.Text.Json/Release/net10.0/System.Text.Json.AssemblyInfo.cs`
```

Note: `--source-link audit doesn't work with most platform assemblies.`

There are resources in some assemblies that can be extracted with `dotnet-inspect`:

```bash=
$ dotnet-inspect library --package System.Text.Json -s | grep Reso
Resources
dotnet-inspect library --package System.Text.Json -s Resour*
# System.Text.Json.dll (net10.0)

Name: System.Text.Json | Version: 10.0.3 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 634.3 KB | Source: NuGet | Modified: 2026-01-26

## Resources

| Name | Visibility | Size |
| ---- | ---------- | ---- |
| FxResources.System.Text.Json.SR.resources | public | 39.1 KB |
| ILLink.Substitutions.xml | public | 840 B |
$ dotnet-inspect library --package System.Text.Json --extract-resources resources/ 1>/dev/null
Extracted 2 resource(s) to resources/
  FxResources.System.Text.Json.SR.resources
  ILLink.Substitutions.xml
$ ls resources/
FxResources.System.Text.Json.SR.resources  ILLink.Substitutions.xml
$ cat resources/ILLink.Substitutions.xml | head -3
<linker>
  <assembly fullname="System.Text.Json" feature="System.Resources.UseSystemResourceKeys" featurevalue="true">
    <resource name="FxResources.System.Text.Json.SR.resources" action="remove" />
```

## Package

The `package` command loads and queries packages for package metadata. They are acquired from remote nuget feeds (including relying on the nuget cache). As the command name strongly suggests, this command only downloads packages.

```bash
dotnet-inspect package System.Text.Json                     # Metadata view for package (same as -v:m)
dotnet-inspect package System.Text.Json 8.0.0               # Specify version by positional argument
dotnet-inspect package System.Text.Json@8.0.0               # Specify version by convention
dotnet-inspect package System.Text.Json --version 8.0.0     # Specify version by arg
dotnet-inspect package System.Text.Json -v:d                # Metadata, statistics, and direct package dependencies
dotnet-inspect package System.Text.Json@8.0.0 -s "Vulnerabilities"    # Select a specific section by name
dotnet-inspect package System.Text.Json --tfm net8.0 -v:d   # Filter metadata in detailed via to one TFM
dotnet-inspect package System.Text.Json -v:q                # Terse 3-line details about latest package version
```

The `-s` flag is highly contextual. It only offers sections only if one is on offer, with content.

Only one of these should offer a vulnerabilities section:

```bash
$ dotnet-inspect package System.Text.Json@8.0.0 -s -T:q
Package
Statistics
Package Dependencies
Vulnerabilities
$ dotnet-inspect package System.Text.Json -s -T:q | grep "Vulnerabilities"
$
```

The high-value line is the "compact field list", which is printed with all verbosity levels.

```bash
$ dotnet-inspect package System.Text.Json@8.0.0 -v:q | grep '^Version:'
Version: 8.0.0 | Type: Library | TFM: net8.0 | Updated: 2023-11-14 | Vulnerabilities: 2
```

Very quick way to check vulnerabilities. Output is a regular form, essentially a markdown table row without outer pipes with an interior KVP format.

Pure data:

```bash
dotnet-inspect package System.Text.Json --files             # List all file in the package, package-root qualified, one per line
dotnet-inspect package System.Text.Json --tfms              # Lists TFM folders in the package, one per line
```

Fancy:

```bash
dotnet-inspect package System.Text.Json --layout            # Tree view of all files in the package
```

It's almost always the case that you don't want to see the whole package. The following flags provide the most common filters.

- `--lib`
- `--tools`
- `--tfm [tfm]`

Examples:

```bash
dotnet-inspect package System.Text.Json --layout --lib
dotnet-inspect package System.Text.Json --files --tfm net10.0
dotnet-inspect package dotnet-inspect --layout --tools
```

Versions available:

```bash
$ dotnet-inspect package System.Text.Json --versions | wc -l
89
```

Package dependencies:

```
$ dotnet-inspect package System.Text.Json --dependencies
# System.Text.Json (10.0.3)

No additional dependencies for net10.0.
$ dotnet-inspect package System.Text.Json --tfm net9.0 --dependencies
# System.Text.Json (10.0.3)

Package: System.Text.Json | Version: 10.0.3 | TFM: net9.0

├─ System.IO.Pipelines 10.0.3 [Microsoft]
└─ System.Text.Encodings.Web 10.0.3 [Microsoft]
$ dotnet-inspect package System.Text.Json --tfms
net10.0
net9.0
net8.0
netstandard2.0
net462
```

The tool support nuget sources.

```bash=
$ dotnet-inspect package System.Text.Json --version 11.0.0-preview* --add-source https://dnceng.pkgs.visualstudio.com/public/_packaging/dotnet11/nuget/v3/index.json -v:q --prerelease
# System.Text.Json (11.0.0-preview.2.26110.125)

Version: 11.0.0-preview.2.26110.125 | Type: Library | TFM: net11.0
```

Source: https://github.com/dotnet/runtime/blob/main/docs/project/dogfooding.md

## Diff

There is a built-in diff facility.

```bash=
$ dotnet-inspect diff System.CommandLine@2.0.0-beta4.22272.1..2.0.3 -v:q | grep removed | wc -l
108
$ dotnet-inspect diff System.CommandLine@2.0.0-beta4.22272.1..2.0.3 -v:q | head -10
# API Diff: System.CommandLine

**2.0.0-beta4.22272.1** → **2.0.3**

**Summary:** 134 breaking, 81 additive, 1 potentially breaking across 83 types

## Breaking Changes

### Default

 ~/git/dotnet-inspect[⎇ main *] dotnet-inspect diff System.CommandLine@2.0.0-beta4.22272.1..2.0.3 -v:q | head -16
# API Diff: System.CommandLine

**2.0.0-beta4.22272.1** → **2.0.3**

**Summary:** 134 breaking, 81 additive, 1 potentially breaking across 83 types

## Breaking Changes

### Default

- Member 'GetArgumentDefaultValue' signature changed: `string GetArgumentDefaultValue(System.CommandLine.Argument argument)` → `string GetArgumentDefaultValue(System.CommandLine.Symbol symbol)`
- Member 'GetArgumentUsageLabel' signature changed: `string GetArgumentUsageLabel(System.CommandLine.Argument argument)` → `string GetArgumentUsageLabel(System.CommandLine.Symbol parameter)`
- Member 'GetLayout' signature changed: `System.Collections.Generic.IEnumerable<System.CommandLine.Help.HelpSectionDelegate> GetLayout()` → `System.Collections.Generic.IEnumerable<System.Func<System.CommandLine.Help.HelpContext, bool>> GetLayout()`
- Member 'SynopsisSection' signature changed: `System.CommandLine.Help.HelpSectionDelegate SynopsisSection()` → `System.Func<System.CommandLine.Help.HelpContext, bool> SynopsisSection()`
- Member 'CommandUsageSection' signature changed: `System.CommandLine.Help.HelpSectionDelegate CommandUsageSection()` → `System.Func<System.CommandLine.Help.HelpContext, bool> CommandUsageSection()`
- Member 'CommandArgumentsSection' signature changed: `System.CommandLine.Help.HelpSectionDelegate CommandArgumentsSection()` → `System.Func<System.CommandLine.Help.HelpContext, bool> CommandArgumentsSection()`
```

## Implements

It can sometimes be useful to know which types implement another, like a Stream. `implements` does that.

```bash=
$ dotnet-inspect implements Stream --framework runtime --framework aspnetcore
# Types Implementing Stream

Matches: 17

## runtime@10.0.3

| Type | Kind | Relationship | Library |
| ---- | ---- | ------------ | ------- |
| System.IO.BufferedStream | class | extends | System.Runtime |
| System.IO.Compression.BrotliStream | class | extends | System.IO.Compression.Brotli |
| System.IO.Compression.DeflateStream | class | extends | System.IO.Compression |
| System.IO.Compression.GZipStream | class | extends | System.IO.Compression |
| System.IO.Compression.ZLibStream | class | extends | System.IO.Compression |
| System.IO.FileStream | class | extends | System.Runtime |
| System.IO.MemoryStream | class | extends | System.Runtime |
| System.IO.Pipes.PipeStream | class | extends | System.IO.Pipes |
| System.IO.UnmanagedMemoryStream | class | extends | System.Runtime |
| System.Net.Quic.QuicStream | class | extends | System.Net.Quic |
| System.Net.Security.AuthenticatedStream | class | extends | System.Net.Security |
| System.Net.Sockets.NetworkStream | class | extends | System.Net.Sockets |
| System.Net.WebSockets.WebSocketStream | class | extends | System.Net.WebSockets |
| System.Security.Cryptography.CryptoStream | class | extends | System.Security.Cryptography |

## aspnetcore@10.0.3

| Type | Kind | Relationship | Library |
| ---- | ---- | ------------ | ------- |
| Microsoft.AspNetCore.WebUtilities.BufferedReadStream | class | extends | Microsoft.AspNetCore.WebUtilities |
| Microsoft.AspNetCore.WebUtilities.FileBufferingReadStream | class | extends | Microsoft.AspNetCore.WebUtilities |
| Microsoft.AspNetCore.WebUtilities.FileBufferingWriteStream | class | extends | Microsoft.AspNetCore.WebUtilities |
```

## Extensions

The `extensions` command loads assemblies at a specified scope for extensions targeting a given type name. 


```bash
dotnet-inspect extensions HttpClient                    # List all extensions targeting HttpClient
dotnet-inspect extensions HttpClient --reachable        # List all extensions one can use from HttpClient + one property away
```

These commands show a small workflow for finding extension methods:

```bash
# Step 1: Explore the Aspire hosting API — see the builder interface and core extensions
$ dotnet-inspect library Aspire.Hosting -s Ex* --json | \
    jq '[.extension_methods[].extended_type] | group_by(.) | map({type: .[0], count: length}) |
    sort_by(-.count) | .[0:3]'
[
  {
    "type": "Aspire.Hosting.ApplicationModel.IResourceBuilder<T>",
    "count": 77
  },
  {
    "type": "Aspire.Hosting.ApplicationModel.IResource",
    "count": 35
  },
  {
    "type": "Aspire.Hosting.IDistributedApplicationBuilder",
    "count": 16
  }
]
$ dotnet-inspect api --package Aspire.Hosting -t 'IDistributedApplicationBuilder'
# Aspire.Hosting

Library: Aspire.Hosting.dll | Types: 1 | Methods: 757 | Properties: 583 | Source: NuGet | Version: 13.1.0 | TFM: net8.0

## Interfaces

| Type | Members |
| ---- | ------- |
| Aspire.Hosting.IDistributedApplicationBuilder | 14 |
# Step 2: See how component packages extend that same builder
dotnet-inspect extensions IDistributedApplicationBuilder \
 --package Aspire.Hosting \
 --package Aspire.Hosting.Redis \
 --package Aspire.Hosting.PostgreSQL \
 --package Aspire.Hosting.OpenAI \
 --package Aspire.Hosting.SqlServer \
 --package Aspire.Hosting.AWS \
 --tfm net8.0 | head -10
 # Extension Methods for IDistributedApplicationBuilder

## IDistributedApplicationBuilder Extensions (30)

| Name | Kind | Class | Library | Source |
| ---- | ---- | ----- | ------- | ------ |
| AddAWSAPIGatewayEmulator | method | Aspire.Hosting.APIGatewayExtensions | Aspire.Hosting.AWS | Aspire.Hosting.AWS@9.3.2 |
| AddAWSProvisioner | method | Aspire.Hosting.AWSProvisionerExtensions | Aspire.Hosting.AWS | Aspire.Hosting.AWS@9.3.2 |
| AddAWSProvisioning | method | Aspire.Hosting.AWSProvisionerExtensions | Aspire.Hosting.AWS | Aspire.Hosting.AWS@9.3.2 |
| AddCertificateAuthorityCollection | method | Aspire.Hosting.ApplicationModel.CertificateAuthorityCollectionResourceExtensions | Aspire.Hosting | Aspire.Hosting@13.1.0 |
```

Note: The first step isn't actually required if you know the popular types that are extended. The main thing is showing that you can search for extensions in two directions.


This example shows some nice C/#14 extensions syntax.

```bash=
$  dotnet-inspect library artifacts/bin/DotnetInspector.Metadata/debug/DotnetInspector.Metadata.dll -s Ext*
# DotnetInspector.Metadata.dll

Name: DotnetInspector.Metadata | Version: 1.0.0 | TFM: .NETCoreApp,Version=v10.0 | Arch: AnyCPU | Size: 183.0 KB | Source: File | Modified: 2026-02-11

## Extension Methods

| Name | Kind | Extended Type | Class |
| ---- | ---- | ------------- | ----- |
| GetFullTypeName (3 overloads) | method | System.Reflection.Metadata.MetadataReader | DotnetInspector.Metadata.MetadataReaderExtensions |
| GetGenericParameterName | method | System.Reflection.Metadata.MetadataReader | DotnetInspector.Metadata.MetadataReaderExtensions |
| IsPublic | property | System.Reflection.Metadata.TypeDefinition | DotnetInspector.Metadata.MetadataReaderExtensions |
```

The `property` extension is the tell.
