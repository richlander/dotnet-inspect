---
name: dotnet-inspect-sourcelink
version: 0.1.0
description: Inspect source mapped by Portable PDB and SourceLink data — map files and member locations, fetch source, or resolve checksum-matched content locally.
---

# dotnet-inspect: SourceLink and PDB source

Use this skill to get source mapped by the Portable PDB. dotnet-inspect
verifies local files and GitHub committed blobs read through `--repo` against
the PDB checksum. A network `PDB Source` fetch also verifies the checksum
and requires the final redirect origin to match before returning the body. Use
`library -S "SourceLink: Integrity"` for opt-in verification of every
fetchable, non-embedded compiler-source document. If no usable PDB or
checksum-matching source is available locally or through SourceLink, use the
always-local `decompiler` skill.

The checksum proves that returned bytes match the PDB's declaration. It does
not independently prove that those bytes are the physical syntax tree that
produced a MethodDef; `PDB Source` names that evidence boundary explicitly.

```bash
dnx dotnet-inspect -y -- <command>
```

## Find where the source lives

`-S "Source Files"` maps types in `type` scope. Library and package scope use
`SourceLink: Files`; `-S "Source Locations"` gives per-member file and line
URLs without fetching bodies. Use `--urls` for a clean URL list and `--paths`
for source path rows.

```bash
dnx dotnet-inspect -y -- library System.Text.Json -S "SourceLink: Files"
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files" --urls
dnx dotnet-inspect -y -- member Type Method:1 -S "Source Locations" --paths
dnx dotnet-inspect -y -- library System.Text.Json --il-offset 0x06000001+0x0
```

## Fetch PDB source

`-S "PDB Source"` returns the source body selected by Portable PDB coordinates,
acquired locally or through SourceLink, and verified against the PDB checksum
(also part of the `-S @Source` bundle alongside the decompiled and IL views).
Use `--print` to fetch the source body behind one printable SourceLink row. When
the section renders multiple rows, add
`--row N|first|last`; `N`
addresses the displayed 1-based row number, while `first` and `last` mean the
rendered endpoints. If that row has no printable document, the command reports
it instead of silently choosing another row.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S "PDB Source"
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json \
  Serialize:1 -S "PDB Source,Source Diff" --repo /path/to/runtime
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files" --print --row 1
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Source Locations" --print --row 1
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files" --print --row 1 --json-array
```

`--repo` requires a fully qualified clone path and applies only to
`raw.githubusercontent.com` SourceLink URLs. Member PDB Source, printable type
Source Files, printable member Source Locations, and implementation-diff PDB
source consult the clone before fetching the source body remotely. Package or
PDB acquisition may still use the network.

## URL forms

PDBs *carry* SourceLink data; they are not SourceLink themselves. SourceLink URL
rows default to raw/fetchable form; add `--blob` for browser URLs. Prefer
`--urls` when you want URL payloads, `--paths` for file paths, and `--print` when
you want the referenced source body. `--bare` remains a raw selected-payload
escape hatch.

```bash
dnx dotnet-inspect -y -- member Type Method:1 -S "Source Locations" --urls --jsonl
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files" --urls --json-array
dnx dotnet-inspect -y -- library System.Text.Json -S "SourceLink: Files" --urls --blob
```

To check *whether* SourceLink is present and usable (rather than fetch source),
see the `signals` skill. Library `Signals` summarizes map usability and
`SourceLink: Diagnostics` reports parse errors or rejected mappings;
`SourceLink: Availability`, `SourceLink: Integrity`, and
`SourceLink: Missing Files` check the mapped documents. The document checks
also aggregate across selected package libraries.
