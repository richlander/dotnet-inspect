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
  BrowserAnnotatedSourceAllocationExceptionPath,
  BrowserAnnotatedSourceAllocationExceptionPathInspection,
  BrowserAnnotatedSourceAwaitCompletionPath,
  BrowserAnnotatedSourceAwaitCompletionPathInspection,
  BrowserAnnotatedSourceCallCycle,
  BrowserAnnotatedSourceCallCycleInspection,
  BrowserAnnotatedSourceCallRelationship,
  BrowserAnnotatedSourceCapabilityAvailability,
  BrowserAnnotatedSourceFindingEvidence,
  BrowserAnnotatedSourceFindingEvidenceDocument,
  BrowserAnnotatedSourceInvocationDestination,
  BrowserAnnotatedSourceLocalThrowPath,
  BrowserAnnotatedSourceLocalThrowPathInspection,
  BrowserAnnotatedSourceSynchronousCompletion,
  BrowserAnnotatedSourceSynchronousCompletionInspection,
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
export type RelationshipPresentation = "Table" | "Diagram";

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
  | { kind: "relationship"; factId: number }
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
  relationshipPresentation: RelationshipPresentation;
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
  callRelationships: readonly BrowserAnnotatedSourceCallRelationship[];
  callCycles: BrowserAnnotatedSourceCallCycleInspection;
  callCyclesByFactId:
    ReadonlyMap<number, readonly BrowserAnnotatedSourceCallCycle[]>;
  synchronousCompletions:
    BrowserAnnotatedSourceSynchronousCompletionInspection;
  synchronousCompletionsByFactId:
    ReadonlyMap<number, BrowserAnnotatedSourceSynchronousCompletion>;
  awaitCompletionPaths:
    BrowserAnnotatedSourceAwaitCompletionPathInspection;
  awaitCompletionPathsByNodeId:
    ReadonlyMap<number, BrowserAnnotatedSourceAwaitCompletionPath>;
  allocationExceptionPaths:
    BrowserAnnotatedSourceAllocationExceptionPathInspection;
  allocationExceptionPathsByFactId:
    ReadonlyMap<number, BrowserAnnotatedSourceAllocationExceptionPath>;
  localThrowPaths:
    BrowserAnnotatedSourceLocalThrowPathInspection;
  localThrowPathsByFactId:
    ReadonlyMap<number, readonly BrowserAnnotatedSourceLocalThrowPath[]>;
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
  | { kind: "relationship-presentation"; value: RelationshipPresentation }
  | { kind: "inspector"; factId: number }
  | { kind: "relationship"; factId: number }
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
  const callRelationships =
    validateCallRelationships(result.document, result);
  const callCycles =
    validateCallCycles(result.document, result, callRelationships);
  const synchronousCompletions =
    validateSynchronousCompletions(result, callRelationships);
  const awaitCompletionPaths =
    validateAwaitCompletionPaths(result);
  const allocationExceptionPaths =
    validateAllocationExceptionPaths(result);
  const localThrowPaths =
    validateLocalThrowPaths(result, callRelationships);
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
    callRelationships,
    callCycles,
    callCyclesByFactId: indexCallCycles(callCycles),
    synchronousCompletions,
    synchronousCompletionsByFactId:
      new Map(synchronousCompletions.observations.map(
        observation => [observation.factId, observation])),
    awaitCompletionPaths,
    awaitCompletionPathsByNodeId:
      new Map(awaitCompletionPaths.observations.map(
        observation => [observation.nodeId, observation])),
    allocationExceptionPaths,
    allocationExceptionPathsByFactId:
      new Map(allocationExceptionPaths.observations.map(
        observation => [observation.factId, observation])),
    localThrowPaths,
    localThrowPathsByFactId:
      indexLocalThrowPaths(localThrowPaths),
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

export function callCyclesForFact(
  model: AnnotatedSourceViewerModel,
  factId: number,
): readonly BrowserAnnotatedSourceCallCycle[] {
  return model.callCyclesByFactId.get(factId) ?? [];
}

export function synchronousCompletionForFact(
  model: AnnotatedSourceViewerModel,
  factId: number,
): BrowserAnnotatedSourceSynchronousCompletion | null {
  return model.synchronousCompletionsByFactId.get(factId) ?? null;
}

