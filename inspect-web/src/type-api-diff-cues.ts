// Member change cues for the Type on screen: the Type-surface Library API Diff
// against the last-patch baseline, run as visible-subject background analysis
// for a Type whose Fast Diff API axis is reported. Owned by
// docs/design/inspect-web-background-analysis.md.

import type { BackgroundAnalysisQueue } from "./background-analysis.ts";
import type { BrowserLibraryApiDiffRequest } from "./facades/inspect-web-metadata.d.ts";
import {
  createHeldAnalysis,
  type HeldAnalysis,
  type HeldAnalysisEntry,
} from "./held-analysis.ts";
import {
  createLibraryApiDiffRequest,
  libraryApiDiffResultPresence,
  validateLibraryApiDiffResult,
  type LibraryApiDiffOperationInput,
} from "./library-api-diff.ts";
import {
  libraryFastDiffKey,
  type LibraryFastDiffBaseline,
} from "./library-fast-diff.ts";
import type {
  OperationAuthorityPage,
  OperationCancelReason,
  OperationDiagnostic,
  OperationId,
} from "./operation-authority.ts";

export interface TypeApiDiffCueRequest {
  readonly packageModel: object;
  readonly baseline: LibraryFastDiffBaseline;
  /** The Type's query identity (`ApiType.FullName`). */
  readonly typeQueryIdentifier: string;
}

export interface TypeApiDiffCuesDependencies {
  readonly queue: BackgroundAnalysisQueue;
  readonly operationAuthority: OperationAuthorityPage;
  query(
    operationId: OperationId,
    request: BrowserLibraryApiDiffRequest,
  ): Promise<unknown>;
  cancel(operationId: OperationId, reason: OperationCancelReason): void;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  render(): void;
}

/** Held changed-Member fingerprints per Type and baseline. */
export type TypeApiDiffCues = HeldAnalysis<TypeApiDiffCueRequest, ReadonlySet<string>>;

export function typeApiDiffCueKey(request: TypeApiDiffCueRequest): string {
  return JSON.stringify([
    "type-api-diff",
    libraryFastDiffKey(request.baseline),
    request.typeQueryIdentifier,
  ]);
}

function operationInput(request: TypeApiDiffCueRequest): LibraryApiDiffOperationInput {
  const { baseline } = request;
  return {
    packageModel: request.packageModel,
    packageId: baseline.packageId,
    currentVersion: baseline.currentVersion,
    targetVersion: baseline.targetVersion,
    targetFramework: baseline.targetFramework,
    compileAssetId: baseline.compileAssetId,
    // Member presence needs only the API changes of the one Type.
    query: {
      surface: "Type",
      analyses: ["api"],
      views: "Changes",
      typeNames: [request.typeQueryIdentifier],
      memberTargetIdentities: [],
    },
  };
}

export function createTypeApiDiffCues(
  dependencies: TypeApiDiffCuesDependencies,
): TypeApiDiffCues {
  return createHeldAnalysis<TypeApiDiffCueRequest, ReadonlySet<string>>({
    ...dependencies,
    name: "Member change cues",
    order: "visible-subject",
    key: typeApiDiffCueKey,
    query: (operationId, request) =>
      dependencies.query(operationId, createLibraryApiDiffRequest(operationInput(request))),
    read: (result, request) => {
      validateLibraryApiDiffResult(result, operationInput(request));
      switch (result.kind) {
        case "Succeeded":
          return {
            kind: "ready",
            value: libraryApiDiffResultPresence(result).memberFingerprints,
          };
        case "Canceled":
          return { kind: "canceled" };
        default:
          return {
            kind: "failed",
            error: result.error && result.error.length > 0
              ? result.error
              : "The Type's API comparison is unavailable.",
          };
      }
    },
  });
}

/**
 * The Members header line for a selected Type with a change cue: the change,
 * the member-cue state, and the Compare content that shows it.
 */
export function renderTypeChangeStatus(
  change: { readonly description: string; readonly api: boolean },
  cues: HeldAnalysisEntry<ReadonlySet<string>> | null,
  escapeHtml: (value: unknown) => string,
): string {
  const detail = cues?.status === "failed"
    ? `<span class="type-change-detail">Member cues unavailable: ${escapeHtml(cues.error)}</span>
      <button type="button" class="type-change-action" data-type-change-retry>Retry</button>`
    : cues?.status === "ready" && cues.value.size === 0
      ? '<span class="type-change-detail">No member changed; Compare shows the Type-level change.</span>'
      : "";
  return `<p class="type-change-status" role="status">
      <span class="item-achievement-glyph ${change.api ? "api-diff" : "body-diff"}" aria-hidden="true"></span>
      <span class="type-change-text">${escapeHtml(change.description)}</span>${detail}
      <button type="button" class="type-change-action" data-type-change-compare="${change.api ? "api" : "member-body"}">${change.api ? "Compare API" : "Compare bodies"}</button></p>`;
}

export function bindTypeChangeActions(
  root: ParentNode,
  actions: {
    compare(content: "api" | "member-body"): void;
    retry(): void;
  },
) {
  root.querySelectorAll<HTMLButtonElement>("[data-type-change-compare]")
    .forEach(button => button.addEventListener("click", () =>
      actions.compare(
        button.dataset.typeChangeCompare === "member-body" ? "member-body" : "api")));
  root.querySelectorAll<HTMLButtonElement>("[data-type-change-retry]")
    .forEach(button => button.addEventListener("click", () => actions.retry()));
}
