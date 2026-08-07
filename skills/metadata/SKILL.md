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

Metadata tables are unbounded and explicit-only: no verbosity and not even
`-S @All` renders them automatically. Discover the tables present in the image,
then count or select one:

```bash
dnx dotnet-inspect -y -- library MyLib.dll -D @Metadata
dnx dotnet-inspect -y -- library MyLib.dll -S @Metadata --count
dnx dotnet-inspect -y -- library MyLib.dll -D "Metadata: TypeDef"
```

`Metadata: Image` reports image-level facts. Table sections such as
`Metadata: TypeDef`, `Metadata: MethodDef`, and `Metadata: MemberRef` decode
handles, ranges, heap values, and flags instead of dumping raw integers.

## Query one table

The `#` column is the real metadata row id. `--rows` therefore addresses table
rows by displayed metadata position, including ranges, rather than by the
current page:

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
