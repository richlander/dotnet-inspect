import {
  beginSourceRequestState,
  cancelSourceRequestState,
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
  operationAuthority: OperationAuthorityPage;
  queryMemberSource(request: MemberSourceQuery): Promise<BrowserSource>;
  queryTypeSource(
    operationId: OperationId,
    request: TypeSourceQuery,
  ): Promise<BrowserTypeSourceResult>;
  queryGraphSource(
    request: GraphSourceRequest,
    taste: string,
  ): Promise<BrowserSource>;
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
        beginSourceRequestState(state);
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
  const beginLegacySourceRequest = (): number => {
    if (cancelTypeSource("superseded") === "rejected")
      throw new Error("Cannot replace source work during feature publication.");
    return beginSourceRequestState(state);
  };
  const cancelCurrentRequest = () => {
    const typeCancellation = cancelTypeSource("user");
    if (typeCancellation === "rejected") return false;
    const legacyCancellation = cancelSourceRequestState(state);
    if (legacyCancellation && typeCancellation !== "applied")
      dependencies.cancelEngineSourceRequest();
    return typeCancellation === "applied" || legacyCancellation;
  };
  const clearGraphSource = () => {
    cancelCurrentRequest();
    state.graphSourceSeq++;
    state.graphSourceOpen = false;
    state.graphSource = null;
    state.graphSourceError = "";
    state.graphSourceLoading = false;
    state.graphSourceRequest = null;
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
        return;
      }
      await result.handle.quiesced;
    },

    async openGraphSource(request, title) {
      const generation = beginLegacySourceRequest();
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
      clearGraphSource();
      dependencies.render();
    },
  };
}
