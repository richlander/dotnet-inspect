import assert from "node:assert/strict";
import test from "node:test";

import {
  bindPackageQueryView,
  capturePackageQueryFocus,
  capturePackageQueryScroll,
  renderPackageQueryView,
  restorePackageQueryFocus,
  restorePackageQueryScroll,
  type PackageQueryBindingActions,
} from "../src/package-query-view.ts";
import {
  appendFailure,
  appendProgress,
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
  .replace(/>/g, "&gt;")
  .replace(/"/g, "&quot;");

const NUSPEC_FACET: QueryFacetTerm = { key: "tfm-out-of-support", label: "out-of-support only", tier: "nuspec" };
const DOWNLOAD_FACET: QueryFacetTerm = { key: "downloads-1m", label: "1M+ downloads", tier: "nuspec" };
const TOOL_FACETS: readonly QueryFacetTerm[] = [
  {
    key: "package.query.dotnet-tool",
    label: ".NET Tool",
    tier: "nuspec",
    selectionGroupId: "package.query.dotnet-tool-format",
    displayGroupId: "package.query.display.dotnet-tool",
    displayGroupLabel: ".NET tool format",
  },
  {
    key: "package.query.dotnet-tool-v1",
    label: "v1",
    tier: "package-content",
    selectionGroupId: "package.query.dotnet-tool-format",
    displayGroupId: "package.query.display.dotnet-tool",
    displayGroupLabel: ".NET tool format",
  },
  {
    key: "package.query.dotnet-tool-v2",
    label: "v2",
    tier: "package-content",
    selectionGroupId: "package.query.dotnet-tool-format",
    displayGroupId: "package.query.display.dotnet-tool",
    displayGroupLabel: ".NET tool format",
  },
];
const SKILL_FACET: QueryFacetTerm = {
  key: "package.query.embedded-skill",
  label: "embedded SKILL.md",
  tier: "package-content",
};
const FACETS: readonly QueryFacetTerm[] = [
  NUSPEC_FACET,
  ...TOOL_FACETS,
  DOWNLOAD_FACET,
  SKILL_FACET,
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

test("the persistent application scopes distinguish Query from Workspace", () => {
  const html = renderPackageQueryView({
    state: initialQueryState(),
    availableFacets: FACETS,
    workspaceAvailable: true,
    escapeHtml,
  });

  assert.match(
    html,
    /data-application-scope="query"[^>]*aria-current="page"[\s\S]*data-application-scope="workspace"(?![^>]*aria-current)/);
  assert.match(
    html,
    /id="package-query-product" class="brand" href="\/" aria-label="dotnet inspect home"/);
});

test("a packageId cannot break out of the row's HTML attribute context via a quote", () => {
  const maliciousRow: QueryResultRow = {
    ...row('Microsoft.Bcl.AsyncInterfaces" onmouseover="alert(1)'),
  };
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: appendRows(emptyOutcome(), [maliciousRow]),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.ok(!html.includes('" onmouseover="alert(1)'), "raw quote must not break out of the attribute");
  assert.match(html, /&quot; onmouseover=&quot;alert\(1\)/);
});

test("a streaming result renders rows, product facets, and the streaming footer", () => {
  const state: PackageQueryState = {
    request: withFacet(createQueryRequest("Microsoft."), NUSPEC_FACET),
    outcome: appendRows(emptyOutcome(), [row("Microsoft.Bcl.AsyncInterfaces")]),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /Microsoft\.Bcl\.AsyncInterfaces/);
  assert.match(html, /query-tier-nuspec/);
  assert.match(html, /1M\+ downloads/);
  assert.match(html, /streaming…/);
  assert.match(html, /data-query-cancel="1"/);
  assert.doesNotMatch(html, /Deepen|data-query-row-select/);
  assert.doesNotMatch(html, /class="query-footer" role="status"/);
});

test("streaming progress renders with and without matching rows", () => {
  const progress = appendProgress(
    appendProgress(emptyOutcome(), {
      phase: "search",
      completed: 1,
      limit: 1,
    }),
    {
      phase: "manifest",
      completed: 14,
      limit: 20,
    });
  const withoutRows = renderPackageQueryView({
    state: {
      request: createQueryRequest("System.*"),
      outcome: progress,
    },
    availableFacets: FACETS,
    escapeHtml,
  });
  const withRows = renderPackageQueryView({
    state: {
      request: createQueryRequest("System.*"),
      outcome: appendRows(progress, [row("System.Text.Json")]),
    },
    availableFacets: FACETS,
    escapeHtml,
  });

  assert.match(withoutRows, /Source search/);
  assert.match(withoutRows, /Manifests/);
  assert.match(withoutRows, /14 of up to 20/);
  assert.match(withoutRows, /<progress value="14" max="20">/);
  assert.match(withRows, /System\.Text\.Json/);
  assert.match(withRows, /14 of up to 20/);
});

test("result rows render typed producer identity instead of a source literal", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: appendRows(emptyOutcome(), [{
      ...row("Microsoft.Extensions.Logging"),
      producer: "contoso.example/v3",
    }]),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, />contoso\.example\/v3</);
  assert.doesNotMatch(html, />nuget\.org<\/span>/);
});

test("facet buttons expose pressed state without shipping promoted placeholders", () => {
  const state: PackageQueryState = {
    request: withFacet(
      createQueryRequest("Microsoft."),
      NUSPEC_FACET),
    outcome: appendRows(emptyOutcome(), [row("A")]),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(
    html,
    /data-query-facet="tfm-out-of-support"[\s\S]*aria-pressed="true"/);
  assert.match(
    html,
    /data-query-facet="downloads-1m"[\s\S]*aria-pressed="false"/);
  assert.doesNotMatch(html, /promoted|Deepen/);
});

test("tool format facets render as one independently selectable segmented control", () => {
  const state: PackageQueryState = {
    request: withFacet(
      createQueryRequest("Microsoft."),
      TOOL_FACETS[2]!),
    outcome: appendRows(emptyOutcome(), [row("A")]),
  };

  const html = renderPackageQueryView({
    state,
    availableFacets: FACETS,
    escapeHtml,
  });

  assert.match(
    html,
    /class="query-facet-group"[\s\S]*role="group"[\s\S]*aria-label="\.NET tool format"/);
  assert.match(
    html,
    /data-query-facet="package\.query\.dotnet-tool"[\s\S]*>\s*\.NET Tool\s*<\/button>[\s\S]*data-query-facet="package\.query\.dotnet-tool-v1"[\s\S]*>\s*v1\s*<\/button>[\s\S]*data-query-facet="package\.query\.dotnet-tool-v2"[\s\S]*aria-pressed="true"[\s\S]*>\s*v2\s*<\/button>/);
  assert.match(html, />\s*embedded SKILL\.md\s*<\/button>/);
  assert.match(
    html,
    /Content facets download up to 20 candidate package archives/);
});

test("package-content results disclose their evidence tier", () => {
  const state: PackageQueryState = {
    request: withFacet(createQueryRequest("Contoso."), SKILL_FACET),
    outcome: appendRows(emptyOutcome(), [{
      ...row("Contoso.Skill"),
      tier: "package-content",
    }]),
  };

  const html = renderPackageQueryView({
    state,
    availableFacets: FACETS,
    escapeHtml,
  });

  assert.match(html, /query-tier-package-content">package-content</);
});

test("a facet catalog failure remains visible beside an empty facet rail", () => {
  const html = renderPackageQueryView({
    state: initialQueryState(),
    availableFacets: [],
    navigationError: "Package-query facets are unavailable: catalog failed.",
    escapeHtml,
  });

  assert.match(html, /Package-query facets are unavailable: catalog failed/);
  assert.match(html, /class="query-facets"><\/div>/);
  assert.doesNotMatch(html, /role="alert"/);
  assert.doesNotMatch(
    html,
    /class="query-navigation-error" role="alert"/);
});

test("failures render alongside already-streamed rows, never as a bare empty state", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: appendFailure(appendRows(emptyOutcome(), [row("A")]), "feed Y unreachable"),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /feed Y unreachable/);
  assert.match(html, /<h2>A<\/h2>/);
  assert.doesNotMatch(html, /class="query-failures" role="alert"/);
});

test("an exhausted outcome with a partial failure never claims 'all matches'", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(
      appendFailure(appendRows(emptyOutcome(), [row("A")]), "feed Y unreachable"),
      { kind: "exhausted" },
    ),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /feed Y unreachable/);
  // "all matches" alone would overclaim exhaustiveness when a source failed.
  assert.doesNotMatch(html, /· all matches<\/span>/);
  assert.match(html, /all matches from the source work that succeeded/);
});

