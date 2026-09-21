import assert from "node:assert/strict";
import test from "node:test";

import {
  bindPackageChangesFacade,
  createSharedEngineOperationAuthority,
  registerEngineWorkerPackageChangesAdapter,
} from "../src/engine-worker-client.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  engineWorkerPackageChangesCancellationIsRunning,
  engineWorkerPackageChangesInput,
  engineWorkerPackageChangesInspection,
  isDateTimeOffsetJsonString,
  mapEngineWorkerPackageChangesResult,
  registerEngineWorkerPackageChangesOperation,
  type EngineWorkerPackageChangesFacade,
} from "../src/engine-worker-package-changes.ts";
import type {
  BrowserPackageChangesFailure,
  BrowserPackageChangesInspection,
  BrowserPackageChangesProgress,
  BrowserPackageChangesRequest,
  BrowserPackageChangesResult,
  BrowserPackageChangesRow,
} from "../src/facades/inspect-web-package.d.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
} from "../src/worker-runtime-core.ts";
import { dateTimeOffsetString } from "./date-time-offset-string-fixture.ts";

const request: BrowserPackageChangesRequest = {
  packageSetId: "package-set.microsoft-extensions",
  fromExclusive: null,
  throughInclusive: null,
  securityOnly: false,
  maximumRows: 100,
};

const progress: BrowserPackageChangesProgress = {
  phase: "Catalog",
  completed: 10,
  total: null,
  capturedHorizon: dateTimeOffsetString(
    "2026-04-01T00:00:00+00:00"),
  catalogPagesAcquired: 2,
  catalogHttpAttempts: 2,
  catalogDecodedBytes: 4_096,
};

const advisoryEvidence = {
  availability: "Complete",
  advisories: [],
} as const;

const row: BrowserPackageChangesRow = {
  catalogActivity: {
    packageId: "Example.Package",
    version: "1.0.0",
    normalizedPackageId: "example.package",
    normalizedVersion: "1.0.0",
    leafUrl: "https://api.nuget.org/v3/catalog0/page/leaf.json",
    commitId: "0123456789abcdef",
    commitTimestamp: dateTimeOffsetString(
      "2026-03-31T00:00:00+00:00"),
    catalogKind: "Details",
    activity: "SnapshotObserved",
  },
  currentAdvisoryContext: advisoryEvidence,
  fixedVersionEvidence: advisoryEvidence,
  packageReceipt: null,
  securityReleaseStatus: "CheckedNoFixedVersionAssociation",
  securityRelease: null,
  isSecurityRelevant: false,
};

const failure: BrowserPackageChangesFailure = {
  provider: "Advisory",
  catalogFailure: null,
  advisoryFailure: "SourceUnavailable",
  packageReceiptFailure: null,
};

function inspection(): BrowserPackageChangesInspection {
  return {
    content: {
      schemaVersion: 1,
      request: {
        referenceTime: dateTimeOffsetString(
          "2026-04-01T00:00:00+00:00"),
        fromExclusive: dateTimeOffsetString(
          "2026-02-18T00:00:00+00:00"),
        throughInclusive: dateTimeOffsetString(
          "2026-04-01T00:00:00+00:00"),
        usedDefaultInterval: true,
        packageScope: {
          kind: "PackageSet",
          selectionId: "package-set.microsoft-extensions",
          prefix: null,
          packageIds: ["Example.Package"],
        },
        securitySelection: "AllActivity",
        maximumRows: 100,
        maximumCandidateEvents: 1_000,
        maximumReceiptRequests: 100,
      },
      source: {
        producerKey: "nuget.org",
        producer: "NuGet.org",
        transportKind: "NuGetV3",
      },
      progress: [progress],
      rows: [row],
      failures: [failure],
      summary: {
        capturedHorizon: dateTimeOffsetString(
          "2026-04-01T00:00:00+00:00"),
        catalogCompletion: "WindowExhausted",
        catalogFailure: null,
        catalogPagesAcquired: 2,
        catalogHttpAttempts: 2,
        catalogDecodedBytes: 4_096,
        catalogInWindowEventCount: 10,
        matchingEventCount: 1,
        retainedEventCount: 1,
        candidateLimitReached: false,
        advisoryEvidence: {
          packageProducerKey: "nuget.org",
          advisoryProducer: "GitHub Advisory Database",
          observedAt: dateTimeOffsetString(
            "2026-04-01T00:00:00+00:00"),
          apiRequests: 1,
          responseBytes: 2,
          complete: false,
          failures: ["SourceUnavailable"],
          packages: [],
        },
        receiptCandidates: 0,
        receiptRequests: 0,
        receiptSuccesses: 0,
        receiptFailures: [],
        receiptLimitReached: false,
        currentContextUnevaluableRows: 1,
        securityReleaseUnevaluableRows: 0,
        eligibleRowCount: 1,
        returnedRowCount: 1,
        resultLimitReached: false,
        completion: "Partial",
      },
    },
    share: {
      kind: "NonProjectable",
      fullUrl: null,
      packet: null,
      path: "package-changes/share",
      reason: "Package Activity is not yet share-projectable.",
    },
    diagnostics: [],
  };
}

