# Library Info composition

## Status

Focused design for how the CLI `Library Info` section is composed from the
host-neutral [Library document](library-inspection-document.md) during the
migration of the `library` command. It is step 4 of that document's
[production adoption](library-inspection-document.md#production-adoption) for
the scalar facts adopted so far. Population sections and their counts keep
their existing paths until their own slices.

## Authority and exact claim

The CLI (`DotnetInspect.Cli`), which owns command presentation, owns this
claim:

> For a managed assembly, every scalar field the `library` view renders, in
> `Library Info`, in the compact `-v:q` summary, and in the `Library` summary
> field, has exactly one
> named source: a Library document fact, host source provenance, or a named
> legacy owner. A field whose source is a Library document fact is rendered from
> that fact and from no other reading of the image, with the same value in every
> place the view shows it.

This owner composes existing contracts and does not redefine them:

- [Library inspection documents](library-inspection-document.md#library-facts)
  owns identity and the Image, Description, and Enablements fact groups.
- [Library enablements](library-enablements.md) owns enablement states,
  reasons, and labels.
- [Assembly-context Library adapter](assembly-context-library-adapter.md) owns
  materializing an inspected image as a direct Library.
- [Primary subject views](primary-subject-views.md) owns which section a
  subject view shows by default.

## Product question

`dotnet-inspect library System.Net.Sockets` answers "what is this Library?"
The migration moves that answer onto the same facts Inspect Web and `--envelope`
report, so the three hosts cannot disagree. Displayed values do not change:
for the installed .NET 11 rc.1.26425.128 Platform Library, rows keep their
current values except that Modified disappears and Enabled appears (excerpt):

```text
| Assembly Version      | 11.0.0.0                        |
| Compilation           | ReadyToRun                      |
| Company               | Microsoft Corporation           |
| Enabled               | AOT · Runtime Async             |
| File Size             | 686.3 KB                        |
| Methods               | 1,299                           |
| Name                  | System.Net.Sockets              |
| Reproducible          | Yes                             |
| Signed                | Yes                             |
| Source                | Platform                        |
| Target Framework      | .NETCoreApp,Version=v11.0       |
| Types                 | 111                             |
| Version               | 11.0.0-rc.1.26425.128           |
```

Architecture stays absent for this image, as today, because its OS-specific
ReadyToRun machine value is one the Image vocabulary does not name.

## Field sources

| Field | Source |
| --- | --- |
| Name, Assembly Version, Public Key Token | Library document identity |
| File Size, Target Framework, Compilation, Architecture, Signed, Reproducible | Image facts |
| Informational Version, Company, Product, Copyright | Description facts |
| Enabled | Enablements facts |
| Source | Host source provenance |
| Version | See [Version](#version) |
| Types, Methods | Legacy Metadata table row counts, until a population owner claims them |
| Async Methods, Custom Attributes, Extension Methods, Integrations, Resources, Switches, Type Forwarders, Union Types | Legacy counts of their existing sections, until each population slice |
| Facade | Legacy Platform surface classification |
| Deterministic | Legacy SourceLink and PDB owner |
| Ecosystem Dependencies, Ecosystem Dependency Status | Legacy ecosystem recognition owner |
| Modified | Removed; it describes the local copy, not the Library |

The compact `-v:q` summary (Name, Version, TFM, Arch, Size, Source) and the
`Library` summary field (architecture, target framework, compilation, signed)
use the same sources as the matching `Library Info` rows and keep their current
spellings; the summary still spells a signed image `Signed`. Modified leaves
the compact summary too.

`Types` and `Methods` stay table row counts. They are not the public Type
population, and replacing them with a population Count would change their
meaning, not only their source.

A legacy row retires when its owner produces a Library document population or
fact and its section adopts it. The composition never reads the image a second
time for a fact the document already carries.

## Scope of input

The composition applies when the inspected image is a managed assembly, which
is the input a Library document describes. A native PE image, such as a
`runtimes/*/native/*.dll` package asset, and a managed module without an
assembly manifest have no assembly identity. They keep the legacy view path
unchanged, including their `Native` and `NativeAOT` compilation labels and
PE-machine architecture, until an owner for such images exists.

## Realization

Each CLI route for a managed assembly already resolves one image: a direct
file, a package asset, or a Platform assembly. The CLI materializes that image as a direct
Library through the assembly-context adapter and requests Image, Description,
and Enablements in one facts-only plan.

The host chooses the adapter role from its own selection, not from the image:

- a package implementation asset, a runtime-pack assembly, or an installed
  shared-framework assembly uses the `Implementation` role, so Enablements are
  judged on it;
- a package reference asset, a reference-pack assembly, or a direct file uses
  the `ApiOnly` role.

Source and Version come from the same host resolution. The direct Library does
not carry the package or Platform coordinate, so provenance is the host's
statement, not a document fact.

## Version

Version keeps its current fallback order:

1. the host Platform version, when the host resolved the Library from a
   Platform;
2. the Informational Version fact without build metadata, when its version
   part is numeric; a prerelease suffix is kept;
3. the Assembly Version from document identity.

An unavailable or non-numeric Informational Version falls through to the
Assembly Version. The legacy final fallback to the file version is not
reachable here, because every managed assembly has an assembly identity with a
version; inputs without one stay on the legacy path.

## Rendering

Typed Image facts keep their current spellings:

| Fact | Rendering |
| --- | --- |
| Compilation | `IL` renders `CoreCLR` and `ReadyToRun` renders `ReadyToRun`, the current labels |
| Architecture | `AnyCPU`, `AnyCPU (32-bit preferred)`, `x86`, `x64`, `ARM`, `ARM64`; the row is omitted when the fact is absent |
| Signed | `Yes` when signed; the row is omitted when not signed, as today |
| Reproducible | `Yes`, `No`, or bare `unavailable` when the debug directory cannot be read; the fact carries no reason |
| File Size | Image byte length in the existing byte-size format |

Renaming the `CoreCLR` label is a separate presentation decision.

- A present text fact renders as its text. A fact the image does not carry
  omits its row, as today.
- An unavailable Image or Description fact renders its row as
  `unavailable (<reason>)`, so a decoding problem stays visible.
- Enabled lists the labels of Enabled enablements, joined by ` · `. The row is
  omitted when none is Enabled. When the Enablements group failed, the row reads
  `unavailable (<reason>)`.
- If the Library document is rejected or fails, `Library Info` reports that
  failure instead of rendering document-sourced rows from another reading.

`Library Info` JSON mirrors these rows. The structured output that retains
every enablement state and reason is the Library document itself, through
`library --envelope`.

## Evidence

Release gates in the CLI tests:

- the installed or pinned runtime `System.Net.Sockets.dll` renders the Image,
  Description, and Enabled rows shown above, and no Modified row;
- a reference-pack assembly renders no Enabled row;
- JSON output carries the same values as the Markdown rows;
- the IL-only `System.Runtime.CompilerServices.Unsafe` 6.0.0 package asset keeps
  `Compilation | CoreCLR`, `Architecture | AnyCPU`, `Signed | Yes`,
  `Reproducible | No`, and `File Size | 17.6 KB`;
- `Antlr` 3.5.0.2, which carries no Informational Version, keeps
  `Version | 3.5.0.2` from its Assembly Version;
- the `-v:q` summary shows the same values as `Library Info` and no Modified;
- a native PE asset, such as `runtimes/win-x64/native/capstone.dll` from
  `Gee.External.Capstone` 2.3.0, keeps its current `Library Info` and `-v:q`
  rows, including `Compilation | Native`;
- an undecodable Company attribute renders `unavailable (undecodable-metadata)`;
- legacy rows keep their current values for `System.Text.Json`.

## Non-claims

This owner does not define:

- which sections a verbosity preset shows;
- Library document facts, enablement states, or their JSON shape;
- population sections or the population counts that replace legacy rows;
- Browser Library Overview presentation; or
- package or Platform aggregation of Library facts.
