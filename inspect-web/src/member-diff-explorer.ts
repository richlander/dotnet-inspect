import type {
  BrowserLibraryApiDiffMemberExploreEndpoint,
  BrowserLibraryApiDiffMemberIdentity,
} from "./facades/inspect-web-metadata.d.ts";
import {
  libraryApiDiffMemberStateLabel,
  renderLibraryApiDiffChangeChips,
  renderLibraryApiDiffMemberChanges,
  type LibraryApiDiffMemberExploreContext,
} from "./library-api-diff.ts";
import type {
  OperationAuthorityPage,
  OperationCancelReason,
  OperationDiagnostic,
  OperationFeatureEvent,
  OperationId,
  OperationProducerAdapter,
  OperationSession,
} from "./operation-authority.ts";
import { safeExternalHref } from "./package-changes-view.ts";
import { trapModalTab } from "./shell-controls.ts";
import type {
  BrowserSourceComparison,
  BrowserSourceComparisonEndpoint,
  BrowserSourceComparisonEndpointRequest,
  BrowserSourceComparisonRequest,
  BrowserSourceComparisonResult,
  BrowserSourceDiff,
  BrowserSourceDiffChange,
  BrowserSourceDiffSpan,
} from "./source-diff-transport.ts";

export type MemberDiffExplorerSourceState =
  | { readonly status: "idle" }
  | { readonly status: "loading" }
  | {
      readonly status: "ready";
      readonly result: BrowserSourceComparisonResult;
    }
  | {
      readonly status: "failed";
      readonly error: string;
    }
  | {
      readonly status: "canceled";
      readonly reason: string;
    };

interface MemberDiffExplorerOperationInput {
  readonly context: LibraryApiDiffMemberExploreContext;
  readonly request: BrowserSourceComparisonRequest;
}

type FeatureEvent = OperationFeatureEvent<
  BrowserSourceComparisonResult,
  unknown,
  never
>;

type Session = OperationSession<
  MemberDiffExplorerOperationInput,
  BrowserSourceComparisonResult,
  unknown,
  never,
  never
>;

export interface MemberDiffExplorerDependencies {
  readonly document: Document;
  readonly operationAuthority: OperationAuthorityPage;
  readonly query: (
    operationId: OperationId,
    request: BrowserSourceComparisonRequest,
  ) => Promise<BrowserSourceComparisonResult>;
  readonly cancel: (
    operationId: OperationId,
    reason: OperationCancelReason,
  ) => void;
  readonly describeError: (error: unknown) => string;
  readonly escapeHtml: (value: unknown) => string;
  readonly reportOperationDiagnostic: (
    diagnostic: OperationDiagnostic,
  ) => void;
}

export interface MemberDiffExplorerController {
  readonly isOpen: boolean;
  open(
    context: LibraryApiDiffMemberExploreContext,
    invoker: HTMLElement,
  ): void;
  reconcile(
    context: LibraryApiDiffMemberExploreContext | null,
  ): boolean;
  afterRender(fallback: HTMLElement | null): void;
  dispose(): void;
}

interface RetainedSource {
  readonly context: LibraryApiDiffMemberExploreContext;
  readonly result: BrowserSourceComparisonResult;
}

function sameContext(
  left: LibraryApiDiffMemberExploreContext,
  right: LibraryApiDiffMemberExploreContext,
): boolean {
  return left.packageModel === right.packageModel
    && left.result === right.result
    && left.destination === right.destination
    && left.member === right.member;
}

function memberRequest(
  member: BrowserLibraryApiDiffMemberIdentity | null,
): BrowserSourceComparisonEndpointRequest | null {
  if (member === null) return null;
  return {
    typeIdentity: member.declaringTypeIdentifier,
    stableSelector: member.stableSelector,
    canonicalSignature: member.canonicalSignature,
    fingerprint: member.fingerprint,
    typeFullName: member.typeFullName,
    memberName: member.memberName,
  };
}

