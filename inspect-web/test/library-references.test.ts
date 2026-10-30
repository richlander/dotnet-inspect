import assert from "node:assert/strict";
import { test } from "node:test";
import {
  createWorkspaceLibraryReferenceCandidates,
  renderLibraryReferencesSurface,
  resolveLibraryReferenceDestinations,
  type LibraryReferencesOptions,
  type WorkspaceLibraryReferenceCandidate,
} from "../src/library-references.ts";
import type {
  AssemblyReferenceRow,
} from "../src/facades/inspect-web-package.d.ts";
import type {
  LibraryReferenceInspectionEnvelope,
} from "../src/package-inspection.ts";
import { packageInertStringFixture } from "./inert-string-fixture.ts";

const references: readonly AssemblyReferenceRow[] = [
  { name: "System.Runtime", version: "10.0.0.0", culture: null, publicKeyToken: "b03f5f7f11d50a3a" },
  { name: "Example.Other", version: "1.2.3.4", culture: "fr", publicKeyToken: null },
];

function availableEnvelope(
  rows: readonly AssemblyReferenceRow[] = references,
): LibraryReferenceInspectionEnvelope {
  return {
    content: { outcome: "available", references: rows },
    share: {
      kind: "nonProjectable",
      fullUrl: null,
      packet: null,
      path: "",
      reason: packageInertStringFixture(
        "Library reference inspection is not projectable."),
    },
    diagnostics: [],
  };
}

const data = availableEnvelope();

function render(overrides: Partial<LibraryReferencesOptions> = {}) {
  return renderLibraryReferencesSurface({
    loading: false,
    error: "",
    data,
    referenceDestinations: [null, null],
    referenceIcons: [null, null],
    escapeHtml: value => String(value).replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;").replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;").replaceAll("'", "&#39;"),
    ...overrides,
  });
}

const candidates: readonly WorkspaceLibraryReferenceCandidate[] = [
  {
    packageKey: "example.package|1.0.0|net10.0",
    libraryId: "asset:system-runtime",
    name: "system.runtime",
    version: "10.0.0.0",
    culture: "neutral",
    publicKeyToken: "B03F5F7F11D50A3A",
  },
  {
    packageKey: "other.package|1.0.0|net10.0",
    libraryId: "asset:example-other",
    name: "Example.Other",
    version: "1.2.3.4",
    culture: "fr",
    publicKeyToken: null,
  },
];

test("exact normalized identities resolve to their admitted Workspace Library", () => {
  assert.deepEqual(
    resolveLibraryReferenceDestinations(references, candidates),
    [
      {
        packageKey: "example.package|1.0.0|net10.0",
        libraryId: "asset:system-runtime",
      },
      {
        packageKey: "other.package|1.0.0|net10.0",
        libraryId: "asset:example-other",
      },
    ]);
});

test("absent and ambiguous identities remain inert", () => {
  assert.deepEqual(
    resolveLibraryReferenceDestinations(references, [
      candidates[0]!,
      {
        ...candidates[0]!,
        packageKey: "duplicate.package|1.0.0|net10.0",
        libraryId: "asset:duplicate-system-runtime",
      },
    ]),
    [null, null]);
});

test("reference matching uses the metadata assembly name, not the asset filename", () => {
  const admittedLibraries = [{
    id: "asset:renamed",
    name: "File.Name.dll",
  }];
  const resolvedCandidates = createWorkspaceLibraryReferenceCandidates(
    "renamed.package|1.0.0|net10.0",
    admittedLibraries,
    [{
      id: "asset:renamed",
      name: "Manifest.Name",
      version: "1.0.0.0",
      culture: null,
      publicKeyToken: null,
    }]);
  assert.deepEqual(
    resolveLibraryReferenceDestinations([
      {
        name: "File.Name",
        version: "1.0.0.0",
        culture: null,
        publicKeyToken: null,
      },
      {
        name: "Manifest.Name",
        version: "1.0.0.0",
        culture: null,
        publicKeyToken: null,
      },
    ], resolvedCandidates),
    [
      null,
      {
        packageKey: "renamed.package|1.0.0|net10.0",
        libraryId: "asset:renamed",
      },
    ]);
});

test("References uses a compact heading, graph followed by list, and complete bottom context", () => {
  const html = render();
  assert.equal(html.match(/<h1\b/g)?.length, 1);
  assert.match(html, /<h1 id="library-references-title">References<\/h1>/);
  assert.match(html, /2 direct references/);
  assert.match(html, /<h2>Reference graph<\/h2>/);
  assert.match(html, /id="library-reference-graph-diagram"/);
  assert.match(html, /<h2>Assembly references<\/h2>/);
  assert.ok(html.indexOf("Reference graph") < html.indexOf("Assembly references"));
  assert.match(html, /System\.Runtime[\s\S]*10\.0\.0\.0.*neutral.*pkt b03f5f7f11d50a3a/);
  assert.match(html, /Example\.Other[\s\S]*1\.2\.3\.4.*fr.*unsigned/);
  assert.ok(html.indexOf("System.Runtime") < html.indexOf("Example.Other"));
  assert.doesNotMatch(html, /<footer/);
  assert.doesNotMatch(html, /type-heading/);
});

