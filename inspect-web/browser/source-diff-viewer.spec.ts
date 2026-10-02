import { expect, test, type Page } from "@playwright/test";
import { renderSourceDiffViewer } from "../src/source-diff-viewer.ts";
import type { BrowserSourceDiff } from "../src/source-diff-transport.ts";

function comparison(
  before: string[],
  after: string[],
  changes: BrowserSourceDiff["changes"],
): BrowserSourceDiff {
  return {
    version: 1,
    before: {
      label: "Before",
      lines: before,
      finalLineTerminator: "Present",
    },
    after: {
      label: "After",
      lines: after,
      finalLineTerminator: "Present",
    },
    relations: [],
    statistics: {
      added: changes.reduce((count, change) => count + change.after.count, 0),
      removed: changes.reduce(
        (count, change) => count + change.before.count,
        0,
      ),
      changedBefore: 0,
      changedAfter: 0,
      movedBefore: 0,
      movedAfter: 0,
    },
    changes,
  };
}

async function show(
  page: Page,
  diff: BrowserSourceDiff,
  mode: "unified" | "side-by-side",
): Promise<void> {
  await page.goto("/browser/annotated-source.html");
  const html = renderSourceDiffViewer(diff, String, { mode });
  await page.setContent(`<!doctype html>
    <link rel="stylesheet" href="/src/styles.css">
    <main>${html}</main>`);
  await expect(page.locator("[data-source-diff-viewer]")).toBeVisible();
}

test("unified labels stay accessible without entering layout or selection",
  async ({ page }) => {
    await show(page, comparison(
      ["a", "old", "c"],
      ["a", "new", "c", "tail"],
      [{
        before: { start: 1, count: 1 },
        after: { start: 1, count: 1 },
        innerMappings: [],
        annotations: [],
      }, {
        before: { start: 3, count: 0 },
        after: { start: 3, count: 1 },
        innerMappings: [],
        annotations: [],
      }],
    ), "unified");

    const hidden = page.locator(".source-diff-viewer .visually-hidden").first();
    await expect(hidden).toHaveCSS("position", "absolute");
    await expect(hidden).toHaveCSS("user-select", "none");
    const firstSource = await page.locator(
      ".source-diff-viewer-unified code",
    ).first().boundingBox();
    expect(firstSource?.x).toBeLessThan(200);

    const selectedText = await page.evaluate(() => {
      const sources = [
        ...document.querySelectorAll(".source-diff-viewer-unified code"),
      ];
      const range = document.createRange();
      range.setStart(sources[0]!, 0);
      range.setEnd(sources.at(-1)!, 1);
      const browserSelection = getSelection();
      browserSelection?.removeAllRanges();
      browserSelection?.addRange(range);
      return browserSelection?.toString() ?? "";
    });
    expect(selectedText).not.toContain("Context");
    expect(selectedText).not.toContain("Removed");
    expect(selectedText).not.toContain("Added");
    expect(selectedText).toContain("old");
    expect(selectedText).toContain("new");
  });

test("split rows share one endpoint boundary under long source lines",
  async ({ page }) => {
    const longBefore = `old ${"value ".repeat(30)}`;
    await show(page, comparison(
      ["a", longBefore, "c"],
      ["a", "new", "c"],
      [{
        before: { start: 1, count: 1 },
        after: { start: 1, count: 1 },
        innerMappings: [],
        annotations: [],
      }],
    ), "side-by-side");

    const geometry = await page.locator(".source-diff-viewer-split")
      .evaluate(table => {
        const afterEdges = [...table.querySelectorAll(
          ".source-diff-viewer-split-row > :nth-child(2)",
        )].map(cell => cell.getBoundingClientRect().left);
        const changedRow = table.querySelector(
          ".source-diff-viewer-row-change",
        );
        const beforeText = changedRow?.querySelector(
          ":scope > :first-child code",
        )?.getBoundingClientRect();
        const afterCell = changedRow?.querySelector(
          ":scope > :nth-child(2)",
        )?.getBoundingClientRect();
        return {
          afterEdges,
          beforeTextRight: beforeText?.right ?? Number.POSITIVE_INFINITY,
          afterCellLeft: afterCell?.left ?? Number.NEGATIVE_INFINITY,
          clientWidth: table.clientWidth,
          scrollWidth: table.scrollWidth,
          documentOverflow:
            document.documentElement.scrollWidth - window.innerWidth,
        };
      });
    expect(Math.max(...geometry.afterEdges) - Math.min(
      ...geometry.afterEdges,
    )).toBeLessThan(1);
    expect(geometry.beforeTextRight).toBeLessThanOrEqual(
      geometry.afterCellLeft,
    );
    expect(geometry.scrollWidth).toBeGreaterThan(geometry.clientWidth);
    expect(geometry.documentOverflow).toBeLessThanOrEqual(0);
  });

test("split additions retain both endpoint cells and change identity",
  async ({ page }) => {
    await show(page, comparison(
      ["a", "c"],
      ["a", "b", "c"],
      [{
        before: { start: 1, count: 0 },
        after: { start: 1, count: 1 },
        innerMappings: [],
        annotations: [],
      }],
    ), "side-by-side");

    const row = page.locator(
      ".source-diff-viewer-split-row[data-source-diff-change]",
    );
    await expect(row.getByRole("cell")).toHaveCount(2);
    await expect(row.getByRole("cell").nth(0))
      .toHaveAttribute("aria-colindex", "1");
    await expect(row.getByRole("cell").nth(0))
      .toContainText("Before; no line");
    await expect(row.getByRole("cell").nth(1))
      .toHaveAttribute("aria-colindex", "2");
    await expect(row.getByRole("cell").nth(1))
      .toContainText("After; Added; line 2");
  });
