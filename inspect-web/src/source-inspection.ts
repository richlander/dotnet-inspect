import {
  assertNever,
  graphSourceStatusIsOpen,
  sourceSurfaceIsVisible,
  type SourceWorkbenchState,
} from "./data.ts";
import type {
  BrowserSource,
  BrowserTypeSourceResult,
} from "./facades/inspect-web-source.d.ts";
import type { MemberFocusSnapshot } from "./member-focus.ts";
import type {
  OperationAuthorityPage,
  OperationCancelReason,
  OperationDiagnostic,
  OperationFeatureEvent,
  OperationId,
  OperationProducerAdapter,
  OperationSession,
} from "./operation-authority.ts";

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

interface GraphSourceTarget {
  readonly request: GraphSourceRequest;
  readonly title: string;
}

export type GraphSourceState =
  | { readonly status: "closed" }
  | ({ readonly status: "loading" } & GraphSourceTarget)
  | ({
      readonly status: "ready";
      readonly source: BrowserSource;
    } & GraphSourceTarget)
  | ({
      readonly status: "failed";
      readonly error: string;
    } & GraphSourceTarget)
  | ({ readonly status: "cancelled" } & GraphSourceTarget);

export type OpenGraphSourceState =
  Exclude<GraphSourceState, { readonly status: "closed" }>;

export function graphSourceIsOpen(
  state: GraphSourceState,
): state is OpenGraphSourceState {
  return graphSourceStatusIsOpen(state);
}

export function graphSourceRequest(
  state: GraphSourceState,
): GraphSourceTarget | null {
  switch (state.status) {
    case "closed":
      return null;
    case "loading":
    case "ready":
    case "failed":
    case "cancelled":
      return { request: state.request, title: state.title };
    default:
      return assertNever(state, "graph source state");
  }
}

