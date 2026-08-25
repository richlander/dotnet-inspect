import assert from "node:assert/strict";
import test from "node:test";

import {
  bindPackageQueryView,
  renderPackageQueryView,
  type PackageQueryBindingActions,
} from "../src/package-query-view.ts";
import {
  appendFailure,
  appendRows,
  createQueryRequest,
  emptyOutcome,
  initialQueryState,
  withCompletion,
  withFacet,
  type PackageQueryState,
  type QueryFacetTerm,
  type QueryResultRow,
} from "../src/package-query.ts";
import { fakeDom } from "./fake-dom.ts";

const escapeHtml = (value: unknown) => String(value)
  .replace(/&/g, "&amp;")
  .replace(/</g, "&lt;")
  .replace(/>/g, "&gt;");

const FACETS: readonly QueryFacetTerm[] = [
  { key: "tfm-out-of-support", label: "out-of-support only", tier: "nuspec" },
  { key: "union-usage", label: "uses C# union", tier: "promoted" },
];

function row(packageId: string): QueryResultRow {
  return {
    packageId,
    version: "1.0.0",
    tier: "nuspec",
    evidence: ["net45", "net461"],
    totalDownloads: 4200,
  };
}

test("an unstarted query renders the composing empty state", () => {
  const html = renderPackageQueryView({
    state: initialQueryState(),
    availableFacets: FACETS,
    escapeHtml,
  });

  assert.match(html, /Query nuget\.org/);
});

test("row tier is escaped like every other row field (defense in depth for untrusted data)", () => {
  const maliciousRow: QueryResultRow = {
    ...row("Microsoft.Bcl.AsyncInterfaces"),
    // A row's fields ultimately originate from a nuspec/search response, so
    // this must be escaped the same as packageId/version/evidence even
    // though the type is currently a closed union (see
    // untrusted-data-threat-model.md).
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion -- simulating a hostile/malformed row field
    tier: "<img src=x onerror=alert(1)>" as unknown as QueryResultRow["tier"],
  };
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft.*", "Microsoft."),
    outcome: appendRows(emptyOutcome(), [maliciousRow]),
    selected: new Set(),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.ok(!html.includes("<img src=x"), "raw tier markup must not appear unescaped");
  assert.match(html, /&lt;img src=x onerror=alert\(1\)&gt;/);
});

test("a streaming result renders rows, tiers, facets, and the streaming footer", () => {
  const state: PackageQueryState = {
    request: withFacet(createQueryRequest("Microsoft.*", "Microsoft."), FACETS[0]),
    outcome: appendRows(emptyOutcome(), [row("Microsoft.Bcl.AsyncInterfaces")]),
    selected: new Set(),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /Microsoft\.Bcl\.AsyncInterfaces/);
  assert.match(html, /query-tier-nuspec/);
  assert.match(html, /uses C# union/);
  assert.match(html, /streaming…/);
  assert.match(html, /data-query-cancel="1"/);
});

test("failures render alongside already-streamed rows, never as a bare empty state", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft.*", "Microsoft."),
    outcome: appendFailure(appendRows(emptyOutcome(), [row("A")]), "feed Y unreachable"),
    selected: new Set(),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /feed Y unreachable/);
  assert.match(html, /class="opp-type-name">A</);
});

test("an exhausted outcome with a partial failure never claims 'all matches'", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft.*", "Microsoft."),
    outcome: withCompletion(
      appendFailure(appendRows(emptyOutcome(), [row("A")]), "feed Y unreachable"),
      { kind: "exhausted" },
    ),
    selected: new Set(),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /feed Y unreachable/);
  // "all matches" alone would overclaim exhaustiveness when a source failed.
  assert.doesNotMatch(html, /· all matches<\/span>/);
  assert.match(html, /all matches from sources that succeeded/);
});

test("an exhausted outcome with rows and no failures still says plain 'all matches'", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft.*", "Microsoft."),
    outcome: withCompletion(appendRows(emptyOutcome(), [row("A")]), { kind: "exhausted" }),
    selected: new Set(),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  // Without a failure, the qualified wording would be an unwarranted hedge.
  assert.match(html, /· all matches<\/span>/);
  assert.doesNotMatch(html, /all matches from sources that succeeded/);
});

test("a bounded-complete outcome states the exact bound rather than a bare count", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft.*", "Microsoft."),
    outcome: withCompletion(
      appendRows(emptyOutcome(), [row("A")]),
      { kind: "bounded", reason: "first 1,500 relevance-ranked ids" },
    ),
    selected: new Set(),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /bounded: first 1,500 relevance-ranked ids/);
  assert.doesNotMatch(html, /data-query-cancel="1"/);
});

test("no matches after completion renders the empty-match state, not the composing state", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft.*", "Microsoft."),
    outcome: withCompletion(emptyOutcome(), { kind: "exhausted" }),
    selected: new Set(),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /No matches/);
  // Every empty-state message here tells the user to broaden the facets —
  // that's only actionable if the facet rail (and its Deepen tier) is still
  // mounted, rather than vanishing along with the results pane.
  assert.match(html, /query-facet-rail/);
  assert.match(html, /uses C# union/);
});

