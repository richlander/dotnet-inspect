import assert from "node:assert/strict";
import test from "node:test";
import {
  renderPackageVulnerabilities,
} from "../src/package-vulnerabilities.ts";
import type {
  BrowserPackageVulnerabilityResult,
} from "../src/facades/inspect-web-package.d.ts";
import {
  dateTimeOffsetString,
} from "./date-time-offset-string-fixture.ts";

const escapeHtml = (value: unknown) => String(value);

function result(
  overrides: Partial<BrowserPackageVulnerabilityResult> = {},
): BrowserPackageVulnerabilityResult {
  return {
    package: "example.package",
    version: "1.2.3",
    availability: "Complete",
    advisories: [],
    failures: [],
    advisoryProducer: "https://api.github.com/advisories",
    observedAt: dateTimeOffsetString("2026-10-05T00:00:00Z"),
    ...overrides,
  };
}

test("complete empty vulnerability evidence remains qualified", () => {
  const html = renderPackageVulnerabilities({
    packageId: "Example.Package",
    packageVersion: "1.2.3",
    loading: false,
    error: "",
    result: result(),
    escapeHtml,
  });

  assert.match(html, /0 reviewed advisories/);
  assert.match(html, /No GitHub-reviewed NuGet advisories match this exact package version/);
  assert.doesNotMatch(html, /safe|secure/iu);
});

for (const [availability, failure] of [
  ["Partial", "RateLimitOrForbidden"],
  ["Unavailable", "SourceUnavailable"],
] as const) {
  test(`${availability} empty vulnerability evidence does not claim no match`, () => {
    const html = renderPackageVulnerabilities({
      packageId: "Example.Package",
      packageVersion: "1.2.3",
      loading: false,
      error: "",
      result: result({
        availability,
        failures: [failure],
      }),
      escapeHtml,
    });

    assert.match(html, /Advisory lookup incomplete/);
    assert.match(
      html,
      /did not establish whether GitHub-reviewed NuGet advisories match/);
    assert.doesNotMatch(html, /No matching reviewed advisories/);
  });
}

test("vulnerability advisories retain identity severity dates and destination", () => {
  const html = renderPackageVulnerabilities({
    packageId: "Example.Package",
    packageVersion: "1.2.3",
    loading: false,
    error: "",
    result: result({
      availability: "Partial",
      failures: ["RateLimitOrForbidden"],
      advisories: [{
        ghsaId: "GHSA-aaaa-bbbb-cccc",
        cveId: "CVE-2026-1234",
        severity: "High",
        advisoryUrl: "https://github.com/advisories/GHSA-aaaa-bbbb-cccc",
        publishedAt: dateTimeOffsetString("2026-01-02T03:04:05Z"),
        updatedAt: dateTimeOffsetString("2026-02-03T04:05:06Z"),
      }],
    }),
    escapeHtml,
  });

  assert.match(html, /1 reviewed advisory · partial coverage/);
  assert.match(html, /GHSA-aaaa-bbbb-cccc · CVE-2026-1234/);
  assert.match(html, /severity-high">High/);
  assert.match(html, /2026-01-02/);
  assert.match(html, /2026-02-03/);
  assert.match(html, /GitHub rejected or rate-limited/);
  assert.match(html, /rel="noopener noreferrer"/);
});
