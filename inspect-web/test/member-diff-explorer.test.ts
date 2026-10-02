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
  renderInlineMemberSourceDiff,
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
    schemaVersion: 2,
    request: {
      schemaVersion: 2,
      packageId: "Example.Package",
      currentVersion: "2.0.0",
      targetVersion: "1.0.0",
      targetFramework: "net11.0",
      compileAssetId: "lib/net11.0/Example.dll",
      surface: "Library",
      analyses: ["api"],
      views: "Changes",
      typeNames: [],
      memberTargetIdentities: [],
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
  isExact = false,
): BrowserSourceComparisonResult {
  return {
    version: 1,
    kind: "Succeeded",
    value: {
      request: requestFor(value),
      status: diff === null ? "Unavailable" : "Compared",
      isExact: diff !== null && isExact,
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

test("Member Diff Explore renders one full-width authored Source diff", () => {
  const value = context();
  const state: MemberDiffExplorerSourceState = {
    status: "ready",
    result: sourceResult(value),
  };
  const html = renderMemberDiffExplorer(value, state, String);
  assert.match(html, /Member Diff · Changed/);
  assert.match(html, /Run\(long\)/);
  assert.match(html, /Run\(long\)/);
  assert.match(html, /data-member-diff-mode="text"/);
  assert.match(html, /1 Before changed/);
  assert.match(html, /<mark>int<\/mark>/);
  assert.match(html, /<mark>long<\/mark>/);
  assert.match(html, /No newline at end/);
  assert.match(html, /Copy Before/);
  assert.match(html, /Side by side/);
  assert.match(html, /code-evidence-viewer-content member-diff-explorer-content/);
  assert.match(html, /code-evidence-viewer-rail member-diff-explorer-rail/);
  assert.match(html, /data-member-diff-mode="text"[\s\S]*Authored Source[\s\S]*Changed/);
  assert.doesNotMatch(
    html,
    /What changed|Declaration|Parameter type changed|Authored Source changed|Final line terminators|Mapped change evidence/,
  );
  const rail = html.indexOf("code-evidence-viewer-rail");
  const endpoint = html.indexOf("member-diff-source-endpoint");
  assert.ok(rail >= 0 && rail < endpoint);
});

test("unified Source rendering walks mapped changes in positional order", () => {
  const html = renderMemberSourceDiff(sourceDiff(), String, true);

  assert.match(html, /data-row-kind="removal" data-before-line="0"/);
  assert.match(html, /data-row-kind="addition" data-before-line="" data-after-line="0"/);
  assert.match(html, /data-row-kind="context" data-before-line="1" data-after-line="1"/);
  assert.doesNotMatch(html, /Mapped change evidence|Before 0:1 → After 0:1|Warning/);
  assert.equal(
    (html.match(/data-relation-content="Changed" data-relation-placement="Stable"/g) ?? []).length,
    2,
  );
  assert.equal(
    (html.match(/data-relation-content="Unchanged" data-relation-placement="Stable"/g) ?? []).length,
    1,
  );
});

test("a final-newline-only change identifies the affected side", () => {
  const diff: BrowserSourceDiff = {
    version: 1,
    before: {
      label: "Before",
      lines: ["public void Run()"],
      finalLineTerminator: "Present",
    },
    after: {
      label: "After",
      lines: ["public void Run()"],
      finalLineTerminator: "Absent",
    },
    relations: [],
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
      innerMappings: [],
      annotations: [],
    }],
  };

  const html = renderMemberSourceDiff(diff, String);

  assert.equal(
    (html.match(/data-final-line-terminator="after"/g) ?? []).length,
    2,
  );
  const removal = html.indexOf('data-row-kind="removal"');
  const addition = html.indexOf('data-row-kind="addition"');
  const marker = html.indexOf('data-final-line-terminator="after"');
  assert.ok(removal >= 0 && removal < addition && addition < marker);
  assert.ok(marker < html.indexOf("</div>", addition));
});

test("a shared final row with no terminator renders one marker", () => {
  const diff: BrowserSourceDiff = {
    version: 1,
    before: {
      label: "Before",
      lines: ["public void Run()"],
      finalLineTerminator: "Absent",
    },
    after: {
      label: "After",
      lines: ["public void Run()"],
      finalLineTerminator: "Absent",
    },
    relations: [],
    statistics: {
      added: 0,
      removed: 0,
      changedBefore: 0,
      changedAfter: 0,
      movedBefore: 0,
      movedAfter: 0,
    },
    changes: [],
  };

  const html = renderMemberSourceDiff(diff, String);

  assert.equal(
    (html.match(/data-final-line-terminator="both"/g) ?? []).length,
    1,
  );
  assert.equal(
    (html.match(/data-final-line-terminator="before"/g) ?? []).length,
    1,
  );
  assert.equal(
    (html.match(/data-final-line-terminator="after"/g) ?? []).length,
    1,
  );
  assert.match(html, /data-row-kind="context"/);
  assert.match(
    html,
    /data-final-line-terminator="both"[^>]*>No newline at end/,
  );
});

test("a middle insertion renders once between its surrounding context", () => {
  const diff: BrowserSourceDiff = {
    version: 1,
    before: {
      label: "Before",
      lines: ["a", "c"],
      finalLineTerminator: "Present",
    },
    after: {
      label: "After",
      lines: ["a", "b", "c"],
      finalLineTerminator: "Present",
    },
    relations: [],
    statistics: {
      added: 1,
      removed: 0,
      changedBefore: 0,
      changedAfter: 0,
      movedBefore: 0,
      movedAfter: 0,
    },
    changes: [{
      before: { start: 1, count: 0 },
      after: { start: 1, count: 1 },
      innerMappings: [],
      annotations: [],
    }],
  };
  const html = renderMemberSourceDiff(diff, String, true);
  const first = html.indexOf(
    'data-row-kind="context" data-before-line="0" data-after-line="0"',
  );
  const insertion = html.indexOf(
    'data-row-kind="addition" data-before-line="" data-after-line="1"',
  );
  const last = html.indexOf(
    'data-row-kind="context" data-before-line="1" data-after-line="2"',
  );
  assert.ok(first >= 0 && first < insertion && insertion < last);
  assert.equal((html.match(/<code role="cell">a<\/code>/g) ?? []).length, 1);
  assert.equal((html.match(/<code role="cell">b<\/code>/g) ?? []).length, 1);
  assert.equal((html.match(/<code role="cell">c<\/code>/g) ?? []).length, 1);
  assert.doesNotMatch(html, /member-diff-source-relations/);
});

test("decoded N:M correspondence cannot override mapped presentation order", () => {
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
      added: 1,
      removed: 2,
      changedBefore: 2,
      changedAfter: 1,
      movedBefore: 2,
      movedAfter: 1,
    },
    changes: [{
      before: { start: 0, count: 2 },
      after: { start: 0, count: 1 },
      innerMappings: [],
      annotations: [],
    }],
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

  const html = renderMemberSourceDiff(
    decoded.value.value.diff,
    String,
    true,
  );
  assert.equal((html.match(/data-row-kind="removal"/g) ?? []).length, 2);
  assert.equal((html.match(/data-row-kind="addition"/g) ?? []).length, 1);
  assert.equal(
    (html.match(/data-relation-content="Unchanged" data-relation-placement="Moved"/g) ?? []).length,
    3,
  );
  assert.equal(
    (html.match(/>Unchanged · Moved<\/span>/g) ?? []).length,
    3,
  );
});

