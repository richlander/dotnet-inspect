// Selected-Type Member states, held by the existing visible-subject queue.
import type { BackgroundAnalysisQueue } from "./background-analysis.ts";
import type { BrowserFindingDiffAnyMember, BrowserFindingDiffAnyState, BrowserTypeFindingDiffAnyRequest } from "./facades/inspect-web-metadata.d.ts";
import { createHeldAnalysis, type HeldAnalysis, type HeldAnalysisEntry } from "./held-analysis.ts";
import { createLibraryFindingDiffAnyRequest, libraryFindingDiffAnyKey, findingDiffAnyAxisReported, type LibraryFindingDiffAnyBaseline } from "./library-finding-diff-any.ts";
import type { ItemAchievement } from "./item-achievements.ts";
import type { OperationAuthorityPage, OperationCancelReason, OperationDiagnostic, OperationId } from "./operation-authority.ts";

export interface TypeFindingDiffAnyCueRequest {
  readonly packageModel: object;
  readonly baseline: LibraryFindingDiffAnyBaseline;
  readonly typeQueryIdentifier: string;
  readonly typeIdentifier: string;
}
export interface TypeFindingDiffAnyCueValue {
  readonly found: boolean;
  readonly residualApi: BrowserFindingDiffAnyState;
  readonly residualBody: BrowserFindingDiffAnyState;
  readonly members: ReadonlyMap<string, BrowserFindingDiffAnyMember>;
}
export interface TypeFindingDiffAnyCuesDependencies {
  readonly queue: BackgroundAnalysisQueue;
  readonly operationAuthority: OperationAuthorityPage;
  query(operationId: OperationId, request: BrowserTypeFindingDiffAnyRequest): Promise<unknown>;
  cancel(operationId: OperationId, reason: OperationCancelReason): void;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  render(): void;
}
export type TypeFindingDiffAnyCues = HeldAnalysis<TypeFindingDiffAnyCueRequest, TypeFindingDiffAnyCueValue>;
export function typeFindingDiffAnyCueKey(request: TypeFindingDiffAnyCueRequest): string {
  return JSON.stringify(["type-finding-diff-any", libraryFindingDiffAnyKey(request.baseline), request.typeIdentifier]);
}
function wireRequest(request: TypeFindingDiffAnyCueRequest): BrowserTypeFindingDiffAnyRequest {
  return { schemaVersion: 1, comparison: createLibraryFindingDiffAnyRequest(request.baseline), typeIdentifier: request.typeIdentifier };
}
function record(value: unknown, label: string): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) throw new Error(`${label} must be an object.`);
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return value as Record<string, unknown>;
}
function state(value: unknown): BrowserFindingDiffAnyState {
  if (value !== "Unchanged" && value !== "Changed" && value !== "Indeterminate") throw new Error("Malformed Type Member state.");
  return value;
}
function fingerprint(value: unknown): string | null {
  if (value === null || typeof value === "string" && value.length > 0) return value;
  throw new Error("Malformed Type Member fingerprint.");
}
export function createTypeFindingDiffAnyCues(dependencies: TypeFindingDiffAnyCuesDependencies): TypeFindingDiffAnyCues {
  return createHeldAnalysis<TypeFindingDiffAnyCueRequest, TypeFindingDiffAnyCueValue>({
    ...dependencies, name: "Member change cues", order: "visible-subject", key: typeFindingDiffAnyCueKey,
    query: (operationId, request) => dependencies.query(operationId, wireRequest(request)),
    read: (input, request) => {
      const result = record(input, "Type Finding Diff Any result"), sent = wireRequest(request);
      const echoed = record(result.request, "Type Finding Diff Any request"), comparison = record(echoed.comparison, "Type comparison");
      if (result.schemaVersion !== 1 || echoed.schemaVersion !== 1 || echoed.typeIdentifier !== sent.typeIdentifier
        || Object.entries(sent.comparison).some(([key, value]) => comparison[key] !== value))
        throw new Error("Type Finding Diff Any result does not match its request.");
      switch (result.kind) {
        case "Succeeded": {
          const value = record(result.value, "Type Finding Diff Any value");
          if (value.typeIdentifier !== sent.typeIdentifier || typeof value.targetAssetId !== "string" || typeof value.currentAssetId !== "string"
            || !Array.isArray(value.members)) throw new Error("Malformed Type Finding Diff Any value.");
          const api = state(value.api), body = state(value.body), residualApi = state(value.residualApi), residualBody = state(value.residualBody);
          const members = new Map<string, BrowserFindingDiffAnyMember>();
          let reportedApi = findingDiffAnyAxisReported(residualApi), reportedBody = findingDiffAnyAxisReported(residualBody);
          for (const item of value.members) {
            const row = record(item, "Type Member");
            const member: BrowserFindingDiffAnyMember = {
              beforeFingerprint: fingerprint(row.beforeFingerprint), afterFingerprint: fingerprint(row.afterFingerprint), api: state(row.api), body: state(row.body),
            };
            reportedApi ||= findingDiffAnyAxisReported(member.api);
            reportedBody ||= findingDiffAnyAxisReported(member.body);
            if (member.beforeFingerprint === null && member.afterFingerprint === null) throw new Error("Type Member has no endpoint fingerprint.");
            if (member.afterFingerprint !== null) {
              if (members.has(member.afterFingerprint)) throw new Error("Duplicate current Member fingerprint.");
              members.set(member.afterFingerprint, member);
            }
          }
          if (findingDiffAnyAxisReported(api) !== reportedApi || findingDiffAnyAxisReported(body) !== reportedBody)
            throw new Error("The Type comparison returned inconsistent Member states.");
          return { kind: "ready", value: { found: findingDiffAnyAxisReported(api) || findingDiffAnyAxisReported(body), residualApi, residualBody, members } };
        }
        case "Canceled": return { kind: "canceled" };
        case "Rejected": case "MissingCounterpart": case "Failed":
          return { kind: "failed", error: typeof result.error === "string" && result.error.length > 0 ? result.error : "The Type's comparison is unavailable." };
        default: throw new Error("Unknown Type Finding Diff Any result kind.");
      }
    },
  });
}