export function memberDiffSourceRequest(
  context: LibraryApiDiffMemberExploreContext,
): BrowserSourceComparisonRequest {
  const { target, current } = context.destination;
  if (target.packageId !== current.packageId
    || target.framework !== current.framework
    || target.asset.id !== current.asset.id
    || target.assembly.name !== current.assembly.name) {
    throw new Error(
      "Member Diff Explore destination endpoints do not identify one comparable asset.",
    );
  }
  return {
    packageId: target.packageId,
    beforeVersion: target.version,
    afterVersion: current.version,
    framework: target.framework,
    assembly: target.asset.id,
    before: memberRequest(target.member),
    after: memberRequest(current.member),
  };
}

function sameEndpointRequest(
  left: BrowserSourceComparisonEndpointRequest | null,
  right: BrowserSourceComparisonEndpointRequest | null,
): boolean {
  return left === right
    || (left !== null
      && right !== null
      && left.typeIdentity === right.typeIdentity
      && left.stableSelector === right.stableSelector
      && left.canonicalSignature === right.canonicalSignature
      && left.fingerprint === right.fingerprint
      && left.typeFullName === right.typeFullName
      && left.memberName === right.memberName);
}

function sameRequest(
  left: BrowserSourceComparisonRequest,
  right: BrowserSourceComparisonRequest,
): boolean {
  return left.packageId === right.packageId
    && left.beforeVersion === right.beforeVersion
    && left.afterVersion === right.afterVersion
    && left.framework === right.framework
    && left.assembly === right.assembly
    && sameEndpointRequest(left.before, right.before)
    && sameEndpointRequest(left.after, right.after);
}

function validateResult(
  result: BrowserSourceComparisonResult,
  request: BrowserSourceComparisonRequest,
): void {
  if (result.kind !== "Succeeded") return;
  if (result.value === null || !sameRequest(result.value.request, request)) {
    throw new Error(
      "Authored Source returned a result for a different Member comparison.",
    );
  }
  if ((request.before === null) !== (result.value.before.state === "Unrequested")
    || (request.after === null)
      !== (result.value.after.state === "Unrequested")) {
    throw new Error(
      "Authored Source endpoint state does not match the requested sides.",
    );
  }
}

function attributeText(
  value: unknown,
  escapeHtml: (value: unknown) => string,
): string {
  return escapeHtml(value).replaceAll("\r", "&#13;");
}

function endpointProvenance(
  endpoint: BrowserSourceComparisonEndpoint,
  escapeHtml: (value: unknown) => string,
): string {
  const parts = [
    endpoint.assetPath,
    endpoint.repositoryUrl,
    endpoint.revision,
  ].filter((part): part is string => part !== null && part !== "");
  const browseUrl = endpoint.browseUrl === null
    ? null
    : safeExternalHref(endpoint.browseUrl);
  const browse = browseUrl === null
    ? ""
    : ` · <a href="${attributeText(browseUrl, escapeHtml)}" target="_blank" rel="noopener noreferrer">Open source</a>`;
  return parts.length === 0 && browse === ""
    ? ""
    : `<p class="member-diff-source-provenance">${parts.map(escapeHtml).join(" · ")}${browse}</p>`;
}

function endpointStatus(
  endpoint: BrowserSourceComparisonEndpoint,
  destination: BrowserLibraryApiDiffMemberExploreEndpoint,
  label: string,
  escapeHtml: (value: unknown) => string,
): string {
  if (destination.member === null) {
    return `<section class="member-diff-source-endpoint">
      <h3>${escapeHtml(label)}</h3>
      <p>Not present on this side.</p>
    </section>`;
  }
  const text = endpoint.text === null
    ? ""
    : `<pre class="member-diff-source-text"><code>${escapeHtml(endpoint.text)}</code></pre>`;
  const detail = endpoint.detail === null
    ? ""
    : `<p>${escapeHtml(endpoint.detail)}</p>`;
  return `<section class="member-diff-source-endpoint">
    <h3>${escapeHtml(label)}</h3>
    <p><strong>${escapeHtml(endpoint.state)}</strong> · ${escapeHtml(endpoint.version)}</p>
    ${detail}
    ${endpointProvenance(endpoint, escapeHtml)}
    ${text}
  </section>`;
}