test("side-by-side Source rendering top-aligns asymmetric changed blocks", () => {
  const diff: BrowserSourceDiff = {
    version: 1,
    before: {
      label: "Before",
      lines: ["old 1", "old 2"],
      finalLineTerminator: "Absent",
    },
    after: {
      label: "After",
      lines: ["new 1", "new 2", "new 3"],
      finalLineTerminator: "Absent",
    },
    relations: [],
    statistics: {
      added: 3,
      removed: 2,
      changedBefore: 2,
      changedAfter: 3,
      movedBefore: 0,
      movedAfter: 0,
    },
    changes: [{
      before: { start: 0, count: 2 },
      after: { start: 0, count: 3 },
      innerMappings: [],
      annotations: [],
    }],
  };

  const html = renderMemberSourceDiff(
    diff,
    String,
    false,
    "side-by-side",
  );
  assert.match(html, /data-mode="side-by-side"/);
  assert.equal(
    (html.match(/class="source-diff-viewer-split-row source-diff-viewer-row-change"/g) ?? []).length,
    3,
  );
  assert.equal(
    (html.match(/class="source-diff-viewer-split-empty"/g) ?? []).length,
    1,
  );
  assert.match(
    html,
    /class="source-diff-viewer-split-empty" role="cell" aria-colindex="1">[\s\S]*Before; no line/,
  );
  assert.match(
    html,
    /class="source-diff-viewer-split-source" role="cell" aria-colindex="2">[\s\S]*After; Added; line 3/,
  );
  assert.match(html, /aria-colcount="2"/);
  assert.equal(
    (html.match(/class="source-diff-viewer-terminator"/g) ?? []).length,
    4,
  );
});

