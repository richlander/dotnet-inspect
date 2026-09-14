import { expect, test, type Page } from "@playwright/test";

const paletteRoles = [
  "--bg",
  "--panel",
  "--panel-active",
  "--text",
  "--accent",
  "--accent-fill",
  "--accent-soft",
  "--shell-accent",
  "--shell-accent-fill",
  "--shell-accent-soft",
  "--finding-allocation",
  "--graph-target-fill",
  "--graph-target-stroke",
  "--graph-target-text",
  "--green",
  "--yellow",
] as const;

async function readPalette(page: Page): Promise<Record<string, string>> {
  return page.evaluate(roles => {
    const style = getComputedStyle(document.documentElement);
    return Object.fromEntries(roles.map(role => [
      role,
      style.getPropertyValue(role).trim(),
    ]));
  }, paletteRoles);
}

function color(palette: Record<string, string>, role: string): string {
  const value = palette[role];
  if (!value) throw new Error(`Theme palette role ${role} is missing`);
  return value;
}

function luminance(hex: string): number {
  const channels = [1, 3, 5]
    .map(index => Number.parseInt(hex.slice(index, index + 2), 16) / 255)
    .map(channel => channel <= 0.04045
      ? channel / 12.92
      : ((channel + 0.055) / 1.055) ** 2.4);
  return 0.2126 * channels[0]!
    + 0.7152 * channels[1]!
    + 0.0722 * channels[2]!;
}

function contrast(first: string, second: string): number {
  const firstLuminance = luminance(first);
  const secondLuminance = luminance(second);
  return (Math.max(firstLuminance, secondLuminance) + 0.05)
    / (Math.min(firstLuminance, secondLuminance) + 0.05);
}

test("shared theme roles use the modern .NET and C# palette", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?member=1");
  await page.locator("#application-menu-button").click();
  await page.getByRole("menuitem", { name: "Settings" }).click();

  const expected = {
    dark: {
      "--bg": "#100d1d",
      "--panel": "#171226",
      "--panel-active": "#271f3d",
      "--text": "#f0edf7",
      "--accent": "#b9aaee",
      "--accent-fill": "#512bd4",
      "--accent-soft": "#2b2054",
      "--shell-accent": "#b9aaee",
      "--shell-accent-fill": "#512bd4",
      "--shell-accent-soft": "#2b2054",
      "--finding-allocation": "#e5663f",
      "--graph-target-fill": "#311a7f",
      "--graph-target-stroke": "#b9aaee",
      "--graph-target-text": "#f0edf7",
      "--green": "#8ebb76",
      "--yellow": "#d5ad5c",
    },
    light: {
      "--bg": "#ffffff",
      "--panel": "#ffffff",
      "--panel-active": "#eeeafb",
      "--text": "#211a32",
      "--accent": "#512bd4",
      "--accent-fill": "#512bd4",
      "--accent-soft": "#eeeafb",
      "--shell-accent": "#512bd4",
      "--shell-accent-fill": "#512bd4",
      "--shell-accent-soft": "#eeeafb",
      "--finding-allocation": "#b74728",
      "--graph-target-fill": "#eeeafb",
      "--graph-target-stroke": "#512bd4",
      "--graph-target-text": "#211a32",
      "--green": "#397044",
      "--yellow": "#8a650d",
    },
  };

  for (const theme of ["dark", "light"] as const) {
    await page.evaluate(value => {
      document.documentElement.dataset.theme = value;
    }, theme);
    const palette = await readPalette(page);
    expect(palette).toEqual(expected[theme]);
    expect(contrast(color(palette, "--accent"), color(palette, "--bg")))
      .toBeGreaterThanOrEqual(4.5);
    expect(contrast("#ffffff", color(palette, "--accent-fill")))
      .toBeGreaterThanOrEqual(4.5);
    expect(contrast(
      color(palette, "--graph-target-text"),
      color(palette, "--graph-target-fill"),
    )).toBeGreaterThanOrEqual(4.5);
    expect(palette["--accent"]).not.toBe(palette["--yellow"]);
    expect(palette["--accent"]).not.toBe(palette["--green"]);

    await expect(page.locator(".subject-path-segment.current")).toHaveCSS(
      "color",
      theme === "dark" ? "rgb(185, 170, 238)" : "rgb(81, 43, 212)",
    );
    await expect(page.locator(".settings-seg.active")).toHaveCSS(
      "background-color",
      "rgb(81, 43, 212)",
    );
  }
});

