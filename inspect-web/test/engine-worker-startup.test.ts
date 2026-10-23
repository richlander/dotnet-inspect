import assert from "node:assert/strict";
import test from "node:test";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  bindEngineWorkerStartupClient,
  registerEngineWorkerStartupOperations,
  type EngineStartupClient,
} from "../src/engine-worker-startup.ts";
import {
  encodeEngineStartupResult,
  engineStartupInput,
  engineStartupMaximumJsonCharacters,
  engineStartupOperations,
} from "../src/engine-worker-startup-contract.ts";
import type { BrowserBuildIdentity } from "../src/facades/inspect-web-host.d.ts";
import type {
  BrowserEcosystemCatalog,
  BrowserHomeDemoCatalog,
  BrowserViewFacetCatalog,
  BrowserViewFacetDescriptor,
  BrowserVocabularyInspection,
} from "../src/facades/inspect-web-catalog.d.ts";
import type {
  BrowserPackageChangesEcosystemCatalog,
} from "../src/facades/inspect-web-package.d.ts";
import type { PackageQueryFacetData } from "../src/package-query-source.ts";
import type {
  PackageQueryDurableRowPresentationLayout,
} from "../src/package-query-durable-row.ts";
import {
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
} from "../src/worker-runtime-core.ts";
import { WorkerOperationCatalog } from "../src/worker-runtime-realm.ts";

