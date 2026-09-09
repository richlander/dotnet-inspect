import assert from "node:assert/strict";
import test from "node:test";

import type {
  EngineWorkerPackageQueryAdapter,
  EngineWorkerPackageQueryCompletionEvent,
  EngineWorkerPackageQueryDurableEvent,
} from "../src/engine-worker-package-query.ts";
import {
  createOperationAuthorityPage,
  type OperationCancelReason,
  type OperationProducerSink,
} from "../src/operation-authority.ts";
import {
  createEngineWorkerPackageQueryDataSource,
} from "../src/package-query-source.ts";
import {
  createQueryRequest,
} from "../src/package-query.ts";

const match: EngineWorkerPackageQueryDurableEvent = {
  kind: "Match",
  row: {
    packageId: "Contoso.Library",
    version: "1.2.3",
    tier: "Nuspec",
    evidence: [{
      id: "package-id",
      text: "Contoso.Library",
      scope: "Package",
      summary: null,
    }],
    totalDownloads: 42,
    verified: true,
    producer: "nuget-gallery",
    description: "A test package.",
    rootRequest: "root1:Contoso.Library@1.2.3",
  },
  failure: null,
  completion: null,
  progress: null,
  assessment: null,
};

const completed: EngineWorkerPackageQueryCompletionEvent = {
  kind: "Completed",
  row: null,
  failure: null,
  completion: {
    prefix: "Contoso.",
    producer: "nuget-gallery",
    candidateLimit: 200,
    matchLimit: 100,
    candidates: 1,
    matches: 1,
    failures: 0,
    kind: "Exhausted",
    sourceCandidates: 1,
    estimatedTotalHits: 1,
    semanticMisses: 0,
    notApplicable: 0,
    scope: "prefix",
  },
  progress: null,
  assessment: null,
};

type PackageQuerySink = OperationProducerSink<
  EngineWorkerPackageQueryCompletionEvent,
  string,
  never,
  EngineWorkerPackageQueryDurableEvent
>;

function preparedSink(
  value: PackageQuerySink | null,
): PackageQuerySink {
  if (value === null) {
    throw new Error("Package Query producer did not prepare.");
  }
  return value;
}

test("Worker Package Query data source forwards durable rows, credit, and completion", async () => {
  let sink: PackageQuerySink | null = null;
  let operationId = "";
  const controls: Array<readonly [string, number]> = [];
  const cancellations: Array<readonly [string, OperationCancelReason]> = [];
  const adapter: EngineWorkerPackageQueryAdapter = {
    prepare(identity, _request, producerSink) {
      operationId = identity.id;
      sink = producerSink;
      return {
        kind: "prepared",
        binding: {
          requestCancellation(reason) {
            cancellations.push([identity.id, reason]);
            producerSink.reportTerminal({
              kind: "canceled",
              reason,
            });
            producerSink.reportQuiesced();
            return undefined;
          },
          activate: () => undefined,
          abandon: () => undefined,
        },
      };
    },
    async requestControl(id, additionalMatchCredit) {
      controls.push([id, additionalMatchCredit]);
      return {
        kind: "acknowledged",
        value: additionalMatchCredit,
      };
    },
  };
  const diagnostics: unknown[] = [];
  const source = createEngineWorkerPackageQueryDataSource(
    createOperationAuthorityPage({
      allocation: {
        createId: () => "worker-package-query",
      },
    }),
    adapter,
    {
      reportOperationDiagnostic(diagnostic) {
        diagnostics.push(diagnostic);
        return undefined;
      },
    });
  const rows: string[] = [];
  const abort = new AbortController();
  const running = source.run(
    createQueryRequest("Contoso."),
    page => rows.push(...page.map(row => row.packageId)),
    () => undefined,
    () => undefined,
    abort.signal);

  assert.equal(operationId, "worker-package-query");
  const activeSink = preparedSink(sink);
  activeSink.reportDurable(match);
  assert.deepEqual(rows, ["Contoso.Library"]);
  assert.equal(await source.requestMore?.(10), true);
  assert.deepEqual(controls, [["worker-package-query", 10]]);
  activeSink.reportTerminal({ kind: "succeeded", value: completed });
  activeSink.reportQuiesced();

  assert.deepEqual(await running, { kind: "exhausted" });
  assert.deepEqual(cancellations, []);
  assert.deepEqual(diagnostics, []);
  assert.equal(await source.requestMore?.(10), false);
});

test("Worker Package Query data source uses authority cancellation and rejects control failures", async () => {
  let sink: PackageQuerySink | null = null;
  const cancellations: OperationCancelReason[] = [];
  const adapter: EngineWorkerPackageQueryAdapter = {
    prepare(_identity, _request, producerSink) {
      sink = producerSink;
      return {
        kind: "prepared",
        binding: {
          requestCancellation(reason) {
            cancellations.push(reason);
            producerSink.reportTerminal({
              kind: "canceled",
              reason,
            });
            producerSink.reportQuiesced();
            return undefined;
          },
          activate: () => undefined,
          abandon: () => undefined,
        },
      };
    },
    async requestControl() {
      return {
        kind: "failed",
        error: "credit unavailable",
      };
    },
  };
  const source = createEngineWorkerPackageQueryDataSource(
    createOperationAuthorityPage({
      allocation: {
        createId: () => "cancel-package-query",
      },
    }),
    adapter,
    {
      reportOperationDiagnostic: () => undefined,
    });
  const abort = new AbortController();
  const running = source.run(
    createQueryRequest("Contoso."),
    () => undefined,
    () => undefined,
    () => undefined,
    abort.signal);

  assert.notEqual(sink, null);
  if (source.requestMore === undefined) {
    throw new Error("Worker Package Query source has no credit control.");
  }
  await assert.rejects(
    Promise.resolve(source.requestMore(10)),
    /credit unavailable/);
  abort.abort("superseded");
  assert.deepEqual(await running, { kind: "cancelled" });
  assert.deepEqual(cancellations, ["superseded"]);
});
