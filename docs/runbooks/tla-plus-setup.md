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

## Curated TLA+ examples

TLA+ is used per
[`AGENTS.md`](../../AGENTS.md#keep-specifications-readable-model-interactions)
to check stateful or concurrent interactions that are hard to reason about in
prose alone.

Keep each model in its own directory, normally under `docs/models/` or the
owning design's `models/` directory. Store the `.tla` module, its `.cfg`
configurations, model `README.md`, and local exclusions for generated TLC
artifacts together; do not place standalone model files in the parent models
directory or combine unrelated models in one directory.

The following table is a user-curated set of at most six merged examples, not
an inventory of repository models. It is intentionally incomplete.
Contributors and agents must not add a new model to this list as part of normal
model work; only the user may add, remove, or replace a curated example.

| Example | What it demonstrates |
| --- | --- |
| [`TsJsExportLifecycle.tla`](../design/models/ts-jsexport-lifecycle/TsJsExportLifecycle.tla) (plus scenario and mutation configurations and a model [`README.md`](../design/models/ts-jsexport-lifecycle/README.md)) | Models two generated `ts-jsexport` facades, multiple callers, one shared SDK runtime, shared-in-flight and serialized coordination, local failure isolation, terminal state, and bounded realm restart. Demonstrates separate success/failure scenarios and targeted counterexample mutations without claiming implementation or browser conformance. |
| [`ArtifactSessionAdmission.tla`](../models/artifact-session-admission/ArtifactSessionAdmission.tla) ([`.cfg`](../models/artifact-session-admission/ArtifactSessionAdmission.cfg)) | Models `ArtifactSetSession` admission for [Artifact acquisition and workspace composition](../design/artifact-acquisition-and-workspaces.md#artifactsetsession). Demonstrates single-flight admission, incompatible-generation exclusion, voluntary and disposal-forced draining, late-result suppression, guard witnesses, and weak-fairness progress. |
| [`AssemblyContextGroupLifecycle.tla`](../models/assembly-context-group-lifecycle/AssemblyContextGroupLifecycle.tla) (plus safety, liveness, and mutation configurations and a model [`README.md`](../models/assembly-context-group-lifecycle/README.md)) | Models the existing `AssemblyContextGroup` callback, image-budget, result-publication, finalization, disposal, and quiescent-release lifecycle for [Inspection space](../inspection-space.md). Demonstrates same-participant contention, ordinary versus one-shot release, exceptional retry, resource ordering, and independent counterexample mutations. |

## Check in models as you go

A TLA+ model that exists only as uncommitted files in a local worktree is not
a checked-in asset — it is not backed up, not reviewable, and invisible to
every other contributor and agent until it is committed and pushed. This
guidance exists because real, apparently-finished model sets have previously
sat only on one machine.

Commit a model to its branch and push that branch as soon as it reaches a
checkable state (parses, and TLC runs against its `.cfg` without unexpected
errors), the same way any other source file is checked in during ordinary
work — do not wait for the owning design PR to be otherwise complete. Treat an
uncommitted `.tla`/`.cfg`/model-`README.md` set sitting in a worktree as a bug
to fix, not a stable resting state.
