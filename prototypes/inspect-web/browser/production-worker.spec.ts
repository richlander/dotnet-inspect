import { readFileSync } from "node:fs";
import { expect, test, type Page, type Worker } from "@playwright/test";
import { healthyNupkg, galleryDownloadPath } from "./package-adoption-nupkg.ts";
import type {
  RawMainToWorkerEnvelope,
  RawWorkerToMainEnvelope,
} from "../src/worker-runtime-protocol.ts";
import { decodeWorkerToMainEnvelope } from "../src/worker-runtime-protocol.ts";
import { engineWorkerPackageQueryDurableEvent } from "../src/engine-worker-package-query.ts";

interface WorkerQueryActivity {
  ticks: number[];
  messages: { at: number; message: unknown }[];
  stop(): void;
}

interface PageQueryActivity {
  inputs: { at: number; value: string }[];
  frames: { at: number; inputIndex: number; value: string }[];
  stop(): void;
}

declare global {
  interface Window {
    productionWorkerEvidence: {
      pageRuntimeStarts: number;
      sent: RawMainToWorkerEnvelope[];
      received: RawWorkerToMainEnvelope[];
    };
    productionQueryTiming: {
      firstRowMilliseconds: number | null;
      firstWindowMilliseconds: number | null;
      completionMilliseconds: number | null;
      renderCount: number;
      longestTimerDelayMilliseconds: number;
    };
    productionAssemblyActivity: PageQueryActivity;
  }
}

async function observeProduction(page: Page) {
  await page.addInitScript(() => {
    const evidence = window.productionWorkerEvidence = {
      pageRuntimeStarts: 0,
      sent: [] as RawMainToWorkerEnvelope[],
      received: [] as RawWorkerToMainEnvelope[],
    };
    for (const name of ["instantiate", "instantiateStreaming"] as const) {
      const original = WebAssembly[name];
      Object.defineProperty(WebAssembly, name, {
        value(...args: unknown[]) {
          evidence.pageRuntimeStarts++;
          const result: unknown = Reflect.apply(original, WebAssembly, args);
          return result;
        },
      });
    }
    const NativeWorker = window.Worker;
    window.Worker = class extends NativeWorker {
      constructor(url: string | URL, options?: WorkerOptions) {
        super(url, options);
        this.addEventListener("message", (event: MessageEvent<RawWorkerToMainEnvelope>) => {
          evidence.received.push(event.data);
        });
        const post = this.postMessage.bind(this);
        Object.defineProperty(this, "postMessage", {
          value(message: RawMainToWorkerEnvelope, transfer: Transferable[] | StructuredSerializeOptions = []) {
            evidence.sent.push(message);
            if (Array.isArray(transfer)) post(message, transfer);
            else post(message, transfer);
          },
        });
      }
    };
  });
}

