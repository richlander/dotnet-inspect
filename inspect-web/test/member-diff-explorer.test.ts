import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserLibraryApiDiffMember,
  BrowserLibraryApiDiffMemberExploreDestination,
  BrowserLibraryApiDiffMemberIdentity,
  BrowserLibraryApiDiffResult,
  BrowserLibraryApiDiffSucceeded,
  BrowserLibraryApiDiffType,
} from "../src/facades/inspect-web-metadata.d.ts";
import type { LibraryApiDiffMemberExploreContext } from "../src/library-api-diff.ts";
import {
  createMemberDiffExplorer,
  memberDiffSourceRequest,
  renderMemberDiffExplorer,
  renderMemberSourceDiff,
  type MemberDiffExplorerSourceState,
} from "../src/member-diff-explorer.ts";
import {
  sourceDiffPayloadDecoder,
  type BrowserSourceComparisonEndpoint,
  type BrowserSourceComparisonRequest,
  type BrowserSourceComparisonResult,
  type BrowserSourceDiff,
} from "../src/source-diff-transport.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import { fakeDom } from "./fake-dom.ts";

function identity(
  fingerprint: string,
  signature: string,
  display: string,
): BrowserLibraryApiDiffMemberIdentity {
  return {
    declaringTypeIdentifier: "Example.Widget",
    stableSelector: "Run",
    canonicalSignature: signature,
    fingerprint,
    typeFullName: "Example.Widget",
    memberName: "Run",
    display,
  };
}

const beforeMember = identity("before-run", "void Run(int)", "Run(int)");
const afterMember = identity("after-run", "void Run(long)", "Run(long)");

function destinationEndpoint(
  version: string,
  member: BrowserLibraryApiDiffMemberIdentity | null,
) {
  return {
    packageId: "Example.Package",
    version,
    framework: "net11.0",
    asset: {
      id: "lib/net11.0/Example.dll",
      path: "lib/net11.0/Example.dll",
      assemblyName: "Example",
    },
    assembly: {
      name: "Example",
      version,
      culture: null,
      publicKeyToken: null,
    },
    member,
  };
}

function destination(
  before: BrowserLibraryApiDiffMemberIdentity | null = beforeMember,
  after: BrowserLibraryApiDiffMemberIdentity | null = afterMember,
): BrowserLibraryApiDiffMemberExploreDestination {
  return {
    kind: "member-diff",
    target: destinationEndpoint("1.0.0", before),
    current: destinationEndpoint("2.0.0", after),
  };
}

function context(
  explore = destination(),
): LibraryApiDiffMemberExploreContext {
  const member: BrowserLibraryApiDiffMember = {
    documentIdentifier: "relation-run",
    pairKind: explore.target.member === null ? "Added" : "Changed",
    role: explore.target.member === null ? "After" : "Both",
    before: explore.target.member,
    after: explore.current.member,
    changes: [{
      kind: explore.target.member === null
        ? "MemberAdded"
        : "MemberSignatureChanged",
      classification: explore.target.member === null
        ? "Additive"
        : "Breaking",
      category: "Signature",
      message: explore.target.member === null
        ? "Member Run() was added."
        : "Parameter type changed from int to long.",
      oldValue: explore.target.member?.canonicalSignature ?? null,
      newValue: explore.current.member?.canonicalSignature ?? null,
    }],
    match: null,
    explore,
  };
  const type: BrowserLibraryApiDiffType = {
    documentIdentifier: "Example.Widget",
    display: "Example.Widget",
    state: "Diff",
    typeDefinitionChanged: false,
    changedMemberCount: 1,
    breakingCount: 1,
    additiveCount: 0,
    potentiallyBreakingCount: 0,
    before: {
      identifier: "Example.Widget",
      namespace: "Example",
      segments: ["Widget"],
      display: "Example.Widget",
    },
    after: {
      identifier: "Example.Widget",
      namespace: "Example",
      segments: ["Widget"],
      display: "Example.Widget",
    },
    members: [member],
    changes: [],
  };
  const document: BrowserLibraryApiDiffSucceeded = {
    libraryIdentifier: "Example",
    libraryDisplay: "Example",
    target: {
      ...destinationEndpoint("1.0.0", null),
      scope: "Public",
      isComplete: true,
      issues: [],
    },
    current: {
      ...destinationEndpoint("2.0.0", null),
      scope: "Public",
      isComplete: true,
      issues: [],
    },
    aggregate: {
      changedTypeCount: 1,
      addedTypeCount: 0,
      removedTypeCount: 0,
      changedMemberCount: 1,
      breakingCount: 1,
      additiveCount: 0,
      potentiallyBreakingCount: 0,
    },
    types: [type],
  };
  const result: BrowserLibraryApiDiffResult = {
    schemaVersion: 1,
    request: {
      schemaVersion: 1,
      packageId: "Example.Package",
      currentVersion: "2.0.0",
      targetVersion: "1.0.0",
      targetFramework: "net11.0",
      compileAssetId: "lib/net11.0/Example.dll",
    },
    kind: "Succeeded",
    value: document,
    unavailable: null,
    rejected: null,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
    inspection: null,
  };
  return {
    packageModel: {},
    result,
    document,
    type,
    member,
    destination: explore,
    subjectLabel: "Example.Widget.Run",
    targetText: "1.0.0 → 2.0.0",
  };
}

