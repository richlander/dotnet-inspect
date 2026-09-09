import assert from "node:assert/strict";
import test from "node:test";
import {
  createOperationAuthorityPage,
} from "../src/operation-authority.ts";
import {
  bindEngineWorkerCpuProbe,
  registerEngineWorkerCpuOperation,
} from "../src/engine-worker-cpu.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
} from "../src/worker-runtime-core.ts";
import { WorkerOperationCatalog } from "../src/worker-runtime-realm.ts";

function fixture() {
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new WorkerOperationCatalog();
  let time = 1_000;
  registerEngineWorkerCpuOperation(
    operations,
    async () => ({ managedCpuCanary: () => "c0583d8c" }),
    () => time += 25,
  );
  const workers = Array.from({ length: 2 }, () => new FakeWorkerRuntime({
    scheduler: environment,
    bootstrap: { decoder: engineWorkerText, bootstrap: () => undefined },
    diagnostic: engineWorkerDiagnostic,
    unknownOperationRejection: () => ({
      error: "Unknown operation.",
      diagnostic: "Unknown operation.",
    }),
    operations,
    producerClasses: createEngineWorkerProducerClasses(),
  }));
  const failures: string[] = [];
  const diagnostics: string[] = [];
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
  const probe = bindEngineWorkerCpuProbe(
    host,
    createOperationAuthorityPage(),
    diagnostic => { diagnostics.push(diagnostic.kind); },
  );
  return { environment, failures, diagnostics, host, probe, workers };
}

async function run(state: ReturnType<typeof fixture>) {
  const started = state.probe.start();
  assert.equal(started.kind, "started");
  if (started.kind !== "started") throw new Error("CPU probe did not start.");
  await state.environment.flushAsync();
  const entry = await started.entry;
  assert.equal(entry.kind, "entered");
  const outcome = await started.handle.outcome;
  if (entry.kind === "entered" && outcome.kind === "succeeded")
    assert.equal(entry.at, outcome.value.startedAt);
  await started.handle.quiesced;
  return outcome;
}

test("managed CPU probes retain diagnostic cache state only within one Worker epoch", async () => {
  const state = fixture();
  assert.deepEqual(await run(state), {
    kind: "succeeded",
    value: {
      invocation: 1,
      checksum: "c0583d8c",
      startedAt: 1_025,
      completedAt: 1_050,
    },
  });
  assert.deepEqual(await run(state), {
    kind: "succeeded",
    value: {
      invocation: 2,
      checksum: "c0583d8c",
      startedAt: 1_075,
      completedAt: 1_100,
    },
  });
  state.host.restart();
  assert.equal(state.host.start("https://inspect.example").kind, "started");
  assert.deepEqual(await run(state), {
    kind: "succeeded",
    value: {
      invocation: 1,
      checksum: "c0583d8c",
      startedAt: 1_125,
      completedAt: 1_150,
    },
  });
  assert.deepEqual(state.failures, []);
  assert.deepEqual(state.diagnostics, []);
  state.probe.dispose();
  state.host.dispose();
});

test("entry resolves as closed when the epoch ends before the Worker reports entry", async () => {
  const state = fixture();
  const started = state.probe.start();
  assert.equal(started.kind, "started");
  if (started.kind !== "started") throw new Error("CPU probe did not start.");
  state.host.restart();
  assert.deepEqual(await started.entry, {
    kind: "closed",
    outcome: { kind: "canceled", reason: "worker-restarted" },
  });
  await started.handle.quiesced;
  state.probe.dispose();
  state.host.dispose();
});
