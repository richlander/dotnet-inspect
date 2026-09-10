import {
  assertNever,
  beginSourceRequestState,
  cancelSourceRequestState,
  graphSourceStatusIsOpen,
  sourceRequestNeedsLoad,
  sourceSurfaceIsVisible,
  type SourceRequestState,
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

type TypeSourceAdapter = OperationProducerAdapter<
  TypeSourceLoadRequest, BrowserSource, unknown, never, unknown
>;

export type SourceInspectionDependencies = {
  state: SourceInspectionState;
  operationAuthority: OperationAuthorityPage;
  queryMemberSource(request: MemberSourceQuery): Promise<BrowserSource>;
  queryGraphSource(
    request: GraphSourceRequest,
    taste: string,
  ): Promise<BrowserSource | null>;
  memberSourceHasConcreteOverload(): boolean;
  cancelEngineSourceRequest(): void;
  readonly reportOperationDiagnostic: (
    diagnostic: OperationDiagnostic,
  ) => undefined;
  describeError(error: unknown): string;
  render(): void;
  renderPreservingMemberFocus(
    fallback?: MemberFocusSnapshot | null,
  ): MemberFocusSnapshot;
} & ({
  typeSourceAdapter: TypeSourceAdapter;
  queryTypeSource?: never;
  cancelTypeSourceRequest?: never;
} | {
  typeSourceAdapter?: never;
  queryTypeSource(
    operationId: OperationId,
    request: TypeSourceQuery,
  ): Promise<BrowserTypeSourceResult>;
  cancelTypeSourceRequest(
    operationId: OperationId,
    reason: OperationCancelReason,
  ): void;
});

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
    unknown
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
  const beginSourceRequest = (): number => {
    const generation = beginSourceRequestState(state);
    cancelGraphSourceRequest();
    return generation;
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
        state.typeSourceKey = context.request.signature;
        state.typeSource = null;
        state.typeSourceError = "";
        state.typeSourceLoading = true;
        context.preservedFocus =
          dependencies.renderPreservingMemberFocus();
        break;
      }
      case "terminal": {
        const context = typeSourceContext(event.operationId);
        if (event.outcome.kind === "succeeded")
          state.typeSource = event.outcome.value;
        else
          state.typeSourceError =
            dependencies.describeError(event.outcome.error);
        state.typeSourceLoading = false;
        if (context.request.isVisible()) {
          dependencies.renderPreservingMemberFocus(
            context.preservedFocus,
          );
        }
        break;
      }
      case "canceled":
        state.typeSourceLoading = false;
        state.typeSourceKey = "";
        state.typeSourceError = "";
        break;
      case "disposed":
        state.typeSourceLoading = false;
        state.typeSourceKey = "";
        state.typeSourceError = "";
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
    unknown
  > = {
    prepare: (identity, request, sink, cancellation) => {
      typeSourceOperations.set(identity.id, {
        request,
        preservedFocus: null,
      });
      if (dependencies.typeSourceAdapter) {
        const prepared = dependencies.typeSourceAdapter.prepare(identity, request, {
          ...sink,
          reportQuiesced: () => {
            typeSourceOperations.delete(identity.id);
            return sink.reportQuiesced();
          },
        }, cancellation);
        if (prepared.kind === "rejected") {
          typeSourceOperations.delete(identity.id);
          return prepared;
        }
        return {
          kind: "prepared",
          binding: {
            ...prepared.binding,
            abandon: () => {
              typeSourceOperations.delete(identity.id);
              return prepared.binding.abandon();
            },
          },
        };
      }
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
  const beginLegacySourceRequest = (): number => {
    if (cancelTypeSource("superseded") === "rejected")
      throw new Error("Cannot replace source work during feature publication.");
    return beginSourceRequest();
  };
  const cancelCurrentRequest = () => {
    const typeCancellation = cancelTypeSource("user");
    if (typeCancellation === "rejected") return false;
    const legacyCancellation = cancelSourceRequestState(state);
    const graphCancellation = cancelGraphSourceRequest();
    if (graphCancellation && !legacyCancellation) {
      state.sourceRequestGeneration++;
    }
    if ((legacyCancellation || graphCancellation)
      && typeCancellation !== "applied") {
      dependencies.cancelEngineSourceRequest();
    }
    return typeCancellation === "applied"
      || legacyCancellation
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
      if (!sourceRequestNeedsLoad(
          state.memberSourceKey === request.signature,
          state.memberSourceLoading,
          state.memberSource,
          state.memberSourceError)) {
        dependencies.render();
        return;
      }

      const generation = beginLegacySourceRequest();
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
        if (reason !== "producer-rejected" && reason !== "identity-exhausted") return;
        if (typeSourceSession.cancelCurrent("superseded").kind === "rejected") return;
        beginSourceRequest();
        state.typeSourceKey = request.signature;
        state.typeSource = null;
        state.typeSourceLoading = false;
        state.typeSourceError = `Type source operation could not start: ${reason}.`;
        if (request.isVisible()) dependencies.renderPreservingMemberFocus();
        return;
      }
      await result.handle.quiesced;
    },

    async openGraphSource(request, title) {
      const generation = beginLegacySourceRequest();
      const pending = {
        status: "loading",
        request,
        title,
      } as const;
      state.graphSource = pending;
      dependencies.render();
      const isCurrent = () =>
        generation === state.sourceRequestGeneration
        && state.graphSource === pending;
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
