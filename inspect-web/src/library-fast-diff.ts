// Fast Diff as a background analysis adopter: one result per exact baseline
// request, held for the page, and the Type navigation cues it drives. Owned by
// docs/design/inspect-web-background-analysis.md.

import type {
  BackgroundAnalysisQueue,
  BackgroundAnalysisRun,
} from "./background-analysis.ts";
import type {
  BrowserFastDiffAxes,
  BrowserFastDiffState,
  BrowserFastDiffType,
  BrowserLibraryFastDiffRequest,
} from "./facades/inspect-web-metadata.d.ts";
import type { ItemAchievement } from "./item-achievements.ts";
import type {
  OperationAuthorityPage,
  OperationCancelReason,
  OperationDiagnostic,
  OperationId,
  OperationProducerAdapter,
} from "./operation-authority.ts";

export const LIBRARY_FAST_DIFF_SCHEMA_VERSION = 2;

export interface LibraryFastDiffBaseline {
  readonly packageId: string;
  readonly currentVersion: string;
  readonly targetVersion: string;
  readonly targetFramework: string;
  readonly compileAssetId: string;
  readonly axes: BrowserFastDiffAxes;
}

export type LibraryFastDiffEntry =
  | { readonly status: "loading" }
  | {
      readonly status: "ready";
      readonly types: ReadonlyMap<string, BrowserFastDiffType>;
    }
  | { readonly status: "failed"; readonly error: string };

export interface LibraryFastDiffDependencies {
  readonly queue: BackgroundAnalysisQueue;
  readonly operationAuthority: OperationAuthorityPage;
  query(
    operationId: OperationId,
    request: BrowserLibraryFastDiffRequest,
  ): Promise<unknown>;
  cancel(operationId: OperationId, reason: OperationCancelReason): void;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  render(): void;
}

export interface LibraryFastDiff {
  /** Queues the baseline unless its result is held or in flight. */
  ensure(baseline: LibraryFastDiffBaseline, isRelevant: () => boolean): void;
  retry(baseline: LibraryFastDiffBaseline, isRelevant: () => boolean): void;
  entry(baseline: LibraryFastDiffBaseline): LibraryFastDiffEntry | null;
}

export function libraryFastDiffKey(baseline: LibraryFastDiffBaseline): string {
  return JSON.stringify([
    "fast-diff",
    baseline.packageId.toLowerCase(),
    baseline.currentVersion,
    baseline.targetVersion,
    baseline.targetFramework,
    baseline.compileAssetId,
    baseline.axes,
  ]);
}

function isReported(state: BrowserFastDiffState): boolean {
  return state === "Changed" || state === "Indeterminate";
}

/**
 * The navigation cue for one Type against one baseline, or null when every
 * compared axis is unchanged.
 */
