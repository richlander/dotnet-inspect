import assert from "node:assert/strict";
import { test } from "node:test";
import { renderLibraryReferencesSurface, type LibraryReferencesOptions } from "../src/library-references.ts";
import type {
  BrowserAssemblyReferenceList,
  BrowserPackageDependencies,
} from "../src/facades/inspect-web-package.d.ts";

const referenceList: BrowserAssemblyReferenceList = {
  references: [
    { name: "System.Runtime", version: "10.0.0.0", culture: null, publicKeyToken: "b03f5f7f11d50a3a" },
    { name: "Example.Other", version: "1.2.3.4", culture: "fr", publicKeyToken: null },
  ],
};

const data: BrowserPackageDependencies = {
  package: "Example.Package", version: "1.0.0", activeFramework: "net10.0",
  assembly: "Example.Core",
  dependencyGroups: [], declarationFailures: [], dependencyGroupError: null,
  assemblyReferences: referenceList,
  compileLibrary: { status: "Selected", targetFramework: "net10.0", message: null },
};

function render(overrides: Partial<LibraryReferencesOptions> = {}) {
  return renderLibraryReferencesSurface({
    assemblyIdentity: "Example.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    assetPath: "lib/net10.0/Example.Core.dll",
    coordinate: "net10.0 / Example.Package@1.0.0",
    loading: false,
    error: "",
    data,
    referenceLibraryIds: [null, null],
    escapeHtml: value => String(value).replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;").replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;").replaceAll("'", "&#39;"),
    ...overrides,
  });
}

test("References uses a compact heading and graph followed by list", () => {
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

test("resolved assembly references open their exact Library destination", () => {
  const html = render({
    referenceLibraryIds: ["library:system-runtime", null],
  });
  assert.match(
    html,
    /<button[^>]*data-package-child-library="library:system-runtime"[^>]*>System\.Runtime<\/button>/);
  assert.match(html, /<span class="dep-name">Example\.Other<\/span>/);
  assert.doesNotMatch(
    html,
    /data-package-child-library="[^"]+"[^>]*>Example\.Other<\/button>/);
});

test("a single direct reference uses a singular count", () => {
  assert.match(render({ data: { ...data, assemblyReferences: {
    references: referenceList.references.slice(0, 1),
  } } }),
    /1 direct reference<\/p>/);
});

test("successful zero references retain the frame and explicit empty result", () => {
  const html = render({ data: { ...data, assemblyReferences: { references: [] } } });
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
  const html = render({ data: { ...data, assemblyReferences: "Cannot decode AssemblyRef." } });
  assert.match(html, /Inspection failed/);
  assert.match(html, /Cannot decode AssemblyRef/);
  assert.doesNotMatch(html, /<footer/);
  assert.doesNotMatch(html, /<ul|0 direct references|2 direct references/);
});

for (const [result, message] of [
  ["", "No failure details were provided."],
  [null, "The engine returned no assembly-reference result."],
] as const) {
  test(`reference result ${JSON.stringify(result)} is a settled failure, not initial loading`, () => {
    const html = render({ data: { ...data, assemblyReferences: result } });

    assert.match(html, /Inspection failed/);
    assert.ok(html.includes(message));
    assert.doesNotMatch(html, /<footer/);
    assert.doesNotMatch(html, /declares no direct|loader|Loading|<ul/);
  });
}

test("identity, reference fields, and diagnostics use the existing escape boundary", () => {
  const html = render({
    assemblyIdentity: 'Example."Core"',
    assetPath: "lib/Example&Core.dll",
    coordinate: "net10.0 / Example<Package>@1",
    data: { ...data, assemblyReferences: { references: [
      { name: "Example<Other>", version: "1&2", culture: "a&b", publicKeyToken: "c&d" },
    ] } },
  });
  assert.match(html, /Example&lt;Other&gt;/);
  assert.match(html, /1&amp;2.*a&amp;b.*c&amp;d/);
  assert.doesNotMatch(html, /Example<Other>|1&2|a&b|c&d/);
  assert.match(render({ error: "Read <failed>" }), /Read &lt;failed&gt;/);
});
