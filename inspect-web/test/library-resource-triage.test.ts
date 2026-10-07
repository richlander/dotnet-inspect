import assert from "node:assert/strict";
import test from "node:test";
import { renderLibraryResourceTriageSurface } from "../src/library-resource-triage.ts";
import type { BrowserResourceTriage, BrowserResourceTriageCandidate } from "../src/facades/inspect-web-analysis.d.ts";

const escapeHtml = (value: unknown) => String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
const options = { libraryName: "Fixture", assemblyIdentity: "Fixture", assetPath: "Fixture.dll", coordinate: "Fixture@1.0", pickerHtml: "", requireLibrary: false, fresh: true, loading: false, error: "", escapeHtml };
const candidate: BrowserResourceTriageCandidate = {
  candidateId: "rt~1234", findingId: "analysis.resource-lifecycle", provenance: "exact",
  assembly: "Fixture.dll", method: "Fixture.Read", methodToken: 0x06000001, moduleVersionId: "00000000-0000-0000-0000-000000000001",
  typeId: "Fixture", stableSelector: "Read~abc", resource: "ArrayPool<byte>",
  shape: "pool-churn-on-exception", acquireOffset: 7, bodyTypeId: "Fixture", bodyMemberName: "Read",
  boundaries: [{ ilOffset: 18, operation: "System.IO.Stream.Read", kind: "ExternalInput" }],
  actionability: "UntrustedActionable", reason: "ExternalInputBoundaryBeforeCleanup",
  impact: "PoolChurnOnException", remediation: "EnsureExceptionalCleanup", confidence: "Medium",
};
const result = (overrides: Partial<BrowserResourceTriage> = {}): BrowserResourceTriage => ({
  outcome: "available", candidates: [candidate], limitations: [], inspectionError: null, share: null, diagnostics: [], ...overrides,
});

test("Resource Triage retains candidate identity and IL evidence with member navigation", () => {
  const html = renderLibraryResourceTriageSurface({ ...options, data: result() });
  assert.match(html, /data-analysis-mode="resource-triage" aria-selected="true"/);
  assert.match(html, /data-perf-selector="Read~abc"/);
  assert.match(html, /data-perf-assembly="Fixture.dll"/);
  for (const text of ["IL_0007", "IL_0012", "External-input boundary", "medium confidence", "finally"]) assert.ok(html.includes(text));
});

test("incomplete results retain candidates with a concise warning", () => {
  const html = renderLibraryResourceTriageSurface({ ...options, data: result({ outcome: "incomplete", limitations: [{ kind: "ExceptionFlow", detail: "<unsafe>", method: "Other" }] }) });
  assert.match(html, /1 candidate · incomplete/);
  assert.match(html, /This library could not be analyzed completely/);
  assert.doesNotMatch(html, /analysis limitation|Module:|Finding:|rt~/);
  assert.doesNotMatch(html, /<unsafe>/);
});

test("an incomplete empty census does not claim absence", () => {
  const html = renderLibraryResourceTriageSurface({ ...options, data: result({ outcome: "incomplete", candidates: [] }) });
  assert.match(html, /absence cannot be established/);
  assert.doesNotMatch(html, /No ArrayPool exception-cleanup candidates found/);
});

test("non-public candidates retain evidence without fabricated navigation", () => {
  const html = renderLibraryResourceTriageSurface({ ...options, data: result({ candidates: [{ ...candidate, typeId: null, stableSelector: null }] }) });
  assert.doesNotMatch(html, /data-triage-code|<details/);
  assert.match(html, /IL_0007/);
  assert.doesNotMatch(html, /data-perf-selector=/);
});

test("failed, unavailable, pending, and complete empty results remain distinct", () => {
  for (const outcome of ["failed", "rejected", "unavailable"]) {
    const html = renderLibraryResourceTriageSurface({ ...options, data: result({ outcome, candidates: [], inspectionError: "<failure>" }) });
    assert.match(html, /&lt;failure&gt;/);
    assert.doesNotMatch(html, /No ArrayPool exception-cleanup candidates found/);
  }
  assert.match(renderLibraryResourceTriageSurface({ ...options, data: result({ candidates: [] }) }), /No ArrayPool exception-cleanup candidates found/);
  assert.match(renderLibraryResourceTriageSurface({ ...options, fresh: false, data: result() }), /Analyzing resource cleanup/);
  assert.match(renderLibraryResourceTriageSurface({ ...options, requireLibrary: true, data: null }), /Pick a library/);
});
