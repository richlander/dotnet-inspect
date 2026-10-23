# Component explanation: worked shared reading

This walkthrough uses System.Text.Json 10.0.0/net10.0 and the captured
[Package component descriptor](../../../docs/examples/workspace-package-component.json).
Root, collection and contract requests below are implemented CLI commands.
The detached examples are actual shared-operation output; accepting a component
file through CLI `explain` remains the facade adoption in issue #10140.

## Find the declaration contract

Start with the installed routing summaries:

```bash
dotnet-inspect explain .hal --json > root.json
jq '._embedded.resources[] | ._embedded.data.facts |
  select(.identity == "component-contracts") | {name, purpose, questions}' root.json
```

The reference entry routes to `component-contracts`. Read that collection only
if the question needs a declaration contract:

```bash
dotnet-inspect explain component-contracts .hal --json > contracts.json
href=$(jq -r '._links["collection-component-contract"][0].href' contracts.json)
dotnet-inspect explain "$href" .hal --json > contract.json
jq '._embedded.data.facts.fields[] | select(.id == "framework") |
  {name, meaning, shape, cardinality}' contract.json
```

The answer is:

```json
{
  "name": "Framework",
  "meaning": "Requested acquisition TFM; absent leaves the requested target unspecified.",
  "shape": "workspace/components/shapes/text",
  "cardinality": "OptionalOne"
}
```

No Package is acquired. Root reading contains the routing entries, without the
contract's field definitions. The contract's `fields` table comes from the
owner schema; `field_limits` contains only the two fields with population
limits. Known readers can enter at `workspace/components/package` directly.
Save the answer and use jq for further local questions; another request is
needed only for knowledge absent from the saved content.

Captured examples:

- [HAL root](../../../docs/examples/explain-root_hal.json)
- [HAL Package contract](../../../docs/examples/explain-package-contract_hal.json)

Compact CLI output from this capture is 10,676 bytes for the HAL root versus
26,335 bytes for its direct reading, 755 bytes for the HAL collection, and
2,739 bytes for the HAL contract leaf. These are serialized UTF-8 byte counts,
including CLI newlines, not latency measurements or an agent usability result.
The checked-in JSON is pretty-printed for reading, so its file size is larger.
The root includes the current nine routing entries; these captures record this
catalog rather than a permanent inventory of product capabilities.

## Explain an extracted declaration

The shared consumer accepts an owner-validated input:

```csharp
var input = WorkspacePackageComponentExplanation.Parse(componentJson);
var document = WorkspaceComponentExplanationOperation.Explain(input).Content;
var hal = ResourceExplanationDataProjection.CreateHal(
    document, path => "inspect-resource:/" + path.Value);
```

The [direct output](../../../docs/examples/explain-package-declaration_direct.json)
and [HAL output](../../../docs/examples/explain-package-declaration_hal.json)
carry the declared Package, Version and TFM. A copied `contexts/g0` or focused
flag does not authorize parent facts. Without the parent, those facts are
`Unavailable`; RID is `Absent` because the captured declaration supplies null.
The `defaulted_fields` array is empty because this descriptor supplied every
optional field, including explicit null.

A cached HAL response supports local value and availability questions:

```bash
jq '._embedded.data | {declared: .facts, unavailable: .fact_states}' declaration_hal.json
```

Its `describedby` link is `inspect-resource:/workspace/components/package`.
Follow it through `explain` only when the question needs field interpretation
not already present locally. It describes the declaration kind; it does not
identify a realized Package or a canonical component occurrence.

## Supply the actual parent when the question needs it

For **which context contains this contribution?**, supply the immutable packet
through the owner API:

```csharp
var input = WorkspacePackageComponentExplanation.Parse(componentJson, parentPacket);
var document = WorkspaceComponentExplanationOperation.Explain(input).Content;
```

The captured [qualified HAL result](../../../docs/examples/explain-package-occurrence_hal.json)
uses the [saved Package replay packet](../../../docs/examples/workspace-package-replay.json).
It reports `contexts/g0`, the native focused boolean, and the packet-local
component path as facts. It gives that path no global `self` link. Different
parent contents qualify different detached input keys, even when local path
text matches. The original parent, not a guessed or active Workspace, settles
those facts. No restoration or saved inspection is executed.

Ordinary HAL links replace redundant typed keys/addresses for installed,
addressable resources. `empty_relations` explicitly records complete empty
relations. Unavailable, failed or incomplete relationships and unaddressable
targets retain typed observations under `relationships`; native fact states
and traversal completeness remain explicit. The full typed graph continues to
be available through the direct and contract representations.
