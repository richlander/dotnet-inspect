---
name: dotnet-inspect-compatibility
version: 0.1.0
description: Decide whether a change or upgrade is safe — API-surface diffs, behavioral diffs (allocations, exceptions), feature switches, and version resolution.
---

# dotnet-inspect: compatibility and change analysis

Use this skill to decide whether a change is safe to adopt: what changed between
two versions and what surface a library exposes. The scenario crosses commands —
`diff` for change, `library`/`package` for the surface a single version exposes.

```bash
dnx dotnet-inspect -y -- <command>
```

## Did the API surface change?

`diff` compares a version range from a package, a platform (in-box) library, or
two local builds. Pick the lens for the question you are answering:

```bash
dnx dotnet-inspect -y -- diff --package System.Text.Json@9.0.0..10.0.0 --breaking
dnx dotnet-inspect -y -- diff --platform System.Runtime@9.0.0..10.0.0 --additive
dnx dotnet-inspect -y -- diff --library old/Foo.dll..new/Foo.dll --changed
```

`--breaking` for migration work, `--additive` for release notes, `--changed`
for in-place member changes, `--name-only` for a quick list. Narrow with
`-t TypeName`; widen with `--all`.

## Did runtime behavior change? (allocations, exceptions)

`-S "Analysis Diff"` compares body-level signal *deltas* between the two
versions, not just the API shape. Rows are `Member | Signal | Old | New |
Delta`, where `Signal` covers `allocations`, `copies`, `reflection`, `throws`,
`catches`, `finallys`, `unsafe`, `constructed-exceptions`, and `optimization`
shapes. This is how you catch an allocation regression or a change in exception
coverage across versions. (For what these signals mean on a single version, see
the `performance` and `correctness` skills.)

```bash
dnx dotnet-inspect -y -- diff --package Foo@1.0.0..2.0.0 -S "Analysis Diff"
dnx dotnet-inspect -y -- diff --library old/Foo.dll..new/Foo.dll -S "Analysis Diff" --changed
```

## Did the implementation change? (C# + IL)

`-S "Implementation Diff"` selects Research-composed body evidence instead of
the default API compatibility view. Rows identify the member, producer (`C#` or
`IL`), change kind, and producer-owned evidence. Narrow with `-t` and `-m`; use
`--table`, `--tsv`, or `--jsonl` for columnar output.

```bash
dnx dotnet-inspect -y -- diff --library old/Foo.dll..new/Foo.dll -S "Implementation Diff" -t MyType -m HotPath
```

Treat these rows as implementation evidence, not semantic-equivalence proof.

## What can be configured? (feature switches)

`-S Switches` (alias `-S @Switches`) on `library` or `package --library` reports
the behavior and trim/AOT knobs: `[FeatureSwitchDefinition]`s, runtime host
configuration options, and `AppContext` switches.

```bash
dnx dotnet-inspect -y -- library System.Text.Json -S Switches
```

## Which versions to compare

Version resolution is cache-first (local cache in milliseconds; nuget.org
~1–4s). Use `Foo --version` for the cached version a bare inspection will use,
`Foo --latest-version` for the newest on nuget.org, and `Foo --versions [N]`
(add `--preview`) to list published versions. Pin with `@`: `Foo@9.0.0`,
`Foo@latest`.

For caller-driven onset or bisect work, resolve an inclusive addressable vector,
then probe only the cells you choose:

```bash
dnx dotnet-inspect -y -- package Foo@1.0.0..2.0.0 --versions
dnx dotnet-inspect -y -- type TargetType --package Foo@1.0.0..2.0.0 --at '#5'
dnx dotnet-inspect -y -- member TargetType TargetMember --package Foo@1.0.0..2.0.0 --at 1.6.0
```

`--at` accepts an exact version, one-based `#N`, `first`, or `last`. Vector
resolution does not download every package; only the selected probe is
acquired. The agent owns the search policy and bound. For recurrence-safe
current onset, walk backward from the bad version until the first successful
absence; use binary search only for a predicate known to be monotonic.
