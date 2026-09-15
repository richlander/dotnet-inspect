# Accessing Platform Components

Use `find` to discover where a type lives, then retain its package or platform
scope when inspecting the type and its members.

The former `api` and standalone `platform` commands, and the `--docs`,
`--use-local-docs`, and `--samples` switches, are not current invocation syntax.
The examples below use `type`, `member`, verbosity, and section selection.

## Overview

Some APIs are available from both:

1. **NuGet packages**, selected with a package coordinate.
2. **Platform libraries**, selected from a framework such as `runtime`,
   `aspnetcore`, or `netstandard`.

For example, `JsonSerializer` is available in the `System.Text.Json` package
and the .NET runtime. These are distinct inputs and need not have the same
version or API surface.

Platform resolution can use installed SDK/runtime assets or acquired platform
packs. Selecting `--platform` is not an offline guarantee: a requested framework
version that is not available locally may require acquisition. See
[version resolution](design/version-resolution.md) for the selection and cache
rules.

## Using `find` to Discover Types

Explicit search scopes compose. To search the platform and one package:

```bash
dnx dotnet-inspect -y -- find JsonSerializer \
  --platform --package System.Text.Json@10.0.0
```

The results distinguish the sources even when the type, namespace, and library
names agree. For example, the `Source` column identifies a package coordinate
such as `System.Text.Json@10.0.0` separately from a `runtime@...` version.
The platform version depends on the selected installation/framework.

Without an explicit source, `find` uses its implicit platform scope. An
explicit package suppresses that default; add bare `--platform` when both
sources should participate.

## Package vs Platform Access

### From NuGet Package (`--package`)

```bash
dnx dotnet-inspect -y -- type JsonSerializer \
  --package System.Text.Json@10.0.0 --markdown -v:q
```

The compact output identifies the selected package, version, and TFM. A pinned
coordinate selects that package version; omitting `@version` resolves a current
stable version. Package acquisition may download a missing payload.

Documentation and symbols depend on the package's actual contents. A package
is not guaranteed to include XML documentation, an embedded PDB, or SourceLink.

### From Platform (`--platform`)

```bash
dnx dotnet-inspect -y -- type JsonSerializer \
  --platform System.Text.Json --markdown -v:q
```

Here `--platform` takes a **library name**, unlike the bare search-scope flag on
`find`. Use `--framework` to select a framework, optionally with `@version`.

Platform inputs include reference assemblies for API surfaces and runtime
assemblies for implementations. Source availability depends on the selected
assembly and matching symbols, not simply on whether its scope is a package or
platform.

Single-type output uses a tree by default. Add `--markdown` for section-based
output; the tree renderer accepts minimal, normal, and detailed verbosity,
while compact `-v:q` requires the Markdown view.

## Documentation Access

Member inspection reads available XML documentation and includes descriptions
in its member/signature output. Select an overload rather than requesting a
separate documentation flag:

```bash
dnx dotnet-inspect -y -- member JsonSerializer Serialize:1 \
  --package System.Text.Json@10.0.0

dnx dotnet-inspect -y -- member JsonSerializer Serialize:1 \
  --platform System.Text.Json
```

Both invocations render the selected `Signature`. When the XML documentation
is available, its `Description` explains the operation. The numeric overload
selector is local to that selected type surface; use `-S "Member Index"` to
inspect the overloads before choosing another member or version.

For a single type, normal verbosity enables available XML summaries in the
Markdown view:

```bash
dnx dotnet-inspect -y -- type JsonSerializer \
  --platform System.Text.Json --markdown -v:n
```

Package/local-library documentation is read from XML beside the assembly.
Platform member/type documentation can come from the framework reference
pack's XML files. Reading those available descriptions does not require
fetching authored source through SourceLink. Missing XML documentation does not
mean the metadata API or PDB source is missing.

If the platform inputs are already installed or cached, use the actual network
policy flag for an offline inspection:

```bash
dnx dotnet-inspect -y -- member JsonSerializer Serialize:1 \
  --platform System.Text.Json --offline
```

Offline mode does not manufacture a missing target, XML file, or PDB. The flag
governs dotnet-inspect's acquisition, not an initial tool download by `dnx`;
disconnected use also requires the tool itself to be installed or cached.

## Documentation vs Source and Samples

API descriptions, PDB-mapped source, and sample references are different
evidence. Do not infer a `Documentation` or `Samples` section from the presence
of XML documentation, or assume a package supplies usable sample URLs.

Discover the current type or member section catalog with `-D`:

```bash
dnx dotnet-inspect -y -- type JsonSerializer \
  --platform System.Text.Json --markdown -D
```

Source-specific selections include type `Source Files` and member
`Source Locations` / `PDB Source`. Those paths may need matching PDBs and
authorized source acquisition. A supported section name is not a promise of
source evidence for every artifact. See [SourceLink exposure](sourcelink-exposure.md)
and [PDB acquisition](pdb-acquisition.md) for those boundaries.

[Sample references](sample-references.md) describes reference formats in
documentation; it is not a package-only sample-discovery command.

## Comparing Package and Platform Versions

Use discovery to see the selected platform version, and package version
listing to inspect published package versions:

```bash
dnx dotnet-inspect -y -- find JsonSerializer --platform
dnx dotnet-inspect -y -- package System.Text.Json --versions -n 1
```

The results answer different questions: the platform version currently
selected and the latest stable package version. They need not match. Retain
the appropriate scope when continuing to `type` or `member`.

## Scope Flags

Search commands support these source groups:

| Flag | What it searches |
| ---- | ---------------- |
| *(no source flags)* | Implicit platform scope: runtime, aspnetcore, netstandard |
| bare `--platform` | Explicit platform scope, including when packages are also selected |
| `--extensions` | Current Microsoft.Extensions package set |
| `--aspnetcore` | Current ASP.NET Core package set |
| `--package Name@version` | The specified package coordinate |

[Search scope resolution](design/search-scope-resolution.md) owns default
activation and explicit composition. Named package sets can require package
downloads; they are not aliases for the installed shared frameworks.

## When to Use Each

| Scenario | Starting point |
| -------- | -------------- |
| Discover a type's location | `find <pattern>`; add explicit package/platform scopes as needed |
| Inspect a package version | `type <type> --package Name@version` |
| Inspect a platform API | `type <type> --platform Library` |
| Read a member's available XML description | `member <type> Name:N` with the retained source scope |
| Request PDB-mapped source evidence | Discover with `-D`, then select the relevant source section |
| Prohibit network acquisition | Add `--offline`; required inputs must already be available |

## Platform Directory Structure

Use the .NET CLI to list installed SDKs and runtimes:

```bash
dotnet --list-sdks
dotnet --list-runtimes
```

Under a .NET installation, reference packs and runtime implementations occupy
different directories. For example:

```text
packs/Microsoft.NETCore.App.Ref/<version>/ref/<tfm>/
  System.Text.Json.dll
  System.Text.Json.xml

shared/Microsoft.NETCore.App/<version>/
  System.Text.Json.dll
```

Paths and installed versions vary by host. Use `find --platform` and
`type`/`member --platform Library` to inspect through the tool's resolver rather
than assuming one installation path.
