import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import test from "node:test";
import { runInNewContext } from "node:vm";
import type { BrowserSourceComparisonResult } from "../src/facades/inspect-web-source.d.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import {
  createSourceComparisonCoordinator,
  createSourceDiffState,
  isExactSourceComparisonVersion,
} from "../src/source-comparison.ts";
import {
  sourceComparison, sourceContext, sourceEndpoint, sourceRequest, sourceResult,
} from "./source-comparison-fixture.ts";

function harness() {
  const state = createSourceDiffState();
  const queries: Array<{
    operationId: string;
    requestJson: string;
    resolve: (value: BrowserSourceComparisonResult) => void;
    reject: (error: unknown) => void;
  }> = [];
  const cancellations: Array<{ operationId: string; reason: string }> = [];
  const diagnostics: unknown[] = [];
  let nextId = 0;
  const coordinator = createSourceComparisonCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage({
      allocation: { createId: () => `source-comparison-${++nextId}` },
    }),
    queryComparison: (operationId, requestJson) => new Promise((resolve, reject) => {
      queries.push({ operationId, requestJson, resolve, reject });
    }),
    cancelComparison: (operationId, reason) => {
      cancellations.push({ operationId, reason });
    },
    reportOperationDiagnostic: diagnostic => {
      diagnostics.push(diagnostic);
      return undefined;
    },
    describeError: error => error instanceof Error ? error.message : String(error),
    render: () => {},
  });
  return { state, queries, cancellations, diagnostics, coordinator };
}

test("opening and editing never acquire Source; Compare submits the fixed package/member pair", async () => {
  const h = harness();
  h.coordinator.open(sourceContext, "#compare-authored-source");
  assert.equal(h.state.afterVersion, "");
  assert.equal(h.queries.length, 0);
  h.coordinator.setAfterVersion("2.0.0");
  assert.equal(h.queries.length, 0);
  const comparing = h.coordinator.compare();
  assert.equal(h.state.loading, true);
  assert.deepEqual(JSON.parse(h.queries[0]!.requestJson), sourceRequest);
  assert.equal(Object.isFrozen(h.state.submittedRequest), true);
  h.queries[0]!.resolve(sourceResult());
  await comparing;
  assert.equal(h.state.comparison?.status, "Compared");
  assert.equal(h.state.comparison?.isExact, false);
  assert.equal(h.state.comparison?.after.metadataToken, 0x06000019);
  assert.equal(h.state.loading, false);
  assert.deepEqual(h.diagnostics, []);
});

test("same version is explicit and valid; invalid or floating versions do not acquire", async () => {
  for (const valid of ["1", "1.2", "1.2.3.4", "10.0.1", "1.2.3-preview.7+build.1"])
    assert.equal(isExactSourceComparisonVersion(valid), true, valid);
  for (const invalid of ["", "latest", "*", "1.*", "[1,2)", "1.0.0 || 2.0.0", "1.2.3.4.5"])
    assert.equal(isExactSourceComparisonVersion(invalid), false, invalid);
  const h = harness();
  h.coordinator.open(sourceContext, "#compare-authored-source");
  h.coordinator.setAfterVersion("latest");
  await h.coordinator.compare();
  assert.equal(h.queries.length, 0);
  assert.match(h.state.error, /exact After package version/);
  h.coordinator.setAfterVersion(sourceContext.version);
  const comparing = h.coordinator.compare();
  const request = { ...sourceRequest, afterVersion: sourceContext.version };
  assert.deepEqual(JSON.parse(h.queries[0]!.requestJson), request);
  h.queries[0]!.resolve(sourceResult(sourceComparison({ request, isExact: true, lines: [] })));
  await comparing;
  assert.equal(h.state.comparison?.isExact, true);
});

test("editing invalidates submitted labels and rejects a late completion before the next Compare", async () => {
  const h = harness();
  h.coordinator.open(sourceContext, "#compare-authored-source");
  h.coordinator.setAfterVersion("2.0.0");
  const comparing = h.coordinator.compare();
  const submitted = h.state.submittedRequest;
  h.coordinator.setAfterVersion("3.0.0");
  assert.equal(h.queries.length, 1);
  assert.equal(submitted?.afterVersion, "2.0.0");
  assert.equal(h.state.submittedRequest, null);
  assert.equal(h.state.comparison, null);
  assert.equal(h.state.loading, false);
  assert.deepEqual(h.cancellations, [{
    operationId: h.queries[0]!.operationId, reason: "superseded",
  }]);
  const successor = h.coordinator.compare();
  h.queries[1]!.resolve(sourceResult(sourceComparison({
    request: { ...sourceRequest, afterVersion: "3.0.0" },
    after: sourceEndpoint({ version: "3.0.0" }),
  })));
  await successor;
  h.queries[0]!.resolve(sourceResult());
  await comparing;
  assert.deepEqual(h.state.submittedRequest, { ...sourceRequest, afterVersion: "3.0.0" });
  assert.deepEqual(h.state.comparison, sourceComparison({
    request: { ...sourceRequest, afterVersion: "3.0.0" },
    after: sourceEndpoint({ version: "3.0.0" }),
  }));
});

