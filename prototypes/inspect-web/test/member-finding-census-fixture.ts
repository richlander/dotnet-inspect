import { sampleDocument } from "../../annotated-source-viewer/src/sample-document.js";
import {
  createMemberFindingInteraction,
  type MemberFindingCensus,
} from "../src/finding-interaction.ts";
import { validateAnnotatedSourceDocument } from "../src/annotated-source-view.ts";
import type { AnnotatedSourceDocument } from "../src/document-model.ts";
import { sampleViewerCatalog } from "./annotated-source-result-fixture.ts";

export type MemberFindingCensusFixtureMode = "populated" | "long" | "empty";

export function memberFindingCensusFixture(
  mode: MemberFindingCensusFixtureMode = "populated",
): MemberFindingCensus {
  const sample: unknown = sampleDocument;
  validateAnnotatedSourceDocument(sample);
  const duplicate = sample.facts[0]!;
  const populatedDocument: AnnotatedSourceDocument = {
    ...sample,
    facts: sample.facts.map((fact, index) =>
      index < 2 ? { ...duplicate, id: fact.id } : fact),
    targets: [
      ...sample.targets.filter(target => target.fact_id !== 1),
      ...sample.targets
        .filter(target => target.fact_id === 0)
        .map(target => ({ ...target, fact_id: 1 })),
    ],
  };
  validateAnnotatedSourceDocument(populatedDocument);
  const displayShape = {
    member: "Example.Widget.Run()",
    ilOffset: 12,
    cSharpLine: 4,
    anchor: "call",
    category: "Cost",
    id: "allocation",
    detail: "allocates",
    conditionality: "Always",
  } as const;
  const longDisplayShape = {
    member: "Example.Serialization.BufferedDocumentReader<System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.List<System.Text.Json.JsonElement>>>.ReadDocumentAsync(System.ReadOnlyMemory<System.Byte>, System.Threading.CancellationToken)",
    ilOffset: 0x12345678,
    cSharpLine: 2147483647,
    anchor: "call-site-on-a-deeply-nested-generic-instantiation-with-complete-provenance",
    category: "CostAndAllocationBehaviorFromNestedGenericCollectionMaterialization",
    id: "analysis.allocation.with-an-intentionally-long-descriptor-for-containment",
    detail: "Allocates an intentionally long nested generic value while materializing a deeply composed document path; the complete producer detail remains visible without truncation.",
    conditionality: "ConditionalOnTheErrorPathWithinEachLoopIteration",
  } as const;
  const document: AnnotatedSourceDocument = mode === "empty"
    ? { ...populatedDocument, facts: [], targets: [] }
    : populatedDocument;
  validateAnnotatedSourceDocument(document);
  const facts = mode === "empty"
    ? []
    : mode === "long"
      ? [
          { ...longDisplayShape, instanceKey: 41 },
          { ...displayShape, instanceKey: 42 },
          {
            ...longDisplayShape,
            ilOffset: null,
            cSharpLine: null,
            anchor: "member-header-with-an-intentionally-long-anchor-value",
            id: "research.member-header.with-an-intentionally-long-identifier",
            detail: "Member-level observation without an Annotated Source body target.",
            instanceKey: null,
          },
        ]
      : [
          { ...displayShape, instanceKey: 41 },
          { ...displayShape, instanceKey: 42 },
          {
            ...displayShape,
            anchor: "member",
            id: "member-header",
            instanceKey: null,
          },
        ];
  return {
    factCensusReceipt: "11111111-1111-1111-1111-111111111111",
    facts,
    annotatedSource: {
      document,
      viewerCatalog: sampleViewerCatalog,
      provenance: "decompiled from IL",
      contextLimitation: null,
    },
    sourceFactInstances: mode === "empty"
      ? []
      : [
          { factId: 0, instanceKey: 41 },
          { factId: 1, instanceKey: 42 },
        ],
  };
}

export function memberFindingInteractionFixture(
  mode: MemberFindingCensusFixtureMode = "populated",
) {
  return createMemberFindingInteraction(memberFindingCensusFixture(mode));
}
