import {
  nodeIdsForFact,
  nodesAtOffset,
  validateDocument,
} from "./document-model.ts";
import type {
  AnnotatedSourceDocument,
  AnnotatedSourceFact,
  AnnotatedSourceNode,
  AnnotatedSourceRegion,
  SourceMedium,
} from "./document-model.ts";
import type {
  BrowserAnnotatedSource,
  BrowserAnnotatedSourceCapabilityAvailability,
  BrowserAnnotatedSourceFindingEvidence,
  BrowserAnnotatedSourceFindingEvidenceDocument,
  BrowserAnnotatedSourceInvocationDestination,
  BrowserAnnotatedSourceViewerCatalog,
} from "./facades/inspect-web-source.d.ts";

interface AnnotatedSourceFindingEvidenceDocument
  extends Omit<BrowserAnnotatedSourceFindingEvidenceDocument, "document"> {
  document: AnnotatedSourceDocument;
}

export interface AnnotatedSourceFindingEvidence
  extends BrowserAnnotatedSourceFindingEvidence {
  document: AnnotatedSourceDocument | null;
}

export interface AnnotatedSourceResult
  extends Omit<
    BrowserAnnotatedSource,
    "document" | "findingEvidenceDocuments"
  > {
  document: AnnotatedSourceDocument;
  findingEvidenceDocuments:
    readonly AnnotatedSourceFindingEvidenceDocument[];
}

type AnnotatedSurface = "embedded" | "modal";
export type AnnotationState = "Default" | "All" | "Clear" | "Custom";

export type AnnotatedPrimary =
  | { kind: "finding"; id: number }
  | { kind: "node"; id: number }
  | null;

export interface AnnotationTargetIdentity {
  factId: number;
  nodeId: number;
  medium: SourceMedium;
}

export type FindingDetailOpener =
  | { kind: "inspector"; factId: number }
  | ({ kind: "annotation" } & AnnotationTargetIdentity);

interface FindingDetailState {
  factId: number;
  opener: FindingDetailOpener;
}

export interface AnnotatedSourceSession {
  surface: AnnotatedSurface;
  primary: AnnotatedPrimary;
  activeFindingIds: readonly number[];
  activeRegionIds: readonly number[];
  visibleMedia: readonly SourceMedium[];
  coordinatesVisible: boolean;
  detail: FindingDetailState | null;
}

export interface RenderedFindingTarget extends AnnotationTargetIdentity {
  fact: AnnotatedSourceFact;
  node: AnnotatedSourceNode;
}

export interface RenderedStructuralTarget {
  regionId: number;
  region: AnnotatedSourceRegion;
  medium: SourceMedium;
  start: number;
  length: number;
}

export interface AnnotatedSourceViewerModel {
  result: AnnotatedSourceResult;
  document: AnnotatedSourceDocument;
  catalog: BrowserAnnotatedSourceViewerCatalog;
  supportedMedia: readonly SourceMedium[];
  annotatableFindingIds: readonly number[];
  structuralRegionIds: readonly number[];
  defaultFindingIds: readonly number[];
  invocationLikeNodeKinds: ReadonlySet<string>;
  invocationDestinations:
    readonly BrowserAnnotatedSourceInvocationDestination[];
  findingEvidence: readonly AnnotatedSourceFindingEvidence[];
  findingEvidenceByFactId:
    ReadonlyMap<number, AnnotatedSourceFindingEvidence>;
}

export interface IndexedInvocationDestination {
  index: number;
  destination: BrowserAnnotatedSourceInvocationDestination;
}

export type AnnotatedFocusTarget =
  | { kind: "heading" }
  | { kind: "explore" }
  | { kind: "annotation-control"; control: "Default" | "All" | "Clear" }
  | { kind: "finding-toggle"; factId: number }
  | { kind: "medium-toggle"; medium: SourceMedium }
  | { kind: "coordinate-toggle" }
  | { kind: "inspector"; factId: number }
  | ({ kind: "annotation" } & AnnotationTargetIdentity)
  | { kind: "node"; nodeId: number };

export interface AnnotatedTransition {
  state: AnnotatedSourceSession;
  focus: AnnotatedFocusTarget;
}

export interface AnnotatedEscapeResult {
  state: AnnotatedSourceSession;
  handled: boolean;
  dismissModal: boolean;
  focus: AnnotatedFocusTarget | null;
}

