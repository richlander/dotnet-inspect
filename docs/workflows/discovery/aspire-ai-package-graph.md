---
id: aspire-ai-package-graph
description: Simple Aspire hosting + AI package demo (integrations, extensions web, seed call graph)
commands: [library, package, extensions, member, type]
areas: [packages, integrations, call-graph, extensions, aspire, ai]
---

# Aspire hosting + AI

> Simple **package hosting** demo: Aspire’s AppHost hub, AI resource packages,
> and a seed-centric call graph into the hub. Pattern: integration census →
> hub-type extension web → one `Add*` drill. Flex: multi-package `extensions`,
> `Integration: Aspire`, and `Call Graph` with `--caller-package`.
>
> Deliberately small. Expand when call-graph modes grow (seed-centric vs ad hoc
> multi-input — #4133; cross-library body resolution — #3632).

## Pins

| Role | Package | Version |
| ---- | ------- | ------- |
| Hosting hub | `Aspire.Hosting` | 13.4.6 |
| OpenAI hosting | `Aspire.Hosting.OpenAI` | 13.4.6 |
| Azure OpenAI hosting | `Aspire.Hosting.Azure.CognitiveServices` | 13.4.6 |
| GitHub Models hosting | `Aspire.Hosting.GitHub.Models` | 13.4.6 |
| AI abstractions (optional client) | `Microsoft.Extensions.AI` | 10.9.0 |

Hub type: `IDistributedApplicationBuilder`.  
Seed member: `OpenAIExtensions.AddOpenAI`.

## Preconditions

```bash
export DOTNET_INSPECT_ISOLATED=aspire-ai-package-graph
```

```bash
dotnet-inspect cache clear
```

```bash
dotnet-inspect Aspire.Hosting@13.4.6 -v:q
```

```bash
dotnet-inspect Aspire.Hosting.OpenAI@13.4.6 -v:q
```

```bash
dotnet-inspect Aspire.Hosting.Azure.CognitiveServices@13.4.6 -v:q
```

```bash
dotnet-inspect Aspire.Hosting.GitHub.Models@13.4.6 -v:q
```

```bash
dotnet-inspect Microsoft.Extensions.AI@10.9.0 -v:q
```

## 1. Integration census (AI hosting packages)

> Goal: Each package’s Aspire contract card — resources and `Add*` entry points.

### 1a. OpenAI

```prompt
What Aspire resources does Aspire.Hosting.OpenAI expose?
```

```bash
dotnet-inspect library Aspire.Hosting.OpenAI@13.4.6 -S "Integration: Aspire" -v:n
```

```expect
Integration: Aspire
```

```expect
OpenAIResource
```

```expect
AddOpenAI
```

### 1b. Azure OpenAI and GitHub Models

```prompt
What do the other AI hosting packages declare to Aspire?
```

```bash
dotnet-inspect library Aspire.Hosting.Azure.CognitiveServices@13.4.6 -S "Integration: Aspire" -v:n
```

```expect
AzureOpenAIResource
```

```expect
AddAzureOpenAI
```

```bash
dotnet-inspect library Aspire.Hosting.GitHub.Models@13.4.6 -S "Integration: Aspire" -v:n
```

```expect
GitHubModelResource
```

```expect
AddGitHubModel
```

## 2. Package web on the hub type

> Goal: One multi-package `extensions` query — how AI packages hang off
> `IDistributedApplicationBuilder`.

### 2a. Hub + AI spokes

```prompt
Which AI Add* methods hang off IDistributedApplicationBuilder?
```

```bash
dotnet-inspect extensions IDistributedApplicationBuilder \
  --package Aspire.Hosting@13.4.6 \
  --package Aspire.Hosting.OpenAI@13.4.6 \
  --package Aspire.Hosting.Azure.CognitiveServices@13.4.6 \
  --package Aspire.Hosting.GitHub.Models@13.4.6 \
  --tfm net8.0 -v:n
```

```expect
AddOpenAI
```

```expect
Aspire.Hosting.OpenAI
```

```expect
AddAzureOpenAI
```

```expect
AddGitHubModel
```

## 3. Seed call graph — `AddOpenAI` into the hub

> Goal: Classic seed-centric path. Focus one builder API; widen into
> `Aspire.Hosting` so edges resolve to real hub APIs (parameters, snapshots)
> instead of opaque externals.

### 3a. Edge table

```prompt
Show the call graph for AddOpenAI with Aspire.Hosting in scope.
```

```bash
dotnet-inspect member OpenAIExtensions AddOpenAI \
  --package Aspire.Hosting.OpenAI@13.4.6 \
  --caller-package Aspire.Hosting@13.4.6 \
  -S "Call Graph" -v:n
```

```expect
Call Graph
```

```expect
AddOpenAI
```

```expect
from Aspire.Hosting
```

```expect
WithInitialState
```

```expect
OpenAIResource
```

### 3b. Mermaid

```prompt
Render the AddOpenAI call graph as Mermaid.
```

```bash
dotnet-inspect member OpenAIExtensions AddOpenAI \
  --package Aspire.Hosting.OpenAI@13.4.6 \
  --caller-package Aspire.Hosting@13.4.6 \
  -S "Call Graph" --markdown --mermaid -v:n
```

```expect
mermaid
```

```expect
graph TD
```

```expect
AddOpenAI
```

### 3c. Type surface of the seed’s class

```prompt
What fluent members does OpenAIExtensions expose?
```

```bash
dotnet-inspect type OpenAIExtensions --package Aspire.Hosting.OpenAI@13.4.6 -v:n
```

```expect
AddOpenAI
```

```expect
AddModel
```

```expect
WithApiKey
```

## 4. Optional — client AI integrations

> Goal: Brief “apps consume models” counterpart via MEAI (not required for the
> hosting story).

### 4a. Microsoft.Extensions.AI

```prompt
What AI integration points does Microsoft.Extensions.AI expose?
```

```bash
dotnet-inspect library Microsoft.Extensions.AI@10.9.0 -S "Integration: AI" -v:n
```

```expect
Integration: AI
```

```expect
AddChatClient
```

## Narrative

1. **Census** — `Integration: Aspire` on each AI hosting package.
2. **Web** — multi-package `extensions` on `IDistributedApplicationBuilder`.
3. **Seed graph** — `AddOpenAI` + `--caller-package Aspire.Hosting`.
4. **Optional client** — MEAI `Integration: AI`.

## Later (out of scope here)

- Ad hoc multi-input call graph (several packages as equal hubs) — #4133.
- Resolve workspace `(external)` callees into defining bodies — #3632.
- Deeper Azure substrate multi-hub walk (`AddAzureOpenAI` across
  `Aspire.Hosting.Azure` + Hosting) — natural expansion once #4133/#3632 land.
- Workspace integrations roll-up — #3629. Type call-graph sibling demo — separate.
