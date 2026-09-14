import type {
  BrowserLibraryApiDiffEndpoint,
  BrowserLibraryApiDiffResult,
  BrowserLibraryApiDiffType,
} from "./facades/inspect-web-metadata.d.ts";
import type { EffectiveDiffTarget } from "./package-comparison-targets.ts";
import type {
  OperationAuthorityPage,
  OperationCancelReason,
  OperationDiagnostic,
  OperationFeatureEvent,
  OperationId,
  OperationProducerAdapter,
  OperationSession,
} from "./operation-authority.ts";

export interface LibraryApiDiffSelection {
  readonly packageModel: object;
  readonly packageId: string;
  readonly currentVersion: string;
  readonly targetFramework: string;
  readonly compileAssetId: string;
  readonly target: EffectiveDiffTarget;
}

export interface LibraryApiDiffOperationInput {
  readonly packageModel: object;
  readonly packageId: string;
  readonly currentVersion: string;
  readonly targetVersion: string;
  readonly targetFramework: string;
  readonly compileAssetId: string;
}

export type LibraryApiDiffState =
  | { readonly status: "idle" }
  | {
      readonly status: "target-loading";
      readonly selection: LibraryApiDiffSelection;
      readonly message: string;
    }
  | {
      readonly status: "target-unavailable";
      readonly selection: LibraryApiDiffSelection;
      readonly message: string;
    }
  | {
      readonly status: "loading";
      readonly input: LibraryApiDiffOperationInput;
    }
  | {
      readonly status: "ready";
      readonly input: LibraryApiDiffOperationInput;
      readonly result: BrowserLibraryApiDiffResult;
    }
  | {
      readonly status: "failed";
      readonly input: LibraryApiDiffOperationInput;
      readonly error: string;
    };

export interface LibraryApiDiffStateHost {
  libraryApiDiff: LibraryApiDiffState;
}

export interface LibraryApiDiffDependencies {
  readonly state: LibraryApiDiffStateHost;
  readonly operationAuthority: OperationAuthorityPage;
  query(
    operationId: OperationId,
    requestJson: string,
  ): Promise<BrowserLibraryApiDiffResult>;
  cancel(
    operationId: OperationId,
    reason: OperationCancelReason,
  ): void;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  render(): void;
}

export interface LibraryApiDiffCoordinator {
  reconcile(selection: LibraryApiDiffSelection | null): void;
  retry(selection: LibraryApiDiffSelection): void;
  cancelCurrentRequest(): boolean;
}

function sameSelection(
  left: LibraryApiDiffSelection,
  right: LibraryApiDiffSelection,
): boolean {
  return left.packageModel === right.packageModel
    && left.packageId === right.packageId
    && left.currentVersion === right.currentVersion
    && left.targetFramework === right.targetFramework
    && left.compileAssetId === right.compileAssetId
    && left.target.kind === right.target.kind
    && (left.target.kind !== "available"
      || (right.target.kind === "available"
        && left.target.version === right.target.version))
    && (left.target.kind === "available"
      || (right.target.kind !== "available"
        && left.target.message === right.target.message));
}

function sameInput(
  left: LibraryApiDiffOperationInput,
  right: LibraryApiDiffOperationInput,
): boolean {
  return left.packageModel === right.packageModel
    && left.packageId === right.packageId
    && left.currentVersion === right.currentVersion
    && left.targetVersion === right.targetVersion
    && left.targetFramework === right.targetFramework
    && left.compileAssetId === right.compileAssetId;
}

function stateMatchesSelection(
  state: LibraryApiDiffState,
  selection: LibraryApiDiffSelection,
): boolean {
  if (state.status === "target-loading"
    || state.status === "target-unavailable") {
    return sameSelection(state.selection, selection);
  }
  if (selection.target.kind !== "available"
    || state.status === "idle") {
    return false;
  }
  return sameInput(state.input, {
    packageModel: selection.packageModel,
    packageId: selection.packageId,
    currentVersion: selection.currentVersion,
    targetVersion: selection.target.version,
    targetFramework: selection.targetFramework,
    compileAssetId: selection.compileAssetId,
  });
}

function requestJson(input: LibraryApiDiffOperationInput): string {
  return JSON.stringify({
    schemaVersion: 1,
    packageId: input.packageId,
    currentVersion: input.currentVersion,
    targetVersion: input.targetVersion,
    targetFramework: input.targetFramework,
    compileAssetId: input.compileAssetId,
  });
}