export function createAnnotatedSourceViewerModel(
  result: AnnotatedSourceResult,
): AnnotatedSourceViewerModel {
  validateDocument(result.document);
  const supportedMedia = normalizeSupportedMedia(result.viewerCatalog);
  const supported = new Set(supportedMedia);
  const factIds = new Set(result.document.facts.map(fact => fact.id));
  const targetNodeByFact = new Map<number, AnnotatedSourceNode[]>();

  for (const target of result.document.targets) {
    const node = result.document.nodes[target.node_id];
    if (!node || !supported.has(node.medium)) continue;
    const nodes = targetNodeByFact.get(target.fact_id) ?? [];
    nodes.push(node);
    targetNodeByFact.set(target.fact_id, nodes);
  }

  const annotatableFindingIds = result.document.facts
    .filter(fact => (targetNodeByFact.get(fact.id)?.length ?? 0) > 0)
    .map(fact => fact.id);
  const structuralRegionIds = result.document.regions
    .map((region, id) => ({ region, id }))
    .filter(({ region }) => regionTargets(result.document, region, supported).length > 0)
    .map(({ id }) => id);
  const annotatable = new Set(annotatableFindingIds);
  const defaultFindingIds = uniqueSorted(
    result.viewerCatalog.defaultFindingIds
      .filter(id => factIds.has(id) && annotatable.has(id)),
  );
  const invocationDestinations =
    validateInvocationDestinations(result.document, result.viewerCatalog);
  const findingEvidence =
    validateFindingEvidence(
      result.document,
      result.findingEvidenceDocuments,
      result,
    );

  return {
    result,
    document: result.document,
    catalog: result.viewerCatalog,
    supportedMedia,
    annotatableFindingIds,
    structuralRegionIds,
    defaultFindingIds,
    invocationLikeNodeKinds:
      new Set(result.viewerCatalog.invocationLikeNodeKinds),
    invocationDestinations,
    findingEvidence,
    findingEvidenceByFactId:
      new Map(findingEvidence.map(evidence => [evidence.factId, evidence])),
  };
}

export function invocationDestinationForNode(
  model: AnnotatedSourceViewerModel,
  nodeId: number,
): IndexedInvocationDestination | null {
  const index = model.invocationDestinations.findIndex(
    destination => destination.nodeId === nodeId,
  );
  return index < 0
    ? null
    : {
        index,
        destination: model.invocationDestinations[index]!,
      };
}

export function findingEvidenceForFact(
  model: AnnotatedSourceViewerModel,
  factId: number,
): AnnotatedSourceFindingEvidence | null {
  return model.findingEvidenceByFactId.get(factId) ?? null;
}

export function createEmbeddedSession(
  model: AnnotatedSourceViewerModel,
): AnnotatedSourceSession {
  return {
    surface: "embedded",
    primary: null,
    activeFindingIds: [...model.defaultFindingIds],
    activeRegionIds: [],
    visibleMedia: ["CSharp"],
    coordinatesVisible: false,
    detail: null,
  };
}

export function openModalSession(
  model: AnnotatedSourceViewerModel,
  embedded: AnnotatedSourceSession,
): {
  embedded: AnnotatedSourceSession;
  modal: AnnotatedSourceSession;
  focus: AnnotatedFocusTarget;
} {
  const transferred = eligibleEmbeddedFindingId(model, embedded.primary);
  return {
    embedded: {
      ...embedded,
      detail: null,
    },
    modal: {
      surface: "modal",
      primary: transferred === null
        ? null
        : { kind: "finding", id: transferred },
      activeFindingIds: [...model.defaultFindingIds],
      activeRegionIds: [],
      visibleMedia: ["CSharp"],
      coordinatesVisible: false,
      detail: null,
    },
    focus: transferred === null
      ? { kind: "heading" }
      : { kind: "inspector", factId: transferred },
  };
}

export function dismissModalSession(
  model: AnnotatedSourceViewerModel,
  modal: AnnotatedSourceSession,
): AnnotatedSourceSession {
  const transferred = eligibleEmbeddedFindingId(model, modal.primary);
  return {
    ...createEmbeddedSession(model),
    primary: transferred === null
      ? null
      : { kind: "finding", id: transferred },
  };
}

