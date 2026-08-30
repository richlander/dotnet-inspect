import assert from "node:assert/strict";
import test from "node:test";

import {
  createBrowserPackageQueryDataSource,
  packageQueryFacets,
  type BrowserPackageQueryEngine,
} from "../src/package-query-source.ts";
import { createQueryRequest, withFacet } from "../src/package-query.ts";
import type {
  BrowserPackageQueryEvent,
  BrowserPackageQueryFacetCatalog,
} from "../src/inspect-web-engine.d.ts";

const completionEvent: BrowserPackageQueryEvent = {
  kind: "Completed",
  row: null,
  failure: null,
  completion: {
    prefix: "Microsoft.",
    producer: "nuget.org",
    candidateLimit: 200,
    matchLimit: 100,
    candidates: 1,
    matches: 1,
    failures: 1,
    kind: "Exhausted",
  },
};

test("packageQueryFacets preserves product descriptors and producer ordering", () => {
  const catalog: BrowserPackageQueryFacetCatalog = {
    facets: [
      {
        id: "package.query.no-dependencies",
        label: "No dependencies",
        summary: "Packages with no dependency groups.",
        weight: 20,
        tier: "Nuspec",
        selectionGroupId: "package.query.dependencies",
      },
      {
        id: "package.query.source-verified",
        label: "Verified source",
        summary: "Packages with repository provenance.",
        weight: 10,
        tier: "Nuspec",
        selectionGroupId: null,
      },
    ],
  };

  assert.deepEqual(packageQueryFacets(catalog), [
    {
      key: "package.query.no-dependencies",
      label: "No dependencies",
      summary: "Packages with no dependency groups.",
      weight: 20,
      tier: "nuspec",
      selectionGroupId: "package.query.dependencies",
    },
    {
      key: "package.query.source-verified",
      label: "Verified source",
      summary: "Packages with repository provenance.",
      weight: 10,
      tier: "nuspec",
      selectionGroupId: null,
    },
  ]);
});

test("Browser data source streams matches and failures before terminal completion", async () => {
  let receivedArguments: readonly unknown[] = [];
  const matchEvent: BrowserPackageQueryEvent = {
    kind: "Match",
    failure: null,
    completion: null,
    row: {
      packageId: "Microsoft.Extensions.Hosting",
      version: "10.0.0",
      tier: "Nuspec",
      evidence: [{ id: "package.query.source-verified", text: "Verified source" }],
      totalDownloads: 1234,
      verified: true,
      producer: "nuget.org",
    },
  };
  const failureEvent: BrowserPackageQueryEvent = {
    kind: "Failure",
    row: null,
    completion: null,
    failure: {
      packageId: "Microsoft.Extensions.Bad",
      version: "1.0.0",
      producer: "nuget.org",
      kind: "ManifestAcquisition",
      message: "manifest unavailable",
    },
  };
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    async run(...args) {
      receivedArguments = args;
      const eventSink = args[5];
      assert.ok(typeof eventSink === "object" && eventSink !== null);
      Reflect.set(eventSink, "event", JSON.stringify(matchEvent));
      Reflect.set(eventSink, "event", JSON.stringify(failureEvent));
      Reflect.set(eventSink, "event", JSON.stringify(completionEvent));
      return completionEvent;
    },
  };
  const rows: string[] = [];
  const failures: string[] = [];
  const request = withFacet(
    createQueryRequest("Microsoft."),
    {
      key: "package.query.source-verified",
      label: "Verified source",
      tier: "nuspec",
    });

  const completion = await createBrowserPackageQueryDataSource(engine).run(
    request,
    page => rows.push(...page.map(row => row.packageId)),
    failure => failures.push(failure),
    new AbortController().signal);

  assert.deepEqual(receivedArguments.slice(0, 5), [
    "Microsoft.",
    '["package.query.source-verified"]',
    200,
    100,
    false,
  ]);
  assert.deepEqual(rows, ["Microsoft.Extensions.Hosting"]);
  assert.deepEqual(
    failures,
    ["Microsoft.Extensions.Bad@1.0.0: manifest unavailable"]);
  assert.deepEqual(completion, { kind: "exhausted" });
});

test("Browser data source maps product bounds without calling them exhaustive", async () => {
  const boundedEvent: BrowserPackageQueryEvent = {
    ...completionEvent,
    completion: {
      ...completionEvent.completion!,
      kind: "CandidateLimitReached",
      candidateLimit: 200,
    },
  };
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    async run() {
      return boundedEvent;
    },
  };

  const completion = await createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Microsoft."),
    () => {},
    () => {},
    new AbortController().signal);

  assert.deepEqual(completion, {
    kind: "bounded",
    reason: "first 200 candidates",
  });
});

test("aborting Browser query work invokes the engine cancellation export", async () => {
  let cancelCount = 0;
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const engine: BrowserPackageQueryEngine = {
    cancel() {
      cancelCount++;
      release();
    },
    async run() {
      await gate;
      return completionEvent;
    },
  };
  const abort = new AbortController();

  const running = createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Microsoft."),
    () => {},
    () => {},
    abort.signal);
  abort.abort();

  assert.deepEqual(await running, { kind: "cancelled" });
  assert.equal(cancelCount, 1);
});

test("malformed streamed events fail visibly instead of becoming empty output", async () => {
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    async run(_prefix, _facets, _candidates, _matches, _prerelease, sink) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", "{}");
      return completionEvent;
    },
  };

  await assert.rejects(
    createBrowserPackageQueryDataSource(engine).run(
      createQueryRequest("Microsoft."),
      () => {},
      () => {},
      new AbortController().signal),
    /Unknown Browser package-query event/);
});