function sourceEndpoint(
  version: string,
  state: BrowserSourceComparisonEndpoint["state"] = "Available",
  text: string | null = null,
): BrowserSourceComparisonEndpoint {
  return {
    packageId: "Example.Package",
    version,
    framework: "net11.0",
    assembly: "lib/net11.0/Example.dll",
    assetPath: "lib/net11.0/Example.dll",
    moduleVersionId: null,
    assemblyIdentity: `Example, Version=${version}`,
    memberIdentity: "Example.Widget.Run",
    metadataToken: 100663297,
    state,
    detail: state === "Available" ? null : "Source was not published.",
    text,
    browseUrl: null,
    repositoryUrl: "https://example.test/repository",
    revision: "abc123",
  };
}

function sourceDiff(): BrowserSourceDiff {
  return {
    version: 1,
    before: {
      label: "Before",
      lines: ["public void Run(int value)", "{"],
      finalLineTerminator: "Present",
    },
    after: {
      label: "After",
      lines: ["public void Run(long value)", "{"],
      finalLineTerminator: "Absent",
    },
    relations: [
      {
        kind: "Correspondence",
        beforeCoordinates: [0],
        afterCoordinates: [0],
        content: "Changed",
        placement: "Stable",
      },
      {
        kind: "Correspondence",
        beforeCoordinates: [1],
        afterCoordinates: [1],
        content: "Unchanged",
        placement: "Stable",
      },
    ],
    statistics: {
      added: 0,
      removed: 0,
      changedBefore: 1,
      changedAfter: 1,
      movedBefore: 0,
      movedAfter: 0,
    },
    changes: [{
      before: { start: 0, count: 1 },
      after: { start: 0, count: 1 },
      innerMappings: [{
        before: { line: 0, start: 16, count: 3 },
        after: { line: 0, start: 16, count: 4 },
      }],
      annotations: [{
        text: "Parameter type changed.",
        severity: "Warning",
        targetKind: "Change",
        side: "Both",
        line: null,
        span: null,
      }],
    }],
  };
}

function requestFor(value: LibraryApiDiffMemberExploreContext):
BrowserSourceComparisonRequest {
  return memberDiffSourceRequest(value);
}

function sourceResult(
  value: LibraryApiDiffMemberExploreContext,
  diff: BrowserSourceDiff | null = sourceDiff(),
): BrowserSourceComparisonResult {
  return {
    version: 1,
    kind: "Succeeded",
    value: {
      request: requestFor(value),
      status: diff === null ? "Unavailable" : "Compared",
      isExact: true,
      before: sourceEndpoint("1.0.0"),
      after: sourceEndpoint("2.0.0"),
      diff,
      failure: null,
    },
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
    capacity: null,
  };
}

test("Member Diff Explore renders all three evidence panes from typed evidence", () => {
  const value = context();
  const state: MemberDiffExplorerSourceState = {
    status: "ready",
    result: sourceResult(value),
  };
  const html = renderMemberDiffExplorer(value, state, String);

  assert.match(html, /Member Diff · Changed/);
  assert.match(html, /Run\(long\)/);
  assert.match(html, /What changed/);
  assert.match(html, /Parameter type changed from int to long\./);
  assert.match(html, /Paired declaration evidence is not available yet/);
  assert.match(html, /Exact member match/);
  assert.match(html, /1 Before changed/);
  assert.match(html, /<mark>int<\/mark>/);
  assert.match(html, /<mark>long<\/mark>/);
  assert.match(html, /Parameter type changed\./);
  assert.match(html, /Final line terminators: Before Present; After Absent/);
});

test("unified Source rendering uses transported relations and mappings", () => {
  const html = renderMemberSourceDiff(sourceDiff(), String);

  assert.match(html, /data-side="before" data-line="0"/);
  assert.match(html, /data-side="after" data-line="0"/);
  assert.match(html, /Unchanged correspondence · Stable · 1 Before line ↔ 1 After line/);
  assert.match(html, /Mapped change evidence/);
  assert.match(html, /Before 0:1 → After 0:1/);
  assert.match(html, /Warning/);
});

