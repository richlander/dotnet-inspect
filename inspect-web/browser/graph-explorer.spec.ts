import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/browser/graph-explorer.html");
  await expect(page.locator("#diagram svg")).toBeVisible();
});

test("Explore relocates the live graph without remounting or losing zoom", async ({ page }) => {
  await page.getByRole("button", { name: "Zoom in", exact: true }).click();
  const transform = await page.locator("#diagram svg").getAttribute("style");
  await page.evaluate(() => window.graphExploreProbe.rememberSvg());
  const before = await page.evaluate(() => ({
    ...window.graphExploreProbe.counts(),
    history: history.length,
    url: location.href,
  }));
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Call graph" });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator(".graph-explorer-kind")).toHaveText("Call graph");
  await expect(dialog.locator("#graph-explorer-title")).toHaveText("Process(int)");
  await expect(dialog.locator(".graph-explorer-context"))
    .toHaveText("Example.Package@1.0.0 · Example.Long.Namespace.Worker");
  await expect(dialog.locator(".graph-explorer-summary")).toHaveText("0 callers · 2 callees");
  await expect(dialog.locator(".call-graph-section > .section-title")).toBeHidden();
  const legend = dialog.locator(".graph-legend");
  await expect(legend).toContainText("target member");
  await expect(legend).toContainText("same declaring type");
  await expect(legend).toContainText("different type, same assembly");
  await expect(legend).toContainText("different assembly");
  await expect(legend).toContainText("solid border: no platform lookup");
  await expect(legend).toContainText("dashed border: platform lookup on click");
  await expect(legend.locator(".legend-swatch")).toHaveCount(6);
  await expect(page.locator("#graph-explorer-title")).toBeFocused();
  expect(await page.evaluate(() => window.graphExploreProbe.sameSvg())).toBe(true);
  expect(await page.locator("#diagram svg").getAttribute("style")).toBe(transform);
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeFocused();
  expect(await page.evaluate(() => window.graphExploreProbe.sameSvg())).toBe(true);
  expect(await page.locator("#diagram svg").getAttribute("style")).toBe(transform);
  expect(await page.evaluate(() => ({
    ...window.graphExploreProbe.counts(),
    history: history.length,
    url: location.href,
  }))).toEqual(before);
});

test("pristine framing follows the viewport while a user-adjusted view stays fixed", async ({ page }) => {
  const svg = page.locator("#diagram svg");
  const inlineTransform = await svg.getAttribute("style");
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await expect.poll(() => svg.getAttribute("style")).not.toBe(inlineTransform);

  const framing = await page.evaluate(() => {
    const viewport = document.querySelector(".graph-explorer .graph-viewport")!;
    const renderedSvg = viewport.querySelector("svg")!;
    const viewportRect = viewport.getBoundingClientRect();
    const svgRect = renderedSvg.getBoundingClientRect();
    return {
      horizontalGap: Math.abs(
        svgRect.left - viewportRect.left
        - (viewportRect.right - svgRect.right)),
      verticalGap: Math.abs(
        svgRect.top - viewportRect.top
        - (viewportRect.bottom - svgRect.bottom)),
      scale: Number(
        /scale\(([^)]+)\)/
          .exec(renderedSvg.getAttribute("style") || "")?.[1]),
    };
  });
  expect(framing.horizontalGap).toBeLessThan(2);
  expect(framing.verticalGap).toBeLessThan(2);
  expect(framing.scale).toBe(1.5);

  await page.getByRole("button", { name: "Zoom in", exact: true }).click();
  const adjusted = await svg.getAttribute("style");
  await page.setViewportSize({ width: 1200, height: 800 });
  await page.evaluate(() => new Promise(resolve =>
    requestAnimationFrame(() => requestAnimationFrame(resolve))));
  expect(await svg.getAttribute("style")).toBe(adjusted);

  await page.getByRole("button", { name: "Fit", exact: true }).click();
  const fitted = await svg.getAttribute("style");
  await page.setViewportSize({ width: 1280, height: 860 });
  await page.evaluate(() => new Promise(resolve =>
    requestAnimationFrame(() => requestAnimationFrame(resolve))));
  expect(await svg.getAttribute("style")).not.toBe(fitted);
});

for (const interaction of ["wheel", "keyboard", "pointer"] as const) {
  test(`${interaction} adjustment survives opening and closing Explore`, async ({ page }) => {
    const viewport = page.locator(".graph-viewport");
    const svg = page.locator("#diagram svg");
    const box = await viewport.boundingBox();
    if (interaction === "wheel") {
      await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2);
      await page.mouse.wheel(0, -180);
    } else if (interaction === "keyboard") {
      await viewport.focus();
      await page.keyboard.press("+");
      await page.keyboard.press("ArrowRight");
    } else {
      await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2);
      await page.mouse.down();
      await page.mouse.move(
        box!.x + box!.width / 2 + 70,
        box!.y + box!.height / 2 + 35,
        { steps: 5 });
      await page.mouse.up();
    }
    const adjusted = await svg.getAttribute("style");
    await page.getByRole("button", { name: "Explore", exact: true }).click();
    await page.evaluate(() => new Promise(resolve =>
      requestAnimationFrame(() => requestAnimationFrame(resolve))));
    expect(await svg.getAttribute("style")).toBe(adjusted);
    await page.getByRole("button", { name: "Close", exact: true }).click();
    await page.evaluate(() => new Promise(resolve =>
      requestAnimationFrame(() => requestAnimationFrame(resolve))));
    expect(await svg.getAttribute("style")).toBe(adjusted);
  });
}

