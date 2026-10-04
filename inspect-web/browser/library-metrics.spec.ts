import { expect, test } from "@playwright/test";

test("complexity cells disclose evidence and activate exact type keys", async ({
  page,
}) => {
  await page.goto("/browser/library-metrics.html");
  const first = page.locator('[data-metrics-type-key="Example.A"]');
  const second = page.locator('[data-metrics-type-key="Example.B"]');
  const evidence = page.locator("[data-metrics-treemap-evidence]");
  const activation = page.locator("#metrics-activated-type");

  await first.hover();
  await expect(evidence).toContainText(
    "Example.A · 1 body · 12 instructions · average complexity 3.0",
  );
  await first.click();
  await expect(activation).toHaveText("Example.A");

  await second.focus();
  await expect(evidence).toContainText(
    "Example.B · 2 bodies · 24 instructions · average complexity 3.0",
  );
  await second.press("Enter");
  await expect(activation).toHaveText("Example.B");
});

test("reciprocal relationship evidence remains independently reachable", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1100, height: 700 });
  await page.goto("/browser/library-metrics.html");
  const edges = page.locator("path.metrics-relationship-edge");
  const visibleEdges = page.locator(
    "path.metrics-relationship-edge:not([hidden])",
  );
  const activation = page.locator("#metrics-activated-type");
  const limit = page.locator("[data-metrics-relationship-limit]");
  const limitOutput = page.locator(
    "[data-metrics-relationship-limit-output]",
  );
  await expect(edges).toHaveCount(30);
  await expect(visibleEdges).toHaveCount(15);
  await expect(limitOutput).toHaveText("15 / 30");
  await page.locator("svg.metrics-relationship-crossing")
    .scrollIntoViewIfNeeded();

  const paths = await edges.evaluateAll(elements =>
    Object.fromEntries(elements.map(element => [
      element.querySelector("title")?.textContent ?? "",
      element.getAttribute("d") ?? "",
    ])));
  const forward = "Example.A calls Example.B at 1 retained site";
  const reverse = "Example.B calls Example.A at 1 retained site";
  expect(paths[forward]).toBeTruthy();
  expect(paths[reverse]).toBeTruthy();
  expect(paths[forward]).not.toBe(paths[reverse]);

  for (const title of [forward, reverse]) {
    const reachable = await edges.evaluateAll((elements, expectedTitle) => {
      const edge = elements.find(element =>
        element.querySelector("title")?.textContent === expectedTitle);
      if (!(edge instanceof SVGPathElement)) return false;
      const matrix = edge.getScreenCTM();
      if (matrix === null) return false;
      const length = edge.getTotalLength();
      return [.25, .4, .5, .6, .75].some(fraction => {
        const point = edge.getPointAtLength(length * fraction);
        const screen = new DOMPoint(point.x, point.y).matrixTransform(matrix);
        return document.elementFromPoint(screen.x, screen.y) === edge;
      });
    }, title);
    expect(reachable, `${title} should own at least one pointer position`)
      .toBe(true);
  }

  const detail = page.locator("[data-metrics-relationship-detail]");
  const forwardEdge = edges.filter({ hasText: forward });
  const forwardPoint = await forwardEdge.evaluate(element => {
    if (!(element instanceof SVGPathElement))
      throw new Error("Relationship edge is not an SVG path.");
    const matrix = element.getScreenCTM();
    if (matrix === null) throw new Error("Relationship edge has no screen CTM.");
    const point = element.getPointAtLength(element.getTotalLength() / 2);
    const screen = new DOMPoint(point.x, point.y).matrixTransform(matrix);
    return { x: screen.x, y: screen.y };
  });
  await page.mouse.click(forwardPoint.x, forwardPoint.y);
  await expect(forwardEdge).toHaveAttribute("aria-pressed", "true");
  await expect(detail.locator("[data-metrics-relationship-source]"))
    .toHaveText("Example.A");
  await expect(detail.locator("[data-metrics-relationship-target]"))
    .toHaveText("Example.B");
  await expect(detail.locator("[data-metrics-relationship-depth]"))
    .toHaveText("1 retained call site");
  await expect(detail.locator("[data-metrics-relationship-rank]"))
    .toHaveText("1/30 most connected");

  await detail.locator("[data-metrics-relationship-target]").click();
  await expect(activation).toHaveText("Example.B");

  const reverseEdge = edges.filter({ hasText: reverse });
  await reverseEdge.focus();
  await reverseEdge.press("Enter");
  await expect(reverseEdge).toHaveAttribute("aria-pressed", "true");
  await expect(forwardEdge).toHaveAttribute("aria-pressed", "false");
  await expect(detail.locator("[data-metrics-relationship-source]"))
    .toHaveText("Example.B");
  await expect(detail.locator("[data-metrics-relationship-target]"))
    .toHaveText("Example.A");
  await expect(detail.locator("[data-metrics-relationship-rank]"))
    .toHaveText("6/30 most connected");

  await limit.focus();
  await limit.press("End");
  await expect(visibleEdges).toHaveCount(30);
  await expect(limitOutput).toHaveText("30 / 30");

  const lastEdge = edges.nth(29);
  await lastEdge.focus();
  await lastEdge.press("Enter");
  await expect(detail.locator("[data-metrics-relationship-rank]"))
    .toHaveText("30/30 most connected");

  await page.getByRole("button", {
    name: "Load dependency structure",
  }).click();
  await expect(page.locator(".metrics-dependency-structure")).toBeVisible();
  await expect(visibleEdges).toHaveCount(30);
  await expect(limitOutput).toHaveText("30 / 30");
  await expect(lastEdge).toHaveAttribute("aria-pressed", "true");
  await expect(detail.locator("[data-metrics-relationship-rank]"))
    .toHaveText("30/30 most connected");

  await limit.focus();
  await limit.press("Home");
  await expect(visibleEdges).toHaveCount(8);
  await expect(limitOutput).toHaveText("8 / 30");
  await expect(detail.locator("[data-metrics-relationship-detail-empty]"))
    .toBeVisible();

  const svg = page.locator("svg.metrics-relationship-crossing");
  const viewport = await svg.boundingBox();
  expect(viewport).not.toBeNull();
  const labels = await page.locator(".metrics-relationship-node text")
    .evaluateAll(elements => elements.map(element => {
      const rectangle = element.getBoundingClientRect();
      return {
        left: rectangle.left,
        top: rectangle.top,
        right: rectangle.right,
        bottom: rectangle.bottom,
      };
    }));
  for (const label of labels) {
    expect(label.left).toBeGreaterThanOrEqual(viewport!.x - .5);
    expect(label.top).toBeGreaterThanOrEqual(viewport!.y - .5);
    expect(label.right).toBeLessThanOrEqual(viewport!.x + viewport!.width + .5);
    expect(label.bottom).toBeLessThanOrEqual(
      viewport!.y + viewport!.height + .5,
    );
  }
});