const identity: BrowserBuildIdentity = {
  version: "1.0", commit: null, builtAtUtc: "2026-09-06T00:00:00Z", commitUrl: null,
};
const vocabulary: BrowserVocabularyInspection = {
  content: {
    formatVersion: 1,
    catalog: { value: "dotnet-inspect.product" },
    identity: { value: `sha256:${"0".repeat(64)}` },
    vocabularies: [],
  },
  share: {
    kind: "nonProjectable",
    path: "vocabulary/share",
    reason: "Static vocabulary has no Share projection.",
  },
  diagnostics: [],
};
const demos: BrowserHomeDemoCatalog = {
  demos: [{ id: "source", title: "Source", summary: "Show generated source." }],
};
const typeViewFacets: BrowserViewFacetCatalog = {
  facets: [{
    id: "type.api",
    kind: "Type",
    title: "API",
    summary: "Public API.",
    order: 100,
    role: "TypeApi",
  } satisfies BrowserViewFacetDescriptor],
};
const productEcosystems: BrowserEcosystemCatalog = {
  ecosystems: [{
    id: "ecosystem.aspire",
    title: "Aspire",
    summary: "Aspire package and demo content.",
    corePackageCount: 2,
    namespaceRootCount: 1,
    toolPackageCount: 1,
    demoCount: 2,
    hasPackageSet: true,
    hasScanner: true,
    hasPopulationLoader: false,
    hasWorkspaceRegistration: true,
  }],
};
const facetPayload = {
  scope: {
    profile: "query-preparation/v1",
    resource: "package-query",
    root: "package-query/query",
    population: "selected-facets",
    completeness: "Complete",
    catalog_facets: 1,
  },
  facet_order: ["depends"],
  facets: {
    depends: {
      name: "Direct dependency",
      summary: "Matches a direct dependency.",
      operators: ["eq"],
      value_kind: "package-id",
      examples: ["Microsoft.Extensions.Hosting"],
      effects: ["AcquisitionTier: nuspec"],
      input_rules: [],
      presentation_order: "10",
      control_kind: "input",
      presentation_kind: "value-query",
      execution_class: "nuspec",
      selection_group: null,
      combines_within_selection_group: false,
      replacement_group: null,
      option_source: null,
      options: [],
      related_operations: ["package.dependencies.inspect"],
    },
  },
  bindings: {
    browser: {
      consumer_kind: "Browser",
      gesture: null,
      input_rules: [],
      exposed_facets: ["depends"],
    },
  },
};
const facets: PackageQueryFacetData = {
  facetOrder: ["depends"],
  facets: {
    depends: {
      key: "depends",
      name: "Direct dependency",
      summary: "Matches a direct dependency.",
      operators: ["eq"],
      valueKind: "package-id",
      examples: ["Microsoft.Extensions.Hosting"],
      effects: ["AcquisitionTier: nuspec"],
      inputRules: [],
      presentationOrder: 10,
      controlKind: "input",
      presentationKind: "value-query",
      executionClass: "nuspec",
      selectionGroup: null,
      combinesWithinSelectionGroup: false,
      replacementGroup: null,
      optionSource: null,
      options: [],
      relatedOperations: ["package.dependencies.inspect"],
    },
  },
  bindings: {
    browser: {
      consumerKind: "Browser",
      exposedFacets: ["depends"],
    },
  },
};
const ecosystems: BrowserPackageChangesEcosystemCatalog = {
  version: 1,
  ecosystems: [{
    id: "ecosystem.example",
    title: "Example",
    summary: "Example product-issued Ecosystem.",
    order: 10,
    prefixes: ["Example."],
  }],
};
const durableRowLayout: PackageQueryDurableRowPresentationLayout = {
  contract: "package-query.durable-row",
  schemaIdentity: "sha256:schema",
  descriptorIdentity: "sha256:descriptor",
  vocabularySnapshotIdentity: "sha256:vocabulary",
  fields: [{
    ordinal: 0,
    schemaLocation: "/prefixItems/0",
    term: {
      identity: { value: "package-id" },
      displayLabel: "Package",
      summary: "Exact package identity.",
      mapEntries: [],
    },
  }],
};
const cases = [
  { operation: engineStartupOperations.buildIdentity, expected: identity, field: "version",
    read: (client: EngineStartupClient) => client.host.buildIdentity() },
  { operation: engineStartupOperations.inspectVocabulary, expected: vocabulary, field: "content",
    read: (client: EngineStartupClient) => client.catalog.inspectVocabulary() },
  { operation: engineStartupOperations.listHomeDemos, expected: demos, field: "demos",
    read: (client: EngineStartupClient) => client.catalog.listHomeDemos() },
  {
    operation: engineStartupOperations.listTypeViewFacets,
    expected: typeViewFacets,
    field: "facets",
    read: (client: EngineStartupClient) =>
      client.catalog.listTypeViewFacets(),
  },
  {
    operation: engineStartupOperations.listEcosystems,
    expected: productEcosystems,
    field: "ecosystems",
    read: (client: EngineStartupClient) => client.catalog.listEcosystems(),
  },
  {
    operation: engineStartupOperations.inspectPackageQueryFacets,
    expected: facets,
    encoded: facetPayload,
    field: "facets",
    read: (client: EngineStartupClient) =>
      client.package.inspectPackageQueryFacets(),
  },
  {
    operation: engineStartupOperations.packageQueryDurableRowLayout,
    expected: durableRowLayout,
    field: "contract",
    read: (client: EngineStartupClient) =>
      client.package.packageQueryDurableRowLayout(),
  },
  {
    operation: engineStartupOperations.listPackageActivityEcosystems,
    expected: ecosystems,
    field: "ecosystems",
    read: (client: EngineStartupClient) =>
      client.package.listPackageActivityEcosystems(),
  },
];

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

