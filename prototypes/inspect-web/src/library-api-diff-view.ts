import type {
  BrowserLibraryApiCompatibilityChange,
  BrowserLibraryApiDiff,
  BrowserLibraryApiDiffEndpointIssue,
  BrowserLibraryApiDiffEndpointSummary,
  BrowserLibraryApiMemberDiff,
  BrowserLibraryApiTypeIdentity,
  BrowserLibraryApiTypeSubject,
} from "./facades/inspect-web-metadata.d.ts";
import type { EscapeHtml } from "./csharp-highlighting.ts";
import type { LibraryApiDiffState } from "./library-api-diff.ts";

export interface LibraryApiDiffViewOptions {
  readonly libraryName: string;
  readonly assemblyIdentity: string;
  readonly assetPath: string;
  readonly coordinate: string;
  readonly requireLibrary: boolean;
  readonly state: LibraryApiDiffState;
  readonly escapeHtml: EscapeHtml;
}

export type LibraryApiDiffAction =
  | { readonly kind: "change-target" }
  | { readonly kind: "retry" }
  | { readonly kind: "select-type"; readonly typeId: string };

export interface LibraryApiDiffSelectionSnapshot {
  readonly surfaceScrollTop: number | null;
  readonly scrollTop: number | null;
  readonly focusedTypeId: string | null;
}

export interface LibraryApiDiffSelectionRestoreOptions {
  readonly restoreFocus?: boolean;
}

export function captureLibraryApiDiffSelection(
  root: ParentNode,
): LibraryApiDiffSelectionSnapshot {
  const surface = root.querySelector<HTMLElement>(".library-api-diff-scroll");
  const list = root.querySelector<HTMLElement>(".library-api-diff-list");
  const focused = list?.querySelector<HTMLElement>(
    "[data-library-api-diff-type]:focus");
  return {
    surfaceScrollTop: surface?.scrollTop ?? null,
    scrollTop: list?.scrollTop ?? null,
    focusedTypeId: focused?.dataset.libraryApiDiffType ?? null,
  };
}

export function restoreLibraryApiDiffSelection(
  root: ParentNode,
  snapshot: LibraryApiDiffSelectionSnapshot,
  options: LibraryApiDiffSelectionRestoreOptions = {},
): void {
  const surface = root.querySelector<HTMLElement>(".library-api-diff-scroll");
  const list = root.querySelector<HTMLElement>(".library-api-diff-list");
  if (!list) return;
  if (snapshot.surfaceScrollTop !== null && surface) {
    surface.scrollTop = snapshot.surfaceScrollTop;
  }
  if (snapshot.scrollTop !== null) list.scrollTop = snapshot.scrollTop;
  if (options.restoreFocus === false || !snapshot.focusedTypeId) return;
  const focused = [...list.querySelectorAll<HTMLElement>(
    "[data-library-api-diff-type]"    )].find(
        item => item.dataset.libraryApiDiffType === snapshot.focusedTypeId);
    focused?.focus({ preventScroll: true });
}

function issueDescription(
  issue: BrowserLibraryApiDiffEndpointIssue,
  escapeHtml: EscapeHtml,
): string {
  switch (issue.kind) {
    case "Truncated":
      return `Truncated at ${escapeHtml(String(issue.truncation?.bound ?? ""))} ${escapeHtml(issue.truncation?.limit ?? "")}.`;
    case "Rejected":
      return `Rejected${issue.openFailureKind ? ` (${escapeHtml(issue.openFailureKind)})` : ""}${issue.detail ? `: ${escapeHtml(issue.detail)}` : "."}`;
    case "Failed":
      return `Failed${issue.detail ? `: ${escapeHtml(issue.detail)}` : "."}`;
    case "InspectionFailures":
      return `${escapeHtml(String(issue.count ?? 0))} inspection failure${issue.count === 1 ? "" : "s"}.`;
    case "DegradedSignatures":
      return `${escapeHtml(String(issue.count ?? 0))} degraded signature${issue.count === 1 ? "" : "s"}.`;
    case "UnexpectedAssemblyPopulation":
      return `${escapeHtml(String(issue.count ?? 0))} unexpected assembly population issue${issue.count === 1 ? "" : "s"}.`;
    default:
      return escapeHtml(issue.kind);
  }
}

