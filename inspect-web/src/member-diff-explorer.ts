import type {
  BrowserLibraryApiDiffMemberExploreEndpoint,
  BrowserLibraryApiDiffMemberIdentity,
} from "./facades/inspect-web-metadata.d.ts";
import {
  libraryApiDiffMemberStateLabel,
  renderLibraryApiDiffChangeChips,
  type LibraryApiDiffMemberExploreContext,
} from "./library-api-diff.ts";
import { renderCodeEvidenceViewerFrame } from "./code-evidence-viewer.ts";
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
import {
  bindSourceDiffViewer,
  renderSourceDiffViewer,
  type SourceDiffViewerMode,
} from "./source-diff-viewer.ts";
import type {
  BrowserSourceComparison,
  BrowserSourceComparisonEndpoint,
  BrowserSourceComparisonEndpointRequest,
  BrowserSourceComparisonRequest,
  BrowserSourceComparisonResult,
  BrowserSourceDiff,
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
  readonly writeClipboardText?: (value: string) => Promise<void>;
  readonly renderPage: () => void;
}

export interface MemberDiffExplorerController {
  readonly isOpen: boolean;
  renderInline(context: LibraryApiDiffMemberExploreContext): string;
  bindInline(root: ParentNode): void;
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
    && sameRequest(
      memberDiffSourceRequest(left),
      memberDiffSourceRequest(right),
    );
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

function endpointStatus(
  endpoint: BrowserSourceComparisonEndpoint,
  destination: BrowserLibraryApiDiffMemberExploreEndpoint,
  label: string,
  escapeHtml: (value: unknown) => string,
  includeText = false,
): string {
  if (destination.member === null) {
    return `<section class="member-diff-source-endpoint">
      <h3>${escapeHtml(label)}</h3>
      <p>Not present on this side.</p>
    </section>`;
  }
  const detail = endpoint.detail === null
    ? ""
    : `<p>${escapeHtml(endpoint.detail)}</p>`;
  const open = sourceOpenAction(endpoint, "Open source", escapeHtml);
  const text = includeText && endpoint.text !== null
    ? `<pre class="member-diff-source-text"><code>${escapeHtml(endpoint.text)}</code></pre>`
    : "";
  return `<section class="member-diff-source-endpoint">
    <h3>${escapeHtml(label)}</h3>
    <p><strong>${escapeHtml(endpoint.state)}</strong> · ${escapeHtml(endpoint.version)}</p>
    ${detail}
    ${open}
    ${text}
  </section>`;
}

export function renderMemberSourceDiff(
  diff: BrowserSourceDiff,
  escapeHtml: (value: unknown) => string,
  compact = false,
  mode: SourceDiffViewerMode = "unified",
): string {
  return renderSourceDiffViewer(diff, escapeHtml, { compact, mode });
}

export function renderInlineMemberSourceDiff(
  context: LibraryApiDiffMemberExploreContext,
  source: MemberDiffExplorerSourceState,
  escapeHtml: (value: unknown) => string,
): string {
  const content = source.status === "idle"
    ? `${compactDestinationNotices(context, escapeHtml)}
      <button type="button" class="secondary" data-member-diff-source-show>Show authored Source diff</button>`
    : renderSourcePane(source, context, escapeHtml, true);
  return `<section class="library-api-diff-change-section member-diff-inline-source" aria-labelledby="member-diff-inline-source-title">
    <h2 id="member-diff-inline-source-title">Authored Source</h2>
    ${content}
  </section>`;
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

function compactEndpointNotice(
  destination: BrowserLibraryApiDiffMemberExploreEndpoint,
  endpoint: BrowserSourceComparisonEndpoint | null,
  label: string,
  escapeHtml: (value: unknown) => string,
): string {
  if (destination.member === null) {
    return `<p class="member-diff-source-unavailable"><strong>${escapeHtml(label)}</strong>: Not present on this side.</p>`;
  }
  if (endpoint === null || endpoint.state === "Available") {
    return "";
  }
  return `<p class="member-diff-source-unavailable"><strong>${escapeHtml(label)}</strong>: ${escapeHtml(endpoint.detail ?? endpoint.state)}</p>`;
}

function compactDestinationNotices(
  context: LibraryApiDiffMemberExploreContext,
  escapeHtml: (value: unknown) => string,
): string {
  return `${compactEndpointNotice(
    context.destination.target,
    null,
    "Before",
    escapeHtml,
  )}${compactEndpointNotice(
    context.destination.current,
    null,
    "After",
    escapeHtml,
  )}`;
}

function compactComparisonNotices(
  value: BrowserSourceComparison,
  context: LibraryApiDiffMemberExploreContext,
  escapeHtml: (value: unknown) => string,
): string {
  return `${compactEndpointNotice(
    context.destination.target,
    value.before,
    "Before",
    escapeHtml,
  )}${compactEndpointNotice(
    context.destination.current,
    value.after,
    "After",
    escapeHtml,
  )}`;
}

function sourceOpenAction(
  endpoint: BrowserSourceComparisonEndpoint,
  label: string,
  escapeHtml: (value: unknown) => string,
): string {
  if (endpoint.browseUrl === null) return "";
  const href = safeExternalHref(endpoint.browseUrl);
  return href === null
    ? ""
    : `<a href="${attributeText(href, escapeHtml)}" target="_blank" rel="noopener noreferrer">${escapeHtml(label)}</a>`;
}

function renderComparedSource(
  value: BrowserSourceComparison,
  context: LibraryApiDiffMemberExploreContext,
  escapeHtml: (value: unknown) => string,
  compact = false,
  mode: SourceDiffViewerMode = "unified",
): string {
  const exact = value.isExact
    ? '<span class="member-diff-source-exact">Authored Source is identical</span>'
    : "";
  const endpoints = compact
    ? compactComparisonNotices(value, context, escapeHtml)
    : "";
  if (value.status === "Compared" && value.diff !== null) {
    return compact
      ? `${exact}${renderMemberSourceDiff(value.diff, escapeHtml, true)}`
      : `${exact}${renderMemberSourceDiff(
          value.diff,
          escapeHtml,
          false,
          mode,
        )}`;
  }
  if (value.status === "Failed") {
    const failure = `<p class="member-diff-source-failure">${escapeHtml(
      value.failure ?? "Authored Source comparison failed.",
    )}</p>${retryButton()}`;
    return compact ? `${failure}${endpoints}` : `${endpoints}${failure}`;
  }
  const unavailable = '<p class="member-diff-source-unavailable">A paired authored Source comparison is unavailable for these endpoints.</p>';
  return compact
    ? `${unavailable}${endpoints}`
    : `${endpoints}${unavailable}`;
}

function renderSourcePane(
  state: MemberDiffExplorerSourceState,
  context: LibraryApiDiffMemberExploreContext,
  escapeHtml: (value: unknown) => string,
  compact = false,
  mode: SourceDiffViewerMode = "unified",
): string {
  const endpointContext = compact
    ? compactDestinationNotices(context, escapeHtml)
    : "";
  switch (state.status) {
    case "idle":
    case "loading":
      return `${endpointContext}<p class="member-diff-source-loading" role="status">Loading authored Source…</p>`;
    case "failed":
      return `${endpointContext}<p class="member-diff-source-failure">${escapeHtml(state.error)}</p>${retryButton()}`;
    case "canceled":
      return `${endpointContext}<p class="member-diff-source-canceled">${escapeHtml(state.reason)}</p>${retryButton()}`;
    case "ready": {
      const result = state.result;
      if (result.kind === "Succeeded" && result.value !== null) {
        return renderComparedSource(
          result.value,
          context,
          escapeHtml,
          compact,
          mode,
        );
      }
      if (result.kind === "TooComplex" && result.capacity !== null) {
        const failure = `<p class="member-diff-source-failure">Authored Source exceeds the ${escapeHtml(result.capacity.dimension)} capacity: ${result.capacity.actual.toLocaleString()} observed, ${result.capacity.limit.toLocaleString()} allowed.</p>${retryButton()}`;
        return `${endpointContext}${failure}`;
      }
      if (result.kind === "Canceled") {
        const canceled = `<p class="member-diff-source-canceled">${escapeHtml(result.reason ?? "Authored Source was canceled.")}</p>${retryButton()}`;
        return `${endpointContext}${canceled}`;
      }
      const failure = `<p class="member-diff-source-failure">${escapeHtml(
        result.error ?? result.reason ?? "Authored Source failed.",
      )}</p>${retryButton()}`;
      return `${endpointContext}${failure}`;
    }
  }
  const exhaustive: never = state;
  throw new Error(`Unhandled Member Diff Source state: ${String(exhaustive)}`);
}

function sourceEvidenceStatus(
  source: MemberDiffExplorerSourceState,
): string {
  switch (source.status) {
    case "idle":
    case "loading":
      return "Loading";
    case "failed":
      return "Failed";
    case "canceled":
      return "Canceled";
    case "ready": {
      const result = source.result;
      if (result.kind === "TooComplex") return "Too complex";
      if (result.kind !== "Succeeded" || result.value === null)
        return result.kind;
      if (result.value.status !== "Compared") return result.value.status;
      return result.value.isExact ? "Identical" : "Changed";
    }
  }
  const exhaustive: never = source;
  throw new Error(`Unhandled Member Diff Source state: ${String(exhaustive)}`);
}

function renderMemberDiffRail(
  context: LibraryApiDiffMemberExploreContext,
  source: MemberDiffExplorerSourceState,
  escapeHtml: (value: unknown) => string,
): string {
  const comparison = source.status === "ready"
      && source.result.kind === "Succeeded"
    ? source.result.value
    : null;
  const state = sourceEvidenceStatus(source);
  const endpoints = comparison === null
    ? requestedEndpoints(context, `Authored Source ${state.toLowerCase()}.`, escapeHtml)
    : `<div class="member-diff-source-endpoints">
        ${endpointStatus(
          comparison.before,
          context.destination.target,
          "Before",
          escapeHtml,
          comparison.status !== "Compared",
        )}
        ${endpointStatus(
          comparison.after,
          context.destination.current,
          "After",
          escapeHtml,
          comparison.status !== "Compared",
        )}
      </div>`;
  return `<div class="member-diff-evidence">
    <section class="member-diff-evidence-section"
      aria-labelledby="member-diff-evidence-title">
      <h2 id="member-diff-evidence-title">Evidence</h2>
      <div class="member-diff-evidence-choice" data-member-diff-mode="text"
        aria-current="true">
        <strong>Authored Source</strong>
        <span>${escapeHtml(state)}</span>
      </div>
    </section>
    <section class="member-diff-evidence-section"
      aria-labelledby="member-diff-endpoints-title">
      <h2 id="member-diff-endpoints-title">Endpoints</h2>
      ${endpoints}
    </section>
  </div>`;
}

export function renderMemberDiffExplorer(
  context: LibraryApiDiffMemberExploreContext,
  source: MemberDiffExplorerSourceState,
  escapeHtml: (value: unknown) => string,
  mode: SourceDiffViewerMode = "unified",
): string {
  const before = context.destination.target.member;
  const after = context.destination.current.member;
  const signature = after?.display ?? before?.display ?? "";
  return renderCodeEvidenceViewerFrame({
    viewerId: "member-diff-explorer-frame",
    viewerClassName: "member-diff-explorer-frame",
    labelledBy: "member-diff-explorer-title member-diff-explorer-baseline",
    headerClassName: "member-diff-explorer-header",
    header: `
      <div>
        <p class="member-diff-explorer-kicker">Member Diff · ${escapeHtml(
          libraryApiDiffMemberStateLabel(context.member),
        )}</p>
        <h1 id="member-diff-explorer-title" tabindex="-1">${escapeHtml(context.subjectLabel)}</h1>
        <p id="member-diff-explorer-baseline">${escapeHtml(context.destination.target.version)} → ${escapeHtml(context.destination.current.version)} · ${escapeHtml(context.destination.current.framework)}${signature === "" ? "" : ` · ${escapeHtml(signature)}`}</p>
        ${renderLibraryApiDiffChangeChips(context.member.changes, escapeHtml)}
      </div>
      <button type="button" class="member-diff-explorer-close"
        data-member-diff-close
        aria-label="Close Member Diff Explore">Close</button>`,
    body: {
      kind: "workspace",
      className: "member-diff-explorer-workspace",
      content: {
        html: `<div class="member-diff-explorer-source"
          data-member-diff-mode="text" tabindex="0">
          ${renderSourcePane(source, context, escapeHtml, false, mode)}
        </div>`,
        label: "Authored Source text diff",
        className: "member-diff-explorer-content",
      },
      rail: {
        html: renderMemberDiffRail(context, source, escapeHtml),
        label: "Member Diff evidence",
        className: "member-diff-explorer-rail",
      },
    },
    escapeHtml,
  });
}

function isRetainable(result: BrowserSourceComparisonResult): boolean {
  return result.kind === "Succeeded"
    && result.value !== null
    && result.value.status !== "Failed";
}

function readySourceDiff(
  source: MemberDiffExplorerSourceState,
): BrowserSourceDiff | null {
  return source.status === "ready"
      && source.result.kind === "Succeeded"
      && source.result.value?.status === "Compared"
    ? source.result.value.diff
    : null;
}

function retriesOnActivation(source: MemberDiffExplorerSourceState): boolean {
  return source.status === "failed"
    || source.status === "canceled"
    || (source.status === "ready"
      && (source.result.kind === "Failed"
        || source.result.kind === "Canceled"
        || (source.result.kind === "Succeeded"
          && source.result.value?.status === "Failed")));
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
  let sourceDiffMode: SourceDiffViewerMode = "unified";
  const inputs = new Map<OperationId, MemberDiffExplorerOperationInput>();

  const render = (): void => {
    if (context === null || dialog === null) return;
    const focusSelectors = [
      "#member-diff-explorer-title",
      "[data-member-diff-close]",
      "[data-member-diff-source-retry]",
      "[data-source-diff-viewer]",
      '[data-member-diff-mode="text"]',
    ];
    const focusSelector = focusSelectors.find(selector =>
      dialog?.querySelector(selector) === dependencies.document.activeElement);
    dialog.innerHTML = renderMemberDiffExplorer(
      context,
      source,
      dependencies.escapeHtml,
      sourceDiffMode,
    );
    dialog.querySelector<HTMLElement>("[data-member-diff-close]")
      ?.addEventListener("click", () => close(true, "user"));
    dialog.querySelector<HTMLElement>("[data-member-diff-source-retry]")
      ?.addEventListener("click", startSource);
    const diff = readySourceDiff(source);
    if (diff !== null) {
      bindSourceDiffViewer(dialog, diff, {
        mode: sourceDiffMode,
        onModeChanged: mode => {
          sourceDiffMode = mode;
        },
        writeClipboardText: dependencies.writeClipboardText
          ?? (() => Promise.reject(
            new Error("Clipboard access is unavailable."),
          )),
      });
    }
    if (focusSelector !== undefined) {
      const focusTarget = dialog.querySelector<HTMLElement>(focusSelector)
        ?? dialog.querySelector<HTMLElement>(
          '[data-member-diff-mode="text"]',
        );
      focusTarget?.focus({ preventScroll: true });
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
        if (!alreadyLoading) {
          render();
          dependencies.renderPage();
        }
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
        dependencies.renderPage();
        break;
      }
      case "canceled":
        source = {
          status: "canceled",
          reason: "Authored Source was canceled.",
        };
        render();
        dependencies.renderPage();
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
      dependencies.renderPage();
      return;
    }
    const started = session.start({ context, request }, adapter);
    if (started.kind === "rejected") {
      source = {
        status: "failed",
        error: "Authored Source could not start.",
      };
      render();
      dependencies.renderPage();
    }
  }

  function close(
    restoreFocus: boolean,
    _reason: OperationCancelReason,
  ): void {
    const returnTarget = invoker;
    invoker = null;
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
    renderInline(nextContext) {
      return renderInlineMemberSourceDiff(
        nextContext,
        context !== null && sameContext(context, nextContext)
          ? source
          : { status: "idle" },
        dependencies.escapeHtml,
      );
    },
    bindInline(root) {
      root.querySelector<HTMLElement>("[data-member-diff-source-show]")
        ?.addEventListener("click", startSource);
      root.querySelector<HTMLElement>("[data-member-diff-source-retry]")
        ?.addEventListener("click", startSource);
    },
    open(nextContext, nextInvoker) {
      if (dialog !== null) close(false, "superseded");
      if (context === null || !sameContext(context, nextContext)) {
        session.cancelCurrent("superseded");
        context = nextContext;
        source = retained !== null
            && sameContext(retained.context, nextContext)
          ? { status: "ready", result: retained.result }
          : { status: "idle" };
      } else {
        context = nextContext;
        if (retriesOnActivation(source)) source = { status: "idle" };
      }
      invoker = nextInvoker;
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
      if (source.status === "idle") {
        startSource();
      }
    },
    reconcile(nextContext) {
      if (nextContext !== null
        && context !== null
        && sameContext(context, nextContext)) {
        context = nextContext;
        return false;
      }
      const dialogWasOpen = dialog !== null;
      session.cancelCurrent("superseded");
      context = nextContext;
      source = nextContext !== null
          && retained !== null
          && sameContext(retained.context, nextContext)
        ? { status: "ready", result: retained.result }
        : { status: "idle" };
      if (!dialogWasOpen) return false;
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
