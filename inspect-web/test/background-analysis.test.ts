import assert from "node:assert/strict";
import test from "node:test";
import {
  createBackgroundAnalysisQueue,
  type BackgroundAnalysisOrder,
  type BackgroundAnalysisTask,
} from "../src/background-analysis.ts";

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>(complete => { resolve = complete; });
  return { promise, resolve };
}

const settle = () => new Promise<void>(resolve => setTimeout(resolve, 0));

interface Probe {
  readonly task: BackgroundAnalysisTask;
  readonly finish: () => void;
  relevant: boolean;
  canceled: boolean;
}

function probe(
  key: string,
  order: BackgroundAnalysisOrder,
  started: string[],
): Probe {
  const done = deferred();
  const state: Probe = {
    relevant: true,
    canceled: false,
    finish: done.resolve,
    task: {
      key,
      order,
      isRelevant: () => state.relevant,
      start: () => {
        started.push(key);
        return {
          settled: done.promise,
          cancel: () => {
            state.canceled = true;
            done.resolve();
          },
        };
      },
    },
  };
  return state;
}

function queueWith(idle: () => Promise<void> = () => Promise.resolve()) {
  const errors: unknown[] = [];
  const queue = createBackgroundAnalysisQueue({
    whenForegroundIdle: idle,
    reportError: error => { errors.push(error); },
  });
  return { queue, errors };
}

test("tasks run one at a time by order class, oldest first within a class", async () => {
  const gate = deferred();
  const { queue, errors } = queueWith(() => gate.promise);
  const started: string[] = [];
  const ranking = probe("ranking", "library-ranking", started);
  const baselineA = probe("baseline-a", "library-baseline", started);
  const baselineB = probe("baseline-b", "library-baseline", started);
  const visible = probe("visible", "visible-subject", started);

  queue.enqueue(ranking.task);
  queue.enqueue(baselineB.task);
  queue.enqueue(baselineA.task);
  queue.enqueue(visible.task);
  gate.resolve();
  await settle();
  assert.deepEqual(started, ["visible"]);

  visible.finish();
  await settle();
  baselineB.finish();
  await settle();
  baselineA.finish();
  await settle();
  ranking.finish();
  await settle();
  assert.deepEqual(started, ["visible", "baseline-b", "baseline-a", "ranking"]);
  assert.equal(queue.pending, 0);
  assert.deepEqual(errors, []);
});

test("a task starts only after the foreground is idle", async () => {
  const idle = deferred();
  const { queue } = queueWith(() => idle.promise);
  const started: string[] = [];
  const baseline = probe("baseline", "library-baseline", started);

  queue.enqueue(baseline.task);
  await settle();
  assert.deepEqual(started, []);

  idle.resolve();
  await settle();
  assert.deepEqual(started, ["baseline"]);
  baseline.finish();
});

test("irrelevant tasks are dropped before they start", async () => {
  const idle = deferred();
  const { queue } = queueWith(() => idle.promise);
  const started: string[] = [];
  const waiting = probe("waiting", "library-baseline", started);
  const queued = probe("queued", "library-baseline", started);

  queue.enqueue(waiting.task);
  queue.enqueue(queued.task);
  waiting.relevant = false;
  queued.relevant = false;
  queue.reconcile();
  idle.resolve();
  await settle();

  assert.deepEqual(started, []);
  assert.equal(queue.pending, 0);
});

test("a running task the reader has left is canceled", async () => {
  const { queue } = queueWith();
  const started: string[] = [];
  const running = probe("running", "library-baseline", started);
  const next = probe("next", "library-baseline", started);

  queue.enqueue(running.task);
  queue.enqueue(next.task);
  await settle();
  assert.deepEqual(started, ["running"]);

  running.relevant = false;
  queue.reconcile();
  await settle();

  assert.equal(running.canceled, true);
  assert.deepEqual(started, ["running", "next"]);
  next.finish();
});

test("a key that is queued or running is not queued again", async () => {
  const { queue } = queueWith();
  const started: string[] = [];
  const first = probe("same", "library-baseline", started);
  const second = probe("same", "library-baseline", started);

  queue.enqueue(first.task);
  await settle();
  queue.enqueue(second.task);
  await settle();
  assert.equal(queue.pending, 1);

  first.finish();
  await settle();
  assert.deepEqual(started, ["same"]);
  assert.equal(queue.pending, 0);
});