function fixture(options: {
  readonly bootstrap?: () => Promise<void>;
  readonly reads?: Partial<Parameters<typeof registerEngineWorkerStartupOperations>[1]>;
} = {}) {
  const environment = new ManualWorkerRuntimeEnvironment();
  const calls: string[] = [];
  const failures: string[] = [];
  const diagnostics: string[] = [];
  let starts = 0;
  const operations = new WorkerOperationCatalog();
  registerEngineWorkerStartupOperations(operations, {
    async buildIdentity() { calls.push("identity"); return identity; },
    async inspectVocabulary() { calls.push("vocabulary"); return vocabulary; },
    async listHomeDemos() { calls.push("demos"); return demos; },
    async listTypeViewFacets() {
      calls.push("type-view-facets");
      return typeViewFacets;
    },
    async listEcosystems() {
      calls.push("product-ecosystems");
      return productEcosystems;
    },
    async inspectPackageQueryFacets() {
      calls.push("facets");
      return facetPayload;
    },
    async packageQueryDurableRowLayout() {
      calls.push("durable-row-layout");
      return durableRowLayout;
    },
    async listPackageActivityEcosystems() {
      calls.push("ecosystems");
      return ecosystems;
    },
    ...options.reads,
  });
  const workers = Array.from({ length: 2 }, () => new FakeWorkerRuntime({
    scheduler: environment,
    bootstrap: {
      decoder: engineWorkerText,
      async bootstrap() { starts++; await options.bootstrap?.(); },
    },
    diagnostic: engineWorkerDiagnostic,
    unknownOperationRejection: () => ({ error: "Unknown operation.", diagnostic: "Unknown operation." }),
    operations,
    producerClasses: createEngineWorkerProducerClasses(),
  }));
  const host = new WorkerRuntimeHost({
    ...engineWorkerPolicy,
    transport: new QueueWorkerRuntimeTransportFactory(workers),
    clock: environment,
    lifecycle: environment,
    bootstrap: { encode: engineWorkerText.decode, diagnostic: engineWorkerText },
    diagnostic: engineWorkerText,
    createDiagnostic: (_kind, detail) => engineWorkerDiagnostic(detail),
    producerClasses: createEngineWorkerProducerClasses(),
    callbacks: {
      failure: failure => { failures.push(failure.kind); },
      diagnostic: diagnostic => { diagnostics.push(diagnostic.kind); },
      realmReleased: () => undefined,
    },
  });
  assert.equal(host.start("https://inspect.example").kind, "started");
  const client = bindEngineWorkerStartupClient(host, diagnostic => { diagnostics.push(diagnostic.kind); });
  return { host, client, environment, calls, failures, diagnostics, workers, starts: () => starts };
}

test("all cold reads share readiness and preserve full generated-shaped results", async () => {
  const ready = deferred<void>();
  const state = fixture({ bootstrap: () => ready.promise });
  const results = Promise.all(cases.map(item => item.read(state.client)));
  assert.equal(state.host.snapshot().heldOperations, cases.length);
  await state.environment.flushAsync();
  assert.equal(state.starts(), 1);
  assert.deepEqual(state.calls, []);
  ready.resolve();
  await state.environment.flushAsync();
  assert.deepEqual(await results, cases.map(item => item.expected));
  assert.deepEqual(
    state.calls,
    [
      "identity", "vocabulary", "demos", "type-view-facets",
      "product-ecosystems", "facets", "durable-row-layout", "ecosystems",
    ]);
  assert.deepEqual(
    await Promise.all(cases.map(item => item.read(state.client))),
    cases.map(item => item.expected));
  assert.deepEqual(
    state.calls,
    [
      "identity", "vocabulary", "demos", "type-view-facets",
      "product-ecosystems", "facets", "durable-row-layout", "ecosystems",
      "identity", "demos", "type-view-facets", "product-ecosystems",
      "facets", "durable-row-layout", "ecosystems",
    ]);
  assert.equal(state.starts(), 1);
  assert.equal(state.host.snapshot().activeOperations, 0);
  assert.deepEqual(state.diagnostics, []);
  state.host.dispose();
});

test("concurrent calls to the same method complete independently and out of order", async () => {
  const first = deferred<BrowserBuildIdentity>();
  const second = deferred<BrowserBuildIdentity>();
  let calls = 0;
  const state = fixture({ reads: { buildIdentity: () => (++calls === 1 ? first.promise : second.promise) } });
  const one = state.client.host.buildIdentity();
  const two = state.client.host.buildIdentity();
  await state.environment.flushAsync();
  second.resolve({ ...identity, version: "second" });
  assert.equal((await two).version, "second");
  first.resolve({ ...identity, version: "first" });
  assert.equal((await one).version, "first");
  assert.deepEqual(state.diagnostics, []);
  state.host.dispose();
});