function changedSpans(
  changes: readonly BrowserSourceDiffChange[],
  side: "before" | "after",
  line: number,
): readonly BrowserSourceDiffSpan[] {
  return changes.flatMap(change => change.innerMappings
    .map(mapping => mapping[side])
    .filter(span => span.line === line))
    .sort((left, right) => left.start - right.start);
}

function highlightedLine(
  text: string,
  spans: readonly BrowserSourceDiffSpan[],
  escapeHtml: (value: unknown) => string,
): string {
  if (spans.length === 0) return escapeHtml(text);
  const parts: string[] = [];
  let cursor = 0;
  for (const span of spans) {
    if (span.start < cursor || span.start + span.count > text.length) continue;
    parts.push(escapeHtml(text.slice(cursor, span.start)));
    parts.push(`<mark>${escapeHtml(
      text.slice(span.start, span.start + span.count),
    )}</mark>`);
    cursor = span.start + span.count;
  }
  parts.push(escapeHtml(text.slice(cursor)));
  return parts.join("");
}

function diffLine(
  side: "before" | "after",
  index: number,
  diff: BrowserSourceDiff,
  escapeHtml: (value: unknown) => string,
  relationKind: "Addition" | "Removal" | "Correspondence",
  placement: "Stable" | "Moved" | null,
): string {
  const sequence = diff[side];
  const text = sequence.lines[index] ?? "";
  const beforeNumber = side === "before" ? String(index + 1) : "";
  const afterNumber = side === "after" ? String(index + 1) : "";
  const marker = side === "before" ? "−" : "+";
  return `<div class="member-diff-source-line member-diff-source-line-${side}" data-side="${side}" data-line="${index}" data-relation-kind="${relationKind}" data-placement="${placement ?? ""}">
    <span class="member-diff-source-number">${beforeNumber}</span>
    <span class="member-diff-source-number">${afterNumber}</span>
    <span class="member-diff-source-marker" aria-hidden="true">${marker}</span>
    <code>${highlightedLine(
      text,
      changedSpans(diff.changes, side, index),
      escapeHtml,
    )}</code>
  </div>`;
}

function annotationTarget(
  annotation: BrowserSourceDiffChange["annotations"][number],
): string {
  if (annotation.targetKind === "Change") return "Change";
  if (annotation.targetKind === "Line") {
    return `${annotation.side ?? "Both"} line ${annotation.line ?? 0}`;
  }
  const span = annotation.span;
  return span === null
    ? `${annotation.side ?? "Both"} span`
    : `${annotation.side ?? "Both"} line ${span.line}, ${span.start}:${span.count}`;
}

function mappedChangeEvidence(
  diff: BrowserSourceDiff,
  escapeHtml: (value: unknown) => string,
): string {
  if (diff.changes.length === 0) return "";
  return `<details class="member-diff-source-evidence">
    <summary>Mapped change evidence</summary>
    <ol>${diff.changes.map((change, index) => {
      const annotations = change.annotations.length === 0
        ? ""
        : `<ul>${change.annotations.map(annotation =>
          `<li><strong>${escapeHtml(annotation.severity)}</strong> · ${escapeHtml(annotation.text)} · ${escapeHtml(annotationTarget(annotation))}</li>`).join("")}</ul>`;
      return `<li>
        Change ${index + 1}: Before ${change.before.start}:${change.before.count} → After ${change.after.start}:${change.after.count};
        ${change.innerMappings.length.toLocaleString()} inner mappings.
        ${annotations}
      </li>`;
    }).join("")}</ol>
  </details>`;
}

