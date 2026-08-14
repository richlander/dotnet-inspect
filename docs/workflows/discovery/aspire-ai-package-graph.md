---
id: aspire-ai-package-graph
description: Package call-graph and integration web over Aspire hosting plus AI client stack
commands: [library, package, extensions, member, type]
areas: [packages, integrations, call-graph, extensions, aspire, ai]
---

# Aspire + AI package graph

> First **package call-graph** demo: a deep web of Aspire hosting libraries
> that provision AI resources, paired with the Microsoft.Extensions.AI client
> stack that consumes models. Pattern people steal: “hub type + component
> packages + integration census + call-graph drill.” Flex: multi-package
> `extensions`, `Integration:` sections, and cross-package `Call Graph` via
> `--caller-package`.

Pinned coordinates (bump together when refreshing the demo):

| Role | Package | Version |
| ---- | ------- | ------- |
| Hosting hub | `Aspire.Hosting` | 13.4.6 |
| OpenAI hosting | `Aspire.Hosting.OpenAI` | 13.4.6 |
| Azure OpenAI hosting | `Aspire.Hosting.Azure.CognitiveServices` | 13.4.6 |
| GitHub Models hosting | `Aspire.Hosting.GitHub.Models` | 13.4.6 |
| AI abstractions | `Microsoft.Extensions.AI` | 10.9.0 |
| OpenAI client adapter | `Microsoft.Extensions.AI.OpenAI` | 10.9.0 |

Hub type for the package web: `IDistributedApplicationBuilder`.
Hero member for the call-graph drill: `OpenAIExtensions.AddOpenAI`.

## Preconditions

```bash
export DOTNET_INSPECT_ISOLATED=aspire-ai-package-graph
```

```bash
dotnet-inspect cache clear
```

Prime the cache (network once per machine/session):

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

```bash
dotnet-inspect Microsoft.Extensions.AI.OpenAI@10.9.0 -v:q
```

## 1. Per-package Aspire integration census

> Goal: See how each AI hosting package declares itself to Aspire (resources
> and builder entry points) without reading source.

### 1a. OpenAI hosting

```prompt
What Aspire resources and builders does Aspire.Hosting.OpenAI expose?
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

### 1b. Azure OpenAI hosting

```prompt
How does Aspire model Azure OpenAI?
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

### 1c. GitHub Models hosting

```prompt
How do GitHub Models show up as Aspire resources?
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

## 2. Package web via hub-type extensions

> Goal: Treat `IDistributedApplicationBuilder` as the hub and list every
> component package’s `Add*` spokes in one multi-package query. This is the
> package graph people navigate when composing an AppHost.

### 2a. Hub + three AI spokes

```prompt
What AI-related Add* methods hang off IDistributedApplicationBuilder across Aspire hosting packages?
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
Aspire.Hosting.Azure.CognitiveServices
```

```expect
AddGitHubModel
```

```expect
Aspire.Hosting.GitHub.Models
```

## 3. Type surface of the OpenAI spoke

> Goal: From the package web, open the extension type and see the full fluent
> chain (`AddOpenAI`, `AddModel`, `WithApiKey`, …) — the type-scoped companion
> to the package graph (not the type call-graph demo).

### 3a. OpenAIExtensions members

```prompt
What members does OpenAIExtensions expose on the OpenAI Aspire package?
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

```expect
OpenAIResource
```

## 4. Package call graph — AddOpenAI into Aspire.Hosting

> Goal: Deep-drill one spoke: follow `AddOpenAI` into the hosting hub so
> external edges resolve as real Aspire.Hosting APIs (parameters, snapshots,
> annotations) rather than opaque `(external)` stubs.

### 4a. Edge table with caller-package scope

```prompt
Show the call graph for AddOpenAI with Aspire.Hosting on the caller/callee scope.
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

### 4b. Mermaid projection (Markdown)

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

```expect
OpenAIResource
```

## 5. Client-side AI stack (how apps talk to models)

> Goal: Pair hosting with the MEAI client surface. Aspire provisions resources;
> Microsoft.Extensions.AI is how application code registers chat/embeddings
> clients — including the OpenAI adapter.

### 5a. MEAI integration census

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

```expect
ChatClientBuilder
```

### 5b. OpenAI provider adapter

```prompt
How does Microsoft.Extensions.AI.OpenAI adapt OpenAI clients into MEAI?
```

```bash
dotnet-inspect library Microsoft.Extensions.AI.OpenAI@10.9.0 -S "Integration: AI" -v:n
```

```expect
AsIChatClient
```

```expect
AsIEmbeddingGenerator
```

## 6. Optional dependency color

> Goal: Show that the OpenAI hosting package is not a leaf — it depends on the
> hub and pulls ecosystem packages (including MCP) worth a follow-on demo.

### 6a. OpenAI hosting dependencies

```prompt
What does Aspire.Hosting.OpenAI depend on?
```

```bash
dotnet-inspect package Aspire.Hosting.OpenAI@13.4.6 -S Dependencies -v:n
```

```expect
Aspire.Hosting
```

```expect
ModelContextProtocol
```

## Narrative (for presenters)

1. **Census** — each AI hosting package’s `Integration: Aspire` row is the
   contract card (resource types + `Add*` entry).
2. **Web** — multi-package `extensions IDistributedApplicationBuilder` is the
   package graph: one hub type, many spokes, labeled by library.
3. **Type** — open `OpenAIExtensions` for the fluent API neighborhood.
4. **Call graph** — `AddOpenAI` + `--caller-package Aspire.Hosting` is the
   package call-graph flex: edges cross the package boundary into real hub
   APIs (`WithInitialState`, parameters, snapshots).
5. **Client** — MEAI `Integration: AI` + OpenAI `AsIChatClient` is the app
   half of “Aspire provisions; MEAI consumes.”

Non-goals for this demo (tracked separately):

- Declarative workspace/scenario registry (wasm share packet) — design only
  until productized.
- True transitive multi-hop call graph beyond current session bounds (#3632).
- Workspace-wide integrations roll-up (#3629).
- Type call-graph demo (sibling of this package demo).