function succeeded(): BrowserPackageChangesResult {
  return {
    version: 1,
    kind: "Succeeded",
    inspection: inspection(),
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

test("Package Activity input is closed, paired, and bounded", () => {
  assert.equal(engineWorkerPackageChangesInput.decode(request).kind, "decoded");
  assert.equal(engineWorkerPackageChangesInput.decode({
    ...request,
    packageSetId: "not-a-package-set",
  }).kind, "rejected");
  assert.equal(engineWorkerPackageChangesInput.decode({
    ...request,
    fromExclusive: "2026-03-01T00:00:00.0000000Z",
  }).kind, "rejected");
  assert.equal(engineWorkerPackageChangesInput.decode({
    ...request,
    extra: true,
  }).kind, "rejected");
  const oversized = engineWorkerPackageChangesInput.decode({
    ...request,
    packageSetId: `package-set.${"a".repeat(100)}`,
  });
  assert.equal(oversized.kind, "rejected");
  if (oversized.kind === "rejected")
    assert.equal(oversized.reason, "oversized");
});

test("Package Activity terminal envelope remains physically successful when partial", () => {
  const decoded =
    engineWorkerPackageChangesInspection.decode(inspection());
  assert.equal(decoded.kind, "decoded");

  const settlement = mapEngineWorkerPackageChangesResult(succeeded());
  assert.equal(settlement.kind, "succeeded");
  if (settlement.kind === "succeeded") {
    assert.equal(settlement.value.content.summary.completion, "Partial");
    assert.equal(settlement.value.content.failures.length, 1);
    assert.equal(settlement.value.share.kind, "NonProjectable");
  }

  const malformed = {
    ...structuredClone(inspection()),
    extra: true,
  };
  assert.equal(
    engineWorkerPackageChangesInspection.decode(malformed).kind,
    "rejected",
  );

  assert.equal(
    isDateTimeOffsetJsonString("2024-02-29T23:59:59.1234567-14:00"),
    true,
  );
  for (const malformedTimestamp of [
    "2026-03-31T00:00:00.12345678+00:00",
    "2026-02-29T00:00:00+00:00",
    "2026-02-30T00:00:00+00:00",
    "2026-03-31T24:00:00+00:00",
    "2026-03-31T00:60:00+00:00",
    "2026-03-31T00:00:60+00:00",
    "2026-03-31T00:00:00+14:01",
    "0001-01-01T13:59:59+14:00",
    "9999-12-31T10:00:00-14:00",
  ]) {
    const malformedTimestampEnvelope = {
      ...inspection(),
      content: {
        ...inspection().content,
        rows: [{
          ...row,
          catalogActivity: {
            ...row.catalogActivity,
            commitTimestamp: malformedTimestamp,
          },
        }],
      },
    };
    assert.equal(
      engineWorkerPackageChangesInspection.decode(
        malformedTimestampEnvelope,
      ).kind,
      "rejected",
      malformedTimestamp,
    );
  }
});

test("Package Activity admits multiplicative advisory evidence at maximum rows", () => {
  const advisories = Array.from({ length: 30 }, (_, index) => ({
    ghsaId: `GHSA-0000-0000-${String(index).padStart(4, "0")}`,
    cveId: null,
    severity: "High",
    advisoryUrl: `https://github.com/advisories/${index}`,
    publishedAt: dateTimeOffsetString(
      "2026-03-01T00:00:00+00:00"),
    updatedAt: dateTimeOffsetString(
      "2026-03-02T00:00:00+00:00"),
  }));
  const populatedRows: BrowserPackageChangesRow[] =
    Array.from({ length: 1_000 }, (_, index) => {
      const packageId = `Example.Package.${index}`;
      return {
        ...row,
        catalogActivity: {
          ...row.catalogActivity,
          packageId,
          normalizedPackageId: packageId.toLowerCase(),
          leafUrl:
            `https://api.nuget.org/v3/catalog0/page/${packageId}.json`,
        },
        currentAdvisoryContext: {
          availability: "Complete",
          advisories,
        },
        fixedVersionEvidence: advisoryEvidence,
      };
    });
  const maximum = inspection();
  const populated = {
    ...maximum,
    content: {
      ...maximum.content,
      request: {
        ...maximum.content.request,
        packageScope: {
          ...maximum.content.request.packageScope,
          packageIds: populatedRows.map(
            item => item.catalogActivity.packageId),
        },
        maximumRows: 1_000,
      },
      rows: populatedRows,
      summary: {
        ...maximum.content.summary,
        advisoryEvidence: {
          ...maximum.content.summary.advisoryEvidence,
          packages: populatedRows.map(item => ({
            packageId: item.catalogActivity.packageId,
            version: item.catalogActivity.version,
            currentAdvisoryContext: item.currentAdvisoryContext,
            fixedVersionEvidence: item.fixedVersionEvidence,
          })),
        },
        eligibleRowCount: 1_000,
        returnedRowCount: 1_000,
      },
    },
  };

  assert.equal(
    engineWorkerPackageChangesInspection.decode(populated).kind,
    "decoded",
  );
  assert.equal(
    mapEngineWorkerPackageChangesResult({
      version: 1,
      kind: "Succeeded",
      inspection: populated,
      failureKind: null,
      error: null,
      diagnostic: null,
      reason: null,
    }).kind,
    "succeeded",
  );
});

test("Package Activity cancellation preserves the first managed reason", () => {
  assert.equal(
    engineWorkerPackageChangesCancellationIsRunning(
      { kind: "Requested", reason: "superseded" },
      "superseded",
    ),
    true,
  );
  assert.equal(
    engineWorkerPackageChangesCancellationIsRunning(
      { kind: "NotActive", reason: null },
      "user",
    ),
    false,
  );
  assert.throws(() =>
    engineWorkerPackageChangesCancellationIsRunning(
      { kind: "Requested", reason: "user" },
      "superseded",
    )
  );
});

test("Package Activity publishes progress, rows, and failures before settlement", async () => {
  const published: unknown[] = [];
  const facade: EngineWorkerPackageChangesFacade = {
    async runPackageActivity(_operationId, requestJson, eventSink) {
      assert.deepEqual(JSON.parse(requestJson), request);
      if ((typeof eventSink !== "object" && typeof eventSink !== "function")
        || eventSink === null) {
        throw new TypeError("Test event sink is unavailable.");
      }
      assert.equal(Reflect.set(eventSink, "event", JSON.stringify({
        kind: "Progress",
        progress,
        row: null,
        failure: null,
      })), true);
      assert.equal(Reflect.set(eventSink, "event", JSON.stringify({
        kind: "Row",
        progress: null,
        row,
        failure: null,
      })), true);
      assert.equal(Reflect.set(eventSink, "event", JSON.stringify({
        kind: "Failure",
        progress: null,
        row: null,
        failure,
      })), true);
      return succeeded();
    },
    cancelPackageActivity() {
      return { kind: "NotActive", reason: null };
    },
  };
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new FakeWorkerOperationCatalog();
  registerEngineWorkerPackageChangesOperation(operations, () => facade);
  const worker = new FakeWorkerRuntime<string, string>({
    scheduler: environment,
    bootstrap: {
      decoder: engineWorkerText,
      bootstrap: () => undefined,
    },
    diagnostic: engineWorkerDiagnostic,
    unknownOperationRejection: kind => ({
      error: `Unknown operation: ${kind}`,
      diagnostic: `Unknown operation: ${kind}`,
    }),
    operations,
    producerClasses: createEngineWorkerProducerClasses(),
  });
  const host = new WorkerRuntimeHost<string, string>({
    transport: new QueueWorkerRuntimeTransportFactory([worker]),
    clock: environment,
    lifecycle: environment,
    bootstrap: {
      encode: engineWorkerText.decode,
      diagnostic: engineWorkerText,
    },
    diagnostic: engineWorkerText,
    callbacks: {
      failure: () => undefined,
      diagnostic: () => undefined,
      realmReleased: () => undefined,
    },
    createDiagnostic: (kind, detail) =>
      `${kind}: ${engineWorkerDiagnostic(detail)}`.slice(0, 4_096),
    idleHeartbeatIntervalMilliseconds:
      engineWorkerPolicy.idleHeartbeatIntervalMilliseconds,
    schedulingToleranceMilliseconds:
      engineWorkerPolicy.schedulingToleranceMilliseconds,
    startupBudgetMilliseconds: 100,
    controlResponseGraceMilliseconds: 10,
    drainBudgetMilliseconds: 20,
    producerClasses: createEngineWorkerProducerClasses(),
  });
  assert.equal(host.start("").kind, "started");
  await environment.flushAsync();
  const binding = bindPackageChangesFacade(
    registerEngineWorkerPackageChangesAdapter(host),
    () => undefined,
    createSharedEngineOperationAuthority(),
  );
  const eventSink = {};
  Object.defineProperty(eventSink, "event", {
    set(value: unknown) {
      published.push(JSON.parse(String(value)));
    },
  });

  const pending = binding.runPackageActivity(
    "package-changes-1",
    JSON.stringify(request),
    eventSink,
  );
  await environment.flushAsync();
  const result = await pending;

  assert.equal(result.kind, "Succeeded");
  assert.deepEqual(
    published,
    [
      {
        kind: "Progress",
        progress,
        row: null,
        failure: null,
      },
      {
        kind: "Row",
        progress: null,
        row,
        failure: null,
      },
      {
        kind: "Failure",
        progress: null,
        row: null,
        failure,
      },
    ],
  );
  assert.deepEqual(result.inspection?.content.rows, [row]);
  assert.deepEqual(result.inspection?.content.failures, [failure]);

  binding.dispose();
  host.dispose();
});
