# All-frameworks package discovery

## Status

Adopted under [#9833](https://github.com/richlander/dotnet-inspect/issues/9833),
part of [#9240](https://github.com/richlander/dotnet-inspect/issues/9240).
This document is the normative owner of `--tfm all`. It supersedes the
document-only rule from #3885 and the aggregate Table contract from #9904.

## Authority and exact claim

> `--tfm all` lists a package's files across every framework. Only the
> `package` command accepts it, and only for the Files sections. It consumes
> the package's archive entries, selects no target framework, and never reads
> an assembly image or a PDB.

Inspection across several frameworks has no single meaningful answer, so this
owner never selects one Library or one slice from several frameworks. The file
listing is the one view whose rows are already independent of a selected
framework, so it is the only view `--tfm all` admits. Every other request fails
before acquisition.

## Admission

| Request with `--tfm all` | Result |
| --- | --- |
| `package <id>` with `-S Files`, `--files`, or `--path`, or with only other `@Files` family sections (`Nuspec`, `README`, `Licenses`, `Skills`) | Every admitted entry with no Target predicate, under [Package file inventory](package-file-inventory.md#selected-entry-set). |
| `package <id>` with no section, any other section, a category whose expansion contains another section, or `--versions` | Fails before acquisition: `--tfm all applies only to -S Files (or --files).` |
| `package <id> --library [L]` | Fails before acquisition: `--library cannot be combined with --tfm all, which is package discovery.` |
| `package query` | Fails before acquisition, because it evaluates assemblies. |
| `library`, `type`, `member`, `find`, `extensions`, `diff`, `depends`, `workspace`, or any other command with a package target | Fails before acquisition and names `package <id> --tfm all`. |
| Any command with a non-package target, such as `--platform`, a local file, or a project | Fails before acquisition and states that `--tfm all` applies only to `package`. |

An admitted request does not run the package-wide binary-signal scan, because
no Files row consumes it.

Failures are ordinary visible errors with exit code 1. No route treats `all`
as a framework spelling or falls back to the highest framework.

## Non-claims

This owner does not change compile selection, its available-slice inventory,
or the Files inventory itself. It defines no all-framework Library view, Package
Info subset, or dependency projection. It does not change `--tfm <TFM>` for a
single framework.

## Adoption

The first CLI adoption slice admitted `--tfm all` only on `package`: one shared
check fails every other command and non-package target, including
`package query`, before acquisition, and both `package --library` routes fail
before acquisition. It removed the IL-reading `library --tfm all` paths.

The second slice narrows `package` to the Files sections. The package command
checks the resolved section set before acquisition, skips the binary-signal
scan for admitted requests, and gates both outcomes in
`CommandExecutionTests.TfmAll`. It retires the planned per-section admission
table and all-framework discovery projection.