export function annotationState(
  model: AnnotatedSourceViewerModel,
  session: AnnotatedSourceSession,
): AnnotationState {
  const active = uniqueSorted(session.activeFindingIds);
  const activeRegions = uniqueSorted(session.activeRegionIds);
  if (setsEqual(active, model.defaultFindingIds)
    && activeRegions.length === 0) return "Default";
  if (setsEqual(active, model.annotatableFindingIds)
    && setsEqual(activeRegions, model.structuralRegionIds)) return "All";
  if (active.length === 0 && activeRegions.length === 0) return "Clear";
  return "Custom";
}

export function renderedStructuralTargets(
  model: AnnotatedSourceViewerModel,
  session: AnnotatedSourceSession,
): RenderedStructuralTarget[] {
  const active = new Set(session.activeRegionIds);
  const visible = new Set(session.visibleMedia);
  const supported = new Set(model.supportedMedia);
  return model.document.regions.flatMap((region, regionId) =>
    !active.has(regionId)
      ? []
      : regionTargets(model.document, region, supported)
        .filter(target => visible.has(target.medium))
        .map(target => ({
          regionId,
          region,
          ...target,
        })));
}

export function renderedFindingTargets(
  model: AnnotatedSourceViewerModel,
  session: AnnotatedSourceSession,
): RenderedFindingTarget[] {
  const active = new Set(session.activeFindingIds);
  const visible = new Set(session.visibleMedia);
  const facts = new Map(model.document.facts.map(fact => [fact.id, fact]));
  return model.document.targets.flatMap(target => {
    const fact = facts.get(target.fact_id);
    const node = model.document.nodes[target.node_id];
    return fact
      && node
      && active.has(fact.id)
      && visible.has(node.medium)
      && model.supportedMedia.includes(node.medium)
      ? [{
          factId: fact.id,
          nodeId: node.id,
          medium: node.medium,
          fact,
          node,
        }]
      : [];
  });
}

export function selectFinding(
  session: AnnotatedSourceSession,
  opener: FindingDetailOpener,
): AnnotatedSourceSession {
  return {
    ...session,
    primary: { kind: "finding", id: opener.factId },
    detail: {
      factId: opener.factId,
      opener,
    },
  };
}

export function selectNode(
  session: AnnotatedSourceSession,
  nodeId: number,
): AnnotatedSourceSession {
  return {
    ...session,
    primary: { kind: "node", id: nodeId },
    detail: null,
  };
}

export function selectDefaultAnnotations(
  model: AnnotatedSourceViewerModel,
  session: AnnotatedSourceSession,
): AnnotatedTransition {
  return {
    state: {
      ...session,
      primary: null,
      activeFindingIds: [...model.defaultFindingIds],
      activeRegionIds: [],
      detail: null,
    },
    focus: { kind: "annotation-control", control: "Default" },
  };
}

export function selectAllAnnotations(
  model: AnnotatedSourceViewerModel,
  session: AnnotatedSourceSession,
): AnnotatedTransition {
  return {
    state: {
      ...session,
      activeFindingIds: [...model.annotatableFindingIds],
      activeRegionIds: [...model.structuralRegionIds],
    },
    focus: { kind: "annotation-control", control: "All" },
  };
}

export function clearAnnotations(
  session: AnnotatedSourceSession,
): AnnotatedTransition {
  return {
    state: {
      ...session,
      primary: null,
      activeFindingIds: [],
      activeRegionIds: [],
      detail: null,
    },
    focus: { kind: "annotation-control", control: "Clear" },
  };
}

export function toggleFindingAnnotation(
  model: AnnotatedSourceViewerModel,
  session: AnnotatedSourceSession,
  factId: number,
): AnnotatedTransition {
  if (!model.annotatableFindingIds.includes(factId)) {
    return {
      state: session,
      focus: { kind: "finding-toggle", factId },
    };
  }

  const active = new Set(session.activeFindingIds);
  if (active.has(factId)) active.delete(factId);
  else active.add(factId);
  const removedPrimary =
    !active.has(factId)
    && session.primary?.kind === "finding"
    && session.primary.id === factId;
  return {
    state: {
      ...session,
      primary: removedPrimary ? null : session.primary,
      activeFindingIds: [...active].sort(compareNumber),
      detail: removedPrimary ? null : session.detail,
    },
    focus: { kind: "finding-toggle", factId },
  };
}

