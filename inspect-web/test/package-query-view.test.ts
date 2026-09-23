import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import {
  bindPackageQueryView,
  capturePackageQueryFocus,
  capturePackageQueryScroll,
  decodeLibraryLiteralEditorValue,
  encodeLibraryLiteralEditorValue,
  packageQueryNeedsMoreMatches,
  patchPackageQueryStream,
  renderPackageQueryView,
  restorePackageQueryFocus,
  restorePackageQueryScroll,
  type PackageQueryBindingActions,
} from "../src/package-query-view.ts";
import {
  createPackageQueryRenderScheduler,
  packageQueryEditorCompositionActive,
} from "../src/package-query-editor-lifecycle.ts";
import {
  appendAssessment,
  appendFailure,
  appendProgress,
  appendRows,
  createQueryRequest,
  emptyOutcome,
  initialQueryState,
  withCompletion,
  withPreset,
  withTerm,
  type PackageQueryState,
  type QueryPreset,
  type QueryResultRow,
  type QuerySourceSelection,
  type QueryTermDescriptor,
} from "../src/package-query.ts";
import { fakeDom } from "./fake-dom.ts";

const escapeHtml = (value: unknown) => String(value)
  .replace(/&/g, "&amp;")
  .replace(/</g, "&lt;")
  .replace(/>/g, "&gt;")
  .replace(/"/g, "&quot;");
const styles = readFileSync(
  new URL("../src/styles.css", import.meta.url),
  "utf8");

const NUSPEC_FACET: QueryPreset = {
  id: "readme:eq:true",
  key: "readme",
  operator: "eq",
  value: "true",
  label: "embedded README",
  tier: "nuspec",
  executionClass: "nuspec",
};
const DOWNLOAD_FACET: QueryPreset = {
  id: "downloads:eq:1m",
  key: "downloads",
  operator: "eq",
  value: "1m",
  label: "1M+ downloads",
  tier: "search-metadata",
  executionClass: "search-metadata",
};
const TOOL_FACETS: readonly QueryPreset[] = [
  {
    id: "tool:eq:true",
    key: "tool",
    operator: "eq",
    value: "true",
    label: ".NET Tool",
    tier: "nuspec",
    executionClass: "nuspec",
    displayGroupId: "package.query.display.dotnet-tool",
    displayGroupLabel: ".NET tool format",
  },
  {
    id: "tool-format:eq:v1",
    key: "tool-format",
    operator: "eq",
    value: "v1",
    label: "v1",
    tier: "package-content",
    executionClass: "package-content",
    selectionGroupId: "tool-format",
    displayGroupId: "package.query.display.dotnet-tool",
    displayGroupLabel: ".NET tool format",
  },
  {
    id: "tool-format:eq:v2",
    key: "tool-format",
    operator: "eq",
    value: "v2",
    label: "v2",
    tier: "package-content",
    executionClass: "package-content",
    selectionGroupId: "tool-format",
    displayGroupId: "package.query.display.dotnet-tool",
    displayGroupLabel: ".NET tool format",
  },
];
const SKILL_FACET: QueryPreset = {
  id: "skill:eq:true",
  key: "skill",
  operator: "eq",
  value: "true",
  label: "embedded SKILL.md",
  tier: "package-content",
  executionClass: "package-content",
};
const FACETS: readonly QueryPreset[] = [
  NUSPEC_FACET,
  ...TOOL_FACETS,
  DOWNLOAD_FACET,
  SKILL_FACET,
];
const DEPENDS_TERM: QueryTermDescriptor = {
  key: "depends",
  label: "Direct dependency",
  summary: "Matches a direct dependency in any group.",
  weight: 10,
  tier: "nuspec",
  executionClass: "nuspec",
  operators: ["eq"],
  valueKind: "package-id",
  example: "Microsoft.Extensions.Hosting",
  multiline: false,
};
const LIBRARY_LITERAL_TERM: QueryTermDescriptor = {
  key: "library-literal",
  label: "Library literal",
  summary: "Matches decoded string-literal uses.",
  weight: 650,
  tier: "package-content",
  executionClass: "metadata-expensive",
  operators: ["eq"],
  valueKind: "decoded UTF-16 text",
  example: "Microsoft.Extensions.",
  multiline: true,
};
const TERMS: readonly QueryTermDescriptor[] = [
  DEPENDS_TERM,
  LIBRARY_LITERAL_TERM,
];

function row(packageId: string): QueryResultRow {
  return {
    packageId,
    version: "1.0.0",
    tier: "nuspec",
    answers: [],
    evidence: [
      {
        id: "test.framework.net45",
        scope: "package",
        summary: null,
        properties: [{ name: "value", value: "net45" }],
        number: null,
      },
      {
        id: "test.framework.net461",
        scope: "package",
        summary: null,
        properties: [{ name: "value", value: "net461" }],
        number: null,
      },
    ],
    totalDownloads: 4200,
  };
}

test("an unstarted query renders the composing empty state", () => {
  const html = renderPackageQueryView({
    state: initialQueryState(),
    availablePresets: FACETS,
    escapeHtml,
  });

  assert.match(html, /Select package input/);
  assert.match(html, /Package ID or prefix/);
  assert.match(html, /terminal <code>\*<\/code>/);
  assert.doesNotMatch(html, /Feeling lucky|Gallery filters|package-query-discover/);
  assert.doesNotMatch(html, /role="tablist"|data-query-mode/);
  assert.doesNotMatch(html, /maxlength=/);
});

test("package options retain prerelease without Gallery controls", () => {
  for (const request of [
    null,
    createQueryRequest("Newtonsoft.Json"),
    createQueryRequest("Newtonsoft.*"),
  ]) {
    const html = renderPackageQueryView({
      state: {
        request,
        outcome: emptyOutcome(),
      },
      availablePresets: FACETS,
      escapeHtml,
    });
    assert.match(html, /<h2>Search options<\/h2>/);
    assert.match(html, /id="package-query-prerelease"/);
    assert.match(html, /<h2>Inspection facts<\/h2>/);
    assert.doesNotMatch(
      html,
      /Feeling lucky|Gallery filters|package-query-(discover|type|order)/);
  }

  const html = renderPackageQueryView({
    state: {
      request: {
        ...createQueryRequest("Newtonsoft.Json"),
        includePrerelease: true,
      },
      outcome: emptyOutcome(),
    },
    availablePresets: FACETS,
    escapeHtml,
  });
  assert.match(
    html,
    /id="package-query-prerelease" type="checkbox" checked/);
  assert.ok(html.indexOf('aria-label="Package query options"') < html.indexOf("<h2>Inspection facts</h2>"));
});

test("library-literal renders as a composable multiline term with target selection", () => {
  const request = withTerm(
    withPreset(createQueryRequest("Contoso.Package"), NUSPEC_FACET),
    LIBRARY_LITERAL_TERM,
    "eq",
    "shared-literal-use-marker");
  const result: QueryResultRow = {
    ...row("Contoso.Package"),
    tier: "assembly",
    rootRequest: "opaque-root",
    evidence: [{
      id: "implementation-libraries",
      scope: "package",
      summary: {
        count: 5,
        preview: ["first", "second", "third"],
      },
      properties: [
        { name: "evaluated-library-count", value: "2" },
        { name: "matched-library-count", value: "1" },
      ],
      number: null,
    }],
  };
  const html = renderPackageQueryView({
    state: {
      request,
      outcome: withCompletion(appendRows(emptyOutcome(), [result]), {
        kind: "library-literal",
        population: "ExactPackageComplete",
        candidateCount: 1,
        evaluatedCandidateCount: 1,
        notEvaluatedCount: 0,
        matchedPackageCount: 1,
        occurrenceCount: 5,
        semanticMissCount: 0,
        notApplicableCount: 0,
        failureCount: 0,
        complete: true,
      }),
    },
    availablePresets: FACETS,
    availableTerms: TERMS,
    escapeHtml,
  });

  assert.match(html, /<h2>Library selection<\/h2>/);
  assert.match(html, /<textarea[\s\S]*shared-literal-use-marker<\/textarea>/);
  assert.match(html, /value="net10\.0"/);
  assert.match(
    html,
    /2 implementation libraries evaluated; 1 matched\./);
  assert.match(html, /Showing 3 of 5 occurrences/);
  assert.match(html, /1 matching package · 5 occurrences/);
  assert.match(html, /exact package population complete/);
  assert.match(html, /data-query-root-request="opaque-root"/);
  assert.match(html, /data-query-preset="readme:eq:true"/);
  assert.match(html, /data-query-term-add="depends"/);
  assert.match(html, /data-query-term-add="library-literal"/);
});

test("library-literal renders selector-ordered per-Library assessments", () => {
  const request = withTerm(
    createQueryRequest("Contoso.Package"),
    LIBRARY_LITERAL_TERM,
    "eq",
    "https://",
  );
  const outcome = appendAssessment(emptyOutcome(), {
    packageId: "Contoso.Package",
    version: "1.0.0",
    disposition: "Matched",
    message: "The selected implementation libraries contain matching literals.",
    assetPath: "lib/net10.0/Contoso.Package.dll",
    rootRequest: "opaque-root",
    libraries: [{
      path: "lib/net10.0/Contoso.Package.dll",
      assemblyName: "Contoso.Package",
      targetFramework: "net10.0",
      ordinal: 0,
      disposition: "NoMatch",
      occurrenceCount: 0,
      failureStage: null,
      message: null,
    }, {
      path: "lib/net10.0/Contoso.Package.Common.dll",
      assemblyName: "Contoso.Package.Common",
      targetFramework: "net10.0",
      ordinal: 1,
      disposition: "Matched",
      occurrenceCount: 2,
      failureStage: null,
      message: null,
    }],
  });
  const html = renderPackageQueryView({
    state: { request, outcome },
    availablePresets: FACETS,
    availableTerms: TERMS,
    escapeHtml,
  });

  assert.match(html, /Selected implementation Library outcomes/);
  assert.match(html, /selector-issued implementation Libraries/);
  assert.ok(
    html.indexOf("Contoso.Package.dll") <
      html.indexOf("Contoso.Package.Common.dll"));
  assert.match(html, /Contoso.Package.dll · No match/);
  assert.match(
    html,
    /Contoso.Package.Common.dll · Matched \(2 occurrences\)/);
  assert.doesNotMatch(html, /primary implementation/);
});

test("whitespace-only library literals remain active and unchanged", () => {
  const request = withTerm(
    createQueryRequest("Contoso.Package"),
    LIBRARY_LITERAL_TERM,
    "eq",
    " ",
  );
  const html = renderPackageQueryView({
    state: {
      request,
      outcome: emptyOutcome(),
    },
    availablePresets: FACETS,
    availableTerms: TERMS,
    escapeHtml,
  });

  assert.match(html, /<textarea[\s\S]*> <\/textarea>/);
  assert.match(html, /data-query-term-add="depends"/);
  assert.match(html, /value="net10\.0"/);
});

test("library-literal editor spelling round-trips exact UTF-16 text", () => {
  for (const value of [
    "plain text",
    "\n",
    "\r",
    "\r\n",
    "\\",
    String.raw`\r`,
    "\0",
    "\u2028",
    "\ud800",
    `prefix\r\n${String.raw`\r\\`}suffix`,
  ]) {
    assert.equal(
      decodeLibraryLiteralEditorValue(
        encodeLibraryLiteralEditorValue(value)),
      value);
  }
  assert.equal(
    decodeLibraryLiteralEditorValue(String.raw`\q`),
    String.raw`\q`);
});

test("library-literal completion distinguishes exhausted and bounded populations", () => {
  const request = withTerm(
    createQueryRequest("Contoso.*"),
    LIBRARY_LITERAL_TERM,
    "eq",
    "shared-literal-use-marker",
  );
  const render = (
    population:
      | "PrefixExhausted"
      | "MatchLimitReached"
      | "SourcePageLimitReached",
    complete: boolean,
  ) => renderPackageQueryView({
    state: {
      request,
      outcome: withCompletion(emptyOutcome(), {
        kind: "library-literal",
        population,
        candidateCount: 5,
        evaluatedCandidateCount: 5,
        notEvaluatedCount: 0,
        matchedPackageCount: 0,
        occurrenceCount: 0,
        semanticMissCount: 5,
        notApplicableCount: 0,
        failureCount: 0,
        complete,
      }),
    },
    availablePresets: FACETS,
    availableTerms: TERMS,
    escapeHtml,
  });

  const exhausted = render("PrefixExhausted", true);
  const head = render("MatchLimitReached", true);
  const bounded = render("SourcePageLimitReached", false);

  assert.match(exhausted, /No matching package libraries/);
  assert.match(exhausted, /prefix population exhausted/);
  assert.doesNotMatch(exhausted, /not a confirmed empty result/);
  assert.match(head, /match limit reached/);
  assert.doesNotMatch(head, /operation incomplete/);
  assert.match(
    bounded,
    /No matching package libraries in the completed work/);
  assert.match(bounded, /source page limit reached; operation incomplete/);
  assert.match(bounded, /not a confirmed empty result/);
  assert.notEqual(exhausted, bounded);
});

test("library-literal result footer discloses an incomplete population", () => {
  const request = withTerm(
    createQueryRequest("Contoso.*"),
    LIBRARY_LITERAL_TERM,
    "eq",
    "shared-literal-use-marker",
  );
  const html = renderPackageQueryView({
    state: {
      request,
      outcome: withCompletion(
        appendRows(emptyOutcome(), [row("Contoso.Package")]),
        {
          kind: "library-literal",
          population: "SourcePageLimitReached",
          candidateCount: 5,
          evaluatedCandidateCount: 5,
          notEvaluatedCount: 0,
          matchedPackageCount: 1,
          occurrenceCount: 1,
          semanticMissCount: 4,
          notApplicableCount: 0,
          failureCount: 0,
          complete: false,
        }),
    },
    availablePresets: FACETS,
    availableTerms: TERMS,
    escapeHtml,
  });

  assert.match(
    html,
    /1 matching package · 1 occurrence · source page limit reached; operation incomplete/);
});

test("active terms render above the product-issued available-term palette", () => {
  const request = withTerm(
    withTerm(
      createQueryRequest("Microsoft.*"),
      TERMS[0]!,
      "eq",
      "Microsoft.Extensions.Hosting"),
    TERMS[0]!,
    "eq",
    "Microsoft.Extensions.DependencyInjection");
  const html = renderPackageQueryView({
    state: {
      request,
      outcome: emptyOutcome(),
    },
    availablePresets: FACETS,
    availableTerms: TERMS,
    escapeHtml,
  });

  assert.ok(html.indexOf("<h2 id=\"query-active-terms-heading\">Active terms</h2>")
    < html.indexOf("<h2 id=\"query-available-terms-heading\">Available terms</h2>"));
  assert.match(html, /data-query-term-form="0"/);
  assert.match(html, /data-query-term-form="1"/);
  assert.match(html, /value="Microsoft\.Extensions\.Hosting"/);
  assert.match(html, /value="Microsoft\.Extensions\.DependencyInjection"/);
  assert.match(html, /data-query-term-add="depends"/);
  assert.match(html, /Add Direct dependency/);
});

test("an empty term draft is editable but not part of the executable request", () => {
  const request = createQueryRequest("Microsoft.*");
  const html = renderPackageQueryView({
    state: {
      request,
      outcome: emptyOutcome(),
      termDraft: {
        descriptor: TERMS[0]!,
        operator: "eq",
        value: "",
      },
      termEdits: [],
    },
    availablePresets: FACETS,
    availableTerms: TERMS,
    escapeHtml,
  });

  assert.deepEqual(request.terms, []);
  assert.match(html, /data-query-term-form="draft"/);
  assert.match(html, /data-query-term-draft-value/);
  assert.match(html, /placeholder="Microsoft\.Extensions\.Hosting"/);
});

test("pending term edits survive full view rerenders", () => {
  const request = withTerm(
    createQueryRequest("Microsoft.*"),
    TERMS[0]!,
    "eq",
    "Microsoft.Extensions.Hosting");
  const html = renderPackageQueryView({
    state: {
      request,
      outcome: emptyOutcome(),
      termDraft: {
        descriptor: TERMS[0]!,
        operator: "eq",
        value: "Microsoft.Extensions.Logging",
      },
      termEdits: [{
        operator: "eq",
        value: "Microsoft.Extensions.DependencyInjection",
      }],
    },
    availablePresets: FACETS,
    availableTerms: TERMS,
    escapeHtml,
  });

  assert.match(
    html,
    /data-query-term-form="0"[\s\S]*value="Microsoft\.Extensions\.DependencyInjection"/);
  assert.match(
    html,
    /data-query-term-form="draft"[\s\S]*value="Microsoft\.Extensions\.Logging"/);
});
test("candidate and local match bounds are independently disclosed before and during inspection", () => {
  for (const request of [
    null,
    withPreset(createQueryRequest(""), NUSPEC_FACET),
    withPreset(createQueryRequest(""), SKILL_FACET),
    { ...createQueryRequest(""), requestedMatchLimit: 7 },
  ]) {
    const html = renderPackageQueryView({
      state: { request, outcome: emptyOutcome() },
      availablePresets: FACETS,
      escapeHtml,
    });
    assert.ok(html.includes(`Candidate bound K: ${request?.requestedLimit ?? 200}`));
    assert.match(html, /exact IDs use one candidate/);
    assert.ok(html.includes(`Maximum matches N: ${request?.requestedMatchLimit ?? 100}`));
    assert.match(html, /The match limit does not change prefix capacity/);
    assert.match(html, /Content facts download up to 20 candidate package archives/);
    assert.match(html, /Transitive dependency facts inspect up to 5 package candidates/);
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
              scope: "query",
              summary: null,
              properties: [{
                name: "value",
                value: "Source selection and order from the producer",
              }],
              number: null,
            }],
          },
          {
            ...row("Producer.Neighbor"),
            tier: "search-metadata",
            totalDownloads,
            evidence: [{
              id: "producer.source-selection",
              scope: "query",
              summary: null,
              properties: [{
                name: "value",
                value: "Source selection and order from the producer",
              }],
              number: null,
            }],
          },
        ]),
      },
      availablePresets: [],
      escapeHtml,
    });
    assert.match(html, /query-tier-search-metadata">search-metadata</);
    assert.match(html, /Source selection and order from the producer/);
    assert.equal((html.match(/Source selection and order from the producer/g)
      ?? []).length, 1);
    assert.equal((html.match(/<article class="query-row"/g) ?? []).length, 2);
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

test("a large outcome mounts only the scrolled row window while retaining total accounting", () => {
  const rows = Array.from(
    { length: 100 },
    (_, index) => row(`Package.${index.toString().padStart(3, "0")}`));
  const html = renderPackageQueryView({
    state: {
      request: createQueryRequest("Package.*"),
      outcome: appendRows(emptyOutcome(), rows),
    },
    viewport: {
      scrollTop: 50 * 180,
      clientHeight: 800,
      surfaceTop: 0,
      rowExtent: 180,
      anchorRowIndex: null,
      anchorOffsetTop: null,
    },
    availablePresets: [],
    escapeHtml,
  });

  assert.equal((html.match(/<article/g) ?? []).length, 30);
  assert.match(html, /<h2>Package\.045<\/h2>/);
  assert.match(html, /<h2>Package\.074<\/h2>/);
  assert.doesNotMatch(html, /<h2>Package\.044<\/h2>/);
  assert.doesNotMatch(html, /<h2>Package\.075<\/h2>/);
  assert.match(html, /aria-label="Packages 46 through 75 of 100"/);
  assert.match(html, /aria-posinset="46"/);
  assert.match(html, /aria-setsize="100"/);
  assert.match(html, /100 packages · streaming…/);
  assert.match(html, /style="height:8100\.00px"/);
  assert.match(html, /style="height:4500\.00px"/);
});

test("query context renders once while package summaries remain on their cards", () => {
  const queryEvidence = {
    id: "producer.source-selection",
    scope: "query" as const,
    summary: null,
    properties: [{ name: "value", value: "Selected by producer ranking." }],
    number: null,
  };
  const first = {
    ...row("Contoso.First"),
    evidence: [
      queryEvidence,
      {
        id: "depends",
        scope: "package" as const,
        summary: {
          count: 4,
          preview: ["A", "B", "C"],
        },
        properties: [],
        number: null,
      },
    ] as const,
  };
  const second = {
    ...row("Contoso.Second"),
    evidence: [
      queryEvidence,
      {
        id: "skill",
        scope: "package" as const,
        summary: {
          count: 2,
          preview: ["skills/SKILL.md", "skills/build/SKILL.md"],
        },
        properties: [],
        number: null,
      },
    ] as const,
  };

  const html = renderPackageQueryView({
    state: {
      request: createQueryRequest("Contoso.*"),
      outcome: appendRows(emptyOutcome(), [first, second]),
    },
    availablePresets: [],
    escapeHtml,
  });

  assert.equal((html.match(/Selected by producer ranking\./g) ?? []).length, 1);
  assert.match(
    html,
    /<section class="query-context"[\s\S]*Selected by producer ranking\.[\s\S]*<div class="query-list"/);
  assert.equal((html.match(/4 dependency declarations: A, B, C \(\+1 more\)/g)
    ?? []).length, 1);
  assert.equal((html.match(/2 skill documents:/g) ?? []).length, 1);
  assert.doesNotMatch(
    html,
    /<article class="query-row">[\s\S]*Selected by producer ranking\./);
});

test("bounded prefix completion text remains visible with or without rows", () => {
  for (const rows of [[], [row("Producer.Result")]]) {
    const reason = "first 20 matches";
    const html = renderPackageQueryView({
      state: {
        request: createQueryRequest("Contoso.*"),
        outcome: withCompletion(appendRows(emptyOutcome(), rows), {
          kind: "bounded",
          reason,
        }),
      },
      availablePresets: [],
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
    availablePresets: [],
    escapeHtml,
  });

  assert.match(html, /No package selected/);
  assert.match(html, /No fallback search was used/);
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
    availablePresets: [],
    escapeHtml,
  });

  assert.match(html, /Exact package inspection incomplete/);
  assert.match(html, /not a confirmed empty result/);
  assert.match(html, /The package manifest could not be acquired/);
  assert.match(html, /No fallback search was used/);
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
      availablePresets: [],
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

test("multiple semantic answers render as distinct list items", () => {
  const html = renderPackageQueryView({
    state: {
      request: createQueryRequest(""),
      outcome: appendRows(emptyOutcome(), [{
        ...row("Producer.Result"),
        answers: [
          { id: "license", value: "MIT" },
          { id: "downloads", value: "1m" },
        ],
      }]),
    },
    availablePresets: [],
    escapeHtml,
  });

  assert.match(
    html,
    /<ul class="query-answers" aria-label="Answers"><li class="query-answer">MIT<\/li><li class="query-answer">1m<\/li><\/ul>/);
  assert.doesNotMatch(html, /<span class="query-answer">MIT<\/span><span/);
});

test("the query header keeps product navigation collapsed beside Back", () => {
  const html = renderPackageQueryView({
    state: initialQueryState(),
    availablePresets: FACETS,
    escapeHtml,
  });

  assert.doesNotMatch(html, /application-scope/);
  assert.match(html, /id="package-query-back" type="button">Back<\/button>/);
  assert.match(
    html,
    /id="package-query-product" class="brand" type="button"[\s\S]*data-product-destination="home"[\s\S]*data-product-destination="query"[\s\S]*data-product-destination="workspace"[\s\S]*data-product-destination="activity"/);
});

test("a packageId cannot break out of the row's HTML attribute context via a quote", () => {
  const maliciousRow: QueryResultRow = {
    ...row('Microsoft.Bcl.AsyncInterfaces" onmouseover="alert(1)'),
  };
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: appendRows(emptyOutcome(), [maliciousRow]),
  };

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

  assert.ok(!html.includes('" onmouseover="alert(1)'), "raw quote must not break out of the attribute");
  assert.match(html, /&quot; onmouseover=&quot;alert\(1\)/);
});

test("a streaming result renders rows, product presets, and the streaming footer", () => {
  const state: PackageQueryState = {
    request: withPreset(createQueryRequest("Microsoft."), NUSPEC_FACET),
    outcome: appendRows(emptyOutcome(), [row("Microsoft.Bcl.AsyncInterfaces")]),
  };

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

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
    availablePresets: FACETS,
    escapeHtml,
  });
  const withRows = renderPackageQueryView({
    state: {
      request: createQueryRequest("System.*"),
      outcome: appendRows(progress, [row("System.Text.Json")]),
    },
    availablePresets: FACETS,
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

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

  assert.match(html, />contoso\.example\/v3</);
  assert.doesNotMatch(html, />nuget\.org<\/span>/);
});

test("preset buttons expose pressed state without shipping promoted placeholders", () => {
  const state: PackageQueryState = {
    request: withPreset(
      createQueryRequest("Microsoft."),
      NUSPEC_FACET),
    outcome: appendRows(emptyOutcome(), [row("A")]),
  };

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

  assert.match(
    html,
    /data-query-preset="readme:eq:true"[\s\S]*aria-pressed="true"/);
  assert.match(
    html,
    /data-query-preset="downloads:eq:1m"[\s\S]*aria-pressed="false"/);
  assert.doesNotMatch(html, /promoted|Deepen/);
});

test("tool format presets render as one independently selectable segmented control", () => {
  const state: PackageQueryState = {
    request: withPreset(
      createQueryRequest("Microsoft."),
      TOOL_FACETS[2]!),
    outcome: appendRows(emptyOutcome(), [row("A")]),
  };

  const html = renderPackageQueryView({
    state,
    availablePresets: FACETS,
    escapeHtml,
  });

  assert.match(
    html,
    /class="query-preset-group"[\s\S]*role="group"[\s\S]*aria-label="\.NET tool format"/);
  assert.match(
    html,
    /data-query-preset="tool:eq:true"[\s\S]*>\s*\.NET Tool\s*<\/button>[\s\S]*data-query-preset="tool-format:eq:v1"[\s\S]*>\s*v1\s*<\/button>[\s\S]*data-query-preset="tool-format:eq:v2"[\s\S]*aria-pressed="true"[\s\S]*>\s*v2\s*<\/button>/);
  assert.match(html, />\s*embedded SKILL\.md\s*<\/button>/);
  assert.match(
    html,
    /Content facts download up to 20 candidate package archives/);
});

test("preset markup classes have matching Package Query style selectors", () => {
  const html = renderPackageQueryView({
    state: initialQueryState(),
    availablePresets: FACETS,
    escapeHtml,
  });

  for (const className of [
    "query-preset",
    "query-preset-group",
    "query-preset-rail",
    "query-preset-disclosure",
  ]) {
    assert.match(html, new RegExp(`class="[^"]*${className}`));
    assert.match(styles, new RegExp(`\\.${className}(?:[\\s:{.,]|$)`));
  }

  assert.doesNotMatch(
    styles,
    /\.query-facet(?:-group|-rail)?(?:[\s:{.,]|$)/);
});

test("package-content results disclose their evidence tier", () => {
  const state: PackageQueryState = {
    request: withPreset(createQueryRequest("Contoso."), SKILL_FACET),
    outcome: appendRows(emptyOutcome(), [{
      ...row("Contoso.Skill"),
      tier: "package-content",
    }]),
  };

  const html = renderPackageQueryView({
    state,
    availablePresets: FACETS,
    escapeHtml,
  });

  assert.match(html, /query-tier-package-content">package-content</);
});

test("a preset catalog failure remains visible beside an empty preset rail", () => {
  const html = renderPackageQueryView({
    state: initialQueryState(),
    availablePresets: [],
    navigationError: "Package-query facts are unavailable: catalog failed.",
    escapeHtml,
  });

  assert.match(html, /Package-query facts are unavailable: catalog failed/);
  assert.match(html, /class="query-presets"><\/div>/);
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

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

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

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

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

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

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

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

  assert.match(html, /bounded: first 1,500 relevance-ranked ids/);
  assert.doesNotMatch(html, /data-query-cancel="1"/);
});

test("no matches after completion renders the empty-match state, not the composing state", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(emptyOutcome(), { kind: "exhausted" }),
  };

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

  assert.match(html, /No matches/);
  // The preset rail stays mounted so the empty-state guidance is actionable.
  assert.match(html, /query-preset-rail/);
  assert.match(html, /1M\+ downloads/);
});

test("a bounded-complete zero-row outcome never claims plain 'no matches' — it names the bound", () => {
  const state: PackageQueryState = {
    request: createQueryRequest("Microsoft."),
    outcome: withCompletion(emptyOutcome(), { kind: "bounded", reason: "first 1,500 relevance-ranked ids" }),
  };

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

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

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

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

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

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

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

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

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

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

  const html = renderPackageQueryView({ state, availablePresets: FACETS, escapeHtml });

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
  open = false;
  parentDisclosure: FakeElement | null = null;
  value = "";
  checked = false;
  selectionStart: number | null = null;
  selectionEnd: number | null = null;
  selectionDirection: "forward" | "backward" | "none" | null = null;
  selectionRange: readonly [number, number] | null = null;
  customValidity = "";
  validityReports = 0;
  selectedOptions: { item(index: number): FakeElement | null } = {
    item: () => null,
  };
  private readonly listeners = new Map<string, EventListener[]>();
  private readonly elements = new Map<string, FakeElement[]>();

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

  add(selector: string, ...elements: FakeElement[]) {
    this.elements.set(selector, elements);
    return elements;
  }

  // oxlint-disable-next-line typescript/no-unnecessary-type-parameters
  querySelector<T extends Element>(selector: string): T | null {
    const found = this.elements.get(selector)?.[0] ?? null;
    // Test fake implements exactly the subset consumed by the binder.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return found as unknown as T | null;
  }

  // oxlint-disable-next-line typescript/no-unnecessary-type-parameters
  closest<T extends Element>(selector: string): T | null {
    const found = selector === "details.query-library-literal"
      ? this.parentDisclosure
      : null;
    // Test fake implements exactly the subset consumed by focus restoration.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return found as unknown as T | null;
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

  setSelectionRange(
    start: number,
    end: number,
    direction: "forward" | "backward" | "none" = "none",
  ) {
    this.selectionRange = [start, end];
    this.selectionDirection = direction;
  }

  setCustomValidity(message: string) {
    this.customValidity = message;
  }

  reportValidity() {
    this.validityReports++;
    return this.customValidity.length === 0;
  }
}

if (!("HTMLTextAreaElement" in globalThis)) {
  Object.defineProperty(globalThis, "HTMLTextAreaElement", {
    configurable: true,
    value: FakeElement,
  });
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
      active: new FakeElement({}, "package-query-prerelease"),
      selector: "#package-query-prerelease",
      replacement: new FakeElement({}, "package-query-prerelease"),
    },
    {
      active: new FakeElement({ queryPreset: "downloads:eq:1m" }),
      selector: "[data-query-preset]",
      replacement: new FakeElement({ queryPreset: "downloads:eq:1m" }),
    },
    {
      active: new FakeElement({ queryTermAdd: "depends" }),
      selector: "[data-query-term-add]",
      replacement: new FakeElement({ queryTermAdd: "depends" }),
    },
    {
      active: new FakeElement({
        queryTermIndex: "1",
        queryTermControl: "value",
      }),
      selector: "[data-query-term-control]",
      replacement: new FakeElement({
        queryTermIndex: "1",
        queryTermControl: "value",
      }),
    },
    {
      active: new FakeElement({ queryTermDraftControl: "value" }),
      selector: "[data-query-term-draft-control]",
      replacement: new FakeElement({ queryTermDraftControl: "value" }),
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
  const cases = [new FakeElement({}, "package-query-prerelease")];

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

test("a virtualized query row keeps results focus across later renders", () => {
  const active = new FakeElement({
    queryRowOpen: "Vanished.Package",
    queryRowVersion: "1.0.0",
  });
  const firstResults = new FakeElement({}, "package-query-results");
  const firstRoot = new FakeRoot(active);
  firstRoot.add("#package-query-results", firstResults);
  // Test fake implements the Document and ParentNode subset consumed by the helpers.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const firstDocument = firstRoot as unknown as Document;

  const rowSnapshot = capturePackageQueryFocus(firstDocument);
  const rowRestoration = restorePackageQueryFocus(firstDocument, rowSnapshot);

  assert.equal(rowRestoration, "fallback");
  assert.equal(firstResults.focusCount, 1);

  const nextResults = new FakeElement({}, "package-query-results");
  const prefix = new FakeElement({}, "package-query-prefix");
  const nextRoot = new FakeRoot(firstResults);
  nextRoot.add("#package-query-results", nextResults);
  nextRoot.add("#package-query-prefix", prefix);
  // Test fake implements the Document and ParentNode subset consumed by the helpers.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const nextDocument = nextRoot as unknown as Document;

  const resultsSnapshot = capturePackageQueryFocus(nextDocument);
  const resultsRestoration = restorePackageQueryFocus(
    nextDocument,
    resultsSnapshot);

  assert.deepEqual(resultsSnapshot, { kind: "results" });
  assert.equal(resultsRestoration, "restored");
  assert.equal(nextResults.focusCount, 1);
  assert.equal(prefix.focusCount, 0);
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

test("query text controls preserve selection across a full render", () => {
  for (const id of [
    "package-query-prefix",
    "package-query-library-tfm",
  ]) {
    const active = new FakeElement({}, id);
    active.value = `${id}-live`;
    active.selectionStart = 3;
    active.selectionEnd = 8;
    active.selectionDirection = "backward";
    const replacement = new FakeElement({}, id);
    replacement.value = `${id}-rendered`;
    const root = new FakeRoot(active);
    root.add(`#${id}`, replacement);
    // Test fake implements the Document and ParentNode subset consumed by the helpers.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    const documentRoot = root as unknown as Document;

    const snapshot = capturePackageQueryFocus(documentRoot);
    restorePackageQueryFocus(documentRoot, snapshot);

    assert.equal(replacement.focusCount, 1);
    assert.equal(replacement.value, `${id}-live`);
    assert.deepEqual(replacement.selectionRange, [3, 8]);
    assert.equal(replacement.selectionDirection, "backward");
  }
});

test("term editors preserve live values and backward selections across a full render", () => {
  const cases = [
    {
      active: new FakeElement({
        queryTermIndex: "1",
        queryTermControl: "value",
      }),
      selector: "[data-query-term-control]",
      replacement: new FakeElement({
        queryTermIndex: "1",
        queryTermControl: "value",
      }),
    },
    {
      active: new FakeElement({ queryTermDraftControl: "value" }),
      selector: "[data-query-term-draft-control]",
      replacement: new FakeElement({ queryTermDraftControl: "value" }),
    },
  ];

  for (const scenario of cases) {
    scenario.active.value = "Microsoft.Extensions.Hosting";
    scenario.active.selectionStart = 10;
    scenario.active.selectionEnd = 20;
    scenario.active.selectionDirection = "backward";
    scenario.replacement.value = "rendered-state";
    const root = new FakeRoot(scenario.active);
    root.add(scenario.selector, scenario.replacement);
    const documentRoot = fakeDom.document(root);

    const snapshot = capturePackageQueryFocus(documentRoot);
    const restoration = restorePackageQueryFocus(documentRoot, snapshot);

    assert.equal(restoration, "restored");
    assert.equal(
      scenario.replacement.value,
      "Microsoft.Extensions.Hosting");
    assert.deepEqual(scenario.replacement.selectionRange, [10, 20]);
    assert.equal(scenario.replacement.selectionDirection, "backward");
  }
});

test("multiline term editor spelling survives a full render", () => {
  const active = new FakeElement({
    queryTermIndex: "0",
    queryTermControl: "value",
  });
  active.value = String.raw`\r\\tail`;
  active.selectionStart = 2;
  active.selectionEnd = 4;
  const replacement = new FakeElement({
    queryTermIndex: "0",
    queryTermControl: "value",
  });
  replacement.value = "canonicalized";
  const root = new FakeRoot(active);
  root.add("[data-query-term-control]", replacement);
  // Test fake implements the Document and ParentNode subset consumed by the helpers.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const documentRoot = root as unknown as Document;

  const snapshot = capturePackageQueryFocus(documentRoot);
  restorePackageQueryFocus(documentRoot, snapshot);

  assert.equal(replacement.value, String.raw`\r\\tail`);
  assert.deepEqual(replacement.selectionRange, [2, 4]);
});

test("library target editor restores focus without using fallback", () => {
  const active = new FakeElement({}, "package-query-library-tfm");
  active.selectionStart = 1;
  active.selectionEnd = 2;
  const replacement = new FakeElement({}, "package-query-library-tfm");
  const prefix = new FakeElement({}, "package-query-prefix");
  prefix.value = "Contoso.Package";
  const root = new FakeRoot(active);
  root.add("#package-query-library-tfm", replacement);
  root.add("#package-query-prefix", prefix);
  const documentRoot = fakeDom.document(root);

  const snapshot = capturePackageQueryFocus(documentRoot);
  const restoration = restorePackageQueryFocus(documentRoot, snapshot);

  assert.equal(restoration, "restored");
  assert.equal(replacement.focusCount, 1);
  assert.equal(prefix.focusCount, 0);
  assert.equal(prefix.value, "Contoso.Package");
});

test("removed library target snapshots never modify a fallback control", () => {
  const active = new FakeElement({}, "package-query-library-tfm");
  active.value = "";
  active.selectionStart = 0;
  active.selectionEnd = 0;
  const replacement = new FakeElement({}, "package-query-library-tfm");
  replacement.rendered = false;
  const prefix = new FakeElement({}, "package-query-prefix");
  prefix.value = "Contoso.Package";
  const root = new FakeRoot(active);
  root.add("#package-query-library-tfm", replacement);
  root.add("#package-query-prefix", prefix);
  // Test fake implements the Document and ParentNode subset consumed by the helpers.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const documentRoot = root as unknown as Document;

  const snapshot = capturePackageQueryFocus(documentRoot);
  const restoration = restorePackageQueryFocus(documentRoot, snapshot);

  assert.equal(restoration, "fallback");
  assert.equal(prefix.value, "Contoso.Package");
  assert.equal(prefix.selectionRange, null);
});

test("removed term editor snapshots never modify the prefix fallback", () => {
  const active = new FakeElement({
    queryTermIndex: "0",
    queryTermControl: "value",
  });
  active.value = "Unapplied.Dependency";
  active.selectionStart = 3;
  active.selectionEnd = 9;
  const prefix = new FakeElement({}, "package-query-prefix");
  prefix.value = "Contoso.*";
  const root = new FakeRoot(active);
  root.add("#package-query-prefix", prefix);
  const documentRoot = fakeDom.document(root);

  const snapshot = capturePackageQueryFocus(documentRoot);
  const restoration = restorePackageQueryFocus(documentRoot, snapshot);

  assert.equal(restoration, "fallback");
  assert.equal(prefix.value, "Contoso.*");
  assert.equal(prefix.selectionRange, null);
});

test("bindPackageQueryView wires back, row-open, preset, and cancel", () => {
  const root = new FakeRoot();
  const [back] = root.add("#package-query-back", new FakeElement());
  const [open] = root.add("[data-query-row-open]", new FakeElement({ queryRowOpen: "A", queryRowVersion: "1.0.0" }));
  const [preset] = root.add("[data-query-preset]", new FakeElement({ queryPreset: "readme:eq:true" }));
  const [cancel] = root.add("[data-query-cancel]", new FakeElement());

  const calls: string[] = [];
  const actions: PackageQueryBindingActions = {
    onBack: () => calls.push("back"),
    onCancel: () => calls.push("cancel"),
    onPresetToggle: key => calls.push(`preset:${key}`),
    onLibraryTargetInput: () => {},
    onPrefixInput: () => {},
    onResultPressure: () => calls.push("pressure"),
    onResultViewportChange: () => calls.push("viewport"),
    onRowOpen: (id, version) => calls.push(`open:${id}:${version}`),
    onRun: () => {},
    onSourceChange: () => {},
  };

  bindPackageQueryView(fakeDom.parentNode(root), actions);

  back?.dispatch("click");
  open?.dispatch("click");
  preset?.dispatch("click");
  cancel?.dispatch("click");

  assert.deepEqual(calls, [
    "back",
    "open:A:1.0.0",
    "preset:readme:eq:true",
    "cancel",
  ]);
});

test("bindPackageQueryView forwards library target edits independently", () => {
  const root = new FakeRoot();
  const targetFramework =
    new FakeElement({}, "package-query-library-tfm");
  root.add("#package-query-library-tfm", targetFramework);
  const calls: string[] = [];

  bindPackageQueryView(fakeDom.parentNode(root), {
    onBack: () => {},
    onCancel: () => {},
    onPresetToggle: () => {},
    onLibraryTargetInput: value => calls.push(value),
    onPrefixInput: () => {},
    onResultPressure: () => {},
    onResultViewportChange: () => {},
    onRowOpen: () => {},
    onRun: () => {},
    onSourceChange: () => {},
  });

  targetFramework.value = "net10.0";
  targetFramework.dispatch("input");

  assert.deepEqual(calls, ["net10.0"]);
});

test("bindPackageQueryView defers term updates and render resumption during composition", () => {
  const value = new FakeElement();
  const operator = new FakeElement();
  operator.value = "eq";
  const form = new FakeElement({ queryTermForm: "0" });
  form.add("[data-query-term-value]", value);
  form.add("[data-query-term-operator]", operator);
  const root = new FakeRoot(value);
  root.add("[data-query-term-form]", form);
  const calls: string[] = [];

  bindPackageQueryView(fakeDom.parentNode(root), {
    onBack: () => {},
    onCancel: () => {},
    onPresetToggle: () => {},
    onLibraryTargetInput: () => {},
    onPrefixInput: () => {},
    onResultPressure: () => {},
    onResultViewportChange: () => {},
    onRowOpen: () => {},
    onRun: () => {},
    onSourceChange: () => {},
    onTermEdit: (index, termOperator, termValue) =>
      calls.push(`edit:${index}:${termOperator}:${termValue}`),
    onEditorCompositionEnd: () => calls.push("resume"),
  });

  value.value = "に";
  value.dispatch("input", fakeDom.event({ isComposing: true }));
  assert.equal(
    packageQueryEditorCompositionActive(fakeDom.document(root)),
    true);
  assert.deepEqual(calls, []);

  value.value = "日本";
  value.dispatch("compositionend");

  assert.equal(
    packageQueryEditorCompositionActive(fakeDom.document(root)),
    false);
  assert.deepEqual(calls, ["edit:0:eq:日本", "resume"]);
});

test("bindPackageQueryView ignores an unpaired term compositionend", () => {
  const value = new FakeElement();
  const operator = new FakeElement();
  operator.value = "eq";
  const form = new FakeElement({ queryTermForm: "0" });
  form.add("[data-query-term-value]", value);
  form.add("[data-query-term-operator]", operator);
  const root = new FakeRoot(value);
  root.add("[data-query-term-form]", form);
  const calls: string[] = [];

  bindPackageQueryView(fakeDom.parentNode(root), {
    onBack: () => {},
    onCancel: () => {},
    onPresetToggle: () => {},
    onLibraryTargetInput: () => {},
    onPrefixInput: () => {},
    onResultPressure: () => {},
    onResultViewportChange: () => {},
    onRowOpen: () => {},
    onRun: () => {},
    onSourceChange: () => {},
    onTermEdit: () => calls.push("edit"),
    onEditorCompositionEnd: () => calls.push("resume"),
  });

  value.dispatch("compositionend");

  assert.deepEqual(calls, []);
});

test("Package Query rendering resumes once after composition settles", () => {
  let frameCallback: (() => void) | null = null;
  let composing = true;
  let open = true;
  let streamRenderCount = 0;
  let fullRenderCount = 0;
  const cancelled: number[] = [];
  const scheduler = createPackageQueryRenderScheduler({
    requestFrame: callback => {
      assert.equal(frameCallback, null);
      frameCallback = callback;
      return 7;
    },
    cancelFrame: handle => {
      cancelled.push(handle);
      frameCallback = null;
    },
    shouldRender: () => open,
    compositionActive: () => composing,
    renderStream: () => streamRenderCount++,
    renderFull: () => fullRenderCount++,
  });
  const runFrame = () => {
    const callback = frameCallback;
    assert.notEqual(callback, null);
    frameCallback = null;
    callback?.();
  };

  scheduler.scheduleStream();
  runFrame();
  assert.equal(streamRenderCount, 0);

  scheduler.scheduleStream();
  assert.equal(frameCallback, null);

  scheduler.cancel();
  composing = false;
  scheduler.scheduleStream();
  runFrame();
  assert.equal(streamRenderCount, 1);

  composing = true;
  scheduler.scheduleStream();
  runFrame();
  assert.equal(streamRenderCount, 1);

  composing = false;
  scheduler.resume();
  assert.notEqual(frameCallback, null);
  runFrame();
  assert.equal(streamRenderCount, 2);

  scheduler.resume();
  assert.equal(frameCallback, null);

  scheduler.scheduleStream();
  scheduler.cancel();
  assert.deepEqual(cancelled, [7]);
  assert.equal(frameCallback, null);

  open = false;
  scheduler.scheduleStream();
  runFrame();
  assert.equal(streamRenderCount, 2);
  assert.equal(fullRenderCount, 0);
});

test("Package Query full rendering supersedes stream work and defers during composition", () => {
  let frameCallback: (() => void) | null = null;
  let composing = true;
  let open = true;
  let streamRenderCount = 0;
  let fullRenderCount = 0;
  const cancelled: number[] = [];
  const scheduler = createPackageQueryRenderScheduler({
    requestFrame: callback => {
      assert.equal(frameCallback, null);
      frameCallback = callback;
      return 11;
    },
    cancelFrame: handle => {
      cancelled.push(handle);
      frameCallback = null;
    },
    shouldRender: () => open,
    compositionActive: () => composing,
    renderStream: () => streamRenderCount++,
    renderFull: () => fullRenderCount++,
  });

  scheduler.scheduleStream();
  scheduler.renderFull();
  assert.deepEqual(cancelled, [11]);
  assert.equal(frameCallback, null);
  assert.equal(fullRenderCount, 0);

  scheduler.scheduleStream();
  scheduler.renderFull();
  assert.equal(frameCallback, null);
  assert.equal(fullRenderCount, 0);

  composing = false;
  scheduler.resume();
  assert.equal(fullRenderCount, 1);
  assert.equal(streamRenderCount, 0);

  scheduler.resume();
  assert.equal(fullRenderCount, 1);

  composing = true;
  scheduler.renderFull();
  open = false;
  composing = false;
  scheduler.resume();
  assert.equal(fullRenderCount, 1);

  open = true;
  scheduler.scheduleStream();
  scheduler.renderFull();
  assert.deepEqual(cancelled, [11, 11]);
  assert.equal(fullRenderCount, 2);

  open = false;
  scheduler.renderFull();
  assert.equal(fullRenderCount, 2);
});

test("bindPackageQueryView applies exact term values and keeps empty drafts idle", () => {
  const root = new FakeRoot();
  const prefix = new FakeElement({}, "package-query-prefix");
  prefix.value = "Microsoft.*";
  const add = new FakeElement({ queryTermAdd: "depends" });
  const form = new FakeElement({ queryTermForm: "draft" });
  const value = new FakeElement();
  const operator = new FakeElement();
  operator.value = "eq";
  form.add("[data-query-term-value]", value);
  form.add("[data-query-term-operator]", operator);
  const remove = new FakeElement({ queryTermRemove: "0" });
  const cancel = new FakeElement();
  root.add("#package-query-prefix", prefix);
  root.add("[data-query-term-add]", add);
  root.add("[data-query-term-form]", form);
  root.add("[data-query-term-remove]", remove);
  root.add("[data-query-term-draft-cancel]", cancel);
  const calls: string[] = [];

  bindPackageQueryView(fakeDom.parentNode(root), {
    onBack: () => {},
    onCancel: () => {},
    onPresetToggle: () => {},
    onLibraryTargetInput: () => {},
    onPrefixInput: () => {},
    onResultPressure: () => {},
    onResultViewportChange: () => {},
    onRowOpen: () => {},
    onRun: () => {},
    onSourceChange: () => {},
    onTermAdd: key => calls.push(`add:${key}`),
    onTermApply: (index, termOperator, termValue, searchText) =>
      calls.push(`apply:${index}:${termOperator}:${termValue}:${searchText}`),
    onTermEdit: (index, termOperator, termValue) =>
      calls.push(`edit:${index}:${termOperator}:${termValue}`),
    onTermDraftCancel: () => calls.push("draft-cancel"),
    onTermRemove: (index, searchText) =>
      calls.push(`remove:${index}:${searchText}`),
  });

  add.dispatch("click");
  form.dispatch("submit", fakeDom.event({ preventDefault() {} }));
  assert.equal(value.customValidity, "Enter a term value.");
  assert.equal(value.validityReports, 1);
  value.value = "  Microsoft.Extensions.Hosting  ";
  value.dispatch("input");
  assert.equal(value.customValidity, "");
  form.dispatch("submit", fakeDom.event({ preventDefault() {} }));
  remove.dispatch("click");
  cancel.dispatch("click");

  assert.deepEqual(calls, [
    "add:depends",
    "edit:null:eq:  Microsoft.Extensions.Hosting  ",
    "apply:null:eq:  Microsoft.Extensions.Hosting  :Microsoft.*",
    "remove:0:Microsoft.*",
    "draft-cancel",
  ]);
});
test("assembly row binding forwards the exact opaque Root request", () => {
  const root = new FakeRoot();
  const rootRequest =
    "{\"kind\":\"package\",\"selection\":{\"framework\":\"net10.0\"}}";
  const [open] = root.add("[data-query-row-open]", new FakeElement({
    queryRowOpen: "Contoso.Match",
    queryRowVersion: "1.2.3",
    queryRootRequest: rootRequest,
  }));
  const calls: unknown[][] = [];

  bindPackageQueryView(fakeDom.parentNode(root), {
    onBack: () => {},
    onCancel: () => {},
    onPresetToggle: () => {},
    onLibraryTargetInput: () => {},
    onPrefixInput: () => {},
    onResultPressure: () => {},
    onResultViewportChange: () => {},
    onRowOpen: (...args) => calls.push(args),
    onRun: () => {},
    onSourceChange: () => {},
  });
  open?.dispatch("click");

  assert.deepEqual(calls, [[
    "Contoso.Match",
    "1.2.3",
    rootRequest,
  ]]);
});

test("prerelease changes forward the selection and current unmodified package text", () => {
  const root = new FakeRoot();
  const input = new FakeElement({}, "package-query-prefix");
  const prerelease = new FakeElement({}, "package-query-prerelease");
  root.add("#package-query-prefix", input);
  root.add("#package-query-prerelease", prerelease);
  const calls: {
    selection: Partial<QuerySourceSelection>;
    searchText: string;
  }[] = [];
  bindPackageQueryView(fakeDom.parentNode(root), {
    onBack: () => {},
    onCancel: () => {},
    onPresetToggle: () => assert.fail("source controls are not inspection presets"),
    onLibraryTargetInput: () => {},
    onPrefixInput: () => {},
    onResultPressure: () => {},
    onResultViewportChange: () => {},
    onRowOpen: () => {},
    onRun: () => assert.fail("source changes use their own action"),
    onSourceChange: (selection, searchText) => calls.push({ selection, searchText }),
  });

  input.value = " hosting libraries * ";
  prerelease.checked = true;
  prerelease.dispatch("change");
  prerelease.checked = false;
  prerelease.dispatch("change");

  assert.deepEqual(calls, [
    {
      selection: {
        includePrerelease: true,
      },
      searchText: " hosting libraries * ",
    },
    {
      selection: {
        includePrerelease: false,
      },
      searchText: " hosting libraries * ",
    },
  ]);
});

test("query form submits package text without a Gallery action", () => {
  const root = new FakeRoot();
  const form = new FakeElement({}, "package-query-form");
  const input = new FakeElement({}, "package-query-prefix");
  root.add("#package-query-form", form);
  root.add("#package-query-prefix", input);
  const calls: string[] = [];
  let prevented = 0;
  bindPackageQueryView(fakeDom.parentNode(root), {
    onBack: () => {},
    onCancel: () => {},
    onPresetToggle: () => {},
    onLibraryTargetInput: () => {},
    onPrefixInput: () => {},
    onResultPressure: () => {},
    onResultViewportChange: () => {},
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
  assert.deepEqual(calls, [
    "",
    " hosting libraries ",
    "System.*",
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
  let viewportChanges = 0;
  const binding = bindPackageQueryView(fakeDom.parentNode(root), {
    onBack: () => {},
    onCancel: () => {},
    onPresetToggle: () => {},
    onLibraryTargetInput: () => {},
    onPrefixInput: () => {},
    onResultPressure: () => { pressure++; },
    onResultViewportChange: () => { viewportChanges++; },
    onRowOpen: () => {},
    onRun: () => {},
    onSourceChange: () => {},
  });

  main.scrollTop = 401;
  main.dispatch("scroll");
  assert.equal(pressure, 1);
  assert.equal(viewportChanges, 1);

  binding.disconnect();
  main.dispatch("scroll");
  assert.equal(pressure, 1);
  assert.equal(viewportChanges, 1);
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
      onPresetToggle: () => {},
      onLibraryTargetInput: () => {},
      onPrefixInput: () => {},
      onResultPressure: () => { pressure++; },
      onResultViewportChange: () => {},
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
