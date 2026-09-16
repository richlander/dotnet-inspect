import { expect, type Locator, type Page } from "@playwright/test";

export async function expectGraphNodeInteractionFeedback(
  page: Page,
  node: Locator,
): Promise<void> {
  const visualState = () => node.evaluate(element => {
    const style = getComputedStyle(element);
    return {
      filter: style.filter,
      outlineStyle: style.outlineStyle,
      outlineWidth: style.outlineWidth,
    };
  });
  const initial = await visualState();
  expect(initial.filter).toBe("none");
  expect(initial.outlineStyle).toBe("none");

  await node.hover();
  expect((await visualState()).filter).not.toBe("none");

  await page.mouse.move(0, 0);
  await page.locator("#graph-explorer-title").focus();
  for (let index = 0; index < 12 && !await node.evaluate(element =>
    element.matches(":focus")); index++) {
    await page.keyboard.press("Tab");
  }
  await expect(node).toBeFocused();
  expect(await node.evaluate(element => element.matches(":focus-visible"))).toBe(true);
  expect(await visualState()).toEqual({
    filter: expect.not.stringMatching(/^none$/),
    outlineStyle: "solid",
    outlineWidth: "2px",
  });
}