export function libraryFastDiffAchievement(
  type: BrowserFastDiffType | undefined,
  targetVersion: string,
): ItemAchievement | null {
  if (!type) return null;
  const api = isReported(type.api);
  const body = isReported(type.body);
  if (!api && !body) return null;
  const axis = (label: string, state: BrowserFastDiffState) =>
    state === "Indeterminate" ? `${label} undecided` : `${label} changed`;
  const parts = [
    ...(api ? [axis("API", type.api)] : []),
    ...(body ? [axis("implementation", type.body)] : []),
  ];
  const text = parts.join(", ");
  return {
    kind: api ? "api-diff" : "body-diff",
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
  | { readonly kind: "ready"; readonly types: BrowserFastDiffType[] }
  | { readonly kind: "failed"; readonly error: string }
  | { readonly kind: "canceled" };

function readResult(result: unknown, request: BrowserLibraryFastDiffRequest): Outcome {
  const record = requireRecord(result, "Library Fast Diff result");
  if (record.schemaVersion !== LIBRARY_FAST_DIFF_SCHEMA_VERSION)
    throw new Error("Unsupported Library Fast Diff result schema.");
  switch (record.kind) {
    case "Succeeded": {
      const echoed = requireRecord(record.request, "Library Fast Diff result request");
      if (echoed.packageId !== request.packageId
        || echoed.currentVersion !== request.currentVersion
        || echoed.targetVersion !== request.targetVersion
        || echoed.compileAssetId !== request.compileAssetId
        || echoed.axes !== request.axes) {
        throw new Error("Library Fast Diff result does not match its request.");
      }
      const value = requireRecord(record.value, "Library Fast Diff value");
      if (!Array.isArray(value.types))
        throw new Error("Library Fast Diff Types must be an array.");
      const types = value.types.map((item: unknown) => {
        const type = requireRecord(item, "Library Fast Diff Type");
        if (typeof type.identifier !== "string"
          || typeof type.fullName !== "string"
          || !states.has(type.api)
          || !states.has(type.body)) {
          throw new Error("Library Fast Diff Type is malformed.");
        }
        // oxlint-disable-next-line typescript/no-unsafe-type-assertion
        return type as unknown as BrowserFastDiffType;
      });
      return { kind: "ready", types };
    }
    case "Rejected":
    case "Failed":
      return {
        kind: "failed",
        error: typeof record.error === "string" && record.error.length > 0
          ? record.error
          : "Fast Diff could not compare the baseline.",
      };
    case "Canceled":
      return { kind: "canceled" };
    default:
      throw new Error("Unknown Library Fast Diff result kind.");
  }
}

export function createLibraryFastDiff(
  dependencies: LibraryFastDiffDependencies,
): LibraryFastDiff {
  const entries = new Map<string, LibraryFastDiffEntry>();
  const session = dependencies.operationAuthority.createSession<
    BrowserLibraryFastDiffRequest,
    Outcome,
    unknown,
    never,
    never
  >({
    feature: { publish: () => undefined },
    diagnostic: {
      report: diagnostic => dependencies.reportOperationDiagnostic(diagnostic),
    },
  });
  const adapter: OperationProducerAdapter<
    BrowserLibraryFastDiffRequest,
    Outcome,
    unknown,
    never,
    never
  > = {
    prepare: (identity, request, sink) => {
      let cancellationRequested = false;
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
            const finish = (outcome: Outcome | null, error?: unknown): undefined => {
              if (outcome) sink.reportTerminal({ kind: "succeeded", value: outcome });
              else sink.reportUnexpectedTerminal(error, error);
              sink.reportQuiesced();
              return undefined;
            };
            let query: Promise<unknown>;
            try {
              query = dependencies.query(identity.id, request);
            } catch (error: unknown) {
              return finish(null, error);
            }
            void query.then(
              result => {
                let outcome: Outcome;
                try {
                  outcome = readResult(result, request);
                } catch (error: unknown) {
                  return finish(null, error);
                }
                return finish(outcome);
              },
              (error: unknown) => finish(null, error));
            return undefined;
          },
          abandon: () => undefined,
        },
      };
    },
  };

  const run = (baseline: LibraryFastDiffBaseline, key: string): BackgroundAnalysisRun => {
    entries.set(key, { status: "loading" });
    const started = session.start({
      schemaVersion: LIBRARY_FAST_DIFF_SCHEMA_VERSION,
      ...baseline,
    }, adapter);
    if (started.kind === "rejected") {
      entries.set(key, {
        status: "failed",
        error: `Fast Diff could not start: ${started.reason.kind}.`,
      });
      dependencies.render();
      return { settled: Promise.resolve(), cancel: () => undefined };
    }
    const record = (outcome: Awaited<typeof started.handle.outcome>) => {
      if (outcome.kind === "succeeded" && outcome.value.kind === "ready") {
        entries.set(key, {
          status: "ready",
          types: new Map(outcome.value.types.map(type => [type.identifier, type])),
        });
      } else if (outcome.kind === "succeeded" && outcome.value.kind === "failed") {
        entries.set(key, { status: "failed", error: outcome.value.error });
      } else if (outcome.kind === "succeeded" || outcome.kind === "canceled") {
        // A canceled baseline is not held, so a later visit queues it again.
        entries.delete(key);
      } else {
        entries.set(key, {
          status: "failed",
          error: dependencies.describeError(outcome.error),
        });
      }
      dependencies.render();
    };
    return {
      settled: (async () => {
        record(await started.handle.outcome);
        await started.handle.quiesced;
      })(),
      cancel: () => {
        // A render inside another feature's publication cannot cancel; the
        // publication ends before the next microtask.
        if (started.handle.cancel("superseded").kind === "rejected")
          queueMicrotask(() => { started.handle.cancel("superseded"); });
      },
    };
  };

  const ensure = (baseline: LibraryFastDiffBaseline, isRelevant: () => boolean) => {
    const key = libraryFastDiffKey(baseline);
    if (entries.has(key)) return;
    dependencies.queue.enqueue({
      key,
      order: "library-baseline",
      isRelevant,
      start: () => run(baseline, key),
    });
  };

  return {
    ensure,
    retry(baseline, isRelevant) {
      const key = libraryFastDiffKey(baseline);
      if (entries.get(key)?.status === "failed") entries.delete(key);
      ensure(baseline, isRelevant);
    },
    entry: baseline => entries.get(libraryFastDiffKey(baseline)) ?? null,
  };
}

/** The Type navigation status for a baseline: a visible failure, else none. */
export function renderLibraryFastDiffStatus(
  entry: LibraryFastDiffEntry | null,
  targetVersion: string,
  escapeHtml: (value: unknown) => string,
): string {
  if (entry?.status !== "failed") return "";
  return `<p role="alert">Changes since ${escapeHtml(targetVersion)} unavailable: ${escapeHtml(entry.error)}
      <button type="button" data-library-fast-diff-retry>Retry</button></p>`;
}

export function bindLibraryFastDiffRetry(root: ParentNode, retry: () => void) {
  root.querySelectorAll<HTMLButtonElement>("[data-library-fast-diff-retry]")
    .forEach(button => button.addEventListener("click", () => retry()));
}
