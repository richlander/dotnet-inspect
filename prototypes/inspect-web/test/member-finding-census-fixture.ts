import { sampleDocument } from "../../annotated-source-viewer/src/sample-document.js";
import {
  createMemberFindingInteraction,
  type MemberFindingCensus,
} from "../src/finding-interaction.ts";
import { validateAnnotatedSourceDocument } from "../src/annotated-source-view.ts";
import type { AnnotatedSourceDocument } from "../src/document-model.ts";
import { sampleViewerCatalog } from "./annotated-source-result-fixture.ts";

export function memberFindingCensusFixture(): MemberFindingCensus {
  const sample: unknown = sampleDocument;
  validateAnnotatedSourceDocument(sample);
  const duplicate = sample.facts[0]!;
  const document: AnnotatedSourceDocument = {
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
  validateAnnotatedSourceDocument(document);
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
  return {
    factCensusReceipt: "11111111-1111-1111-1111-111111111111",
    facts: [
      { ...displayShape, instanceKey: 41 },
      { ...displayShape, instanceKey: 42 },
      {
        ...displayShape,
        anchor: "member",
        id: "member-header",
        instanceKey: null,
      },
    ],
    annotatedSource: {
      document,
      viewerCatalog: sampleViewerCatalog,
      provenance: "decompiled from IL",
      contextLimitation: null,
    },
    sourceFactInstances: [
      { factId: 0, instanceKey: 41 },
      { factId: 1, instanceKey: 42 },
    ],
  };
}

export function memberFindingInteractionFixture() {
  return createMemberFindingInteraction(memberFindingCensusFixture());
}
