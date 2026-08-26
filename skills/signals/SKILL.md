---
name: dotnet-inspect-signals
version: 0.1.0
description: Surface a dependency's observable signals — provenance, compatibility, dependency risk, unsafe/PInvoke surface, artifact-text containment, and identifier confusion — to judge how much caution it warrants. Observations, not verdicts.
---

# dotnet-inspect: dependency signals

Use this skill to surface the observable signals about a package or library —
provenance, build quality, safety surface, and supply-chain — so you can judge
how much caution a dependency warrants. dotnet-inspect reports observations, not
verdicts: it can give you concrete reasons for caution, not a stamp of trust.

```bash
dnx dotnet-inspect -y -- <command>
```

## The Signals rollup

`-S Signals` on `package` or `library` is the one-stop view. It reports
SourceLink, determinism, trim/AOT compatibility, memory-safety metadata,
unsafe/PInvoke surface, references, TFMs, manifest/docs, license,
vulnerabilities, package age, dependency risk, and content-free summaries of
artifact-text containment and identifier confusion where applicable.

```bash
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI -S Signals
dnx dotnet-inspect -y -- library System.Text.Json -S Signals
```

## Safety and interop surface

`-S @Audit` expands the audit sections authored for the target. On libraries it
includes `P/Invoke Methods` and `Audit: Identifier Confusion` alongside signals,
symbols, and path evidence. `Unsafe Members` is a standalone library section,
not a category member; select it by name. On packages, `@Audit` includes
`Audit: Artifact Text` and `Audit: Identifier Confusion`. `Switches` is also a
separate section; select it explicitly for feature-switch and trim/AOT knobs.
(For unsafe operations inside one method, see the `correctness` skill; for what
switches mean across versions, see `compatibility`.)

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "@Audit,Switches"
dnx dotnet-inspect -y -- library MyLib.dll -S "Unsafe Members,P/Invoke Methods"
```

`Signals` stays summary-only and never repeats a concerning value. Package
`Artifact text containment` names the Unicode concern kinds found in
package-model fields; its focused audit lists content-free field locations and
concern kinds. Package and library `Identifier confusion` summarizes
non-ASCII identifiers and reserved-prefix homoglyphs; its focused audit adds
content-free locations, classifications, similarity, and code points. The
explicit library audit resolves the transitive reference closure, while the
Signals row stays bounded to the selected assembly and direct references.

```bash
dnx dotnet-inspect -y -- package MyPackage -S "Signals,Audit: Artifact Text"
dnx dotnet-inspect -y -- package MyPackage -S "Signals,Audit: Identifier Confusion"
dnx dotnet-inspect -y -- library MyLib.dll -S "Signals,Audit: Identifier Confusion"
```

## SourceLink provenance

Distinct from *fetching* source (the `sourcelink` skill), `-S @SourceLink`
groups the provenance sections. Library `Signals` distinguishes a present
usable map from a partially usable or unusable one; `SourceLink: Diagnostics`
reports parse errors and rejected mappings without network access.
`SourceLink: Availability` reports whether documents are embedded or reachable,
`SourceLink: Integrity` validates their content, and `SourceLink: Missing Files`
reports gaps. The document-check sections also aggregate selected libraries on
`package`. Availability and missing-file checks may send HEAD requests for
uncached SourceLink URLs. The integrity pass fetches and hashes fetchable,
non-embedded compiler-source documents, so request these networked checks
explicitly.

```bash
dnx dotnet-inspect -y -- library System.Text.Json -D @SourceLink
dnx dotnet-inspect -y -- library System.Text.Json -D @SourceLink --effective
dnx dotnet-inspect -y -- package System.Text.Json -D @SourceLink
dnx dotnet-inspect -y -- library MyLib.dll -S "Signals,SourceLink: Diagnostics"
dnx dotnet-inspect -y -- library System.Text.Json \
  -S "SourceLink: Availability,SourceLink: Missing Files"
dnx dotnet-inspect -y -- library System.Text.Json -S "SourceLink: Integrity"
dnx dotnet-inspect -y -- package System.Text.Json -S "SourceLink: Availability,SourceLink: Missing Files"
```

On `library`, category discovery is structural until `--effective` is added.
On `package`, target discovery is effective by default.
