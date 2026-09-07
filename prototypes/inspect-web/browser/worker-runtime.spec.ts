import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { expect, test, type Page, type Worker } from "@playwright/test";
import type { createEngineWorkerProbe } from "../src/engine-worker-client.ts";

type WorkerProbe = ReturnType<typeof createEngineWorkerProbe>;
type WorkerClient = typeof import("../src/engine-worker-client.ts");

declare global {
  var engineWorkerEpochReporterGate: Pick<
    typeof import("/inspect-web-host.js"),
    "registerEpochWorkReporter" | "drainEpochWorkReporter" | "unregisterEpochWorkReporter"
  >;
  interface Window {
    engineWorkerProbe: WorkerProbe;
    engineWorkerEvents: string[];
    engineWorkerPending: ReturnType<WorkerProbe["probe"]> | null;
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
const entry = manifest["src/engine-worker-client.ts"];
if (typeof entry !== "object" || entry === null
    || !("file" in entry) || typeof entry.file !== "string") {
  throw new Error("Published Worker client entry has no asset.");
}
const clientUrl = `/${entry.file}`;

async function start(page: Page, startupBudgetMilliseconds = 60_000) {
  await page.goto("/worker-runtime-gate.html");
  return page.evaluate(async ({ url, budget }) => {
    const imported: unknown = await import(url);
    function isClient(value: unknown): value is WorkerClient {
      return typeof value === "object" && value !== null
        && "createEngineWorkerProbe" in value
        && typeof value.createEngineWorkerProbe === "function";
    }
    if (!isClient(imported)) throw new Error("Published Worker client exports are missing.");
    window.engineWorkerEvents = [];
    window.engineWorkerProbe = imported.createEngineWorkerProbe({
      startupBudgetMilliseconds: budget,
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
    const started = window.engineWorkerProbe.host.start(location.origin);
    window.engineWorkerPending = window.engineWorkerProbe.probe();
    return started;
  }, { url: clientUrl, budget: startupBudgetMilliseconds });
}

async function canary(page: Page) {
  return page.evaluate(async () => {
    const result = window.engineWorkerPending ?? window.engineWorkerProbe.probe();
    window.engineWorkerPending = null;
    if (result.kind !== "started") throw new Error(`Canary refused: ${result.reason.kind}`);
    const outcome = await result.handle.outcome;
    await result.handle.quiesced;
    return outcome;
  });
}

test("published generated facades boot in a real Worker and serve cold and warm managed calls", async ({ page }) => {
  const workers: Worker[] = [];
  page.on("worker", worker => workers.push(worker));
  expect((await start(page)).kind).toBe("started");
  expect(await canary(page)).toEqual({
    kind: "succeeded", value: "inspect-web-async-lowering-ok",
  });
  expect(await canary(page)).toEqual({
    kind: "succeeded", value: "inspect-web-async-lowering-ok",
  });
  expect(workers).toHaveLength(1);
  const worker = workers[0];
  if (worker === undefined) throw new Error("The runtime did not create a Worker.");
  expect(await worker.evaluate(() => globalThis.constructor.name)).toBe("DedicatedWorkerGlobalScope");
  const origin = await page.evaluate(() => window.engineWorkerProbe.host.snapshot().lastTaskEvidenceOrigin);
  await expect.poll(() => page.evaluate(
    () => window.engineWorkerProbe.host.snapshot().lastTaskEvidenceOrigin,
  )).toBeGreaterThan(origin ?? 0);
  await page.evaluate(() => window.engineWorkerProbe.dispose());
  expect(await page.evaluate(() => window.engineWorkerEvents)).toEqual(["released:1"]);
  await expect(worker.evaluate(() => 1)).rejects.toThrow();
});

test("restart destroys the old realm and explicitly boots a new epoch", async ({ page }) => {
  expect((await start(page)).kind).toBe("started");
  expect((await canary(page)).kind).toBe("succeeded");
  const restarted = await page.evaluate(() => {
    window.engineWorkerProbe.host.restart();
    return window.engineWorkerProbe.host.start(location.origin);
  });
  expect(restarted.kind).toBe("started");
  if (restarted.kind === "started") expect(restarted.epochToken).toBe(2);
  expect((await canary(page)).kind).toBe("succeeded");
  await page.evaluate(() => window.engineWorkerProbe.dispose());
  expect(await page.evaluate(() => window.engineWorkerEvents)).toEqual(["released:1", "released:2"]);
});

test("Worker Ready includes the managed reporter and its generated lifecycle exports", async ({ page, context }) => {
  await context.addCookies([{
    name: "worker-runtime-gate",
    value: "observe-epoch-reporter",
    url: "http://127.0.0.1:4186",
  }]);
  const workerReady = page.waitForEvent("worker");
  expect((await start(page)).kind).toBe("started");
  expect((await canary(page)).kind).toBe("succeeded");
  const worker = await workerReady;
  const receipt = await worker.evaluate(async () => {
    const host = globalThis.engineWorkerEpochReporterGate;
    let duplicateRejected = false;
    try {
      host.registerEpochWorkReporter("unused", () => undefined, () => undefined);
    } catch {
      duplicateRejected = true;
    }
    await host.drainEpochWorkReporter();
    host.unregisterEpochWorkReporter();
    let reuseRejected = false;
    try {
      host.registerEpochWorkReporter("unused", () => undefined, () => undefined);
    } catch {
      reuseRejected = true;
    }
    return { duplicateRejected, reuseRejected };
  });
  expect(receipt).toEqual({ duplicateRejected: true, reuseRejected: true });
  await page.evaluate(() => window.engineWorkerProbe.dispose());
  expect(await page.evaluate(() => window.engineWorkerEvents)).toEqual(["released:1"]);
});

test("a generated-facade bootstrap rejection fails held work and releases the partial realm", async ({ page, context }) => {
  await context.addCookies([{
    name: "worker-runtime-gate",
    value: "reject-bootstrap",
    url: "http://127.0.0.1:4186",
  }]);
  expect((await start(page)).kind).toBe("started");
  expect(await canary(page)).toEqual({ kind: "failed", error: "Worker startup failed." });
  expect(await page.evaluate(() => window.engineWorkerEvents)).toEqual(["failure:startup", "released:1"]);
  await page.evaluate(() => window.engineWorkerProbe.dispose());
});

test("stalled Wasm initialization leaves page input available and exhausts startup", async ({ page, context }) => {
  let blocked = 0;
  await context.route("**/_framework/*.wasm", () => { blocked++; });
  expect((await start(page, 5_000)).kind).toBe("started");
  await expect.poll(() => blocked).toBeGreaterThan(0);
  await page.evaluate(() => {
    document.querySelector("#input")!.addEventListener("click", () => {
      document.querySelector("#count")!.textContent = "1";
    });
  });
  await page.getByRole("button", { name: "Input" }).click();
  await expect(page.locator("#count")).toHaveText("1");
  await expect.poll(() => page.evaluate(
    () => window.engineWorkerProbe.host.snapshot().phase,
  ), { timeout: 15_000 }).toBe("closed");
  expect(await page.evaluate(() => window.engineWorkerEvents)).toEqual(["failure:startup", "released:1"]);
  await page.evaluate(() => window.engineWorkerProbe.dispose());
});
