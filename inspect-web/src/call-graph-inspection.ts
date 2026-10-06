import type {
  BrowserCallGraph as CallGraphFromCallGraphFacade,
  BrowserCallGraphBoundary as BoundaryFromCallGraphFacade,
  BrowserCallGraphTarget as CallGraphTargetFromCallGraphFacade,
  expandPlatformCallGraph,
} from "./facades/inspect-web-call-graph.d.ts";
import type {
  BrowserCallGraph as CallGraphFromCatalogFacade,
  BrowserCallGraphBoundary as BoundaryFromCatalogFacade,
  BrowserCallGraphTarget as CallGraphTargetFromCatalogFacade,
} from "./facades/inspect-web-catalog.d.ts";
import type {
  BrowserCallGraphTarget as CallGraphTargetFromSourceFacade,
} from "./facades/inspect-web-source.d.ts";
import type { MemberFocusSnapshot } from "./member-focus.ts";
import { mergeInspectionErrors } from "./data.ts";

// Call graphs reach the application from two owners: the call-graph facade expands package
// and platform topology, and the catalog facade returns the graph a product home demo
// activates. Annotated source adds a third owner for graph targets, because the source
// facade publishes its own invocation destinations. CallGraph and Catalog carry the
// package subject available to whole dependency-aware graphs; Source destinations do
// not manufacture that unavailable fact. These aliases adapt each owner rather than
// making one facade's declaration stand in for the others.
export type InspectedCallGraph =
  | CallGraphFromCallGraphFacade
  | CallGraphFromCatalogFacade;

export type InspectedCallGraphTarget =
  | CallGraphTargetFromCallGraphFacade
  | CallGraphTargetFromCatalogFacade
  | CallGraphTargetFromSourceFacade;

export type InspectedCallGraphBoundary =
  | BoundaryFromCallGraphFacade
  | BoundaryFromCatalogFacade;

export interface PlatformStackEntry {
  graph: InspectedCallGraph;
  title: string;
}

export interface MemberCallGraphRequest {
  signature: string;
  isRuntimePack: boolean;
  packageId: string;
  version: string;
  framework: string;
  assembly: string;
  platformPack: string;
  platformContextId: string | null;
  platformAssemblyVersion: string | null;
  platformAssemblyCulture: string | null;
  platformAssemblyPublicKeyToken: string | null;
  typeIdentity: string;
  type: string;
  platformType: string;
  member: string;
  memberSignature: string;
  selectorKey: string;
  metadataToken: number;
  traversalFramework: string;
  isCurrent(): boolean;
}

export interface PlatformDrillRequest {
  contextId: string | null;
  framework: string;
  platformVersion: string;
  assembly: string;
  pack: string;
  assemblyVersion: string | null;
  assemblyCulture: string | null;
  assemblyPublicKeyToken: string | null;
  type: string;
  member: string;
  selectorKey: string;
  metadataToken: number;
  title: string;
  errorTarget: string;
  isCurrent(): boolean;
}

export interface CallGraphInspectionState {
  memberCallGraph: InspectedCallGraph | null;
  memberCallGraphLoading: boolean;
  memberCallGraphError: string;
  graphMemberNavigationError: string;
  memberCallGraphKey: string;
  memberCallGraphExpanding: boolean;
  memberCallGraphSeq: number;
  platformStack: PlatformStackEntry[];
  platformDrillLoading: boolean;
  platformDrillError: string;
}

export function callGraphErrorForView(state: CallGraphInspectionState) {
  return state.platformStack.length > 0
    ? mergeInspectionErrors(state.graphMemberNavigationError, "")
    : mergeInspectionErrors(
        state.graphMemberNavigationError,
        state.memberCallGraphError);
}

export interface CallGraphInspectionDependencies {
  state: CallGraphInspectionState;
  queryPackage(request: MemberCallGraphRequest): Promise<InspectedCallGraph>;
  queryPlatform(request: {
    contextId: string | null;
    framework: string;
    platformVersion: string;
    assembly: string;
    pack: string;
    assemblyVersion: string | null;
    assemblyCulture: string | null;
    assemblyPublicKeyToken: string | null;
    type: string;
    member: string;
    selectorKey: string;
    metadataToken: number;
  }): Promise<InspectedCallGraph>;
  describeError(error: unknown): string;
  render(): void;
  renderPreservingMemberFocus(
    fallback?: MemberFocusSnapshot | null,
  ): MemberFocusSnapshot;
  renderCallGraph(): Promise<void>;
}

export interface CallGraphInspectionCoordinator {
  load(request: MemberCallGraphRequest): Promise<void>;
  drill(request: PlatformDrillRequest): Promise<void>;
  popDrill(): Promise<void>;
}

export function queryPlatformCallGraph(
  query: typeof expandPlatformCallGraph,
  request: Parameters<CallGraphInspectionDependencies["queryPlatform"]>[0],
): Promise<InspectedCallGraph> {
  return query(
    request.framework,
    request.platformVersion,
    request.assembly,
    request.pack,
    request.assemblyVersion ?? "",
    request.assemblyCulture,
    request.assemblyPublicKeyToken,
    request.type,
    request.member,
    request.selectorKey,
    request.metadataToken,
    request.contextId);
}

