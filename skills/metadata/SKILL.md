---
name: dotnet-inspect-metadata
version: 0.1.0
description: Query raw ECMA-335 assembly metadata — discover table and heap sections, inspect decoded rows and handles, address table or heap coordinates, and project bounded machine-readable results.
---

# dotnet-inspect: raw assembly metadata

Use this skill when API views are too high-level and you need the assembly's
actual ECMA-335 tables, handles, flags, or heap values. The metadata lens
inspects one assembly image and never loads the inspected assembly.

```bash
dnx dotnet-inspect -y -- <command>
```

## Discover before reading

Metadata tables are unbounded and explicit-only: no verbosity or base category
renders them automatically. Structural discovery lists the authored metadata
family without running its producers; add `--effective` to identify the tables
and heaps that have data for this image:

```bash
dnx dotnet-inspect -y -- library MyLib.dll -D @Metadata
dnx dotnet-inspect -y -- library MyLib.dll -D @Metadata --effective
dnx dotnet-inspect -y -- library MyLib.dll -S @Metadata --count
dnx dotnet-inspect -y -- library MyLib.dll -D "Metadata: TypeDef" --effective
```

`Metadata: Image` reports image-level facts. Table sections such as
`Metadata: TypeDef`, `Metadata: MethodDef`, and `Metadata: MemberRef` decode
handles, ranges, heap values, and flags instead of dumping raw integers.

## Select a ReadyToRun manifest

CLI metadata is the default root. ReadyToRun images can carry a separate
manifest root; select it explicitly rather than interpreting its addresses
against CLI metadata:

```bash
dnx dotnet-inspect -y -- library System.Private.CoreLib \
  -S "Metadata: ReadyToRun"
dnx dotnet-inspect -y -- library System.Private.CoreLib \
  --metadata-root r2r-manifest
dnx dotnet-inspect -y -- library System.Private.CoreLib \
  --metadata-root r2r-manifest -S "Metadata: TypeRef" --jsonl
dnx dotnet-inspect -y -- library System.Private.CoreLib \
  --metadata-root r2r-manifest -D @Metadata --effective
```

`Metadata: ReadyToRun` reports envelope facts, not native instructions.
An explicit root with no section selection opens `Metadata: Image`, including
requested root, canonical root, RVA, and size. An exact CLI alias is one
physical root with manifest-request provenance. Table and heap addresses stay
relative to the selected root, including `--heap`, `--rows`, and `--count`.
An absent or malformed requested manifest fails; it never falls back to CLI
metadata. Use `--metadata-root cli` to request the CLI root explicitly.
Use `--jsonl` or `--tsv` for structured rows; `--json` is supported for Count
and discovery, not root-selected or ReadyToRun row output.

## Query one table

The `#` column is the real metadata row id. `--rows` therefore addresses table
rows by displayed, 1-based metadata position rather than by the current page.
Ranges are inclusive, so `100..199` selects 100 rows:

```bash
dnx dotnet-inspect -y -- library MyLib.dll \
  -S "Metadata: MethodDef" --rows 100..199 --tsv
dnx dotnet-inspect -y -- library MyLib.dll \
  -S "Metadata: TypeRef" --columns "Name,Namespace,ResolutionScope" --jsonl
```

Table indices are accepted as input aliases when following a token:

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Metadata: 0x02"
```

That is the `TypeDef` table. A full token such as `0x02000015` addresses a row,
not a table, and is rejected by `-S`.

## Inspect heaps

The heap sections are `Metadata: #Strings`, `Metadata: #Blob`,
`Metadata: #GUID`, and `Metadata: #US`. Use `--heap` for one exact coordinate:

```bash
dnx dotnet-inspect -y -- library MyLib.dll \
  -S "Metadata: #Strings" --rows 20
dnx dotnet-inspect -y -- library MyLib.dll --heap "#Strings:0x1a4"
```

Heap addresses are decimal unless prefixed with `0x`. String and blob listings
contain distinct values referenced by projected table rows, not a claim of
complete heap enumeration; each listing reports its coverage.

Selecting `@Metadata` for a package that resolves to several assemblies is
ambiguous and fails. Choose one DLL with `library`, or use
`package Foo --library` only when the package has one unambiguous library.