function validateResult(
  result: BrowserLibraryApiDiffResult,
  input: LibraryApiDiffOperationInput,
): void {
  if (result.schemaVersion !== 1)
    throw new Error("Unsupported Library API Diff result schema.");
  const request = result.request;
  if (request === null
    || request.schemaVersion !== 1
    || request.packageId !== input.packageId
    || request.currentVersion !== input.currentVersion
    || request.targetVersion !== input.targetVersion
    || request.targetFramework !== input.targetFramework
    || request.compileAssetId !== input.compileAssetId) {
    throw new Error("Library API Diff result does not match its request.");
  }
  switch (result.kind) {
    case "Succeeded":
      if (result.value === null)
        throw new Error("Library API Diff success has no value.");
      return;
    case "Unavailable":
      if (result.unavailable === null)
        throw new Error("Library API Diff unavailable result has no evidence.");
      return;
    case "Rejected":
      if (result.rejected === null)
        throw new Error("Library API Diff rejection has no evidence.");
      return;
    case "Failed":
      if (result.failureKind === null
        || typeof result.error !== "string"
        || typeof result.diagnostic !== "string") {
        throw new Error("Library API Diff failure has no diagnostic.");
      }
      return;
    case "Canceled":
      if (typeof result.reason !== "string")
        throw new Error("Library API Diff cancellation has no reason.");
      return;
    default:
      throw new Error("Unknown Library API Diff result kind.");
  }
}

export function createLibraryApiDiffCoordinator(
  dependencies: LibraryApiDiffDependencies,
): LibraryApiDiffCoordinator {
  type FeatureEvent =
    OperationFeatureEvent<BrowserLibraryApiDiffResult, unknown, never>;
  type Session = OperationSession<
    LibraryApiDiffOperationInput,
    BrowserLibraryApiDiffResult,
    unknown,
    never,
    never
  >;

  const inputs = new Map<OperationId, LibraryApiDiffOperationInput>();
  const inputFor = (operationId: OperationId) => {
    const input = inputs.get(operationId);
    if (input === undefined)
      throw new Error("Library API Diff operation context is unavailable.");
    return input;
  };
  const publish = (event: FeatureEvent): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced":
        dependencies.state.libraryApiDiff = {
          status: "loading",
          input: inputFor(event.operation.id),
        };
        break;
      case "terminal": {
        const input = inputFor(event.operationId);
        dependencies.state.libraryApiDiff =
          event.outcome.kind === "succeeded"
            ? {
                status: "ready",
                input,
                result: event.outcome.value,
              }
            : {
                status: "failed",
                input,
                error: dependencies.describeError(event.outcome.error),
              };
        dependencies.render();
        break;
      }
      case "canceled":
      case "disposed":
        dependencies.state.libraryApiDiff = { status: "idle" };
        break;
      case "progress":
        break;
    }
    return undefined;
  };
  const session: Session = dependencies.operationAuthority.createSession({
    feature: { publish },
    diagnostic: {
      report: diagnostic =>
        dependencies.reportOperationDiagnostic(diagnostic),
    },
  });
  const adapter: OperationProducerAdapter<
    LibraryApiDiffOperationInput,
    BrowserLibraryApiDiffResult,
    unknown,
    never,
    never
  > = {
    prepare: (identity, input, sink) => {
      inputs.set(identity.id, input);
      let cancellationRequested = false;
      const quiesce = (): undefined => {
        inputs.delete(identity.id);
        sink.reportQuiesced();
        return undefined;
      };
      const boundaryFailure = (error: unknown): undefined => {
        sink.reportUnexpectedTerminal(error, error);
        return quiesce();
      };
      const finish = (result: BrowserLibraryApiDiffResult): undefined => {
        try {
          validateResult(result, input);
          sink.reportTerminal({ kind: "succeeded", value: result });
        } catch (error: unknown) {
          sink.reportUnexpectedTerminal(error, error);
        }
        return quiesce();
      };
      return {
        kind: "prepared",
        binding: {
          requestCancellation: reason => {
            if (!cancellationRequested) {
              cancellationRequested = true;
              dependencies.cancel(identity.id, reason);
            }
            return undefined;
          },
          activate: () => {
            let query: Promise<BrowserLibraryApiDiffResult>;
            try {
              query = dependencies.query(identity.id, requestJson(input));
            } catch (error: unknown) {
              return boundaryFailure(error);
            }
            void query.then(finish, boundaryFailure);
            return undefined;
          },
          abandon: () => {
            inputs.delete(identity.id);
            return undefined;
          },
        },
      };
    },
  };

  const cancelCurrentRequest = (): boolean => {
    const cancellation = session.cancelCurrent("superseded");
    if (cancellation.kind === "rejected") {
      dependencies.reportOperationDiagnostic({
        kind: "producer-contract",
        operationId: null,
        error: new Error(
          "Library API Diff cancellation was attempted during feature publication.",
        ),
      });
      return false;
    }
    dependencies.state.libraryApiDiff = { status: "idle" };
    return cancellation.kind === "applied";
  };

  const start = (selection: LibraryApiDiffSelection): void => {
    if (selection.target.kind !== "available")
      throw new Error("A Library API Diff operation requires a resolved target.");
    const started = session.start({
      packageModel: selection.packageModel,
      packageId: selection.packageId,
      currentVersion: selection.currentVersion,
      targetVersion: selection.target.version,
      targetFramework: selection.targetFramework,
      compileAssetId: selection.compileAssetId,
    }, adapter);
    if (started.kind === "rejected") {
      dependencies.state.libraryApiDiff = {
        status: "failed",
        input: {
          packageModel: selection.packageModel,
          packageId: selection.packageId,
          currentVersion: selection.currentVersion,
          targetVersion: selection.target.version,
          targetFramework: selection.targetFramework,
          compileAssetId: selection.compileAssetId,
        },
        error: `Library API Diff could not start: ${started.reason.kind}.`,
      };
    }
  };

  const reconcile = (selection: LibraryApiDiffSelection | null): void => {
    if (selection === null) {
      cancelCurrentRequest();
      return;
    }
    if (stateMatchesSelection(
      dependencies.state.libraryApiDiff,
      selection,
    )) {
      return;
    }
    if (selection.target.kind !== "available") {
      cancelCurrentRequest();
      dependencies.state.libraryApiDiff = {
        status: selection.target.kind === "loading"
          ? "target-loading"
          : "target-unavailable",
        selection,
        message: selection.target.message,
      };
      return;
    }
    start(selection);
  };

  return {
    reconcile,
    retry(selection) {
      cancelCurrentRequest();
      start(selection);
    },
    cancelCurrentRequest,
  };
}

