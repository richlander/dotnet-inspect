---
name: dotnet-inspect-sourcelink
version: 0.1.0
description: Find and fetch the authoritative original source for a .NET API via SourceLink — type-to-URL maps, member file/line locations, and the original source body. Needs a SourceLink-enabled PDB and a network fetch.
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
without fetching the bodies.

```bash
dnx dotnet-inspect -y -- library System.Text.Json -S "Source Files"
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json -S "Source Files"
dnx dotnet-inspect -y -- member Type Method:1 -S "Source Locations"
```

## Fetch the original source

`-S "Original Source"` returns the original source body when SourceLink can
resolve it (also part of the `-S @Source` bundle alongside the decompiled and IL
views).

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S "Original Source"
```

## URL forms

PDBs *carry* SourceLink data; they are not SourceLink themselves. SourceLink URLs
default to raw/fetchable form; add `--blob` for browser URLs. Use `--bare` to
extract a clean URL list for scripting.

```bash
dnx dotnet-inspect -y -- member Type Method:1 -S "Source Locations" --bare
dnx dotnet-inspect -y -- library System.Text.Json -S "Source Files" --blob
```
