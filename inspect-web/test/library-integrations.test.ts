import assert from "node:assert/strict";
import { test } from "node:test";
import { renderLibraryIntegrationsSurface, type LibraryIntegrationsOptions } from "../src/library-integrations.ts";
import type { BrowserPackageIntegrations } from "../src/facades/inspect-web-analysis.d.ts";

const data: BrowserPackageIntegrations = {
  package: "Example.Package", version: "1.0.0", framework: "net10.0",
  categories: [
    { integration: "Dependency Injection", signals: [
      { name: "Example.Extensions.ZAdd()", shape: "Method", kind: "Extension method" },
      { name: "Example.Service", shape: "Type", kind: "Implementation" },
      { name: "Example.Extensions.Add()", shape: "Method", kind: "Extension method" },
    ] },
    { integration: "Logging", signals: [
      { name: "Example.Logger.Write(ILogger logger)", shape: "Method", kind: "Parameter" },
    ] },
  ],
  totalSignals: 4, isComplete: true, inspectionError: null,
  compileLibrary: { status: "Selected", targetFramework: "net10.0", message: null },
};

function render(overrides: Partial<LibraryIntegrationsOptions> = {}) {
  return renderLibraryIntegrationsSurface({
    libraryName: "Example.Core",
    assemblyIdentity: "Example.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    assetPath: "lib/net10.0/Example.Core.dll",
    coordinate: "net10.0 / Example.Package@1.0.0",
    requireLibrary: false, pickerHtml: "", loading: false, error: "", data,
    escapeHtml: value => String(value).replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;").replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;").replaceAll("'", "&#39;"),
    ...overrides,
  });
}

test("Integrations uses one quiet heading and bottom identity instead of duplicate summaries", () => {
  const html = render();
  assert.equal(html.match(/<h1\b/g)?.length, 1);
  assert.match(html, /<h1 id="library-integrations-title">Integrations<\/h1>/);
  assert.match(html, /2 categories.*4 signals/);
  assert.match(html, /library-integrations-scroll"><section class="integration-category"/);
  assert.match(html, /<footer[\s\S]*lib\/net10.0\/Example.Core.dll.*Example.Core, Version=1.0.0.0/);
  assert.match(html, /<footer[\s\S]*net10.0 \/ Example.Package@1.0.0/);
  assert.doesNotMatch(html, /type-heading|type-chip-list|Ecosystem integrations|library-integrations-controls/);
});

test("category order, type-first sorting, kind/name sorting and counts are retained without mutating data", () => {
  const original = JSON.stringify(data);
  const html = render();
  const names = [...html.matchAll(/class="signal-name">([^<]*)<\/span>/g)].map(match => match[1]);
  assert.deepEqual(names, ["Service", "Add()", "ZAdd()", "Write(ILogger logger)"]);
  assert.ok(html.indexOf(">Dependency Injection</h2>") < html.indexOf(">Logging</h2>"));
  assert.match(html, /1 type &middot; 2 APIs/);
  assert.match(html, /0 types &middot; 1 API/);
  assert.match(html, /signal-badge signal-type">T/);
  assert.match(html, /signal-badge signal-api">&#402;/);
  assert.match(html, /role="listitem" title="Example.Extensions.Add\(\).*Method.*Extension method"/);
  assert.equal(JSON.stringify(data), original);
});

test("generic and parameter suffixes stay on the short name", () => {
  const html = render({ data: {
    ...data, totalSignals: 1,
    categories: [{ integration: "Example", signals: [
      { name: "Acme.Widget.Make<Acme.Item>(System.String input)", shape: "Method", kind: "Factory" },
    ] }],
  } });
  assert.match(html, /1 category.*1 signal/);
  assert.match(html, /class="signal-name">Make&lt;Acme.Item&gt;\(System.String input\)<\/span>/);
  assert.match(html, /class="signal-ns">Acme.Widget<\/span>/);
});

test("platform selection stays outside the scroller and takes precedence over retained results", () => {
  const pickerHtml = '<select class="scope-select platform-library-select" data-platform-integrations-library aria-label="Select a platform library"><option>Example.Core</option></select>';
  const html = render({ requireLibrary: true, pickerHtml, loading: true, error: "earlier failure" });
  assert.match(html, /library-integrations-with-controls/);
  assert.match(html, /library-integrations-controls[\s\S]*data-platform-integrations-library[\s\S]*library-integrations-scroll/);
  assert.match(html, /Pick a library to scan/);
  assert.match(html, /<footer/);
  assert.doesNotMatch(html, /role="listitem"|earlier failure|4 signals/);
});

for (const [name, overrides, expected] of [
  ["loading", { loading: true, error: "earlier failure" }, /Reading the public surface of Example.Core/],
  ["query failure", { error: "Scan unavailable." }, /Integration scan failed[\s\S]*Scan unavailable/],
  ["pending", { data: null }, /Loading/],
] satisfies Array<[string, Partial<LibraryIntegrationsOptions>, RegExp]>) {
  test(`${name} retains the frame without exposing retained results or successful absence`, () => {
    const html = render(overrides);
    assert.match(html, expected);
    assert.match(html, /<footer/);
    assert.doesNotMatch(html, /role="listitem"|4 signals|No ecosystem integrations detected/);
  });
}

test("a complete empty scan retains its explicit absence result", () => {
  const html = render({ data: { ...data, categories: [], totalSignals: 0 } });
  assert.match(html, /0 categories.*0 signals/);
  assert.match(html, /No ecosystem integrations detected/);
  assert.doesNotMatch(html, /metadata-warning|partial/);
});

test("partial results retain rows and diagnostics without claiming completeness", () => {
  const html = render({ data: { ...data, isComplete: false, inspectionError: "Cannot read <participant>." } });
  assert.match(html, /4 signals.*partial/);
  assert.match(html, /This library could not be scanned completely/);
  assert.match(html, /Cannot read &lt;participant&gt;/);
  assert.equal(html.match(/role="listitem"/g)?.length, 4);
});

test("incomplete zero-row scans do not claim absence, even without diagnostic text", () => {
  for (const inspectionError of [null, "Participant unavailable."]) {
    const html = render({ data: { ...data, isComplete: false, inspectionError, categories: [], totalSignals: 0 } });
    assert.match(html, /Integration scan incomplete/);
    assert.match(html, /This library could not be scanned completely/);
    assert.doesNotMatch(html, /No ecosystem integrations detected|shows no known/);
  }
});

test("rendered facts, context and errors use the supplied text escape boundary", () => {
  const html = render({
    assemblyIdentity: 'Example."Core"', assetPath: "lib/Core&Other.dll",
    coordinate: "Example<Package>@1", error: "Read <failed>",
  });
  assert.match(html, /title="lib\/Core&amp;Other.dll.*Example.&quot;Core&quot;"/);
  assert.match(html, /Example&lt;Package&gt;/);
  assert.match(html, /Read &lt;failed&gt;/);
});