test("modal contains keyboard focus and makes the background unavailable", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.keyboard.press("Shift+Tab");
  await expect(page.getByText("Mermaid source", { exact: true })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "Close", exact: true })).toBeFocused();
  await page.evaluate(() => document.getElementById("background")!.focus());
  await expect(page.getByRole("button", { name: "Close", exact: true })).toBeFocused();
  await page.locator(".graph-viewport").focus();
  const before = await page.locator("#diagram svg").getAttribute("style");
  await page.keyboard.press("ArrowRight");
  expect(await page.locator("#diagram svg").getAttribute("style")).not.toBe(before);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeFocused();
});

test("drill and back replace the result without leaving Explore", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.getByRole("button", { name: "Drill into platform" }).focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("dialog")).toContainText("Platform depth 1");
  await expect(page.locator("#diagram svg")).toBeVisible();
  await page.getByRole("button", { name: "Back", exact: true }).click();
  await expect(page.getByRole("dialog")).not.toContainText("Platform depth");
  await expect(page.locator("#diagram svg")).toBeVisible();
  await page.getByRole("button", { name: "Open member", exact: true }).focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Member member-two");
  await expect(page.getByRole("heading", { level: 1 })).toBeFocused();
});

test("pending results, errors, and no-body results remain visible in the viewer", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.graphExploreProbe.update("pending"));
  await expect(page.getByRole("dialog")).toContainText("Scanning callers");
  await expect(page.locator("#diagram svg")).toBeVisible();
  await page.evaluate(() => window.graphExploreProbe.update("ready"));
  await expect(page.getByRole("dialog")).not.toContainText("Scanning callers");
  await page.evaluate(() => window.graphExploreProbe.update("failure"));
  await expect(page.getByRole("dialog")).toContainText("Call graph query failed");
  await page.evaluate(() => window.graphExploreProbe.update("no-body"));
  await expect(page.getByRole("dialog")).toContainText("No IL body");
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByRole("heading", { name: "No call graph" })).toBeFocused();
});

test("replacement modal and history dismissal never reopen Explore", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.graphExploreProbe.replaceModal());
  await expect(page.getByRole("dialog", { name: "Settings" })).toBeVisible();
  await expect(page.getByRole("dialog", { name: "Call graph" })).toHaveCount(0);
  await page.getByRole("button", { name: "Done", exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.dispatchEvent(new PopStateEvent("popstate")));
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Member history-member");
});

for (const size of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`uses the available graph area at ${size.width}px without page overflow`, async ({ page }) => {
    await page.setViewportSize(size);
    await page.getByRole("button", { name: "Explore", exact: true }).click();
    const viewport = await page.locator(".graph-viewport").boundingBox();
    const scope = await page.locator(".graph-scope").boundingBox();
    const legend = await page.locator(".graph-legend").boundingBox();
    expect(viewport!.width).toBeGreaterThan(size.width - 30);
    expect(viewport!.height).toBeGreaterThan(size.height * 0.6);
    expect(viewport!.y + viewport!.height).toBeLessThanOrEqual(scope!.y);
    expect(scope!.y + scope!.height).toBeLessThanOrEqual(legend!.y);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(size.width);
    await page.getByRole("button", { name: "Fit", exact: true }).click();
    await page.getByText("Mermaid source", { exact: true }).click();
    await expect(page.getByRole("dialog").locator("pre")).toBeVisible();
    await expect(page.getByRole("button", { name: "Close", exact: true })).toBeInViewport();
    await expect(page.getByRole("button", { name: "Fit", exact: true })).toBeInViewport();
  });
}

test("long subjects and context wrap completely without displacing Close", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 900 });
  await page.goto("/browser/graph-explorer.html?header=long");
  await expect(page.locator("#diagram svg")).toBeVisible();
  await page.getByRole("button", { name: "Explore", exact: true }).click();

  const subject = page.locator("#graph-explorer-title");
  const context = page.locator(".graph-explorer-context");
  await expect(subject).toHaveText(
    "System.Threading.Tasks.ValueTask<System.Collections.Immutable.ImmutableArray<Example.Result>> ProcessAsync<TRequest, TResponse>(TRequest request, System.Threading.CancellationToken cancellationToken)");
  await expect(context).toHaveText(
    "Example.Package.Experimental.Extensions@12.0.0-preview.7.26381.103 · Example.Long.Namespace.Containing.Multiple.Nested.Types.Worker<TRequest, TResponse>");
  await expect(subject).toBeInViewport();
  await expect(context).toBeInViewport();
  await expect(page.getByRole("button", { name: "Close", exact: true })).toBeInViewport();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(360);
});
