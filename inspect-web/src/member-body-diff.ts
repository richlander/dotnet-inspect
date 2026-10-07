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
  readonly tools: string;
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
  readonly request: BrowserMemberBodyDiffRequest;
  readonly retained: Retained;
  readonly member: BrowserMemberBodyMember | null;
  readonly key: string;
}

type Session = OperationSession<Input, BrowserMemberBodyDiffResult, unknown, never, never>;

export function memberBodyDestination(
  inventory: BrowserMemberBodyDiffInventory,
  context: Pick<MemberBodyDiffContext, "typeIdentity" | "memberFingerprint" | "methodToken">,
): BrowserMemberBodyMember | null {
  const candidates = inventory.destinations.filter(member => member.typeIdentity === context.typeIdentity && member.fingerprint !== null
    && member.fingerprint === context.memberFingerprint
    && (context.methodToken === null || member.methodToken === context.methodToken));
  return candidates.length === 1 ? candidates[0]! : null;
}

export function renderMemberBodyReader(reader: Pick<Reader, "document" | "medium" | "mode">,
  escapeHtml: (value: unknown) => string): string {
  const document = reader.document;
  const medium = document.media.find(candidate => candidate.medium === reader.medium);
  if (!medium) return '<p role="status">This medium is unavailable.</p>';
  const absent = document.beforeOutcome === "Absent"
    ? '<p class="member-body-side-state">Before: Not present on this side.</p>' : "";
  if (medium.limit) return `${absent}<p role="status">${escapeHtml(medium.limit)}</p>`;
  if (medium.diff) {
    const diff = decodeMemberBodyMappedDiff(medium.diff);
    return `${absent}${diff.changes.length === 0
      ? '<p role="status">Identical</p>'
      : renderSourceDiffViewer(diff, escapeHtml, { mode: reader.mode })}`;
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
              document: result.document, medium: "CSharp", mode: "unified", scrollTop: 0,
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
        if (expanded) paintExplore(reader);
        else { savePosition(); dependencies.render(); }
      }));
    const medium = reader.document.media.find(candidate => candidate.medium === reader.medium);
    if (medium?.diff) bindSourceDiffViewer(root, decodeMemberBodyMappedDiff(medium.diff), {
      mode: reader.mode, onModeChanged(mode) { reader.mode = mode; },
      writeClipboardText: text => dependencies.document.defaultView!.navigator.clipboard.writeText(text),
    });
  }
  function mediaControls(reader: Reader): string {
    return reader.document.media.map(medium =>
      `<button type="button" data-member-body-medium="${escape(medium.medium)}" aria-pressed="${medium.medium === reader.medium}">${medium.medium === "CSharp" ? "C#" : "IL"}</button>`).join("");
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
    get isOpen() { return dialog !== null; },
    reconcile(next: MemberBodyDiffContext | null): void {
      savePosition();
      const prior = context;
      if (prior?.packageModel !== next?.packageModel
        || JSON.stringify(prior?.request) !== JSON.stringify(next?.request)
        || prior?.subject !== next?.subject || prior?.typeIdentity !== next?.typeIdentity
        || prior?.memberFingerprint !== next?.memberFingerprint || prior?.methodToken !== next?.methodToken)
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
      input = { key: operationKey, retained, member,
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
      if (failure) content = `<p role="status">${escape(failure)}</p><button type="button" id="compare-retry">Retry comparison</button>`;
      else if (!inventory) content = '<p role="status">Loading implementation changes…</p>';
      else if (context.subject === "member") {
        const reader = currentReader();
        content = `<section class="member-body-reader"><header class="section-title"><h2>Member Body</h2>${reader ? `${mediaControls(reader)}<button type="button" id="member-body-explore" data-member-body-explore>Explore</button>` : ""}</header><div class="member-body-scroll" data-member-body-scroll>${reader
          ? renderMemberBodyReader(reader, escape)
          : pending ? '<p role="status">Loading exact Member diff…</p>'
            : inventory.isComplete ? '<p role="status">No exact body comparison destination is available for this Member.</p>'
              : '<p role="status">Member comparison is incomplete. No complete result is available.</p>'}</div></section>`;
      } else {
        const types = context.subject === "type"
          ? inventory.types.filter(type => type.identity === context!.typeIdentity) : inventory.types;
        const coverage = inventory.coverage.map(item => `${item.mechanism}: ${item.changed} changed · ${item.exact}/${item.evaluated} exact · ${item.unavailable} unavailable · ${item.incomplete} incomplete · ${item.failed} failed`).join("; ");
        status = inventory.isComplete ? `${types.reduce((count, type) => count + type.members.length, 0)} changed Members` : "Incomplete implementation comparison";
        const rows = context.subject === "library" ? types.map(type => type.canNavigate
          ? `<button type="button" class="library-api-diff-type" data-member-body-type="${escape(type.identity)}">${escape(type.display)} <span>${type.members.length} changed Members</span></button>`
          : `<div class="library-api-diff-type">${escape(type.display)} <span>Removed · ${type.members.length} Members</span></div>`).join("")
          : types.flatMap(type => type.members).map(member => member.fingerprint !== null && member.methodToken !== null
            ? `<button type="button" class="library-api-diff-member" data-member-body-member="${escape(member.id)}">${escape(member.display)} <span>${escape(member.outcome)} · ${escape(member.mechanisms.join(" · "))}</span></button>`
            : `<div class="library-api-diff-member">${escape(member.display)} <span>${escape(member.identityFailure ?? member.outcome)}</span></div>`).join("");
        content = `<p class="member-body-coverage">${escape(coverage)}</p><div class="member-body-inventory member-body-scroll" data-member-body-scroll>${rows || `<p role="status">${inventory.isComplete ? "No implementation changes found" : "No changed rows; comparison coverage is incomplete."}</p>`}</div>`;
      }
      return renderCompareFrame({ subjectKind: context.subject, subjectLabel: context.subjectLabel,
        mode: "diff", targetText: context.targetText, status, content, tools: context.tools, escapeHtml: escape });
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
    retry(): void {
      if (pending) return;
      failure = null; input = null;
      dependencies.render();
    },
    dispose(): void { input = null; pending = false; session.dispose(); closeExplore(); },
  };
}
