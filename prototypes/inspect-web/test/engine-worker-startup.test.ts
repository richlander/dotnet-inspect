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
  BrowserHomeDemoCatalog,
  BrowserVocabularyDocument,
} from "../src/facades/inspect-web-catalog.d.ts";
import type {
  BrowserGalleryDiscoveryCatalog,
  BrowserPackageQueryFacetCatalog,
} from "../src/facades/inspect-web-package.d.ts";
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
const vocabulary: BrowserVocabularyDocument = {
  schema_version: 1,
  sections: [{
    id: "api", name: "API", summary: "API vocabulary", categories: ["metadata"],
    accepted_by: ["type"],
    fields: [{ id: "name", label: "Name", summary: "Member name", type: "string", operators: ["="] }],
    values: [{ value: "public", extensions: [null, 42, { label: "\u03BB" }] }],
  }],
};
const demos: BrowserHomeDemoCatalog = {
  demos: [{ id: "source", title: "Source", summary: "Show generated source." }],
};
const facets: BrowserPackageQueryFacetCatalog = {
  facets: [{
    id: "license", label: "License", summary: "Package license", weight: 2, tier: "Nuspec",
    selectionGroupId: null, combinesWithinSelectionGroup: false,
    displayGroupId: "package", displayGroupLabel: "Package",
  }],
};
const gallery: BrowserGalleryDiscoveryCatalog = {
  packageType: {
    id: "package-type", label: "Type", summary: "Package type",
    suggestions: [{ value: "DotnetTool", label: "Tool" }],
  },
  orders: [{ id: "downloads", label: "Downloads", summary: "Most downloaded first." }],
};
const cases = [
  { operation: engineStartupOperations.buildIdentity, expected: identity, field: "version",
    read: (client: EngineStartupClient) => client.host.buildIdentity() },
  { operation: engineStartupOperations.listVocabulary, expected: vocabulary, field: "sections",
    read: (client: EngineStartupClient) => client.catalog.listVocabulary() },
  { operation: engineStartupOperations.listHomeDemos, expected: demos, field: "demos",
    read: (client: EngineStartupClient) => client.catalog.listHomeDemos() },
  { operation: engineStartupOperations.listPackageQueryFacets, expected: facets, field: "facets",
    read: (client: EngineStartupClient) => client.package.listPackageQueryFacets() },
  { operation: engineStartupOperations.listGalleryDiscoveryCatalog, expected: gallery, field: "orders",
    read: (client: EngineStartupClient) => client.package.listGalleryDiscoveryCatalog() },
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
    async listVocabulary() { calls.push("vocabulary"); return vocabulary; },
    async listHomeDemos() { calls.push("demos"); return demos; },
    async listPackageQueryFacets() { calls.push("facets"); return facets; },
    async listGalleryDiscoveryCatalog() { calls.push("gallery"); return gallery; },
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

test("all five cold reads share readiness and preserve full generated-shaped results", async () => {
  const ready = deferred<void>();
  const state = fixture({ bootstrap: () => ready.promise });
  const results = Promise.all(cases.map(item => item.read(state.client)));
  assert.equal(state.host.snapshot().heldOperations, 5);
  await state.environment.flushAsync();
  assert.equal(state.starts(), 1);
  assert.deepEqual(state.calls, []);
  ready.resolve();
  await state.environment.flushAsync();
  assert.deepEqual(await results, cases.map(item => item.expected));
  assert.deepEqual(state.calls, ["identity", "vocabulary", "demos", "facets", "gallery"]);
  assert.deepEqual(await Promise.all(cases.map(item => item.read(state.client))), cases.map(item => item.expected));
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
  const state = fixture({ reads: { async listVocabulary() { throw new Error("Vocabulary unavailable."); } } });
  const failure = assert.rejects(state.client.catalog.listVocabulary(), /Vocabulary unavailable/);
  const neighbor = state.client.catalog.listHomeDemos();
  await state.environment.flushAsync();
  await failure;
  assert.deepEqual(await neighbor, demos);
  assert.equal(state.host.snapshot().phase, "ready");
  assert.deepEqual(state.failures, []);
  assert.deepEqual(await state.client.package.listGalleryDiscoveryCatalog(), gallery);
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
    const value = { ...item.expected, future: { nested: [null, "preserved"] } };
    assert.deepEqual(item.operation.value.decode(encodeEngineStartupResult(value)), { kind: "decoded", value });
    const invalid = { ...item.expected, [item.field]: false };
    assert.equal(item.operation.value.decode(JSON.stringify(invalid)).kind, "rejected");
    assert.equal(item.operation.value.decode(item.expected).kind, "rejected");
    assert.equal(item.operation.value.decode("{").kind, "rejected");
  }
  assert.equal(engineStartupOperations.listPackageQueryFacets.value.decode(JSON.stringify({
    facets: [{ ...facets.facets[0], tier: 42 }],
  })).kind, "decoded");
  assert.equal(engineStartupOperations.listPackageQueryFacets.value.decode(JSON.stringify({
    facets: [{ ...facets.facets[0], tier: "not-a-tier" }],
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