export function toggleMedium(
  model: AnnotatedSourceViewerModel,
  session: AnnotatedSourceSession,
  medium: SourceMedium,
): AnnotatedTransition {
  if (!model.supportedMedia.includes(medium)) {
    return {
      state: session,
      focus: { kind: "medium-toggle", medium },
    };
  }
  const visible = new Set(session.visibleMedia);
  if (visible.has(medium)) {
    if (visible.size > 1) visible.delete(medium);
  } else {
    visible.add(medium);
  }
  return {
    state: {
      ...session,
      visibleMedia: model.supportedMedia.filter(candidate => visible.has(candidate)),
    },
    focus: { kind: "medium-toggle", medium },
  };
}

export function toggleCoordinates(
  session: AnnotatedSourceSession,
): AnnotatedTransition {
  return {
    state: {
      ...session,
      coordinatesVisible: !session.coordinatesVisible,
    },
    focus: { kind: "coordinate-toggle" },
  };
}

export function closeFindingDetail(
  model: AnnotatedSourceViewerModel,
  session: AnnotatedSourceSession,
): AnnotatedTransition {
  const detail = session.detail;
  if (!detail) {
    return {
      state: session,
      focus: { kind: "heading" },
    };
  }
  const opener = detail.opener;
  const focus = opener.kind === "annotation"
    && renderedFindingTargets(model, session).some(
      target => sameTarget(target, opener),
    )
    ? opener
    : { kind: "inspector" as const, factId: detail.factId };
  return {
    state: {
      ...session,
      detail: null,
    },
    focus,
  };
}

export function escapeAnnotatedSource(
  model: AnnotatedSourceViewerModel,
  session: AnnotatedSourceSession,
): AnnotatedEscapeResult {
  if (session.detail) {
    const closed = closeFindingDetail(model, session);
    return {
      state: closed.state,
      handled: true,
      dismissModal: false,
      focus: closed.focus,
    };
  }
  if (session.surface === "modal") {
    return {
      state: session,
      handled: true,
      dismissModal: true,
      focus: { kind: "explore" },
    };
  }
  return {
    state: session,
    handled: false,
    dismissModal: false,
    focus: null,
  };
}

export function hitTestAnnotatedNode(
  model: AnnotatedSourceViewerModel,
  offset: number,
  medium: SourceMedium,
): AnnotatedSourceNode | null {
  const nodes = nodesAtOffset(model.document, offset, medium);
  const invocation = nodes.find(node =>
    model.invocationLikeNodeKinds.has(node.kind));
  return invocation ?? nodes[0] ?? null;
}

export function factForId(
  model: AnnotatedSourceViewerModel,
  factId: number,
): AnnotatedSourceFact | null {
  return model.document.facts.find(fact => fact.id === factId) ?? null;
}

export function nodesForPrimary(
  model: AnnotatedSourceViewerModel,
  primary: AnnotatedPrimary,
): AnnotatedSourceNode[] {
  if (!primary) return [];
  if (primary.kind === "node") {
    const node = model.document.nodes[primary.id];
    return node ? [node] : [];
  }
  return nodeIdsForFact(model.document, primary.id)
    .map(id => model.document.nodes[id])
    .filter((node): node is AnnotatedSourceNode => node !== undefined);
}

export function capabilityReason(
  availability: BrowserAnnotatedSourceCapabilityAvailability,
): string {
  if (availability.available) return "Available";
  switch (availability.unavailableReason) {
    case "NotProjected":
      return "Not projected by the current product query";
    case "ContextUnavailable":
      return "The assembly analysis context was unavailable";
    case null:
      return "Unavailable";
    default:
      return `Unavailable (${String(availability.unavailableReason)})`;
  }
}

function eligibleEmbeddedFindingId(
  model: AnnotatedSourceViewerModel,
  primary: AnnotatedPrimary,
): number | null {
  if (primary?.kind !== "finding") return null;
  if (!model.defaultFindingIds.includes(primary.id)) return null;
  return model.document.targets.some(target =>
    target.fact_id === primary.id
    && model.document.nodes[target.node_id]?.medium === "CSharp")
    ? primary.id
    : null;
}