export function renderMemberSourceDiff(
  diff: BrowserSourceDiff,
  escapeHtml: (value: unknown) => string,
): string {
  const rows: string[] = [];
  for (const relation of diff.relations) {
    const unchanged = relation.kind === "Correspondence"
      && relation.content === "Unchanged";
    if (unchanged) {
      const count = Math.max(
        relation.beforeCoordinates.length,
        relation.afterCoordinates.length,
      );
      for (let index = 0; index < count; index++) {
        const before = relation.beforeCoordinates[index];
        const after = relation.afterCoordinates[index];
        const text = after === undefined
          ? diff.before.lines[before ?? -1] ?? ""
          : diff.after.lines[after] ?? "";
        rows.push(`<div class="member-diff-source-line member-diff-source-line-same${relation.placement === "Moved" ? " member-diff-source-line-moved" : ""}" data-before-line="${before ?? ""}" data-after-line="${after ?? ""}" data-relation-kind="Correspondence" data-placement="${relation.placement}">
          <span class="member-diff-source-number">${before === undefined ? "" : before + 1}</span>
          <span class="member-diff-source-number">${after === undefined ? "" : after + 1}</span>
          <span class="member-diff-source-marker" aria-hidden="true">${relation.placement === "Moved" ? "↕" : " "}</span>
          <code>${escapeHtml(text)}</code>
        </div>`);
      }
      continue;
    }
    for (const coordinate of relation.beforeCoordinates) {
      rows.push(diffLine(
        "before",
        coordinate,
        diff,
        escapeHtml,
        relation.kind,
        relation.placement,
      ));
    }
    for (const coordinate of relation.afterCoordinates) {
      rows.push(diffLine(
        "after",
        coordinate,
        diff,
        escapeHtml,
        relation.kind,
        relation.placement,
      ));
    }
  }
  const statistics = diff.statistics;
  return `<div class="member-diff-source-summary" aria-label="Source diff statistics">
    <span>${statistics.added.toLocaleString()} added</span>
    <span>${statistics.removed.toLocaleString()} removed</span>
    <span>${statistics.changedBefore.toLocaleString()} Before changed</span>
    <span>${statistics.changedAfter.toLocaleString()} After changed</span>
    <span>${statistics.movedAfter.toLocaleString()} moved</span>
  </div>
  <div class="member-diff-source-diff" role="table" aria-label="Unified authored Source diff">${rows.join("")}</div>
  <p class="member-diff-source-terminators">Final line terminators: Before ${escapeHtml(diff.before.finalLineTerminator)}; After ${escapeHtml(diff.after.finalLineTerminator)}.</p>
  ${mappedChangeEvidence(diff, escapeHtml)}`;
}

function retryButton(): string {
  return '<button type="button" class="secondary" data-member-diff-source-retry>Retry Authored Source</button>';
}

function requestedEndpoint(
  endpoint: BrowserLibraryApiDiffMemberExploreEndpoint,
  label: string,
  state: string,
  escapeHtml: (value: unknown) => string,
): string {
  return `<section class="member-diff-source-endpoint">
    <h3>${escapeHtml(label)}</h3>
    <p>${endpoint.member === null
      ? "Not present on this side."
      : escapeHtml(state)}</p>
  </section>`;
}

function requestedEndpoints(
  context: LibraryApiDiffMemberExploreContext,
  state: string,
  escapeHtml: (value: unknown) => string,
): string {
  return `<div class="member-diff-source-endpoints">
    ${requestedEndpoint(
      context.destination.target,
      "Before",
      state,
      escapeHtml,
    )}
    ${requestedEndpoint(
      context.destination.current,
      "After",
      state,
      escapeHtml,
    )}
  </div>`;
}

