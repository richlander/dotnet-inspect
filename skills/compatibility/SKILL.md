---
name: dotnet-inspect-compatibility
version: 0.1.0
description: Compare .NET library versions for API breaks, implementation changes and static analysis shifts; follow exact API correspondence and history without treating comparisons as safe-upgrade verdicts.
---

# dotnet-inspect: Compare .NET APIs and Implementations Across Versions

Use this skill to establish what changed, then judge the consequences.
Run `dnx dotnet-inspect -y -- <command>`. Skills provide productive starts;
installed explanation supplies detailed analyses, bindings and limits.

## Compare the public API

```bash
dnx dotnet-inspect -y -- diff \
  --package System.Text.Json@9.0.0..10.0.0 --breaking
dnx dotnet-inspect -y -- diff \
  --platform System.Runtime@9.0.0..10.0.0 --additive
dnx dotnet-inspect -y -- diff --library old/Foo.dll..new/Foo.dll -S Changes
```

`--breaking` selects assessed breaks; `--additive` selects additions.
Use `-S Changes` for the complete API change population, including unclassified
changes. Unclassified does not mean safe or breaking. Narrow with `-t Type`;
use `--all` for non-public, hidden and obsolete declarations.

Single-Library API comparisons require compatible assembly identities.
Incomplete or rejected comparisons are **not compared**, not "no changes."
Read completion, diagnostics and exit status. Unprojected `--json` gives the
typed comparison outcome; where supported, `--envelope` adds Share and
diagnostics. Sectioned/filtered JSON can use a presentation schema instead.

## Follow one API across versions

```bash
dnx dotnet-inspect -y -- member System.Text.Json.JsonSerializer Deserialize:1 \
  --package System.Text.Json@9.0.0..10.0.0 --match
```

Subject-owned `--match` follows API correspondence from the source coordinate.
Resolve the selector only at the source: the same overload ordinal on the
destination is not identity. This matches declarations, not accessor bodies.
Root `match` instead compares implementation bodies or discovers similar
members; similar code is not API correspondence or semantic equivalence.

## Look beyond API shape

```bash
dnx dotnet-inspect -y -- diff --package Foo@1.0.0..2.0.0 \
  -S "Analysis Diff"
dnx dotnet-inspect -y -- diff --library old/Foo.dll..new/Foo.dll \
  --implementation -t MyType -m HotPath
dnx dotnet-inspect -y -- explain "compare string literals"
```

Analysis Diff surfaces allocation, unsafe, exception and other static shifts.
Implementation Diff compares reconstructed C#, IL and explicitly requested
PDB Source. Changed literals can identify endpoint changes, not actual network
traffic. Read the returned analysis and CLI binding for required predicates
before executing an unfamiliar analysis; do not guess the filter grammar.

Decompiled C# is not authored source. PDB Source is checksum-matched to the
Portable PDB; source absence is not deletion. Neither identical IL nor similar
source proves behavioral equivalence. Load `skill decompiler` or
`skill sourcelink` when fidelity or provenance is material.

## Choose versions and locate a boundary

Use `package Foo --versions -n 1` for one newest listed version row,
`package Foo@latest --versions` for a fresh check, or an exact `Foo@version`
pin for reproducibility. `--preview` opts into prereleases.
Load `skill private-feeds` when source authority matters.

```bash
dnx dotnet-inspect -y -- package Foo@1.0.0..2.0.0 --versions
dnx dotnet-inspect -y -- diff --history --package Foo@1.0.0..2.0.0 \
  --type Foo.Parser --members --major-versions
dnx dotnet-inspect -y -- diff --history --package Foo@1.0.0..2.0.0 \
  -t Foo.Parser -m Parse --finding analysis.allocation --at first --at last
```

History can follow API presence, members, attributes and body Findings.
`--at` selects an exact version, one-based `#N`, `first` or `last`.
Checkpoint transitions establish changes between probes, not the exact
introduction version. Omitting checkpoints may evaluate the full population;
choose an explicit bound before expensive history work.

For recurrence-safe onset, walk backward from the bad version to a successful
absence. Binary search needs a known monotonic predicate. Preserve the same
source, visibility and framework across probes; listed-only vectors can differ
from an `--include-unlisted` listing.

Confirm a candidate adjacent boundary with the native Finding comparison:

```bash
dnx dotnet-inspect -y -- diff --package Foo@1.4.0..1.5.0 \
  -t Foo.Parser -m Parse --finding analysis.allocation
```

`PairFinding.Added` with `Old=absent` and `New=present` establishes an added
occurrence. `Present`, `Removed` and `Changed` remain different outcomes;
aggregate count deltas do not establish onset. Use installed analysis discovery
for other Finding families and their required target grain.

Report assessed breaks, other API changes, implementation observations and
unresolved runtime questions separately. A sparse history or failed comparison
must not become an all-versions compatibility claim.
