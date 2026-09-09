import assert from "node:assert/strict";
import test from "node:test";

import {
  captureLibraryApiDiffSelection,
  renderLibraryApiDiffSurface,
  restoreLibraryApiDiffSelection,
  type LibraryApiDiffViewOptions,
} from "../src/library-api-diff-view.ts";
import { createLibraryApiDiffState, type LibraryApiDiffState } from "../src/library-api-diff.ts";
import type {
  BrowserLibraryApiDiff,
  BrowserLibraryApiDiffRequest,
  BrowserLibraryApiTypeSubject,
} from "../src/facades/inspect-web-metadata.d.ts";
import { fakeDom } from "./fake-dom.ts";

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const request: BrowserLibraryApiDiffRequest = {
  packageId: "Example.Package",
  version: "2.0.0",
  framework: "net10.0",
  comparisonVersion: "1.0.0",
  assembly: "Example.Package",
};

function endpoint(overrides: Partial<BrowserLibraryApiDiff["before"]> = {}): BrowserLibraryApiDiff["before"] {
  return {
    identity: { name: "Example.Package", version: "1.0.0.0", culture: null, publicKeyToken: null },
    scope: "Public",
    isComplete: true,
    issues: [],
    ...overrides,
  };
}

function widgetSubject(overrides: Partial<BrowserLibraryApiTypeSubject> = {}): BrowserLibraryApiTypeSubject {
  return {
    identifier: "T:Example.Widget",
    display: "Example.Widget",
    change: "Diff",
    typeDiff: {
      before: { identifier: "T:Example.Widget", display: "Example.Widget" },
      after: { identifier: "T:Example.Widget", display: "Example.Widget" },
      pairKind: "Changed",
      typeDefinitionChanged: false,
      compatibilityChanges: [{
        kind: "MemberSignatureChanged",
        classification: "Breaking",
        category: "Signature",
        message: "Signature changed.",
        oldValue: "void M()",
        newValue: "void M(int x)",
        subject: {
          kind: "Member",
          beforeType: null,
          afterType: null,
          beforeMember: {
            declaringType: { identifier: "T:Example.Widget", display: "Example.Widget" },
            anchor: {
              stableSelector: "M()", canonicalSignature: "void M()",
              fingerprint: "abc", typeFullName: "Example.Widget", memberName: "M",
            },
            display: "M()",
          },
          afterMember: {
            declaringType: { identifier: "T:Example.Widget", display: "Example.Widget" },
            anchor: {
              stableSelector: "M(Int32)", canonicalSignature: "void M(int x)",
              fingerprint: "def", typeFullName: "Example.Widget", memberName: "M",
            },
            display: "M(int x)",
          },
        },
      }],
      members: [{
        relation: {
          identifier: "library-api-member-relation.v1|Example.Widget.M()|0",
          pairKind: "Changed",
          before: {
            declaringType: { identifier: "T:Example.Widget", display: "Example.Widget" },
            anchor: {
              stableSelector: "M()", canonicalSignature: "void M()",
              fingerprint: "abc", typeFullName: "Example.Widget", memberName: "M",
            },
            display: "M()",
          },
          after: {
            declaringType: { identifier: "T:Example.Widget", display: "Example.Widget" },
            anchor: {
              stableSelector: "M(Int32)", canonicalSignature: "void M(int x)",
              fingerprint: "def", typeFullName: "Example.Widget", memberName: "M",
            },
            display: "M(int x)",
          },
          match: { tierId: "extension-instance", tierConfidence: 80, confidence: 80 },
        },
        role: "Both",
      }],
      breakingCount: 1, additiveCount: 0, potentiallyBreakingCount: 0, changedMemberCount: 1,
    },
    ...overrides,
  };
}

function available(subjects: readonly BrowserLibraryApiTypeSubject[]): BrowserLibraryApiDiff {
  return {
    request,
    kind: "Available",
    before: endpoint(),
    after: endpoint({ identity: { name: "Example.Package", version: "2.0.0.0", culture: null, publicKeyToken: null } }),
    summary: {
      changedTypeCount: subjects.length, addedTypeCount: 0, removedTypeCount: 0,
      changedMemberCount: subjects.length, breakingCount: subjects.length,
      additiveCount: 0, potentiallyBreakingCount: 0,
    },
    document: { identifier: "library-api.v1|Example.Package", display: "Example.Package", subjects },
    unavailableKind: null,
    rejectionKind: null,
  };
}

function view(overrides: Partial<LibraryApiDiffViewOptions> = {}): string {
  const state: LibraryApiDiffState = { ...createLibraryApiDiffState(), ...overrides.state };
  return renderLibraryApiDiffSurface({
    libraryName: "Example.Package",
    assemblyIdentity: "Example.Package, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null",
    assetPath: "lib/net10.0/Example.Package.dll",
    coordinate: "net10.0 · Example.Package@2.0.0",
    requireLibrary: false,
    escapeHtml,
    ...overrides,
    state,
  });
}