export function createCallGraphInspectionCoordinator(
  dependencies: CallGraphInspectionDependencies,
): CallGraphInspectionCoordinator {
  const { state } = dependencies;
  const resetPlatformDrill = () => {
    state.platformStack = [];
    state.platformDrillLoading = false;
    state.platformDrillError = "";
  };

  const loadPlatformGraph = async (request: MemberCallGraphRequest) => {
    // Runtime members use the selected demo context or ordinary Platform scope.
    const sequence = ++state.memberCallGraphSeq;
    resetPlatformDrill();
    state.memberCallGraphLoading = true;
    state.memberCallGraphExpanding = false;
    state.memberCallGraphError = "";
    const preservedFocus = dependencies.renderPreservingMemberFocus();
    const ownsRequest = () =>
      sequence === state.memberCallGraphSeq
      && request.isCurrent()
      && state.memberCallGraphKey === request.signature;
    try {
      const graph = await dependencies.queryPlatform({
        contextId: request.platformContextId,
        framework: request.framework,
        platformVersion: request.version,
        assembly: request.assembly,
        pack: request.platformPack,
        assemblyVersion: request.platformAssemblyVersion,
        assemblyCulture: request.platformAssemblyCulture,
        assemblyPublicKeyToken: request.platformAssemblyPublicKeyToken,
        type: request.platformType,
        member: request.member,
        selectorKey: request.selectorKey,
        metadataToken: request.metadataToken,
      });
      if (!ownsRequest()) return;
      state.memberCallGraph = graph;
      state.memberCallGraphLoading = false;
      state.memberCallGraphExpanding = false;
      dependencies.renderPreservingMemberFocus(preservedFocus);
      await dependencies.renderCallGraph();
    } catch (error) {
      if (!ownsRequest()) return;
      state.memberCallGraphLoading = false;
      state.memberCallGraphExpanding = false;
      state.memberCallGraphError = dependencies.describeError(error);
      dependencies.renderPreservingMemberFocus(preservedFocus);
    }
  };

  return {
    async load(request) {
      if (state.memberCallGraphKey === request.signature
        && (state.memberCallGraph || state.memberCallGraphError)) {
        dependencies.render();
        await dependencies.renderCallGraph();
        return;
      }
      state.memberCallGraphKey = request.signature;
      state.memberCallGraph = null;
      state.memberCallGraphError = "";

      if (request.isRuntimePack) {
        await loadPlatformGraph(request);
        return;
      }

      const sequence = ++state.memberCallGraphSeq;
      resetPlatformDrill();
      state.memberCallGraphLoading = true;
      state.memberCallGraphExpanding = false;
      state.memberCallGraphError = "";
      const preservedFocus = dependencies.renderPreservingMemberFocus();
      const ownsRequest = () =>
        sequence === state.memberCallGraphSeq
        && request.isCurrent()
        && state.memberCallGraphKey === request.signature;
      try {
        const graph = await dependencies.queryPackage(request);
        if (!ownsRequest()) return;
        state.memberCallGraph = graph;
        state.memberCallGraphLoading = false;
        state.memberCallGraphExpanding = false;
        dependencies.renderPreservingMemberFocus(preservedFocus);
        await dependencies.renderCallGraph();
      } catch (error) {
        if (!ownsRequest()) return;
        state.memberCallGraphLoading = false;
        state.memberCallGraphExpanding = false;
        state.memberCallGraphError = dependencies.describeError(error);
        dependencies.renderPreservingMemberFocus(preservedFocus);
      }
    },

    async drill(request) {
      if (state.platformDrillLoading) return;
      const sequence = state.memberCallGraphSeq;
      state.platformDrillLoading = true;
      state.platformDrillError = "";
      const preservedFocus = dependencies.renderPreservingMemberFocus();
      const ownsRequest = () =>
        sequence === state.memberCallGraphSeq && request.isCurrent();
      const abandonStaleRequest = () => {
        if (sequence !== state.memberCallGraphSeq) return;
        state.platformDrillLoading = false;
        dependencies.renderPreservingMemberFocus();
      };
      try {
        const graph = await dependencies.queryPlatform(request);
        if (!ownsRequest()) {
          abandonStaleRequest();
          return;
        }
        state.platformStack.push({ graph, title: request.title });
        state.platformDrillLoading = false;
        dependencies.renderPreservingMemberFocus(preservedFocus);
        await dependencies.renderCallGraph();
      } catch (error) {
        if (!ownsRequest()) {
          abandonStaleRequest();
          return;
        }
        state.platformDrillLoading = false;
        state.platformDrillError =
          `Could not descend into ${request.errorTarget}: ${dependencies.describeError(error)}`;
        dependencies.renderPreservingMemberFocus(preservedFocus);
        await dependencies.renderCallGraph();
      }
    },

    async popDrill() {
      if (state.platformStack.length === 0) return;
      state.memberCallGraphSeq++;
      state.memberCallGraphExpanding = false;
      state.platformDrillLoading = false;
      state.platformStack.pop();
      state.platformDrillError = "";
      dependencies.render();
      await dependencies.renderCallGraph();
    },
  };
}
