# Installing TLA+ and Java

This runbook records how to install the TLA+ tools (`tla2tools.jar`) and their
Java prerequisite on each OS a contributor might use to build or check the
small TLA+ models referenced by
[`AGENTS.md`](../../AGENTS.md#keep-specifications-readable-model-interactions).
It does not cover the Toolbox GUI or the VS Code extension; this repository
uses the command-line tools only.

## Requirements

Running the TLA+ tools needs exactly two things:

1. **Java 11 or later** on `PATH` as `java` (per
   [`USE.md`](https://github.com/tlaplus/tlaplus/blob/master/USE.md)). Any
   distribution satisfies this; see [Java prerequisite](#java-prerequisite).
2. **`tla2tools.jar`** — the single jar containing SANY (parser), TLC (model
   checker), the PlusCal translator, and TLATeX.

General preference is to install from each OS's package manager rather than
downloading raw binaries, and that applies to Java below. It does **not**
apply to `tla2tools.jar`: no mainstream package manager (`apt`, `brew`,
`winget`, `choco`, `scoop`) carries the CLI jar. Homebrew's only TLA+ artifact
is the `tla+-toolbox` cask — the GUI IDE, not the CLI tools, and macOS-only —
so it does not give a cross-platform, scriptable install and is not used here.
Pin and download `tla2tools.jar` directly from the project's GitHub releases
on every OS instead.

## Pin one TLA+ version across every OS

Use the same `tla2tools.jar` release on every machine so model-checking
results are reproducible across contributors and OSes. As of this writing the
pinned release is:

- **Version:** `v1.8.0` ("The Clarke" release) — currently a GitHub
  **prerelease**, not the "latest" stable tag. Agents in this repository are
  already standardized on it (confirmed TLC build
  `2026.08.21.155922`, rev `9787e65`), so pin to it rather than the older
  stable `v1.7.4` to stay consistent with work already done.
- **Source:** <https://github.com/tlaplus/tlaplus/releases/tag/v1.8.0>
- **Asset:** `tla2tools.jar`

(For reference, Homebrew's `tla+-toolbox` cask still tracks the older stable
`1.7.4` — it does not pick up prereleases. That is another reason it is not
the install path used here, on top of being macOS-only and installing the GUI
app rather than the jar.)

Because `v1.8.0` is a prerelease, `.../releases/latest` (used by both the
GitHub UI default view and tools that resolve "latest") will not surface it —
always fetch the exact tagged asset URL below, never a "latest" alias:

```bash
curl -fLO https://github.com/tlaplus/tlaplus/releases/download/v1.8.0/tla2tools.jar
```

Put `tla2tools.jar` somewhere stable (for example `~/.local/share/tlaplus/` on
Linux/macOS or `%LOCALAPPDATA%\tlaplus\` on Windows) and reference it by full
path or via `CLASSPATH`/`java -jar`, rather than copying it per-worktree. When
the pinned version changes, update this file and re-download on every OS.

## Java prerequisite

The TLA+ tools require **Java 11 or later** (per
[`USE.md`](https://github.com/tlaplus/tlaplus/blob/master/USE.md)). Modern
OpenJDK distributions no longer ship a separate "JRE-only" package upstream —
the JDK includes everything needed to run `java`. Where an OS package manager
still offers a JRE-only package (Debian/Ubuntu does), prefer it for a
runtime-only install; otherwise install the JDK and use it purely to run
`java`.

### Pinned Java version: OpenJDK 25

Ubuntu 26.04 LTS's `default-jre`/`default-jdk` metapackages resolve to
**OpenJDK 25** (its bundled Java LTS). Since the package-manager install is
already pinned by the distro, standardize every OS on the same major version
— **25** — via each OS's own package manager, rather than letting each one
pick an independent default.

### Linux (Ubuntu 26.04)

```bash
sudo apt-get install default-jre   # resolves to OpenJDK 25 on 26.04
java -version                      # confirm: openjdk version "25...."
```

Use the explicit `openjdk-25-jre` package instead if you need the pin to
survive a future Ubuntu release changing what `default-jre` resolves to.

### macOS

Homebrew does not package a standalone JRE; install the versioned OpenJDK
**formula** (not the `temurin` **cask**, which is a full JDK `.pkg`
installer) and symlink it so the system `java` wrapper finds it — mirroring
the Ubuntu package-manager install above:

```bash
brew install openjdk@25
sudo ln -sfn "$(brew --prefix openjdk@25)/libexec/openjdk.jdk" \
  /Library/Java/JavaVirtualMachines/openjdk-25.jdk
java -version
```

`openjdk@25` is keg-only (Homebrew does not symlink versioned formulae into
`/opt/homebrew`), which is why the explicit symlink step is required —
without it, `java` will report "Unable to locate a Java Runtime" even after
`brew install`.

### Windows

There is no distinct "JRE-only" Windows package from Eclipse Temurin either;
install the versioned Temurin JDK, which provides `java` on `PATH`:

```powershell
winget install --id EclipseAdoptium.Temurin.25.JDK -e
```

Or use Chocolatey: `choco install temurin25`.

### Fedora/RHEL

```bash
sudo dnf install java-25-openjdk-headless
```

## Running the tools

After installing Java and downloading `tla2tools.jar`, verify with:

```bash
java -jar /path/to/tla2tools.jar -help   # aliases to tlc2.TLC
```

See `USE.md` in the TLA+ repository for the full command list (`tla2sany.SANY`,
`tlc2.TLC`, `tlc2.REPL`, `pcal.trans`, `tla2tex.TLA`).

## Inventory: TLA+ usage in this repository

TLA+ is used per
[`AGENTS.md`](../../AGENTS.md#keep-specifications-readable-model-interactions)
to check stateful/concurrent interaction designs that are hard to reason about
in prose alone. All current models are on open, unmerged design PRs (not yet
on `main`); links point at the PR branch.

| Model(s) | PR | Description |
| --- | --- | --- |
| [`CompileBackAdmission.tla`](https://github.com/richlander/dotnet-inspect/blob/docs/4810-compileback-closure-provenance/docs/models/CompileBackAdmission.tla) ([`.cfg`](https://github.com/richlander/dotnet-inspect/blob/docs/4810-compileback-closure-provenance/docs/models/CompileBackAdmission.cfg)) | [#4838](https://github.com/richlander/dotnet-inspect/pull/4838) | Models the compile-back tool's planning → product-attempt → legacy-attempt → supersession → receipt/verdict transitions for [C# member recompilation](../design/csharp-member-recompilation.md). Checks termination, that `Exact` verdicts always trace to an admitted receipt, that a failed product attempt never crosses into legacy evidence, and that supersession clears the prior receipt. |
| [`NavigationSession.tla`](https://github.com/richlander/dotnet-inspect/blob/design/inspection-subject-navigation/docs/design/models/inspection-subject-navigation/NavigationSession.tla), [`AtomicRestoration.tla`](https://github.com/richlander/dotnet-inspect/blob/design/inspection-subject-navigation/docs/design/models/inspection-subject-navigation/AtomicRestoration.tla), [`SnapshotAuthority.tla`](https://github.com/richlander/dotnet-inspect/blob/design/inspection-subject-navigation/docs/design/models/inspection-subject-navigation/SnapshotAuthority.tla) (plus matching `.cfg` files and a model [`README.md`](https://github.com/richlander/dotnet-inspect/blob/design/inspection-subject-navigation/docs/design/models/inspection-subject-navigation/README.md)) | [#4830](https://github.com/richlander/dotnet-inspect/pull/4830) | Three independent models backing [Inspection subject navigation](../design/inspection-subject-navigation.md): retained-session intent/supersession/maintenance ordering and effect authority (`NavigationSession`), one-transaction canonical subject+lens restoration (`AtomicRestoration`), and retained-versus-stateless execution and which prior state each may read (`SnapshotAuthority`). Each is scoped to one mechanism; none models identity ranking, availability semantics, UI accessibility, or implementation conformance. |
| [`SemanticRowSelection.tla`](https://github.com/richlander/dotnet-inspect/blob/docs/4677-selection-planning/docs/models/SemanticRowSelection.tla) ([`.cfg`](https://github.com/richlander/dotnet-inspect/blob/docs/4677-selection-planning/docs/models/SemanticRowSelection.cfg)) | [#4815](https://github.com/richlander/dotnet-inspect/pull/4815) | Models one immutable row-selection plan applied to ordered named sequences for [Semantic row selection](../design/semantic-row-selection.md): sequence-major/stage-major traversal, strict `Range`, positional `Head`/`Tail`, ranked `Top`, resolver caching, callback failure, withheld publication, and atomic success. Checks type safety, atomic publication, at-most-once resolver invocation, sequence/stage failure precedence, and termination under weak fairness. |

Each PR records its own TLC run (states generated, distinct states, max
depth) inline next to its model description. [#4815](https://github.com/richlander/dotnet-inspect/pull/4815)
records a run against the pinned `v1.8.0` prerelease `tla2tools.jar` exactly
(TLC `2026.08.21.155922`, rev `9787e65`); the other two PRs' recorded TLC
versions predate that pin and were not re-verified against it as part of
writing this runbook.

### Known in-progress work, not yet checked in

As of this writing, two additional models exist only as uncommitted files in
a contributor's local worktrees and are not linked above because they have no
pushed branch or PR to point at:

- `PackageCachePublication.tla` (plus `Safety`/`Liveness`/`BrokenAtomic`/
  `BrokenEviction` configs and a README), for
  [`cache-concurrency.md`](../design/cache-concurrency.md).
- `TsJsExportLifecycle.tla` (plus several lifecycle-scenario configs and a
  README), for [`ts-jsexport.md`](../design/ts-jsexport.md).

Update this table once either is committed and pushed.

## Check in models as you go

A TLA+ model that exists only as uncommitted files in a local worktree is not
a checked-in asset — it is not backed up, not reviewable, and invisible to
every other contributor and agent until it is committed and pushed. The two
models above are exactly this risk: real, apparently-finished work (model +
configs + README) sitting only on one machine's disk.

Commit a model to its branch and push that branch as soon as it reaches a
checkable state (parses, and TLC runs against its `.cfg` without unexpected
errors), the same way any other source file is checked in during ordinary
work — do not wait for the owning design PR to be otherwise complete. Treat an
uncommitted `.tla`/`.cfg`/model-`README.md` set sitting in a worktree as a bug
to fix, not a stable resting state.
