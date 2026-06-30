---
name: dotnet-inspect-sourcelink
version: 0.1.0
description: Find and fetch the authoritative original source via SourceLink — type-to-URL maps, member file/line locations, and the original source body. Needs a SourceLink-enabled PDB and a network fetch, so it can be unavailable.
---

# dotnet-inspect: SourceLink and original source

Use this skill to get the authoritative original source as written by the
author. It resolves through SourceLink data in the PDB and fetches over the
network, so it can be unavailable (no PDB, no SourceLink, private repo). When it
is unavailable, the `decompiler` skill is the always-local fallback.

```bash
dnx dotnet-inspect -y -- <command>
```

## Find where the source lives

`-S "Source Files"` maps types to their SourceLink URLs (on `type`, `library`,
or `package`); `-S "Source Locations"` gives per-member file and line URLs
without fetching the bodies. Use `--urls` for a clean URL list and `--paths` for
source path rows.

```bash
dnx dotnet-inspect -y -- library System.Text.Json -S "Source Files"
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files" --urls
dnx dotnet-inspect -y -- member Type Method:1 -S "Source Locations" --paths
dnx dotnet-inspect -y -- library System.Text.Json -S "IL Offset:0x06000001+0x0"
```

## Fetch the original source

`-S "Original Source"` returns the original source body when SourceLink can
resolve it (also part of the `-S @Source` bundle alongside the decompiled and IL
views). Use `--print` to fetch the source body behind one printable SourceLink
row; add `--row N` when the selected section has multiple printable rows.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S "Original Source"
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files" --print --row 1
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Source Locations" --print --row 1
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files" --print-all --json-array
```

## URL forms

PDBs *carry* SourceLink data; they are not SourceLink themselves. SourceLink URL
rows default to raw/fetchable form; add `--blob` for browser URLs. Prefer
`--urls` when you want URL payloads, `--paths` for file paths, and `--print` when
you want the referenced source body. `--bare` remains a raw selected-payload
escape hatch.

```bash
dnx dotnet-inspect -y -- member Type Method:1 -S "Source Locations" --urls --jsonl
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files" --urls --json-array
dnx dotnet-inspect -y -- library System.Text.Json -S "Source Files" --urls --blob
```

To check *whether* SourceLink is present and valid (rather than fetch source),
see the `signals` skill (`SourceLink Availability`/`Integrity`/`Missing Files`).