test("zero rows plus a failure never renders as a confirmed empty result", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft.*", "Microsoft."),
    outcome: withCompletion(
      appendFailure(emptyOutcome(), "feed Y unreachable"),
      { kind: "exhausted" },
    ),
    selected: new Set(),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /feed Y unreachable/);
  // "No matches" alone would falsely claim a clean, confirmed zero even
  // though a source failed and part of the space was never searched.
  assert.doesNotMatch(html, /<h2>No matches<\/h2>/);
  assert.match(html, /not a confirmed empty result/);
});

test("a failed outcome with rows still shows them, with an escaped reason in the footer", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft.*", "Microsoft."),
    outcome: withCompletion(
      appendRows(emptyOutcome(), [row("A")]),
      { kind: "failed", reason: "<script>steal()</script>" },
    ),
    selected: new Set(),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /class="opp-type-name">A</);
  assert.ok(!html.includes("<script>steal()"), "raw failure reason must not appear unescaped");
  assert.match(html, /failed: &lt;script&gt;steal\(\)&lt;\/script&gt;/);
  // A failed run never streamed to completion, so it must not offer Cancel.
  assert.doesNotMatch(html, /data-query-cancel="1"/);
});

test("a failed outcome with zero rows renders the failed empty state with an escaped reason", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft.*", "Microsoft."),
    outcome: withCompletion(emptyOutcome(), { kind: "failed", reason: "<script>steal()</script>" }),
    selected: new Set(),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /<h2>Query failed<\/h2>/);
  assert.ok(!html.includes("<script>steal()"), "raw failure reason must not appear unescaped");
  assert.match(html, /&lt;script&gt;steal\(\)&lt;\/script&gt; — not a confirmed empty result/);
  assert.doesNotMatch(html, /<h2>No matches<\/h2>/);
});

test("a cancelled query with zero rows never renders as a confirmed empty result", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft.*", "Microsoft."),
    outcome: withCompletion(emptyOutcome(), { kind: "cancelled" }),
    selected: new Set(),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  // "No matches" alone would falsely claim a confirmed clean zero even
  // though the run was stopped before it could search the whole scope.
  assert.doesNotMatch(html, /<h2>No matches<\/h2>/);
  assert.match(html, /Cancelled before any matches/);
});

test("deepen is disabled with no selection and enabled once a row is selected", () => {
  const withoutSelection = renderPackageQueryView({
    state: {
      request: createQueryRequest("Microsoft.*", "Microsoft."),
      outcome: appendRows(emptyOutcome(), [row("A")]),
      selected: new Set(),
    },
    availableFacets: FACETS,
    escapeHtml,
  });
  const withSelection = renderPackageQueryView({
    state: {
      request: createQueryRequest("Microsoft.*", "Microsoft."),
      outcome: appendRows(emptyOutcome(), [row("A")]),
      selected: new Set(["A"]),
    },
    availableFacets: FACETS,
    escapeHtml,
  });

  assert.match(withoutSelection, /data-query-deepen="1" disabled/);
  assert.match(withSelection, /Deepen 1 selected/);
  assert.doesNotMatch(withSelection, /data-query-deepen="1" disabled/);
});

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  private readonly listeners = new Map<string, EventListener[]>();

  constructor(dataset: Record<string, string | undefined> = {}) {
    this.dataset = dataset;
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string) {
    for (const listener of this.listeners.get(type) ?? []) listener(fakeDom.event());
  }
}

class FakeRoot {
  private readonly elements = new Map<string, FakeElement[]>();

  add(selector: string, ...elements: FakeElement[]) {
    this.elements.set(selector, elements);
    return elements;
  }

  querySelectorAll<T extends Element>(selector: string): NodeListOf<T> {
    const found = this.elements.get(selector) ?? [];
    // Test fake implements exactly the subset consumed by the binder.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return found as unknown as NodeListOf<T>;
  }
}

test("bindPackageQueryView wires row-open, select, facet, deepen, and cancel", () => {
  const root = new FakeRoot();
  const [open] = root.add("[data-query-row-open]", new FakeElement({ queryRowOpen: "A", queryRowVersion: "1.0.0" }));
  const [select] = root.add("[data-query-row-select]", new FakeElement({ queryRowSelect: "A" }));
  const [facet] = root.add("[data-query-facet]", new FakeElement({ queryFacet: "tfm-out-of-support" }));
  const [deepen] = root.add("[data-query-deepen]", new FakeElement());
  const [cancel] = root.add("[data-query-cancel]", new FakeElement());

  const calls: string[] = [];
  const actions: PackageQueryBindingActions = {
    onRowOpen: (id, version) => calls.push(`open:${id}:${version}`),
    onRowSelectToggle: id => calls.push(`select:${id}`),
    onFacetToggle: key => calls.push(`facet:${key}`),
    onDeepen: () => calls.push("deepen"),
    onCancel: () => calls.push("cancel"),
  };

  bindPackageQueryView(fakeDom.parentNode(root), actions);

  open?.dispatch("click");
  select?.dispatch("change");
  facet?.dispatch("click");
  deepen?.dispatch("click");
  cancel?.dispatch("click");

  assert.deepEqual(calls, [
    "open:A:1.0.0",
    "select:A",
    "facet:tfm-out-of-support",
    "deepen",
    "cancel",
  ]);
});