function endpointSummaryHtml(
  label: "Before" | "After",
  summary: BrowserLibraryApiDiffEndpointSummary,
  escapeHtml: EscapeHtml,
): string {
  const identity = summary.identity;
  const identityDisplay = [
    identity.name,
    identity.version ? `Version=${identity.version}` : null,
    `Culture=${identity.culture ?? "neutral"}`,
    `PublicKeyToken=${identity.publicKeyToken ?? "null"}`,
  ].filter(value => value !== null).join(", ");
  const issues = summary.issues.length
    ? `<ul class="library-api-diff-issue-list">${summary.issues.map(issue =>
        `<li data-library-api-diff-issue="${escapeHtml(issue.kind)}">${issueDescription(issue, escapeHtml)}</li>`).join("")}</ul>`
    : "";
  return `<div class="library-api-diff-endpoint" data-library-api-diff-endpoint="${label.toLowerCase()}">
    <p class="library-api-diff-endpoint-head">
      <strong>${label}</strong>
      <span>${escapeHtml(identityDisplay)}</span>
      <span data-library-api-diff-endpoint-complete="${summary.isComplete}">${summary.isComplete ? "Complete" : "Incomplete"}</span>
      <span>${escapeHtml(summary.scope)}</span>
    </p>
    ${issues}
  </div>`;
}

function typeIdentityLabel(identity: BrowserLibraryApiTypeIdentity | null): string {
  return identity?.display ?? "";
}

function aggregateSummaryHtml(
  summary: BrowserLibraryApiDiff["summary"],
  escapeHtml: EscapeHtml,
): string {
  if (!summary) return "";
  const cell = (label: string, key: string, value: number) =>
    `<div class="library-api-diff-count" data-library-api-diff-count="${key}"><dt>${escapeHtml(label)}</dt><dd>${value.toLocaleString()}</dd></div>`;
  return `<dl class="library-api-diff-summary">
    ${cell("Changed types", "changed-types", summary.changedTypeCount)}
    ${cell("Added types", "added-types", summary.addedTypeCount)}
    ${cell("Removed types", "removed-types", summary.removedTypeCount)}
    ${cell("Changed members", "changed-members", summary.changedMemberCount)}
    ${cell("Breaking", "breaking", summary.breakingCount)}
    ${cell("Additive", "additive", summary.additiveCount)}
    ${cell("Potentially breaking", "potentially-breaking", summary.potentiallyBreakingCount)}
  </dl>`;
}

function typeRowHtml(
  subject: BrowserLibraryApiTypeSubject,
  selected: boolean,
  escapeHtml: EscapeHtml,
): string {
  const diff = subject.typeDiff;
  return `<button type="button" class="library-api-diff-type-row${selected ? " selected" : ""}"
      data-library-api-diff-type="${escapeHtml(subject.identifier)}"
      aria-current="${selected}">
    <span class="library-api-diff-type-head">
      <span data-library-api-diff-type-change="${escapeHtml(subject.change)}">${escapeHtml(subject.change)}</span>
      <span class="library-api-diff-type-name">${escapeHtml(subject.display)}</span>
    </span>
    <span class="library-api-diff-type-metrics">
      <span title="Changed members">${diff.changedMemberCount}</span>
      <span title="Breaking" data-library-api-diff-metric="breaking">${diff.breakingCount}</span>
      <span title="Additive" data-library-api-diff-metric="additive">${diff.additiveCount}</span>
      <span title="Potentially breaking" data-library-api-diff-metric="potentially-breaking">${diff.potentiallyBreakingCount}</span>
    </span>
  </button>`;
}