test("an exhausted outcome with rows and no failures still says plain 'all matches'", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(appendRows(emptyOutcome(), [row("A")]), { kind: "exhausted" }),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  // Without a failure, the qualified wording would be an unwarranted hedge.
  assert.match(html, /· all matches<\/span>/);
  assert.doesNotMatch(html, /all matches from sources that succeeded/);
});

test("a bounded-complete outcome states the exact bound rather than a bare count", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(
      appendRows(emptyOutcome(), [row("A")]),
      { kind: "bounded", reason: "first 1,500 relevance-ranked ids" },
    ),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /bounded: first 1,500 relevance-ranked ids/);
  assert.doesNotMatch(html, /data-query-cancel="1"/);
});

test("no matches after completion renders the empty-match state, not the composing state", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(emptyOutcome(), { kind: "exhausted" }),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /No matches/);
  // The facet rail stays mounted so the empty-state guidance is actionable.
  assert.match(html, /query-facet-rail/);
  assert.match(html, /1M\+ downloads/);
});

test("a bounded-complete zero-row outcome never claims plain 'no matches' — it names the bound", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(emptyOutcome(), { kind: "bounded", reason: "first 1,500 relevance-ranked ids" }),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  // Plain "No matches" would overclaim exhaustiveness: a bounded search only
  // covered the declared cap, not the whole scope, so zero rows there is not
  // the same claim as zero rows over the full ecosystem.
  assert.doesNotMatch(html, /<h2>No matches<\/h2>/);
  assert.match(html, /first 1,500 relevance-ranked ids/);
});

