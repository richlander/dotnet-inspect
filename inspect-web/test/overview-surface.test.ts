import assert from "node:assert/strict";
import test from "node:test";
import {
  renderPackageOverviewContent,
  renderOverviewSurface,
  renderTypeOverview,
  type OverviewSurfaceOptions,
} from "../src/overview-surface.ts";

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function overview(overrides: Partial<OverviewSurfaceOptions> = {}): string {
  return renderOverviewSurface({
    subject: "package",
    subjectLabel: "Package",
    displayName: "Example.Package",
    iconHtml: '<span class="subject-icon" aria-hidden="true">P</span>',
    packageId: "Example.Package",
    packageVersion: "10.0.0",
    activeFramework: "net10.0",
    totalTypes: 3,
    totalMembers: 12,
    contentHtml: '<section class="document-section"><h2>Package info</h2></section>',
    escapeHtml,
    ...overrides,
  });
}

test("Package Overview starts with identity and content without a redundant header row", () => {
  const html = overview();
  assert.match(html, /aria-labelledby="package-overview-title"/);
  assert.match(html, /<h1 id="package-overview-title">Example\.Package<\/h1>/);
  assert.equal(html.match(/<h1\b/g)?.length, 1);
  assert.match(html, /overview-identity[\s\S]*subject-icon[\s\S]*Example\.Package/);
  assert.match(html,
    /overview-scroll[\s\S]*<h2>Package info<\/h2>/);
  assert.doesNotMatch(
    html,
    /overview-surface-head|overview-surface-label|overview-subject-label|>Overview<|package-version/);
  assert.doesNotMatch(html, /overview-surface-footer/);
  assert.doesNotMatch(html, /type-heading|package-coordinate-editor/);
});

test("Package Overview composes Package info with its owner-issued children", () => {
  const html = renderPackageOverviewContent({
    packageInfoHtml: "<section><h2>Package info</h2></section>",
    packageChildrenHtml:
      '<section><h2>Libraries</h2><button data-package-child-library="lib/net10.0/Example.dll">Example</button></section>',
    documentsHtml: "<section><h2>Documents</h2></section>",
  });

  assert.match(html, /package-overview-summary[\s\S]*Package info/);
  assert.match(
    html,
    /Package info[\s\S]*<h2>Libraries<\/h2>[\s\S]*data-package-child-library/);
  assert.match(html, /Libraries[\s\S]*Documents/);
  assert.doesNotMatch(html, /data-lib-scope/);
});

test("Package Overview retains supplied document navigation", () => {
  const html = overview({
    totalTypes: 0,
    totalMembers: 0,
    contentHtml: '<button data-doc-path="README.md">Readme</button>',
  });

  assert.match(html,
    /overview-scroll[\s\S]*data-doc-path="README.md"/);
});

test("Library Overview passes identity text through the existing escaping boundary", () => {
  const html = overview({
    subject: "library",
    displayName: "Example <Package>",
    details: ["lib/net10.0/Example&Package.dll"],
    packageId: 'Example."Package',
    packageVersion: "10.0.0&preview",
    activeFramework: "net10.0<browser>",
  });
  assert.match(html, />Example &lt;Package&gt;<\/h1>/);
  assert.match(html, /lib\/net10\.0\/Example&amp;Package\.dll/);
});

test("Library uses the same identity frame without adding package controls", () => {
  const html = overview({
    subject: "library",
    subjectLabel: "Library",
    displayName: "Example.Core",
    details: ["lib/net10.0/Example.Core.dll", "Example.Core, Version=1.0.0.0"],
    contentHtml: '<section class="document-section"><h2>Public surface</h2></section>',
  });
  assert.match(html, /overview-surface library-overview-surface/);
  assert.match(html, /aria-labelledby="library-overview-title"/);
  assert.match(html, /<h1 id="library-overview-title">Example\.Core<\/h1>/);
  assert.match(html, /lib\/net10\.0\/Example\.Core\.dll/);
  assert.match(html, /Example\.Core, Version=1\.0\.0\.0/);
  assert.match(html, /3 types &middot; 12 members/);
  assert.doesNotMatch(html, /overview-controls/);
  assert.equal(html.match(/<h1\b/g)?.length, 1);
});