function regionTargets(
  document: AnnotatedSourceDocument,
  region: AnnotatedSourceRegion,
  supported: ReadonlySet<SourceMedium>,
): Array<{ medium: SourceMedium; start: number; length: number }> {
  const targets: Array<{
    medium: SourceMedium;
    start: number;
    length: number;
  }> = [];
  for (const span of region.spans) {
    for (const medium of supported) {
      const intersects = document.nodes.some(node =>
        node.medium === medium
        && node.spans.some(nodeSpan =>
          spansIntersect(span.start, span.length, nodeSpan.start, nodeSpan.length)));
      if (intersects) {
        targets.push({
          medium,
          start: span.start,
          length: span.length,
        });
      }
    }
  }
  return targets;
}

function spansIntersect(
  leftStart: number,
  leftLength: number,
  rightStart: number,
  rightLength: number,
): boolean {
  return leftStart < rightStart + rightLength
    && rightStart < leftStart + leftLength;
}

function normalizeSupportedMedia(
  catalog: BrowserAnnotatedSourceViewerCatalog,
): SourceMedium[] {
  const media: SourceMedium[] = [];
  for (const value of catalog.supportedMedia) {
    if (value === "CSharp" || value === "Il") media.push(value);
  }
  const normalized = [...new Set(media)];
  if (!normalized.includes("CSharp")) {
    throw new Error("Annotated Source viewer catalog must support CSharp");
  }
  return (["CSharp", "Il"] as const).filter(candidate =>
    normalized.includes(candidate));
}

function validateInvocationDestinations(
  document: AnnotatedSourceDocument,
  catalog: BrowserAnnotatedSourceViewerCatalog,
): readonly BrowserAnnotatedSourceInvocationDestination[] {
  if (!catalog.destinations.available
    && catalog.invocationDestinations.length > 0) {
    throw new TypeError(
      "Unavailable Annotated Source destinations cannot carry rows.");
  }
  const nodeIds = new Set<number>();
  return catalog.invocationDestinations.map((destination, index) => {
    if (!Number.isSafeInteger(destination.nodeId)
      || destination.nodeId < 0
      || destination.nodeId >= document.nodes.length) {
      throw new TypeError(
        `Annotated Source destination ${index} names a node that does not exist.`);
    }
    const node = document.nodes[destination.nodeId]!;
    if (node.medium !== "CSharp" || node.kind !== "InvocationExpression") {
      throw new TypeError(
        `Annotated Source destination ${index} does not name a C# invocation.`);
    }
    if (nodeIds.has(destination.nodeId)) {
      throw new TypeError(
        `Annotated Source destination node ${destination.nodeId} is duplicated.`);
    }
    nodeIds.add(destination.nodeId);
    validateDestinationTarget(destination.target, index);
    return destination;
  });
}