test("a bounded-complete zero-row outcome with a partial failure keeps the bound, not just the failure wording", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(
      appendFailure(emptyOutcome(), "feed Y unreachable"),
      { kind: "bounded", reason: "first 1,500 relevance-ranked ids" },
    ),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  // A bounded outcome keeps its bounded label regardless of a partial
  // failure (same rule the footer follows for non-empty results) — the
  // generic "with failures" wording alone would silently drop the bound.
  assert.match(html, /first 1,500 relevance-ranked ids/);
  assert.match(html, /feed Y unreachable/);
  assert.doesNotMatch(html, /<h2>No matches found — with failures<\/h2>/);
  // The bound alone isn't enough: every other failure-adjacent empty state
  // in this file says explicitly that the result isn't confirmed. This one
  // must too, or a reader could mistake "no matches within the bound" for a
  // confident zero despite the concurrent source failure.
  assert.match(html, /not a confirmed empty result/);
});

test("zero rows plus a failure never renders as a confirmed empty result", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(
      appendFailure(emptyOutcome(), "feed Y unreachable"),
      { kind: "exhausted" },
    ),
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
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(
      appendRows(emptyOutcome(), [row("A")]),
      { kind: "failed", reason: "<script>steal()</script>" },
    ),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /<h2>A<\/h2>/);
  assert.ok(!html.includes("<script>steal()"), "raw failure reason must not appear unescaped");
  assert.match(html, /failed: &lt;script&gt;steal\(\)&lt;\/script&gt;/);
  // A failed run never streamed to completion, so it must not offer Cancel.
  assert.doesNotMatch(html, /data-query-cancel="1"/);
});

test("a failed outcome with zero rows renders the failed empty state with an escaped reason", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(emptyOutcome(), { kind: "failed", reason: "<script>steal()</script>" }),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  assert.match(html, /<h2>Query failed<\/h2>/);
  assert.ok(!html.includes("<script>steal()"), "raw failure reason must not appear unescaped");
  assert.match(
    html,
    /&lt;script&gt;steal\(\)&lt;\/script&gt; This is not a confirmed empty result/);
  assert.doesNotMatch(html, /<h2>No matches<\/h2>/);
});