export function graphSourceAutoLoadRequest(
  state: GraphSourceState,
): GraphSourceTarget | null {
  switch (state.status) {
    case "cancelled":
      return { request: state.request, title: state.title };
    case "closed":
    case "loading":
    case "ready":
    case "failed":
      return null;
    default:
      return assertNever(state, "graph source state");
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

export type SourceResultState<TSource = BrowserSource> =
  | { readonly status: "idle" }
  | {
      readonly status: "loading";
      readonly signature: string;
    }
  | {
      readonly status: "ready";
      readonly signature: string;
      readonly source: TSource;
    }
  | {
      readonly status: "failed";
      readonly signature: string;
      readonly error: string;
    };

export function sourceResultNeedsLoad(
  state: SourceResultState,
  signature: string,
): boolean {
  return state.status === "idle" || state.signature !== signature;
}

export function sourceResultForSignature<TSource>(
  state: SourceResultState<TSource>,
  signature: string,
): TSource | null {
  return state.status === "ready" && state.signature === signature
    ? state.source
    : null;
}

export function normalizeSourceResultSnapshot<TSource>(
  state: SourceResultState<TSource>,
): SourceResultState<TSource> {
  return state.status === "loading" ? { status: "idle" } : state;
}

export interface SourceInspectionState
  extends SourceWorkbenchState {
  memberSource: SourceResultState;
  typeSource: SourceResultState;
  graphSource: GraphSourceState;
  taste: string[];
}

export interface SourceInspectionDependencies {
  state: SourceInspectionState;
  operationAuthority: OperationAuthorityPage;
  queryMemberSource(request: MemberSourceQuery): Promise<BrowserSource>;
  queryTypeSource(
    operationId: OperationId,
    request: TypeSourceQuery,
  ): Promise<BrowserTypeSourceResult>;
  queryGraphSource(
    request: GraphSourceRequest,
    taste: string,
  ): Promise<BrowserSource | null>;
  memberSourceHasConcreteOverload(): boolean;
  cancelEngineSourceRequest(): void;
  cancelTypeSourceRequest(
    operationId: OperationId,
    reason: OperationCancelReason,
  ): void;
  readonly reportOperationDiagnostic: (
    diagnostic: OperationDiagnostic,
  ) => undefined;
  describeError(error: unknown): string;
  render(): void;
  renderPreservingMemberFocus(
    fallback?: MemberFocusSnapshot | null,
  ): MemberFocusSnapshot;
}

export interface SourceInspectionCoordinator {
  cancelCurrentRequest(): boolean;
  cancelHiddenRequest(): void;
  clearGraphSource(): void;
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
  interface TypeSourceOperationContext {
    readonly request: TypeSourceLoadRequest;
    preservedFocus: MemberFocusSnapshot | null;
  }
  type TypeSourceFeatureEvent =
    OperationFeatureEvent<BrowserSource, unknown, never>;
  type TypeSourceSession = OperationSession<
    TypeSourceLoadRequest,
    BrowserSource,
    unknown,
    never,
    never
  >;

  const cancelGraphSourceRequest = (): boolean => {
    const current = state.graphSource;
    if (current.status !== "loading") return false;
    state.graphSource = {
      status: "cancelled",
      request: current.request,
      title: current.title,
    };
    return true;
  };
  const cancelMemberSourceRequest = (): boolean => {
    if (state.memberSource.status !== "loading") return false;
    state.memberSource = { status: "idle" };
    return true;
  };
  const cancelTypeSourceState = (): boolean => {
    if (state.typeSource.status !== "loading") return false;
    state.typeSource = { status: "idle" };
    return true;
  };
  const beginSourceRequest = (): void => {
    cancelMemberSourceRequest();
    cancelTypeSourceState();
    cancelGraphSourceRequest();
  };

  const typeSourceOperations =
    new Map<OperationId, TypeSourceOperationContext>();
  const typeSourceContext = (
    operationId: OperationId,
  ): TypeSourceOperationContext => {
    const context = typeSourceOperations.get(operationId);
    if (context === undefined)
      throw new Error("Type source operation context is unavailable.");
    return context;
  };
  const publishTypeSourceEvent = (event: TypeSourceFeatureEvent): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced": {
        const context = typeSourceContext(event.operation.id);
        beginSourceRequest();
        state.typeSource = {
          status: "loading",
          signature: context.request.signature,
        };
        context.preservedFocus =
          dependencies.renderPreservingMemberFocus();
        break;
      }
      case "terminal": {
        const context = typeSourceContext(event.operationId);
        state.typeSource = event.outcome.kind === "succeeded"
          ? {
              status: "ready",
              signature: context.request.signature,
              source: event.outcome.value,
            }
          : {
              status: "failed",
              signature: context.request.signature,
              error: dependencies.describeError(event.outcome.error),
            };
        if (context.request.isVisible()) {
          dependencies.renderPreservingMemberFocus(
            context.preservedFocus,
          );
        }
        break;
      }
      case "canceled":
        state.typeSource = { status: "idle" };
        break;
      case "disposed":
        state.typeSource = { status: "idle" };
        break;
      case "progress":
        break;
    }
    return undefined;
  };
  const typeSourceSession: TypeSourceSession =
    dependencies.operationAuthority.createSession({
      feature: { publish: publishTypeSourceEvent },
      diagnostic: {
        report: diagnostic =>
          dependencies.reportOperationDiagnostic(diagnostic),
      },
    });
  const typeSourceAdapter: OperationProducerAdapter<
    TypeSourceLoadRequest,
    BrowserSource,
    unknown,
    never,
    never
  > = {
    prepare: (identity, request, sink) => {
      typeSourceOperations.set(identity.id, {
        request,
        preservedFocus: null,
      });
      let engineCancellationRequested = false;
      const cancelEngine = (reason: OperationCancelReason): undefined => {
        if (engineCancellationRequested) {
          return undefined;
        }
        engineCancellationRequested = true;
        dependencies.cancelTypeSourceRequest(identity.id, reason);
        return undefined;
      };
      const quiesce = (): undefined => {
        typeSourceOperations.delete(identity.id);
        sink.reportQuiesced();
        return undefined;
      };
      const boundaryFailure = (error: unknown): undefined => {
        sink.reportUnexpectedTerminal(error, error);
        return quiesce();
      };
      const finish = (result: BrowserTypeSourceResult): undefined => {
        try {
          if (result.version !== 1)
            throw new Error("Unsupported type-source result version.");
          switch (result.kind) {
            case "Succeeded":
              if (result.value === null || typeof result.value !== "object")
                throw new Error("Type-source success has no source.");
              sink.reportTerminal({ kind: "succeeded", value: result.value });
              break;
            case "Failed": {
              if (typeof result.error !== "string" || typeof result.diagnostic !== "string")
                throw new Error("Type-source failure has no error or diagnostic.");
              const error = new Error(result.error);
              if (result.failureKind === "Expected")
                sink.reportTerminal({ kind: "failed", error });
              else if (result.failureKind === "Unexpected")
                sink.reportUnexpectedTerminal(error, result.diagnostic);
              else
                throw new Error("Unknown type-source failure kind.");
              break;
            }
            case "Canceled":
              switch (result.reason) {
                case "user":
                case "superseded":
                case "disposed":
                case "feature-observer-failed":
                case "timeout":
                case "worker-restarted":
                  sink.reportTerminal({ kind: "canceled", reason: result.reason });
                  break;
                default:
                  throw new Error("Unknown type-source cancellation reason.");
              }
              break;
            default:
              throw new Error("Unknown type-source result kind.");
          }
        } catch (error: unknown) {
          sink.reportUnexpectedTerminal(error, error);
        }
        return quiesce();
      };
      return {
        kind: "prepared",
        binding: {
          requestCancellation: cancelEngine,
          activate: () => {
            let query: Promise<BrowserTypeSourceResult>;
            try {
              query = dependencies.queryTypeSource(identity.id, request);
            } catch (error: unknown) {
              return boundaryFailure(error);
            }
            void query.then(finish, boundaryFailure);
            return undefined;
          },
          abandon: () => {
            typeSourceOperations.delete(identity.id);
            return undefined;
          },
        },
      };
    },
  };

  const rejectFeatureReentrancy = (operation: string): void => {
    dependencies.reportOperationDiagnostic({
      kind: "producer-contract",
      operationId: null,
      error: new Error(
        `${operation} was attempted during source feature publication.`,
      ),
    });
  };
  const cancelTypeSource = (
    reason: "user" | "superseded",
  ): "applied" | "no-op" | "rejected" => {
    const result = typeSourceSession.cancelCurrent(reason);
    if (result.kind === "rejected") {
      rejectFeatureReentrancy("Source cancellation");
      return "rejected";
    }
    return result.kind;
  };
  const beginLegacySourceRequest = (): void => {
    if (cancelTypeSource("superseded") === "rejected")
      throw new Error("Cannot replace source work during feature publication.");
    beginSourceRequest();
  };
  const cancelCurrentRequest = () => {
    const typeCancellation = cancelTypeSource("user");
    if (typeCancellation === "rejected") return false;
    cancelTypeSourceState();
    const memberCancellation = cancelMemberSourceRequest();
    const graphCancellation = cancelGraphSourceRequest();
    if ((memberCancellation || graphCancellation)
      && typeCancellation !== "applied") {
      dependencies.cancelEngineSourceRequest();
    }
    return typeCancellation === "applied"
      || memberCancellation
      || graphCancellation;
  };
  const clearGraphSource = () => {
    cancelCurrentRequest();
    state.graphSource = { status: "closed" };
  };

  return {
    cancelCurrentRequest,
    cancelHiddenRequest() {
      if (!sourceSurfaceIsVisible(
          state,
          dependencies.memberSourceHasConcreteOverload())) {
        cancelCurrentRequest();
      }
    },
    clearGraphSource,

    async loadMemberSource(request) {
      if (!sourceResultNeedsLoad(state.memberSource, request.signature)) {
        dependencies.render();
        return;
      }

      beginLegacySourceRequest();
      const pending = {
        status: "loading",
        signature: request.signature,
      } as const;
      state.memberSource = pending;
      const preservedFocus = dependencies.renderPreservingMemberFocus();
      try {
        const result = await dependencies.queryMemberSource(request);
        if (state.memberSource !== pending) return;
        if (!request.isCurrent()) {
          state.memberSource = { status: "idle" };
          return;
        }
        state.memberSource = {
          status: "ready",
          signature: request.signature,
          source: result,
        };
        dependencies.renderPreservingMemberFocus(preservedFocus);
      } catch (error) {
        if (state.memberSource !== pending) return;
        if (!request.isCurrent()) {
          state.memberSource = { status: "idle" };
          return;
        }
        state.memberSource = {
          status: "failed",
          signature: request.signature,
          error: dependencies.describeError(error),
        };
        dependencies.renderPreservingMemberFocus(preservedFocus);
      }
    },

    async loadTypeSource(request) {
      if (!sourceResultNeedsLoad(state.typeSource, request.signature)) {
        dependencies.renderPreservingMemberFocus();
        return;
      }
      const result = typeSourceSession.start(request, typeSourceAdapter);
      if (result.kind === "rejected") {
        const reason = result.reason.kind;
        dependencies.reportOperationDiagnostic({
          kind: "producer-contract",
          operationId: null,
          error: new Error(
            `Type source operation start was rejected: ${reason}.`,
          ),
        });
        return;
      }
      await result.handle.quiesced;
    },

    async openGraphSource(request, title) {
      beginLegacySourceRequest();
      const pending = {
        status: "loading",
        request,
        title,
      } as const;
      state.graphSource = pending;
      dependencies.render();
      const isCurrent = () =>
        state.graphSource === pending;
      let published = false;
      try {
        const source = await dependencies.queryGraphSource(
          request,
          JSON.stringify(state.taste));
        if (isCurrent()) {
          state.graphSource = source
            ? {
                status: "ready",
                request,
                title,
                source,
              }
            : {
                status: "failed",
                request,
                title,
                error: "",
              };
          published = true;
        }
      } catch (error) {
        if (isCurrent()) {
          state.graphSource = {
            status: "failed",
            request,
            title,
            error: dependencies.describeError(error),
          };
          published = true;
        }
      } finally {
        if (published) dependencies.render();
      }
    },

    closeGraphSource() {
      clearGraphSource();
      dependencies.render();
    },
  };
}