test("no library selected is a distinct empty state with no Change target control", () => {
  const html = view({ requireLibrary: true });
  assert.match(html, /Pick a library to compare/);
  assert.doesNotMatch(html, /data-library-api-diff-action="change-target"/);
});

test("a pending, failed, or predecessor-free target is a distinct no-request state", () => {
  const html = view({ state: { ...createLibraryApiDiffState(), noRequestReason: "No earlier listed version is available." } });
  assert.match(html, /data-library-api-diff-state="no-request"/);
  assert.match(html, /No earlier listed version is available\./);
  assert.match(html, /data-library-api-diff-action="change-target"/);
  assert.doesNotMatch(html, /data-library-api-diff-state="loading"/);
});

test("loading is distinct from every other state", () => {
  const html = view({ state: { ...createLibraryApiDiffState(), request, loading: true } });
  assert.match(html, /data-library-api-diff-state="loading"/);
  assert.match(html, /Comparing the public API surface/);
});

test("an expected or unexpected managed failure is a distinct failed state with Retry", () => {
  const html = view({ state: { ...createLibraryApiDiffState(), error: "Public API comparison failed." } });
  assert.match(html, /data-library-api-diff-state="failed"/);
  assert.match(html, /Public API comparison failed\./);
  assert.match(html, /data-library-api-diff-action="retry"/);
});

test("cancellation is a distinct state with Retry, not a failure or an empty success", () => {
  const html = view({ state: { ...createLibraryApiDiffState(), canceledReason: "superseded" } });
  assert.match(html, /data-library-api-diff-state="canceled"/);
  assert.match(html, /canceled \(superseded\)/);
  assert.match(html, /data-library-api-diff-action="retry"/);
});

test("Rejected is distinct from Unavailable and from a successful empty document", () => {
  const rejected: BrowserLibraryApiDiff = {
    request, kind: "Rejected", before: endpoint(), after: endpoint(),
    summary: null, document: null, unavailableKind: null,
    rejectionKind: "MissingExactTypeIdentity",
  };
  const html = view({ state: { ...createLibraryApiDiffState(), request, result: rejected } });
  assert.match(html, /data-library-api-diff-state="rejected"/);
  assert.match(html, /MissingExactTypeIdentity/);
  assert.doesNotMatch(html, /No public API changes/);
});

test("Unavailable is 'Not compared', never equality or a one-sided inventory", () => {
  const unavailable: BrowserLibraryApiDiff = {
    request, kind: "Unavailable", before: endpoint({ isComplete: false, issues: [{
      kind: "Truncated", detail: null, openFailureKind: null, metadataRootReason: null, count: null,
      truncation: {
        limit: "Types", bound: 4000, projectedParticipants: 1, omittedParticipants: 0,
        projectedTypes: 4000, projectedMembers: 0, projectedInspectionFailures: 0,
        projectedTypeForwarders: 0, inspectedMetadataRows: 0, projectedRetainedTextCharacters: 0,
      },
    }] }), after: endpoint(),
    summary: null, document: null, unavailableKind: "BeforeIncomplete", rejectionKind: null,
  };
  const html = view({ state: { ...createLibraryApiDiffState(), request, result: unavailable } });
  assert.match(html, /data-library-api-diff-state="unavailable"/);
  assert.match(html, /Not compared: BeforeIncomplete\./);
  assert.match(html, /data-library-api-diff-endpoint="before"/);
  assert.match(html, /data-library-api-diff-issue="Truncated"/);
  assert.doesNotMatch(html, /No public API changes/);
});

test("a successful empty document is 'No public API changes', distinct from Unavailable", () => {
  const html = view({ state: { ...createLibraryApiDiffState(), request, result: available([]) } });
  assert.match(html, /data-library-api-diff-state="available"/);
  assert.match(html, /No public API changes\./);
  assert.match(html, /data-library-api-diff-empty/);
});

test("a non-empty document preserves order, selects the first Type initially, and exposes aggregate counts", () => {
  const alpha = widgetSubject({ identifier: "T:A.Alpha", display: "A.Alpha" });
  const beta = widgetSubject({ identifier: "T:B.Beta", display: "B.Beta" });
  const html = view({ state: { ...createLibraryApiDiffState(), request, result: available([alpha, beta]) } });
  const alphaIndex = html.indexOf('data-library-api-diff-type="T:A.Alpha"');
  const betaIndex = html.indexOf('data-library-api-diff-type="T:B.Beta"');
  assert.ok(alphaIndex >= 0 && betaIndex >= 0 && alphaIndex < betaIndex);
  assert.match(html, /data-library-api-diff-count="changed-types"[^]*?<dd>2<\/dd>/);
  // The first row (document order) is selected when no explicit selection exists.
  const firstRow = html.slice(alphaIndex - 400, alphaIndex + 40);
  assert.match(firstRow, /class="library-api-diff-type-row selected"/);
  assert.match(html, /data-library-api-diff-detail="T:A.Alpha"/);
});