test("decoded N:M moved correspondence remains one relation with independent populations", () => {
  const value = context();
  const asymmetric: BrowserSourceDiff = {
    version: 1,
    before: {
      label: "Before",
      lines: ["same", "same"],
      finalLineTerminator: "Present",
    },
    after: {
      label: "After",
      lines: ["same"],
      finalLineTerminator: "Present",
    },
    relations: [{
      kind: "Correspondence",
      beforeCoordinates: [0, 1],
      afterCoordinates: [0],
      content: "Unchanged",
      placement: "Moved",
    }],
    statistics: {
      added: 0,
      removed: 0,
      changedBefore: 0,
      changedAfter: 0,
      movedBefore: 2,
      movedAfter: 1,
    },
    changes: [],
  };
  const decoded = sourceDiffPayloadDecoder.decode(JSON.stringify(
    sourceResult(value, asymmetric),
  ));
  assert.equal(decoded.kind, "decoded");
  if (decoded.kind !== "decoded"
    || decoded.value.value?.diff === null
    || decoded.value.value === null) {
    throw new Error("Expected a decoded Source diff.");
  }

  const html = renderMemberSourceDiff(decoded.value.value.diff, String);
  assert.equal(
    (html.match(/class="member-diff-source-relation"/g) ?? []).length,
    1,
  );
  assert.match(
    html,
    /Unchanged correspondence · Moved · 2 Before lines ↔ 1 After line/,
  );
  assert.equal((html.match(/data-side="before"/g) ?? []).length, 2);
  assert.equal((html.match(/data-side="after"/g) ?? []).length, 1);
  assert.match(html, /2 Before moved/);
  assert.match(html, /1 After moved/);
});

test("one-sided destinations omit that Source endpoint intentionally", () => {
  const value = context(destination(null, afterMember));
  const result = sourceResult(value, null);
  if (result.value === null) throw new Error("Expected Source comparison.");
  const oneSided: BrowserSourceComparisonResult = {
    ...result,
    value: {
      ...result.value,
      before: {
        ...sourceEndpoint("1.0.0", "Unrequested"),
        memberIdentity: null,
        metadataToken: null,
        detail: null,
      },
      after: sourceEndpoint(
        "2.0.0",
        "Available",
        "public void Run(long value)",
      ),
    },
  };
  const html = renderMemberDiffExplorer(value, {
    status: "ready",
    result: oneSided,
  }, String);
  assert.match(html, /Member Diff · Added/);
  assert.match(html, /Not present on this side\./);
  assert.match(html, /public void Run\(long value\)/);
  assert.match(html, /paired authored Source comparison is unavailable/);

  const request = requestFor(value);
  assert.equal(request.before, null);
  assert.equal(request.after?.fingerprint, "after-run");
});

test("Source failure, cancellation, and capacity outcomes stay distinct and retryable", () => {
  const value = context();
  const failed = renderMemberDiffExplorer(value, {
    status: "failed",
    error: "Worker stopped.",
  }, String);
  assert.match(failed, /Worker stopped\./);
  assert.match(failed, /Retry Authored Source/);

  const canceled = renderMemberDiffExplorer(value, {
    status: "canceled",
    reason: "Authored Source was canceled.",
  }, String);
  assert.match(canceled, /was canceled/);
  assert.match(canceled, /Retry Authored Source/);

  const capacity = renderMemberDiffExplorer(value, {
    status: "ready",
    result: {
      version: 1,
      kind: "TooComplex",
      value: null,
      failureKind: null,
      error: null,
      diagnostic: null,
      reason: "Too many relations.",
      capacity: {
        dimension: "Relations",
        limit: 2_048,
        actual: 2_049,
      },
    },
  }, String);
  assert.match(capacity, /Relations capacity/);
  assert.match(capacity, /2,049 observed, 2,048 allowed/);
  assert.match(capacity, /Retry Authored Source/);
});

test("Authored Source links admit only safe external destinations", () => {
  const value = context();
  const result = sourceResult(value);
  if (result.value === null) throw new Error("Expected Source comparison.");
  const unsafe: BrowserSourceComparisonResult = {
    ...result,
    value: {
      ...result.value,
      before: {
        ...result.value.before,
        browseUrl: "javascript:alert(1)",
      },
      after: {
        ...result.value.after,
        browseUrl: "https://example.test/source.cs",
      },
    },
  };
  const html = renderMemberDiffExplorer(value, {
    status: "ready",
    result: unsafe,
  }, String);
  assert.doesNotMatch(html, /javascript:/);
  assert.match(html, /href="https:\/\/example\.test\/source\.cs"/);
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => {
    resolve = accept;
  });
  return { promise, resolve };
}

interface FakeDialog {
  open: boolean;
  removed: boolean;
  innerHTML: string;
  closeHandlers: EventListener[];
  keydownHandlers: EventListener[];
}

