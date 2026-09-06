import assert from "node:assert/strict";
import test from "node:test";
import {
  renderOverviewSurface,
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
    coordinateFieldsHtml: '<select id="package-version"></select><select id="framework"></select>',
    contentHtml: '<section class="document-section"><h2>Libraries</h2></section>',
    escapeHtml,
    ...overrides,
  });
}

test("Overview puts counts, controls, content, and coordinates in one working surface", () => {
  const html = overview();
  assert.match(html, /aria-labelledby="package-overview-title"/);
  assert.match(html, /<h1 id="package-overview-title">Example\.Package<\/h1>/);
  assert.equal(html.match(/<h1\b/g)?.length, 1);
  assert.match(html, /overview-identity[\s\S]*subject-icon[\s\S]*Example\.Package/);
  assert.match(html, /3 types &middot; 12 members/);
  assert.match(html,
    /overview-controls[\s\S]*id="package-version"[\s\S]*id="framework"[\s\S]*overview-scroll[\s\S]*<h2>Libraries<\/h2>[\s\S]*overview-surface-footer/);
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
    /overview-scroll[\s\S]*data-doc-path="README.md"[\s\S]*overview-surface-footer/);
  assert.match(overview({ totalTypes: 1, totalMembers: 1 }),
    /1 type &middot; 1 member/);
});

test("Overview passes complete coordinate text through the existing escaping boundary", () => {
  const html = overview({
    displayName: "Example <Package>",
    details: ["lib/net10.0/Example&Package.dll"],
    packageId: 'Example."Package',
    packageVersion: "10.0.0&preview",
    activeFramework: "net10.0<browser>",
  });
  assert.match(html,
    /title="Example\.&quot;Package@10\.0\.0&amp;preview">Example\.&quot;Package@10\.0\.0&amp;preview<\/span>/);
  assert.match(html, /title="net10\.0&lt;browser&gt;">net10\.0&lt;browser&gt;<\/span>/);
  assert.match(html, />Example &lt;Package&gt;<\/h1>/);
  assert.match(html, /lib\/net10\.0\/Example&amp;Package\.dll/);
});

test("Library uses the same identity frame without adding package controls", () => {
  const html = overview({
    subject: "library",
    subjectLabel: "Library",
    displayName: "Example.Core",
    details: ["lib/net10.0/Example.Core.dll", "Example.Core, Version=1.0.0.0"],
    coordinateFieldsHtml: "",
    contentHtml: '<section class="document-section"><h2>Public surface</h2></section>',
  });
  assert.match(html, /overview-surface library-overview-surface/);
  assert.match(html, /aria-labelledby="library-overview-title"/);
  assert.match(html, /<h1 id="library-overview-title">Example\.Core<\/h1>/);
  assert.match(html, /lib\/net10\.0\/Example\.Core\.dll/);
  assert.match(html, /Example\.Core, Version=1\.0\.0\.0/);
  assert.doesNotMatch(html, /overview-controls|overview-with-controls/);
  assert.equal(html.match(/<h1\b/g)?.length, 1);
});
