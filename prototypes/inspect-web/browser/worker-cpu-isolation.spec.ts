import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { expect, test, type Page, type Worker } from "@playwright/test";
import type { createEngineWorkerProbe } from "../src/engine-worker-client.ts";

type WorkerProbe = ReturnType<typeof createEngineWorkerProbe>;
type WorkerClient = typeof import("../src/engine-worker-client.ts");
type CpuStart = ReturnType<WorkerProbe["cpuProbe"]>;

declare global {
  interface Window {
    engineWorkerProbe: WorkerProbe;
    engineWorkerEvents: string[];
    engineWorkerCpuCompleted: boolean;
    engineWorkerInputAt: number;
    engineWorkerCpuOutcome: Promise<Awaited<
      Extract<CpuStart, { kind: "started" }>["handle"]["outcome"]
    >>;
    engineWorkerCpuQuiesced: Promise<void>;
  }
}

const site = resolve(
  process.env.INSPECT_WEB_WORKER_SITE ?? "../../artifacts/inspect-web-publish/wwwroot",
);
const manifest: unknown = JSON.parse(readFileSync(resolve(site, "manifest.json"), "utf8"));
if (typeof manifest !== "object" || manifest === null
    || !("src/engine-worker-client.ts" in manifest)) {
  throw new Error("Published site is missing the Worker client entry.");
}
const manifestEntry = manifest["src/engine-worker-client.ts"];
if (typeof manifestEntry !== "object" || manifestEntry === null
    || !("file" in manifestEntry) || typeof manifestEntry.file !== "string") {
  throw new Error("Published Worker client entry has no asset.");
}
const clientUrl = `/${manifestEntry.file}`;

async function start(page: Page) {
  await page.goto("/worker-runtime-gate.html");
  return page.evaluate(async url => {
    const imported: unknown = await import(url);
    function isClient(value: unknown): value is WorkerClient {
      return typeof value === "object" && value !== null
        && "createEngineWorkerProbe" in value
        && typeof value.createEngineWorkerProbe === "function";
    }
    if (!isClient(imported)) throw new Error("Published Worker client exports are missing.");
    window.engineWorkerEvents = [];
    window.engineWorkerProbe = imported.createEngineWorkerProbe({
      callbacks: {
        failure: failure => {
          window.engineWorkerEvents.push(`failure:${failure.kind}`);
          return undefined;
        },
        diagnostic: diagnostic => {
          window.engineWorkerEvents.push(`diagnostic:${diagnostic.kind}`);
          return undefined;
        },
        realmReleased: epoch => {
          window.engineWorkerEvents.push(`released:${epoch}`);
          return undefined;
        },
      },
      operationDiagnostic: diagnostic => {
        window.engineWorkerEvents.push(`operation:${diagnostic.kind}`);
        return undefined;
      },
    });
    return window.engineWorkerProbe.host.start(location.origin);
  }, clientUrl);
}

async function beginCpu(page: Page) {
  return page.evaluate(async () => {
    const result = window.engineWorkerProbe.cpuProbe();
    if (result.kind !== "started")
      throw new Error(`Managed CPU probe refused: ${result.reason.kind}`);
    window.engineWorkerCpuCompleted = false;
    window.engineWorkerCpuOutcome = result.handle.outcome.then(outcome => {
      window.engineWorkerCpuCompleted = true;
      return outcome;
    });
    window.engineWorkerCpuQuiesced = result.handle.quiesced;
    const entry = await result.entry;
    if (entry.kind !== "entered")
      throw new Error(`Managed CPU probe closed before entry: ${entry.outcome.kind}`);
    return entry;
  });
}

async function finishCpu(page: Page) {
  return page.evaluate(async () => {
    const outcome = await window.engineWorkerCpuOutcome;
    await window.engineWorkerCpuQuiesced;
    return outcome;
  });
}

async function restart(page: Page) {
  return page.evaluate(() => {
    window.engineWorkerProbe.host.restart();
    return window.engineWorkerProbe.host.start(location.origin);
  });
}

