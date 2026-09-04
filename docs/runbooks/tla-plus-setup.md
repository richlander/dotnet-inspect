# Installing TLA+ and Java

This runbook records how to install the TLA+ tools (`tla2tools.jar`) and their
Java prerequisite on each OS a contributor might use to build or check the
small TLA+ models referenced by
[`docs/design-scope.md`](../design-scope.md#keep-specifications-readable-model-interactions).
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
Pin and download `tla2tools.jar` from the immutable mirror release named below
on every OS instead.

## Pin one TLA+ version across every OS

Use the same `tla2tools.jar` release on every machine so model-checking
results are reproducible across contributors and OSes. As of this writing the
pinned release is:

- **Upstream version:** `v1.8.0` ("The Clarke" release), currently published
  as a rolling GitHub prerelease channel rather than an immutable release.
- **TLC build:** `2026.08.11.125311`
- **Upstream source:** <https://github.com/tlaplus/tlaplus/releases/tag/v1.8.0>
- **Immutable distribution:**
  <https://github.com/zactionsz/tla-tools/releases/tag/tla2tools-2026.08.11.125311>
- **Asset:** `tla2tools.jar`
- **SHA-256:** `ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`

The upstream `v1.8.0` channel continuously replaces its asset and deletes the
previous bytes, so its tag-relative URL is not a durable pin. The mirror
preserves the upstream build under a build-specific tag and records the
original upstream asset identity and digest. It is a distribution point, not a
trust root: CI verifies the repository-pinned SHA-256 on every run, including
cache hits. Update the build-specific URL, cache key, and digest together when
the pin changes.

Model READMEs retain the tool hashes and build identities used for their
recorded historical runs; updating the repository's current pin does not
rewrite that evidence.

(For reference, Homebrew's `tla+-toolbox` cask still tracks the older stable
`1.7.4` — it does not pick up prereleases. That is another reason it is not
the install path used here, on top of being macOS-only and installing the GUI
app rather than the jar.)

Always fetch the exact build-specific mirror URL, never the rolling upstream
tag or a `latest` alias:

```bash
curl -fLO https://github.com/zactionsz/tla-tools/releases/download/tla2tools-2026.08.11.125311/tla2tools.jar
echo "ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f  tla2tools.jar" \
  | sha256sum -c -
```

Put `tla2tools.jar` somewhere stable (for example `~/.local/share/tlaplus/` on
Linux/macOS or `%LOCALAPPDATA%\tlaplus\` on Windows) and reference it by full
path or via `CLASSPATH`/`java -jar`, rather than copying it per-worktree. When
the pinned version changes, update this file and re-download on every OS.
On macOS, use `shasum -a 256 -c -` in place of `sha256sum -c -`. On Windows,
compare `(Get-FileHash .\tla2tools.jar -Algorithm SHA256).Hash` with the pinned
digest before using the jar.

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

See [TLA+ methodology](../tla-plus-methodology.md) for model-layout and
check-in conventions and representative models.
