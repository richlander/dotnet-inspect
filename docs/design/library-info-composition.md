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

> Every `Library Info` field has exactly one named source: a Library document
> fact, host source provenance, or a named legacy owner. A field whose source is
> a Library document fact is rendered from that fact and from no other reading
> of the image.

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
report, so the three hosts cannot disagree. For the installed .NET 11
rc.1.26425.128 Platform Library, rows keep their current values except that
Modified disappears and Enabled appears (excerpt):

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
| Version | Host Platform version when present, otherwise the Informational Version fact without its build metadata |
| Types, Methods | Legacy Metadata table row counts, until a population owner claims them |
| Async Methods, Custom Attributes, Extension Methods, Integrations, Resources, Switches, Type Forwarders, Union Types | Legacy counts of their existing sections, until each population slice |
| Facade | Legacy Platform surface classification |
| Deterministic | Legacy SourceLink and PDB owner |
| Ecosystem Dependencies | Legacy ecosystem recognition owner |
| Modified | Removed; it describes the local copy, not the Library |

`Types` and `Methods` stay table row counts. They are not the public Type
population, and replacing them with a population Count would change their
meaning, not only their source.

A legacy row retires when its owner produces a Library document population or
fact and its section adopts it. The composition never reads the image a second
time for a fact the document already carries.

## Realization

Each CLI route already resolves one assembly image: a direct file, a package
asset, or a Platform pack assembly. The CLI materializes that image as a direct
Library through the assembly-context adapter and requests Image, Description,
and Enablements in one facts-only plan.

The host chooses the adapter role from its own selection, not from the image:

- a package implementation asset or a runtime-pack assembly uses the
  `Implementation` role, so Enablements are judged on it;
- a package reference asset, a reference-pack assembly, or a direct file uses
  the `ApiOnly` role.

Source and Version come from the same host resolution. The direct Library does
not carry the package or Platform coordinate, so provenance is the host's
statement, not a document fact.

## Rendering

- A present text fact renders as its text. A fact the image does not carry
  omits its row, as today.
- An unavailable fact renders its row as `unavailable (<reason>)`, so a
  decoding problem stays visible.
- Enabled lists the labels of Enabled enablements, joined by ` · `. The row is
  omitted when none is Enabled. When the Enablements group failed, the row reads
  `unavailable (<reason>)`.
- If the Library document is rejected or fails, `Library Info` reports that
  failure instead of rendering document-sourced rows from another reading.

## Evidence

Release gates in the CLI tests:

- the installed or pinned runtime `System.Net.Sockets.dll` renders the Image,
  Description, and Enabled rows shown above, and no Modified row;
- a reference-pack assembly renders no Enabled row;
- JSON output carries the same values as the Markdown rows;
- an undecodable Company attribute renders `unavailable (undecodable-metadata)`;
- legacy rows keep their current values for `System.Text.Json`.

## Non-claims

This owner does not define:

- which sections a verbosity preset shows;
- Library document facts, enablement states, or their JSON shape;
- population sections or the population counts that replace legacy rows;
- Browser Library Overview presentation; or
- package or Platform aggregation of Library facts.
