// Finding Diff Any as a background analysis adopter: one result per exact baseline
// request, held for the page, and the Type navigation cues it drives. Owned by
// docs/design/inspect-web-background-analysis.md.

import type { BackgroundAnalysisQueue } from "./background-analysis.ts";
import {
  createHeldAnalysis,
  type HeldAnalysis,
  type HeldAnalysisEntry,
} from "./held-analysis.ts";
import type {
  BrowserFindingDiffAnyAxes,
  BrowserFindingDiffAnyState,
  BrowserFindingDiffAnyType,
  BrowserLibraryFindingDiffAnyRequest,
} from "./facades/inspect-web-metadata.d.ts";
import type { ItemAchievement } from "./item-achievements.ts";
import type {
  OperationAuthorityPage,
  OperationCancelReason,
  OperationDiagnostic,
  OperationId,
} from "./operation-authority.ts";

const LIBRARY_FAST_DIFF_SCHEMA_VERSION = 2;

export interface LibraryFindingDiffAnyBaseline {
  readonly packageId: string;
  readonly currentVersion: string;
  readonly targetVersion: string;
  readonly targetFramework: string;
  readonly compileAssetId: string;
  readonly axes: BrowserFindingDiffAnyAxes;
}

export type LibraryFindingDiffAnyEntry =
  HeldAnalysisEntry<ReadonlyMap<string, BrowserFindingDiffAnyType>>;

export interface LibraryFindingDiffAnyDependencies {
  readonly queue: BackgroundAnalysisQueue;
  readonly operationAuthority: OperationAuthorityPage;
  query(
    operationId: OperationId,
    request: BrowserLibraryFindingDiffAnyRequest,
  ): Promise<unknown>;
  cancel(operationId: OperationId, reason: OperationCancelReason): void;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  render(): void;
}

export type LibraryFindingDiffAny =
  HeldAnalysis<LibraryFindingDiffAnyBaseline, ReadonlyMap<string, BrowserFindingDiffAnyType>>;

export function libraryFindingDiffAnyKey(baseline: LibraryFindingDiffAnyBaseline): string {
  return JSON.stringify([
    "finding-diff-any",
    baseline.packageId.toLowerCase(),
    baseline.currentVersion,
    baseline.targetVersion,
    baseline.targetFramework,
    baseline.compileAssetId,
    baseline.axes,
  ]);
}

/** Whether a cue reports the axis: `Changed` or `Indeterminate`. */
export function findingDiffAnyAxisReported(state: BrowserFindingDiffAnyState): boolean {
  return state === "Changed" || state === "Indeterminate";
}

/**
 * The navigation cue for one Type against one baseline, or null when every
 * compared axis is unchanged. A Type's cue is the change glyph whichever axes
 * are reported; its description names them.
 */
export function libraryFindingDiffAnyAchievement(
  type: BrowserFindingDiffAnyType | undefined,
  targetVersion: string,
): ItemAchievement | null {
  if (!type) return null;
  const api = findingDiffAnyAxisReported(type.api);
  const body = findingDiffAnyAxisReported(type.body);
  if (!api && !body) return null;
  const axis = (label: string, state: BrowserFindingDiffAnyState) =>
    state === "Indeterminate" ? `${label} undecided` : `${label} changed`;
  const parts = [
    ...(api ? [axis("API", type.api)] : []),
    ...(body ? [axis("implementation", type.body)] : []),
  ];
  const text = parts.join(", ");
  return {
    kind: "diff",
    description: `${text.charAt(0).toUpperCase()}${text.slice(1)} since ${targetVersion}`,
  };
}

function requireRecord(value: unknown, label: string): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value))
    throw new Error(`${label} must be an object.`);
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return value as Record<string, unknown>;
}

const states: ReadonlySet<unknown> =
  new Set(["Unchanged", "Changed", "Indeterminate", "NotCompared"]);

type Outcome =
  | { readonly kind: "ready"; readonly types: BrowserFindingDiffAnyType[] }
  | { readonly kind: "failed"; readonly error: string }
  | { readonly kind: "canceled" };

function readResult(result: unknown, request: BrowserLibraryFindingDiffAnyRequest): Outcome {
  const record = requireRecord(result, "Library Finding Diff Any result");
  if (record.schemaVersion !== LIBRARY_FAST_DIFF_SCHEMA_VERSION)
    throw new Error("Unsupported Library Finding Diff Any result schema.");
  switch (record.kind) {
    case "Succeeded": {
      const echoed = requireRecord(record.request, "Library Finding Diff Any result request");
      if (echoed.packageId !== request.packageId
        || echoed.currentVersion !== request.currentVersion
        || echoed.targetVersion !== request.targetVersion
        || echoed.compileAssetId !== request.compileAssetId
        || echoed.axes !== request.axes) {
        throw new Error("Library Finding Diff Any result does not match its request.");
      }
      const value = requireRecord(record.value, "Library Finding Diff Any value");
      if (!Array.isArray(value.types))
        throw new Error("Library Finding Diff Any Types must be an array.");
      const types = value.types.map((item: unknown) => {
        const type = requireRecord(item, "Library Finding Diff Any Type");
        if (typeof type.identifier !== "string"
          || typeof type.fullName !== "string"
          || !states.has(type.api)
          || !states.has(type.body)) {
          throw new Error("Library Finding Diff Any Type is malformed.");
        }
        // oxlint-disable-next-line typescript/no-unsafe-type-assertion
        return type as unknown as BrowserFindingDiffAnyType;
      });
      return { kind: "ready", types };
    }
    case "Rejected":
    case "Failed":
      return {
        kind: "failed",
        error: typeof record.error === "string" && record.error.length > 0
          ? record.error
          : "Finding Diff Any could not compare the baseline.",
      };
    case "Canceled":
      return { kind: "canceled" };
    default:
      throw new Error("Unknown Library Finding Diff Any result kind.");
  }
}

export function createLibraryFindingDiffAny(
  dependencies: LibraryFindingDiffAnyDependencies,
): LibraryFindingDiffAny {
  return createHeldAnalysis<LibraryFindingDiffAnyBaseline, ReadonlyMap<string, BrowserFindingDiffAnyType>>({
    ...dependencies,
    name: "Finding Diff Any",
    order: "library-baseline",
    key: libraryFindingDiffAnyKey,
    query: (operationId, baseline) => dependencies.query(operationId, {
      schemaVersion: LIBRARY_FAST_DIFF_SCHEMA_VERSION,
      ...baseline,
    }),
    read: (result, baseline) => {
      const outcome = readResult(result, {
        schemaVersion: LIBRARY_FAST_DIFF_SCHEMA_VERSION,
        ...baseline,
      });
      return outcome.kind === "ready"
        ? {
            kind: "ready",
            value: new Map(outcome.types.map(type => [type.identifier, type])),
          }
        : outcome;
    },
  });
}

/** The Type navigation status for a baseline: a visible failure, else none. */
export function renderLibraryFindingDiffAnyStatus(
  entry: LibraryFindingDiffAnyEntry | null,
  targetVersion: string,
  escapeHtml: (value: unknown) => string,
): string {
  if (entry?.status !== "failed") return "";
  return `<p role="alert">Changes since ${escapeHtml(targetVersion)} unavailable: ${escapeHtml(entry.error)}
      <button type="button" data-library-finding-diff-any-retry>Retry</button></p>`;
}

export function bindLibraryFindingDiffAnyRetry(root: ParentNode, retry: () => void) {
  root.querySelectorAll<HTMLButtonElement>("[data-library-finding-diff-any-retry]")
    .forEach(button => button.addEventListener("click", () => retry()));
}
