import type {
  BrowserCallGraph as CallGraphFromCallGraphFacade,
  BrowserCallGraphTarget as CallGraphTargetFromCallGraphFacade,
} from "./facades/inspect-web-call-graph.d.ts";
import type {
  BrowserCallGraph as CallGraphFromCatalogFacade,
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
// facade publishes its own invocation destinations. Each facade declares its own
// structurally equal DTO; these aliases are the application's adaptation of all three
// rather than one facade's declaration standing in as the others' owner.
export type InspectedCallGraph =
  | CallGraphFromCallGraphFacade
  | CallGraphFromCatalogFacade;

export type InspectedCallGraphTarget =
  | CallGraphTargetFromCallGraphFacade
  | CallGraphTargetFromCatalogFacade
  | CallGraphTargetFromSourceFacade;

export interface PlatformStackEntry {
  graph: InspectedCallGraph;
  title: string;
}

export interface CallGraphWorkspacePackage {
  package: string;
  version: string;
  framework: string;
}

export interface MemberCallGraphRequest {
  signature: string;
  isRuntimePack: boolean;
  packageId: string;
  version: string;
  framework: string;
  assembly: string;
  platformPack: string;
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
  workspacePackages: CallGraphWorkspacePackage[];
  hasOtherLibraries: boolean;
  isCurrent(): boolean;
}

export interface PlatformDrillRequest {
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
  queryWorkspace(
    request: MemberCallGraphRequest,
    workspace: CallGraphWorkspacePackage[],
  ): Promise<InspectedCallGraph>;
  queryPlatform(request: {
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
  nextPaint(): Promise<unknown>;
  refreshPackageStats(): void;
  patchCallGraphSection(previousMermaid: string | undefined): void;
}

export interface CallGraphInspectionCoordinator {
  load(request: MemberCallGraphRequest): Promise<void>;
  drill(request: PlatformDrillRequest): Promise<void>;
  popDrill(): Promise<void>;
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
    // Runtime members have no NuGet workspace to scan for callers; their
    // implementation is range-fetched through the platform expansion query.
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
      let local: InspectedCallGraph | null = null;
      const canceledExpansionStillMatchesView = () =>
        local != null
        && request.isCurrent()
        && state.memberCallGraphKey === request.signature
        && state.memberCallGraph === local
        && !state.memberCallGraphExpanding;
      try {
        local = await dependencies.queryWorkspace(request, []);
        if (!ownsRequest()) return;
        state.memberCallGraph = local;
        state.memberCallGraphLoading = false;
        state.memberCallGraphExpanding = request.hasOtherLibraries;
        dependencies.renderPreservingMemberFocus(preservedFocus);
        await dependencies.renderCallGraph();

        if (request.hasOtherLibraries) {
          // Let the local graph paint before the synchronous engine begins the
          // broader workspace scan.
          await dependencies.nextPaint();
          if (!ownsRequest()) return;
          const full = await dependencies.queryWorkspace(
            request,
            request.workspacePackages);
          const previousMermaid = state.memberCallGraph?.mermaid;
          if (!ownsRequest()) {
            if (!canceledExpansionStillMatchesView()) return;
            state.memberCallGraph = full;
            dependencies.refreshPackageStats();
            if (!state.platformDrillLoading && state.platformStack.length === 0)
              dependencies.patchCallGraphSection(previousMermaid);
            return;
          }
          state.memberCallGraph = full;
          state.memberCallGraphExpanding = false;
          dependencies.refreshPackageStats();
          dependencies.patchCallGraphSection(previousMermaid);
        }
      } catch (error) {
        if (!ownsRequest()) {
          if (!canceledExpansionStillMatchesView()) return;
          state.memberCallGraphError = mergeInspectionErrors(
            state.memberCallGraphError,
            `Workspace expansion was incomplete: ${dependencies.describeError(error)}`);
          if (state.platformDrillLoading || state.platformStack.length > 0)
            return;
          dependencies.renderPreservingMemberFocus(preservedFocus);
          await dependencies.renderCallGraph();
          return;
        }
        state.memberCallGraphLoading = false;
        state.memberCallGraphExpanding = false;
        if (state.memberCallGraph) {
          state.memberCallGraphError =
            `Workspace expansion was incomplete: ${dependencies.describeError(error)}`;
          dependencies.renderPreservingMemberFocus(preservedFocus);
          await dependencies.renderCallGraph();
        } else {
          state.memberCallGraphError = dependencies.describeError(error);
          dependencies.renderPreservingMemberFocus(preservedFocus);
        }
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