function compactCount(value: number, label: string): string {
  return value === 0 ? "" : `${value.toLocaleString()} ${label}`;
}

function endpointIssues(endpoint: BrowserLibraryApiDiffEndpoint): string {
  return endpoint.issues
    .map(issue => issue.detail ?? String(issue.kind))
    .join(" ");
}

function typeMetrics(type: BrowserLibraryApiDiffType): string {
  return [
    compactCount(type.changedMemberCount, type.changedMemberCount === 1
      ? "member"
      : "members"),
    compactCount(type.breakingCount, "breaking"),
    compactCount(type.additiveCount, "additive"),
    compactCount(type.potentiallyBreakingCount, "potentially breaking"),
  ].filter(Boolean).join(" · ");
}

function renderTypeRow(
  type: BrowserLibraryApiDiffType,
  escapeHtml: (value: unknown) => string,
): string {
  const before = type.before?.identifier ?? "";
  const after = type.after?.identifier ?? "";
  const definition = type.typeDefinitionChanged === true
    ? '<span class="library-api-diff-definition">Type definition changed</span>'
    : "";
  return `<li class="library-api-diff-type" data-before-type-id="${escapeHtml(before)}" data-after-type-id="${escapeHtml(after)}">
    <span class="library-api-diff-state library-api-diff-state-${String(type.state).toLowerCase()}">${escapeHtml(type.state)}</span>
    <span class="library-api-diff-type-copy">
      <strong>${escapeHtml(type.display)}</strong>
      <span>${escapeHtml(typeMetrics(type))}</span>
      ${definition}
    </span>
  </li>`;
}

function renderFrame(
  input: LibraryApiDiffOperationInput | null,
  status: string,
  content: string,
  escapeHtml: (value: unknown) => string,
): string {
  const target = input === null
    ? "Package Diff target"
    : `${input.targetVersion} → ${input.currentVersion}`;
  return `<section class="library-api-diff" aria-labelledby="library-api-diff-title">
    <header class="library-api-diff-head">
      <div>
        <p class="library-api-diff-kicker">Compare · Diff</p>
        <h1 id="library-api-diff-title">Library API diff</h1>
        <p class="library-api-diff-target">${escapeHtml(target)}</p>
      </div>
      <button type="button" class="library-api-diff-change-target" id="library-api-diff-change-target">Change target</button>
    </header>
    <p class="library-api-diff-status" role="status">${escapeHtml(status)}</p>
    ${content}
  </section>`;
}

