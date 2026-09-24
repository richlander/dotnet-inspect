import { expect, test } from "@playwright/test";

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
  const forward = "Example.A calls Example.B at 1 retained sites";
  const reverse = "Example.B calls Example.A at 1 retained sites";
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
});