test("dependency edges reveal exact-type explanations", async ({ page }) => {
  await page.goto("/browser/library-metrics.html");
  await expect(page.locator(".metrics-dependency-structure")).toHaveCount(0);
  await page.getByRole("button", {
    name: "Load dependency structure",
  }).click();
  const edge = page.locator('[data-dependency-edge-index="0"]');
  const detail = page.locator('[data-dependency-edge-detail="0"]');
  const activation = page.locator("#metrics-activated-type");

  await edge.focus();
  await edge.press("Enter");
  await expect(detail).toHaveAttribute("open", "");
  await expect(detail).toContainText("Example.A");
  await expect(detail).toContainText("Example.B");

  await detail.locator(
    '[data-dependency-type-key="Example.B"]',
  ).click();
  await expect(activation).toHaveText("Example.B");
});

test("dependency layout preserves issued levels and cycles responsively", async ({
  page,
}) => {
  await page.setViewportSize({ width: 620, height: 700 });
  await page.goto("/browser/library-metrics.html");
  await page.getByRole("button", {
    name: "Load dependency structure",
  }).click();

  await expect(page.locator(".metrics-dependency-level")).toHaveText([
    "Level 0",
    "Level 1",
    "Level 2",
  ]);
  await expect(page.locator(".metrics-dependency-node-cycle")).toHaveCount(2);
  const cycleDirections = await page.locator("path.metrics-dependency-edge")
    .evaluateAll(elements => elements
      .map(element => ({
        title: element.querySelector("title")?.textContent ?? "",
        path: element.getAttribute("d") ?? "",
        markerEnd: element.getAttribute("marker-end") ?? "",
      }))
      .filter(edge =>
        edge.title.includes("Example.Core depends on Example.Workflows") ||
        edge.title.includes("Example.Workflows depends on Example.Core")));
  expect(cycleDirections).toHaveLength(2);
  expect(new Set(cycleDirections.map(edge => edge.path)).size).toBe(2);
  expect(cycleDirections.every(edge =>
    edge.markerEnd === "url(#metrics-dependency-arrow)")).toBe(true);
  const viewport = page.locator(".metrics-dependency-viewport");
  const geometry = await viewport.evaluate(element => ({
    clientWidth: element.clientWidth,
    scrollWidth: element.scrollWidth,
    right: element.getBoundingClientRect().right,
    documentWidth: document.documentElement.clientWidth,
  }));

  expect(geometry.scrollWidth).toBeGreaterThanOrEqual(geometry.clientWidth);
  expect(geometry.right).toBeLessThanOrEqual(geometry.documentWidth + .5);
});