test("a managed exception rejects its read without failing neighboring reads", async () => {
  const state = fixture({ reads: { async inspectVocabulary() { throw new Error("Vocabulary unavailable."); } } });
  const failure = assert.rejects(state.client.catalog.inspectVocabulary(), /Vocabulary unavailable/);
  const neighbor = state.client.catalog.listHomeDemos();
  await state.environment.flushAsync();
  await failure;
  assert.deepEqual(await neighbor, demos);
  assert.equal(state.host.snapshot().phase, "ready");
  assert.deepEqual(state.failures, []);
  assert.deepEqual(await state.client.package.inspectPackageQueryFacets(), facets);
  state.host.dispose();
});

test("one bootstrap rejection fails every held read without a replacement runtime", async () => {
  const state = fixture({ bootstrap: async () => { throw new Error("Facade readiness failed."); } });
  const failures = cases.map(item => assert.rejects(item.read(state.client), /Worker startup failed/));
  await state.environment.flushAsync();
  await Promise.all(failures);
  await assert.rejects(state.client.host.buildIdentity(), /epoch-unavailable/);
  assert.equal(state.starts(), 1);
  assert.deepEqual(state.calls, []);
  assert.deepEqual(state.failures, ["startup"]);
  state.host.dispose();
});

for (const active of [false, true]) {
  test(`disposal rejects ${active ? "active" : "held"} reads and every later call`, async () => {
    const pending = deferred<BrowserBuildIdentity>();
    const state = fixture({ reads: { buildIdentity: () => pending.promise } });
    const failure = assert.rejects(state.client.host.buildIdentity(), /worker-restarted/);
    if (active) await state.environment.flushAsync();
    state.host.dispose();
    await failure;
    await assert.rejects(state.client.host.buildIdentity(), /epoch-unavailable/);
    pending.resolve(identity);
    await state.environment.flushAsync();
    assert.deepEqual(state.diagnostics, []);
  });
}

test("a closed-epoch client cannot dispatch into a replacement epoch", async () => {
  const state = fixture();
  const pending = assert.rejects(state.client.host.buildIdentity(), /worker-restarted/);
  state.host.restart();
  await pending;
  assert.equal(state.host.start("https://inspect.example").kind, "started");
  await state.environment.flushAsync();
  await assert.rejects(state.client.host.buildIdentity(), /closed Worker epoch/);
  assert.deepEqual(state.calls, []);
  state.host.dispose();
});

test("a cached vocabulary cannot outlive its disposed epoch", async () => {
  const state = fixture();
  const warm = state.client.catalog.inspectVocabulary();
  await state.environment.flushAsync();
  assert.deepEqual(await warm, vocabulary);
  state.host.dispose();
  await assert.rejects(
    state.client.catalog.inspectVocabulary(),
    /epoch-unavailable/);
  assert.deepEqual(state.calls, ["vocabulary"]);
});

test("a cached vocabulary cannot cross into a replacement epoch", async () => {
  const state = fixture();
  const warm = state.client.catalog.inspectVocabulary();
  await state.environment.flushAsync();
  assert.deepEqual(await warm, vocabulary);
  state.host.restart();
  assert.equal(state.host.start("https://inspect.example").kind, "started");
  await state.environment.flushAsync();
  await assert.rejects(
    state.client.catalog.inspectVocabulary(),
    /closed Worker epoch/);
  assert.deepEqual(state.calls, ["vocabulary"]);
  state.host.dispose();
});

test("Worker errors reject active reads and leave no success-shaped result", async () => {
  const state = fixture({ reads: { buildIdentity: () => new Promise(() => undefined) } });
  const pending = assert.rejects(state.client.host.buildIdentity(), /Worker message delivery failed/);
  await state.environment.flushAsync();
  state.workers[0]!.emitError(new Error("Worker lost."));
  await pending;
  assert.deepEqual(state.failures, ["worker-message"]);
  state.host.dispose();
});

test("oversized generated results reject only the affected read", async () => {
  const state = fixture({ reads: {
    async buildIdentity() { return { ...identity, version: "x".repeat(engineStartupMaximumJsonCharacters) }; },
  } });
  const failure = assert.rejects(state.client.host.buildIdentity(), /exceeds 1048576 characters/);
  const neighbor = state.client.catalog.listHomeDemos();
  await state.environment.flushAsync();
  await failure;
  assert.deepEqual(await neighbor, demos);
  assert.deepEqual(state.failures, []);
  state.host.dispose();
});