test("Annotated Source uses shared roles for persistent selection", async ({
  page,
}) => {
  await page.goto("/browser/annotated-source.html");
  await page.locator("#explore-annotated").click();
  const invocation = page.locator(
    '#annotated-source-modal .annotated-source-segment.invocation:has-text("object")',
  ).first();
  await invocation.click({ position: { x: 8, y: 8 } });
  const selected = page.locator(
    "#annotated-source-modal .annotated-source-segment.selected",
  );
  const pressedControls = page.locator(
    [
      '.annotated-set-control[aria-pressed="true"]',
      '.annotated-medium-toggle[aria-pressed="true"]',
      '.annotated-coordinate-toggle[aria-pressed="true"]',
    ].join(","),
  );
  const allocationFinding = page.locator(
    ".annotated-inspector-action.category-allocation",
  );
  const costFinding = page.locator(
    ".annotated-inspector-action.category-cost",
  ).first();
  expect(await selected.count()).toBeGreaterThan(0);
  expect(await pressedControls.count()).toBeGreaterThan(0);

  const expected = {
    dark: {
      accent: "rgb(185, 170, 238)",
      allocation: "rgb(229, 102, 63)",
      cost: "rgb(213, 173, 92)",
      selectedSurface: "rgb(43, 32, 84)",
    },
    light: {
      accent: "rgb(81, 43, 212)",
      allocation: "rgb(183, 71, 40)",
      cost: "rgb(138, 101, 13)",
      selectedSurface: "rgb(238, 234, 251)",
    },
  };

  for (const theme of ["dark", "light"] as const) {
    await page.evaluate(value => {
      document.documentElement.dataset.theme = value;
    }, theme);
    for (const segment of await selected.all()) {
      await expect(segment).toHaveCSS(
        "background-color",
        expected[theme].selectedSurface,
      );
    }
    for (const control of await pressedControls.all()) {
      await expect(control).toHaveCSS(
        "background-color",
        expected[theme].selectedSurface,
      );
      await expect(control).toHaveCSS("border-color", expected[theme].accent);
      await expect(control).toHaveCSS("color", expected[theme].accent);
    }
    await expect(allocationFinding).toHaveCSS(
      "border-left-color",
      expected[theme].allocation,
    );
    await expect(costFinding).toHaveCSS(
      "border-left-color",
      expected[theme].cost,
    );
  }
});

test("ordinary keyboard focus uses the shared accent", async ({ page }) => {
  const expected = {
    dark: {
      accent: "rgb(185, 170, 238)",
      background: "rgb(29, 23, 48)",
      border: "rgb(81, 67, 111)",
      hoverBorder: "rgb(130, 122, 146)",
      muted: "rgb(170, 162, 187)",
      text: "rgb(240, 237, 247)",
    },
    light: {
      accent: "rgb(81, 43, 212)",
      background: "rgb(248, 246, 252)",
      border: "rgb(185, 170, 238)",
      hoverBorder: "rgb(117, 108, 132)",
      muted: "rgb(98, 90, 112)",
      text: "rgb(33, 26, 50)",
    },
  };

  await page.goto("/browser/workspace-titlebar.html?member=1");
  const signatureControl = page.locator(".signature-language button");
  for (const theme of ["dark", "light"] as const) {
    await page.evaluate(value => {
      document.documentElement.dataset.theme = value;
    }, theme);
    await page.mouse.move(0, 0);
    await expect(signatureControl).toHaveCSS(
      "background-color",
      expected[theme].background,
    );
    await expect(signatureControl).toHaveCSS("border-color", expected[theme].border);
    await expect(signatureControl).toHaveCSS("color", expected[theme].muted);
    await signatureControl.hover();
    await expect(signatureControl).toHaveCSS(
      "border-color",
      expected[theme].hoverBorder,
    );
    await expect(signatureControl).toHaveCSS("color", expected[theme].text);
    await signatureControl.focus();
    await expect(signatureControl).toHaveCSS(
      "outline-color",
      expected[theme].accent,
    );
  }

  await page.goto("/browser/annotated-source.html");
  await page.locator("#explore-annotated").click();
  const invocation = page.locator(
    '#annotated-source-modal .annotated-source-segment.invocation:has-text("object")',
  ).first();
  await invocation.click({ position: { x: 8, y: 8 } });
  const selected = page.locator(
    "#annotated-source-modal .annotated-source-segment.selected",
  ).first();
  const pressedControl = page.locator(
    '#annotated-source-modal [aria-pressed="true"]',
  ).first();

  for (const theme of ["dark", "light"] as const) {
    await page.evaluate(value => {
      document.documentElement.dataset.theme = value;
    }, theme);
    await selected.focus();
    await page.keyboard.press("Tab");
    await page.keyboard.press("Shift+Tab");
    await expect(selected).toBeFocused();
    await expect(selected).toHaveCSS("outline-color", expected[theme].accent);
    await pressedControl.focus();
    await page.keyboard.press("Tab");
    await page.keyboard.press("Shift+Tab");
    await expect(pressedControl).toBeFocused();
    await expect(pressedControl).toHaveCSS(
      "outline-color",
      expected[theme].accent,
    );
  }
});
