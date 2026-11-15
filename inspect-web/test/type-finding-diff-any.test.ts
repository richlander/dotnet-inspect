import assert from "node:assert/strict";
import test from "node:test";
import { createBackgroundAnalysisQueue } from "../src/background-analysis.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import type { BrowserTypeFindingDiffAnyRequest, BrowserTypeFindingDiffAnyResult } from "../src/facades/inspect-web-metadata.d.ts";
import { createTypeFindingDiffAnyCues, memberFindingDiffAnyAchievements, renderTypeChangeStatus, type TypeFindingDiffAnyCueRequest } from "../src/type-finding-diff-any.ts";

const request: TypeFindingDiffAnyCueRequest = {
  packageModel: {}, typeIdentifier: "Example.Widget", typeQueryIdentifier: "Example.Widget",
  baseline: { packageId: "Example", currentVersion: "2.0.0", targetVersion: "1.0.0", targetFramework: "net11.0", runtimeIdentifier: "linux-x64", compileAssetId: "compile:lib/net11.0/Example.dll", axes: "ApiAndBody" },
};
const settle = () => new Promise<void>(resolve => setTimeout(resolve, 0));
function result(sent: BrowserTypeFindingDiffAnyRequest): BrowserTypeFindingDiffAnyResult {
  return { schemaVersion: 1, request: sent, kind: "Succeeded", error: null, diagnostic: null, reason: null, missingCounterpart: null,
    value: { typeIdentifier: sent.typeIdentifier, targetAssetId: "old", currentAssetId: "new", api: "Changed", body: "Changed", residualApi: "Unchanged", residualBody: "Unchanged",
      members: [
        { beforeFingerprint: "old-api", afterFingerprint: "api", api: "Changed", body: "Unchanged" },
        { beforeFingerprint: "body", afterFingerprint: "body", api: "Unchanged", body: "Changed" },
        { beforeFingerprint: "both", afterFingerprint: "both", api: "Changed", body: "Changed" },
        { beforeFingerprint: "unsure", afterFingerprint: "unsure", api: "Unchanged", body: "Indeterminate" },
        { beforeFingerprint: "removed", afterFingerprint: null, api: "Changed", body: "Changed" },
      ] } };
}
function fixture(respond: (sent: BrowserTypeFindingDiffAnyRequest) => unknown = result) {
  const sent: BrowserTypeFindingDiffAnyRequest[] = [];
  const cues = createTypeFindingDiffAnyCues({
    queue: createBackgroundAnalysisQueue({ whenForegroundIdle: () => Promise.resolve(), reportError: error => { throw error; } }),
    operationAuthority: createOperationAuthorityPage(), query: (_id, body) => { sent.push(body); return Promise.resolve(respond(body)); },
    cancel: () => undefined, describeError: error => error instanceof Error ? error.message : String(error), reportOperationDiagnostic: () => undefined, render: () => undefined,
  });
  return { cues, sent };
}
test("selected Type request keeps exact implementation baseline and current endpoint anchors", async () => {
  const { cues, sent } = fixture(); cues.ensure(request, () => true); await settle();
  assert.equal(sent.length, 1); assert.deepEqual(sent[0]?.comparison, { schemaVersion: 3, ...request.baseline });
  const entry = cues.entry(request); assert.equal(entry?.status, "ready"); if (entry?.status !== "ready") return;
  assert.equal(entry.value.members.has("old-api"), false); assert.equal(entry.value.members.has("removed"), false);
  assert.equal(entry.value.members.get("api")?.beforeFingerprint, "old-api");
  for (const [anchor, kind, text] of [["api", "api-diff", "API changed"], ["body", "body-diff", "implementation changed"], ["both", "diff", "API changed, implementation changed"], ["unsure", "body-diff", "implementation undecided"]] as const) {
    const value = memberFindingDiffAnyAchievements(entry.value, { overloads: [{ anchorDigest: anchor }] }, 0, "1.0.0");
    assert.deepEqual(value, [{ kind, description: `${text} since 1.0.0` }]);
  }
  assert.deepEqual(memberFindingDiffAnyAchievements(entry.value, { overloads: [{ anchorDigest: "other" }] }, null, "1.0.0"), []);
  assert.equal(memberFindingDiffAnyAchievements(entry.value, { overloads: [{ anchorDigest: "api" }, { anchorDigest: "body" }] }, null, "1.0.0")[0]?.kind, "diff");
});
test("body-only changes issue Type comparisons and report implementation cues", async () => {
  const { cues } = fixture(sent => { const value = result(sent); return { ...value, value: { ...value.value!, api: "Unchanged", members: [value.value!.members[1]!] } }; });
  cues.ensure(request, () => true); await settle(); const entry = cues.entry(request); assert.equal(entry?.status, "ready");
  if (entry?.status === "ready") assert.equal(entry.value.found, true);
});
test("residual changes remain visible without manufacturing Member cues", async () => {
  const { cues } = fixture(sent => { const value = result(sent); return { ...value, value: { ...value.value!, residualApi: "Changed", body: "Unchanged", members: [] } }; });
  cues.ensure(request, () => true); await settle(); const entry = cues.entry(request); assert.equal(entry?.status, "ready");
  assert.match(renderTypeChangeStatus({ description: "API changed", api: true, body: false }, entry, String), /outside the listed Members/);
});
for (const corruption of ["schema", "baseline", "state", "duplicate", "empty"]) {
  test(`malformed ${corruption} result is visibly failed`, async () => {
    const { cues } = fixture(sent => {
      const value = result(sent);
      switch (corruption) {
        case "schema": return { ...value, schemaVersion: 9 };
        case "baseline": return { ...value, request: { ...sent, comparison: { ...sent.comparison, targetVersion: "other" } } };
        case "state": return { ...value, value: { ...value.value!, body: "Bogus" } };
        case "duplicate": return { ...value, value: { ...value.value!, members: [value.value!.members[0], value.value!.members[0]] } };
        default: return { ...value, value: { ...value.value!, members: [{ beforeFingerprint: null, afterFingerprint: null, api: "Changed", body: "Changed" }] } };
      }
    });
    cues.ensure(request, () => true); await settle(); assert.equal(cues.entry(request)?.status, "failed");
  });
}
test("missing runtime counterpart exposes failure and retry, with matching Compare actions", async () => {
  const { cues } = fixture(sent => ({ ...result(sent), kind: "MissingCounterpart", value: null, error: "No runtime counterpart" }));
  cues.ensure(request, () => true); await settle();
  const html = renderTypeChangeStatus({ description: "Both changed", api: true, body: true }, cues.entry(request), String);
  assert.match(html, /No runtime counterpart/); assert.match(html, /data-type-change-retry/); assert.match(html, /Compare API/); assert.match(html, /Compare bodies/);
});