export function localThrowPathsForFact(
  model: AnnotatedSourceViewerModel,
  factId: number,
): readonly BrowserAnnotatedSourceLocalThrowPath[] {
  return model.localThrowPathsByFactId.get(factId) ?? [];
}

export function awaitCompletionPathForNode(
  model: AnnotatedSourceViewerModel,
  nodeId: number,
): BrowserAnnotatedSourceAwaitCompletionPath | null {
  return model.awaitCompletionPathsByNodeId.get(nodeId) ?? null;
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
    relationshipPresentation: "Table",
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
      relationshipPresentation: "Table",
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

export function selectRelationshipPresentation(
  session: AnnotatedSourceSession,
  value: RelationshipPresentation,
): AnnotatedTransition {
  return {
    state: {
      ...session,
      relationshipPresentation: value,
    },
    focus: { kind: "relationship-presentation", value },
  };
}

export function showRelationshipOccurrences(
  session: AnnotatedSourceSession,
  factId: number,
): AnnotatedTransition {
  return {
    state: {
      ...session,
      relationshipPresentation: "Table",
    },
    focus: { kind: "relationship", factId },
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
  const focus =
    opener.kind === "annotation"
      && renderedFindingTargets(model, session).some(
        target => sameTarget(target, opener),
      )
      ? opener
      : opener.kind === "relationship"
        && model.callRelationships.some(
          relationship => relationship.factId === opener.factId)
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

function validateCallRelationships(
  document: AnnotatedSourceDocument,
  result: AnnotatedSourceResult,
): readonly BrowserAnnotatedSourceCallRelationship[] {
  const rows = result.callRelationships;
  if (!result.viewerCatalog.callRelationships.available && rows.length > 0) {
    throw new TypeError(
      "Unavailable Annotated Source call relationships cannot carry rows.");
  }

  const factIds = new Set<number>();
  const physicalKeys = new Set<string>();
  const validated = rows.map((relationship, index) => {
    const fact = document.facts[relationship.factId];
    if (!Number.isSafeInteger(relationship.edgeRow)
      || relationship.edgeRow < 1
      || !Number.isSafeInteger(relationship.factId)
      || factIds.has(relationship.factId)
      || !fact
      || fact.origin !== "Body"
      || fact.descriptor !== "call.edge"
      || fact.source_offset !== relationship.ilOffset
      || nodeIdsForFact(document, relationship.factId).length === 0) {
      throw new TypeError(
        `Annotated Source call relationship ${index} does not name one targeted call.edge fact.`);
    }
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu
        .test(relationship.moduleVersionId)
      || !Number.isSafeInteger(relationship.callerToken)
      || (relationship.callerToken & 0xFF000000) !== 0x06000000
      || !Number.isSafeInteger(relationship.ilOffset)
      || relationship.ilOffset < 0
      || !Number.isSafeInteger(relationship.operandToken)
      || relationship.operandToken <= 0
      || !isCallKind(relationship.kind)
      || typeof relationship.inLoop !== "boolean") {
      throw new TypeError(
        `Annotated Source call relationship ${index} has invalid physical evidence.`);
    }
    const physicalKey = [
      relationship.moduleVersionId,
      relationship.callerToken,
      relationship.ilOffset,
      relationship.operandToken,
    ].join("|");
    if (physicalKeys.has(physicalKey)) {
      throw new TypeError(
        `Annotated Source call relationship ${index} duplicates one physical occurrence.`);
    }
    validateCallGraphTarget(
      relationship.target,
      `call relationship ${index}`);
    factIds.add(relationship.factId);
    physicalKeys.add(physicalKey);
    return relationship;
  });

  if (result.viewerCatalog.callRelationships.available) {
    const relationshipFactIds = document.facts
      .filter(fact =>
        fact.origin === "Body"
        && fact.descriptor === "call.edge")
      .map(fact => fact.id);
    if (relationshipFactIds.length !== factIds.size
      || relationshipFactIds.some(factId => !factIds.has(factId))) {
      throw new TypeError(
        "Annotated Source call relationships do not cover every call.edge Finding.");
    }
  }
  return validated;
}

function validateCallCycles(
  document: AnnotatedSourceDocument,
  result: AnnotatedSourceResult,
  relationships: readonly BrowserAnnotatedSourceCallRelationship[],
): BrowserAnnotatedSourceCallCycleInspection {
  const inspection = result.viewerCatalog.callCycles;
  if (!inspection.available) {
    if (inspection.unavailableReason === null
      || inspection.isComplete
      || inspection.limits.length > 0
      || inspection.findings.length > 0) {
      throw new TypeError(
        "Unavailable Annotated Source call cycles cannot carry findings or completeness state.");
    }
    return inspection;
  }
  if (inspection.unavailableReason !== null
    || inspection.isComplete !== (inspection.limits.length === 0)) {
    throw new TypeError(
      "Annotated Source call-cycle availability contradicts its completeness state.");
  }
  if (!result.viewerCatalog.callRelationships.available) {
    throw new TypeError(
      "Available Annotated Source call cycles require call relationships.");
  }

  const knownLimits = new Set([
    "TraversalBoundary",
    "IncompleteCorrespondence",
    "WitnessBudget",
    "PathBudget",
    "AnalysisFailure",
  ]);
  if (new Set(inspection.limits).size !== inspection.limits.length
    || inspection.limits.some(limit =>
      typeof limit !== "string" || !knownLimits.has(limit))) {
    throw new TypeError(
      "Annotated Source call cycles carry unknown or duplicate limits.");
  }

  const findingKeys = new Set<string>();
  for (const [index, finding] of inspection.findings.entries()) {
    if (finding.ordinal !== index
      || !nonEmptyString(finding.findingKey)
      || findingKeys.has(finding.findingKey)
      || finding.edgeRows.length === 0
      || finding.edgeRows.some(edgeRow =>
        !Number.isSafeInteger(edgeRow) || edgeRow < 1)
      || new Set(finding.edgeRows).size !== finding.edgeRows.length
      || finding.factIds.length === 0
      || finding.factIds.some(factId =>
        !Number.isSafeInteger(factId)
        || !document.facts[factId]
        || document.facts[factId]?.descriptor !== "call.edge")
      || new Set(finding.factIds).size !== finding.factIds.length
      || finding.targets.length !== finding.edgeRows.length) {
      throw new TypeError(
        `Annotated Source call cycle ${index} has invalid identity or path evidence.`);
    }

    const expectedFactIds = relationships
      .filter(relationship =>
        relationship.edgeRow === finding.edgeRows[0])
      .map(relationship => relationship.factId);
    const actualFactIds = new Set(finding.factIds);
    if (expectedFactIds.length === 0
      || expectedFactIds.length !== actualFactIds.size
      || expectedFactIds.some(factId => !actualFactIds.has(factId))) {
      throw new TypeError(
        `Annotated Source call cycle ${index} is not anchored to every physical occurrence of its first edge.`);
    }
    finding.targets.forEach((target, targetIndex) =>
      validateCallGraphTarget(
        target,
        `call cycle ${index} target ${targetIndex}`));
    findingKeys.add(finding.findingKey);
  }
  return inspection;
}

function indexCallCycles(
  inspection: BrowserAnnotatedSourceCallCycleInspection,
): ReadonlyMap<number, readonly BrowserAnnotatedSourceCallCycle[]> {
  const indexed = new Map<number, BrowserAnnotatedSourceCallCycle[]>();
  for (const finding of inspection.findings) {
    for (const factId of finding.factIds) {
      const findings = indexed.get(factId) ?? [];
      findings.push(finding);
      indexed.set(factId, findings);
    }
  }
  return indexed;
}

function validateSynchronousCompletions(
  result: AnnotatedSourceResult,
  relationships: readonly BrowserAnnotatedSourceCallRelationship[],
): BrowserAnnotatedSourceSynchronousCompletionInspection {
  const inspection = result.viewerCatalog.synchronousCompletions;
  if (!inspection.available) {
    if (inspection.unavailableReason === null
      || inspection.observations.length > 0) {
      throw new TypeError(
        "Unavailable Annotated Source synchronous completions cannot carry observations.");
    }
    return inspection;
  }
  if (inspection.unavailableReason !== null
    || !result.viewerCatalog.callRelationships.available) {
    throw new TypeError(
      "Available Annotated Source synchronous completions require call relationships.");
  }

  const knownKinds = new Set([
    "TaskWait",
    "TaskResult",
    "TaskAwaiterGetResult",
  ]);
  const relationshipFactIds = new Set(
    relationships.map(relationship => relationship.factId),
  );
  const observedFactIds = new Set<number>();
  for (const [index, observation] of inspection.observations.entries()) {
    if (!Number.isSafeInteger(observation.factId)
      || !relationshipFactIds.has(observation.factId)
      || observedFactIds.has(observation.factId)
      || typeof observation.kind !== "string"
      || !knownKinds.has(observation.kind)) {
      throw new TypeError(
        `Annotated Source synchronous completion ${index} has invalid or duplicate relationship evidence.`);
    }
    observedFactIds.add(observation.factId);
  }
  return inspection;
}

function validateAwaitCompletionPaths(
  result: AnnotatedSourceResult,
): BrowserAnnotatedSourceAwaitCompletionPathInspection {
  const inspection = result.viewerCatalog.awaitCompletionPaths;
  if (!inspection.available) {
    if (inspection.unavailableReason === null
      || inspection.observations.length > 0) {
      throw new TypeError(
        "Unavailable Annotated Source await completion paths cannot carry observations.");
    }
    return inspection;
  }

  if (inspection.unavailableReason !== null) {
    throw new TypeError(
      "Available Annotated Source await completion paths cannot carry an unavailable reason.");
  }

  const observedNodeIds = new Set<number>();
  for (const [index, observation] of inspection.observations.entries()) {
    const node = result.document.nodes[observation.nodeId];
    if (!Number.isSafeInteger(observation.nodeId)
      || observation.nodeId < 0
      || !node
      || node.medium !== "CSharp"
      || node.kind !== "AwaitExpression"
      || observedNodeIds.has(observation.nodeId)) {
      throw new TypeError(
        `Annotated Source await completion path ${index} does not name a unique C# AwaitExpression node.`);
    }
    observedNodeIds.add(observation.nodeId);
  }
  return inspection;
}

function validateAllocationExceptionPaths(
  result: AnnotatedSourceResult,
): BrowserAnnotatedSourceAllocationExceptionPathInspection {
  const inspection = result.viewerCatalog.allocationExceptionPaths;
  if (!inspection.available) {
    if (inspection.unavailableReason === null
      || inspection.observations.length > 0) {
      throw new TypeError(
        "Unavailable Annotated Source allocation exception paths cannot carry observations.");
    }
    return inspection;
  }
  if (inspection.unavailableReason !== null) {
    throw new TypeError(
      "Available Annotated Source allocation exception paths cannot carry an unavailable reason.");
  }

  const knownKinds = new Set([
    "ThrownValue",
    "ExceptionHandler",
  ]);
  const allocationDescriptors = new Set([
    "alloc.box",
    "alloc.array",
    "alloc.new",
    "alloc.closure",
    "alloc.statemachine",
    "alloc.delegate",
    "alloc.enumerator",
  ]);
  const observedFactIds = new Set<number>();
  for (const [index, observation] of inspection.observations.entries()) {
    const fact = result.document.facts[observation.factId];
    if (!Number.isSafeInteger(observation.factId)
      || observation.factId < 0
      || !fact
      || fact.origin !== "Body"
      || !allocationDescriptors.has(fact.descriptor)
      || observedFactIds.has(observation.factId)
      || typeof observation.kind !== "string"
      || !knownKinds.has(observation.kind)) {
      throw new TypeError(
        `Annotated Source allocation exception path ${index} does not name unique typed allocation evidence.`);
    }
    observedFactIds.add(observation.factId);
  }
  return inspection;
}

function validateLocalThrowPaths(
  result: AnnotatedSourceResult,
  relationships: readonly BrowserAnnotatedSourceCallRelationship[],
): BrowserAnnotatedSourceLocalThrowPathInspection {
  const inspection = result.viewerCatalog.localThrowPaths;
  if (!inspection.available) {
    if (inspection.unavailableReason === null
      || inspection.isComplete
      || inspection.boundaries.length > 0
      || inspection.limits !== null
      || inspection.receipt !== null
      || inspection.paths.length > 0) {
      throw new TypeError(
        "Unavailable Annotated Source local throw paths cannot carry evidence or completeness state.");
    }
    return inspection;
  }
  if (inspection.unavailableReason !== null
    || !result.viewerCatalog.callRelationships.available
    || inspection.limits === null
    || inspection.receipt === null
    || inspection.isComplete !== (inspection.boundaries.length === 0)) {
    throw new TypeError(
      "Available Annotated Source local throw paths require relationships, limits, a receipt, and matching completeness.");
  }

  const limits = inspection.limits;
  const receipt = inspection.receipt;
  if (!Number.isSafeInteger(limits.maximumDepth)
    || limits.maximumDepth < 0
    || !Number.isSafeInteger(limits.maximumNodes)
    || limits.maximumNodes < 1
    || !Number.isSafeInteger(limits.maximumEdges)
    || limits.maximumEdges < 1
    || !Number.isSafeInteger(limits.maximumPaths)
    || limits.maximumPaths < 1
    || !Number.isSafeInteger(receipt.destinationSearches)
    || receipt.destinationSearches < 0
    || !Number.isSafeInteger(receipt.searchNodes)
    || receipt.searchNodes < 0
    || !Number.isSafeInteger(receipt.searchedEdges)
    || receipt.searchedEdges < 0
    || !Number.isSafeInteger(receipt.observedReachablePairs)
    || receipt.observedReachablePairs < 0
    || receipt.returnedPaths !== inspection.paths.length) {
    throw new TypeError(
      "Annotated Source local throw path limits or receipt are invalid.");
  }

  const knownBoundaries = new Set([
    "AnalysisIncomplete",
    "TraversalBoundary",
    "PartialMethodEvidenceScope",
    "UnresolvedLocalCalls",
    "UnattributedGeneratedBodies",
    "DepthLimit",
    "NodeBudget",
    "EdgeBudget",
    "PathBudget",
    "IncompleteLocalThrowEvidence",
    "IncompleteCorrespondence",
  ]);
  const boundaryKinds = new Set<string>();
  for (const [index, boundary] of inspection.boundaries.entries()) {
    if (typeof boundary.kind !== "string"
      || !knownBoundaries.has(boundary.kind)
      || boundaryKinds.has(boundary.kind)
      || !Number.isSafeInteger(boundary.value)
      || boundary.value < 0) {
      throw new TypeError(
        `Annotated Source local throw path boundary ${index} is invalid or duplicate.`);
    }
    boundaryKinds.add(boundary.kind);
  }

  const relationshipsByFact = new Map(
    relationships.map(relationship =>
      [relationship.factId, relationship] as const),
  );
  for (const [index, path] of inspection.paths.entries()) {
    const anchored = path.factIds.map(factId =>
      relationshipsByFact.get(factId));
    if (path.factIds.length === 0
      || new Set(path.factIds).size !== path.factIds.length
      || anchored.some(relationship => relationship === undefined)
      || path.targets.length === 0
      || path.targets.length > limits.maximumDepth
      || path.terminalThrows.length === 0) {
      throw new TypeError(
        `Annotated Source local throw path ${index} has invalid source or path evidence.`);
    }
    const edgeRows = new Set(
      anchored.map(relationship => relationship!.edgeRow),
    );
    if (edgeRows.size !== 1) {
      throw new TypeError(
        `Annotated Source local throw path ${index} spans more than one first edge.`);
    }
    const edgeRow = anchored[0]!.edgeRow;
    const expectedFactIds = relationships
      .filter(relationship => relationship.edgeRow === edgeRow)
      .map(relationship => relationship.factId);
    const actualFactIds = new Set(path.factIds);
    if (expectedFactIds.length !== actualFactIds.size
      || expectedFactIds.some(factId => !actualFactIds.has(factId))) {
      throw new TypeError(
        `Annotated Source local throw path ${index} is not anchored to every physical occurrence of its first edge.`);
    }
    path.targets.forEach((target, targetIndex) =>
      validateCallGraphTarget(
        target,
        `local throw path ${index} target ${targetIndex}`));
    for (const [siteIndex, site] of path.terminalThrows.entries()) {
      if (!nonEmptyString(site.exceptionType)
        || !nonEmptyString(site.definitionModuleVersionId)
        || !Number.isSafeInteger(site.definitionToken)
        || (site.definitionToken & 0xff000000) !== 0x02000000
        || !Number.isSafeInteger(site.constructionOffset)
        || site.constructionOffset < 0
        || !Number.isSafeInteger(site.constructorToken)
        || site.constructorToken <= 0
        || !Number.isSafeInteger(site.throwOffset)
        || site.throwOffset < 0) {
        throw new TypeError(
          `Annotated Source local throw path ${index} terminal site ${siteIndex} is invalid.`);
      }
    }
  }
  return inspection;
}

function indexLocalThrowPaths(
  inspection: BrowserAnnotatedSourceLocalThrowPathInspection,
): ReadonlyMap<number, readonly BrowserAnnotatedSourceLocalThrowPath[]> {
  const indexed =
    new Map<number, BrowserAnnotatedSourceLocalThrowPath[]>();
  for (const path of inspection.paths) {
    for (const factId of path.factIds) {
      const paths = indexed.get(factId) ?? [];
      paths.push(path);
      indexed.set(factId, paths);
    }
  }
  return indexed;
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
      || (fact.descriptor !== "cost.callee"
        && fact.descriptor !== "semantics.callee"
        && fact.descriptor !== "safety.callee")) {
      throw new TypeError(
        `Annotated Source Finding evidence ${index} does not name a callee Finding.`);
    }
    if (!nonEmptyString(evidence.member)) {
      throw new TypeError(
        `Annotated Source Finding evidence ${index} has no callee member.`);
    }
    validateCallGraphTarget(evidence.target, `Finding evidence ${index}`);
    validateCalleeEvidenceTarget(evidence.target, index);
    if (!Array.isArray(evidence.aggregateInputs)
      || !isEvidenceCoordinates(evidence.coordinates)
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
    if (fact.descriptor === "cost.callee") {
      if (evidence.state !== "Method"
        || evidence.coordinates.length !== 0
        || evidence.documentId !== null
        || evidenceDocument !== null
        || evidence.nodeIds.length !== 0
        || evidence.unavailableReason !== null
        || !isCostAggregateInputs(evidence.aggregateInputs)) {
        throw new TypeError(
          `Method-level Annotated Source Finding evidence ${index} carries an instruction projection or invalid aggregate inputs.`);
      }
    } else {
      if ((evidence.state !== "Instruction"
          && evidence.state !== "InstructionUnavailable")
        || evidence.aggregateInputs.length !== 0
        || (evidence.state === "Instruction"
          && evidence.coordinates.length === 0)
        || (evidence.state === "InstructionUnavailable"
          && (evidence.coordinates.length !== 0 || !unavailable))) {
        throw new TypeError(
          `Instruction-level Annotated Source Finding evidence ${index} carries an invalid evidence state.`);
      }
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
        && (fact.descriptor === "cost.callee"
          || fact.descriptor === "semantics.callee"
          || fact.descriptor === "safety.callee"))
      .map(fact => fact.id);
    if (eligibleFactIds.some(factId => !factIds.has(factId))) {
      throw new TypeError(
        "Annotated Source Finding evidence does not cover every callee Finding.");
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

function isCostAggregateInputs(
  value: unknown,
): value is AnnotatedSourceFindingEvidence["aggregateInputs"] {
  if (!Array.isArray(value) || value.length === 0) return false;
  const order = [
    "AllocationInLoop",
    "Reflection",
    "CallInLoop",
    "RootReach",
    "DirectCallers",
    "LoopCalls",
  ];
  let previous = -1;
  return value.every(input => {
    if (typeof input !== "object"
      || input === null
      || Array.isArray(input)) {
      return false;
    }
    const kind: unknown = Reflect.get(input, "kind");
    const inputValue: unknown = Reflect.get(input, "value");
    const position = typeof kind === "string" ? order.indexOf(kind) : -1;
    if (position <= previous) return false;
    previous = position;
    const counted = kind === "Reflection"
      || kind === "RootReach"
      || kind === "DirectCallers"
      || kind === "LoopCalls";
    return counted
      ? typeof inputValue === "number"
        && Number.isSafeInteger(inputValue)
        && inputValue > 0
      : inputValue === null;
  });
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

function isCallKind(value: unknown): boolean {
  return value === "Call"
    || value === "CallVirtual"
    || value === "NewObject"
    || value === "LoadFunction"
    || value === "LoadVirtualFunction"
    || value === "CallIndirect";
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
