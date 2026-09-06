import assert from "node:assert/strict";
import test from "node:test";
import {
  renderPackageOverviewSurface,
  type PackageOverviewSurfaceOptions,
} from "../src/package-overview.ts";

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function overview(overrides: Partial<PackageOverviewSurfaceOptions> = {}): string {
  return renderPackageOverviewSurface({
    packageId: "Example.Package",
    packageVersion: "10.0.0",
    activeFramework: "net10.0",
    totalTypes: 3,
    totalMembers: 12,
    coordinateFieldsHtml: '<select id="package-version"></select><select id="framework"></select>',
    contentHtml: '<section class="document-section"><h2>Libraries</h2></section>',
    escapeHtml,
    ...overrides,
  });
}

test("Overview puts counts, controls, content, and coordinates in one working surface", () => {
  const html = overview();
  assert.match(html, /aria-labelledby="package-overview-surface-title"/);
  assert.match(html, /<h1 id="package-overview-surface-title">Overview<\/h1>/);
  assert.match(html, /3 types &middot; 12 members/);
  assert.match(html,
    /package-overview-controls[\s\S]*id="package-version"[\s\S]*id="framework"[\s\S]*package-overview-scroll[\s\S]*<h2>Libraries<\/h2>[\s\S]*package-overview-surface-footer/);
  assert.match(html, /title="Example.Package@10.0.0">Example.Package@10.0.0<\/span>/);
  assert.match(html, /title="net10.0">net10.0<\/span>/);
  assert.doesNotMatch(html, /type-heading|package-coordinate-editor/);
});

test("Overview retains zero totals and supplied document navigation", () => {
  const html = overview({
    totalTypes: 0,
    totalMembers: 0,
    contentHtml: '<button data-doc-path="README.md">Readme</button>',
  });
  assert.match(html, /0 types &middot; 0 members/);
  assert.match(html,
    /package-overview-scroll[\s\S]*data-doc-path="README.md"[\s\S]*package-overview-surface-footer/);
  assert.match(overview({ totalTypes: 1, totalMembers: 1 }),
    /1 type &middot; 1 member/);
});

test("Overview passes complete coordinate text through the existing escaping boundary", () => {
  const html = overview({
    packageId: 'Example."Package',
    packageVersion: "10.0.0&preview",
    activeFramework: "net10.0<browser>",
  });
  assert.match(html,
    /title="Example\.&quot;Package@10\.0\.0&amp;preview">Example\.&quot;Package@10\.0\.0&amp;preview<\/span>/);
  assert.match(html, /title="net10\.0&lt;browser&gt;">net10\.0&lt;browser&gt;<\/span>/);
});