test("editing after success clears the old result without silently submitting another", async () => {
  const h = harness();
  h.coordinator.open(sourceContext, "#compare-authored-source");
  h.coordinator.setAfterVersion("2.0.0");
  const comparing = h.coordinator.compare();
  h.queries[0]!.resolve(sourceResult());
  await comparing;
  h.coordinator.setAfterVersion("3.0.0");
  assert.equal(h.state.comparison, null);
  assert.equal(h.state.submittedRequest, null);
  assert.equal(h.queries.length, 1);
});

test("query unavailable and failed outcomes retain independent endpoint Source instead of transport errors", async () => {
  for (const status of ["Unavailable", "Failed"]) {
    const h = harness();
    h.coordinator.open(sourceContext, "#compare-authored-source");
    h.coordinator.setAfterVersion("2.0.0");
    const comparing = h.coordinator.compare();
    const value = sourceComparison({
      status, lines: [], failure: status === "Failed" ? "Source could not be decoded." : null,
      after: sourceEndpoint({ state: "Unavailable", text: null, detail: "No PDB found." }),
    });
    h.queries[0]!.resolve(sourceResult(value));
    await comparing;
    assert.deepEqual(h.state.comparison, value);
    assert.equal(h.state.error, "");
    assert.equal(h.state.comparison?.before.text, "int Build() => 1 + 2;");
  }
});

test("close, context replacement and reopen dispose the operation and suppress late evidence", async () => {
  for (const action of ["close", "dispose", "reopen"]) {
    const h = harness();
    h.coordinator.open(sourceContext, "#compare-authored-source");
    h.coordinator.setAfterVersion("2.0.0");
    const comparing = h.coordinator.compare();
    if (action === "close")
      assert.deepEqual(h.coordinator.close(), {
        handled: true, returnFocusSelector: "#compare-authored-source",
      });
    else if (action === "dispose") assert.equal(h.coordinator.dispose(), true);
    else h.coordinator.open({ ...sourceContext, version: "4.0.0" }, "#next-launch");
    h.queries[0]!.resolve(sourceResult());
    await comparing;
    assert.equal(h.state.open, action === "reopen");
    assert.equal(h.state.comparison, null);
    assert.equal(h.state.submittedRequest, null);
    assert.equal(h.state.loading, false);
    assert.deepEqual(h.cancellations, [{
      operationId: h.queries[0]!.operationId, reason: "disposed",
    }]);
  }
});

test("managed cancellation is visible without partial Source", async () => {
  const h = harness();
  h.coordinator.open(sourceContext, "#compare-authored-source");
  h.coordinator.setAfterVersion("2.0.0");
  const comparing = h.coordinator.compare();
  h.queries[0]!.resolve(sourceResult(null, { kind: "Canceled", reason: "superseded" }));
  await comparing;
  assert.equal(h.state.comparison, null);
  assert.equal(h.state.loading, false);
  assert.match(h.state.error, /canceled \(superseded\)/);
});

test("transport failures and malformed envelopes are not empty successful comparisons", async () => {
  for (const result of [
    sourceResult(null, { kind: "Failed", failureKind: "Expected", error: "Package not found.", diagnostic: "not-found" }),
    sourceResult(null),
    sourceResult(null, { version: 2 }),
    sourceResult(null, { kind: "Canceled", reason: "invalid" }),
  ]) {
    const h = harness();
    h.coordinator.open(sourceContext, "#compare-authored-source");
    h.coordinator.setAfterVersion("2.0.0");
    const comparing = h.coordinator.compare();
    h.queries[0]!.resolve(result);
    await comparing;
    assert.equal(h.state.comparison, null);
    assert.equal(h.state.loading, false);
    assert.notEqual(h.state.error, "");
  }
});

test("unavailable launch explains why and cannot start acquisition", async () => {
  const h = harness();
  h.coordinator.openUnavailable("Runtime selections are unavailable.", "#launch");
  h.coordinator.setAfterVersion("2.0.0");
  await h.coordinator.compare();
  assert.equal(h.state.unavailableReason, "Runtime selections are unavailable.");
  assert.equal(h.queries.length, 0);
});

test("the routed-navigation dismissal hook ends the actual pending Source comparison", async () => {
  const h = harness();
  h.coordinator.open(sourceContext, "#compare-authored-source");
  h.coordinator.setAfterVersion("2.0.0");
  const comparing = h.coordinator.compare();
  const app = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
  const dismiss = app.match(/function dismissModalsForRoutedNavigation\(\) \{[\s\S]*?\n\}/)?.[0];
  assert.ok(dismiss);
  runInNewContext(stripTypeScriptTypes(`${dismiss}\ndismissModalsForRoutedNavigation();`), {
    state: {}, sourceComparison: h.coordinator,
    methodBodyComparison: { dispose() {} },
    closeGraphExplorerForNavigation() {},
    dismissAnnotatedSourceModal: () => false,
    spotlight: { reset() {} }, sourceInspection: { clearGraphSource() {} },
    documentInspection: { clear() {} },
  });
  h.queries[0]!.resolve(sourceResult());
  await comparing;
  assert.equal(h.state.open, false);
  assert.equal(h.state.comparison, null);
  assert.equal(h.cancellations[0]?.reason, "disposed");
});
