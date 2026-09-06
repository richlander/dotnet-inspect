import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { expect, test } from "@playwright/test";

declare global {
  interface Window {
    cspViolations: string[];
  }
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    window.cspViolations = [];
    document.addEventListener("securitypolicyviolation", event => {
      window.cspViolations.push(`${event.effectiveDirective}:${event.blockedURI}`);
    });
  });
});

test("published page boots with the exact CSP and import-map hash", async ({ page }) => {
  const errors: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  const response = await page.goto("/index.html");
  const map = await page.locator('script[type="importmap"]').textContent();
  if (map === null) throw new Error("Published import map is missing.");
  const hash = createHash("sha256").update(map).digest("base64");
  expect(response?.headers()["content-security-policy"]).toBe(
    `default-src 'self'; script-src 'self' 'wasm-unsafe-eval' 'sha256-${hash}'; `
    + "style-src 'self' 'unsafe-inline'; img-src 'self' https: data:; "
    + "connect-src 'self' https:; worker-src 'self'; object-src 'none'; "
    + "frame-src 'none'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'",
  );
  await expect(page.locator(".home-search")).toHaveAttribute("aria-busy", "false", { timeout: 60_000 });
  await expect(page.locator("#spotlight-input")).toBeEditable();
  expect(errors).toEqual([]);
  expect(await page.evaluate(() => window.cspViolations)).toEqual([]);
});

test("published CSP blocks unapproved inline and external scripts", async ({ page }) => {
  await page.goto("/worker-runtime-gate.html");
  await page.evaluate(() => {
    const inline = document.createElement("script");
    inline.textContent = "document.documentElement.dataset.cspCanary = 'ran';";
    const external = document.createElement("script");
    external.src = "https://example.invalid/csp-canary.js";
    document.head.append(inline, external);
  });
  await expect.poll(() => page.evaluate(() => window.cspViolations)).toEqual(
    expect.arrayContaining([
      "script-src-elem:inline",
      expect.stringMatching(/^script-src-elem:https:\/\/example\.invalid/),
    ]),
  );
  expect(await page.locator("html").getAttribute("data-csp-canary")).toBeNull();
});

test("published Mermaid renders styled diagrams under CSP", async ({ page }) => {
  const site = resolve(
    process.env.INSPECT_WEB_WORKER_SITE ?? "../../artifacts/inspect-web-publish/wwwroot",
  );
  const manifest: unknown = JSON.parse(readFileSync(resolve(site, "manifest.json"), "utf8"));
  const key = "node_modules/mermaid/dist/mermaid.core.mjs";
  if (typeof manifest !== "object" || manifest === null || !(key in manifest)) {
    throw new Error("Published Mermaid entry is missing.");
  }
  const entry = manifest[key];
  if (typeof entry !== "object" || entry === null
    || !("file" in entry) || typeof entry.file !== "string") {
    throw new Error("Published Mermaid entry has no asset.");
  }
  await page.goto("/index.html");
  await expect(page.locator(".home-search")).toHaveAttribute("aria-busy", "false", { timeout: 60_000 });
  await page.evaluate(async url => {
    type Mermaid = {
      initialize: (config: { startOnLoad: boolean; securityLevel: "strict" }) => void;
      render: (id: string, definition: string) => Promise<{ svg: string }>;
    };
    function isMermaid(value: unknown): value is Mermaid {
      return typeof value === "object" && value !== null
        && "initialize" in value && typeof value.initialize === "function"
        && "render" in value && typeof value.render === "function";
    }
    const imported: unknown = await import(url);
    if (typeof imported !== "object" || imported === null) {
      throw new Error("Published Mermaid module is missing.");
    }
    const mermaid = Object.values(imported).find(isMermaid);
    if (!mermaid) throw new Error("Published Mermaid export is missing.");
    mermaid.initialize({ startOnLoad: false, securityLevel: "strict" });
    const { svg } = await mermaid.render("csp-diagram", "graph TD; A[Package] --> B[Assembly]");
    const container = document.createElement("div");
    container.id = "diagram";
    container.innerHTML = svg;
    document.body.append(container);
  }, `/${entry.file}`);
  await expect(page.locator("#diagram svg")).toBeVisible();
  await expect(page.locator("#diagram .node")).toHaveCount(2);
  expect(await page.evaluate(() => window.cspViolations)).toEqual([]);
});