function validateFindingEvidence(
  callerDocument: AnnotatedSourceDocument,
  evidenceDocuments:
    readonly AnnotatedSourceFindingEvidenceDocument[],
  result: AnnotatedSourceResult,
): readonly AnnotatedSourceFindingEvidence[] {
  const evidenceRows = result.findingEvidence;
  if (!result.viewerCatalog.findingEvidence.available
    && evidenceRows.length > 0) {
    throw new TypeError(
      "Unavailable Annotated Source Finding evidence cannot carry rows.");
  }
  const documentsById = new Map<number, AnnotatedSourceDocument>();
  for (const [index, entry] of evidenceDocuments.entries()) {
    if (!Number.isSafeInteger(entry.id)
      || entry.id < 0
      || documentsById.has(entry.id)) {
      throw new TypeError(
        `Annotated Source Finding evidence document ${index} has an invalid or duplicate id.`);
    }
    validateDocument(entry.document);
    documentsById.set(entry.id, entry.document);
  }
  const factIds = new Set<number>();
  const instanceKeys = new Set<number>();
  const referencedDocumentIds = new Set<number>();
  const validated = evidenceRows.map((evidence, index) => {
    if (!Number.isSafeInteger(evidence.factId)
      || !Number.isSafeInteger(evidence.instanceKey)
      || evidence.instanceKey <= 0
      || factIds.has(evidence.factId)
      || instanceKeys.has(evidence.instanceKey)) {
      throw new TypeError(
        `Annotated Source Finding evidence ${index} has an invalid or duplicate identity.`);
    }
    const fact = callerDocument.facts[evidence.factId];
    if (!fact
      || fact.origin !== "Body"
      || (fact.descriptor !== "semantics.callee"
        && fact.descriptor !== "safety.callee")) {
      throw new TypeError(
        `Annotated Source Finding evidence ${index} does not name an instruction-level callee Finding.`);
    }
    if (!nonEmptyString(evidence.member)) {
      throw new TypeError(
        `Annotated Source Finding evidence ${index} has no callee member.`);
    }
    validateCallGraphTarget(evidence.target, `Finding evidence ${index}`);
    validateCalleeEvidenceTarget(evidence.target, index);
    if (!isEvidenceCoordinates(evidence.coordinates)
      || !isEvidenceNodeIds(evidence.nodeIds)
      || new Set(evidence.nodeIds).size !== evidence.nodeIds.length) {
      throw new TypeError(
        `Annotated Source Finding evidence ${index} has invalid coordinates or node ids.`);
    }
    if (evidence.documentId !== null
      && (!Number.isSafeInteger(evidence.documentId)
        || evidence.documentId < 0
        || !documentsById.has(evidence.documentId))) {
      throw new TypeError(
        `Annotated Source Finding evidence ${index} names no callee document.`);
    }
    const evidenceDocument = evidence.documentId === null
      ? null
      : documentsById.get(evidence.documentId)!;
    if (evidence.documentId !== null) {
      referencedDocumentIds.add(evidence.documentId);
    }

    const unavailable = nonEmptyString(evidence.unavailableReason);
    if (unavailable) {
      if (evidence.nodeIds.length > 0) {
        throw new TypeError(
          `Unavailable Annotated Source Finding evidence ${index} cannot carry node ids.`);
      }
      if (evidenceDocument !== null) {
        if (evidence.coordinates.length === 0) {
          throw new TypeError(
            `Unavailable Annotated Source Finding evidence ${index} cannot carry a document without coordinates.`);
        }
        const correspondence = findEvidenceNodeIds(
          evidenceDocument,
          evidence.coordinates,
          index,
        );
        if (correspondence.failure === null) {
          throw new TypeError(
            `Annotated Source Finding evidence ${index} is unavailable despite exact serialized correspondence.`);
        }
      }
    } else {
      if (evidenceDocument === null
        || evidence.coordinates.length === 0
        || evidence.nodeIds.length === 0) {
        throw new TypeError(
          `Available Annotated Source Finding evidence ${index} requires a document, coordinates, and node ids.`);
      }
      const correspondence = findEvidenceNodeIds(
        evidenceDocument,
        evidence.coordinates,
        index,
      );
      if (correspondence.failure !== null) {
        throw new TypeError(correspondence.failure);
      }
      const expectedNodeIds = correspondence.nodeIds;
      if (evidence.nodeIds.length !== expectedNodeIds.length
        || evidence.nodeIds.some((nodeId, nodeIndex) =>
          nodeId !== expectedNodeIds[nodeIndex])) {
        throw new TypeError(
          `Annotated Source Finding evidence ${index} node ids do not equal its exact coordinate matches.`);
      }
    }
    factIds.add(evidence.factId);
    instanceKeys.add(evidence.instanceKey);
    return {
      ...evidence,
      document: evidenceDocument,
    };
  });
  if (result.viewerCatalog.findingEvidence.available) {
    const eligibleFactIds = callerDocument.facts
      .filter(fact =>
        fact.origin === "Body"
        && (fact.descriptor === "semantics.callee"
          || fact.descriptor === "safety.callee"))
      .map(fact => fact.id);
    if (eligibleFactIds.some(factId => !factIds.has(factId))) {
      throw new TypeError(
        "Annotated Source Finding evidence does not cover every instruction-level callee Finding.");
    }
  }
  if (evidenceDocuments.some(entry => !referencedDocumentIds.has(entry.id))) {
    throw new TypeError(
        "Annotated Source carries an unreferenced callee evidence document.");
  }
  return validated;
}

function validateDestinationTarget(
  target: BrowserAnnotatedSourceInvocationDestination["target"],
  index: number,
): void {
  validateCallGraphTarget(target, `destination ${index}`);
}

function validateCallGraphTarget(
  target: BrowserAnnotatedSourceInvocationDestination["target"],
  label: string,
): void {
  if (!target
    || !nonEmptyString(target.id)
    || !nonEmptyString(target.assembly)
    || !nonEmptyString(target.typeFullName)
    || !nonEmptyString(target.memberName)
    || !nonEmptyString(target.selectorKey)
    || !Array.isArray(target.parameterTypes)
    || !target.parameterTypes.every(value => typeof value === "string")
    || !Number.isSafeInteger(target.genericArity)
    || target.genericArity < 0) {
    throw new TypeError(
      `Annotated Source ${label} has an invalid typed target.`);
  }
}