function renderComparedSource(
  value: BrowserSourceComparison,
  context: LibraryApiDiffMemberExploreContext,
  escapeHtml: (value: unknown) => string,
): string {
  const exact = value.isExact
    ? '<span class="member-diff-source-exact">Exact member match</span>'
    : '<span class="member-diff-source-inexact">Non-exact result</span>';
  const endpoints = `<div class="member-diff-source-endpoints">
    ${endpointStatus(value.before, context.destination.target, "Before", escapeHtml)}
    ${endpointStatus(value.after, context.destination.current, "After", escapeHtml)}
  </div>`;
  if (value.status === "Compared" && value.diff !== null) {
    return `${exact}${endpoints}${renderMemberSourceDiff(value.diff, escapeHtml)}`;
  }
  if (value.status === "Failed") {
    return `${endpoints}<p class="member-diff-source-failure">${escapeHtml(
      value.failure ?? "Authored Source comparison failed.",
    )}</p>${retryButton()}`;
  }
  return `${endpoints}<p class="member-diff-source-unavailable">A paired authored Source comparison is unavailable for these endpoints.</p>`;
}

function renderSourcePane(
  state: MemberDiffExplorerSourceState,
  context: LibraryApiDiffMemberExploreContext,
  escapeHtml: (value: unknown) => string,
): string {
  switch (state.status) {
    case "idle":
    case "loading":
      return `${requestedEndpoints(
        context,
        "Loading authored Source…",
        escapeHtml,
      )}<p class="member-diff-source-loading" role="status">Loading authored Source…</p>`;
    case "failed":
      return `${requestedEndpoints(
        context,
        "Authored Source request failed.",
        escapeHtml,
      )}<p class="member-diff-source-failure">${escapeHtml(state.error)}</p>${retryButton()}`;
    case "canceled":
      return `${requestedEndpoints(
        context,
        "Authored Source request canceled.",
        escapeHtml,
      )}<p class="member-diff-source-canceled">${escapeHtml(state.reason)}</p>${retryButton()}`;
    case "ready": {
      const result = state.result;
      if (result.kind === "Succeeded" && result.value !== null) {
        return renderComparedSource(result.value, context, escapeHtml);
      }
      if (result.kind === "TooComplex" && result.capacity !== null) {
        return `${requestedEndpoints(
          context,
          "Authored Source comparison exceeded capacity.",
          escapeHtml,
        )}<p class="member-diff-source-failure">Authored Source exceeds the ${escapeHtml(result.capacity.dimension)} capacity: ${result.capacity.actual.toLocaleString()} observed, ${result.capacity.limit.toLocaleString()} allowed.</p>${retryButton()}`;
      }
      if (result.kind === "Canceled") {
        return `${requestedEndpoints(
          context,
          "Authored Source request canceled.",
          escapeHtml,
        )}<p class="member-diff-source-canceled">${escapeHtml(result.reason ?? "Authored Source was canceled.")}</p>${retryButton()}`;
      }
      return `${requestedEndpoints(
        context,
        "Authored Source request failed.",
        escapeHtml,
      )}<p class="member-diff-source-failure">${escapeHtml(
        result.error ?? result.reason ?? "Authored Source failed.",
      )}</p>${retryButton()}`;
    }
  }
  const exhaustive: never = state;
  throw new Error(`Unhandled Member Diff Source state: ${String(exhaustive)}`);
}

