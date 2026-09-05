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
} from "../src/facades/inspect-web-package.d.ts";

const completionEvent: BrowserPackageQueryEvent = {
  kind: "Completed",
  row: null,
  failure: null,
  progress: null,
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

const toolMatchEvent: BrowserPackageQueryEvent = {
  kind: "Match",
  failure: null,
  progress: null,
  completion: null,
  row: {
    packageId: "Contoso.Tool",
    version: "2.0.0",
    tier: "PackageContent",
    evidence: [{
      id: "package.query.dotnet-tool-v2",
      text: "DotnetToolSettings.xml declares v2.",
    }],
    totalDownloads: 12,
    verified: false,
    producer: "nuget.org",
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
        combinesWithinSelectionGroup: false,
        displayGroupId: null,
        displayGroupLabel: null,
      },
      {
        id: "package.query.source-verified",
        label: "Verified source",
        summary: "Packages with repository provenance.",
        weight: 10,
        tier: "Nuspec",
        selectionGroupId: null,
        combinesWithinSelectionGroup: false,
        displayGroupId: null,
        displayGroupLabel: null,
      },
      {
        id: "package.query.dotnet-tool-v2",
        label: "v2",
        summary: "RID-specific .NET tool format.",
        weight: 30,
        tier: "PackageContent",
        selectionGroupId: "package.query.dotnet-tool-format",
        combinesWithinSelectionGroup: true,
        displayGroupId: "package.query.display.dotnet-tool",
        displayGroupLabel: ".NET tool format",
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
      combinesWithinSelectionGroup: false,
      displayGroupId: null,
      displayGroupLabel: null,
    },
    {
      key: "package.query.source-verified",
      label: "Verified source",
      summary: "Packages with repository provenance.",
      weight: 10,
      tier: "nuspec",
      selectionGroupId: null,
      combinesWithinSelectionGroup: false,
      displayGroupId: null,
      displayGroupLabel: null,
    },
    {
      key: "package.query.dotnet-tool-v2",
      label: "v2",
      summary: "RID-specific .NET tool format.",
      weight: 30,
      tier: "package-content",
      selectionGroupId: "package.query.dotnet-tool-format",
      combinesWithinSelectionGroup: true,
      displayGroupId: "package.query.display.dotnet-tool",
      displayGroupLabel: ".NET tool format",
    },
  ]);
});

test("Browser data source maps package-content rows and visible failures", async () => {
  const failureEvent: BrowserPackageQueryEvent = {
    kind: "Failure",
    row: null,
    progress: null,
    completion: null,
    failure: {
      packageId: "Contoso.Bad",
      version: "1.0.0",
      producer: "nuget.org",
      kind: "PackageContentEvaluation",
      message: "package content could not be evaluated",
    },
  };
  let candidateLimit = 0;
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    requestMatches() { return true; },
    async run(
      _prefix,
      _facets,
      candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      candidateLimit = candidates;
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(toolMatchEvent));
      Reflect.set(sink, "event", JSON.stringify(failureEvent));
      return completionEvent;
    },
  };
  const rows: { packageId: string; tier: string }[] = [];
  const failures: string[] = [];
  const request = withFacet(createQueryRequest("Contoso."), {
    key: "package.query.dotnet-tool-v2",
    label: "v2",
    tier: "package-content",
  });

  await createBrowserPackageQueryDataSource(engine).run(
    request,
    page => rows.push(...page.map(row => ({
      packageId: row.packageId,
      tier: row.tier,
    }))),
    failure => failures.push(failure),
    () => {},
    new AbortController().signal);

  assert.equal(candidateLimit, 20);
  assert.deepEqual(rows, [{
    packageId: "Contoso.Tool",
    tier: "package-content",
  }]);
  assert.deepEqual(
    failures,
    ["Contoso.Bad@1.0.0: package content could not be evaluated"]);
});

test("Browser data source streams matches and failures before terminal completion", async () => {
  let receivedArguments: readonly unknown[] = [];
  const progressEvent: BrowserPackageQueryEvent = {
    kind: "Progress",
    row: null,
    failure: null,
    completion: null,
    progress: {
      phase: "Manifest",
      completed: 1,
      limit: 200,
    },
  };
  const matchEvent: BrowserPackageQueryEvent = {
    kind: "Match",
    failure: null,
    progress: null,
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
    progress: null,
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
    requestMatches() { return true; },
    async run(...args) {
      receivedArguments = args;
      const eventSink = args[6];
      assert.ok(typeof eventSink === "object" && eventSink !== null);
      Reflect.set(eventSink, "event", JSON.stringify(progressEvent));
      Reflect.set(eventSink, "event", JSON.stringify(matchEvent));
      Reflect.set(eventSink, "event", JSON.stringify(failureEvent));
      return completionEvent;
    },
  };
  const rows: string[] = [];
  const failures: string[] = [];
  const progress: string[] = [];
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
    checkpoint => progress.push(
      `${checkpoint.phase}:${checkpoint.completed}/${checkpoint.limit}`),
    new AbortController().signal);

  assert.deepEqual(receivedArguments.slice(0, 6), [
    "Microsoft.",
    '["package.query.source-verified"]',
    200,
    100,
    false,
    20,
  ]);
  assert.deepEqual(rows, ["Microsoft.Extensions.Hosting"]);
  assert.deepEqual(
    failures,
    ["Microsoft.Extensions.Bad@1.0.0: manifest unavailable"]);
  assert.deepEqual(progress, ["manifest:1/200"]);
  assert.deepEqual(completion, { kind: "exhausted" });
});

