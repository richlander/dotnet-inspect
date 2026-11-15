import assert from "node:assert/strict";
import test from "node:test";

import type {
  BrowserLibraryDocumentInspection,
} from "../src/facades/inspect-web-library.d.ts";
import {
  formatLibraryImageSize,
  libraryOverviewFactRows,
  renderLibraryOverview,
} from "../src/library-overview.ts";

const escape = (value: unknown) => String(value)
  .replaceAll("&", "&amp;")
  .replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;")
  .replaceAll('"', "&quot;");
const factRows = (rows: readonly (readonly [string, string])[]) =>
  `<dl class="fact-rows">${rows.map(([key, value]) =>
    `<div><dt>${escape(key)}</dt><dd><code>${escape(value)}</code></dd></div>`).join("")}</dl>`;

const systemTextJson: BrowserLibraryDocumentInspection = {
  outcome: "Available",
  detail: null,
  assembly: {
    name: "System.Text.Json",
    version: "11.0.0.0",
    culture: null,
    publicKeyToken: "cc7b13ffcd2ddd51",
  },
  enablements: null,
  diagnostics: [],
  typeNames: null,
  image: {
    imageBytes: 2_831_155,
    targetFramework: ".NETCoreApp,Version=v11.0",
    compilation: "ReadyToRun",
    architecture: "AnyCPU",
    strongNameSigned: true,
    reproducible: "Yes",
  },
  description: {
    informationalVersion: "11.0.0+297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f",
    company: "Microsoft Corporation",
    product: "Microsoft® .NET",
    copyright: "© Microsoft Corporation. All rights reserved.",
  },
  share: null,
};

function render(
  overrides: Partial<Parameters<typeof renderLibraryOverview>[0]> = {},
) {
  return renderLibraryOverview({
    name: "System.Text.Json",
    iconHtml: '<span class="subject-icon" aria-hidden="true"><img src="icon.png" alt="" data-package-icon></span>',
    asset: "lib/net11.0/System.Text.Json.dll",
    inspection: systemTextJson,
    error: "",
    typeCount: 90,
    enablements: [{ id: "AotCompatible", label: "AOT" }],
    escapeHtml: escape,
    factRows,
    ...overrides,
  });
}

test("exact Library Overview summarizes facts instead of listing Types", () => {
  const html = render();
  assert.match(html, /<span class="library-overview-label">Overview<\/span>\s*<div class="library-overview-subject">\s*<span class="subject-icon"[^>]*><img [^>]*data-package-icon><\/span>\s*<h1 id="library-overview-title">System\.Text\.Json<\/h1>/);
  for (const badge of ["ReadyToRun", "AnyCPU", "signed", "reproducible", "AOT"]) {
    assert.match(html, new RegExp(`data-overview-badge="${badge}"`));
  }
  assert.match(html, /<button type="button" data-library-types>90 types<\/button>/);
  assert.match(html, /<h2>Library info<\/h2>/);
  assert.doesNotMatch(html, /JsonSerializer|library-type-names|Type Count unavailable/);
});

test("Library info rows follow the shared Library Info vocabulary", () => {
  assert.deepEqual(
    libraryOverviewFactRows("lib/net11.0/System.Text.Json.dll", systemTextJson),
    [
      ["Name", "System.Text.Json"],
      ["Asset", "lib/net11.0/System.Text.Json.dll"],
      ["Assembly Version", "11.0.0.0"],
      ["Informational Version", "11.0.0+297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f"],
      ["Target Framework", ".NETCoreApp,Version=v11.0"],
      ["Public Key Token", "cc7b13ffcd2ddd51"],
      ["File Size", "2.7 MB"],
      ["Compilation", "ReadyToRun"],
      ["Architecture", "AnyCPU"],
      ["Signed", "Yes"],
      ["Reproducible", "Yes"],
      ["Company", "Microsoft Corporation"],
      ["Product", "Microsoft® .NET"],
      ["Copyright", "© Microsoft Corporation. All rights reserved."],
    ]);
});

test("absent facts are omitted", () => {
  const rows = libraryOverviewFactRows("lib/netstandard2.0/Tiny.dll", {
    ...systemTextJson,
    assembly: { name: "Tiny", version: "1.0.0.0", culture: null, publicKeyToken: null },
    image: { ...systemTextJson.image!, architecture: null, strongNameSigned: false },
    description: {
      informationalVersion: null,
      company: null,
      product: null,
      copyright: null,
    },
  });
  const keys = rows.map(([key]) => key);
  // An unsigned image omits Signed, as CLI Library Info does.
  for (const missing of ["Public Key Token", "Architecture", "Company", "Signed"]) {
    assert.equal(keys.includes(missing), false, missing);
  }
});

test("incomplete inspection is disclosed beside the facts", () => {
  const html = render({
    inspection: {
      ...systemTextJson,
      diagnostics: [{
        code: "LibraryInspection",
        severity: "Warning",
        summary: "TypeDefinitions: BadImageFormat: invalid row",
        correspondence: null,
      }],
    },
  });
  assert.match(html, /<h2>Library info<\/h2>/);
  assert.match(html, /role="status"[\s\S]*This library could not be inspected completely\.[\s\S]*TypeDefinitions: BadImageFormat: invalid row/);
});

test("loading, failed, and unavailable states keep the summary frame", () => {
  const loading = render({ inspection: null, typeCount: null });
  assert.match(loading, /Reading library metadata…/);
  assert.match(loading, /data-library-types>Types</);
  assert.match(render({ typeCount: 1 }), /data-library-types>1 type</);

  const failed = render({ inspection: null, error: "network down" });
  assert.match(failed, /Library facts unavailable/);
  assert.match(failed, /network down/);
  assert.match(failed, /data-library-api-retry/);

  const unavailable = render({
    inspection: { ...systemTextJson, outcome: "Unavailable", detail: "No compile asset." },
  });
  assert.match(unavailable, /No compile asset\./);
  assert.doesNotMatch(unavailable, /Library info/);
});

test("image sizes use the CLI's byte spelling", () => {
  assert.equal(formatLibraryImageSize(512), "512 B");
  assert.equal(formatLibraryImageSize(2048), "2 KB");
  assert.equal(formatLibraryImageSize(2_831_155), "2.7 MB");
  assert.equal(formatLibraryImageSize(3 * 1024 ** 3), "3 GB");
});