test("an explicit selection survives a rerender of the same result", () => {
  const alpha = widgetSubject({ identifier: "T:A.Alpha", display: "A.Alpha" });
  const beta = widgetSubject({ identifier: "T:B.Beta", display: "B.Beta" });
  const result = available([alpha, beta]);
  const html = view({
    state: { ...createLibraryApiDiffState(), request, result, selectedTypeId: "T:B.Beta" },
  });
  assert.match(html, /data-library-api-diff-detail="T:B.Beta"/);
  const betaIndex = html.indexOf('data-library-api-diff-type="T:B.Beta"');
  const betaRow = html.slice(betaIndex - 400, betaIndex + 40);
  assert.match(betaRow, /class="library-api-diff-type-row selected"/);
});

test("the selected Type's detail exposes complete compatibility changes and distinct member relations", () => {
  const widget = widgetSubject();
  const html = view({
    state: { ...createLibraryApiDiffState(), request, result: available([widget]), selectedTypeId: widget.identifier },
  });
  assert.match(html, /Compatibility changes \(1\)/);
  assert.match(html, /data-library-api-diff-change="MemberSignatureChanged"/);
  assert.match(html, /data-library-api-diff-classification="Breaking"/);
  assert.match(html, /Signature changed\./);
  assert.match(html, /<code data-library-api-diff-change-old>void M\(\)<\/code>/);
  assert.match(html, /<code data-library-api-diff-change-new>void M\(int x\)<\/code>/);
  assert.match(html, /Member relations \(1\)/);
  assert.match(html, /data-library-api-diff-member="library-api-member-relation\.v1\|Example\.Widget\.M\(\)\|0"/);
  assert.match(html, /data-library-api-diff-member-pair-kind="Changed"/);
  assert.match(html, /data-library-api-diff-member-role="Both"/);
  assert.match(html, /data-library-api-diff-member-endpoint="before"/);
  assert.match(html, /data-library-api-diff-member-selector="M\(\)"/);
  assert.match(html, /void M\(int x\)/);
  assert.match(html, /extension-instance · tier 80% · match 80%/);
});

test("safe escaping: hostile display text, messages, and values never break out of their attributes or text nodes", () => {
  const base = widgetSubject({
    identifier: '"><img src=x onerror=alert(1)>',
    display: '<script>alert("type")</script>',
  });
  const hostile: BrowserLibraryApiTypeSubject = {
    ...base,
    typeDiff: {
      ...base.typeDiff,
      compatibilityChanges: [{
        ...base.typeDiff.compatibilityChanges[0]!,
        message: '<img src=x onerror=alert("msg")>',
        oldValue: '"><script>old</script>',
        newValue: '"><script>new</script>',
      }],
    },
  };
  const html = view({
    state: {
      ...createLibraryApiDiffState(), request, result: available([hostile]),
      selectedTypeId: hostile.identifier,
    },
  });
  assert.doesNotMatch(html, /<script>alert/);
  assert.match(html, /&lt;script&gt;alert\(&quot;type&quot;\)&lt;\/script&gt;/);
  assert.match(html, /&lt;img src=x onerror=alert\(&quot;msg&quot;\)&gt;/);
});

test("Type detail rerender restores the inventory scroll and exact focused row", () => {
  let restoredSurfaceScrollTop = 0;
  let restoredScrollTop = 0;
  let focused = false;
  const focusedRow = { dataset: { libraryApiDiffType: "T:Example.Thirty" } };
  const beforeList = {
    scrollTop: 1545,
    querySelector: () => focusedRow,
  };
  const beforeSurface = { scrollTop: 527 };
  const afterRow = {
    dataset: { libraryApiDiffType: "T:Example.Thirty" },
    focus: (options: FocusOptions) => {
      focused = options.preventScroll === true;
    },
  };
  const afterList = {
    get scrollTop() { return restoredScrollTop; },
    set scrollTop(value: number) { restoredScrollTop = value; },
    querySelectorAll: () => [afterRow],
  };
  const afterSurface = {
    get scrollTop() { return restoredSurfaceScrollTop; },
    set scrollTop(value: number) { restoredSurfaceScrollTop = value; },
  };
  const snapshot = captureLibraryApiDiffSelection(fakeDom.parentNode({
    querySelector: (selector: string) =>
      selector === ".library-api-diff-scroll" ? beforeSurface : beforeList,
  }));
  restoreLibraryApiDiffSelection(fakeDom.parentNode({
    querySelector: (selector: string) =>
      selector === ".library-api-diff-scroll" ? afterSurface : afterList,
  }), snapshot);

  assert.deepEqual(snapshot, {
    surfaceScrollTop: 527,
    scrollTop: 1545,
    focusedTypeId: "T:Example.Thirty",
  });
  assert.equal(restoredSurfaceScrollTop, 527);
  assert.equal(restoredScrollTop, 1545);
  assert.equal(focused, true);
});