export function renderMemberDiffExplorer(
  context: LibraryApiDiffMemberExploreContext,
  source: MemberDiffExplorerSourceState,
  escapeHtml: (value: unknown) => string,
): string {
  const before = context.destination.target.member;
  const after = context.destination.current.member;
  const signature = after?.display ?? before?.display ?? "";
  const declarationReason =
    "Paired declaration evidence is not available yet. This stage requires a dedicated product-issued declaration comparison.";
  return `<div class="member-diff-explorer-frame">
    <header class="member-diff-explorer-header">
      <div>
        <p class="member-diff-explorer-kicker">Member Diff · ${escapeHtml(
          libraryApiDiffMemberStateLabel(context.member),
        )}</p>
        <h1 id="member-diff-explorer-title" tabindex="-1">${escapeHtml(context.subjectLabel)}</h1>
        <p id="member-diff-explorer-baseline">${escapeHtml(context.destination.target.version)} → ${escapeHtml(context.destination.current.version)} · ${escapeHtml(context.destination.current.framework)}${signature === "" ? "" : ` · ${escapeHtml(signature)}`}</p>
        ${renderLibraryApiDiffChangeChips(context.member.changes, escapeHtml)}
      </div>
      <button type="button" class="member-diff-explorer-close" data-member-diff-close aria-label="Close Member Diff Explore">Close</button>
    </header>
    <main class="member-diff-explorer-panes">
      <section class="member-diff-explorer-pane" tabindex="0" data-member-diff-pane="changes" aria-labelledby="member-diff-what-changed">
        <h2 id="member-diff-what-changed">What changed</h2>
        ${renderLibraryApiDiffMemberChanges(context.type, context.member, escapeHtml)}
      </section>
      <section class="member-diff-explorer-pane" tabindex="0" data-member-diff-pane="declaration" aria-labelledby="member-diff-declaration">
        <h2 id="member-diff-declaration">Declaration</h2>
        ${requestedEndpoints(context, declarationReason, escapeHtml)}
        <p class="member-diff-declaration-unavailable">${escapeHtml(declarationReason)}</p>
      </section>
      <section class="member-diff-explorer-pane member-diff-explorer-source" tabindex="0" data-member-diff-pane="source" aria-labelledby="member-diff-source">
        <h2 id="member-diff-source">Authored Source</h2>
        ${renderSourcePane(source, context, escapeHtml)}
      </section>
    </main>
  </div>`;
}

function isRetainable(result: BrowserSourceComparisonResult): boolean {
  return result.kind === "Succeeded"
    && result.value !== null
    && result.value.status !== "Failed";
}