function dialogHarness() {
  const dialogs: FakeDialog[] = [];
  const document = fakeDom.document({
    body: {
      append: () => undefined,
    },
    createElement: () => {
      const closeHandlers: EventListener[] = [];
      const keydownHandlers: EventListener[] = [];
      const dialog: FakeDialog = {
        open: false,
        removed: false,
        innerHTML: "",
        closeHandlers,
        keydownHandlers,
      };
      const closeButton = {
        focus: () => undefined,
        addEventListener: (
          type: string,
          listener: EventListenerOrEventListenerObject,
        ) => {
          if (type === "click" && typeof listener === "function")
            closeHandlers.push(listener);
        },
      };
      Object.assign(dialog, {
        className: "",
        setAttribute: () => undefined,
        addEventListener: (
          type: string,
          listener: EventListenerOrEventListenerObject,
        ) => {
          if (type === "keydown" && typeof listener === "function") {
            keydownHandlers.push(listener);
          }
        },
        querySelector: (selector: string) =>
          selector === "[data-member-diff-close]" ? closeButton : null,
        querySelectorAll: () => [],
        focus: () => undefined,
        showModal: () => {
          dialog.open = true;
        },
        close: () => {
          dialog.open = false;
        },
        remove: () => {
          dialog.removed = true;
        },
      });
      dialogs.push(dialog);
      return dialog;
    },
  });
  return { document, dialogs };
}

test("non-Tab keys preserve native dialog button activation", () => {
  const dom = dialogHarness();
  const controller = createMemberDiffExplorer({
    document: dom.document,
    operationAuthority: createOperationAuthorityPage(),
    query: () => new Promise<BrowserSourceComparisonResult>(() => undefined),
    cancel: () => undefined,
    describeError: error => error instanceof Error ? error.message : String(error),
    escapeHtml: String,
    reportOperationDiagnostic: () => undefined,
  });
  controller.open(context(), fakeDom.htmlElement({
    isConnected: true,
    focus: () => undefined,
  }));
  const keydown = dom.dialogs[0]?.keydownHandlers.at(-1);
  if (keydown === undefined) throw new Error("Expected a dialog keydown binding.");
  let prevented = 0;
  keydown(fakeDom.keyboardEvent({
    key: "Enter",
    preventDefault: () => prevented++,
    stopPropagation: () => undefined,
  }));
  assert.equal(prevented, 0);
  controller.dispose();
});

test("replacement cancels pending Source and suppresses its late result", async () => {
  const dom = dialogHarness();
  const pending = deferred<BrowserSourceComparisonResult>();
  const requests: BrowserSourceComparisonRequest[] = [];
  const cancellations: string[] = [];
  let fallbackFocus = 0;
  const controller = createMemberDiffExplorer({
    document: dom.document,
    operationAuthority: createOperationAuthorityPage(),
    query: (_operationId, request) => {
      requests.push(request);
      return pending.promise;
    },
    cancel: (_operationId, reason) => cancellations.push(reason),
    describeError: error => error instanceof Error ? error.message : String(error),
    escapeHtml: String,
    reportOperationDiagnostic: () => undefined,
  });

  controller.open(context(), fakeDom.htmlElement({
    isConnected: true,
    focus: () => undefined,
  }));
  assert.equal(requests.length, 1);
  assert.equal(controller.isOpen, true);

  assert.equal(controller.reconcile(null), true);
  assert.deepEqual(cancellations, ["superseded"]);
  assert.equal(dom.dialogs[0]?.removed, true);
  controller.afterRender(fakeDom.htmlElement({
    focus: () => fallbackFocus++,
  }));
  assert.equal(fallbackFocus, 1);

  pending.resolve(sourceResult(context()));
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(controller.isOpen, false);
  assert.equal(dom.dialogs.length, 1);
});

test("a settled non-failed Source result is retained for the same exact context", async () => {
  const dom = dialogHarness();
  const value = context();
  let queries = 0;
  let invokerFocus = 0;
  const controller = createMemberDiffExplorer({
    document: dom.document,
    operationAuthority: createOperationAuthorityPage(),
    query: () => {
      queries++;
      return Promise.resolve(sourceResult(value));
    },
    cancel: () => undefined,
    describeError: error => error instanceof Error ? error.message : String(error),
    escapeHtml: String,
    reportOperationDiagnostic: () => undefined,
  });
  const invoker = fakeDom.htmlElement({
    isConnected: true,
    focus: () => invokerFocus++,
  });

  controller.open(value, invoker);
  await Promise.resolve();
  await Promise.resolve();
  const firstDialog = dom.dialogs[0];
  const close = firstDialog?.closeHandlers.at(-1);
  if (close === undefined) throw new Error("Expected a Close binding.");
  close(fakeDom.event());
  assert.equal(invokerFocus, 1);

  controller.open(value, invoker);
  assert.equal(queries, 1);
  assert.match(dom.dialogs[1]?.innerHTML ?? "", /Exact member match/);
  controller.dispose();
});
