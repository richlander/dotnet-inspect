import { expect, test } from "@playwright/test";
import {
  publishedRuntimeBenchmarkParameter,
} from "../src/published-runtime-benchmark-bridge.ts";

const site = process.env.INSPECT_WEB_PUBLISHED_BENCHMARK_URL;

test.describe("published runtime benchmark bridge", () => {
  test.skip(!site, "Set INSPECT_WEB_PUBLISHED_BENCHMARK_URL.");
  test.setTimeout(180_000);

  test("uses the page-owned production Worker", async ({ page }) => {
    const url = new URL(site!);
    url.searchParams.set(publishedRuntimeBenchmarkParameter, "1");
    await page.goto(url.href, { waitUntil: "commit" });
    await page.waitForFunction(
      () => window.__inspectWebRuntimeBenchmark !== undefined,
      null,
      { timeout: 180_000 },
    );

    const identity = await page.evaluate(
      () => window.__inspectWebRuntimeBenchmark!.host.buildIdentity(),
    );
    expect(identity.version).not.toBe("");
    expect(identity.version).not.toBe("");
    expect(page.workers()).toHaveLength(1);
  });
});
