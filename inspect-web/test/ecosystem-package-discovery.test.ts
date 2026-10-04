import assert from "node:assert/strict";
import test from "node:test";

import {
  isEcosystemPackageCapacity,
  renderEcosystemPackageDiscovery,
} from "../src/ecosystem-package-discovery.ts";
import {
  createEcosystemQueryRequest,
  initialQueryState,
  type QueryResultRow,
} from "../src/package-query.ts";

const escapeHtml = (value: unknown) => String(value)
  .replaceAll("&", "&amp;")
  .replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;")
  .replaceAll("\"", "&quot;");

function packageRow(index: number): QueryResultRow {
  return {
    packageId: `Aspire.Package.${index}`,
    version: "9.0.0",
    tier: "search-metadata",
    answers: [],
    evidence: [],
    totalDownloads: index,
  };
}

test("Ecosystem discovery renders cumulative 24, 48, and 96 capacities", () => {
  const query = initialQueryState();
  query.request = createEcosystemQueryRequest("ecosystem.aspire");
  query.outcome = {
    rows: Array.from({ length: 24 }, (_unused, index) => packageRow(index)),
    assessments: [],
    failures: [],
    progress: [{ phase: "search", completed: 24, limit: 200 }],
    completion: { kind: "streaming" },
  };

  const html = renderEcosystemPackageDiscovery({
    query,
    capacity: 24,
    pendingCapacity: null,
    navigationError: "",
  }, escapeHtml);

  assert.match(html, /24 packages shown/);
  assert.match(html, /aria-current="true">24/);
  assert.match(html, /data-ecosystem-package-capacity="48"/);
  assert.match(html, /data-ecosystem-package-capacity="96"/);
  assert.match(html, /Aspire\.Package\.0/);
});

test("completed Ecosystem discovery removes capacities that cannot reveal more", () => {
  const query = initialQueryState();
  query.request = createEcosystemQueryRequest("ecosystem.aspire");
  query.outcome = {
    rows: Array.from({ length: 30 }, (_unused, index) => packageRow(index)),
    assessments: [],
    failures: [],
    progress: [],
    completion: { kind: "exhausted" },
  };

  const html = renderEcosystemPackageDiscovery({
    query,
    capacity: 48,
    pendingCapacity: null,
    navigationError: "",
  }, escapeHtml);

  assert.match(html, /30 packages shown/);
  assert.doesNotMatch(html, /data-ecosystem-package-capacity="96"/);
  assert.match(html, /All matching packages are shown/);
});

test("Ecosystem capacity parsing accepts only product capacities", () => {
  assert.equal(isEcosystemPackageCapacity("24"), true);
  assert.equal(isEcosystemPackageCapacity("48"), true);
  assert.equal(isEcosystemPackageCapacity("96"), true);
  assert.equal(isEcosystemPackageCapacity("192"), false);
  assert.equal(isEcosystemPackageCapacity(undefined), false);
});
