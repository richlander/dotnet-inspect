import assert from "node:assert/strict";
import test from "node:test";

import {
  createBrowserPackageQueryDataSource,
  packageQueryFacets,
  type BrowserPackageQueryEngine,
} from "../src/package-query-source.ts";
import {
  createQueryRequest,
  withFacet,
  type QueryResultRow,
} from "../src/package-query.ts";
import type {
  BrowserPackageQueryCompletion,
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
    sourceCandidates: null,
    estimatedTotalHits: null,
    kind: "Exhausted",
  },
};

async function runCompletion(
  completion: BrowserPackageQueryCompletion,
  inputKind: "package" | "gallery" = "gallery",
) {
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    requestMatches() { return true; },
    async run() {
      return { ...completionEvent, completion };
    },
  };
  return createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("", inputKind),
    () => {},
    () => {},
    () => {},
    new AbortController().signal);
}

test("Browser source dispatches package and explicit Gallery input with unchanged K", async () => {
  for (const [searchText, inputKind, discovery] of [
    ["Newtonsoft.Json", "package", false],
    ["Newtonsoft.*", "package", false],
    [`${"a".repeat(100)}*`, "package", false],
    ["", "gallery", true],
  ] as const) {
    for (const matchLimit of [100, 7]) {
      const request = {
        ...withFacet(createQueryRequest(searchText, inputKind), {
          key: "producer.inspection.facet",
          label: "Producer inspection",
          tier: "nuspec",
        }),
        packageType: "Producer.CustomType",
        sourceOrderId: "producer.order.custom",
        includePrerelease: true,
        requestedMatchLimit: matchLimit,
      };
      const engine: BrowserPackageQueryEngine = {
        cancel() {},
        requestMatches() { return true; },
        async run(...args) {
          assert.deepEqual(args.slice(0, 6), [
            searchText, '["producer.inspection.facet"]', 200, matchLimit, true, 20,
          ]);
          assert.ok(typeof args[6] === "object" && args[6] !== null);
          assert.deepEqual(args.slice(7), [
            "Producer.CustomType", "producer.order.custom", discovery,
          ]);
          return completionEvent;
        },
      };
      await createBrowserPackageQueryDataSource(engine).run(
        request, () => {}, () => {}, () => {}, new AbortController().signal);
    }
  }
});

test("Browser source leaves automatic source selections unresolved in either mode", async () => {
  for (const [searchText, inputKind, discovery] of [
    ["Newtonsoft.Json", "package", false],
    ["", "gallery", true],
  ] as const) {
    const engine: BrowserPackageQueryEngine = {
      cancel() {},
      requestMatches() { return true; },
      async run(...args) {
        assert.equal(args[0], searchText);
        assert.equal(args[4], false);
        assert.deepEqual(args.slice(7), [null, null, discovery]);
        return completionEvent;
      },
    };
    await createBrowserPackageQueryDataSource(engine).run(
      createQueryRequest(searchText, inputKind),
      () => {}, () => {}, () => {}, new AbortController().signal);
  }
});

test("Gallery metadata rows preserve unknown downloads and source-authored evidence", async () => {
  for (const totalDownloads of [null, 0, 9876]) {
    for (const verified of [null, false, true]) {
      const event: BrowserPackageQueryEvent = {
        ...toolMatchEvent,
        row: {
          ...toolMatchEvent.row!,
          tier: "SearchMetadata",
          totalDownloads,
          verified,
          evidence: [{
            id: "producer.source-selection",
            text: "Source order: producer ranking; package type: Producer.Type",
          }],
        },
      };
      const rows: QueryResultRow[] = [];
      const engine: BrowserPackageQueryEngine = {
        cancel() {},
        requestMatches() { return true; },
        async run(...args) {
          assert.ok(typeof args[6] === "object" && args[6] !== null);
          Reflect.set(args[6], "event", JSON.stringify(event));
          return completionEvent;
        },
      };
      await createBrowserPackageQueryDataSource(engine).run(
        createQueryRequest("", "gallery"),
        page => rows.push(...page),
        () => {}, () => {}, new AbortController().signal);
      assert.deepEqual(rows, [{
        packageId: "Contoso.Tool",
        version: "2.0.0",
        tier: "search-metadata",
        totalDownloads,
        description: null,
        producer: "nuget.org",
        evidence: ["Source order: producer ranking; package type: Producer.Type"],
      }]);
    }
  }
});

test("Gallery row descriptions are projected unchanged from the producer", async () => {
  const description = "  Tools for <format> packages & templates.  ";
  const rows: QueryResultRow[] = [];
  const engine: BrowserPackageQueryEngine = {
    cancel() {},
    requestMatches() { return true; },
    async run(...args) {
      assert.ok(typeof args[6] === "object" && args[6] !== null);
      Reflect.set(args[6], "event", JSON.stringify({
        ...toolMatchEvent,
        row: {
          ...toolMatchEvent.row!,
          tier: "SearchMetadata",
          description,
        },
      } satisfies BrowserPackageQueryEvent));
      return completionEvent;
    },
  };

  await createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("", "gallery"),
    page => rows.push(...page),
    () => {}, () => {}, new AbortController().signal);

  assert.equal(rows.length, 1);
  assert.equal(rows[0]?.description, description);
});

