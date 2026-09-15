import assert from "node:assert/strict";
import test from "node:test";

import {
  renderPackageChangesView,
  validatePackageChangesInterval,
} from "../src/package-changes-view.ts";
import {
  createPackageChangesRequest,
  initialPackageChangesState,
} from "../src/package-changes.ts";
import { changeRow, failure, inspection } from "./package-changes-fixture.ts";

const escapeHtml = (value: unknown) => String(value)
  .replace(/&/g, "&amp;")
  .replace(/</g, "&lt;")
  .replace(/>/g, "&gt;")
  .replace(/"/g, "&quot;");

const packageSets = [{
  id: "package-set.product-issued",
  title: "Product-issued set",
  summary: "Package membership remains product-owned.",
  order: 10,
}];

test("view renders the product catalog and a keyboard-addressable Packages | Changes peer selector", () => {
  const html = renderPackageChangesView({
    state: initialPackageChangesState(),
    packageSets,
    escapeHtml,
  });

  assert.match(html, /role="tablist" aria-label="Query mode"/);
  assert.match(html, /data-query-mode="packages"/);
  assert.match(html, /data-query-mode="changes" aria-selected="true"/);
  assert.match(html, /value="package-set\.product-issued"/);
  assert.match(html, /Product-issued set/);
  assert.match(html, /Package membership remains product-owned/);
  assert.doesNotMatch(html, /package-set\.microsoft-extensions/);
});

test("current advisory context never manufactures a security-release label", () => {
  const state = initialPackageChangesState();
  const row = changeRow("Current.Context.Only");
  state.rows = [row];
  state.settlement = {
    kind: "succeeded",
    inspection: inspection([row]),
  };
  const html = renderPackageChangesView({
    state,
    packageSets,
    escapeHtml,
  });

  assert.match(html, /Current advisory context/);
  assert.match(html, /Exact fixed-version evidence/);
  assert.match(html, /checked; no fixed-version association/i);
  assert.doesNotMatch(html, /Security release evidenced in interval/);
});

test("positive security release, evidence availability, failures, and coverage use typed fields", () => {
  const state = initialPackageChangesState();
  const positive = changeRow("Security.Release", {
    securityRelease: true,
    currentAvailability: "Partial",
    fixedAvailability: "Complete",
  });
  state.request = createPackageChangesRequest("package-set.product-issued");
  state.rows = [positive];
  state.failures = [failure];
  state.settlement = {
    kind: "succeeded",
    inspection: inspection([positive], [failure], "Partial"),
  };
  const html = renderPackageChangesView({
    state,
    packageSets,
    escapeHtml,
  });

  assert.match(html, /Security release evidenced in interval/);
  assert.match(html, /Partial · 1 references acquired/);
  assert.match(html, /Checked · 1 matching advisory references/);
  assert.match(html, /Advisory provider/);
  assert.match(html, /source unavailable/);
  assert.match(html, /Completion and coverage/);
  assert.match(html, /1 in interval · 1 matching · 1 retained/);
  assert.match(html, /0 current-context · 0 security-release/);
  assert.match(html, /Package Changes reports are not shareable yet/);
});

test("checked-empty, partial-empty, and unavailable evidence remain distinct", () => {
  const complete = changeRow("Complete.Empty");
  const partial = changeRow("Partial.Empty", {
    currentAvailability: "Partial",
  });
  const unavailable = changeRow("Unavailable.Empty", {
    currentAvailability: "Unavailable",
  });
  const rows = [complete, partial, unavailable].map(row => ({
    ...row,
    currentAdvisoryContext: {
      ...row.currentAdvisoryContext,
      advisories: [],
    },
  }));
  const state = initialPackageChangesState();
  state.rows = rows;
  state.settlement = {
    kind: "succeeded",
    inspection: inspection(rows),
  };
  const html = renderPackageChangesView({
    state,
    packageSets,
    escapeHtml,
  });

  assert.match(html, /Checked · no matching advisory in acquired reviewed data/);
  assert.match(html, /Partial · no matching reference was acquired/);
  assert.match(html, /Unavailable · advisory evidence could not be evaluated/);
});

test("a 1000-row report mounts no more than 30 rows while retaining total accounting", () => {
  const rows = Array.from(
    { length: 1000 },
    (_, index) => changeRow(`Package.${index.toString().padStart(4, "0")}`));
  const state = initialPackageChangesState();
  state.rows = rows;
  state.settlement = { kind: "running" };
  const html = renderPackageChangesView({
    state,
    packageSets,
    viewport: {
      scrollTop: 500 * 300,
      clientHeight: 800,
      surfaceTop: 0,
      rowExtent: 300,
      anchorRowIndex: null,
      anchorOffsetTop: null,
    },
    escapeHtml,
  });

  assert.equal(
    (html.match(/<article class="query-row package-changes-row"/g) ?? []).length,
    30);
  assert.match(html, /Changes 496 through 525 of 1000/);
  assert.match(html, /aria-posinset="496"/);
  assert.match(html, /aria-setsize="1000"/);
  assert.match(html, /1,000 changes · streaming/);
});

test("row window uses measured variable extents for spacer accounting", () => {
  const rows = Array.from(
    { length: 100 },
    (_, index) => changeRow(`Package.${index.toString().padStart(3, "0")}`));
  const state = initialPackageChangesState();
  state.rows = rows;
  state.settlement = { kind: "running" };
  const rowExtents = Array.from(
    { length: 100 },
    (_, index) => index % 2 === 0 ? 200 : 600);
  const html = renderPackageChangesView({
    state,
    packageSets,
    viewport: {
      scrollTop: 10_000,
      clientHeight: 800,
      surfaceTop: 0,
      rowExtent: 400,
      rowExtents,
      anchorRowIndex: 25,
      anchorOffsetTop: 0,
    },
    escapeHtml,
  });

  assert.match(html, /Changes 21 through 50 of 100/);
  assert.match(html, /style="height:8000\.00px"/);
  assert.match(html, /style="height:20000\.00px"/);
});

test("repeated coordinates retain distinct Catalog event identities", () => {
  const first = changeRow("Repeated.Package");
  const repeated = changeRow("Repeated.Package");
  const second = {
    ...repeated,
    catalogActivity: {
      ...repeated.catalogActivity,
      commitId: "second-commit",
      leafUrl: "https://api.nuget.org/v3/catalog0/data/second.json",
    },
  };
  const state = initialPackageChangesState();
  state.rows = [first, second];
  state.settlement = {
    kind: "succeeded",
    inspection: inspection([first, second]),
  };
  const html = renderPackageChangesView({
    state,
    packageSets,
    escapeHtml,
  });

  assert.match(html, /0123456789abcdef/);
  assert.match(html, /second-commit/);
  assert.equal((html.match(/>Repeated\.Package <span/g) ?? []).length, 2);
});

test("artifact advisory URLs are inert unless they are HTTPS", () => {
  const unsafe = changeRow("Unsafe.Advisory", {
    advisoryUrl: "javascript:alert(1)",
  });
  const state = initialPackageChangesState();
  state.rows = [unsafe];
  state.settlement = {
    kind: "succeeded",
    inspection: inspection([unsafe]),
  };
  const html = renderPackageChangesView({
    state,
    packageSets,
    escapeHtml,
  });

  assert.doesNotMatch(html, /href="javascript:/);
  assert.match(html, /GHSA-1234-5678-9012/);
});

test("custom intervals require paired increasing UTC endpoints within 42 days", () => {
  assert.equal(
    validatePackageChangesInterval("", "2026-04-01T00:00:00"),
    "Enter both custom UTC interval endpoints.");
  assert.equal(
    validatePackageChangesInterval(
      "2026-04-01T00:00:00",
      "2026-03-01T00:00:00"),
    "The through time must be later than the after time.");
  assert.equal(
    validatePackageChangesInterval(
      "2026-01-01T00:00:00",
      "2026-03-01T00:00:00"),
    "The custom interval cannot exceed 42 days.");
  assert.deepEqual(
    validatePackageChangesInterval(
      "2026-03-01T00:00:00",
      "2026-04-01T12:30:00"),
    {
      fromExclusive: "2026-03-01T00:00:00.0000000Z",
      throughInclusive: "2026-04-01T12:30:00.0000000Z",
    });
});
