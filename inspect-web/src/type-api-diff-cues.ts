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
  /** The Type's definition identity, which the diff's Type rows carry. */
  readonly typeIdentifier: string;
}

/** What the complete API diff says about the one selected Type. */
export interface TypeApiDiffCueValue {
  /** The diff has a changed-Type row for this Type. */
  readonly found: boolean;
  readonly declarationChanged: boolean;
  readonly memberFingerprints: ReadonlySet<string>;
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

/** The selected Type's API diff, held per Type and baseline. */
export type TypeApiDiffCues = HeldAnalysis<TypeApiDiffCueRequest, TypeApiDiffCueValue>;

export function typeApiDiffCueKey(request: TypeApiDiffCueRequest): string {
  return JSON.stringify([
    "type-api-diff",
    libraryFastDiffKey(request.baseline),
    request.typeQueryIdentifier,
    request.typeIdentifier,
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
    // The analyses of Compare's Type subject: Fast Diff's API axis includes
    // member attributes, so attribute changes must place member cues too.
    query: {
      surface: "Type",
      analyses: ["api", "api-attribute"],
      views: "Changes",
      typeNames: [request.typeQueryIdentifier],
      memberTargetIdentities: [],
    },
  };
}

export function createTypeApiDiffCues(
  dependencies: TypeApiDiffCuesDependencies,
): TypeApiDiffCues {
  return createHeldAnalysis<TypeApiDiffCueRequest, TypeApiDiffCueValue>({
    ...dependencies,
    name: "Member change cues",
    order: "visible-subject",
    key: typeApiDiffCueKey,
    query: (operationId, request) =>
      dependencies.query(operationId, createLibraryApiDiffRequest(operationInput(request))),
    read: (result, request) => {
      validateLibraryApiDiffResult(result, operationInput(request));
      switch (result.kind) {
        case "Succeeded": {
          // The value lists every changed Type in the Library; keep the one
          // selected Type's row.
          const row = result.value?.types.find(type =>
            type.after?.identifier === request.typeIdentifier);
          return {
            kind: "ready",
            value: {
              found: row !== undefined,
              declarationChanged: row?.typeDefinitionChanged === true,
              memberFingerprints: new Set(row?.members.flatMap(member =>
                member.after ? [member.after.fingerprint] : []) ?? []),
            },
          };
        }
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
  change: {
    readonly description: string;
    readonly api: boolean;
    readonly body: boolean;
  },
  cues: HeldAnalysisEntry<TypeApiDiffCueValue> | null,
  escapeHtml: (value: unknown) => string,
): string {
  const ready = cues?.status === "ready" ? cues.value : null;
  const detail = cues?.status === "failed"
    ? `<span class="type-change-detail">Member cues unavailable: ${escapeHtml(cues.error)}</span>
      <button type="button" class="type-change-action" data-type-change-retry>Retry</button>`
    : ready && !ready.found
      ? '<span class="type-change-detail">The complete API diff shows no change for this Type.</span>'
      : ready && ready.memberFingerprints.size === 0 && ready.declarationChanged
        ? '<span class="type-change-detail">The change is in the Type\'s declaration.</span>'
        : "";
  const actions = [
    ...(change.api ? ['<button type="button" class="type-change-action" data-type-change-compare="api">Compare API</button>'] : []),
    ...(change.body ? ['<button type="button" class="type-change-action" data-type-change-compare="member-body">Compare bodies</button>'] : []),
  ].join("\n      ");
  return `<p class="type-change-status" role="status">
      <span class="item-achievement-glyph diff" aria-hidden="true"></span>
      <span class="type-change-text">${escapeHtml(change.description)}</span>${detail}
      ${actions}</p>`;
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