test("a cancelled query with zero rows never renders as a confirmed empty result", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(emptyOutcome(), { kind: "cancelled" }),
  };

  const html = renderPackageQueryView({ state, availableFacets: FACETS, escapeHtml });

  // "No matches" alone would falsely claim a confirmed clean zero even
  // though the run was stopped before it could search the whole scope.
  assert.doesNotMatch(html, /<h2>No matches<\/h2>/);
  assert.match(html, /Cancelled before any matches/);
  assert.match(html, /not a confirmed empty result/);
});

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  readonly id: string;
  focusCount = 0;
  hidden = false;
  rendered = true;
  scrollTop = 0;
  selectionStart: number | null = null;
  selectionEnd: number | null = null;
  selectionRange: readonly [number, number] | null = null;
  private readonly listeners = new Map<string, EventListener[]>();

  constructor(
    dataset: Record<string, string | undefined> = {},
    id = "",
  ) {
    this.dataset = dataset;
    this.id = id;
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string) {
    for (const listener of this.listeners.get(type) ?? []) listener(fakeDom.event());
  }

  focus() {
    this.focusCount++;
  }

  checkVisibility() {
    return this.rendered;
  }

  setSelectionRange(start: number, end: number) {
    this.selectionRange = [start, end];
  }
}

class FakeRoot {
  private readonly elements = new Map<string, FakeElement[]>();
  readonly activeElement: FakeElement | null;
  readonly body: FakeElement | null;

  constructor(
    activeElement: FakeElement | null = null,
    body: FakeElement | null = null,
  ) {
    this.activeElement = activeElement;
    this.body = body;
  }

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

  querySelector(selector: string): Element | null {
    const found = this.elements.get(selector)?.[0] ?? null;
    // Test fake implements exactly the subset consumed by the binder.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return found as unknown as Element | null;
  }
}

test("query focus snapshots restore semantic controls after a full render", () => {
  const cases = [
    {
      active: new FakeElement({}, "package-query-run"),
      selector: "#package-query-run",
      replacement: new FakeElement({}, "package-query-run"),
    },
    {
      active: new FakeElement({ applicationScope: "workspace" }),
      selector: "[data-application-scope]",
      replacement: new FakeElement({ applicationScope: "workspace" }),
    },
    {
      active: new FakeElement({}, "package-query-product"),
      selector: "#package-query-product",
      replacement: new FakeElement({}, "package-query-product"),
    },
    {
      active: new FakeElement({}, "package-query-back"),
      selector: "#package-query-back",
      replacement: new FakeElement({}, "package-query-back"),
    },
    {
      active: new FakeElement({ queryFacet: "downloads-1m" }),
      selector: "[data-query-facet]",
      replacement: new FakeElement({ queryFacet: "downloads-1m" }),
    },
    {
      active: new FakeElement({
        queryRowOpen: "Microsoft.Extensions.Logging",
        queryRowVersion: "9.0.0",
      }),
      selector: "[data-query-row-open]",
      replacement: new FakeElement({
        queryRowOpen: "Microsoft.Extensions.Logging",
        queryRowVersion: "9.0.0",
      }),
    },
  ];

  for (const scenario of cases) {
    const root = new FakeRoot(scenario.active);
    root.add(scenario.selector, scenario.replacement);
    // Test fake implements the Document and ParentNode subset consumed by the helpers.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    const documentRoot = root as unknown as Document;

    const snapshot = capturePackageQueryFocus(documentRoot);
    const restoration = restorePackageQueryFocus(documentRoot, snapshot);

    assert.equal(restoration, "restored");
    assert.equal(scenario.replacement.focusCount, 1);
  }
});

test("query cancel focus restores by rendered position", () => {
  const active = new FakeElement({ queryCancel: "1" });
  const replacement = new FakeElement({ queryCancel: "1" });
  const root = new FakeRoot(active);
  root.add("[data-query-cancel]", active);
  // Test fake implements the Document and ParentNode subset consumed by the helpers.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const documentRoot = root as unknown as Document;

  const snapshot = capturePackageQueryFocus(documentRoot);
  root.add("[data-query-cancel]", replacement);
  const restoration = restorePackageQueryFocus(documentRoot, snapshot);

  assert.equal(restoration, "restored");
  assert.equal(replacement.focusCount, 1);
});

test("query scroll position survives streamed full renders", () => {
  const oldMain = new FakeElement();
  oldMain.scrollTop = 480;
  const replacement = new FakeElement();
  const root = new FakeRoot();
  root.add(".query-main", oldMain);
  // Test fake implements the ParentNode subset consumed by the helpers.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const parent = root as unknown as ParentNode;

  const scrollTop = capturePackageQueryScroll(parent);
  root.add(".query-main", replacement);
  restorePackageQueryScroll(parent, scrollTop);

  assert.equal(replacement.scrollTop, 480);
});