function compatibilityChangeHtml(
  change: BrowserLibraryApiCompatibilityChange,
  escapeHtml: EscapeHtml,
): string {
  const subject = change.subject;
  const subjectLabel = subject.kind === "Type"
    ? typeIdentityLabel(subject.afterType ?? subject.beforeType)
    : (subject.afterMember ?? subject.beforeMember)?.display ?? "";
  return `<li class="library-api-diff-change-row" data-library-api-diff-change="${escapeHtml(change.kind)}"
      data-library-api-diff-classification="${escapeHtml(change.classification)}">
    <p class="library-api-diff-change-head">
      <span data-library-api-diff-classification-badge="${escapeHtml(change.classification)}">${escapeHtml(change.classification)}</span>
      <span>${escapeHtml(change.kind)}</span>
      <span class="library-api-diff-change-subject">${escapeHtml(subjectLabel)}</span>
    </p>
    <p class="library-api-diff-change-message">${escapeHtml(change.message)}</p>
    ${change.oldValue !== null || change.newValue !== null
      ? `<p class="library-api-diff-change-values">
          ${change.oldValue !== null ? `<code data-library-api-diff-change-old>${escapeHtml(change.oldValue)}</code>` : ""}
          ${change.newValue !== null ? `<code data-library-api-diff-change-new>${escapeHtml(change.newValue)}</code>` : ""}
        </p>`
      : ""}
  </li>`;
}

function memberRelationHtml(
  memberDiff: BrowserLibraryApiMemberDiff,
  escapeHtml: EscapeHtml,
): string {
  const relation = memberDiff.relation;
  const display = (relation.after ?? relation.before)?.display ?? relation.identifier;
  const match = relation.match;
  const endpoint = (
    label: "Before" | "After",
    identity: typeof relation.before,
  ) => identity
    ? `<div class="library-api-diff-member-endpoint"
        data-library-api-diff-member-endpoint="${label.toLowerCase()}"
        data-library-api-diff-member-selector="${escapeHtml(identity.anchor.stableSelector)}"
        data-library-api-diff-member-fingerprint="${escapeHtml(identity.anchor.fingerprint)}">
        <strong>${label}</strong>
        <span>${escapeHtml(identity.declaringType.display)}</span>
        <code>${escapeHtml(identity.anchor.canonicalSignature)}</code>
      </div>`
    : "";
  return `<li class="library-api-diff-member-row"
      data-library-api-diff-member="${escapeHtml(relation.identifier)}"
      data-library-api-diff-member-pair-kind="${escapeHtml(relation.pairKind)}"
      data-library-api-diff-member-role="${escapeHtml(memberDiff.role)}">
    <span class="library-api-diff-member-head">
      <span data-library-api-diff-member-pair-kind-badge="${escapeHtml(relation.pairKind)}">${escapeHtml(relation.pairKind)}</span>
      <span class="library-api-diff-member-name">${escapeHtml(display)}</span>
      <span class="library-api-diff-member-role" data-library-api-diff-member-role-badge="${escapeHtml(memberDiff.role)}">${escapeHtml(memberDiff.role)}</span>
    </span>
    <div class="library-api-diff-member-endpoints">
      ${endpoint("Before", relation.before)}
      ${endpoint("After", relation.after)}
    </div>
    ${match
      ? `<span class="library-api-diff-member-match" title="Match provenance">${escapeHtml(match.tierId)} · tier ${match.tierConfidence}% · match ${match.confidence}%</span>`
      : ""}
  </li>`;
}

