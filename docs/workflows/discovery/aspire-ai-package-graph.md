---
id: aspire-ai-package-graph
description: Multi-hub Aspire package web (Hosting + Azure + AI spokes) with cross-package call graph
commands: [library, package, extensions, member, type]
areas: [packages, integrations, call-graph, extensions, aspire, ai]
---

# Aspire + AI package graph

> First **package call-graph** demo. Aspire is not one hub package: a useful
> graph starts from **several base packages as peers**, then hangs AI spokes
> and the MEAI client stack off that web.
>
> Pattern: multi-hub package set → hub-type extension web → integration census
> → multi-package call-graph drill. Flex: multi-`--package` `extensions`,
> `Integration:` sections, and `Call Graph` with repeated `--caller-package`.

## Why not only `Aspire.Hosting`?

`Aspire.Hosting` is necessary substrate (application model, builder, resources)
but on its own it under-describes Aspire. Component packages and the Azure
base carry most of the interesting surface. A single-member call graph that
only widens into `Aspire.Hosting` also reads as hub-centric: every spoke’s
`Add*` collapses into the same substrate edges.

This demo therefore treats **three packages as hubs** and keeps AI packages as
first-class peers rather than footnotes on Hosting:

| Hub role | Package | Why it is a hub |
| -------- | ------- | --------------- |
| Application model | `Aspire.Hosting` | Builder, resources, parameters, snapshots |
| Azure substrate | `Aspire.Hosting.Azure` | Provisioning, environment, Bicep/identity |
| Azure OpenAI spoke-hub | `Aspire.Hosting.Azure.CognitiveServices` | AI entry that **fans into both** Hosting and Azure |

AI peers on the same web (not hubs, still first-class):

| Peer | Package |
| ---- | ------- |
| OpenAI hosting | `Aspire.Hosting.OpenAI` |
| GitHub Models | `Aspire.Hosting.GitHub.Models` |

Client plane (how apps consume models Aspire provisions):

| Role | Package |
| ---- | ------- |
| AI abstractions | `Microsoft.Extensions.AI` |
| OpenAI adapter | `Microsoft.Extensions.AI.OpenAI` |

All Aspire pins are **13.4.6**; MEAI pins are **10.9.0**. Bump a column
together when refreshing.

Hub type for the package web: `IDistributedApplicationBuilder`.  
Hero call-graph member: `AzureOpenAIExtensions.AddAzureOpenAI` (multi-hub
fanout). Contrast member: `OpenAIExtensions.AddOpenAI` (Hosting-only fanout).

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
dotnet-inspect Aspire.Hosting.Azure@13.4.6 -v:q
```

```bash
dotnet-inspect Aspire.Hosting.Azure.CognitiveServices@13.4.6 -v:q
```

```bash
dotnet-inspect Aspire.Hosting.OpenAI@13.4.6 -v:q
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

## 1. Hub integration census (base packages first)

> Goal: Read each hub’s `Integration: Aspire` card before any spoke. This is
> the multi-hub story: Hosting alone is thin compared with Azure’s resource
> and builder surface.

### 1a. Azure substrate hub

```prompt
What Aspire integrations does the Azure hosting base package declare?
```

```bash
dotnet-inspect library Aspire.Hosting.Azure@13.4.6 -S "Integration: Aspire" -v:n
```

```expect
Integration: Aspire
```

```expect
AzureEnvironmentResource
```

```expect
AddAzureEnvironment
```

```expect
IAzureResource
```

### 1b. Azure OpenAI hub (AI entry on Azure)

```prompt
How does Aspire model Azure OpenAI on top of the Azure substrate?
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

### 1c. OpenAI and GitHub Models peers

```prompt
What Aspire resources do the non-Azure AI hosting packages expose?
```

```bash
dotnet-inspect library Aspire.Hosting.OpenAI@13.4.6 -S "Integration: Aspire" -v:n
```

```expect
OpenAIResource
```

```expect
AddOpenAI
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

## 2. Package web via hub-type extensions (multi-hub)

> Goal: One query over **all hubs and AI peers**. Contributions are labeled by
> library — the package graph people navigate when composing an AppHost. Hosting
> still dominates method count; Azure and AI packages supply the distinctive
> `Add*` spokes.

### 2a. Three hubs + AI peers on `IDistributedApplicationBuilder`

```prompt
What Add* methods hang off IDistributedApplicationBuilder across Aspire hosting, Azure, and AI packages?
```

```bash
dotnet-inspect extensions IDistributedApplicationBuilder \
  --package Aspire.Hosting@13.4.6 \
  --package Aspire.Hosting.Azure@13.4.6 \
  --package Aspire.Hosting.Azure.CognitiveServices@13.4.6 \
  --package Aspire.Hosting.OpenAI@13.4.6 \
  --package Aspire.Hosting.GitHub.Models@13.4.6 \
  --tfm net8.0 -v:n
```

```expect
AddAzureEnvironment
```

```expect
Aspire.Hosting.Azure
```

```expect
AddAzureOpenAI
```

```expect
Aspire.Hosting.Azure.CognitiveServices
```