test("Gallery completions retain capacity and full acquired response independently of local matches", async () => {
  const completion = await runCompletion({
    ...completionEvent.completion!,
    kind: "MatchLimitReached",
    candidateLimit: 200,
    matchLimit: 7,
    candidates: 7,
    matches: 7,
    sourceCandidates: 200,
    estimatedTotalHits: 42000,
  });

  assert.deepEqual(completion, {
    kind: "bounded",
    reason: "one finite Gallery response (capacity 200 candidates); acquired 200 candidates; local match limit 7 reached; estimated total hits: 42,000 (estimate only)",
  });
});

test("empty and short Gallery responses stay finite even with absent or zero estimates", async () => {
  for (const sourceCandidates of [0, 3]) {
    for (const estimatedTotalHits of [null, 0, 400]) {
      const completion = await runCompletion({
        ...completionEvent.completion!,
        kind: "GalleryResponseComplete",
        candidateLimit: 200,
        sourceCandidates,
        estimatedTotalHits,
        candidates: sourceCandidates,
        matches: 0,
        failures: 0,
      });
      assert.equal(completion.kind, "bounded");
      assert.ok(completion.kind === "bounded");
      assert.match(completion.reason, /one finite Gallery response/);
      assert.match(completion.reason, /capacity 200 candidates/);
      assert.ok(completion.reason.includes(`acquired ${sourceCandidates} candidates`));
      assert.ok(completion.reason.includes(estimatedTotalHits === null
        ? "estimated total hits: unavailable"
        : `estimated total hits: ${estimatedTotalHits} (estimate only)`));
      assert.doesNotMatch(completion.reason, /exhausted|all matches|local match limit/);
    }
  }
});

test("Gallery completion requires received-count evidence and known completion kind", async () => {
  await assert.rejects(runCompletion({
    ...completionEvent.completion!,
    kind: "GalleryResponseComplete",
  }), /no source candidate count/);
  await assert.rejects(runCompletion({
    ...completionEvent.completion!,
    kind: 99,
  }), /Unknown package-query completion/);
  for (const property of ["sourceCandidates", "estimatedTotalHits"]) {
    const incomplete = { ...completionEvent.completion! };
    Reflect.deleteProperty(incomplete, property);
    await assert.rejects(runCompletion(incomplete), /not a finite number/);
  }
});

test("legacy match completion and failed package input retain distinct outcomes", async () => {
  assert.deepEqual(await runCompletion({
    ...completionEvent.completion!,
    kind: "MatchLimitReached",
  }), { kind: "bounded", reason: "first 100 matches" });
  assert.deepEqual(await runCompletion({
    ...completionEvent.completion!,
    kind: "Failed",
  }), {
    kind: "failed",
    reason: "Package source work failed before the query completed.",
  });
});

test("exact package completion remains distinct for zero or one source candidate", async () => {
  for (const sourceCandidates of [0, 1]) {
    assert.deepEqual(await runCompletion({
      ...completionEvent.completion!,
      prefix: "Missing.Package",
      candidateLimit: 1,
      candidates: sourceCandidates,
      matches: sourceCandidates,
      failures: 0,
      sourceCandidates,
      estimatedTotalHits: null,
      kind: "ExactPackageComplete",
    }, "package"), { kind: "exact" });
  }
});

test("streamed metadata admission rejects unknown tiers, malformed metadata, and empty evidence", async () => {
  const invalidRows = [
    { ...toolMatchEvent.row!, tier: "UnknownTier" },
    { ...toolMatchEvent.row!, totalDownloads: "unavailable" },
    { ...toolMatchEvent.row!, verified: "unknown" },
    { ...toolMatchEvent.row!, totalDownloads: undefined },
    { ...toolMatchEvent.row!, verified: undefined },
    { ...toolMatchEvent.row!, description: undefined },
    { ...toolMatchEvent.row!, description: 123 },
    { ...toolMatchEvent.row!, tier: "SearchMetadata", evidence: [] },
    {
      ...toolMatchEvent.row!,
      tier: "SearchMetadata",
      evidence: [{ id: "producer.source", text: " " }],
    },
  ];
  for (const row of invalidRows) {
    const engine: BrowserPackageQueryEngine = {
      cancel() {},
      requestMatches() { return true; },
      async run(...args) {
        assert.ok(typeof args[6] === "object" && args[6] !== null);
        Reflect.set(args[6], "event", JSON.stringify({ ...toolMatchEvent, row }));
        return completionEvent;
      },
    };
    await assert.rejects(
      createBrowserPackageQueryDataSource(engine).run(
        createQueryRequest("", "gallery"),
        () => {}, () => {}, () => {}, new AbortController().signal),
      /Unsupported package-query row tier|not a finite number|not a boolean|not text|no evidence/);
  }
});

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
    description: null,
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
      description: null,
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
  assert.deepEqual(receivedArguments.slice(7), [null, null, false]);
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