function typeDetailHtml(
  subject: BrowserLibraryApiTypeSubject | undefined,
  escapeHtml: EscapeHtml,
): string {
  if (!subject) {
    return `<div class="library-api-diff-detail-empty">Select a changed Type to see its compatibility changes and member relations.</div>`;
  }
  const diff = subject.typeDiff;
  const identityRow = (label: string, identity: BrowserLibraryApiTypeIdentity | null) =>
    identity
      ? `<div><dt>${escapeHtml(label)}</dt><dd>${escapeHtml(identity.display)}</dd></div>`
      : "";
  const definitionState = diff.typeDefinitionChanged === null
    ? "Type definition is present on one side"
    : diff.typeDefinitionChanged
      ? "Type definition changed"
      : "Type definition unchanged";
  return `<div class="library-api-diff-detail" data-library-api-diff-detail="${escapeHtml(subject.identifier)}">
    <header class="library-api-diff-detail-head">
      <h2>${escapeHtml(subject.display)}</h2>
      <p>
        <span data-library-api-diff-type-pair-kind="${escapeHtml(diff.pairKind)}">${escapeHtml(diff.pairKind)}</span>
        <span data-library-api-diff-type-definition-changed="${diff.typeDefinitionChanged ?? "unpaired"}">${definitionState}</span>
      </p>
    </header>
    <dl class="library-api-diff-detail-identity">
      ${identityRow("Before", diff.before)}
      ${identityRow("After", diff.after)}
    </dl>
    <section aria-label="Compatibility changes">
      <h3>Compatibility changes (${diff.compatibilityChanges.length})</h3>
      ${diff.compatibilityChanges.length
        ? `<ul class="library-api-diff-change-list">${diff.compatibilityChanges.map(change => compatibilityChangeHtml(change, escapeHtml)).join("")}</ul>`
        : `<p class="library-api-diff-detail-empty">No compatibility changes on this Type.</p>`}
    </section>
    <section aria-label="Member relations">
      <h3>Member relations (${diff.members.length})</h3>
      ${diff.members.length
        ? `<ul class="library-api-diff-member-list">${diff.members.map(member => memberRelationHtml(member, escapeHtml)).join("")}</ul>`
        : `<p class="library-api-diff-detail-empty">No changed, added, or removed members on this Type.</p>`}
    </section>
  </div>`;
}

function resultBodyHtml(
  result: BrowserLibraryApiDiff,
  selectedTypeId: string,
  escapeHtml: EscapeHtml,
): string {
  const endpoints = `<div class="library-api-diff-endpoints">
    ${endpointSummaryHtml("Before", result.before, escapeHtml)}
    ${endpointSummaryHtml("After", result.after, escapeHtml)}
  </div>`;
  if (result.kind === "Rejected") {
    return `<div class="library-api-diff-state" data-library-api-diff-state="rejected" role="alert">
      ${endpoints}
      <p class="library-api-diff-detail-empty">This comparison was rejected: ${escapeHtml(result.rejectionKind ?? "")}.</p>
    </div>`;
  }
  if (result.kind === "Unavailable") {
    return `<div class="library-api-diff-state" data-library-api-diff-state="unavailable" role="alert">
      ${endpoints}
      <p class="library-api-diff-detail-empty">Not compared: ${escapeHtml(result.unavailableKind ?? "")}.</p>
    </div>`;
  }
  const document = result.document;
  const subjects = document?.subjects ?? [];
  if (subjects.length === 0) {
    return `<div class="library-api-diff-state" data-library-api-diff-state="available">
      ${aggregateSummaryHtml(result.summary, escapeHtml)}
      ${endpoints}
      <p class="library-api-diff-detail-empty" data-library-api-diff-empty>No public API changes.</p>
    </div>`;
  }
  const effectiveSelectedId = subjects.some(subject => subject.identifier === selectedTypeId)
    ? selectedTypeId
    : (subjects[0]?.identifier ?? "");
  const selectedSubject = subjects.find(subject => subject.identifier === effectiveSelectedId);
  return `<div class="library-api-diff-state" data-library-api-diff-state="available">
    ${aggregateSummaryHtml(result.summary, escapeHtml)}
    ${endpoints}
    <div class="library-api-diff-body">
      <nav class="library-api-diff-list" aria-label="Changed Types">
        ${subjects.map(subject => typeRowHtml(subject, subject.identifier === effectiveSelectedId, escapeHtml)).join("")}
      </nav>
      ${typeDetailHtml(selectedSubject, escapeHtml)}
    </div>
  </div>`;
}

