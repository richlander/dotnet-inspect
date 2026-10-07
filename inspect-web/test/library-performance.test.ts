import assert from "node:assert/strict";
import test from "node:test";

import {
  createLibraryPerformanceController,
  initialLibraryPerformanceState,
  type LibraryPerformanceDataSource,
  type LibraryPerformanceTerminalResult,
} from "../src/library-performance.ts";
import { member, summary } from "./library-performance-fixture.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

const request = {
  packageId: "Example.Library",
  version: "1.0.0",
  targetFramework: "net8.0",
  assemblyName: "Example.Library.dll",
};

test("controller appends Item members in arrival order and reconciles the terminal summary", async () => {
  const second = { ...member, memberName: "ComputeAgain" };
  const source: LibraryPerformanceDataSource = {
    async run(_request, onItem) {
      onItem(member);
      onItem(second);
      return { kind: "succeeded", summary: summary() };
    },
  };
  const state = initialLibraryPerformanceState();
  const controller = createLibraryPerformanceController(state, source, () => {});

  await controller.run(request);

  assert.deepEqual(state.members, [member, second]);
  assert.deepEqual(state.settlement, { kind: "succeeded", summary: summary() });
});

test("explicit cancellation retains admitted members while awaiting physical cancellation", async () => {
  const pending = deferred<LibraryPerformanceTerminalResult>();
  let signal!: AbortSignal;
  const source: LibraryPerformanceDataSource = {
    async run(_request, onItem, abortSignal) {
      signal = abortSignal;
      onItem(member);
      return await pending.promise;
    },
  };
  const state = initialLibraryPerformanceState();
  const controller = createLibraryPerformanceController(state, source, () => {});
  const running = controller.run(request);

  controller.cancel("user");

  assert.equal(signal.aborted, true);
  assert.equal(signal.reason, "user");
  assert.deepEqual(state.members, [member]);
  assert.deepEqual(state.settlement, { kind: "canceling", reason: "user" });
  pending.resolve({ kind: "canceled", reason: "user" });
  await running;
  assert.deepEqual(state.members, [member]);
  assert.deepEqual(state.settlement, { kind: "canceled", reason: "user" });
});

test("a cancellation race preserves authoritative success", async () => {
  const pending = deferred<LibraryPerformanceTerminalResult>();
  const source: LibraryPerformanceDataSource = {
    async run(_request, onItem) {
      onItem(member);
      return await pending.promise;
    },
  };
  const state = initialLibraryPerformanceState();
  const controller = createLibraryPerformanceController(state, source, () => {});
  const running = controller.run(request);

  controller.cancel("user");
  pending.resolve({ kind: "succeeded", summary: summary() });
  await running;

  assert.deepEqual(state.settlement, { kind: "succeeded", summary: summary() });
});

test("fresh reset retires a canceled generation before delayed settlement", async () => {
  const pending = deferred<LibraryPerformanceTerminalResult>();
  let signal!: AbortSignal;
  const source: LibraryPerformanceDataSource = {
    async run(_request, onItem, abortSignal) {
      signal = abortSignal;
      onItem(member);
      return await pending.promise;
    },
  };
  const state = initialLibraryPerformanceState();
  const controller = createLibraryPerformanceController(state, source, () => {});
  const running = controller.run(request);

  controller.cancel("disposed");
  controller.reset();

  assert.equal(signal.aborted, true);
  assert.deepEqual(state, initialLibraryPerformanceState());
  pending.resolve({ kind: "succeeded", summary: summary() });
  await running;
  assert.deepEqual(state, initialLibraryPerformanceState());
});

test("replacement suppresses stale callbacks and terminal settlement", async () => {
  const runs: Array<{
    readonly publish: (item: typeof member) => void;
    readonly finish: (result: LibraryPerformanceTerminalResult) => void;
  }> = [];
  const source: LibraryPerformanceDataSource = {
    run(_request, onItem) {
      const terminal = deferred<LibraryPerformanceTerminalResult>();
      runs.push({ publish: onItem, finish: terminal.resolve });
      return terminal.promise;
    },
  };
  const state = initialLibraryPerformanceState();
  const controller = createLibraryPerformanceController(state, source, () => {});
  const oldRun = controller.run({ ...request, packageId: "Old.Library" });
  const newRun = controller.run({ ...request, packageId: "New.Library" });

  runs[0]!.publish({ ...member, memberName: "Stale" });
  runs[0]!.finish({ kind: "succeeded", summary: summary() });
  runs[1]!.publish({ ...member, memberName: "Current" });
  runs[1]!.finish({ kind: "succeeded", summary: summary() });
  await Promise.all([oldRun, newRun]);

  assert.equal(state.request?.packageId, "New.Library");
  assert.deepEqual(state.members.map(item => item.memberName), ["Current"]);
});

test("physical failure remains distinct from a semantic failed settlement", async () => {
  const state = initialLibraryPerformanceState();
  const physical = createLibraryPerformanceController(
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
  await physical.run(request);
  assert.deepEqual(state.settlement, {
    kind: "failed",
    error: "Worker operation failed.",
    diagnostic: "transport diagnostic",
  });

  const semantic = createLibraryPerformanceController(
    state,
    {
      async run() {
        return {
          kind: "succeeded",
          summary: { ...summary(), inspectionError: "analysis could not run" },
        };
      },
    },
    () => {});
  await semantic.run(request);
  assert.equal(state.settlement.kind, "succeeded");
  assert.equal(
    state.settlement.kind === "succeeded"
      ? state.settlement.summary.inspectionError
      : null,
    "analysis could not run");
});

test("an unhandled data source rejection is reported as a failed settlement", async () => {
  const state = initialLibraryPerformanceState();
  const controller = createLibraryPerformanceController(
    state,
    {
      run() {
        return Promise.reject(new Error("transport exploded"));
      },
    },
    () => {});
  await controller.run(request);
  assert.deepEqual(state.settlement, {
    kind: "failed",
    error: "transport exploded",
    diagnostic: null,
  });
});