test("managed CPU work leaves page input and a render opportunity available", async ({ page }) => {
  const workerReady = page.waitForEvent("worker");
  expect((await start(page)).kind).toBe("started");
  const worker = await workerReady;
  const entry = await beginCpu(page);
  const neighbor = page.evaluate(async () => {
    const result = window.engineWorkerProbe.probe();
    if (result.kind !== "started")
      throw new Error(`Neighboring canary refused: ${result.reason.kind}`);
    const outcome = await result.handle.outcome;
    await result.handle.quiesced;
    return outcome;
  });
  await page.evaluate(() => {
    document.querySelector("#input")!.addEventListener("click", () => {
      document.querySelector("#count")!.textContent = "1";
      document.body.dataset.workerCpuRender = "ready";
      window.engineWorkerInputAt = performance.timeOrigin + performance.now();
    });
  });
  await page.getByRole("button", { name: "Input" }).click();
  const frame = await page.evaluate(async () => {
    const at = await new Promise<number>(resolveFrame => {
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          resolveFrame(performance.timeOrigin + performance.now());
        });
      });
    });
    return {
      at,
      completed: window.engineWorkerCpuCompleted,
      inputAt: window.engineWorkerInputAt,
      renderMarker: document.body.dataset.workerCpuRender,
    };
  });
  const outcome = await finishCpu(page);
  expect(outcome.kind).toBe("succeeded");
  if (outcome.kind !== "succeeded") throw new Error("Managed CPU probe did not succeed.");
  expect(entry.at).toBe(outcome.value.startedAt);
  expect(frame.inputAt).toBeGreaterThanOrEqual(outcome.value.startedAt);
  expect(frame.inputAt).toBeLessThan(outcome.value.completedAt);
  expect(frame.at).toBeLessThan(outcome.value.completedAt);
  expect(frame.completed).toBe(false);
  expect(frame.renderMarker).toBe("ready");
  await expect(page.locator("#count")).toHaveText("1");
  expect(outcome.value).toMatchObject({ invocation: 1, checksum: "c0583d8c" });
  expect(await neighbor).toEqual({
    kind: "succeeded", value: "inspect-web-async-lowering-ok",
  });
  const warm = await beginCpu(page);
  expect((await finishCpu(page))).toMatchObject({
    kind: "succeeded", value: { invocation: 2, checksum: "c0583d8c" },
  });
  expect(warm.kind).toBe("entered");
  await page.evaluate(() => window.engineWorkerProbe.dispose());
  expect(await page.evaluate(() => window.engineWorkerEvents)).toEqual(["released:1"]);
  await expect(worker.evaluate(() => 1)).rejects.toThrow();
});

test("planned restart hard-releases active managed CPU work and its epoch cache", async ({ page }) => {
  const workers: Worker[] = [];
  page.on("worker", worker => workers.push(worker));
  expect((await start(page)).kind).toBe("started");
  await beginCpu(page);
  const oldWorker = workers[0];
  if (oldWorker === undefined) throw new Error("The first Worker was not observed.");
  const oldWorkerClosed = new Promise<void>(resolveClose => {
    oldWorker.once("close", () => resolveClose());
  });
  const restarted = await restart(page);
  expect(restarted).toMatchObject({ kind: "started", epochToken: 2 });
  await oldWorkerClosed;
  expect(await finishCpu(page)).toEqual({
    kind: "canceled", reason: "worker-restarted",
  });
  await expect(oldWorker.evaluate(() => 1)).rejects.toThrow();
  await beginCpu(page);
  expect(await finishCpu(page)).toMatchObject({
    kind: "succeeded", value: { invocation: 1, checksum: "c0583d8c" },
  });
  expect(workers).toHaveLength(2);
  await page.evaluate(() => window.engineWorkerProbe.dispose());
  expect(await page.evaluate(() => window.engineWorkerEvents)).toEqual(["released:1", "released:2"]);
});

test("silent Worker loss is detected, released, and recovered with a fresh epoch cache", async ({ page }) => {
  const workers: Worker[] = [];
  page.on("worker", worker => workers.push(worker));
  expect((await start(page)).kind).toBe("started");
  await beginCpu(page);
  expect(await finishCpu(page)).toMatchObject({
    kind: "succeeded", value: { invocation: 1, checksum: "c0583d8c" },
  });
  const oldWorker = workers[0];
  if (oldWorker === undefined) throw new Error("The first Worker was not observed.");
  const oldWorkerClosed = new Promise<void>(resolveClose => {
    oldWorker.once("close", () => resolveClose());
  });
  await oldWorker.evaluate(() => {
    setTimeout(() => globalThis.close(), 0);
  });
  await oldWorkerClosed;
  await expect.poll(() => page.evaluate(
    () => window.engineWorkerProbe.host.snapshot().phase,
  ), { timeout: 10_000 }).toBe("closed");
  expect(await page.evaluate(() => window.engineWorkerEvents)).toEqual([
    "failure:watchdog", "released:1",
  ]);
  expect((await restart(page))).toMatchObject({ kind: "started", epochToken: 2 });
  await beginCpu(page);
  expect(await finishCpu(page)).toMatchObject({
    kind: "succeeded", value: { invocation: 1, checksum: "c0583d8c" },
  });
  expect(workers).toHaveLength(2);
  await page.evaluate(() => window.engineWorkerProbe.dispose());
  expect(await page.evaluate(() => window.engineWorkerEvents)).toEqual([
    "failure:watchdog", "released:1", "released:2",
  ]);
});
