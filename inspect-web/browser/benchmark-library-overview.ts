// Measures the page-owned production Worker and Cache Storage path, with JSON parity.
// Usage: node browser/benchmark-library-overview.ts <before-url> <after-url> <output-dir> [samples]
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdirSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { firefox } from "@playwright/test";
import type { PublishedRuntimeBenchmarkBridge } from "../src/published-runtime-benchmark-bridge.ts";

const [beforeUrl, afterUrl, outputDirectory, sampleText = "7"] = process.argv.slice(2);
if (!beforeUrl || !afterUrl || !outputDirectory) {
  throw new Error("Usage: benchmark-library-overview.ts <before-url> <after-url> <output-dir> [samples]");
}
const samples = Number(sampleText);
assert.ok(Number.isSafeInteger(samples) && samples > 0);
const output = resolve(outputDirectory);
mkdirSync(output, { recursive: true });
const assets = [
  { id: "Avalonia", version: "12.1.3", framework: "net10.0", library: null },
  { id: "Avalonia", version: "12.1.3", framework: "net10.0", library: "compile:ref/net10.0/Avalonia.Base.dll" },
  { id: "Microsoft.CodeAnalysis.CSharp", version: "5.9.0", framework: "netstandard2.0", library: null },
  { id: "Newtonsoft.Json", version: "13.0.4", framework: "net6.0", library: null },
  { id: "Dapper", version: "2.1.66", framework: "net8.0", library: null },
] as const;
const browser = await firefox.launch({ headless: true });
const rows: object[] = [];
try {
  for (const [assetIndex, asset] of assets.entries()) {
    for (let repeat = -1; repeat < samples; repeat++) {
      const parity = new Map<string, string>();
      for (const label of repeat % 2 === 0 ? ["after", "before"] : ["before", "after"]) {
        const context = await browser.newContext({ serviceWorkers: "block" });
        try {
          const page = await context.newPage();
          const requests: { range: string | undefined; status: number | null; failed: boolean }[] = [];
          page.on("request", request => {
            if (!request.url().endsWith(".nupkg")) return;
            const entry = { range: request.headers()["range"], status: null as number | null, failed: false };
            requests.push(entry);
            request.response().then(response => {
              entry.status = response?.status() ?? null;
              entry.failed = response === null;
              return undefined;
            })
              .catch(() => { entry.failed = true; });
          });
          const url = new URL(label === "before" ? beforeUrl : afterUrl);
          url.searchParams.set("runtime-benchmark", "1");
          const startup = performance.now();
          await page.goto(url.href, { waitUntil: "commit" });
          await page.waitForFunction(() => window.__inspectWebRuntimeBenchmark !== undefined,
            null, { timeout: 180_000 });
          const readyMilliseconds = performance.now() - startup;
          const build = await page.evaluate(() => window.__inspectWebRuntimeBenchmark!.host.buildIdentity());
          for (const cache of ["cold", "warm"]) {
            const requestStart = requests.length;
            const started = performance.now();
            const result = await page.evaluate(async ({ asset: coordinate, shared }) => {
              const client: PublishedRuntimeBenchmarkBridge = window.__inspectWebRuntimeBenchmark!;
              const totalStart = performance.now();
              const summary = await client.package.queryPackageSummary(coordinate.id, coordinate.version, coordinate.framework);
              const summaryMilliseconds = performance.now() - totalStart;
              const library = coordinate.library ?? summary.defaultLibraryId;
              if (!library) throw new Error("The Summary did not select a Library.");
              const apiStart = performance.now();
              // The baseline has four arguments; the candidate declares the known companion.
              // This adapter is used only against the separately compiled baseline facade.
              // oxlint-disable-next-line typescript/no-unsafe-type-assertion
              const legacyApi = client.package.queryLibraryApi as (
                id: string, version: string, framework: string, library: string
              ) => ReturnType<typeof client.package.queryLibraryApi>;
              const api = shared
                ? client.package.queryLibraryApi(coordinate.id, coordinate.version, coordinate.framework, library, true)
                : legacyApi(coordinate.id, coordinate.version, coordinate.framework, library);
              const enablements = client.library.inspectLibrary({
                library: { kind: "Package", package: {
                  packageId: coordinate.id, version: coordinate.version,
                  targetFramework: coordinate.framework, assemblyId: library,
                }, platform: null },
                plan: { enablements: true },
              });
              const [apiResult, enablementsResult] = await Promise.all([api, enablements]);
              const resultJson = JSON.stringify({ summary, api: apiResult, enablements: enablementsResult });
              return {
                resultJson, library, summaryMilliseconds,
                libraryMilliseconds: performance.now() - apiStart,
                totalMilliseconds: performance.now() - totalStart,
                apiAvailable: apiResult.content.isAvailable,
                apiComplete: apiResult.content.isComplete,
                apiProjectionLimited: apiResult.content.truncation?.limit === 6
                  && apiResult.content.failures.every(failure => failure.kind === 7),
                stats: await client.package.packageCacheStats(),
              };
            }, { asset, shared: label === "after" });
            const wallMilliseconds = performance.now() - started;
            const hash = createHash("sha256").update(result.resultJson).digest("hex");
            const reference = parity.get(cache);
            if (reference) assert.equal(hash, reference, `${asset.id} ${cache} result parity`);
            else parity.set(cache, hash);
            if (asset.id !== "Microsoft.CodeAnalysis.CSharp") {
              assert.ok(result.apiAvailable && result.apiComplete, `${asset.id} API must be available and complete`);
            } else {
              assert.ok(result.apiProjectionLimited, "Roslyn must retain its expected projection-limit outcome");
            }
            const prefix = `${assetIndex}-${repeat}-${label}-${cache}`;
            writeFileSync(resolve(output, `${prefix}.json`), result.resultJson);
            const { resultJson: ignored, ...measurement } = result;
            void ignored;
            const row = { asset, repeat, label, cache, build, readyMilliseconds,
              wallMilliseconds, hash, requests: requests.slice(requestStart), ...measurement };
            if (repeat >= 0) rows.push(row);
            console.log(JSON.stringify({ ...row, requests: row.requests.length }));
            writeFileSync(resolve(output, "measurements.json"), JSON.stringify(rows, null, 2));
          }
        } finally {
          await context.close();
        }
      }
    }
  }
} finally {
  await browser.close();
}
