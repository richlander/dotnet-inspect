import type {
  BrowserSourceComparison,
  BrowserSourceComparisonRequest,
  BrowserSourceComparisonResult,
} from "./facades/inspect-web-source.d.ts";
import type {
  OperationAuthorityPage,
  OperationCancelReason,
  OperationDiagnostic,
  OperationId,
  OperationProducerAdapter,
  OperationSession,
} from "./operation-authority.ts";
import { reportComparisonEnvelope } from "./comparison-envelope.ts";

export interface SourceComparisonContext {
  readonly packageId: string;
  readonly version: string;
  readonly framework: string;
  readonly assembly: string;
  readonly typeIdentity: string;
  readonly memberName: string;
  readonly selectorKey: string;
  readonly metadataToken: number;
  readonly label: string;
}

export interface SourceDiffState {
  open: boolean;
  context: SourceComparisonContext | null;
  returnFocusSelector: string;
  unavailableReason: string;
  afterVersion: string;
  submittedRequest: BrowserSourceComparisonRequest | null;
  comparison: BrowserSourceComparison | null;
  loading: boolean;
  error: string;
}

export function createSourceDiffState(): SourceDiffState {
  return {
    open: false, context: null, returnFocusSelector: "", unavailableReason: "",
    afterVersion: "", submittedRequest: null, comparison: null,
    loading: false, error: "",
  };
}

export function isExactSourceComparisonVersion(value: string): boolean {
  return /^\d+(?:\.\d+){0,3}(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/.test(value.trim());
}

export interface SourceComparisonDependencies {
  state: SourceDiffState;
  operationAuthority: OperationAuthorityPage;
  queryComparison(
    operationId: OperationId,
    requestJson: string,
  ): Promise<BrowserSourceComparisonResult>;
  cancelComparison(operationId: OperationId, reason: OperationCancelReason): void;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  describeError(error: unknown): string;
  render(): void;
}

export function createSourceComparisonCoordinator(
  dependencies: SourceComparisonDependencies,
) {
  const { state } = dependencies;
  type Session = OperationSession<
    BrowserSourceComparisonRequest, BrowserSourceComparison, unknown, never, never
  >;
  let session: Session | null = null;
  const scheduleRender = (): void => {
    queueMicrotask(() => dependencies.render());
  };
  const diagnose = (message: string): void => {
    dependencies.reportOperationDiagnostic({
      kind: "producer-contract", operationId: null, error: new Error(message),
    });
  };
  const adapter: OperationProducerAdapter<
    BrowserSourceComparisonRequest, BrowserSourceComparison, unknown, never, never
  > = {
    prepare(identity, request, sink) {
      let cancellationRequested = false;
      const quiesce = (): undefined => {
        sink.reportQuiesced();
        return undefined;
      };
      const boundaryFailure = (error: unknown): undefined => {
        sink.reportUnexpectedTerminal(error, error);
        return quiesce();
      };
      return {
        kind: "prepared",
        binding: {
          requestCancellation(reason) {
            if (!cancellationRequested) {
              cancellationRequested = true;
              dependencies.cancelComparison(identity.id, reason);
            }
            return undefined;
          },
          activate() {
            let query: Promise<BrowserSourceComparisonResult>;
            try {
              query = dependencies.queryComparison(identity.id, JSON.stringify(request));
            } catch (error: unknown) {
              return boundaryFailure(error);
            }
            void query.then(result => {
              reportComparisonEnvelope(
                sink, "authored Source comparison", result.version, result.kind,
                result.value, result.failureKind, result.error,
                result.diagnostic, result.reason);
              return quiesce();
            }, boundaryFailure);
            return undefined;
          },
          abandon: () => undefined,
        },
      };
    },
  };

  const shutdown = (): boolean => {
    const wasOpen = state.open;
    if (session?.dispose().kind === "rejected") {
      diagnose("Source Diff disposal was rejected during feature publication.");
      return false;
    }
    session = null;
    Object.assign(state, createSourceDiffState());
    return wasOpen;
  };

  return {
    isOpen: () => state.open,
    open(context: SourceComparisonContext, returnFocusSelector: string): void {
      shutdown();
      state.open = true;
      state.context = Object.freeze({ ...context });
      state.returnFocusSelector = returnFocusSelector;
      session = dependencies.operationAuthority.createSession({
        feature: {
          publish(event) {
            switch (event.kind) {
              case "started":
              case "replaced":
                state.comparison = null;
                state.error = "";
                state.loading = true;
                scheduleRender();
                break;
              case "terminal":
                state.loading = false;
                if (event.outcome.kind === "succeeded")
                  state.comparison = event.outcome.value;
                else
                  state.error = dependencies.describeError(event.outcome.error)
                    || "The authored Source comparison did not complete.";
                scheduleRender();
                break;
              case "canceled":
                state.loading = false;
                state.error = `Source comparison canceled (${event.reason}).`;
                scheduleRender();
                break;
              case "disposed":
                state.loading = false;
                break;
              case "progress":
                break;
            }
            return undefined;
          },
        },
        diagnostic: { report: diagnostic => dependencies.reportOperationDiagnostic(diagnostic) },
      });
      dependencies.render();
    },
    openUnavailable(reason: string, returnFocusSelector: string): void {
      shutdown();
      state.open = true;
      state.unavailableReason = reason;
      state.returnFocusSelector = returnFocusSelector;
      dependencies.render();
    },
    setAfterVersion(value: string): void {
      if (!state.open || value === state.afterVersion) return;
      if (session?.cancelCurrent("superseded").kind === "rejected") {
        diagnose("Source Diff replacement was rejected during feature publication.");
        return;
      }
      state.afterVersion = value;
      state.submittedRequest = null;
      state.comparison = null;
      state.loading = false;
      state.error = "";
      dependencies.render();
    },
    async compare(): Promise<void> {
      if (!state.open || !session || !state.context) return;
      if (!isExactSourceComparisonVersion(state.afterVersion)) {
        state.error = "Enter an exact After package version, not a range or floating version.";
        dependencies.render();
        return;
      }
      const context = state.context;
      const request: BrowserSourceComparisonRequest = Object.freeze({
        packageId: context.packageId,
        beforeVersion: context.version,
        afterVersion: state.afterVersion.trim(),
        framework: context.framework,
        assembly: context.assembly,
        typeIdentity: context.typeIdentity,
        memberName: context.memberName,
        selectorKey: context.selectorKey,
        metadataToken: context.metadataToken,
      });
      state.submittedRequest = request;
      const started = session.start(request, adapter);
      if (started.kind === "rejected") {
        state.loading = false;
        state.error = `The comparison could not start: ${started.reason.kind}.`;
        diagnose(state.error);
        dependencies.render();
        return;
      }
      await started.handle.quiesced;
    },
    close() {
      const returnFocusSelector = state.returnFocusSelector;
      return { handled: shutdown(), returnFocusSelector };
    },
    dispose: shutdown,
  };
}