test("Library Overview keeps unavailable aggregate counts distinct from zero", () => {
  const html = overview({
    subject: "library",
    subjectLabel: "Library",
    totalTypes: null,
    totalMembers: null,
  });
  assert.match(
    html,
    /Type Count unavailable &middot; Member Count unavailable/);
  assert.doesNotMatch(html, /0 types|0 members/);
  assert.match(overview({
    subject: "library",
    subjectLabel: "Library",
    totalTypes: 1,
    totalMembers: 1,
  }), /1 type &middot; 1 member/);
});

test("Library Overview lists only Enabled enablements in the identity header", () => {
  const html = overview({
    subject: "library",
    subjectLabel: "Library",
    enablements: [
      { id: "aot-compatible", label: "AOT" },
      { id: "runtime-async", label: "Runtime Async" },
    ],
  });
  assert.match(
    html,
    /overview-identity[\s\S]*<ul class="overview-enablements" aria-label="Enabled"><li class="overview-enablement" data-enablement="aot-compatible">AOT<\/li><li class="overview-enablement" data-enablement="runtime-async">Runtime Async<\/li><\/ul>/,
  );
  assert.doesNotMatch(overview({ enablements: [] }), /overview-enablements/);
  assert.doesNotMatch(overview(), /overview-enablements/);
});

test("Type Overview separates direct metadata, rollups, and numeric navigation", () => {
  const html = renderTypeOverview({
    subject: {
      displayName: "JsonDocument",
      namespace: "System.Text.Json",
      kind: "class",
      accessibility: "public",
      assembly: "System.Text.Json",
      members: 16,
    },
    projection: {
      status: "available",
      complete: true,
      exact: {
        fullName: "System.Text.Json.JsonDocument",
        namespace: "System.Text.Json",
        kind: "class",
        accessibility: "public",
        isSealed: true,
        isAbstract: false,
        isStatic: false,
        isByRefLike: false,
        isReadOnly: false,
        baseType: "System.Object",
        interfaces: ["System.IDisposable"],
      },
    },
    rollups: [
      { id: "unsafe", label: "unsafe" },
      { id: "async", label: "async" },
    ],
    relatedTypeHtml: name => `<button>${escapeHtml(name)}</button>`,
    escapeHtml,
  });

  assert.match(html,
    /Type metadata[\s\S]*class[\s\S]*sealed[\s\S]*public/);
  assert.match(html, /Type rollup[\s\S]*unsafe[\s\S]*async/);
  assert.match(html, /data-type-members>16 members<\/button>/);
  assert.match(html,
    /data-type-relationship="base"[\s\S]*System\.Object[\s\S]*data-type-relationship="interface"[\s\S]*System\.IDisposable/);
});

test("Type Overview omits an absent rollup group and keeps metadata failure visible", () => {
  const html = renderTypeOverview({
    subject: {
      displayName: "Widget",
      namespace: "Example",
      kind: "class",
      accessibility: "internal",
      assembly: "Example",
      members: 0,
    },
    projection: {
      status: "failed",
      exact: null,
      complete: false,
      detail: "<projection failed>",
    },
    relatedTypeHtml: name => name,
    escapeHtml,
  });

  assert.doesNotMatch(html, /Type rollup/);
  assert.match(html, /role="alert"/);
  assert.match(html, /&lt;projection failed&gt;/);
  assert.doesNotMatch(html, /No base Type or implemented interfaces/);
});

test("Type Overview preserves singular member navigation and incomplete evidence", () => {
  const html = renderTypeOverview({
    subject: {
      displayName: "Widget",
      namespace: "",
      kind: "struct",
      assembly: "Example",
      members: 1,
    },
    projection: {
      status: "available",
      complete: false,
      exact: {
        fullName: "Widget",
        namespace: null,
        kind: "struct",
        accessibility: null,
        isSealed: false,
        isAbstract: false,
        isStatic: false,
        isByRefLike: false,
        isReadOnly: true,
        baseType: null,
        interfaces: [],
      },
    },
    relatedTypeHtml: name => name,
    escapeHtml,
  });

  assert.match(html, /data-type-members>1 member<\/button>/);
  assert.match(html, /Type relationship evidence is incomplete/);
  assert.match(html, /<dt>Namespace<\/dt><dd>global<\/dd>/);
});