export function memberFindingDiffAnyAchievements(
  value: TypeFindingDiffAnyCueValue,
  group: { readonly overloads: readonly { readonly anchorDigest?: string | null }[] },
  index: number | null, targetVersion: string,
): readonly ItemAchievement[] {
  const overloads = index === null ? group.overloads : group.overloads.slice(index, index + 1);
  const members = overloads.flatMap(overload => {
    const member = overload.anchorDigest ? value.members.get(overload.anchorDigest) : undefined;
    return member ? [member] : [];
  });
  const api = members.some(member => findingDiffAnyAxisReported(member.api)), body = members.some(member => findingDiffAnyAxisReported(member.body));
  if (!api && !body) return [];
  const describe = (axis: "api" | "body", label: string) => `${label} ${members.some(member => member[axis] === "Changed") ? "changed" : "undecided"}`;
  return [{ kind: api && body ? "diff" : api ? "api-diff" : "body-diff",
    description: `${[...(api ? [describe("api", "API")] : []), ...(body ? [describe("body", "implementation")] : [])].join(", ")} since ${targetVersion}` }];
}

export function renderTypeChangeStatus(
  change: { readonly description: string; readonly api: boolean; readonly body: boolean },
  cues: HeldAnalysisEntry<TypeFindingDiffAnyCueValue> | null, escapeHtml: (value: unknown) => string,
): string {
  const ready = cues?.status === "ready" ? cues.value : null;
  const residual = ready && (findingDiffAnyAxisReported(ready.residualApi) || findingDiffAnyAxisReported(ready.residualBody));
  const changedMembers = ready && [...ready.members.values()].some(member => findingDiffAnyAxisReported(member.api) || findingDiffAnyAxisReported(member.body));
  const undecided = ready && (ready.residualApi === "Indeterminate" || ready.residualBody === "Indeterminate"
    || [...ready.members.values()].some(member => member.api === "Indeterminate" || member.body === "Indeterminate"));
  const detail = cues?.status === "failed"
    ? `<span class="type-change-detail">Member cues unavailable: ${escapeHtml(cues.error)}</span><button type="button" class="type-change-action" data-type-change-retry>Retry</button>`
    : ready && !ready.found ? '<span class="type-change-detail">No API or implementation change was found for this Type.</span>'
      : undecided ? '<span class="type-change-detail">Some Member changes could not be decided.</span>'
      : residual && !changedMembers ? '<span class="type-change-detail">Changes are outside the listed Members.</span>' : "";
  const actions = [
    ...(change.api ? ['<button type="button" class="type-change-action" data-type-change-compare="api">Compare API</button>'] : []),
    ...(change.body ? ['<button type="button" class="type-change-action" data-type-change-compare="member-body">Compare bodies</button>'] : []),
  ].join("\n");
  return `<p class="type-change-status" role="status"><span class="item-achievement-glyph diff" aria-hidden="true"></span><span class="type-change-text">${escapeHtml(change.description)}</span>${detail}${actions}</p>`;
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