test("resolved assembly references expose their exact Workspace Library destination", () => {
  const html = render({
    referenceDestinations: [{
      packageKey: "example.package|1.0.0|net10.0",
      libraryId: "asset:system-runtime",
    }, null],
  });
  assert.match(
    html,
    /<button[^>]*data-library-reference-package="example\.package\|1\.0\.0\|net10\.0"[^>]*data-library-reference-library="asset:system-runtime"[^>]*>System\.Runtime<\/button>/);
  assert.match(html, /<span class="dep-name">Example\.Other<\/span>/);
  assert.doesNotMatch(
    html,
    /data-library-reference-library="[^"]+"[^>]*>Example\.Other<\/button>/);
});

test("only a uniquely attributed reference displays its package icon", () => {
  const html = render({
    referenceDestinations: [{
      packageKey: "example.package|1.0.0|net10.0",
      libraryId: "asset:system-runtime",
    }, null],
    referenceIcons: ["data:image/png;base64,aGVsbG8=", null],
  });
  assert.match(html,
    /<img class="dep-icon" src="data:image\/png;base64,aGVsbG8=" alt="" data-package-icon data-library-reference-icon="0">/);
  assert.doesNotMatch(html, /data-library-reference-icon="1"/);
});

test("a single direct reference uses a singular count", () => {
  assert.match(render({ data: availableEnvelope(references.slice(0, 1)) }),
    /1 direct reference<\/p>/);
});

test("successful zero references retain the frame and explicit empty result", () => {
  const html = render({ data: availableEnvelope([]) });
  assert.match(html, /0 direct references/);
  assert.match(html, /No direct references/);
  assert.match(html, /This assembly declares no direct AssemblyRef rows/);
  assert.doesNotMatch(html, /<footer/);
  assert.doesNotMatch(html, /<ul|failed/);
});

test("reading state takes precedence over retained data or error", () => {
  const html = render({ loading: true, error: "earlier failure" });
  assert.match(html, /Reading direct AssemblyRef rows/);
  assert.doesNotMatch(html, /<footer/);
  assert.doesNotMatch(html, /System.Runtime|earlier failure|2 direct references/);
});

test("initial pending state is not rendered as successful zero references", () => {
  const html = render({ data: null });
  assert.match(html, /Loading/);
  assert.doesNotMatch(html, /<footer/);
  assert.doesNotMatch(html, /0 direct references|No direct references|<ul/);
});

test("query failure remains visible instead of exposing retained rows", () => {
  const html = render({ error: "The reference query is unavailable." });
  assert.match(html, /Query failed/);
  assert.match(html, /The reference query is unavailable/);
  assert.doesNotMatch(html, /<footer/);
  assert.doesNotMatch(html, /<ul|2 direct references/);
});

test("inspection failure is not a zero-reference result", () => {
  const html = render({
    data: {
      ...data,
      content: {
        outcome: "rejected",
        reason: {
          kind: "InvalidImage",
          detail: packageInertStringFixture("Cannot decode AssemblyRef."),
        },
      },
    },
  });
  assert.match(html, /Inspection failed/);
  assert.match(html, /InvalidImage/);
  assert.match(html, /Cannot decode AssemblyRef/);
  assert.doesNotMatch(html, /<footer/);
  assert.doesNotMatch(html, /<ul|0 direct references|2 direct references/);
});

for (const [content, message] of [
  [{
    outcome: "failed" as const,
    reason: {
      kind: "Unexpected" as const,
      detail: packageInertStringFixture(""),
    },
  }, "No failure details were provided."],
  [null, "The engine returned no assembly-reference result."],
] as const) {
  test(`reference result ${JSON.stringify(content)} is a settled failure, not initial loading`, () => {
    const html = render({ data: { ...data, content } });

    assert.match(html, /Inspection failed/);
    assert.ok(html.includes(message));
    assert.doesNotMatch(html, /<footer/);
    assert.doesNotMatch(html, /declares no direct|loader|Loading|<ul/);
  });
}

test("reference fields and diagnostics use the existing escape boundary", () => {
  const html = render({
    data: availableEnvelope([
      { name: "Example<Other>", version: "1&2", culture: "a&b", publicKeyToken: "c&d" },
    ]),
  });
  assert.match(html, /Example&lt;Other&gt;/);
  assert.match(html, /1&amp;2.*a&amp;b.*c&amp;d/);
  assert.match(render({ error: "Read <failed>" }), /Read &lt;failed&gt;/);
});
