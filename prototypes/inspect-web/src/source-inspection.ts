import {
  assertNever,
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

// One state for the graph-source modal, replacing the previous open/loading/error/result/
// title/request fields and their sequence counter. Every non-closed variant carries the
// request and title it belongs to, so a rendered modal can never show one request's title
// beside another's result.
//
// The `loading` object itself is the request-ownership token: `openGraphSource` keeps a
// reference to the object it installed and only commits a result when `state.graphSource`
// is still that exact object. It is a typed replacement for the prior monotonic sequence:
// both distinguish repeated requests for the same member and reject a stale completion,
// while object identity keeps ownership and visible lifecycle state in one value.
//
// `cancelled` exists because a competing member- or type-source request retires an in-flight
// graph load while its modal is still open. It renders the same "No source was returned."
// text the previous field layout produced for that state, but names it rather than leaving
// it as the absence of all three of loading, result, and error.
export type GraphSourceState =
  | { readonly status: "closed" }
  | {
    readonly status: "loading";
    readonly request: GraphSourceRequest;
    readonly title: string;
  }
  | {
    readonly status: "ready";
    readonly request: GraphSourceRequest;
    readonly title: string;
    readonly source: BrowserSource;
  }
  | {
    readonly status: "failed";
    readonly request: GraphSourceRequest;
    readonly title: string;
    readonly error: string;
  }
  | {
    readonly status: "cancelled";
    readonly request: GraphSourceRequest;
    readonly title: string;
  };

export const closedGraphSource: GraphSourceState = { status: "closed" };

// Whether the auto-load pass -- which runs at the end of *every* render -- should reissue
// this request. `cancelled` is the one state that has no result coming: open, unsettled,
// nothing in flight, nothing to show. Every other variant is either still loading or
// already settled, and reissuing a settled one is the retry loop this union was
// introduced to end.
//
// This is a function rather than a condition at the call site because round 2 review
// (GPT-5.6 Sol) defeated the source-text gate that guarded that condition: writing the
// added comparison Yoda-style (`"failed" === state.graphSource.status`) restored the
// user-visible retry loop with the whole suite green. A decision that can be called can be
// tested for every variant of the union, and `assertNever` makes a new variant declare its
// answer instead of inheriting one.
//
// It returns the work rather than a boolean so the caller has nothing left to decide: with
// a boolean the caller still needs its own comparison to reach `request` and `title`, and
// that comparison is free to disagree with this answer. Handing back the arguments removes
// the second opinion.
export function graphSourceAutoLoad(
  graphSource: GraphSourceState,
): { readonly request: GraphSourceRequest; readonly title: string } | null {
  switch (graphSource.status) {
    case "cancelled":
      return { request: graphSource.request, title: graphSource.title };
    case "closed":
    case "loading":
    case "ready":
    case "failed":
      return null;
    default:
      return assertNever(graphSource, "GraphSourceState");
  }
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
  graphSource: GraphSourceState;
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

  // Retire an in-flight graph load without disturbing a closed, settled, or already-cancelled
  // modal. Returning whether it cancelled anything is what lets the shared request-state
  // helpers report that a cancellation actually happened.
  function cancelGraphSource(): boolean {
    const current = state.graphSource;
    if (current.status !== "loading") return false;
    state.graphSource = {
      status: "cancelled",
      request: current.request,
      title: current.title,
    };
    return true;
  }

  return {
    cancelHiddenRequest() {
      if (!sourceSurfaceIsVisible(state)
        && cancelSourceRequestState(state, cancelGraphSource)) {
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

      const generation = beginSourceRequestState(state, cancelGraphSource);
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
      const generation = beginSourceRequestState(state, cancelGraphSource);
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
      beginSourceRequestState(state, cancelGraphSource);
      // This object is the ownership token. Nothing below commits to `state.graphSource`
      // unless it is still exactly this object, so a close, a reopen, or a competing
      // source request all reject this request's late result without a counter.
      const pending: GraphSourceState = { status: "loading", request, title };
      state.graphSource = pending;
      dependencies.render();
      try {
        const source = await dependencies.queryGraphSource(
          request,
          JSON.stringify(state.taste));
        if (state.graphSource !== pending) return;
        // The engine's `.d.ts` is hand-written and promises a source, but the previous
        // renderer guarded the payload with a truthiness check and drew "No source was
        // returned." when it was absent. A `failed` variant with no message renders that
        // same string, so an empty payload draws what it always drew rather than reaching
        // `source.provider` on nothing.
        //
        // What it renders is unchanged; what it *does* afterwards is not, and round 2
        // review (GPT-5.6 Sol) measured the difference. The predecessor left
        // `loading=false`, `source=null`, and `error=""`, which its auto-load predicate
        // read as work never attempted -- so it reissued the request on the next render,
        // and on every render after that. Recording `failed` settles it, and
        // `graphSourceAutoLoad` does not reload a settled state. A falsy payload now
        // stops instead of retrying forever. That is the same retry loop the empty-error
        // case fixed, reached by a second route.
        state.graphSource = source
          ? { status: "ready", request, title, source }
          : { status: "failed", request, title, error: "" };
      } catch (error) {
        if (state.graphSource !== pending) return;
        state.graphSource = {
          status: "failed",
          request,
          title,
          error: dependencies.describeError(error),
        };
      }
      // Reached only when this request still owned the modal and settled it, matching the
      // previous behavior of rendering the resolution exactly once for the owning request.
      dependencies.render();
    },

    closeGraphSource() {
      if (cancelSourceRequestState(state, cancelGraphSource)) {
        dependencies.cancelEngineSourceRequest();
      }
      // One assignment both resets the modal and invalidates any request that owned the
      // previous object.
      state.graphSource = closedGraphSource;
      dependencies.render();
    },
  };
}