test("production Worker composition: bounded query, demand/continuation, input/render, shared state and Explorer recovery", async ({ page, context }) => {
  const workers: Worker[] = [];
  page.on("worker", worker => workers.push(worker));
  await observeProduction(page);
  const assemblyPath = process.env.INSPECT_WEB_WORKER_SOURCE_DLL;
  if (!assemblyPath) throw new Error("INSPECT_WEB_WORKER_SOURCE_DLL is required.");
  const archive = healthyNupkg(readFileSync(assemblyPath), "TsJsExport.Contracts.dll");
  const cors = { "Access-Control-Allow-Origin": "*" };
  await context.route("https://azuresearch-usnc.nuget.org/**", async route => {
    const url = new URL(route.request().url());
    const prefix = url.searchParams.get("q")?.includes("Other") ? "Other" : "System";
    const data = Array.from({ length: 100 }, (_, index) => ({
      id: `${prefix}.Package${String(index).padStart(3, "0")}`,
      version: "1.0.0", description: "Production Worker gate.",
      owners: ["Fixture"], totalDownloads: index, verified: false,
    }));
    await route.fulfill({
      status: 200, contentType: "application/json", headers: cors,
      body: JSON.stringify({ totalHits: 100, data }),
    });
  });
  await context.route("https://globalcdn.nuget.org/**", async route => {
    const url = new URL(route.request().url());
    if (url.pathname === galleryDownloadPath("Other.Package000", "1.0.0")) {
      await route.fulfill({ status: 200, contentType: "application/zip", headers: cors, body: archive });
    } else {
      await route.fulfill({
        status: 200, contentType: "application/json", headers: cors,
        body: JSON.stringify({ versions: ["1.0.0"], items: [] }),
      });
    }
  });

  await page.goto("/query");
  const input = page.locator("#package-query-prefix");
  const footer = page.locator(".query-footer");
  await expect(input).toBeEnabled({ timeout: 120_000 });
  await input.fill("System*");
  await page.locator("#package-query-run").click();
  await expect(footer).toContainText("20 packages");
  await expect(page.locator(".query-row")).toHaveCount(20);
  expect(workers).toHaveLength(1);
  const firstOperation = await page.evaluate(() => {
    const start = window.productionWorkerEvidence.sent.find(
      message => message.kind === "start" && message.operationKind === "package-query");
    if (start?.kind !== "start") throw new Error("Production query did not start through the Worker.");
    return start.operation;
  });

  const beforeCredit = await page.evaluate(() => window.productionWorkerEvidence);
  expect(beforeCredit.pageRuntimeStarts).toBe(0);
  expect(beforeCredit.sent.filter(message => message.kind === "control")).toHaveLength(0);
  expect(beforeCredit.received.some(message =>
    message.kind === "settled" && message.operation.operationId === firstOperation.operationId)).toBe(false);

  // The initial window is demand-paused, not evidence of active managed CPU work.
  // Observe real continuations on both sides of page input/render instead. Credit
  // acknowledgments and later matches prove continuation, not runnable-at-frame state.
  const continuations: number[] = [];
  for (let target = 30; target <= 100; target += 10) {
    await page.locator(".query-main").evaluate(element => { element.scrollTop = element.scrollHeight; });
    await expect.poll(async () => Number((await footer.textContent())?.trim().match(/^(\d+) packages/)?.[1] ?? "0"))
      .toBeGreaterThanOrEqual(target);
    expect(await page.locator(".query-row").count()).toBeLessThanOrEqual(30);
    continuations.push(target);
    if (target === 30) {
      await input.press("End");
      await input.press("ArrowLeft");
      expect(await input.evaluate(element =>
        element instanceof HTMLInputElement ? element.selectionStart : null)).toBe(6);
      await expect(input).toHaveValue("System*");
      await page.evaluate(() => new Promise<void>(resolve =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
      const betweenContinuations = await page.evaluate(() => window.productionWorkerEvidence);
      expect(betweenContinuations.received.some(message =>
        message.kind === "settled" && message.operation.operationId === firstOperation.operationId)).toBe(false);
    }
  }
  await expect(footer).toContainText("100 packages · bounded: first 100 matches");
  const completed = await page.evaluate(() => window.productionWorkerEvidence);
  const controls = completed.sent.filter(message => message.kind === "control");
  expect(controls.length).toBeGreaterThanOrEqual(8);
  for (const control of controls) {
    expect(control.operation).toEqual(firstOperation);
    expect(control.payload).toBe(10);
    expect(completed.received).toContainEqual({
      protocolVersion: control.protocolVersion, epochToken: control.epochToken,
      kind: "control-acknowledged", operation: firstOperation,
      controlSequence: control.controlSequence, status: "acknowledged", payload: 10,
    });
  }
  const terminalIndex = completed.received.findIndex(message =>
    message.kind === "settled" && message.operation.operationId === firstOperation.operationId);
  expect(terminalIndex).toBeGreaterThan(0);
  expect(completed.received.slice(terminalIndex + 1).some(message =>
    message.kind === "events" && message.operation.operationId === firstOperation.operationId)).toBe(false);
  await test.info().attach("production-query-composition-evidence", {
    body: JSON.stringify({
      initialDemandWindow: 20, continuations, inputAndTwoFramesBetweenWindows: [30, 40],
      scope: "Demand/credit composition only; the separate production assembly query gate checks active-work overlap.",
    }, null, 2),
    contentType: "application/json",
  });

  await page.locator(".query-main").evaluate(element => { element.scrollTop = 0; });
  await page.locator("#package-query-run").click();
  await expect(footer).toContainText("streaming");
  await expect(page.locator(".query-row h2").first()).toHaveText("System.Package000");
  const superseded = await page.evaluate(() => {
    const starts = window.productionWorkerEvidence.sent.filter(message =>
      message.kind === "start" && message.operationKind === "package-query");
    const last = starts.at(-1);
    return last?.kind === "start" ? last.operation : null;
  });
  await input.fill("Other*");
  await page.locator("#package-query-run").click();
  await expect(page.locator(".query-row h2").first()).toHaveText("Other.Package000");
  await expect(page.locator(".query-row h2")).not.toContainText(["System.Package"]);
  const supersession = await page.evaluate(() => window.productionWorkerEvidence.sent);
  expect(supersession.some(message => message.kind === "cancel"
    && message.operation.operationId === superseded?.operationId
    && message.reason === "superseded")).toBe(true);
  await page.locator("[data-query-cancel]").first().click();
  await expect(footer).toContainText("cancelled");
  await page.locator("[data-query-row-open]").first().click();
  await expect(page).not.toHaveURL(/\/query(?:[?#].*)?$/);
  await expect(page.locator("body")).toContainText("Other.Package000");
  const neighbor = await page.evaluate(() => window.productionWorkerEvidence);
  expect(neighbor.sent.some(message => message.kind === "start" && message.operationKind === "package-surface")).toBe(true);
  expect(neighbor.sent.every(message => message.epochToken === firstOperationEpoch(neighbor.sent))).toBe(true);
  expect(neighbor.pageRuntimeStarts).toBe(0);
  expect(workers).toHaveLength(1);
  await expect.poll(() => page.evaluate(() => {
    const evidence = window.productionWorkerEvidence;
    const statsCalls = evidence.sent.filter(message =>
      message.kind === "start" && message.operationKind === "package-cache-stats");
    for (const call of statsCalls) {
      if (call.kind !== "start") continue;
      const response = evidence.received.find(message => message.kind === "settled"
        && message.operation.operationId === call.operation.operationId);
      if (response?.kind !== "settled" || response.settlement.kind !== "succeeded"
        || typeof response.settlement.value !== "string") continue;
      const stats: unknown = JSON.parse(response.settlement.value);
      if (typeof stats === "object" && stats !== null && "packages" in stats
        && typeof stats.packages === "number" && stats.packages > 0) return stats.packages;
    }
    return 0;
  })).toBeGreaterThan(0);

  await page.locator("[data-type]").filter({
    has: page.getByText("JsExportRootAttribute", { exact: true }),
  }).first().click();
  await page.locator('[data-lens="source"]').click();
  await expect(page.locator(".source-result")).toContainText("JsExportRootAttribute");
  expect(await page.evaluate(() => window.productionWorkerEvidence.sent.some(
    message => message.kind === "start" && message.operationKind === "type-source"))).toBe(true);
  await test.info().attach("production-type-source", {
    body: await page.locator(".source-result").screenshot(), contentType: "image/png",
  });

  await page.getByRole("tab", { name: "Library", exact: true }).click();
  await page.locator('[data-library-lens="metadata"]').click();
  await page.locator("[data-mde-open]").first().click();
  await expect(page.locator(".metadata-explorer")).toBeVisible();
  await expect(page.locator("#mde-exit")).toBeVisible();
  await workers[0]!.evaluate(() => {
    setTimeout(() => { throw new Error("Production gate: Worker loss."); }, 0);
  });
  await expect(page.locator(".load-error-message")).toBeVisible();
  await expect(page.locator(".load-error-message")).toContainText("Reload the page to continue.");
  await expect(page.locator("#retry-load")).toBeVisible();
  await expect(page.locator(".metadata-explorer")).toHaveCount(0);
  expect(workers).toHaveLength(1);
  expect(await page.evaluate(() => window.productionWorkerEvidence.pageRuntimeStarts)).toBe(0);
});

function firstOperationEpoch(messages: readonly RawMainToWorkerEnvelope[]): number | undefined {
  return messages[0]?.epochToken;
}

async function correlateClocks(page: Page, worker: Worker) {
  const samples: { pageBefore: number; workerAt: number; pageAfter: number }[] = [];
  for (let index = 0; index < 3; index++) {
    const pageBefore = await page.evaluate(() => performance.timeOrigin + performance.now());
    const workerAt = await worker.evaluate(() => performance.timeOrigin + performance.now());
    const pageAfter = await page.evaluate(() => performance.timeOrigin + performance.now());
    samples.push({ pageBefore, workerAt, pageAfter });
  }
  return samples;
}

test("production assembly query overlaps trusted page input and frames with Worker-blocking work", async ({ page, context }) => {
  const assemblyPath = process.env.INSPECT_WEB_WORKER_QUERY_DLL;
  if (!assemblyPath) throw new Error("INSPECT_WEB_WORKER_QUERY_DLL is required.");
  const assembly = readFileSync(assemblyPath);
  const archive = healthyNupkg(assembly, "ILInspector.Decompiler.dll");
  const operand = "__inspect_web_worker_absent_literal_6435__";
  const packageIds = Array.from({ length: 5 }, (_, index) => `worker.assembly${index}`);
  const coordinates = packageIds.map(id => `${id}@1.0.0`);
  const paths = packageIds.map(id => galleryDownloadPath(id, "1.0.0"));
  const downloads: string[] = [];
  await context.route("https://globalcdn.nuget.org/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (!paths.includes(path)) {
      await route.abort();
      return;
    }
    downloads.push(path);
    await route.fulfill({
      status: 200, contentType: "application/zip",
      headers: { "Access-Control-Allow-Origin": "*" }, body: archive,
    });
  });
  await observeProduction(page);
  const workers: Worker[] = [];
  page.on("worker", worker => workers.push(worker));
  await page.goto("/query");
  await expect(page.locator("#package-query-prefix")).toBeEnabled({ timeout: 120_000 });
  await page.getByText("Assembly patterns", { exact: true }).click();
  await page.locator("#package-query-assembly-pattern").selectOption("il-string-literal-contains");
  await page.locator("#package-query-assembly-packages").fill(coordinates.join("\n"));
  await page.locator("#package-query-assembly-operand").fill(operand);
  await page.locator("#package-query-assembly-tfm").fill("net11.0");
  expect(workers).toHaveLength(1);
  const worker = workers[0]!;
  const clockSamples = await correlateClocks(page, worker);
  const samplePeriodMilliseconds = 4;

  // Debugger instrumentation only: observe existing messages unchanged and
  // ordinary timer dispatch. Never call a facade or synthesize a query event.
  await worker.evaluate(period => {
    const realm = globalThis as typeof globalThis & { productionAssemblyActivity?: WorkerQueryActivity };
    const ticks: number[] = [];
    const messages: WorkerQueryActivity["messages"] = [];
    const original = globalThis.postMessage;
    globalThis.postMessage = (...args: unknown[]) => {
      messages.push({
        at: performance.timeOrigin + performance.now(),
        message: args[0],
      });
      Reflect.apply(original, globalThis, args);
    };
    const timer = setInterval(() => ticks.push(performance.timeOrigin + performance.now()), period);
    realm.productionAssemblyActivity = {
      ticks, messages,
      stop() {
        clearInterval(timer);
        globalThis.postMessage = original;
        delete realm.productionAssemblyActivity;
      },
    };
  }, samplePeriodMilliseconds);
  try {
    await page.evaluate(() => {
      const inputs: PageQueryActivity["inputs"] = [];
      const frames: PageQueryActivity["frames"] = [];
      const recordInput = (event: Event) => {
        if (event.isTrusted && event.target instanceof HTMLInputElement
          && event.target.id === "package-query-assembly-operand") {
          inputs.push({ at: performance.timeOrigin + performance.now(), value: event.target.value });
        }
      };
      document.addEventListener("input", recordInput);
      let frameId: number;
      function frame() {
        const input = document.querySelector<HTMLInputElement>("#package-query-assembly-operand");
        frames.push({
          at: performance.timeOrigin + performance.now(),
          inputIndex: inputs.length - 1, value: input?.value ?? "",
        });
        frameId = requestAnimationFrame(frame);
      }
      frameId = requestAnimationFrame(frame);
      window.productionAssemblyActivity = {
        inputs, frames,
        stop() {
          cancelAnimationFrame(frameId);
          document.removeEventListener("input", recordInput);
        },
      };
    });
    await page.locator("#package-query-assembly-run").click();
    const operation = await page.evaluate(() => {
      const start = window.productionWorkerEvidence.sent.find(
        message => message.kind === "start" && message.operationKind === "package-query");
      if (start?.kind !== "start") throw new Error("Assembly query did not start through the Worker.");
      return start.operation;
    });
    // Assembly controls are submitted explicitly; editing the ordinary prefix
    // would instead debounce a new query and supersede this workload.
    const input = page.locator("#package-query-assembly-operand");
    while (!await page.evaluate(id => window.productionWorkerEvidence.received.some(
      message => message.kind === "settled" && message.operation.operationId === id), operation.operationId)) {
      await input.press("x");
      await page.evaluate(() => new Promise<void>(resolve =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
      await input.press("Backspace");
    }
    clockSamples.push(...await correlateClocks(page, worker));
    const activity = await worker.evaluate(() => {
      const realm = globalThis as typeof globalThis & { productionAssemblyActivity?: WorkerQueryActivity };
      if (!realm.productionAssemblyActivity) throw new Error("Worker timer instrumentation is missing.");
      const { ticks, messages } = realm.productionAssemblyActivity;
      return { ticks, messages };
    });
    const pageActivity = await page.evaluate(() => {
      const { inputs, frames } = window.productionAssemblyActivity;
      return { inputs, frames };
    });
    const evidence = await page.evaluate(() => window.productionWorkerEvidence);
    const epoch = firstOperationEpoch(evidence.sent);
    if (epoch === undefined) throw new Error("Production Worker epoch is missing.");
    const observedMessages = activity.messages.map(({ at, message }) => {
      const decoded = decodeWorkerToMainEnvelope(message, epoch);
      if (decoded.kind !== "success") throw new Error("Worker observer captured an invalid envelope.");
      return { at, message: decoded.value };
    });
    const messages = observedMessages.filter(({ message }) =>
      "operation" in message && message.operation.operationId === operation.operationId);
    const events = messages.flatMap(({ at, message }) => message.kind === "events"
      ? message.entries.map(entry => {
        const decoded = engineWorkerPackageQueryDurableEvent.decode(entry.payload);
        if (decoded.kind !== "decoded") throw new Error("Worker observer captured an invalid query event.");
        return { at, event: decoded.value };
      })
      : []);
    const progress = events.filter(item => item.event.kind === "Progress");
    const assessments = events.filter(item => item.event.kind === "Assessment");
    const settled = messages.find(({ message }) => message.kind === "settled");

    // Sequential page/Worker/page reads bound the clock offset without assuming
    // equal realm origins or exact debugger round-trip latency. Apply the full
    // uncertainty inward, never a midpoint that could manufacture overlap.
    const minimumOffset = Math.max(...clockSamples.map(sample => sample.pageBefore - sample.workerAt));
    const maximumOffset = Math.min(...clockSamples.map(sample => sample.pageAfter - sample.workerAt));
    const firstProgressAt = progress[0]?.at ?? Infinity;
    const settlementAt = settled?.at ?? -Infinity;
    const gaps = activity.ticks.slice(1).map((end, index) => {
      const start = activity.ticks[index]!;
      const assessment = assessments.find(item => item.at > start && item.at <= end);
      const containedStart = start + samplePeriodMilliseconds + maximumOffset;
      const containedEnd = Math.min(end, assessment?.at ?? end) + minimumOffset;
      const overlaps = pageActivity.inputs.flatMap((event, inputIndex) => {
        const frames = pageActivity.frames.filter(frame => frame.inputIndex === inputIndex
          && frame.value === event.value && frame.at > event.at && frame.at < containedEnd);
        return event.at > containedStart && event.at < containedEnd && frames.length >= 2
          ? [{ input: event, frames }] : [];
      });
      return { start, end, milliseconds: end - start, assessment, containedStart, containedEnd, overlaps };
    }).filter(gap => gap.start >= firstProgressAt && gap.end <= settlementAt);
    const overlappingGap = gaps.find(gap => gap.assessment !== undefined && gap.overlaps.length > 0);
    const report = {
      assemblyPath, assemblyBytes: assembly.length, coordinates, samplePeriodMilliseconds,
      clockSamples, offsetBounds: { minimumOffset, maximumOffset },
      workerTicks: activity.ticks,
      progress, assessments, settled, downloads,
      inputCount: pageActivity.inputs.length, frameCount: pageActivity.frames.length,
      largestQueryGaps: [...gaps].sort((a, b) => b.milliseconds - a.milliseconds).slice(0, 10),
      overlappingGap,
      scope: "Timer non-dispatch during real assembly-query work, not exact managed entry/exit or a latency guarantee.",
    };
    await test.info().attach("production-assembly-query-overlap", {
      body: JSON.stringify(report, null, 2), contentType: "application/json",
    });
    console.log(`Production assembly query overlap: ${JSON.stringify({
      offsetBounds: report.offsetBounds, inputCount: report.inputCount, frameCount: report.frameCount,
      gapMilliseconds: overlappingGap?.milliseconds,
      largestGaps: report.largestQueryGaps.map(gap => gap.milliseconds),
    })}`);
    expect(minimumOffset).toBeLessThanOrEqual(maximumOffset);
    expect(progress.map(item => item.event.progress)).toEqual(
      Array.from({ length: 6 }, (_, completed) => ({ phase: "Assembly", completed, limit: 5 })));
    expect(assessments.map(item => item.event.assessment)).toEqual(packageIds.map(packageId => expect.objectContaining({
      packageId, version: "1.0.0", disposition: "NoMatch", assetPath: "lib/net11.0/ILInspector.Decompiler.dll",
    })));
    expect(settled?.message).toMatchObject({
      kind: "settled",
      settlement: { kind: "succeeded", value: {
        kind: "Completed", completion: {
          kind: "ExplicitCandidatesComplete", prefix: operand, candidates: 5, matches: 0,
          semanticMisses: 5, notApplicable: 0, failures: 0,
        },
      } },
    });
    expect(downloads).toEqual(paths);
    expect(evidence.sent.filter(message => message.kind === "control")).toHaveLength(0);
    expect(evidence.sent.filter(message => message.kind === "start" && message.operationKind === "package-query")).toHaveLength(1);
    expect(observedMessages.filter(({ message }) => message.kind === "accepted")
      .map(({ message }) => message.kind === "accepted" ? message.operation : null)).toEqual([operation]);
    expect(overlappingGap, "Trusted input and two subsequent frames must be inside a query-bounded Worker timer gap.").toBeDefined();
    await expect(page.getByRole("heading", { name: "No selected assembly matches" })).toBeVisible();
    expect(evidence.pageRuntimeStarts).toBe(0);
    expect(workers).toHaveLength(1);
  } finally {
    await Promise.all([
      page.evaluate(() => window.productionAssemblyActivity?.stop()),
      worker.evaluate(() => {
        const realm = globalThis as typeof globalThis & { productionAssemblyActivity?: WorkerQueryActivity };
        realm.productionAssemblyActivity?.stop();
      }),
    ]);
  }
});

test("System prefix timing probe over the production Worker client", async ({ page }) => {
  test.skip(process.env.INSPECT_WEB_QUERY_BENCHMARK !== "1",
    "Environmental network evidence; enable explicitly, not a deterministic CI timing assertion.");
  await page.goto("/query");
  const input = page.locator("#package-query-prefix");
  await expect(input).toBeEnabled({ timeout: 120_000 });
  await input.fill("System.*");
  await page.evaluate(() => {
    const timing = window.productionQueryTiming = {
      firstRowMilliseconds: null as number | null,
      firstWindowMilliseconds: null as number | null,
      completionMilliseconds: null as number | null,
      renderCount: 0,
      longestTimerDelayMilliseconds: 0,
    };
    document.querySelector("#package-query-run")?.addEventListener("click", () => {
      const started = performance.now();
      let lastTimer = started;
      const timer = setInterval(() => {
        const now = performance.now();
        timing.longestTimerDelayMilliseconds = Math.max(timing.longestTimerDelayMilliseconds, now - lastTimer - 16);
        lastTimer = now;
      }, 16);
      function frame() {
        timing.renderCount++;
        if (timing.completionMilliseconds === null) requestAnimationFrame(frame);
      }
      requestAnimationFrame(frame);
      const observer = new MutationObserver(() => {
        const text = document.querySelector(".query-footer")?.textContent?.trim() ?? "";
        const count = Number(text.match(/^(\d+) packages/)?.[1] ?? 0);
        const elapsed = performance.now() - started;
        if (count > 0 && timing.firstRowMilliseconds === null) timing.firstRowMilliseconds = elapsed;
        if (count >= 20 && timing.firstWindowMilliseconds === null) timing.firstWindowMilliseconds = elapsed;
        if (count > 0 && !text.includes("streaming")) {
          timing.completionMilliseconds = elapsed;
          clearInterval(timer);
          observer.disconnect();
        }
      });
      observer.observe(document.body, { childList: true, subtree: true, characterData: true });
    }, { once: true });
  });
  await page.locator("#package-query-run").click();
  const footer = page.locator(".query-footer");
  await expect(footer).toContainText("20 packages", { timeout: 60_000 });
  for (let target = 30; target <= 100; target += 10) {
    await page.locator(".query-main").evaluate(element => { element.scrollTop = element.scrollHeight; });
    await expect.poll(async () => Number((await footer.textContent())?.trim().match(/^(\d+) packages/)?.[1] ?? "0"))
      .toBeGreaterThanOrEqual(target);
  }
  await expect(footer).toContainText("bounded: first 100 matches");
  const timing = await page.evaluate(() => window.productionQueryTiming);
  expect(timing.completionMilliseconds).not.toBeNull();
  console.log(`Production System.* query timing: ${JSON.stringify(timing)}`);
  await test.info().attach("system-prefix-timing", {
    body: JSON.stringify(timing, null, 2), contentType: "application/json",
  });
});
