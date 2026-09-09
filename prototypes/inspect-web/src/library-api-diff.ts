import type {
  BrowserLibraryApiDiff,
  BrowserLibraryApiDiffRequest,
  BrowserLibraryApiDiffResult,
} from "./facades/inspect-web-metadata.d.ts";
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
import type { PackageVersionState } from "./catalog-requests.ts";
import type { ComparisonPackage, DiffTarget } from "./package-comparison-targets.ts";
import type { LibraryLens, WorkspaceScope } from "./data.ts";

// The Library-scoped coordinate this feature submits a request for. `comparisonVersion` is
// the resolved Diff target (Before); the rest identifies the currently inspected Library
// (After). The browser never orders or compares package versions itself — it only reads the
// already-resolved `previousVersion`/`previousVersionUnavailableReason` fields the managed
// catalog inventory carries.
export interface LibraryApiDiffRequestContext {
  readonly packageId: string;
  readonly version: string;
  readonly framework: string;
  readonly assembly: string;
  readonly comparisonVersion: string;
}

export type LibraryApiDiffTargetResolution =
  | { readonly kind: "resolved"; readonly comparisonVersion: string }
  | { readonly kind: "unresolved"; readonly reason: string };

export interface LibraryApiDiffViewContext {
  readonly engineReady: boolean;
  readonly loading: boolean;
  readonly hasError: boolean;
  readonly home: boolean;
  readonly credits: boolean;
  readonly packageQueryOpen: boolean;
  readonly explorerOpen: boolean;
  readonly hasPackage: boolean;
  readonly scope: WorkspaceScope;
  readonly libraryLens: LibraryLens;
}

export function libraryApiDiffViewIsActive(
  context: LibraryApiDiffViewContext,
): boolean {
  return context.engineReady
    && !context.loading
    && !context.hasError
    && !context.home
    && !context.credits
    && !context.packageQueryOpen
    && !context.explorerOpen
    && context.hasPackage
    && context.scope === "library"
    && context.libraryLens === "diff";
}

// Resolves the effective Diff target from the Package's comparison-target selection and the
// current version inventory, without ever parsing or ordering a version itself. An exact
// target always resolves to its selected version; an automatic ("previous") target resolves
// only once the managed inventory carries a usable `previousVersion` — a pending, failed, or
// predecessor-free inventory produces a visible no-request reason instead.
export function resolveLibraryApiDiffTarget(
  pkg: ComparisonPackage,
  diff: DiffTarget,
  versions: PackageVersionState,
): LibraryApiDiffTargetResolution {
  if (pkg.source.kind !== "nuget.org") {
    return {
      kind: "unresolved",
      reason: "Diff target selection is currently available for Gallery packages.",
    };
  }
  if (diff.kind === "exact") {
    return { kind: "resolved", comparisonVersion: diff.version };
  }
  if (versions.status !== "available") {
    return {
      kind: "unresolved",
      reason: versions.status === "failed"
        ? versions.message
        : "Reading available versions…",
    };
  }
  const { previousVersion, previousVersionUnavailableReason } = versions.inventory;
  if (previousVersionUnavailableReason) {
    return { kind: "unresolved", reason: previousVersionUnavailableReason };
  }
  if (!previousVersion) {
    return { kind: "unresolved", reason: "No earlier listed version is available." };
  }
  return { kind: "resolved", comparisonVersion: previousVersion };
}

// One signature identifies the exact coordinate the coordinator should be showing: the
// current package coordinate, the selected Library asset, and the resolved comparison
// version. Any difference between this and the state's retained signature means the prior
// request and result are stale and must not be reused.
export function libraryApiDiffSignature(
  context: Omit<LibraryApiDiffRequestContext, "comparisonVersion">,
  comparisonVersion: string,
): string {
  return JSON.stringify([
    context.packageId,
    context.version,
    context.framework,
    context.assembly,
    comparisonVersion,
  ]);
}