test("invalid intraline mappings preserve rows and report the defect", () => {
  const diff = sourceDiff();
  const invalid: BrowserSourceDiff = {
    ...diff,
    changes: [{
      ...diff.changes[0]!,
      innerMappings: [
        {
          before: { line: 0, start: 16, count: 3 },
          after: { line: 0, start: 16, count: 4 },
        },
        {
          before: { line: 0, start: 17, count: 2 },
          after: { line: 0, start: 18, count: 1 },
        },
      ],
    }],
  };

  const html = renderMemberSourceDiff(invalid, String);
  assert.match(html, /Some intraline highlights could not be shown/);
  assert.match(html, /invalid before intraline mappings on line 1/);
  assert.doesNotMatch(html, /<mark>int<\/mark>/);
  assert.match(html, /public void Run\(int value\)/);
});

test("inline Member Diff keeps source work explicit", () => {
  const html = renderInlineMemberSourceDiff(
    context(),
    { status: "idle" },
    String,
  );
  assert.match(html, /Authored Source/);
  assert.match(html, /Show authored Source diff/);
  assert.doesNotMatch(html, /Loading authored Source/);
});

test("inline one-sided Member Diff preserves concise endpoint outcomes", () => {
  const value = context(destination(null, afterMember));
  const idle = renderInlineMemberSourceDiff(
    value,
    { status: "idle" },
    String,
  );
  assert.match(idle, /<strong>Before<\/strong>: Not present on this side\./);
  assert.match(idle, /Show authored Source diff/);
  assert.doesNotMatch(idle, /member-diff-source-endpoint/);

  const loading = renderInlineMemberSourceDiff(
    value,
    { status: "loading" },
    String,
  );
  assert.match(
    loading,
    /<strong>Before<\/strong>: Not present on this side\./,
  );
  assert.match(loading, /Loading authored Source/);
  assert.doesNotMatch(loading, /member-diff-source-endpoint/);

  const result = sourceResult(value, null);
  if (result.value === null) throw new Error("Expected Source comparison.");
  const ready = renderInlineMemberSourceDiff(
    value,
    {
      status: "ready",
      result: {
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
      },
    },
    String,
  );
  assert.match(ready, /<strong>Before<\/strong>: Not present on this side\./);
  assert.match(ready, /paired authored Source comparison is unavailable/);
  assert.doesNotMatch(
    ready,
    /member-diff-source-endpoint|public void Run\(long value\)/,
  );
});

test("inline Member Diff renders typed Source unavailability without cards", () => {
  const value = context();
  const result = sourceResult(value, null);
  if (result.value === null) throw new Error("Expected Source comparison.");
  const html = renderInlineMemberSourceDiff(
    value,
    {
      status: "ready",
      result: {
        ...result,
        value: {
          ...result.value,
          before: sourceEndpoint("1.0.0", "Unavailable"),
        },
      },
    },
    String,
  );

  assert.match(html, /<strong>Before<\/strong>: Source was not published\./);
  assert.doesNotMatch(html, /member-diff-source-endpoint/);
});

test("inline Member Diff renders only the authored Source document", () => {
  const value = context();
  const html = renderInlineMemberSourceDiff(
    value,
    { status: "ready", result: sourceResult(value) },
    String,
  );

  const diff = html.indexOf("member-diff-source-diff");
  assert.ok(diff >= 0);
  assert.doesNotMatch(
    html,
    /Authored Source changed|Source evidence|member-diff-source-endpoints|Source diff statistics|Final line terminators|No newline at end|Mapped change evidence/,
  );
});

test("inline Member Diff starts the shared comparison only on request", () => {
  const dom = dialogHarness();
  const value = context();
  let queries = 0;
  let renders = 0;
  const showHandlers: EventListener[] = [];
  const controller = createMemberDiffExplorer({
    document: dom.document,
    operationAuthority: createOperationAuthorityPage(),
    query: () => {
      queries++;
      return new Promise<BrowserSourceComparisonResult>(() => undefined);
    },
    cancel: () => undefined,
    describeError: error => error instanceof Error ? error.message : String(error),
    escapeHtml: String,
    reportOperationDiagnostic: () => undefined,
    renderPage: () => renders++,
  });
  controller.reconcile(value);
  const showButton = fakeDom.htmlElement({
    addEventListener: (
      type: string,
      listener: EventListenerOrEventListenerObject,
    ) => {
      if (type === "click" && typeof listener === "function")
        showHandlers.push(listener);
    },
  });
  controller.bindInline(fakeDom.parentNode({
    querySelector: (selector: string) =>
      selector === "[data-member-diff-source-show]"
      ? showButton
      : null,
  }));

  assert.equal(queries, 0);
  showHandlers[0]?.(fakeDom.event());
  assert.equal(queries, 1);
  assert.equal(renders, 1);
  controller.dispose();
});

