import assert from "node:assert/strict";
import test from "node:test";

import {
  createBrowserLibraryPerformanceDataSource,
  type BrowserLibraryPerformanceEngine,
} from "../src/library-performance-source.ts";
import type { LibraryPerformanceRequest } from "../src/library-performance.ts";
import type {
  BrowserPackagePerformanceSummary,
  BrowserPerformanceMember,
} from "../src/facades/inspect-web-analysis.d.ts";

function publish(sink: unknown, event: object): void {
  if (typeof sink !== "object" || sink === null) {
    throw new TypeError("Expected an event sink object.");
  }
  Reflect.set(sink, "event", JSON.stringify(event));
}

function member(memberName: string): BrowserPerformanceMember {
  return {
    assembly: "Example.dll",
    typeId: "Example.Type",
    memberName,
    stableSelector: `Example.Type.${memberName}`,
    bodyTokens: [100663297],
    opportunityCount: 1,
    inLoopCount: 0,
    shapes: ["Boxing"],
    confidence: "High",
    bodyTargets: null,
  };
}

function summary(
  overrides: Partial<BrowserPackagePerformanceSummary> = {},
): BrowserPackagePerformanceSummary {
  return {
    inspectionError: null,
    nonPublicOpportunities: 0,
    totalOpportunities: 1,
    compileLibrary: {
      status: "Selected",
      targetFramework: "net8.0",
      message: null,
    },
    ...overrides,
  };
}

const request: LibraryPerformanceRequest = {
  packageId: "Example.Package",
  version: "1.0.0",
  targetFramework: "net8.0",
  assemblyName: "Example.dll",
};

test("source drains Item events in callback order before terminal success", async () => {
  const first = member("First");
  const second = member("Second");
  let receivedRequest: readonly unknown[] | null = null;
  const engine: BrowserLibraryPerformanceEngine = {
    cancel() {},
    async run(operationId, packageId, version, targetFramework, assemblyName, sink) {
      receivedRequest =
        [operationId, packageId, version, targetFramework, assemblyName];
      publish(sink, { kind: "Item", item: first });
      publish(sink, { kind: "Item", item: second });
      return {
        version: 1,
        kind: "Succeeded",
        summary: summary(),
        failureKind: null,
        error: null,
        diagnostic: null,
        reason: null,
      };
    },
  };
  const source = createBrowserLibraryPerformanceDataSource(engine, {
    createOperationId: () => "performance-1",
  });
  const observed: string[] = [];
  const result = await source.run(
    request,
    item => observed.push(item.memberName),
    new AbortController().signal);

  assert.deepEqual(observed, ["First", "Second"]);
  assert.deepEqual(receivedRequest, [
    "performance-1",
    request.packageId,
    request.version,
    request.targetFramework,
    request.assemblyName,
  ]);
  assert.equal(result.kind, "succeeded");
});

test("malformed callback events fail the observer and cancel managed work", async () => {
  const cancellations: string[] = [];
  const engine: BrowserLibraryPerformanceEngine = {
    cancel(_operationId, reason) { cancellations.push(reason); },
    async run(_operationId, _packageId, _version, _targetFramework, _assemblyName, sink) {
      publish(sink, { kind: "Item", item: null });
      await Promise.resolve();
      return {
        version: 1,
        kind: "Succeeded",
        summary: summary(),
        failureKind: null,
        error: null,
        diagnostic: null,
        reason: null,
      };
    },
  };
  const source = createBrowserLibraryPerformanceDataSource(engine, {
    createOperationId: () => "performance-malformed",
  });

  await assert.rejects(
    source.run(request, () => {}, new AbortController().signal),
    /Expected Library Performance member object/);
  assert.deepEqual(cancellations, ["feature-observer-failed"]);
});

test("abort requests managed cancellation and reports physical cancellation", async () => {
  const controller = new AbortController();
  const cancellations: string[] = [];
  const engine: BrowserLibraryPerformanceEngine = {
    cancel(_operationId, reason) { cancellations.push(reason); },
    async run() {
      await new Promise<void>(resolve =>
        controller.signal.addEventListener("abort", () => resolve(), {
          once: true,
        }));
      return {
        version: 1,
        kind: "Canceled",
        summary: null,
        failureKind: null,
        error: null,
        diagnostic: null,
        reason: "disposed",
      };
    },
  };
  const source = createBrowserLibraryPerformanceDataSource(engine, {
    createOperationId: () => "performance-cancel",
  });
  const pending = source.run(
    request, () => {}, controller.signal);
  controller.abort("disposed");

  assert.deepEqual(await pending, { kind: "canceled", reason: "disposed" });
  assert.deepEqual(cancellations, ["disposed"]);
});

test("a failed physical settlement is reported without members", async () => {
  const engine: BrowserLibraryPerformanceEngine = {
    cancel() {},
    async run() {
      return {
        version: 1,
        kind: "Failed",
        summary: null,
        failureKind: "Expected",
        error: "No compile-time assembly was available.",
        diagnostic: "diagnostic detail",
        reason: null,
      };
    },
  };
  const source = createBrowserLibraryPerformanceDataSource(engine, {
    createOperationId: () => "performance-failed",
  });

  const result = await source.run(request, () => {}, new AbortController().signal);
  assert.deepEqual(result, {
    kind: "failed",
    error: "No compile-time assembly was available.",
    diagnostic: "diagnostic detail",
  });
});