export interface LibraryApiDiffState {
  // The signature of the coordinate this state reflects — resolved or not.
  signature: string;
  // Non-empty only while the Diff target could not resolve; the state is then a visible
  // no-request state rather than a query.
  noRequestReason: string;
  // The immutable submitted request. Changing an input after submission cannot relabel it.
  request: BrowserLibraryApiDiffRequest | null;
  // Present only once a managed operation succeeds (in any presentation kind: Available,
  // Unavailable, or Rejected).
  result: BrowserLibraryApiDiff | null;
  loading: boolean;
  // Non-empty only for an expected/unexpected managed failure.
  error: string;
  // Non-empty only for the terminal-canceled outcome.
  canceledReason: string;
  // Detail-only selection. Selecting a Type never starts a managed call.
  selectedTypeId: string;
}

export function createLibraryApiDiffState(): LibraryApiDiffState {
  return {
    signature: "",
    noRequestReason: "",
    request: null,
    result: null,
    loading: false,
    error: "",
    canceledReason: "",
    selectedTypeId: "",
  };
}

export interface LibraryApiDiffDependencies {
  state: LibraryApiDiffState;
  operationAuthority: OperationAuthorityPage;
  queryLibraryApiDiff(
    operationId: OperationId,
    requestJson: string,
  ): Promise<BrowserLibraryApiDiffResult>;
  cancelLibraryApiDiff(operationId: OperationId, reason: OperationCancelReason): void;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  describeError(error: unknown): string;
  render(): void;
}

export interface LibraryApiDiffCoordinator {
  // Ensures the current signature is reflected: starts one managed request when the
  // coordinate resolves and is not already current, or publishes a visible no-request state
  // when it does not resolve. Idempotent for an unchanged, already-current signature.
  ensure(
    signature: string,
    context: LibraryApiDiffRequestContext | null,
    noRequestReason: string,
  ): void;
  // Leaving the Diff inspector invalidates its operation and state. Returning is another
  // explicit request to compare the then-current coordinate and target.
  leave(): void;
  // Explicit retry re-issues the current immutable request even though its signature is
  // unchanged, superseding a prior failed, canceled, or still-loading attempt. It is a no-op
  // for a no-request (unresolved-target) state; Change target is that state's escape.
  retry(): void;
  selectType(typeId: string): void;
  dispose(): boolean;
}

