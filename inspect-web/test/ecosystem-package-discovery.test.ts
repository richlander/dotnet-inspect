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
    ecosystemAdmission: {
      ecosystemId: "ecosystem.aspire",
      basis: "PackagePrefix",
      registration: "Aspire.",
    },
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
    packageAddStates: new Map(),
    admissionPending: false,
    pendingAdmissionKey: null,
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
    packageAddStates: new Map(),
    admissionPending: false,
    pendingAdmissionKey: null,
  }, escapeHtml);

  assert.match(html, /30 packages shown/);
  assert.doesNotMatch(html, /data-ecosystem-package-capacity="96"/);
  assert.match(html, /All matching packages are shown/);
});

test("Ecosystem discovery renders distinct Open and Workspace admission states", () => {
  const query = initialQueryState();
  query.request = createEcosystemQueryRequest("ecosystem.aspire");
  query.outcome = {
    rows: [packageRow(1), packageRow(2), packageRow(3)],
    assessments: [],
    failures: [],
    progress: [],
    completion: { kind: "exhausted" },
  };
  const states = new Map([
    ["Aspire.Package.1\u00009.0.0", { status: "adding" as const }],
    ["Aspire.Package.2\u00009.0.0", { status: "added" as const }],
    ["Aspire.Package.3\u00009.0.0", {
      status: "failed" as const,
      message: "Admission failed visibly.",
    }],
  ]);

  const html = renderEcosystemPackageDiscovery({
    query,
    capacity: 24,
    pendingCapacity: null,
    navigationError: "",
    packageAddStates: states,
    admissionPending: true,
    pendingAdmissionKey: null,
  }, escapeHtml);

  assert.equal((html.match(/>Open</g) ?? []).length, 3);
  assert.match(html, />Adding\u2026</);
  assert.match(html, />Added</);
  assert.match(html, />Add to workspace</);
  assert.match(html, /Admission failed visibly\./);
  assert.equal((html.match(/ disabled/g) ?? []).length, 3);
});

test("in-flight admission survives cleared row state in the rendered Overview", () => {
  const query = initialQueryState();
  query.request = createEcosystemQueryRequest("ecosystem.aspire");
  query.outcome = {
    rows: [packageRow(1), packageRow(2)],
    assessments: [],
    failures: [],
    progress: [],
    completion: { kind: "streaming" },
  };
  const pending = {
    query,
    capacity: 24 as const,
    pendingCapacity: null,
    navigationError: "",
    packageAddStates: new Map(),
    admissionPending: true,
    pendingAdmissionKey: "Aspire.Package.1\u00009.0.0",
  };

  const html = renderEcosystemPackageDiscovery(pending, escapeHtml);
  assert.match(
    html,
    /data-ecosystem-package-add="Aspire\.Package\.1"[^>]* disabled>Adding\u2026<\/button>/,
  );
  assert.match(
    html,
    /data-ecosystem-package-add="Aspire\.Package\.2"[^>]* disabled>Add to workspace<\/button>/,
  );
  assert.equal((html.match(/>Open<\/button>/g) ?? []).length, 2);
  assert.match(html, /data-ecosystem-package-capacity="48"/);
  assert.match(html, /data-ecosystem-package-capacity="96"/);

  const settled = renderEcosystemPackageDiscovery({
    ...pending,
    admissionPending: false,
    pendingAdmissionKey: null,
  }, escapeHtml);
  assert.doesNotMatch(
    settled,
    /data-ecosystem-package-add="[^"]+"[^>]* disabled/,
  );
  assert.equal(
    (settled.match(/>Add to workspace<\/button>/g) ?? []).length,
    2,
  );
});

test("Ecosystem capacity parsing accepts only product capacities", () => {
  assert.equal(isEcosystemPackageCapacity("24"), true);
  assert.equal(isEcosystemPackageCapacity("48"), true);
  assert.equal(isEcosystemPackageCapacity("96"), true);
  assert.equal(isEcosystemPackageCapacity("192"), false);
  assert.equal(isEcosystemPackageCapacity(undefined), false);
});
