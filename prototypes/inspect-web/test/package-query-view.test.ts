import assert from "node:assert/strict";
import test from "node:test";

import {
  bindPackageQueryView,
  capturePackageQueryFocus,
  capturePackageQueryScroll,
  packageQueryNeedsMoreMatches,
  patchPackageQueryStream,
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
  type QuerySourceCatalog,
  type QuerySourceSelection,
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

const SOURCE_CATALOG: QuerySourceCatalog = {
  packageType: {
    id: "producer.package-type",
    label: "Producer package type",
    summary: "Select a type from source metadata.",
    suggestions: [
      { value: "Producer.Tool", label: "Producer tools" },
      { value: "Producer.Template", label: "Producer templates" },
    ],
  },
  orders: [
    {
      id: "producer.order.first",
      label: "Producer first order",
      summary: "First producer ordering description.",
    },
    {
      id: "producer.order.second",
      label: "Producer second order",
      summary: "Second producer ordering description.",
    },
  ],
};

function row(packageId: string): QueryResultRow {
  return {
    packageId,
    version: "1.0.0",
    tier: "nuspec",
    evidence: [
      {
        id: "test.framework.net45",
        text: "net45",
        scope: "package",
        summary: null,
      },
      {
        id: "test.framework.net461",
        text: "net461",
        scope: "package",
        summary: null,
      },
    ],
    totalDownloads: 4200,
  };
}

test("an unstarted query renders the composing empty state", () => {
  const html = renderPackageQueryView({
    state: initialQueryState(),
    availableFacets: FACETS,
    escapeHtml,
  });

  assert.match(html, /Select package input/);
  assert.match(html, /Package ID or prefix/);
  assert.match(html, /Feeling lucky/);
  assert.match(html, /terminal <code>\*<\/code>/);
  assert.doesNotMatch(html, /maxlength=/);
});

test("prerelease remains available while Gallery controls require explicit discovery", () => {
  for (const sourceCatalog of [undefined, null]) {
    const html = renderPackageQueryView({
      state: initialQueryState(),
      availableFacets: FACETS,
      ...(sourceCatalog === undefined ? {} : { sourceCatalog }),
      escapeHtml,
    });
    assert.doesNotMatch(html, /id="package-query-(type|order)"/);
    assert.match(html, /id="package-query-prerelease"/);
    assert.match(html, /<h2>Inspection facets<\/h2>/);
  }
  const packageHtml = renderPackageQueryView({
    state: {
      request: createQueryRequest("Newtonsoft.Json"),
      outcome: emptyOutcome(),
    },
    availableFacets: FACETS,
    sourceCatalog: SOURCE_CATALOG,
    escapeHtml,
  });
  assert.match(packageHtml, /<h2>Package options<\/h2>/);
  assert.match(packageHtml, /Feeling lucky/);
  assert.match(packageHtml, /id="package-query-prerelease"/);
  assert.doesNotMatch(packageHtml, /id="package-query-(type|order)"/);

  const html = renderPackageQueryView({
    state: {
      request: createQueryRequest("", "gallery"),
      outcome: emptyOutcome(),
    },
    availableFacets: FACETS,
    sourceCatalog: SOURCE_CATALOG,
    escapeHtml,
  });

  assert.match(html, /aria-label="Package query options"/);
  assert.match(html, /<h2>Gallery filters<\/h2>/);
  assert.match(html, /Producer package type/);
  assert.match(html, /Select a type from source metadata/);
  assert.match(html, /<option value="" selected>All package types<\/option>/);
  assert.match(html, /<option value="" selected>Automatic<\/option>/);
  assert.match(html, /value="Producer.Tool">Producer tools<\/option>/);
  assert.match(html, /value="producer.order.second" title="Second producer ordering description.">Producer second order<\/option>/);
  assert.match(html, /id="package-query-prerelease" type="checkbox" \/>/);
  assert.match(html, /Gallery discovery is a separate explicit action/);
  assert.match(html, /manifests and package content are acquired only with inspection facets/);
  assert.ok(html.indexOf('aria-label="Package query options"') < html.indexOf("<h2>Inspection facets</h2>"));
});

test("source selections render from request identity without changing inspection facets", () => {
  const state: PackageQueryState = {
    request: {
      ...withFacet(
        createQueryRequest(" hosting libraries ", "gallery"),
        NUSPEC_FACET),
      packageType: "Producer.Template",
      sourceOrderId: "producer.order.second",
      includePrerelease: true,
    },
    outcome: emptyOutcome(),
  };
  const html = renderPackageQueryView({
    state,
    availableFacets: FACETS,
    sourceCatalog: SOURCE_CATALOG,
    escapeHtml,
  });

  assert.match(html, /value=" hosting libraries "/);
  assert.match(html, /value="Producer.Template" selected>Producer templates<\/option>/);
  assert.match(html, /value="producer.order.second"[^>]* selected>Producer second order<\/option>/);
  assert.match(html, /id="package-query-prerelease" type="checkbox" checked/);
  assert.match(html, /data-query-facet="tfm-out-of-support"[\s\S]*aria-pressed="true"/);
  assert.match(html, /id="package-query-order-description">Second producer ordering description./);
});

test("existing custom type and unavailable order stay visible rather than silently resetting", () => {
  const html = renderPackageQueryView({
    state: {
      request: {
        ...createQueryRequest("", "gallery"),
        packageType: "Producer.Custom",
        sourceOrderId: "producer.order.unavailable",
      },
      outcome: emptyOutcome(),
    },
    availableFacets: FACETS,
    sourceCatalog: SOURCE_CATALOG,
    escapeHtml,
  });

  assert.match(html, /value="Producer.Custom" selected>Producer.Custom<\/option>/);
  assert.match(html, /value="producer.order.unavailable" selected>producer.order.unavailable \(unavailable\)<\/option>/);
  assert.match(html, /Unavailable source order: producer.order.unavailable/);
  assert.doesNotMatch(html, /value="" selected>/);
});

test("candidate and local match bounds are independently disclosed before and during inspection", () => {
  for (const request of [
    null,
    withFacet(createQueryRequest(""), NUSPEC_FACET),
    withFacet(createQueryRequest(""), SKILL_FACET),
    { ...createQueryRequest(""), requestedMatchLimit: 7 },
  ]) {
    const html = renderPackageQueryView({
      state: { request, outcome: emptyOutcome() },
      availableFacets: FACETS,
      escapeHtml,
    });
    assert.ok(html.includes(`Candidate bound K: ${request?.requestedLimit ?? 200}`));
    assert.match(html, /exact IDs use one candidate/);
    assert.ok(html.includes(`Maximum matches N: ${request?.requestedMatchLimit ?? 100}`));
    assert.match(html, /The match limit does not change prefix or Gallery capacity/);
    assert.match(html, /Content facets download up to 20 candidate package archives/);
    assert.match(html, /Match counts and lifetime downloads describe a bounded response, not global top-N/);
  }
});

test("basic metadata rows show producer evidence and unavailable lifetime downloads distinctly from zero", () => {
  for (const totalDownloads of [null, 0, 1234]) {
    const html = renderPackageQueryView({
      state: {
        request: createQueryRequest(""),
        outcome: appendRows(emptyOutcome(), [
          {
            ...row("Producer.Result"),
            tier: "search-metadata",
            totalDownloads,
            evidence: [{
              id: "producer.source-selection",
              text: "Source selection and order from the producer",
              scope: "query",
              summary: null,
            }],
          },
          {
            ...row("Producer.Neighbor"),
            tier: "search-metadata",
            totalDownloads,
            evidence: [{
              id: "producer.source-selection",
              text: "Source selection and order from the producer",
              scope: "query",
              summary: null,
            }],
          },
        ]),
      },
      availableFacets: [],
      escapeHtml,
    });
    assert.match(html, /query-tier-search-metadata">search-metadata</);
    assert.match(html, /Source selection and order from the producer/);
    assert.equal((html.match(/Source selection and order from the producer/g)
      ?? []).length, 1);
    assert.equal((html.match(/<article class="query-row">/g) ?? []).length, 2);
    assert.equal((html.match(/<ul class="query-evidence">/g) ?? []).length, 1);
    if (totalDownloads === null) {
      assert.match(html, /Lifetime downloads unavailable/);
      assert.doesNotMatch(html, /0 lifetime downloads/);
    } else {
      assert.ok(html.includes(`${totalDownloads.toLocaleString()} lifetime downloads`));
      assert.doesNotMatch(html, /Lifetime downloads unavailable/);
    }
  }
});

test("query context renders once while package summaries remain on their cards", () => {
  const queryEvidence = {
    id: "producer.source-selection",
    text: "Selected by producer ranking.",
    scope: "query" as const,
    summary: null,
  };
  const first = {
    ...row("Contoso.First"),
    evidence: [
      queryEvidence,
      {
        id: "package.query.has-dependencies",
        text: "4 dependencies: A, B, C (+1 more).",
        scope: "package" as const,
        summary: {
          count: 4,
          preview: ["A", "B", "C"],
        },
      },
    ] as const,
  };
  const second = {
    ...row("Contoso.Second"),
    evidence: [
      queryEvidence,
      {
        id: "package.query.embedded-skill",
        text: "2 skill documents: skills/SKILL.md, skills/build/SKILL.md.",
        scope: "package" as const,
        summary: {
          count: 2,
          preview: ["skills/SKILL.md", "skills/build/SKILL.md"],
        },
      },
    ] as const,
  };

  const html = renderPackageQueryView({
    state: {
      request: createQueryRequest("", "gallery"),
      outcome: appendRows(emptyOutcome(), [first, second]),
    },
    availableFacets: [],
    escapeHtml,
  });

  assert.equal((html.match(/Selected by producer ranking\./g) ?? []).length, 1);
  assert.match(
    html,
    /<section class="query-context"[\s\S]*Selected by producer ranking\.[\s\S]*<div class="query-list">/);
  assert.equal((html.match(/4 dependencies: A, B, C \(\+1 more\)\./g)
    ?? []).length, 1);
  assert.equal((html.match(/2 skill documents:/g) ?? []).length, 1);
  assert.doesNotMatch(
    html,
    /<article class="query-row">[\s\S]*Selected by producer ranking\./);
});

test("Gallery completion text retains the finite bound and estimate with or without rows", () => {
  for (const rows of [[], [row("Producer.Result")]]) {
    const reason = "one finite Gallery response (capacity 200 candidates); acquired 3 candidates; estimated total hits: 0 (estimate only)";
    const html = renderPackageQueryView({
      state: {
        request: createQueryRequest("", "gallery"),
        outcome: withCompletion(appendRows(emptyOutcome(), rows), {
          kind: "bounded",
          reason,
        }),
      },
      availableFacets: [],
      escapeHtml,
    });

    assert.ok(html.includes(reason));
    assert.doesNotMatch(html, /all matches|exhausted|<h2>No matches<\/h2>/);
    assert.doesNotMatch(html, /data-query-cancel/);
  }
});

test("an exact zero-result completion states that no fallback search was used", () => {
  const html = renderPackageQueryView({
    state: {
      request: createQueryRequest("Missing.Package"),
      outcome: withCompletion(emptyOutcome(), { kind: "exact" }),
    },
    availableFacets: [],
    escapeHtml,
  });

  assert.match(html, /No package selected/);
  assert.match(html, /No prefix or Gallery search fallback was used/);
  assert.doesNotMatch(html, /Try a broader search/);
});

test("exact inspection failure is not presented as a confirmed empty result", () => {
  const html = renderPackageQueryView({
    state: {
      request: createQueryRequest("Example.Package"),
      outcome: withCompletion(
        appendFailure(emptyOutcome(), "The package manifest could not be acquired."),
        { kind: "exact" }),
    },
    availableFacets: [],
    escapeHtml,
  });

  assert.match(html, /Exact package inspection incomplete/);
  assert.match(html, /not a confirmed empty result/);
  assert.match(html, /The package manifest could not be acquired/);
  assert.match(html, /No prefix or Gallery search fallback was used/);
  assert.doesNotMatch(html, /<h2>No package selected<\/h2>/);
});

test("row descriptions render as escaped text only when available", () => {
  for (const description of [undefined, null, "", "   ", "Tools for <format> packages & templates."]) {
    const html = renderPackageQueryView({
      state: {
        request: createQueryRequest(""),
        outcome: appendRows(emptyOutcome(), [{
          ...row("Producer.Result"),
          tier: "search-metadata",
          ...(description === undefined ? {} : { description }),
        }]),
      },
      availableFacets: [],
      escapeHtml,
    });
    if (description?.trim()) {
      assert.match(html, /<p class="query-row-description">Tools for &lt;format&gt; packages &amp; templates.<\/p>/);
      assert.doesNotMatch(html, /<format>/);
    } else {
      assert.doesNotMatch(html, /query-row-description/);
    }
  }
});

test("the query header keeps home and Back without Query or Workspace buttons", () => {
  const html = renderPackageQueryView({
    state: initialQueryState(),
    availableFacets: FACETS,
    escapeHtml,
  });

  assert.doesNotMatch(html, /application-scope/);
  assert.match(html, /id="package-query-back" type="button">Back<\/button>/);
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
  assert.match(html, />Open in workspace<\/button>/);
  assert.doesNotMatch(html, /application-scope/);
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

  assert.match(withoutRows, /Source acquisition/);
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
  scrollHeight = 0;
  clientHeight = 0;
  innerHTML = "";
  value = "";
  checked = false;
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

  removeEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    this.listeners.set(
      type,
      listeners.filter(candidate => candidate !== listener));
  }

  dispatch(type: string, event = fakeDom.event()) {
    for (const listener of this.listeners.get(type) ?? []) listener(event);
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
      active: new FakeElement({}, "package-query-discover"),
      selector: "#package-query-discover",
      replacement: new FakeElement({}, "package-query-discover"),
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
    ...["type", "order", "prerelease"].map(control => ({
      active: new FakeElement({}, `package-query-${control}`),
      selector: `#package-query-${control}`,
      replacement: new FakeElement({}, `package-query-${control}`),
    })),
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
  const cases = [
    new FakeElement({
      queryRowOpen: "Vanished.Package",
      queryRowVersion: "1.0.0",
    }),
    ...["type", "order", "prerelease"].map(
      control => new FakeElement({}, `package-query-${control}`)),
  ];

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

test("a CSS-hidden query control reports prefix fallback", () => {
  const active = new FakeElement({}, "package-query-back");
  const replacement = new FakeElement({}, "package-query-back");
  replacement.rendered = false;
  const prefix = new FakeElement({}, "package-query-prefix");
  const root = new FakeRoot(active);
  root.add("#package-query-back", replacement);
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

test("bindPackageQueryView wires back, discovery, row-open, facet, and cancel", () => {
  const root = new FakeRoot();
  const [back] = root.add("#package-query-back", new FakeElement());
  const [discover] = root.add("#package-query-discover", new FakeElement());
  const [open] = root.add("[data-query-row-open]", new FakeElement({ queryRowOpen: "A", queryRowVersion: "1.0.0" }));
  const [facet] = root.add("[data-query-facet]", new FakeElement({ queryFacet: "tfm-out-of-support" }));
  const [cancel] = root.add("[data-query-cancel]", new FakeElement());

  const calls: string[] = [];
  const actions: PackageQueryBindingActions = {
    onBack: () => calls.push("back"),
    onCancel: () => calls.push("cancel"),
    onDiscover: () => calls.push("discover"),
    onFacetToggle: key => calls.push(`facet:${key}`),
    onPrefixInput: () => {},
    onResultPressure: () => calls.push("pressure"),
    onRowOpen: (id, version) => calls.push(`open:${id}:${version}`),
    onRun: () => {},
    onSourceChange: () => {},
  };

  bindPackageQueryView(fakeDom.parentNode(root), actions);

  back?.dispatch("click");
  discover?.dispatch("click");
  open?.dispatch("click");
  facet?.dispatch("click");
  cancel?.dispatch("click");

  assert.deepEqual(calls, [
    "back",
    "discover",
    "open:A:1.0.0",
    "facet:tfm-out-of-support",
    "cancel",
  ]);
});

test("source control changes forward the complete selection and current unmodified search text", () => {
  const root = new FakeRoot();
  const input = new FakeElement({}, "package-query-prefix");
  const packageType = new FakeElement({}, "package-query-type");
  const order = new FakeElement({}, "package-query-order");
  const prerelease = new FakeElement({}, "package-query-prerelease");
  root.add("#package-query-prefix", input);
  root.add("#package-query-type", packageType);
  root.add("#package-query-order", order);
  root.add("#package-query-prerelease", prerelease);
  const calls: {
    selection: Partial<QuerySourceSelection>;
    searchText: string;
  }[] = [];
  bindPackageQueryView(fakeDom.parentNode(root), {
    onBack: () => {},
    onCancel: () => {},
    onDiscover: () => {},
    onFacetToggle: () => assert.fail("source controls are not inspection facets"),
    onPrefixInput: () => {},
    onResultPressure: () => {},
    onRowOpen: () => {},
    onRun: () => assert.fail("source changes use their own action"),
    onSourceChange: (selection, searchText) => calls.push({ selection, searchText }),
  });

  packageType.value = "Producer.CustomType";
  packageType.dispatch("change");
  input.value = " hosting libraries * ";
  order.value = "producer.order.custom";
  order.dispatch("change");
  prerelease.checked = true;
  prerelease.dispatch("change");
  packageType.value = "";
  order.value = "";
  prerelease.checked = false;
  order.dispatch("change");

  assert.deepEqual(calls, [
    {
      selection: {
        packageType: "Producer.CustomType", sourceOrderId: null, includePrerelease: false,
      },
      searchText: "",
    },
    {
      selection: {
        packageType: "Producer.CustomType", sourceOrderId: "producer.order.custom", includePrerelease: false,
      },
      searchText: " hosting libraries * ",
    },
    {
      selection: {
        includePrerelease: true,
      },
      searchText: " hosting libraries * ",
    },
    {
      selection: {
        packageType: null, sourceOrderId: null, includePrerelease: false,
      },
      searchText: " hosting libraries * ",
    },
  ]);
});

test("query form submits package text while Feeling lucky is a separate action", () => {
  const root = new FakeRoot();
  const form = new FakeElement({}, "package-query-form");
  const input = new FakeElement({}, "package-query-prefix");
  const discover = new FakeElement({}, "package-query-discover");
  root.add("#package-query-form", form);
  root.add("#package-query-prefix", input);
  root.add("#package-query-discover", discover);
  const calls: string[] = [];
  let prevented = 0;
  bindPackageQueryView(fakeDom.parentNode(root), {
    onBack: () => {},
    onCancel: () => {},
    onDiscover: () => calls.push("gallery:"),
    onFacetToggle: () => {},
    onPrefixInput: () => {},
    onResultPressure: () => {},
    onRowOpen: () => {},
    onRun: text => calls.push(text),
    onSourceChange: () => assert.fail("source controls are absent"),
  });
  for (const text of ["", " hosting libraries ", "System.*"]) {
    input.value = text;
    form.dispatch("submit", fakeDom.event({
      preventDefault() { prevented++; },
    }));
  }
  discover.dispatch("click");

  assert.deepEqual(calls, [
    "",
    " hosting libraries ",
    "System.*",
    "gallery:",
  ]);
  assert.equal(prevented, 3);
});

test("query result pressure starts within 600 pixels of the current end", () => {
  assert.equal(packageQueryNeedsMoreMatches({
    scrollTop: 200,
    clientHeight: 800,
    scrollHeight: 1601,
  }), false);
  assert.equal(packageQueryNeedsMoreMatches({
    scrollTop: 201,
    clientHeight: 800,
    scrollHeight: 1601,
  }), true);
});

test("bindPackageQueryView reports near-end scroll pressure and disconnects it", () => {
  const root = new FakeRoot();
  const main = new FakeElement();
  main.clientHeight = 800;
  main.scrollHeight = 1800;
  root.add(".query-main", main);
  let pressure = 0;
  const binding = bindPackageQueryView(fakeDom.parentNode(root), {
    onBack: () => {},
    onCancel: () => {},
    onDiscover: () => {},
    onFacetToggle: () => {},
    onPrefixInput: () => {},
    onResultPressure: () => { pressure++; },
    onRowOpen: () => {},
    onRun: () => {},
    onSourceChange: () => {},
  });

  main.scrollTop = 401;
  main.dispatch("scroll");
  assert.equal(pressure, 1);

  binding.disconnect();
  main.dispatch("scroll");
  assert.equal(pressure, 1);
});

test("patchPackageQueryStream updates only dynamic query regions", () => {
  const root = new FakeRoot();
  const failures = new FakeElement();
  const cancel = new FakeElement();
  const results = new FakeElement();
  const main = new FakeElement();
  main.clientHeight = 800;
  main.scrollHeight = 1600;
  main.scrollTop = 800;
  root.add("#package-query-failure-region", failures);
  root.add("#package-query-cancel-region", cancel);
  root.add("#package-query-results", results);
  root.add(".query-main", main);
  const state: PackageQueryState = {
    request: createQueryRequest("Contoso."),
    outcome: appendRows(emptyOutcome(), [row("Contoso.One")]),
  };
  let pressure = 0;

  const patched = patchPackageQueryStream(
    fakeDom.parentNode(root),
    { state, escapeHtml },
    {
      onBack: () => {},
      onCancel: () => {},
      onDiscover: () => {},
      onFacetToggle: () => {},
      onPrefixInput: () => {},
      onResultPressure: () => { pressure++; },
      onRowOpen: () => {},
      onRun: () => {},
      onSourceChange: () => {},
    });

  assert.equal(patched, true);
  assert.match(results.innerHTML, /Contoso\.One/);
  assert.match(cancel.innerHTML, /data-query-cancel="1"/);
  assert.equal(failures.innerHTML, "");
  assert.equal(pressure, 1);
});
