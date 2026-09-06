import type {
  BrowserMethodBodyComparison,
  BrowserMethodBodyComparisonRequest,
  BrowserMethodBodyComparisonResult,
  BrowserMethodBodySelection,
  BrowserMethodBodyTargets,
  BrowserMethodBodyTargetsResult,
} from "./facades/inspect-web-source.d.ts";
import type {
  OperationAuthorityPage,
  OperationCancelReason,
  OperationDiagnostic,
  OperationFeatureEvent,
  OperationId,
  OperationProducerAdapter,
  OperationSession,
} from "./operation-authority.ts";
import { reportComparisonEnvelope } from "./comparison-envelope.ts";

// The launching Before selection: the physical method or explicitly selected
// accessor/body the person is inspecting, in its own implementation assembly.
export interface MethodBodyComparisonContext {
  packageId: string;
  version: string;
  framework: string;
  assembly: string;
  typeIdentity: string;
  memberName: string;
  selectorKey: string;
  metadataToken: number;
  label: string;
}

// A platform selection is resident in the retained runtime pack rather than an acquired
// package, and the managed target signature discriminates it by an empty package id while
// keeping the exact platform version, framework and resident assembly.
export function methodBodyComparisonPackageId(
  pkg: { readonly id: string; readonly isRuntimePack?: boolean },
): string {
  return pkg.isRuntimePack ? "" : pkg.id;
}

export function isMethodBodyToken(token: number): boolean {
  return (token & 0xff000000) === 0x06000000 && (token & 0x00ffffff) !== 0;
}

export interface MethodBodyDiffState {
  open: boolean;
  context: MethodBodyComparisonContext | null;
  returnFocusSelector: string;
  unavailableReason: string;
  filter: string;
  candidateKey: string;
  targets: BrowserMethodBodyTargets | null;
  targetsLoading: boolean;
  targetsError: string;
  submittedRequest: BrowserMethodBodyComparisonRequest | null;
  comparison: BrowserMethodBodyComparison | null;
  comparisonLoading: boolean;
  comparisonError: string;
}

export function createMethodBodyDiffState(): MethodBodyDiffState {
  return {
    open: false,
    context: null,
    returnFocusSelector: "",
    unavailableReason: "",
    filter: "",
    candidateKey: "",
    targets: null,
    targetsLoading: false,
    targetsError: "",
    submittedRequest: null,
    comparison: null,
    comparisonLoading: false,
    comparisonError: "",
  };
}

// One physical selection is identified by its declaring type, member, body selector and
// MethodDef row; display text never identifies a candidate.
export function methodBodySelectionKey(
  selection: BrowserMethodBodySelection,
): string {
  return [
    selection.typeIdentity,
    selection.memberName,
    selection.selectorKey,
    String(selection.metadataToken),
  ].join("\u241f");
}

// A same-method pair is a valid request, so the launching selection stays choosable even
// when the inventory does not repeat it.
export function methodBodyChoices(
  targets: BrowserMethodBodyTargets,
): readonly BrowserMethodBodySelection[] {
  const beforeKey = methodBodySelectionKey(targets.before);
  return targets.methods.some(
    method => methodBodySelectionKey(method) === beforeKey)
    ? targets.methods
    : [targets.before, ...targets.methods];
}

export function filterMethodBodyChoices(
  choices: readonly BrowserMethodBodySelection[],
  filter: string,
  selectedKey: string,
): readonly BrowserMethodBodySelection[] {
  const needle = filter.trim().toLowerCase();
  if (!needle) return choices;
  return choices.filter(choice => {
    if (methodBodySelectionKey(choice) === selectedKey) return true;
    return [
      choice.label,
      choice.typeIdentity,
      choice.memberName,
      choice.selectorKey,
    ].some(value => value.toLowerCase().includes(needle));
  });
}

export function methodBodyChoiceForKey(
  targets: BrowserMethodBodyTargets | null,
  key: string,
): BrowserMethodBodySelection | null {
  if (!targets || !key) return null;
  return methodBodyChoices(targets).find(
    choice => methodBodySelectionKey(choice) === key) ?? null;
}

export interface MethodBodyComparisonDependencies {
  state: MethodBodyDiffState;
  operationAuthority: OperationAuthorityPage;
  queryTargets(
    operationId: OperationId,
    context: MethodBodyComparisonContext,
  ): Promise<BrowserMethodBodyTargetsResult>;
  queryComparison(
    operationId: OperationId,
    requestJson: string,
  ): Promise<BrowserMethodBodyComparisonResult>;
  cancelMethodBodyComparison(
    operationId: OperationId,
    reason: OperationCancelReason,
  ): void;
  readonly reportOperationDiagnostic: (
    diagnostic: OperationDiagnostic,
  ) => undefined;
  describeError(error: unknown): string;
  render(): void;
}

interface MethodBodyDiffDismissal {
  handled: boolean;
  returnFocusSelector: string;
}

