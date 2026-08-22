import * as model from "../../annotated-source-viewer/src/document-model.js";

export type SourceMedium = "CSharp" | "Il";
export type LineMedium = SourceMedium | "Mixed";

interface TextSpan {
  start: number;
  length: number;
}

export interface AnnotatedSourceNode {
  id: number;
  kind: string;
  medium: SourceMedium;
  spans: readonly TextSpan[];
  il_offset?: number | null;
}

export interface AnnotatedSourceRegion {
  role: string;
  spans: readonly TextSpan[];
}

export interface AnnotatedSourceFact {
  id: number;
  descriptor: string;
  category: string;
  conditionality: string;
  detail?: string | null;
  origin: string;
  source_offset: number;
}

interface AnnotatedSourceCapture {
  parent_node_id: number;
  display_name: string;
  use_node_ids: readonly number[];
}

interface AnnotatedSourceTarget {
  fact_id: number;
  node_id: number;
}

export interface AnnotatedSourceDocument {
  text: string;
  nodes: readonly AnnotatedSourceNode[];
  regions: readonly AnnotatedSourceRegion[];
  facts: readonly AnnotatedSourceFact[];
  targets: readonly AnnotatedSourceTarget[];
  captures?: readonly AnnotatedSourceCapture[] | null;
}

export interface SourceLine {
  number: number;
  start: number;
  end: number;
  text: string;
}

export interface SourceSegment {
  start: number;
  end: number;
  text: string;
  nodeIds: number[];
  media: SourceMedium[];
  selected: boolean;
}

type ValidateDocument = (
  document: unknown,
) => asserts document is AnnotatedSourceDocument;
type NodesAtOffset = (
  document: AnnotatedSourceDocument,
  offset: number,
  medium?: SourceMedium | null,
) => AnnotatedSourceNode[];
type SegmentsForLine = (
  document: AnnotatedSourceDocument,
  line: SourceLine,
  selectedNodeIds?: readonly number[],
) => SourceSegment[];

// The portable implementation remains owned by annotated-source-viewer. Production consumes its
// validation, line derivation, and point lookup; projection aliases also serve as the test oracle.
export const validateDocument: ValidateDocument = model.validateDocument;
export const buildLines: (text: string) => SourceLine[] = model.buildLines;
export const lineMedium: (
  document: AnnotatedSourceDocument,
  line: SourceLine,
) => LineMedium = model.lineMedium;
// The portable JavaScript owner is runtime-validated but does not publish TypeScript declarations.
// oxlint-disable-next-line typescript/no-unsafe-type-assertion
export const nodesAtOffset = model.nodesAtOffset as NodesAtOffset;
// oxlint-disable-next-line typescript/no-unsafe-type-assertion
export const segmentsForLine = model.segmentsForLine as SegmentsForLine;