export function renderLibraryApiDiffSurface(options: LibraryApiDiffViewOptions): string {
  const { libraryName, assemblyIdentity, assetPath, coordinate, requireLibrary, state, escapeHtml } = options;
  const request = state.request;
  const changeTargetButton = requireLibrary
    ? ""
    : `<button type="button" data-library-api-diff-action="change-target">Change target</button>`;
  let status: string;
  let body: string;
  if (requireLibrary) {
    status = "Select a library";
    body = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Pick a library to compare</h2><p>Choose a .NET library above to compare its public API against the Package's selected comparison version.</p></section>`;
  } else if (state.noRequestReason) {
    status = "No comparison target";
    body = `<div class="library-api-diff-state" data-library-api-diff-state="no-request" role="status">
      <p class="library-api-diff-detail-empty">${escapeHtml(state.noRequestReason)}</p>
    </div>`;
  } else if (state.loading) {
    status = "Comparing\u2026";
    body = `<div class="library-api-diff-state" data-library-api-diff-state="loading"><span class="loader"></span><p>Comparing the public API surface\u2026</p></div>`;
  } else if (state.error) {
    status = "Failed";
    body = `<div class="library-api-diff-state" data-library-api-diff-state="failed" role="alert">
      <p class="library-api-diff-detail-empty">${escapeHtml(state.error)}</p>
      <button type="button" data-library-api-diff-action="retry">Retry</button>
    </div>`;
  } else if (state.canceledReason) {
    status = "Canceled";
    body = `<div class="library-api-diff-state" data-library-api-diff-state="canceled" role="status">
      <p class="library-api-diff-detail-empty">Library API diff canceled (${escapeHtml(state.canceledReason)}).</p>
      <button type="button" data-library-api-diff-action="retry">Retry</button>
    </div>`;
  } else if (!state.result) {
    status = "Loading\u2026";
    body = `<div class="library-api-diff-state" data-library-api-diff-state="loading"><span class="loader"></span></div>`;
  } else {
    status = "Compared";
    body = resultBodyHtml(state.result, state.selectedTypeId, escapeHtml);
  }
  const versionLabel = request
    ? `Public API: target ${escapeHtml(request.comparisonVersion)} &rarr; current ${escapeHtml(request.version)}`
    : "";
  const identity = assetPath ? `${assetPath} \u00b7 ${assemblyIdentity}` : assemblyIdentity;
  return `<section class="library-api-diff-surface" aria-labelledby="library-api-diff-title">
    <header class="api-surface-head">
      <h1 id="library-api-diff-title">Diff</h1>
      <div class="library-api-diff-head-meta">
        ${versionLabel ? `<p>${versionLabel}</p>` : ""}
        ${changeTargetButton}
      </div>
    </header>
    <div class="library-api-diff-scroll">${body}</div>
    <footer class="metadata-surface-footer">
      <span title="${escapeHtml(libraryName ? identity : status)}">${escapeHtml(libraryName ? identity : status)}</span>
      <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
    </footer>
  </section>`;
}

export function bindLibraryApiDiff(
  root: ParentNode,
  actions: { readonly onAction: (action: LibraryApiDiffAction) => void },
): void {
  root.querySelector('[data-library-api-diff-action="change-target"]')
    ?.addEventListener("click", () => actions.onAction({ kind: "change-target" }));
  root.querySelector('[data-library-api-diff-action="retry"]')
    ?.addEventListener("click", () => actions.onAction({ kind: "retry" }));
  root.querySelectorAll<HTMLElement>("[data-library-api-diff-type]").forEach(button => {
    button.addEventListener("click", () => {
      const typeId = button.dataset.libraryApiDiffType;
      if (typeId) actions.onAction({ kind: "select-type", typeId });
    });
  });
}