test("startup decoders preserve extra JSON data and reject invalid DTO shapes", () => {
  for (const item of cases) {
    const encoded = { ...(item.encoded ?? item.expected), future: { nested: [null, "preserved"] } };
    const expected = item.operation === engineStartupOperations.inspectPackageQueryFacets
      ? item.expected
      : { ...item.expected, future: { nested: [null, "preserved"] } };
    assert.deepEqual(
      item.operation.value.decode(encodeEngineStartupResult(encoded)),
      { kind: "decoded", value: expected });
    const invalid = { ...(item.encoded ?? item.expected), [item.field]: false };
    assert.equal(item.operation.value.decode(JSON.stringify(invalid)).kind, "rejected");
    assert.equal(item.operation.value.decode(item.expected).kind, "rejected");
    assert.equal(item.operation.value.decode("{").kind, "rejected");
  }
  assert.equal(engineStartupOperations.inspectPackageQueryFacets.value.decode(
    JSON.stringify({
      ...facetPayload,
      scope: { ...facetPayload.scope, completeness: "Partial" },
    })).kind, "rejected");
  assert.equal(engineStartupOperations.inspectPackageQueryFacets.value.decode(
    JSON.stringify({
      ...facetPayload,
      facets: {
        depends: {
          ...facetPayload.facets.depends,
          operators: [42],
        },
      },
    })).kind, "rejected");
  assert.equal(engineStartupOperations.inspectPackageQueryFacets.value.decode(
    JSON.stringify({
      ...facetPayload,
      bindings: {
        browser: {
          ...facetPayload.bindings.browser,
          exposed_facets: [42],
        },
      },
    })).kind, "rejected");
  assert.equal(engineStartupOperations.inspectPackageQueryFacets.value.decode(
    JSON.stringify({
      ...facetPayload,
      facet_order: [],
    })).kind, "rejected");
  assert.equal(engineStartupOperations.inspectPackageQueryFacets.value.decode(
    JSON.stringify({
      ...facetPayload,
      facets: {
        depends: {
          ...facetPayload.facets.depends,
          presentation_order: "first",
        },
      },
    })).kind, "rejected");
  assert.equal(engineStartupOperations.inspectPackageQueryFacets.value.decode(
    JSON.stringify({
      ...facetPayload,
      facets: {
        depends: {
          ...facetPayload.facets.depends,
          presentation_order: 10,
        },
      },
    })).kind, "rejected");
  assert.equal(engineStartupOperations.inspectPackageQueryFacets.value.decode(
    JSON.stringify({
      ...facetPayload,
      facets: {
        depends: {
          ...facetPayload.facets.depends,
          presentation_order: "01",
        },
      },
    })).kind, "rejected");
  assert.equal(engineStartupOperations.inspectPackageQueryFacets.value.decode(
    JSON.stringify({
      ...facetPayload,
      facets: {
        depends: {
          ...facetPayload.facets.depends,
          presentation_order: "9007199254740992",
        },
      },
    })).kind, "rejected");
  assert.equal(engineStartupInput.decode(null).kind, "decoded");
  assert.equal(engineStartupInput.decode([]).kind, "rejected");
});

test("startup JSON accepts its exact character bound and rejects the next character", () => {
  const overhead = JSON.stringify({ ...identity, version: "" }).length;
  const value = { ...identity, version: "x".repeat(engineStartupMaximumJsonCharacters - overhead) };
  const encoded = encodeEngineStartupResult(value);
  assert.equal(encoded.length, engineStartupMaximumJsonCharacters);
  assert.deepEqual(engineStartupOperations.buildIdentity.value.decode(encoded), { kind: "decoded", value });
  assert.deepEqual(engineStartupOperations.buildIdentity.value.decode(`${encoded} `), {
    kind: "rejected", reason: "oversized", message: "Startup result JSON exceeds 1048576 characters.",
  });
  assert.throws(() => encodeEngineStartupResult({ ...value, version: `${value.version}x` }), /exceeds/);
});