export function createLibraryApiDiffCoordinator(
  dependencies: LibraryApiDiffDependencies,
): LibraryApiDiffCoordinator {
  const { state } = dependencies;
  type Session = OperationSession<
    BrowserLibraryApiDiffRequest, BrowserLibraryApiDiff, unknown, never, never
  >;
  let session: Session | null = null;
  const requests = new Map<OperationId, BrowserLibraryApiDiffRequest>();

  const diagnose = (message: string): void => {
    dependencies.reportOperationDiagnostic({
      kind: "producer-contract", operationId: null, error: new Error(message),
    });
  };
  // Feature publication runs inside the authority's observer depth; rendering is scheduled
  // just past it, matching the Method Body and Source Diff coordinators.
  const scheduleRender = (): undefined => {
    queueMicrotask(() => dependencies.render());
    return undefined;
  };

  const adapter: OperationProducerAdapter<
    BrowserLibraryApiDiffRequest, BrowserLibraryApiDiff, unknown, never, never
  > = {
    prepare(identity, request, sink) {
      requests.set(identity.id, request);
      let cancellationRequested = false;
      const quiesce = (): undefined => {
        requests.delete(identity.id);
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
            if (cancellationRequested) return undefined;
            cancellationRequested = true;
            dependencies.cancelLibraryApiDiff(identity.id, reason);
            return undefined;
          },
          activate() {
            let query: Promise<BrowserLibraryApiDiffResult>;
            try {
              query = dependencies.queryLibraryApiDiff(identity.id, JSON.stringify(request));
            } catch (error: unknown) {
              return boundaryFailure(error);
            }
            void query.then(result => {
              reportComparisonEnvelope(
                sink, "Library API diff", result.version, result.kind,
                result.value, result.failureKind, result.error,
                result.diagnostic, result.reason);
              return quiesce();
            }, boundaryFailure);
            return undefined;
          },
          abandon: () => {
            requests.delete(identity.id);
            return undefined;
          },
        },
      };
    },
  };

  const publish = (
    event: OperationFeatureEvent<BrowserLibraryApiDiff, unknown, never>,
  ): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced": {
        state.request = requests.get(event.operation.id) ?? null;
        state.result = null;
        state.error = "";
        state.canceledReason = "";
        state.loading = true;
        // A fresh request starts detail selection empty; the view selects the first row once
        // a non-empty result arrives, without the coordinator needing to know row order.
        state.selectedTypeId = "";
        scheduleRender();
        break;
      }
      case "terminal": {
        state.request = requests.get(event.operationId) ?? state.request;
        state.loading = false;
        if (event.outcome.kind === "succeeded") {
          state.result = event.outcome.value;
        } else {
          state.error = dependencies.describeError(event.outcome.error)
            || "The Library API diff did not complete.";
        }
        scheduleRender();
        break;
      }
      case "canceled":
        state.loading = false;
        state.canceledReason = event.reason;
        scheduleRender();
        break;
      case "disposed":
        state.loading = false;
        break;
      case "progress":
        break;
    }
    return undefined;
  };

  const ensureSession = (): Session => {
    session ??= dependencies.operationAuthority.createSession({
      feature: { publish },
      diagnostic: {
        report: diagnostic => dependencies.reportOperationDiagnostic(diagnostic),
      },
    });
    return session;
  };

  const publishNoRequest = (signature: string, reason: string): void => {
    if (session?.cancelCurrent("superseded").kind === "rejected") {
      diagnose("Library API Diff replacement was rejected during feature publication.");
      return;
    }
    state.signature = signature;
    state.noRequestReason = reason;
    state.request = null;
    state.result = null;
    state.error = "";
    state.canceledReason = "";
    state.loading = false;
    state.selectedTypeId = "";
    dependencies.render();
  };

  const startRequest = (request: BrowserLibraryApiDiffRequest): void => {
    const started = ensureSession().start(request, adapter);
    if (started.kind === "rejected") {
      state.loading = false;
      state.error = `The Library API diff could not start: ${started.reason.kind}.`;
      diagnose(state.error);
      dependencies.render();
    }
  };

  const release = (): boolean => {
    const hadContent = Boolean(
      state.signature || state.request || state.result
      || state.loading || state.error || state.canceledReason);
    if (session?.dispose().kind === "rejected") {
      diagnose("Library API Diff disposal was rejected during feature publication.");
      return false;
    }
    session = null;
    requests.clear();
    Object.assign(state, createLibraryApiDiffState());
    return hadContent;
  };

  return {
    ensure(signature, context, noRequestReason) {
      if (!context) {
        if (state.signature === signature && state.noRequestReason === noRequestReason) return;
        publishNoRequest(signature, noRequestReason);
        return;
      }
      if (state.signature === signature
        && (state.loading || state.request || state.result
          || state.error || state.canceledReason)) {
        return;
      }
      state.signature = signature;
      state.noRequestReason = "";
      startRequest(Object.freeze({
        packageId: context.packageId,
        version: context.version,
        framework: context.framework,
        comparisonVersion: context.comparisonVersion,
        assembly: context.assembly,
      }));
    },

    leave() {
      release();
    },

    retry() {
      if (!state.request) return;
      startRequest(Object.freeze({ ...state.request }));
    },

    selectType(typeId) {
      if (state.selectedTypeId === typeId) return;
      state.selectedTypeId = typeId;
      dependencies.render();
    },

    dispose() {
      return release();
    },
  };
}
