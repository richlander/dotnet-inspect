import type {
  BrowserMemberBodyDiffRequest,
  BrowserMemberBodyDiffResult,
  BrowserMemberBodyDiffInventory,
  BrowserMemberBodyDiffDocument,
  BrowserMemberBodyMember,
} from "./facades/inspect-web-source.d.ts";
import {
  type OperationAuthorityPage,
  type OperationProducerAdapter,
  type OperationSession,
  type OperationId,
  type OperationCancelReason,
  type OperationDiagnostic,
} from "./operation-authority.ts";
import { renderCompareFrame } from "./compare-surface.ts";
import { renderCodeEvidenceViewerFrame } from "./code-evidence-viewer.ts";
import { bindSourceDiffViewer, renderSourceDiffViewer, type SourceDiffViewerMode } from "./source-diff-viewer.ts";
import { decodeMemberBodyMappedDiff } from "./source-diff-transport.ts";

export interface MemberBodyDiffContext {
  readonly packageModel: object;
  readonly request: BrowserMemberBodyDiffRequest;
  readonly subject: "library" | "type" | "member";
  readonly subjectLabel: string;
  readonly targetText: string;
  readonly typeIdentity: string | null;
  readonly memberFingerprint: string | null;
  readonly methodToken: number | null;
  readonly bodySelector?: string | null;
}

interface Dependencies {
  readonly authority: OperationAuthorityPage;
  readonly query: (id: OperationId, request: BrowserMemberBodyDiffRequest) => Promise<BrowserMemberBodyDiffResult>;
  readonly cancel: (id: OperationId, reason: OperationCancelReason) => void;
  readonly diagnostic: (diagnostic: OperationDiagnostic) => void;
  readonly render: () => void;
  readonly escapeHtml: (value: unknown) => string;
  readonly document: Document;
  readonly activateType: (identity: string) => void;
  readonly activateMember: (member: BrowserMemberBodyMember) => void;
}

interface Reader {
  readonly document: BrowserMemberBodyDiffDocument;
  medium: "CSharp" | "Il";
  mode: SourceDiffViewerMode;
  scrollTop: number;
}
interface Retained {
  inventory: BrowserMemberBodyDiffInventory | null;
  readonly readers: Map<string, Reader>;
  readonly positions: Map<string, number>;
}
interface Input {
  readonly packageModel: object;
  readonly request: BrowserMemberBodyDiffRequest;
  readonly retained: Retained;
  readonly member: BrowserMemberBodyMember | null;
  readonly key: string;
}

type Session = OperationSession<Input, BrowserMemberBodyDiffResult, unknown, never, never>;

export function memberBodyDestination(
  inventory: BrowserMemberBodyDiffInventory,
  context: Pick<MemberBodyDiffContext, "typeIdentity" | "memberFingerprint" | "methodToken" | "bodySelector">,
): BrowserMemberBodyMember | null {
  const candidates = inventory.destinations.filter(member => member.typeIdentity === context.typeIdentity && member.fingerprint !== null
    && member.fingerprint === context.memberFingerprint
    && (context.bodySelector ? member.selector === context.bodySelector
      : context.methodToken === null
        ? !member.isAccessor
        : member.methodToken === context.methodToken));
  return candidates.length === 1 ? candidates[0]! : null;
}

export function renderMemberBodyReader(reader: Pick<Reader, "document" | "medium" | "mode">,
  escapeHtml: (value: unknown) => string): string {
  const document = reader.document;
  const medium = document.media.find(candidate => candidate.medium === reader.medium);
  if (!medium) return '<p role="status">This medium is unavailable.</p>';
  if (medium.limit) return `<p role="status">${escapeHtml(medium.limit)}</p>`;
  if (medium.diff) {
    const diff = decodeMemberBodyMappedDiff(medium.diff);
    return diff.changes.length === 0
      ? '<p role="status">Identical</p>'
      : renderSourceDiffViewer(diff, escapeHtml, { mode: reader.mode });
  }
  return `<p role="status">Before: ${escapeHtml(document.beforeDetail ?? document.beforeOutcome)}<br>After: ${escapeHtml(document.afterDetail ?? document.afterOutcome)}</p>${medium.beforeText ? `<pre aria-label="Available Before text">${escapeHtml(medium.beforeText)}</pre>` : ""}${medium.afterText ? `<pre aria-label="Available After text">${escapeHtml(medium.afterText)}</pre>` : ""}`;
}

