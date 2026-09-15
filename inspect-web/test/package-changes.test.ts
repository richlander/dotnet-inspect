import assert from "node:assert/strict";
import test from "node:test";

import {
  createPackageChangesController,
  createPackageChangesRequest,
  initialPackageChangesState,
  type PackageChangesDataSource,
  type PackageChangesTerminalResult,
} from "../src/package-changes.ts";
import { changeRow, failure, inspection, progress } from "./package-changes-fixture.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

test("controller preserves progressive order and reconciles the terminal document authoritatively", async () => {
  const first = changeRow("Example.Repeated");
  const second = changeRow("Example.Repeated", { securityRelease: true });
  const source: PackageChangesDataSource = {
    async run(_request, onProgress, onRow, onFailure) {
      onProgress(progress);
      onRow(first);
      onFailure(failure);
      onRow(second);
      return {
        kind: "succeeded",
        inspection: inspection([first, second], [failure], "Partial"),
      };
    },
  };
  const state = initialPackageChangesState();
  const controller = createPackageChangesController(state, source, () => {});

  await controller.run(createPackageChangesRequest("package-set.example"));

  assert.deepEqual(state.rows, [first, second]);
  assert.deepEqual(state.failures, [failure]);
  assert.deepEqual(state.progress, [progress]);
  assert.equal(state.settlement.kind, "succeeded");
  assert.equal(
    state.settlement.kind === "succeeded"
      ? state.settlement.inspection.content.summary.completion
      : null,
    "Partial");
});

test("explicit cancellation retains rows while awaiting physical cancellation", async () => {
  const pending = deferred<PackageChangesTerminalResult>();
  const admitted = changeRow("Example.Admitted");
  let signal!: AbortSignal;
  const source: PackageChangesDataSource = {
    async run(_request, _onProgress, onRow, _onFailure, abortSignal) {
      signal = abortSignal;
      onRow(admitted);
      return await pending.promise;
    },
  };
  const state = initialPackageChangesState();
  const controller = createPackageChangesController(state, source, () => {});
  const running = controller.run(
    createPackageChangesRequest("package-set.example"));

  controller.cancel("user");

  assert.equal(signal.aborted, true);
  assert.equal(signal.reason, "user");
  assert.deepEqual(state.rows, [admitted]);
  assert.deepEqual(state.settlement, { kind: "canceling", reason: "user" });
  pending.resolve({ kind: "canceled", reason: "user" });
  await running;
  assert.deepEqual(state.rows, [admitted]);
  assert.deepEqual(state.settlement, { kind: "canceled", reason: "user" });
});

test("a cancellation race preserves authoritative success", async () => {
  const pending = deferred<PackageChangesTerminalResult>();
  const progressive = changeRow("Example.Progressive");
  const terminal = changeRow("Example.Terminal");
  const source: PackageChangesDataSource = {
    async run(_request, _onProgress, onRow) {
      onRow(progressive);
      return await pending.promise;
    },
  };
  const state = initialPackageChangesState();
  const controller = createPackageChangesController(state, source, () => {});
  const running = controller.run(
    createPackageChangesRequest("package-set.example"));

  controller.cancel("user");
  pending.resolve({
    kind: "succeeded",
    inspection: inspection([terminal]),
  });
  await running;

  assert.deepEqual(state.rows, [terminal]);
  assert.equal(state.settlement.kind, "succeeded");
});

test("a cancellation race preserves authoritative failure", async () => {
  const pending = deferred<PackageChangesTerminalResult>();
  const source: PackageChangesDataSource = {
    async run() {
      return await pending.promise;
    },
  };
  const state = initialPackageChangesState();
  const controller = createPackageChangesController(state, source, () => {});
  const running = controller.run(
    createPackageChangesRequest("package-set.example"));

  controller.cancel("disposed");
  pending.resolve({
    kind: "failed",
    error: "Worker failed after cancellation was requested.",
    diagnostic: "physical failure",
  });
  await running;

  assert.deepEqual(state.settlement, {
    kind: "failed",
    error: "Worker failed after cancellation was requested.",
    diagnostic: "physical failure",
  });
});

test("replacement suppresses stale callbacks and terminal settlement", async () => {
  const runs: Array<{
    readonly publish: (row: ReturnType<typeof changeRow>) => void;
    readonly finish: (result: PackageChangesTerminalResult) => void;
  }> = [];
  const source: PackageChangesDataSource = {
    run(_request, _onProgress, onRow) {
      const terminal = deferred<PackageChangesTerminalResult>();
      runs.push({ publish: onRow, finish: terminal.resolve });
      return terminal.promise;
    },
  };
  const state = initialPackageChangesState();
  const controller = createPackageChangesController(state, source, () => {});
  const oldRun = controller.run(createPackageChangesRequest("package-set.old"));
  const newRun = controller.run(createPackageChangesRequest("package-set.new"));

  runs[0]!.publish(changeRow("Stale.Package"));
  runs[0]!.finish({
    kind: "succeeded",
    inspection: inspection([changeRow("Stale.Terminal")]),
  });
  runs[1]!.publish(changeRow("Current.Package"));
  runs[1]!.finish({
    kind: "succeeded",
    inspection: inspection([changeRow("Current.Package")]),
  });
  await Promise.all([oldRun, newRun]);

  assert.equal(state.request?.packageSetId, "package-set.new");
  assert.deepEqual(
    state.rows.map(row => row.catalogActivity.packageId),
    ["Current.Package"]);
});

test("physical failure remains distinct from semantic failed completion", async () => {
  const state = initialPackageChangesState();
  const physical = createPackageChangesController(
    state,
    {
      async run() {
        return {
          kind: "failed",
          error: "Worker operation failed.",
          diagnostic: "transport diagnostic",
        };
      },
    },
    () => {});
  await physical.run(createPackageChangesRequest("package-set.example"));
  assert.deepEqual(state.settlement, {
    kind: "failed",
    error: "Worker operation failed.",
    diagnostic: "transport diagnostic",
  });

  const semantic = createPackageChangesController(
    state,
    {
      async run() {
        return {
          kind: "succeeded",
          inspection: inspection([], [], "Failed"),
        };
      },
    },
    () => {});
  await semantic.run(createPackageChangesRequest("package-set.example"));
  assert.equal(state.settlement.kind, "succeeded");
  assert.equal(
    state.settlement.kind === "succeeded"
      ? state.settlement.inspection.content.summary.completion
      : null,
    "Failed");
});