export interface MethodBodyComparisonCoordinator {
  isOpen(): boolean;
  open(
    context: MethodBodyComparisonContext,
    returnFocusSelector: string,
  ): Promise<void>;
  openUnavailable(reason: string, returnFocusSelector: string): void;
  setFilter(value: string): void;
  selectCandidate(key: string): boolean;
  compare(): Promise<void>;
  close(): MethodBodyDiffDismissal;
  dispose(): boolean;
}

export function createMethodBodyComparisonCoordinator(
  dependencies: MethodBodyComparisonDependencies,
): MethodBodyComparisonCoordinator {
  const { state } = dependencies;
  type TargetsSession = OperationSession<
    MethodBodyComparisonContext,
    BrowserMethodBodyTargets,
    unknown,
    never,
    never
  >;
  type ComparisonSession = OperationSession<
    BrowserMethodBodyComparisonRequest,
    BrowserMethodBodyComparison,
    unknown,
    never,
    never
  >;
  interface DialogSessions {
    readonly targets: TargetsSession;
    readonly comparison: ComparisonSession;
  }

  let sessions: DialogSessions | null = null;
  const comparisonRequests =
    new Map<OperationId, BrowserMethodBodyComparisonRequest>();

  const diagnose = (message: string, operationId: OperationId | null) => {
    dependencies.reportOperationDiagnostic({
      kind: "producer-contract",
      operationId,
      error: new Error(message),
    });
  };

  // Feature publication runs inside the authority's observer depth, where other page
  // owners cannot accept cancellation or replacement. Rendering is scheduled just past it.
  const scheduleRender = (): undefined => {
    queueMicrotask(() => {
      dependencies.render();
    });
    return undefined;
  };

  const publishTargetsEvent = (
    event: OperationFeatureEvent<BrowserMethodBodyTargets, unknown, never>,
  ): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced":
        state.targets = null;
        state.targetsError = "";
        state.targetsLoading = true;
        scheduleRender();
        break;
      case "terminal":
        state.targetsLoading = false;
        if (event.outcome.kind === "succeeded")
          state.targets = event.outcome.value;
        else
          state.targetsError =
            dependencies.describeError(event.outcome.error)
            || "The comparison inventory is unavailable.";
        scheduleRender();
        break;
      case "canceled":
      case "disposed":
        state.targetsLoading = false;
        break;
      case "progress":
        break;
    }
    return undefined;
  };

  const publishComparisonEvent = (
    event: OperationFeatureEvent<BrowserMethodBodyComparison, unknown, never>,
  ): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced": {
        const request = comparisonRequests.get(event.operation.id) ?? null;
        state.submittedRequest = request;
        state.comparison = null;
        state.comparisonError = "";
        state.comparisonLoading = true;
        scheduleRender();
        break;
      }
      case "terminal": {
        // The submitted request, not the current chooser, owns this publication.
        const request = comparisonRequests.get(event.operationId) ?? null;
        state.submittedRequest = request;
        state.comparisonLoading = false;
        if (event.outcome.kind === "succeeded")
          state.comparison = event.outcome.value;
        else
          state.comparisonError =
            dependencies.describeError(event.outcome.error)
            || "The method body comparison did not complete.";
        scheduleRender();
        break;
      }
      case "canceled":
      case "disposed":
        state.comparisonLoading = false;
        break;
      case "progress":
        break;
    }
    return undefined;
  };

  const targetsAdapter: OperationProducerAdapter<
    MethodBodyComparisonContext,
    BrowserMethodBodyTargets,
    unknown,
    never,
    never
  > = {
    prepare: (identity, context, sink) => {
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
          requestCancellation: reason => {
            if (cancellationRequested) return undefined;
            cancellationRequested = true;
            dependencies.cancelMethodBodyComparison(identity.id, reason);
            return undefined;
          },
          activate: () => {
            let query: Promise<BrowserMethodBodyTargetsResult>;
            try {
              query = dependencies.queryTargets(identity.id, context);
            } catch (error: unknown) {
              return boundaryFailure(error);
            }
            void query.then(result => {
              reportComparisonEnvelope(
                sink,
                "method body target",
                result.version,
                result.kind,
                result.value,
                result.failureKind,
                result.error,
                result.diagnostic,
                result.reason);
              return quiesce();
            }, boundaryFailure);
            return undefined;
          },
          abandon: () => undefined,
        },
      };
    },
  };

  const comparisonAdapter: OperationProducerAdapter<
    BrowserMethodBodyComparisonRequest,
    BrowserMethodBodyComparison,
    unknown,
    never,
    never
  > = {
    prepare: (identity, request, sink) => {
      comparisonRequests.set(identity.id, request);
      let cancellationRequested = false;
      const quiesce = (): undefined => {
        comparisonRequests.delete(identity.id);
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
          requestCancellation: reason => {
            if (cancellationRequested) return undefined;
            cancellationRequested = true;
            dependencies.cancelMethodBodyComparison(identity.id, reason);
            return undefined;
          },
          activate: () => {
            let query: Promise<BrowserMethodBodyComparisonResult>;
            try {
              query = dependencies.queryComparison(
                identity.id,
                JSON.stringify(request));
            } catch (error: unknown) {
              return boundaryFailure(error);
            }
            void query.then(result => {
              reportComparisonEnvelope(
                sink,
                "method body comparison",
                result.version,
                result.kind,
                result.value,
                result.failureKind,
                result.error,
                result.diagnostic,
                result.reason);
              return quiesce();
            }, boundaryFailure);
            return undefined;
          },
          abandon: () => {
            comparisonRequests.delete(identity.id);
            return undefined;
          },
        },
      };
    },
  };

  const createSessions = (): DialogSessions => ({
    targets: dependencies.operationAuthority.createSession({
      feature: { publish: publishTargetsEvent },
      diagnostic: {
        report: diagnostic =>
          dependencies.reportOperationDiagnostic(diagnostic),
      },
    }),
    comparison: dependencies.operationAuthority.createSession({
      feature: { publish: publishComparisonEvent },
      diagnostic: {
        report: diagnostic =>
          dependencies.reportOperationDiagnostic(diagnostic),
      },
    }),
  });

  // Closing, navigation replacement and re-opening all release the feature operations
  // through the existing authority boundary, which is what suppresses their late results.
  const shutdown = (): boolean => {
    const wasOpen = state.open;
    if (sessions) {
      const comparison = sessions.comparison.dispose();
      const targets = sessions.targets.dispose();
      if (comparison.kind === "rejected" || targets.kind === "rejected") {
        diagnose(
          "Method Body Diff disposal was rejected during feature publication.",
          null);
        return false;
      }
      sessions = null;
    }
    comparisonRequests.clear();
    Object.assign(state, createMethodBodyDiffState());
    return wasOpen;
  };

  const clearComparison = (): void => {
    if (!sessions) return;
    const cancellation = sessions.comparison.cancelCurrent("superseded");
    if (cancellation.kind === "rejected") {
      diagnose(
        "Method Body Diff replacement was rejected during feature publication.",
        null);
      return;
    }
    state.comparison = null;
    state.comparisonError = "";
    state.comparisonLoading = false;
    state.submittedRequest = null;
  };

  return {
    isOpen: () => state.open,

    async open(context, returnFocusSelector) {
      shutdown();
      state.open = true;
      state.context = { ...context };
      state.returnFocusSelector = returnFocusSelector;
      sessions = createSessions();
      const started = sessions.targets.start(state.context, targetsAdapter);
      if (started.kind === "rejected") {
        state.targetsLoading = false;
        state.targetsError =
          `The comparison inventory could not start: ${started.reason.kind}.`;
        diagnose(
          `Method Body Diff inventory start was rejected: ${started.reason.kind}.`,
          null);
        dependencies.render();
        return;
      }
      await started.handle.quiesced;
    },

    openUnavailable(reason, returnFocusSelector) {
      shutdown();
      state.open = true;
      state.returnFocusSelector = returnFocusSelector;
      state.unavailableReason = reason;
      dependencies.render();
    },

    setFilter(value) {
      if (!state.open || state.filter === value) return;
      state.filter = value;
      dependencies.render();
    },

    // Choosing a candidate never compares, and it never leaves an older result under a
    // newer pair.
    selectCandidate(key) {
      if (!state.open || state.candidateKey === key) return false;
      state.candidateKey = key;
      clearComparison();
      dependencies.render();
      return true;
    },

    async compare() {
      if (!state.open || !sessions) return;
      const targets = state.targets;
      const after = methodBodyChoiceForKey(targets, state.candidateKey);
      if (!targets || !after) {
        state.comparisonError =
          "Choose an After method before comparing method bodies.";
        dependencies.render();
        return;
      }
      const request: BrowserMethodBodyComparisonRequest = {
        packageId: targets.packageId,
        version: targets.version,
        framework: targets.framework,
        assembly: targets.assembly,
        moduleVersionId: targets.moduleVersionId,
        before: targets.before,
        after,
      };
      const started = sessions.comparison.start(request, comparisonAdapter);
      if (started.kind === "rejected") {
        state.comparisonLoading = false;
        state.comparisonError =
          `The comparison could not start: ${started.reason.kind}.`;
        diagnose(
          `Method Body Diff comparison start was rejected: ${started.reason.kind}.`,
          null);
        dependencies.render();
        return;
      }
      await started.handle.quiesced;
    },

    close() {
      const returnFocusSelector = state.returnFocusSelector;
      const handled = shutdown();
      return { handled, returnFocusSelector };
    },

    dispose() {
      return shutdown();
    },
  };
}