export function createMemberBodyDiff(dependencies: Dependencies) {
  const cache = new WeakMap<object, Map<string, Retained>>();
  const inputs = new Map<OperationId, Input>();
  let context: MemberBodyDiffContext | null = null;
  let retained: Retained | null = null;
  let input: Input | null = null;
  let failure: string | null = null;
  let pending = false;
  let dialog: HTMLDialogElement | null = null;
  const escape = dependencies.escapeHtml;

  const restoredMedia = new WeakMap<object, "CSharp" | "Il">();
  const restoredBodies = new WeakMap<object, { type: string; anchor: string; selector: string }>();
  const session: Session = dependencies.authority.createSession({
    feature: { publish(event) {
      if (event.kind === "started") { pending = true; failure = null; }
      if (event.kind === "terminal") {
        pending = false;
        const completed = inputs.get(event.operationId);
        if (!completed) throw new Error("Member Body operation lost its input.");
        if (event.outcome.kind === "failed") failure = String(event.outcome.error);
        else {
          const result = event.outcome.value;
          if (result.kind === "Available" && result.inventory && completed.member === null)
            completed.retained.inventory = result.inventory;
          else if (result.kind === "Available" && result.document && completed.member) {
            for (const medium of result.document.media)
              if (medium.diff) decodeMemberBodyMappedDiff(medium.diff);
            completed.retained.readers.set(completed.member.id, {
              document: result.document, medium: restoredMedia.get(completed.packageModel) ?? "CSharp", mode: "unified", scrollTop: 0,
            });
          } else {
            if (result.kind === "Unavailable" && completed.member) {
              completed.retained.inventory = null;
              completed.retained.readers.clear();
            }
            failure = result.detail ?? `Member Body ${result.kind}.`;
          }
        }
        queueMicrotask(dependencies.render);
      }
      if (event.kind === "canceled") { pending = false; failure = "Canceled"; }
      return undefined;
    } },
    diagnostic: { report(value) { dependencies.diagnostic(value); return undefined; } },
  });
  const producer: OperationProducerAdapter<Input, BrowserMemberBodyDiffResult, unknown, never, never> = {
    prepare(identity, prepared, sink) {
      inputs.set(identity.id, prepared);
      return { kind: "prepared", binding: {
        activate() {
          const settle = (result: BrowserMemberBodyDiffResult): void => {
            try { sink.reportTerminal({ kind: "succeeded", value: result }); }
            catch (error: unknown) { sink.reportUnexpectedTerminal(error, error); }
            finally { inputs.delete(identity.id); sink.reportQuiesced(); }
          };
          void dependencies.query(identity.id, prepared.request).then(result => {
            settle(result);
            return null;
          }, (error: unknown) => { sink.reportUnexpectedTerminal(error, error); inputs.delete(identity.id); sink.reportQuiesced(); return null; });
          return undefined;
        },
        requestCancellation(reason) { dependencies.cancel(identity.id, reason); return undefined; },
        abandon() { inputs.delete(identity.id); return undefined; },
      } };
    },
  };
  const currentReader = (): Reader | null => {
    if (!context || !retained?.inventory || context.subject !== "member") return null;
    const member = memberBodyDestination(retained.inventory, context);
    return member ? retained.readers.get(member.id) ?? null : null;
  };
  const positionKey = (): string => `${context?.subject}:${context?.typeIdentity ?? ""}:${context?.memberFingerprint ?? ""}:${context?.methodToken ?? ""}`;
  const savePosition = (): void => {
    if (!retained) return;
    const scroller = dependencies.document.querySelector<HTMLElement>("[data-member-body-scroll]");
    if (scroller) {
      retained.positions.set(positionKey(), scroller.scrollTop);
      const reader = currentReader();
      if (reader) reader.scrollTop = scroller.scrollTop;
    }
  };
  const closeExplore = (): void => {
    if (!dialog) return;
    const reader = currentReader();
    const scroller = dialog.querySelector<HTMLElement>("[data-member-body-scroll]");
    if (reader && scroller) reader.scrollTop = scroller.scrollTop;
    dialog.close(); dialog.remove(); dialog = null;
  };
  function dismissExplore(): void {
    closeExplore(); dependencies.render();
    dependencies.document.querySelector<HTMLElement>("[data-member-body-explore]")?.focus({ preventScroll: true });
  }
  function bindReader(root: ParentNode, reader: Reader, expanded: boolean): void {
    root.querySelectorAll<HTMLButtonElement>("[data-member-body-medium]").forEach(button =>
      button.addEventListener("click", () => {
        reader.medium = button.dataset.memberBodyMedium === "Il" ? "Il" : "CSharp";
        if (context) restoredMedia.set(context.packageModel, reader.medium);
        if (expanded) paintExplore(reader);
        else { savePosition(); dependencies.render(); }
      }));
    const medium = reader.document.media.find(candidate => candidate.medium === reader.medium);
    if (medium?.diff) bindSourceDiffViewer(root, decodeMemberBodyMappedDiff(medium.diff), {
      mode: reader.mode, onModeChanged(mode) { reader.mode = mode; },
      writeClipboardText: text => dependencies.document.defaultView!.navigator.clipboard.writeText(text),
    });
  }
  function mediaControls(reader: Reader, toolbar = false): string {
    return reader.document.media.map(medium =>
      `<button type="button"${toolbar ? ` id="member-body-medium-${escape(medium.medium)}"` : ""} data-member-body-medium="${escape(medium.medium)}" aria-pressed="${medium.medium === reader.medium}">${medium.medium === "CSharp" ? "C#" : "IL"}</button>`).join("");
  }
  function categoryLabel(mechanism: string): string {
    if (mechanism === "CSharp") return "C#";
    if (mechanism === "IlBody") return "IL";
    return mechanism;
  }
  function memberCategories(member: BrowserMemberBodyMember): string[] {
    return [
      ...(member.hasApiChange ? ["API"] : []),
      ...member.mechanisms.map(categoryLabel),
    ];
  }
  function renderCategories(categories: readonly string[]): string {
    if (categories.length === 0) return "";
    return `<span class="member-body-categories">${categories.map(category =>
      `<span class="member-body-category">${escape(category)}</span>`).join("")}</span>`;
  }
  function renderInventoryRow(
    kind: "type" | "member",
    display: string,
    outcome: string,
    categories: readonly string[],
    attribute: string | null,
    inertReason: string | null,
  ): string {
    const state = `<span class="library-api-diff-state library-api-diff-state-${escape(outcome.toLowerCase())}">${escape(outcome)}</span>`;
    const copy = `<span class="library-api-diff-type-copy">
      <strong>${escape(display)}</strong>
      ${renderCategories(categories)}
      ${inertReason ? `<span class="library-api-diff-inert">${escape(inertReason)}</span>` : ""}
    </span>`;
    const row = attribute === null
      ? `<div class="library-api-diff-row" aria-disabled="true">${state}${copy}</div>`
      : `<button type="button" class="library-api-diff-row" data-member-body-${kind}="${escape(attribute)}" aria-label="Open ${escape(display)} Compare">${state}${copy}</button>`;
    return `<li class="library-api-diff-${kind}${attribute === null ? ` library-api-diff-${kind}-inert` : ""}">${row}</li>`;
  }
  function coverageNotice(inventory: BrowserMemberBodyDiffInventory): string {
    const notices = inventory.coverage.flatMap(item => {
      const mechanism = categoryLabel(item.mechanism);
      return [
        ...(item.unavailable > 0 ? [`${mechanism} unavailable for some Members`] : []),
        ...(item.incomplete > 0 ? [`${mechanism} incomplete for some Members`] : []),
        ...(item.failed > 0 ? [`${mechanism} failed for some Members`] : []),
      ];
    });
    return notices.length === 0
      ? ""
      : `<p class="member-body-coverage" role="status">${escape(notices.join(" · "))}</p>`;
  }
  function renderBodyFailure(): string {
    return `<p role="status">${escape(failure)}</p><button type="button" data-member-body-retry>Retry body comparison</button>`;
  }
  function renderInventorySection(subject: "library" | "type"): string {
    if (!context || context.subject !== subject) return "";
    if (failure) return renderBodyFailure();
    const inventory = retained?.inventory;
    if (!inventory) return pending ? '<p role="status">Loading implementation changes…</p>' : "";
    const types = subject === "type"
      ? inventory.types.filter(type => type.identity === context!.typeIdentity)
      : inventory.types;
    const rows = subject === "library" ? types.map(type => type.canNavigate
      ? renderInventoryRow("type", type.display, type.outcome,
          [...new Set([
            ...(type.hasApiChange ? ["API"] : []),
            ...type.members.flatMap(member => memberCategories(member)),
          ])],
          type.identity, null)
      : renderInventoryRow("type", type.display, "Removed", ["API"], null,
          `Removed in the current version; Before-side evidence only · ${type.apiMemberNames.length} ${type.apiMemberNames.length === 1 ? "Member" : "Members"}${type.apiMemberNames.length > 0 ? `: ${type.apiMemberNames.join(", ")}` : ""}`)).join("")
      : types.flatMap(type => type.members).map(member => member.fingerprint !== null
        ? renderInventoryRow("member", member.display, member.outcome,
            memberCategories(member), member.id, null)
        : renderInventoryRow("member", member.display, member.outcome,
            memberCategories(member), null,
            member.identityFailure ?? "No current Member destination")).join("");
    const listClass = subject === "library"
      ? "library-api-diff-types"
      : "library-api-diff-members";
    const listLabel = subject === "library"
      ? "Changed Types"
      : "Changed Members";
    return `${coverageNotice(inventory)}<div class="member-body-inventory member-body-scroll" data-member-body-scroll>${
      rows
        ? `<ol class="${listClass}" aria-label="${listLabel}">${rows}</ol>`
        : `<p role="status">${inventory.isComplete
          ? subject === "type" && types.some(type => type.hasApiChange)
            ? "No Member-level implementation changes"
            : "No API or implementation changes found"
          : "No changed rows; comparison coverage is incomplete."}</p>`
    }</div>`;
  }
  function renderMemberSection(): string {
    if (!context || context.subject !== "member") return "";
    if (failure) return `<section class="member-body-reader">${renderBodyFailure()}</section>`;
    if (!retained?.inventory)
      return pending ? '<section class="member-body-reader"><p role="status">Loading implementation changes…</p></section>' : "";
    const member = memberBodyDestination(retained.inventory, context);
    if (!member) return "";
    const reader = retained.readers.get(member.id);
    const body = reader
      ? renderMemberBodyReader(reader, escape)
      : pending
        ? '<p role="status">Loading exact Member diff…</p>'
        : retained.inventory.isComplete
          ? '<p role="status">No exact body comparison is available for this Member.</p>'
          : '<p role="status">Member comparison is incomplete. No complete result is available.</p>';
    return `<section class="member-body-reader"><div class="member-body-scroll" data-member-body-scroll>${body}</div></section>`;
  }
  function retry(): void {
    if (pending) return;
    failure = null;
    input = null;
    dependencies.render();
  }
  function paintExplore(reader: Reader): void {
    if (!dialog) return;
    dialog.innerHTML = renderCodeEvidenceViewerFrame({
      viewerId: "member-body-explore-viewer", labelledBy: "member-body-explore-title",
      header: `<h2 id="member-body-explore-title">${escape(context?.subjectLabel)}</h2>${mediaControls(reader)}<button type="button" data-member-body-close>Close</button>`,
      body: { kind: "workspace", content: { html: renderMemberBodyReader(reader, escape),
        label: "Member Body diff", scrollAttribute: "data-member-body-scroll", scrollKey: "reader" } },
      escapeHtml: escape,
    });
    bindReader(dialog, reader, true);
    dialog.querySelector("[data-member-body-close]")?.addEventListener("click", dismissExplore);
    const scroller = dialog.querySelector<HTMLElement>("[data-member-body-scroll]");
    if (scroller) scroller.scrollTop = reader.scrollTop;
  }
  return {
    copyPackages(copies: ReadonlyMap<object, object>) {
      for (const [original, copy] of copies) {
        const retainedPackages = cache.get(original);
        if (retainedPackages) {
          cache.set(copy, new Map([...retainedPackages].map(([key, value]) => [
            key,
            {
              inventory: value.inventory,
              readers: new Map([...value.readers].map(([id, reader]) => [id, { ...reader }])),
              positions: new Map(value.positions),
            },
          ])));
        }
        const body = restoredBodies.get(original);
        if (body) restoredBodies.set(copy, body);
        const medium = context?.packageModel === original
          ? currentReader()?.medium ?? restoredMedia.get(original)
          : restoredMedia.get(original);
        if (medium) restoredMedia.set(copy, medium);
      }
    },
    restoreBodySelector(packageModel: object, type: string, anchor: string, selector: string) {
      restoredBodies.set(packageModel, { type, anchor, selector });
    },
    clearRestoredBodySelector(packageModel: object) { restoredBodies.delete(packageModel); },
    restoredBodySelector(packageModel: object, type: string | null, anchor: string | null) {
      const body = restoredBodies.get(packageModel);
      return body?.type === type && body?.anchor === anchor ? body.selector : null;
    },
    captureBodySelector() {
      return context && retained?.inventory
        ? memberBodyDestination(retained.inventory, context)?.selector ?? context.bodySelector ?? null
        : context?.bodySelector ?? null;
    },
    captureMedium() { return currentReader()?.medium ?? (context ? restoredMedia.get(context.packageModel) : null) ?? "CSharp"; },
    restoreMedium(packageModel: object, medium: "CSharp" | "Il") { restoredMedia.set(packageModel, medium); },
    get isOpen() { return dialog !== null; },
    renderActions(): string {
      const reader = currentReader();
      return reader ? `${mediaControls(reader, true)}<button type="button" class="primary-action" id="member-body-explore" data-member-body-explore>Explore</button>` : "";
    },
    renderMemberSection,
    renderLibrarySection: () => renderInventorySection("library"),
    renderTypeSection: () => renderInventorySection("type"),
    get hasSettledInventory() { return retained?.inventory !== null && retained?.inventory !== undefined; },
    reconcile(next: MemberBodyDiffContext | null): void {
      savePosition();
      const prior = context;
      if (prior?.packageModel !== next?.packageModel
        || JSON.stringify(prior?.request) !== JSON.stringify(next?.request)
        || prior?.subject !== next?.subject || prior?.typeIdentity !== next?.typeIdentity
        || prior?.memberFingerprint !== next?.memberFingerprint || prior?.methodToken !== next?.methodToken || prior?.bodySelector !== next?.bodySelector)
        closeExplore();
      context = next;
      if (!next) {
        queueMicrotask(() => { session.cancelCurrent("superseded"); }); pending = false; input = null; failure = null;
        retained = null; closeExplore(); return;
      }
      const key = JSON.stringify(next.request);
      let packages = cache.get(next.packageModel);
      if (!packages) { packages = new Map(); cache.set(next.packageModel, packages); }
      retained = packages.get(key) ?? { inventory: null, readers: new Map(), positions: new Map() };
      packages.set(key, retained);
      while (packages.size > 4) packages.delete(packages.keys().next().value!);
      const member = next.subject === "member" && retained.inventory
        ? memberBodyDestination(retained.inventory, next) : null;
      const operationKey = `${key}:${retained.inventory ? member?.id ?? "settled" : "inventory"}`;
      if (prior?.packageModel !== next.packageModel || input?.key !== operationKey) {
        queueMicrotask(() => { session.cancelCurrent("superseded"); }); pending = false; failure = null; input = null;
        closeExplore();
      }
      if (pending || failure || (retained.inventory && (!member || retained.readers.has(member.id)))) return;
      input = { packageModel: next.packageModel, key: operationKey, retained, member,
        request: member && retained.inventory ? { ...next.request, inventoryId: retained.inventory.id, memberId: member.id } : next.request };
      const prepared = input;
      pending = true;
      queueMicrotask(() => {
        if (input !== prepared) return;
        const start = session.start(prepared, producer);
        if (start.kind === "rejected") {
          pending = false;
          failure = "The Member Body operation could not start.";
          dependencies.render();
        }
      });
    },
    render(): string {
      if (!context) return "";
      const inventory = retained?.inventory;
      let content: string;
      let status = pending ? "Loading Member Body comparison" : "Member Body";
      if (failure) content = renderBodyFailure();
      else if (!inventory) content = '<p role="status">Loading implementation changes…</p>';
      else if (context.subject === "member") {
        content = renderMemberSection()
          || '<p role="status">This Member has API changes but no body comparison.</p>';
      } else {
        status = inventory.isComplete ? "Comparison complete" : "Comparison incomplete";
        content = renderInventorySection(context.subject);
      }
      return renderCompareFrame({ subjectKind: context.subject, subjectLabel: context.subjectLabel,
        mode: "diff", targetText: context.targetText, status, content, externalToolbar: true, escapeHtml: escape });
    },
    bind(root: ParentNode): void {
      root.querySelectorAll<HTMLButtonElement>("[data-member-body-type]").forEach(button =>
        button.addEventListener("click", () => dependencies.activateType(button.dataset.memberBodyType!)));
      root.querySelectorAll<HTMLButtonElement>("[data-member-body-member]").forEach(button =>
        button.addEventListener("click", () => {
          const member = retained?.inventory?.types.flatMap(type => type.members)
            .find(candidate => candidate.id === button.dataset.memberBodyMember);
          if (member) dependencies.activateMember(member);
        }));
      root.querySelector<HTMLButtonElement>("[data-member-body-retry]")?.addEventListener("click", retry);
      const reader = currentReader();
      if (reader) {
        bindReader(root, reader, false);
        root.querySelector<HTMLButtonElement>("[data-member-body-explore]")?.addEventListener("click", () => {
          savePosition(); dialog = dependencies.document.createElement("dialog");
          dialog.className = "member-body-explore";
          dialog.setAttribute("aria-labelledby", "member-body-explore-title");
          dependencies.document.body.append(dialog); paintExplore(reader);
          dialog.addEventListener("cancel", event => { event.preventDefault(); dismissExplore(); });
          dialog.showModal();
        });
      }
      const scroller = root.querySelector<HTMLElement>("[data-member-body-scroll]");
      if (scroller && retained) scroller.scrollTop = currentReader()?.scrollTop ?? retained.positions.get(positionKey()) ?? 0;
    },
    retry,
    dispose(): void { input = null; pending = false; session.dispose(); closeExplore(); },
  };
}