```expect
AddOpenAI
```

```expect
Aspire.Hosting.OpenAI
```

```expect
AddGitHubModel
```

```expect
Aspire.Hosting.GitHub.Models
```

## 3. Multi-hub call graph — `AddAzureOpenAI`

> Goal: Deep-drill the member that actually crosses hubs. With
> `--caller-package` for both `Aspire.Hosting` and `Aspire.Hosting.Azure`,
> edges resolve into **two** package groups (provisioning/environment on Azure,
> resources/snapshots on Hosting) instead of a Hosting-only star.

### 3a. Edge table across Hosting + Azure

```prompt
Show the call graph for AddAzureOpenAI with Aspire.Hosting and Aspire.Hosting.Azure in scope.
```

```bash
dotnet-inspect member AzureOpenAIExtensions AddAzureOpenAI \
  --package Aspire.Hosting.Azure.CognitiveServices@13.4.6 \
  --caller-package Aspire.Hosting@13.4.6 \
  --caller-package Aspire.Hosting.Azure@13.4.6 \
  -S "Call Graph" -v:n
```

```expect
Call Graph
```

```expect
AddAzureOpenAI
```

```expect
from Aspire.Hosting.Azure
```

```expect
AddAzureProvisioning
```

```expect
AddAzureEnvironment
```

```expect
from Aspire.Hosting
```

```expect
AddResource
```

### 3b. Mermaid projection

```prompt
Render the multi-hub AddAzureOpenAI call graph as Mermaid.
```

```bash
dotnet-inspect member AzureOpenAIExtensions AddAzureOpenAI \
  --package Aspire.Hosting.Azure.CognitiveServices@13.4.6 \
  --caller-package Aspire.Hosting@13.4.6 \
  --caller-package Aspire.Hosting.Azure@13.4.6 \
  -S "Call Graph" --markdown --mermaid -v:n
```

```expect
mermaid
```

```expect
graph TD
```

```expect
AddAzureOpenAI
```

```expect
AddAzureProvisioning
```

## 4. Contrast — Hosting-only fanout (`AddOpenAI`)

> Goal: Same recipe on the pure OpenAI spoke. The graph is still useful, but
> almost every resolved edge is `from Aspire.Hosting`. That is the hub-centric
> shape to avoid as the *only* story in the demo.

### 4a. AddOpenAI into Hosting

```prompt
Show that AddOpenAI’s cross-package graph collapses into Aspire.Hosting.
```

```bash
dotnet-inspect member OpenAIExtensions AddOpenAI \
  --package Aspire.Hosting.OpenAI@13.4.6 \
  --caller-package Aspire.Hosting@13.4.6 \
  --caller-package Aspire.Hosting.Azure@13.4.6 \
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

```expect-not
from Aspire.Hosting.Azure
```

### 4b. Type surface of the OpenAI spoke

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

## 5. Client-side AI stack

> Goal: Pair multi-hub hosting with MEAI. Aspire hubs provision; MEAI is how
> application code registers chat/embeddings clients.

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

## 6. Dependency color on the Azure OpenAI hub

> Goal: Show CognitiveServices is not a leaf — it depends on both hub packages
> the call graph just traversed.

### 6a. Package dependencies

```prompt
What does Aspire.Hosting.Azure.CognitiveServices depend on?
```

```bash
dotnet-inspect package Aspire.Hosting.Azure.CognitiveServices@13.4.6 -S Dependencies -v:n
```

```expect
Aspire.Hosting
```

```expect
Aspire.Hosting.Azure
```

## Narrative (for presenters)

1. **Hubs first** — `Aspire.Hosting` + `Aspire.Hosting.Azure` + Azure OpenAI
   CognitiveServices as the third hub; do not open with Hosting alone.
2. **Web** — multi-package `extensions IDistributedApplicationBuilder` labels
   every `Add*` by library (Hosting bulk + Azure/AI distinctive spokes).
3. **Multi-hub call graph** — `AddAzureOpenAI` with two `--caller-package`
   values; edges cite `from Aspire.Hosting.Azure` *and* `from Aspire.Hosting`.
4. **Contrast** — `AddOpenAI` is Hosting-star shaped; useful, but incomplete as
   the only graph in the demo.
5. **Client** — MEAI `Integration: AI` + `AsIChatClient` closes
   “provision vs consume.”

## Product notes (honest limits)

- Today’s member call graph is **seed-centric**: one focus member, scope widened
  by `--caller-package` / `--bin` / `--project`. It is not yet a multi-root
  “package graph” object. The demo fakes multi-hub interest by (a) choosing a
  seed that fans across hubs and (b) showing the extension web as the true
  multi-package map.
- `Aspire.Hosting.AppHost` is a real product package but a weak inspect hub
  today (MSBuild/tasks-heavy; default library selection is easy to mis-pick).
  Left out until library selection in the demo is bulletproof.
- Declarative workspace/scenario registry (wasm share) is still design-only.
- Deeper transitive multi-hop graphs: #3632. Workspace integrations roll-up:
  #3629. Sibling **type** call-graph demo remains separate.