export function renderLibraryApiDiff(
  state: LibraryApiDiffState,
  escapeHtml: (value: unknown) => string,
): string {
  if (state.status === "idle") {
    return renderFrame(
      null,
      "Choose a Gallery Package Library to compare.",
      "",
      escapeHtml,
    );
  }
  if (state.status === "target-loading"
    || state.status === "target-unavailable") {
    return renderFrame(
      null,
      state.message,
      state.status === "target-loading"
        ? '<div class="library-api-diff-loading" aria-hidden="true"></div>'
        : '<div class="library-api-diff-empty">Choose another Package Diff target to continue.</div>',
      escapeHtml,
    );
  }
  if (state.status === "loading") {
    return renderFrame(
      state.input,
      "Comparing complete public API surfaces...",
      '<div class="library-api-diff-loading" aria-hidden="true"></div>',
      escapeHtml,
    );
  }
  if (state.status === "failed") {
    return renderFrame(
      state.input,
      state.error,
      '<button type="button" class="library-api-diff-retry" id="library-api-diff-retry">Retry comparison</button>',
      escapeHtml,
    );
  }

  const { input, result } = state;
  switch (result.kind) {
    case "Succeeded": {
      const value = result.value;
      if (value === null)
        throw new Error("Library API Diff success has no value.");
      const aggregate = value.aggregate;
      const metrics = [
        `${aggregate.changedTypeCount.toLocaleString()} changed ${
          aggregate.changedTypeCount === 1 ? "Type" : "Types"
        }`,
        compactCount(aggregate.changedMemberCount, "changed members"),
        compactCount(aggregate.breakingCount, "breaking"),
        compactCount(aggregate.additiveCount, "additive"),
        compactCount(
          aggregate.potentiallyBreakingCount,
          "potentially breaking",
        ),
      ].filter(Boolean);
      const content = value.types.length === 0
        ? `<div class="library-api-diff-empty">
            <strong>No public API changes</strong>
            <span>The selected Library is unchanged between these versions.</span>
          </div>`
        : `<div class="library-api-diff-metrics">${metrics.map(metric =>
            `<span>${escapeHtml(metric)}</span>`).join("")}</div>
          <ol class="library-api-diff-types">${value.types.map(type =>
            renderTypeRow(type, escapeHtml)).join("")}</ol>`;
      return renderFrame(
        input,
        value.types.length === 0
          ? "Comparison complete. No changed Types."
          : `Comparison complete. ${aggregate.changedTypeCount.toLocaleString()} changed Types.`,
        content,
        escapeHtml,
      );
    }
    case "Unavailable": {
      const unavailable = result.unavailable;
      if (unavailable === null)
        throw new Error("Library API Diff unavailable result has no evidence.");
      const detail = [
        endpointIssues(unavailable.target),
        endpointIssues(unavailable.current),
      ].filter(Boolean).join(" ");
      return renderFrame(
        input,
        `Comparison unavailable: ${String(unavailable.kind)}.`,
        `<div class="library-api-diff-empty">${escapeHtml(
          detail || "One or both API surfaces are incomplete.",
        )}</div>`,
        escapeHtml,
      );
    }
    case "Rejected": {
      const rejected = result.rejected;
      if (rejected === null)
        throw new Error("Library API Diff rejection has no evidence.");
      const bound = rejected.bound === null
        ? ""
        : ` Bound ${rejected.bound.toLocaleString()}, observed ${
          rejected.observed?.toLocaleString() ?? "unknown"
        }.`;
      return renderFrame(
        input,
        `Comparison rejected: ${String(rejected.kind)}.`,
        `<div class="library-api-diff-empty">${escapeHtml(
          `The complete result could not be admitted.${bound}`,
        )}</div>`,
        escapeHtml,
      );
    }
    case "Failed":
      return renderFrame(
        input,
        result.error ?? "Library API Diff failed.",
        '<button type="button" class="library-api-diff-retry" id="library-api-diff-retry">Retry comparison</button>',
        escapeHtml,
      );
    case "Canceled":
      return renderFrame(
        input,
        "Comparison canceled.",
        '<button type="button" class="library-api-diff-retry" id="library-api-diff-retry">Run comparison</button>',
        escapeHtml,
      );
    default:
      throw new Error("Unknown Library API Diff result kind.");
  }
}

export function bindLibraryApiDiff(
  root: ParentNode,
  actions: {
    readonly changeTarget: () => void;
    readonly retry: () => void;
  },
): void {
  root.querySelector("#library-api-diff-change-target")
    ?.addEventListener("click", actions.changeTarget);
  root.querySelector("#library-api-diff-retry")
    ?.addEventListener("click", actions.retry);
}
