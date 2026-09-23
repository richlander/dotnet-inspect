---
name: dotnet-inspect-sourcelink
version: 0.1.0
description: Inspect source mapped by Portable PDB and SourceLink data — map files and member locations, fetch source, or resolve checksum-matched content locally.
---

# dotnet-inspect: SourceLink and PDB source

Use this skill to get source mapped by the Portable PDB. dotnet-inspect
verifies local files and GitHub committed blobs read through `--repo` against
the PDB checksum. A network `PDB Source` fetch follows redirects permitted by
the host transport and returns the body only when it matches that checksum; it
does not treat the final destination as source provenance. Use
`library -S "SourceLink: Integrity"` for opt-in verification that every
fetchable, non-embedded compiler-source document preserves its attributed
repository and revision through redirects. If no usable PDB or
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
dnx dotnet-inspect -y -- package Newtonsoft.Json@13.0.3 \
  -S "SourceLink: Files" -t JsonReader -n 1 --tail --urls
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files" --urls
dnx dotnet-inspect -y -- member Type Method:1 -S "Source Locations" --paths
dnx dotnet-inspect -y -- library coordinate 0x06000001+0x0 \
  --package System.Text.Json --library System.Text.Json.dll
```

For one package with exactly `SourceLink: Files` selected, `-n`, `--tail`, and
`--rows A..B` select complete library/type/URL rows after every selected
library has completed SourceLink collection and the optional `--type` filter
has run. Count, structured formats, `--urls`, and `--raw` consume those same
rows. Use `--lines` only when you intentionally want to clip rendered text.
The multi-section `@SourceLink` document is not one file-row sequence.

## Fetch PDB source

For an authored-first result rather than one provider, select `Source` on one
exact Type or Member. It prefers verified authored source, may use the shared
decompiled fallback, and retains provider and fallback context. `PDB Source`
below remains the provider-specific view and never substitutes decompiled text.

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

## Inspect or print authored member parts

Focused member `-S "Source Locations" --json` returns `member`, `document`,
and `pdb_span`, without acquiring source text. The PDB span is executable
source, not a complete member boundary. Add `--source-parts` to acquire and
verify source, then discover lexical `parts` separately from that span.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json \
  Serialize:1 --source-parts --json
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json \
  Serialize:1 --print --part xml-docs
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json \
  Serialize:1 --print --part body --markdown
```

Both gestures imply Source Locations unless `-S` is explicit. Select one member
such as `Serialize:1`, or use `--print --part` with `--row` to select a member
row. Part names are `member`, `xml-docs`, `attributes`, `signature`, and `body`.
The full member includes attached XML documentation and attributes; a body
includes its delimiters. Unavailable parts fail rather than selecting a
substitute. Multiple documentation or attribute fragments are joined with LF;
selection uses exact character spans, not whole-line slicing.

Discovery supports Markdown, plaintext, and `--json`. Printing supports
ordinary text, explicit `--markdown` framing, and `--json`, `--jsonl`, or
`--json-array`; JSON preserves the selected source characters. Rendered CLI
text restores each fragment's original first-line indentation and follows
normal LF and text-containment rules.
Unqualified `--print` still prints the whole file.
`xml-docs` selects raw source comments, not parsed DocumentationHouse content.
These lexical ranges do not strengthen the PDB/checksum provenance claim.

## URL forms

PDBs *carry* SourceLink data; they are not SourceLink themselves. SourceLink URL
rows default to raw/fetchable form; add `--prefer-rendered-urls` to prefer
browser views when supported, leaving other URLs unchanged. Prefer
`--urls` when you want URL payloads, `--paths` for file paths, and `--print` when
you want the referenced source body. `--raw` remains an undecorated
selected-payload escape hatch.

```bash
dnx dotnet-inspect -y -- member Type Method:1 -S "Source Locations" --urls --jsonl
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files" --urls --json-array
dnx dotnet-inspect -y -- library System.Text.Json -S "SourceLink: Files" --urls --prefer-rendered-urls
```

To check *whether* SourceLink is present and usable (rather than fetch source),
see the `signals` skill. Library `Signals` summarizes map usability and
`SourceLink: Diagnostics` reports parse errors or rejected mappings;
`SourceLink: Availability`, `SourceLink: Integrity`, and
`SourceLink: Missing Files` check the mapped documents. The document checks
also aggregate across selected package libraries.