function validateCalleeEvidenceTarget(
  target: BrowserAnnotatedSourceInvocationDestination["target"],
  index: number,
): void {
  if (!nonEmptyString(target.typeDefinitionId)
    || !nonEmptyString(target.returnType)
    || !Number.isSafeInteger(target.metadataToken)
    || target.metadataToken === null
    || (target.metadataToken & 0xFF000000) !== 0x06000000
    || target.kind !== "method") {
    throw new TypeError(
      `Annotated Source Finding evidence ${index} has an incomplete callee target.`);
  }
}

function isEvidenceKind(value: unknown): boolean {
  return value === "ExceptionConstruction"
    || value === "Localloc"
    || value === "Calli";
}

function isEvidenceCoordinates(
  value: unknown,
): value is AnnotatedSourceFindingEvidence["coordinates"] {
  if (!Array.isArray(value)) return false;
  const coordinates: unknown[] = value;
  return coordinates.every(coordinate => {
    if (typeof coordinate !== "object"
      || coordinate === null
      || Array.isArray(coordinate)) {
      return false;
    }
    const ilOffset: unknown = Reflect.get(coordinate, "ilOffset");
    const kind: unknown = Reflect.get(coordinate, "kind");
    return typeof ilOffset === "number"
      && Number.isSafeInteger(ilOffset)
      && ilOffset >= 0
      && isEvidenceKind(kind);
  });
}

function isEvidenceNodeIds(value: unknown): value is readonly number[] {
  if (!Array.isArray(value)) return false;
  const nodeIds: unknown[] = value;
  return nodeIds.every(nodeId =>
    Number.isSafeInteger(nodeId)
      && typeof nodeId === "number"
      && nodeId >= 0);
}

function evidenceNodeKind(
  kind: AnnotatedSourceFindingEvidence["coordinates"][number]["kind"],
): string {
  switch (kind) {
    case "ExceptionConstruction":
      return "ObjectCreationExpression";
    case "Localloc":
      return "StackAllocationExpression";
    case "Calli":
      return "IndirectInvocationExpression";
    default:
      throw new TypeError(`Unknown callee evidence kind '${String(kind)}'.`);
  }
}

function nonEmptyString(value: unknown): value is string {
  return typeof value === "string" && value.length > 0;
}

function findEvidenceNodeIds(
  document: AnnotatedSourceDocument,
  coordinates: AnnotatedSourceFindingEvidence["coordinates"],
  evidenceIndex: number,
): { readonly nodeIds: readonly number[]; readonly failure: null }
  | { readonly nodeIds: null; readonly failure: string } {
  const matchedNodeIds: number[] = [];
  for (const coordinate of coordinates) {
    const expectedKind = evidenceNodeKind(coordinate.kind);
    const matches = document.nodes.filter(node =>
      node.medium === "CSharp"
      && node.kind === expectedKind
      && node.provenance?.il_offsets.includes(coordinate.ilOffset) === true);
    if (matches.length !== 1) {
      return {
        nodeIds: null,
        failure:
          `Annotated Source Finding evidence ${evidenceIndex} coordinate IL_${coordinate.ilOffset.toString(16).toUpperCase().padStart(4, "0")} matches ${matches.length} ${expectedKind} nodes.`,
      };
    }
    matchedNodeIds.push(matches[0]!.id);
  }
  return {
    nodeIds: uniqueSorted(matchedNodeIds),
    failure: null,
  };
}

function uniqueSorted(values: readonly number[]): number[] {
  return [...new Set(values)].sort(compareNumber);
}

function compareNumber(left: number, right: number): number {
  return left - right;
}

function setsEqual(
  left: readonly number[],
  right: readonly number[],
): boolean {
  if (left.length !== right.length) return false;
  return left.every((value, index) => value === right[index]);
}

function sameTarget(
  left: AnnotationTargetIdentity,
  right: AnnotationTargetIdentity,
): boolean {
  return left.factId === right.factId
    && left.nodeId === right.nodeId
    && left.medium === right.medium;
}
