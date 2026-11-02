---
id: secure-supply-chain
description: Review frozen dependency upgrades using provenance, advisories, API differences, and HTTPS literal changes
commands: [depends, package, library, diff, explain]
areas: [supply-chain, dependencies, provenance, security, diff, discovery]
---

# Secure supply-chain review

Review a restored project's direct dependencies against fixed upstream
endpoints. This is static, evidence-bounded upgrade triage, not a vulnerability
exploit test or a certification that an upgrade is safe.

The reference path reuses the real package pairs from the
[supply-chain pilot](https://github.com/richlander/dotnet-inspect/issues/8151#issuecomment-6506751679).
It adds package provenance and current advisory/deprecation evidence to the API
and literal comparisons. This is expanded workflow coverage, not another
controlled skill-versus-evidence efficiency experiment.

## Preconditions

Use the exact NativeAOT build and version described in the
[validation instructions](../../../.github/skills/workflow-scenarios/validating-workflows.md#preconditions).
The development SDK and `jq` must be available. Network access is required for
restore, package metadata, advisory data, symbols, and initial acquisition.
Do not run preparation during an agent trial.

Create a restore-only project independent of the repository's build settings:

```bash
set -e -o pipefail
export DOTNET_INSPECT_ISOLATED=secure-supply-chain
export SUPPLY_WORKFLOW="$PWD/artifacts/workflows/secure-supply-chain"
mkdir -p "$SUPPLY_WORKFLOW/Review"
cat > "$SUPPLY_WORKFLOW/Directory.Build.props" <<'EOF'
<Project />
EOF
cat > "$SUPPLY_WORKFLOW/Directory.Build.targets" <<'EOF'
<Project />
EOF
cat > "$SUPPLY_WORKFLOW/Directory.Packages.props" <<'EOF'
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
EOF
cat > "$SUPPLY_WORKFLOW/Review/Review.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Identity.Client" Version="4.61.3" />
    <PackageReference Include="Serilog" Version="4.3.0" />
  </ItemGroup>
</Project>
EOF
cat > "$SUPPLY_WORKFLOW/upstream.json" <<'EOF'
{
  "snapshotDate": "2026-10-30",
  "selection": "Fixed regression endpoints, not a live latest query",
  "comparisonTfm": "netstandard2.0",
  "dependencies": [
    {"package": "Microsoft.Identity.Client", "current": "4.61.3", "upstream": "4.92.0"},
    {"package": "Serilog", "current": "4.3.0", "upstream": "4.4.0"}
  ]
}
EOF
dotnet restore "$SUPPLY_WORKFLOW/Review/Review.csproj" \
  --nologo --verbosity quiet
```

The project targets `net10.0`; its restored compile assets need not be the
Library slice used for comparison. Both package pairs have real
`netstandard2.0` implementation Libraries, which are the fixed comparison
population. Never substitute reference assemblies, another TFM, or assembly
versions for the package endpoints.

Upstream versions are deliberately frozen, not claimed to be today's latest.
NuGet metadata, advisories, deprecation, certificate verification, and symbol
availability are live evidence: record observation time and actual status
rather than pinning a vulnerability count or treating service failure as zero.
Restore auditing is disabled only to prepare this old input independently;
the review must still query the product's advisory evidence.

## 1. Review the dependency upgrade

> Goal: Join dependency inventory, provenance, compatibility, and changed
> endpoint text into an upgrade assessment with explicit evidence limits.

For agent mode, give a fresh reader only the following prompt, the prepared
project/snapshot paths, and access to the exact tool's current embedded skills.
The rest of this document is judge-side material.

```prompt
Review the restored project at <project-path> against the frozen upstream
snapshot at <snapshot-path> for security-relevant dependency-upgrade concerns.
Identify its direct package references and exact comparison endpoints.
For each dependency, report package provenance and currently reported
vulnerabilities or deprecation, ordinary API compatibility changes, and added
or removed static strings containing "https://". Use the snapshot's shared
implementation TFM for comparisons. Distinguish completed checks with no
matching changes from unavailable or failed evidence. Give an evidence-backed
upgrade assessment and name what remains unknown; do not infer actual network
traffic or overall package safety from static strings or signatures.
```

Judge all six outcomes; success on one check does not cover the others:

| Outcome | Required evidence and qualification |
| --- | --- |
| Scope | The two direct PackageReferences and their exact frozen endpoints; distinguish transitive dependencies and the project's TFM from the comparison TFM. |
| Package risk | Actual license/repository/signature and checked advisory/deprecation evidence for both current packages, with observation time and live-data limitations. |
| Implementation scope | Both endpoints of each comparison are the requested `netstandard2.0` implementation Libraries with IL, not facades. SourceLink presence is provenance, not source-integrity verification. |
| API changes | Completed ordinary API comparisons for both pairs, assessed breaks and scope exclusions; do not call hidden/protected changes public-consumer breaks. |
| HTTPS changes | MSAL has both added and removed literal-use findings; Serilog is a completed zero-change control. Keep use findings separate from distinct strings and actual runtime requests. |
| Assessment | Findings tied to evidence, prioritized concerns, and unresolved checks; no automatic safe-upgrade or vulnerability verdict. |

### 1a. Establish the restored dependency scope

```bash
dotnet-inspect depends --project "$SUPPLY_WORKFLOW/Review/Review.csproj" \
  --tfm net10.0 -S Dependencies --json
```

```expect
Microsoft.Identity.Client
4.61.3
Serilog
4.3.0
```

The project and snapshot define the two direct roots. Transitive packages
remain context, not additional direct PackageReferences. `depends --project`
reads the existing restore; it must not be described as restoring or building.

### 1b. Read package provenance and checked advisory evidence

```bash
set -e -o pipefail
for package in Microsoft.Identity.Client@4.61.3 Serilog@4.3.0; do
  dotnet-inspect package "$package" --tfm netstandard2.0 \
    -S "Package Info,Signals" --json
done
```

```query
jq -s -e 'length == 2 and (map([.package_name,.version]) | sort) == [["Microsoft.Identity.Client","4.61.3"],["Serilog","4.3.0"]] and all(.[]; .source == "NuGet" and (.license | length > 0) and (.repository | length > 0) and (.signature_result | type == "object") and any(.audit_signals[]; .signal == "Known vulnerabilities" and (.value | test("^[0-9]+$")) and .evidence == "NuGet advisory data"))'
```

```expect
true
```

Read the actual `deprecation`, `vulnerabilities`, `signature_result`, and audit
rows before assessing the packages. A checked zero is not a clean bill of
health. A NuGet package signature, an assembly strong name, a verified publisher,
and trusted behavior are different claims. Report supplied deprecation reasons;
do not infer them from age or the vulnerability count.

### 1c. Verify the four implementation endpoints

```bash
set -e -o pipefail
for package in Microsoft.Identity.Client@4.61.3 Microsoft.Identity.Client@4.92.0 \
  Serilog@4.3.0 Serilog@4.4.0; do
  dotnet-inspect library --package "$package" --tfm netstandard2.0 \
    --namesake-library -S "Library Info,Symbols" --json
done
```

```query
jq -s -e 'length == 4 and (map(.assembly_info.assembly_name) | sort) == ["Microsoft.Identity.Client","Microsoft.Identity.Client","Serilog","Serilog"] and all(.[]; .assembly_info.target_framework == ".NETStandard,Version=v2.0" and .assembly_info.has_il_code == true and .assembly_info.method_definition_count > 0)'
```

```expect
true
```

Retain PDB and SourceLink status, including unavailable results. Discoverable
source mappings do not establish that source documents were fetched or their
checksums verified.

### 1d. Compare ordinary API compatibility

```bash
dotnet-inspect diff --package Microsoft.Identity.Client@4.61.3..4.92.0 \
  --tfm netstandard2.0 --json
```

```query
jq -e '.outcome == "available" and .document.before.isComplete and .document.after.isComplete and .document.summary.changedTypeCount > 0 and .document.summary.breakingCount > 0'
```

```expect
true
```

```bash
dotnet-inspect diff --package Serilog@4.3.0..4.4.0 \
  --tfm netstandard2.0 --json
```

```query
jq -e '.outcome == "available" and .document.before.isComplete and .document.after.isComplete and .document.summary.changedTypeCount == 0 and (.document.rows | length) == 0'
```

```expect
true
```

These are ordinary API comparisons, not exhaustive implementation comparisons
or guarantees that a consumer will upgrade successfully. Preserve exclusion
notes; `--all` changes the population and its findings need separate labels.

### 1e. Discover the literal comparison contract

```bash
dotnet-inspect explain "compare string literals"
```

```expect
diff-analysis
```

Follow the returned capability's binding:

```bash
dotnet-inspect explain diff-analysis/bindings/cli --json
```

```query
jq -e '.facts["input-rules"] | any(contains("exactly one --where")) and any(contains("Literal contains value")) and any(contains("one shell argument"))'
```

```expect
true
```

This reference step proves that the installed binding exposes the recipe.
Only the separate reader's captured calls can establish where that reader
actually learned it.

### 1f. Compare matching literal-use changes

```bash
dotnet-inspect diff --package Microsoft.Identity.Client@4.61.3..4.92.0 \
  --tfm netstandard2.0 --analysis string-literals \
  --where "Literal contains https://" --json
```

```query
jq -e 'any(.outcomes[]; .analysis == "string-literals" and .kind == "Compared") and (.transitions | length) == 48 and ([.transitions[] | select(.transition == "PairFinding.Added")] | length) == 24 and ([.transitions[] | select(.transition == "PairFinding.Removed")] | length) == 24 and all(.transitions[]; .oldInspection == "complete" and .newInspection == "complete")'
```

```expect
true
```

```bash
dotnet-inspect diff --package Serilog@4.3.0..4.4.0 \
  --tfm netstandard2.0 --analysis string-literals \
  --where "Literal contains https://" --json
```

```query
jq -e 'any(.outcomes[]; .analysis == "string-literals" and .kind == "Compared") and (.transitions | length) == 0'
```

```expect
true
```

The MSAL counts are literal-use finding transitions, not 24 distinct URLs in
each direction. Serilog's negative result means no matching changes in this
comparison, not absence of all HTTPS literals. Neither result proves actual
requests, effective endpoints, exploitability, malicious intent, or upgrade
safety. Do not execute inspected package code to establish these static facts.
