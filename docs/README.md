# dotnet-inspect documentation

## Acquire and run

Install the global tool:

```bash
dotnet tool install -g dotnet-inspect
dotnet-inspect <command>
```

Or run without installing:

```bash
dnx dotnet-inspect -y -- <command>
```

## Agent guidance

Run the embedded skill for current, version-matched guidance:

```bash
dotnet-inspect skill
```

Agents should do this before relying on remembered command patterns. When
running without a global install, use `dnx dotnet-inspect -y -- skill`.

## Websites

| Site | Channel and update cadence | Runtime |
| --- | --- | --- |
| <https://dotnet-inspect.net> | Production; the same commit as the NuGet tool release. | .NET 11 RC1 |
| <https://dotnet-inspect.ca> | Working version; updated for each commit. | .NET 11 RC1 |
| <https://coreclr.dotnet-inspect.ca> | Nightly CoreCLR interpreter version. | .NET 12 daily build |
| <https://coreclr-r2r.dotnet-inspect.ca> | Nightly CoreCLR ReadyToRun version; the same commit as the interpreter version. | .NET 12 daily build |

This page is curated navigation for users and contributors. For the full
product guide, current commands, examples, supported behavior, and
user-visible limitations, continue with the root [README](../README.md).

## Documentation entrypoint ownership

| Surface | Owns | Update when |
| --- | --- | --- |
| [`README.md`](../README.md) | Full product guide: overview, canonical acquisition, primary workflows, capability and command inventory, examples, requirements, and top-level limitations. | One of those current product claims changes or a capability earns top-level discovery. |
| [`docs/README.md`](README.md) | User and contributor landing page: minimal acquisition and agent guidance, website channels, curated documentation routes, and the boundaries in this table. | Canonical acquisition, skill guidance, website channels, a high-value route, or an entrypoint's role changes. |
| [`docs/overview.md`](overview.md) | Subsystem topology and the map from cross-subsystem composition to normative owners. | A subsystem boundary, owner, or cross-subsystem relationship changes. |
| [`docs/architecture.md`](architecture.md) | Current implementation composition, project boundaries, shared currencies, and code location. | Current code structure or an explicit migration boundary changes. |
| Focused documents | Their own contracts, status, evidence, consumers, and successor work. | The focused owner's claim changes. |

Update only the surfaces whose owned claims change. Adding or editing a focused
document does not by itself require a root README, documentation index,
overview, or architecture edit.

The acquire-and-run commands are intentional duplication between the two
README entrypoints because both audiences need them immediately. Keep that
small block aligned; do not copy the rest of the product guide here.

Detailed user behavior belongs with its focused guide or product skill. The
root README remains current without cataloging every focused capability.

## Start here

| Need | Entry point |
| --- | --- |
| Use the current product | [Root README](../README.md) |
| Understand cross-subsystem ownership | [Overview](overview.md) |
| Locate current implementation and project boundaries | [Architecture](architecture.md) |
| Understand the target workspace, query, cache, and safety model | [Inspection Space Architecture](inspection-space.md) |
| Build a shared inspection from product question to both hosts | [Building Shared Inspections](building-shared-inspections.md) |
| Contribute under repository workflow rules | [AGENTS.md](../AGENTS.md) |

## Core design routes

| Concern | Entry point |
| --- | --- |
| Layering and project families | [Inspection Layers](design/inspection-layers.md) and [Library Family Boundaries](design/library-family-boundaries.md) |
| Cross-host operation composition | [Inspection Operation Composition](design/inspection-operation-composition.md) |
| Query library, composition, operation registration, portable intent, and payload | [QuerySpace Library Boundary](design/query-space-library.md), [Query Space Composition](design/query-space-composition.md), [Query Operation Infrastructure](design/query-operation-infrastructure.md), [Portable Query Intent](design/portable-query-intent.md), and [Portable Query Payload](design/portable-query-payload.md) |
| Installed resource discovery and exact explanation | [Schema Query](design/schema-query.md), [Resource Explanation](design/resource-explanation.md), and [Product Vocabulary](design/vocabulary.md) |
| Retained state and service orientation | [Stateless Core Services](design/stateless-core-services.md) |
| Resource ownership and current adoption | [Resource Ownership and Borrowing](design/resource-ownership-and-borrowing.md), [Resource Occurrence Analysis](design/resource-occurrence-analysis.md), and the [Resource-Owner Type Map](design/resource-owner-type-map.md) |
| Command placement, names, defaults, and disclosure | [Operation Commands and Subject Sections](design/operation-command-and-subject-section-composition.md), [Relationship Section Naming](design/relationship-section-naming.md), [Progressive Disclosure](design/progressive-disclosure.md), and [CLI Host Architecture](cli-architecture.md) |
| Output data and rendering | [CLI Output Format and Destination](design/cli-output-format.md), [Output Shapes](design/output-shapes.md), [Style Guide](design/style-guide.md), and [Inspection Envelope](design/inspection-envelope.md) |
| Metadata and API inspection | [Assembly Inspection Query](design/assembly-inspection-query.md) |
| Package composition | [PackageHouse](design/package-house.md) |
| Platform composition | [PlatformHouse](design/platform-house-reference-processing.md) |
| Source and PDB composition | [SourceHouse](design/source-house.md) and [PDB Acquisition](pdb-acquisition.md) |
| Documentation composition | [DocumentationHouse](design/documentation-house.md) |
| Decompiler architecture and correctness | [Decompiler Architecture](decompiler-architecture.md) and [Decompiler Correctness Pipeline](decompiler-correctness-pipeline.md) |
| Browser host | [Inspect Web](../inspect-web/README.md) |

## Contributor workflow routes

| Need | Entry point |
| --- | --- |
| Engineering model and PR demos | [Development Practices](development-practices.md) |
| Design ownership and scope | [Design Scope and Composition](design-scope.md) |
| Evidence and validation | [Evidence and Validation](evidence-and-validation.md) |
| Test fixture placement and ownership | [Fixture Governance](fixture-governance.md) |
| Local tools, SDKs, and focused test commands | [Local Development Environment](dev-environment.md) |
| Adversarial review rounds | [Round Orchestration](round-orchestration.md) and the [canonical review prompt](adversarial-review-prompt.md) |
| Session and tmux state | [Agent Session State](agent-session-state.md) |
| GitHub automation | [GitHub API Operations](github-api-operations.md) and [GitHub Status Queries](github-status-queries.md) |
| Multi-PR work | [Stacked PRs](stacked-prs.md) |
| Release certification and publication | [Release Workflow](release-workflow.md) |
| TLA+ setup and modeling | [TLA+ Methodology](tla-plus-methodology.md) and [TLA+ Setup](runbooks/tla-plus-setup.md) |
| Markout co-development | [Markout Co-development](markout-co-development.md) |

## Finding focused documentation

Focused documents under [`docs/design/`](design/) are reached from their
normative owner, consumers, implementation, issue, or pull request. Search that
directory by subsystem or identifier when no curated route above applies.
Templates live under [`docs/templates/`](templates/), runbooks under
[`docs/runbooks/`](runbooks/), contributor skills under
[`.github/skills/`](../.github/skills/), shipped product skills under
[`skills/`](../skills/), and historical/backlog material under `docs/` and
`docs/design/`.

Some design files record proposals or design history rather than current
behavior. When current sources disagree, prefer product behavior and tests,
then resolve which focused owner is authoritative rather than silently
choosing one.
