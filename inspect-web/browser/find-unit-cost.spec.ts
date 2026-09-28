import { appendFileSync, readFileSync, writeFileSync } from "node:fs";
import process from "node:process";
import { test, type Page, type Request } from "@playwright/test";
import {
  publishedRuntimeBenchmarkParameter,
} from "../src/published-runtime-benchmark-bridge.ts";

// Per-package unit cost of the Browser work a Find would need: acquisition plus
// Type and member surface projection (`queryPackage`), and the platform
// (`loadRuntimePack`). Each unit runs in a fresh browser context, so the first
// call is cold (no HTTP cache, fresh engine) and the second is warm.
//
// FIND_UNIT_SAMPLE: TSV with group, package, version columns and a header.
// FIND_UNIT_OUTPUT: TSV written with one row per call.

const site = process.env.INSPECT_WEB_PUBLISHED_BENCHMARK_URL;
const samplePath = process.env.FIND_UNIT_SAMPLE;
const outputPath = process.env.FIND_UNIT_OUTPUT;

interface Unit {
  readonly group: string;
  readonly package: string;
  readonly version: string;
}

interface CallResult {
  readonly milliseconds: number;
  readonly types: number;
  readonly members: number;
  readonly assemblies: number;
  readonly error: string;
}

function readSample(path: string): Unit[] {
  return readFileSync(path, "utf8")
    .split("\n")
    .slice(1)
    .filter(line => line.trim().length > 0)
    .map(line => {
      const [group = "", packageId = "", version = ""] = line.split("\t");
      return { group, package: packageId, version };
    });
}

function trackTransfer(page: Page): () => Promise<number> {
  const requests: Request[] = [];
  const onFinished = (request: Request) => {
    if (!new URL(request.url()).pathname.includes("/_framework/")) {
      requests.push(request);
    }
  };
  page.on("requestfinished", onFinished);
  return async () => {
    page.off("requestfinished", onFinished);
    const sizes = await Promise.all(requests.map(request => request.sizes()));
    return sizes.reduce(
      (total, size) => total + size.responseHeadersSize + size.responseBodySize,
      0,
    );
  };
}

async function openSite(page: Page): Promise<number> {
  const url = new URL(site!);
  url.searchParams.set(publishedRuntimeBenchmarkParameter, "1");
  const started = Date.now();
  await page.goto(url.href, { waitUntil: "commit" });
  await page.waitForFunction(
    () => window.__inspectWebRuntimeBenchmark !== undefined,
    null,
    { timeout: 180_000 },
  );
  await page.evaluate(() => window.__inspectWebRuntimeBenchmark!.host.buildIdentity());
  return Date.now() - started;
}

async function queryPackage(page: Page, unit: Unit): Promise<CallResult> {
  return page.evaluate(async ({ packageId, version }) => {
    const started = performance.now();
    try {
      const result = await window.__inspectWebRuntimeBenchmark!.package
        .queryPackage(packageId, version, "");
      const surface = result.surface;
      if (surface === null) {
        return {
          milliseconds: performance.now() - started,
          types: 0, members: 0, assemblies: 0,
          error: result.versionSettlement.content.failure?.reason ?? "no surface",
        };
      }
      return {
        milliseconds: performance.now() - started,
        types: surface.types.length,
        members: surface.totalMembers,
        assemblies: surface.assemblies.length,
        error: surface.inspectionError ?? "",
      };
    } catch (error: unknown) {
      return {
        milliseconds: performance.now() - started,
        types: 0, members: 0, assemblies: 0,
        error: error instanceof Error ? error.message : String(error),
      };
    }
  }, { packageId: unit.package, version: unit.version });
}

async function loadRuntimePack(page: Page): Promise<CallResult> {
  return page.evaluate(async () => {
    const started = performance.now();
    try {
      const json = await window.__inspectWebRuntimeBenchmark!.package
        .loadRuntimePack("net10.0", "");
      const surface: unknown = JSON.parse(json);
      const field = (name: string): unknown =>
        typeof surface === "object" && surface !== null
          ? Reflect.get(surface, name)
          : undefined;
      const count = (value: unknown): number =>
        Array.isArray(value) ? value.length : 0;
      const members = field("totalMembers");
      return {
        milliseconds: performance.now() - started,
        types: count(field("types")),
        members: typeof members === "number" ? members : 0,
        assemblies: count(field("assemblies")),
        error: "",
      };
    } catch (error: unknown) {
      return {
        milliseconds: performance.now() - started,
        types: 0, members: 0, assemblies: 0,
        error: error instanceof Error ? error.message : String(error),
      };
    }
  });
}

function write(unit: Unit, phase: string, startupMs: number, bytes: number, result: CallResult) {
  appendFileSync(
    outputPath!,
    [
      unit.group, unit.package, unit.version, phase,
      startupMs, result.milliseconds.toFixed(1), bytes,
      result.assemblies, result.types, result.members,
      result.error.replace(/[\t\n]/g, " "),
    ].join("\t") + "\n",
  );
}

test.describe("find unit cost", () => {
  test.skip(
    !site || !samplePath || !outputPath,
    "Set INSPECT_WEB_PUBLISHED_BENCHMARK_URL, FIND_UNIT_SAMPLE, and FIND_UNIT_OUTPUT.",
  );
  test.setTimeout(3_600_000);

  test("measures platform and package units cold and warm", async ({ browser }) => {
    writeFileSync(
      outputPath!,
      "group\tpackage\tversion\tphase\tstartup_ms\tms\ttransfer_bytes\tassemblies\ttypes\tmembers\terror\n",
    );
    const units: Unit[] = [
      { group: "platform", package: "Microsoft.NETCore.App", version: "net10.0" },
      ...readSample(samplePath!),
    ];
    for (const unit of units) {
      const context = await browser.newContext({ serviceWorkers: "block" });
      const page = await context.newPage();
      try {
        const startupMs = await openSite(page);
        const call = unit.group === "platform"
          ? () => loadRuntimePack(page)
          : () => queryPackage(page, unit);
        const coldTransfer = trackTransfer(page);
        const cold = await call();
        write(unit, "cold", startupMs, await coldTransfer(), cold);
        const warmTransfer = trackTransfer(page);
        const warm = await call();
        write(unit, "warm", startupMs, await warmTransfer(), warm);
      } finally {
        await context.close();
      }
    }
  });
});