test("Browser data source replenishes match credit through the engine export", () => {
  const requested: number[] = [];
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    requestMatches(additionalMatchCredit) {
      requested.push(additionalMatchCredit);
      return additionalMatchCredit === 10;
    },
    async run() {
      return completionEvent;
    },
  };
  const source = createBrowserPackageQueryDataSource(engine);

  assert.equal(source.initialMatchCredit, 20);
  assert.equal(source.requestMore?.(10), true);
  assert.equal(source.requestMore?.(5), false);
  assert.deepEqual(requested, [10, 5]);
});

test("Browser data source batches consecutive matches into one controller page", async () => {
  const secondMatch = {
    ...toolMatchEvent,
    row: {
      ...toolMatchEvent.row!,
      packageId: "Contoso.Tool.Next",
    },
  } satisfies BrowserPackageQueryEvent;
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    requestMatches() { return true; },
    async run(
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(toolMatchEvent));
      Reflect.set(sink, "event", JSON.stringify(secondMatch));
      return completionEvent;
    },
  };
  const pages: string[][] = [];

  await createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Contoso."),
    page => pages.push(page.map(row => row.packageId)),
    () => {},
    () => {},
    new AbortController().signal);

  assert.deepEqual(
    pages,
    [["Contoso.Tool", "Contoso.Tool.Next"]]);
});

test("Browser progress is delivered while later engine work remains pending", async () => {
  let releaseEngine!: () => void;
  const engineGate = new Promise<void>(resolve => { releaseEngine = resolve; });
  const received: string[] = [];
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    requestMatches() { return true; },
    async run(
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify({
        kind: "Progress",
        row: null,
        failure: null,
        completion: null,
        progress: {
          phase: "Manifest",
          completed: 4,
          limit: 20,
        },
      } satisfies BrowserPackageQueryEvent));
      assert.deepEqual(
        received,
        [],
        "the synchronous managed callback must not perform UI work");
      await engineGate;
      return completionEvent;
    },
  };

  const running = createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Microsoft."),
    () => {},
    () => {},
    progress => received.push(
      `${progress.phase}:${progress.completed}/${progress.limit}`),
    new AbortController().signal);
  await Promise.resolve();

  assert.deepEqual(received, ["manifest:4/20"]);
  releaseEngine();
  assert.deepEqual(await running, { kind: "exhausted" });
});

test("established durable events flush before producer failure is reported", async () => {
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    requestMatches() { return true; },
    async run(
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(toolMatchEvent));
      throw new Error("producer failed");
    },
  };
  const rows: string[] = [];

  await assert.rejects(
    createBrowserPackageQueryDataSource(engine).run(
      createQueryRequest("Contoso."),
      page => rows.push(...page.map(row => row.packageId)),
      () => {},
      () => {},
      new AbortController().signal),
    /producer failed/);

  assert.deepEqual(rows, ["Contoso.Tool"]);
});

test("established durable events reach the generation guard before cancellation returns", async () => {
  let releaseEngine!: () => void;
  const engineGate = new Promise<void>(resolve => { releaseEngine = resolve; });
  const engine: BrowserPackageQueryEngine = {
    cancel() {
      releaseEngine();
    },
    requestMatches() { return true; },
    async run(
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(toolMatchEvent));
      await engineGate;
      return completionEvent;
    },
  };
  const abort = new AbortController();
  const rows: string[] = [];

  const running = createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Contoso."),
    page => rows.push(...page.map(row => row.packageId)),
    () => {},
    () => {},
    abort.signal);
  abort.abort();

  assert.deepEqual(await running, { kind: "cancelled" });
  assert.deepEqual(rows, ["Contoso.Tool"]);
});

test("durable-event delivery failure remains visible during cancellation", async () => {
  let releaseEngine!: () => void;
  const engineGate = new Promise<void>(resolve => { releaseEngine = resolve; });
  const engine: BrowserPackageQueryEngine = {
    cancel() {
      releaseEngine();
    },
    requestMatches() { return true; },
    async run(
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(toolMatchEvent));
      await engineGate;
      return completionEvent;
    },
  };
  const abort = new AbortController();

  const running = createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Contoso."),
    () => { throw new Error("view delivery failed"); },
    () => {},
    () => {},
    abort.signal);
  abort.abort();

  await assert.rejects(running, /view delivery failed/);
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
    requestMatches() { return true; },
    async run() {
      return boundedEvent;
    },
  };

  const completion = await createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Microsoft."),
    () => {},
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
    requestMatches() { return true; },
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
    () => {},
    abort.signal);
  abort.abort();

  assert.deepEqual(await running, { kind: "cancelled" });
  assert.equal(cancelCount, 1);
});

test("malformed streamed events fail visibly instead of becoming empty output", async () => {
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    requestMatches() { return true; },
    async run(
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
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
      () => {},
      new AbortController().signal),
    /Unknown Browser package-query event/);
});

test("terminal completion is rejected on the nonterminal callback channel", async () => {
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    requestMatches() { return true; },
    async run(
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(completionEvent));
      return completionEvent;
    },
  };

  await assert.rejects(
    createBrowserPackageQueryDataSource(engine).run(
      createQueryRequest("Microsoft."),
      () => {},
      () => {},
      () => {},
      new AbortController().signal),
    /callback carried a terminal event/);
});
