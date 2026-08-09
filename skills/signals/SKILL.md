---
name: dotnet-inspect-signals
version: 0.1.0
description: Surface a dependency's observable signals — SourceLink, determinism, trim/AOT, license, vulnerabilities, age, dependency risk, and the unsafe/PInvoke surface — to judge how much caution it warrants. Observations, not verdicts.
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
vulnerabilities, package age, and dependency risk.

```bash
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI -S Signals
dnx dotnet-inspect -y -- library System.Text.Json -S Signals
```

## Safety and interop surface

`-S @Audit` includes `Unsafe Members` and `P/Invoke Methods` alongside library
signals, symbols, and path audit evidence. `Switches` is a separate section;
select it explicitly for feature-switch and trim/AOT knobs. (For unsafe
operations inside one method, see the `correctness` skill; for what switches
mean across versions, see `compatibility`.)

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "@Audit,Switches"
dnx dotnet-inspect -y -- library MyLib.dll -S "Unsafe Members,P/Invoke Methods"
```

## SourceLink provenance

Distinct from *fetching* source (the `sourcelink` skill), `-S @SourceLink`
groups the provenance sections: `SourceLink: Availability` (is it wired up),
`SourceLink: Integrity` (do the documents validate), and `SourceLink: Missing
Files` (gaps). The same sections work on `library` and aggregate selected
libraries on `package`. The integrity pass fetches and hashes source content,
so request it explicitly.

```bash
dnx dotnet-inspect -y -- library System.Text.Json -D @SourceLink
dnx dotnet-inspect -y -- library System.Text.Json \
  -S "SourceLink: Availability,SourceLink: Missing Files"
dnx dotnet-inspect -y -- library System.Text.Json -S "SourceLink: Integrity"
dnx dotnet-inspect -y -- package System.Text.Json -S "SourceLink: Availability,SourceLink: Missing Files"
```