test("a vanished query control reports prefix fallback", () => {
  const cases = [new FakeElement({
    queryRowOpen: "Vanished.Package",
    queryRowVersion: "1.0.0",
  })];

  for (const active of cases) {
    const prefix = new FakeElement({}, "package-query-prefix");
    const root = new FakeRoot(active);
    root.add("#package-query-prefix", prefix);
    // Test fake implements the Document and ParentNode subset consumed by the helpers.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    const documentRoot = root as unknown as Document;

    const snapshot = capturePackageQueryFocus(documentRoot);
    const restoration = restorePackageQueryFocus(documentRoot, snapshot);

    assert.equal(restoration, "fallback");
    assert.equal(prefix.focusCount, 1);
  }
});

test("a CSS-hidden application scope reports prefix fallback", () => {
  const active = new FakeElement({ applicationScope: "workspace" });
  const replacement = new FakeElement({ applicationScope: "workspace" });
  replacement.rendered = false;
  const prefix = new FakeElement({}, "package-query-prefix");
  const root = new FakeRoot(active);
  root.add("[data-application-scope]", replacement);
  root.add("#package-query-prefix", prefix);
  // Test fake implements the Document and ParentNode subset consumed by the helpers.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const documentRoot = root as unknown as Document;

  const snapshot = capturePackageQueryFocus(documentRoot);
  const restoration = restorePackageQueryFocus(documentRoot, snapshot);

  assert.equal(restoration, "fallback");
  assert.equal(replacement.focusCount, 0);
  assert.equal(prefix.focusCount, 1);
});

test("an unfocused query render does not move focus into the prefix", () => {
  const body = new FakeElement();
  const prefix = new FakeElement({}, "package-query-prefix");
  const root = new FakeRoot(body, body);
  root.add("#package-query-prefix", prefix);
  // Test fake implements the Document and ParentNode subset consumed by the helpers.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const documentRoot = root as unknown as Document;

  const snapshot = capturePackageQueryFocus(documentRoot);
  const restoration = restorePackageQueryFocus(documentRoot, snapshot);

  assert.equal(snapshot, null);
  assert.equal(restoration, "none");
  assert.equal(prefix.focusCount, 0);
});

test("query prefix focus preserves its selection across a full render", () => {
  const active = new FakeElement({}, "package-query-prefix");
  active.selectionStart = 3;
  active.selectionEnd = 8;
  const replacement = new FakeElement({}, "package-query-prefix");
  const root = new FakeRoot(active);
  root.add("#package-query-prefix", replacement);
  // Test fake implements the Document and ParentNode subset consumed by the helpers.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const documentRoot = root as unknown as Document;

  const snapshot = capturePackageQueryFocus(documentRoot);
  restorePackageQueryFocus(documentRoot, snapshot);

  assert.equal(replacement.focusCount, 1);
  assert.deepEqual(replacement.selectionRange, [3, 8]);
});

test("bindPackageQueryView wires back, row-open, facet, and cancel", () => {
  const root = new FakeRoot();
  const [back] = root.add("#package-query-back", new FakeElement());
  const [workspace] = root.add(
    "[data-application-scope]",
    new FakeElement({ applicationScope: "workspace" }));
  const [open] = root.add("[data-query-row-open]", new FakeElement({ queryRowOpen: "A", queryRowVersion: "1.0.0" }));
  const [facet] = root.add("[data-query-facet]", new FakeElement({ queryFacet: "tfm-out-of-support" }));
  const [cancel] = root.add("[data-query-cancel]", new FakeElement());

  const calls: string[] = [];
  const actions: PackageQueryBindingActions = {
    onApplicationScopeSelect: scope =>
      calls.push(`application:${scope}`),
    onBack: () => calls.push("back"),
    onCancel: () => calls.push("cancel"),
    onFacetToggle: key => calls.push(`facet:${key}`),
    onPrefixInput: () => {},
    onRowOpen: (id, version) => calls.push(`open:${id}:${version}`),
    onRun: () => {},
  };

  bindPackageQueryView(fakeDom.parentNode(root), actions);

  workspace?.dispatch("click");
  back?.dispatch("click");
  open?.dispatch("click");
  facet?.dispatch("click");
  cancel?.dispatch("click");

  assert.deepEqual(calls, [
    "application:workspace",
    "back",
    "open:A:1.0.0",
    "facet:tfm-out-of-support",
    "cancel",
  ]);
});
