import {
  beginSourceRequestState,
  cancelSourceRequestState,
  sourceRequestNeedsLoad,
  sourceSurfaceIsVisible,
  type SourceRequestState,
  type SourceWorkbenchState,
} from "./data.ts";
import type { BrowserSource } from "./inspect-web-engine.d.ts";
import type { MemberFocusSnapshot } from "./member-focus.ts";

interface SourceCoordinates {
  packageId: string;
  version: string;
  framework: string;
  assembly: string;
  type: string;
}

export interface MemberSourceQuery extends SourceCoordinates {
  member: string;
  selectorKey: string;
  metadataToken: number;
  taste: string;
}

export interface TypeSourceQuery extends SourceCoordinates {
  taste: string;
}

export interface GraphSourceRequest extends SourceCoordinates {
  member: string;
  selectorKey: string;
  metadataToken: number;
}

export interface MemberSourceLoadRequest extends MemberSourceQuery {
  signature: string;
  isCurrent(): boolean;
}

export interface TypeSourceLoadRequest extends TypeSourceQuery {
  signature: string;
  isVisible(): boolean;
}

export interface SourceInspectionState
  extends SourceRequestState, SourceWorkbenchState {
  sourceRequestGeneration: number;
  memberSource: BrowserSource | null;
  memberSourceLoading: boolean;
  memberSourceError: string;
  memberSourceKey: string;
  typeSource: BrowserSource | null;
  typeSourceLoading: boolean;
  typeSourceError: string;
  typeSourceKey: string;
  graphSourceOpen: boolean;
  graphSource: BrowserSource | null;
  graphSourceLoading: boolean;
  graphSourceError: string;
  graphSourceTitle: string;
  graphSourceRequest: {
    request: GraphSourceRequest;
    title: string;
  } | null;
  graphSourceSeq: number;
  taste: string[];
}

export interface SourceInspectionDependencies {
  state: SourceInspectionState;
  queryMemberSource(request: MemberSourceQuery): Promise<BrowserSource>;
  queryTypeSource(request: TypeSourceQuery): Promise<BrowserSource>;
  queryGraphSource(
    request: GraphSourceRequest,
    taste: string,
  ): Promise<BrowserSource>;
  cancelEngineSourceRequest(): void;
  describeError(error: unknown): string;
  render(): void;
  renderPreservingMemberFocus(
    fallback?: MemberFocusSnapshot | null,
  ): MemberFocusSnapshot;
}

export interface SourceInspectionCoordinator {
  cancelHiddenRequest(): void;
  loadMemberSource(request: MemberSourceLoadRequest): Promise<void>;
  loadTypeSource(request: TypeSourceLoadRequest): Promise<void>;
  openGraphSource(
    request: GraphSourceRequest,
    title: string,
  ): Promise<void>;
  closeGraphSource(): void;
}

export function createSourceInspectionCoordinator(
  dependencies: SourceInspectionDependencies,
): SourceInspectionCoordinator {
  const { state } = dependencies;

  return {
    cancelHiddenRequest() {
      if (!sourceSurfaceIsVisible(state)
        && cancelSourceRequestState(state)) {
        dependencies.cancelEngineSourceRequest();
      }
    },

    async loadMemberSource(request) {
      if (!sourceRequestNeedsLoad(
          state.memberSourceKey === request.signature,
          state.memberSourceLoading,
          state.memberSource,
          state.memberSourceError)) {
        dependencies.render();
        return;
      }

      const generation = beginSourceRequestState(state);
      state.memberSourceKey = request.signature;
      state.memberSource = null;
      state.memberSourceLoading = true;
      state.memberSourceError = "";
      const preservedFocus = dependencies.renderPreservingMemberFocus();
      try {
        const result = await dependencies.queryMemberSource(request);
        if (generation === state.sourceRequestGeneration
          && request.isCurrent()
          && state.memberSourceKey === request.signature) {
          state.memberSource = result;
        }
      } catch (error) {
        if (generation === state.sourceRequestGeneration
          && request.isCurrent()
          && state.memberSourceKey === request.signature) {
          state.memberSourceError = dependencies.describeError(error);
        }
      } finally {
        const current = generation === state.sourceRequestGeneration
          && state.memberSourceKey === request.signature;
        if (current) {
          state.memberSourceLoading = false;
          if (request.isCurrent()) {
            dependencies.renderPreservingMemberFocus(preservedFocus);
          }
        }
      }
    },

    async loadTypeSource(request) {
      if (!sourceRequestNeedsLoad(
          state.typeSourceKey === request.signature,
          state.typeSourceLoading,
          state.typeSource,
          state.typeSourceError)) {
        dependencies.renderPreservingMemberFocus();
        return;
      }
      const generation = beginSourceRequestState(state);
      state.typeSourceKey = request.signature;
      state.typeSource = null;
      state.typeSourceError = "";
      state.typeSourceLoading = true;
      const preservedFocus = dependencies.renderPreservingMemberFocus();
      const ownsRequest = () =>
        generation === state.sourceRequestGeneration
        && state.typeSourceKey === request.signature;
      try {
        const result = await dependencies.queryTypeSource(request);
        if (ownsRequest()) state.typeSource = result;
      } catch (error) {
        if (ownsRequest()) {
          state.typeSourceError = dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.typeSourceLoading = false;
          if (request.isVisible()) {
            dependencies.renderPreservingMemberFocus(preservedFocus);
          }
        }
      }
    },

    async openGraphSource(request, title) {
      const generation = beginSourceRequestState(state);
      const sequence = ++state.graphSourceSeq;
      state.graphSourceOpen = true;
      state.graphSourceTitle = title;
      state.graphSourceRequest = { request, title };
      state.graphSource = null;
      state.graphSourceError = "";
      state.graphSourceLoading = true;
      dependencies.render();
      const isCurrent = () =>
        generation === state.sourceRequestGeneration
        && sequence === state.graphSourceSeq
        && state.graphSourceOpen;
      try {
        const source = await dependencies.queryGraphSource(
          request,
          JSON.stringify(state.taste));
        if (isCurrent()) state.graphSource = source;
      } catch (error) {
        if (isCurrent()) {
          state.graphSourceError = dependencies.describeError(error);
        }
      } finally {
        if (isCurrent()) {
          state.graphSourceLoading = false;
          dependencies.render();
        }
      }
    },

    closeGraphSource() {
      if (cancelSourceRequestState(state)) {
        dependencies.cancelEngineSourceRequest();
      }
      state.graphSourceSeq++;
      state.graphSourceOpen = false;
      state.graphSource = null;
      state.graphSourceError = "";
      state.graphSourceLoading = false;
      state.graphSourceRequest = null;
      dependencies.render();
    },
  };
}