export function createMemberDiffExplorer(
  dependencies: MemberDiffExplorerDependencies,
): MemberDiffExplorerController {
  let context: LibraryApiDiffMemberExploreContext | null = null;
  let dialog: HTMLDialogElement | null = null;
  let invoker: HTMLElement | null = null;
  let pendingFallbackFocus = false;
  let source: MemberDiffExplorerSourceState = { status: "idle" };
  let retained: RetainedSource | null = null;
  const inputs = new Map<OperationId, MemberDiffExplorerOperationInput>();

  const render = (): void => {
    if (context === null || dialog === null) return;
    const focusSelectors = [
      "#member-diff-explorer-title",
      "[data-member-diff-close]",
      "[data-member-diff-source-retry]",
      '[data-member-diff-pane="changes"]',
      '[data-member-diff-pane="declaration"]',
      '[data-member-diff-pane="source"]',
    ];
    const focusSelector = focusSelectors.find(selector =>
      dialog?.querySelector(selector) === dependencies.document.activeElement);
    dialog.innerHTML = renderMemberDiffExplorer(
      context,
      source,
      dependencies.escapeHtml,
    );
    dialog.querySelector<HTMLElement>("[data-member-diff-close]")
      ?.addEventListener("click", () => close(true, "user"));
    dialog.querySelector<HTMLElement>("[data-member-diff-source-retry]")
      ?.addEventListener("click", startSource);
    if (focusSelector !== undefined) {
      dialog.querySelector<HTMLElement>(focusSelector)
        ?.focus({ preventScroll: true });
    }
  };

  const inputFor = (operationId: OperationId) => {
    const input = inputs.get(operationId);
    if (input === undefined)
      throw new Error("Member Diff Explore operation context is unavailable.");
    return input;
  };
  const publish = (event: FeatureEvent): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced": {
        const alreadyLoading = source.status === "loading";
        source = { status: "loading" };
        if (!alreadyLoading) render();
        break;
      }
      case "terminal": {
        const input = inputFor(event.operationId);
        if (event.outcome.kind === "succeeded") {
          source = { status: "ready", result: event.outcome.value };
          retained = isRetainable(event.outcome.value)
            ? { context: input.context, result: event.outcome.value }
            : null;
        } else {
          source = {
            status: "failed",
            error: dependencies.describeError(event.outcome.error),
          };
          retained = null;
        }
        render();
        break;
      }
      case "canceled":
        source = {
          status: "canceled",
          reason: "Authored Source was canceled.",
        };
        render();
        break;
      case "disposed":
        break;
      case "progress":
        break;
    }
    return undefined;
  };
  const session: Session = dependencies.operationAuthority.createSession({
    feature: { publish },
    diagnostic: {
      report: diagnostic => {
        dependencies.reportOperationDiagnostic(diagnostic);
        return undefined;
      },
    },
  });
  const adapter: OperationProducerAdapter<
    MemberDiffExplorerOperationInput,
    BrowserSourceComparisonResult,
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
      const finish = (result: BrowserSourceComparisonResult): undefined => {
        try {
          validateResult(result, input.request);
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
            let query: Promise<BrowserSourceComparisonResult>;
            try {
              query = dependencies.query(identity.id, input.request);
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

  function startSource(): void {
    if (context === null) return;
    retained = null;
    let request: BrowserSourceComparisonRequest;
    try {
      request = memberDiffSourceRequest(context);
    } catch (error: unknown) {
      source = {
        status: "failed",
        error: dependencies.describeError(error),
      };
      render();
      return;
    }
    const started = session.start({ context, request }, adapter);
    if (started.kind === "rejected") {
      source = {
        status: "failed",
        error: "Authored Source could not start.",
      };
      render();
    }
  }

  function close(
    restoreFocus: boolean,
    reason: OperationCancelReason,
  ): void {
    const returnTarget = invoker;
    context = null;
    invoker = null;
    session.cancelCurrent(reason);
    if (dialog !== null) {
      if (dialog.open) dialog.close();
      dialog.remove();
      dialog = null;
    }
    if (restoreFocus && returnTarget?.isConnected === true) {
      returnTarget.focus({ preventScroll: true });
    }
  }

  return {
    get isOpen() {
      return dialog !== null;
    },
    open(nextContext, nextInvoker) {
      if (dialog !== null) close(false, "superseded");
      context = nextContext;
      invoker = nextInvoker;
      source = retained !== null && sameContext(retained.context, nextContext)
        ? { status: "ready", result: retained.result }
        : { status: "loading" };
      const nextDialog = dependencies.document.createElement("dialog");
      dialog = nextDialog;
      nextDialog.className = "member-diff-explorer";
      nextDialog.setAttribute(
        "aria-labelledby",
        "member-diff-explorer-title member-diff-explorer-baseline",
      );
      nextDialog.addEventListener("cancel", event => {
        event.preventDefault();
        close(true, "user");
      });
      nextDialog.addEventListener("keydown", event => {
        if (event.key === "Escape") {
          event.preventDefault();
          event.stopPropagation();
          close(true, "user");
          return;
        }
        if (event.key === "Tab") trapModalTab(nextDialog, event);
      });
      dependencies.document.body.append(nextDialog);
      render();
      nextDialog.showModal();
      nextDialog.querySelector<HTMLElement>("#member-diff-explorer-title")
        ?.focus();
      if (retained === null || !sameContext(retained.context, nextContext)) {
        startSource();
      }
    },
    reconcile(nextContext) {
      if (context === null || dialog === null) return false;
      if (nextContext !== null && sameContext(context, nextContext)) {
        context = nextContext;
        return false;
      }
      if (nextContext !== null && retained !== null
        && !sameContext(retained.context, nextContext)) {
        retained = null;
      }
      pendingFallbackFocus = true;
      close(false, "superseded");
      return true;
    },
    afterRender(fallback) {
      if (dialog !== null && context !== null) {
        invoker = dependencies.document.querySelector<HTMLElement>(
          "[data-member-diff-explore]",
        ) ?? invoker;
      }
      if (!pendingFallbackFocus) return;
      pendingFallbackFocus = false;
      if (fallback !== null) fallback.tabIndex = -1;
      fallback?.focus({ preventScroll: true });
    },
    dispose() {
      context = null;
      invoker = null;
      retained = null;
      if (dialog !== null) {
        if (dialog.open) dialog.close();
        dialog.remove();
        dialog = null;
      }
      session.dispose();
    },
  };
}