test("identical Source does not upgrade a soft Member correspondence", () => {
  const value = context();
  const softContext: LibraryApiDiffMemberExploreContext = {
    ...value,
    member: {
      ...value.member,
      match: {
        tier: "signature",
        confidence: 80,
      },
    },
  };
  const exactDiff: BrowserSourceDiff = {
    version: 1,
    before: {
      label: "Before",
      lines: ["public void Run()"],
      finalLineTerminator: "Present",
    },
    after: {
      label: "After",
      lines: ["public void Run()"],
      finalLineTerminator: "Present",
    },
    relations: [{
      kind: "Correspondence",
      beforeCoordinates: [0],
      afterCoordinates: [0],
      content: "Unchanged",
      placement: "Stable",
    }],
    statistics: {
      added: 0,
      removed: 0,
      changedBefore: 0,
      changedAfter: 0,
      movedBefore: 0,
      movedAfter: 0,
    },
    changes: [],
  };

  const html = renderMemberDiffExplorer(softContext, {
    status: "ready",
    result: sourceResult(softContext, exactDiff, true),
  }, String);
  assert.match(html, /Member Diff · Changed/);
  assert.match(html, /Authored Source is identical/);
  assert.doesNotMatch(html, /Exact member match/);
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
  retryHandlers: EventListener[];
  retryButton: HTMLElement;
  sourceContent: HTMLElement;
}

function dialogHarness() {
  const dialogs: FakeDialog[] = [];
  let activeElement: unknown = null;
  let document: Document;
  document = fakeDom.document({
    get activeElement() {
      return activeElement;
    },
    body: {
      append: () => undefined,
    },
    createElement: () => {
      const closeHandlers: EventListener[] = [];
      const keydownHandlers: EventListener[] = [];
      const retryHandlers: EventListener[] = [];
      const focusable = (
        handlers: EventListener[] | null = null,
      ): HTMLElement => {
        let element: HTMLElement;
        element = fakeDom.htmlElement({
          focus: () => {
            activeElement = element;
          },
          addEventListener: (
            type: string,
            listener: EventListenerOrEventListenerObject,
          ) => {
            if (type === "click"
              && handlers !== null
              && typeof listener === "function") {
              handlers.push(listener);
            }
          },
        });
        return element;
      };
      const closeButton = focusable(closeHandlers);
      const retryButton = focusable(retryHandlers);
      const sourceContent = focusable();
      const heading = focusable();
      const dialog: FakeDialog = {
        open: false,
        removed: false,
        innerHTML: "",
        closeHandlers,
        keydownHandlers,
        retryHandlers,
        retryButton,
        sourceContent,
      };
      Object.assign(dialog, {
        className: "",
        ownerDocument: document,
        setAttribute: () => undefined,
        addEventListener: (
          type: string,
          listener: EventListenerOrEventListenerObject,
        ) => {
          if (type === "keydown" && typeof listener === "function") {
            keydownHandlers.push(listener);
          }
        },
        querySelector: (selector: string) => {
          if (selector === "[data-member-diff-close]") return closeButton;
          if (selector === "#member-diff-explorer-title") return heading;
          if (selector === '[data-member-diff-mode="text"]')
            return sourceContent;
          if (selector === "[data-member-diff-source-retry]"
            && dialog.innerHTML.includes("data-member-diff-source-retry")) {
            return retryButton;
          }
          return null;
        },
        querySelectorAll: () => [],
        contains: (candidate: unknown) =>
          candidate === closeButton
          || candidate === retryButton
          || candidate === sourceContent
          || candidate === heading,
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
  return {
    document,
    dialogs,
    activeElement: () => activeElement,
  };
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
    renderPage: () => undefined,
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

test("retry transition keeps focus inside the dialog", async () => {
  const dom = dialogHarness();
  const pending = deferred<BrowserSourceComparisonResult>();
  let queryCount = 0;
  const controller = createMemberDiffExplorer({
    document: dom.document,
    operationAuthority: createOperationAuthorityPage(),
    query: () => {
      queryCount++;
      return queryCount === 1
        ? Promise.resolve({
            version: 1,
            kind: "Failed",
            value: null,
            failureKind: "Expected",
            error: "Source was unavailable.",
            diagnostic: null,
            reason: null,
            capacity: null,
          })
        : pending.promise;
    },
    cancel: () => undefined,
    describeError: error => error instanceof Error ? error.message : String(error),
    escapeHtml: String,
    reportOperationDiagnostic: () => undefined,
    renderPage: () => undefined,
  });
  controller.open(context(), fakeDom.htmlElement({
    isConnected: true,
    focus: () => undefined,
  }));
  await Promise.resolve();
  await Promise.resolve();

  const dialog = dom.dialogs[0];
  if (dialog === undefined) throw new Error("Expected a dialog.");
  dialog.retryButton.focus();
  assert.equal(dom.activeElement(), dialog.retryButton);
  const retry = dialog.retryHandlers.at(-1);
  if (retry === undefined) throw new Error("Expected a Retry binding.");
  retry(fakeDom.event());

  assert.equal(queryCount, 2);
  assert.equal(dom.activeElement(), dialog.sourceContent);
  controller.dispose();
});

test("reopening Explore retries a failed Source comparison", async () => {
  const dom = dialogHarness();
  const value = context();
  const pending = deferred<BrowserSourceComparisonResult>();
  let queries = 0;
  const controller = createMemberDiffExplorer({
    document: dom.document,
    operationAuthority: createOperationAuthorityPage(),
    query: () => {
      queries++;
      return queries === 1
        ? Promise.resolve({
            version: 1,
            kind: "Failed",
            value: null,
            failureKind: "Expected",
            error: "Source was unavailable.",
            diagnostic: null,
            reason: null,
            capacity: null,
          })
        : pending.promise;
    },
    cancel: () => undefined,
    describeError: error => error instanceof Error ? error.message : String(error),
    escapeHtml: String,
    reportOperationDiagnostic: () => undefined,
    renderPage: () => undefined,
  });
  const invoker = fakeDom.htmlElement({
    isConnected: true,
    focus: () => undefined,
  });

  controller.open(value, invoker);
  await Promise.resolve();
  await Promise.resolve();
  const close = dom.dialogs[0]?.closeHandlers.at(-1);
  if (close === undefined) throw new Error("Expected a Close binding.");
  close(fakeDom.event());

  controller.open(value, invoker);
  assert.equal(queries, 2);

  pending.resolve(sourceResult(value));
  await Promise.resolve();
  await Promise.resolve();
  assert.match(dom.dialogs[1]?.innerHTML ?? "", /member-diff-source-diff/);
  controller.dispose();
});

test("closing Explore keeps a pending inline Source comparison alive", async () => {
  const dom = dialogHarness();
  const value = context();
  const pending = deferred<BrowserSourceComparisonResult>();
  const cancellations: string[] = [];
  const controller = createMemberDiffExplorer({
    document: dom.document,
    operationAuthority: createOperationAuthorityPage(),
    query: () => pending.promise,
    cancel: (_operationId, reason) => cancellations.push(reason),
    describeError: error => error instanceof Error ? error.message : String(error),
    escapeHtml: String,
    reportOperationDiagnostic: () => undefined,
    renderPage: () => undefined,
  });
  controller.open(value, fakeDom.htmlElement({
    isConnected: true,
    focus: () => undefined,
  }));

  const close = dom.dialogs[0]?.closeHandlers.at(-1);
  if (close === undefined) throw new Error("Expected a Close binding.");
  close(fakeDom.event());
  assert.deepEqual(cancellations, []);

  pending.resolve(sourceResult(value));
  await Promise.resolve();
  await Promise.resolve();
  assert.match(controller.renderInline(value), /member-diff-source-diff/);
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
    renderPage: () => undefined,
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
    renderPage: () => undefined,
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

  const replacement: LibraryApiDiffMemberExploreContext = {
    ...value,
    result: { ...value.result },
    member: { ...value.member },
    destination: {
      ...value.destination,
      target: { ...value.destination.target },
      current: { ...value.destination.current },
    },
  };
  controller.open(replacement, invoker);
  assert.equal(queries, 1);
  assert.match(dom.dialogs[1]?.innerHTML ?? "", /member-diff-source-diff/);
  controller.dispose();
});