test("level-zero reciprocal cycle routes remain inside the viewport", async ({
  page,
}) => {
  await page.setViewportSize({ width: 620, height: 700 });
  await page.goto("/browser/library-metrics.html?dependency=cycle-zero");
  await page.getByRole("button", {
    name: "Load dependency structure",
  }).click();

  const geometry = await page.locator("path.metrics-dependency-edge")
    .evaluateAll(elements => {
      const first = elements[0];
      if (!(first instanceof SVGGraphicsElement) || !first.ownerSVGElement)
        throw new Error("Dependency SVG is missing.");
      const svg = first.ownerSVGElement;
      return {
        viewBoxWidth: svg.viewBox.baseVal.width,
        paths: elements.map(element => {
          if (!(element instanceof SVGGraphicsElement))
            throw new Error("Dependency edge is not graphical.");
          const bounds = element.getBBox();
          return {
            left: bounds.x,
            right: bounds.x + bounds.width,
            path: element.getAttribute("d") ?? "",
            markerEnd: element.getAttribute("marker-end") ?? "",
          };
        }),
      };
    });

  expect(geometry.paths).toHaveLength(2);
  expect(new Set(geometry.paths.map(path => path.path)).size).toBe(2);
  for (const path of geometry.paths) {
    expect(path.left).toBeGreaterThanOrEqual(0);
    expect(path.right).toBeLessThanOrEqual(geometry.viewBoxWidth);
    expect(path.markerEnd).toBe("url(#metrics-dependency-arrow)");
  }
});

test("deep dependency levels scroll without shrinking labels", async ({
  page,
}) => {
  await page.setViewportSize({ width: 620, height: 700 });
  await page.goto("/browser/library-metrics.html?dependency=deep");
  await page.getByRole("button", {
    name: "Load dependency structure",
  }).click();

  const geometry = await page.locator(".metrics-dependency-viewport")
    .evaluate(element => {
      const svg = element.querySelector(".metrics-dependency-structure");
      const label = element.querySelector(".metrics-dependency-level");
      if (!(svg instanceof SVGSVGElement) ||
          !(label instanceof SVGTextElement)) {
        throw new Error("Dependency layout is incomplete.");
      }
      const matrix = label.getScreenCTM();
      return {
        clientWidth: element.clientWidth,
        scrollWidth: element.scrollWidth,
        svgWidth: svg.getBoundingClientRect().width,
        labelScale: matrix === null ? 0 : Math.hypot(matrix.a, matrix.b),
      };
    });

  expect(geometry.scrollWidth).toBeGreaterThan(geometry.clientWidth);
  expect(geometry.svgWidth).toBeGreaterThanOrEqual(2_580);
  expect(geometry.labelScale).toBeGreaterThanOrEqual(.99);
});
