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
  await expect(edges).toHaveCount(20);
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
