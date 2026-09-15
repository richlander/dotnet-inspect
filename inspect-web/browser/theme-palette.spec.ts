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
  "--overlay-scrim",
  "--shadow-menu",
  "--shadow-dialog",
  "--shadow-drawer-color",
  "--finding-allocation",
  "--graph-target-fill",
  "--graph-target-stroke",
  "--graph-target-text",
  "--graph-package-same-prefix-fill",
  "--graph-package-same-prefix-stroke",
  "--graph-package-same-prefix-text",
  "--graph-package-external-fill",
  "--graph-package-external-stroke",
  "--graph-package-external-text",
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
      "--overlay-scrim": "rgb(8 6 18 / 72%)",
      "--shadow-menu": "0 12px 32px rgb(0 0 0 / 35%)",
      "--shadow-dialog": "0 24px 70px rgb(0 0 0 / 55%)",
      "--shadow-drawer-color": "rgb(0 0 0 / 38%)",
      "--finding-allocation": "#e5663f",
      "--graph-target-fill": "#311a7f",
      "--graph-target-stroke": "#b9aaee",
      "--graph-target-text": "#f0edf7",
      "--graph-package-same-prefix-fill": "#284c73",
      "--graph-package-same-prefix-stroke": "#9cc8f1",
      "--graph-package-same-prefix-text": "#ffffff",
      "--graph-package-external-fill": "#343a46",
      "--graph-package-external-stroke": "#aeb8c8",
      "--graph-package-external-text": "#ffffff",
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
      "--overlay-scrim": "rgb(33 26 50 / 32%)",
      "--shadow-menu": "0 12px 32px rgb(33 26 50 / 18%)",
      "--shadow-dialog": "0 24px 70px rgb(33 26 50 / 28%)",
      "--shadow-drawer-color": "rgb(33 26 50 / 22%)",
      "--finding-allocation": "#b74728",
      "--graph-target-fill": "#eeeafb",
      "--graph-target-stroke": "#512bd4",
      "--graph-target-text": "#211a32",
      "--graph-package-same-prefix-fill": "#c9dcf1",
      "--graph-package-same-prefix-stroke": "#284c73",
      "--graph-package-same-prefix-text": "#172c43",
      "--graph-package-external-fill": "#e0e3e8",
      "--graph-package-external-stroke": "#596273",
      "--graph-package-external-text": "#252a33",
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
    expect(contrast(
      color(palette, "--graph-package-same-prefix-text"),
      color(palette, "--graph-package-same-prefix-fill"),
    )).toBeGreaterThanOrEqual(4.5);
    expect(contrast(
      color(palette, "--graph-package-external-text"),
      color(palette, "--graph-package-external-fill"),
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

test("overlays use the shared elevation hierarchy", async ({ page }) => {
  const expected = {
    dark: {
      dialog: "rgba(0, 0, 0, 0.55) 0px 24px 70px 0px",
      drawerBlock: "rgba(0, 0, 0, 0.38) 0px -12px 36px 0px",
      drawerInline: "rgba(0, 0, 0, 0.38) -16px 0px 40px 0px",
      menu: "rgba(0, 0, 0, 0.35) 0px 12px 32px 0px",
      scrim: "rgba(8, 6, 18, 0.72)",
    },
    light: {
      dialog: "rgba(33, 26, 50, 0.28) 0px 24px 70px 0px",
      drawerBlock: "rgba(33, 26, 50, 0.22) 0px -12px 36px 0px",
      drawerInline: "rgba(33, 26, 50, 0.22) -16px 0px 40px 0px",
      menu: "rgba(33, 26, 50, 0.18) 0px 12px 32px 0px",
      scrim: "rgba(33, 26, 50, 0.32)",
    },
  };

  await page.goto("/browser/workspace-titlebar.html?member=1");
  await page.locator("#application-menu-button").click();
  const menu = page.locator(".application-menu");
  for (const theme of ["dark", "light"] as const) {
    await page.evaluate(value => {
      document.documentElement.dataset.theme = value;
    }, theme);
    await expect(menu).toHaveCSS("box-shadow", expected[theme].menu);
  }

  await page.getByRole("menuitem", { name: "Settings" }).click();
  const applicationBackdrop = page.locator("#settings-backdrop");
  const applicationDialog = page.locator("#settings-dialog");
  await page.evaluate(() => {
    const toast = document.createElement("div");
    toast.className = "toast";
    toast.textContent = "selection link copied";
    document.body.append(toast);
  });
  const toast = page.locator(".toast");
  for (const theme of ["dark", "light"] as const) {
    await page.evaluate(value => {
      document.documentElement.dataset.theme = value;
    }, theme);
    await expect(applicationBackdrop).toHaveCSS(
      "background-color",
      expected[theme].scrim,
    );
    await expect(applicationDialog).toHaveCSS(
      "box-shadow",
      expected[theme].dialog,
    );
    await expect(toast).toHaveCSS("box-shadow", expected[theme].menu);
  }

  await page.goto("/browser/annotated-source.html");
  await page.locator("#annotated-chip-embedded-0-1-CSharp").click();
  const embeddedDrawer = page.locator(".annotated-detail");
  for (const theme of ["dark", "light"] as const) {
    await page.evaluate(value => {
      document.documentElement.dataset.theme = value;
    }, theme);
    await expect(embeddedDrawer).toHaveCSS(
      "box-shadow",
      expected[theme].drawerBlock,
    );
  }

  await page.locator('[data-annotated-action="close-detail"]').click();
  await page.locator("#explore-annotated").click();
  const annotatedBackdrop = page.locator("#annotated-source-backdrop");
  const annotatedModal = page.locator("#annotated-source-modal");
  await page.locator("#annotated-chip-modal-0-1-CSharp").click();
  const modalDrawer = annotatedModal.locator(".annotated-detail");
  for (const theme of ["dark", "light"] as const) {
    await page.evaluate(value => {
      document.documentElement.dataset.theme = value;
    }, theme);
    await expect(annotatedBackdrop).toHaveCSS(
      "background-color",
      expected[theme].scrim,
    );
    await expect(annotatedModal).toHaveCSS(
      "box-shadow",
      expected[theme].dialog,
    );
    await expect(modalDrawer).toHaveCSS(
      "box-shadow",
      expected[theme].drawerInline,
    );
  }
});

test("Annotated Source uses shared roles for selection and Findings", async ({
  page,
}) => {
  await page.goto("/browser/annotated-source.html");
  await page.locator("#explore-annotated").click();
  await page.locator("#annotated-set-all").click();
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
  expect(await selected.count()).toBeGreaterThan(0);
  expect(await pressedControls.count()).toBeGreaterThan(0);

  const expected = {
    dark: {
      accent: "rgb(185, 170, 238)",
      findings: {
        allocation: "rgb(229, 102, 63)",
        cost: "rgb(213, 173, 92)",
        lifetime: "rgb(160, 139, 232)",
        semantics: "rgb(135, 174, 202)",
        unsafety: "rgb(217, 112, 112)",
      },
      selectedSurface: "rgb(43, 32, 84)",
    },
    light: {
      accent: "rgb(81, 43, 212)",
      findings: {
        allocation: "rgb(183, 71, 40)",
        cost: "rgb(138, 101, 13)",
        lifetime: "rgb(104, 70, 218)",
        semantics: "rgb(36, 95, 134)",
        unsafety: "rgb(217, 112, 112)",
      },
      selectedSurface: "rgb(238, 234, 251)",
    },
  };
  const findingTreatments = [
    ".annotated-finding-chip",
    ".annotated-finding-toggle",
    ".annotated-inspector-action",
  ] as const;

  for (const theme of ["dark", "light"] as const) {
    await page.evaluate(value => {
      document.documentElement.dataset.theme = value;
    }, theme);
    await page.mouse.move(0, 0);
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
    for (const [category, borderColor] of Object.entries(
      expected[theme].findings,
    )) {
      for (const treatment of findingTreatments) {
        const finding = page.locator(
          `#annotated-source-modal ${treatment}.category-${category}`,
        ).first();
        await expect(finding).toBeVisible();
        await expect(finding).toHaveCSS("border-left-color", borderColor);
        await finding.hover();
        await expect(finding).toHaveCSS("border-left-color", borderColor);
      }
    }
  }
});

test("ordinary keyboard focus uses the shared accent", async ({ page }) => {
  const expected = {
    dark: {
      accent: "rgb(185, 170, 238)",
      background: "rgb(29, 23, 48)",
      border: "rgb(81, 67, 111)",
      fillFocus: "rgb(255, 255, 255)",
      hoverBorder: "rgb(130, 122, 146)",
      muted: "rgb(170, 162, 187)",
      text: "rgb(240, 237, 247)",
    },
    light: {
      accent: "rgb(81, 43, 212)",
      background: "rgb(248, 246, 252)",
      border: "rgb(185, 170, 238)",
      fillFocus: "rgb(255, 255, 255)",
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
    const activeScope = page.locator(".scope-seg.active");
    await activeScope.focus();
    await page.keyboard.press("Tab");
    await page.keyboard.press("Shift+Tab");
    await expect(activeScope).toBeFocused();
    await expect(activeScope).toHaveCSS(
      "outline-color",
      expected[theme].fillFocus,
    );
    await expect(activeScope).toHaveCSS("outline-width", "2px");
    await expect(activeScope).toHaveCSS("outline-offset", "-3px");
  }

  await page.locator("#application-menu-button").click();
  await page.getByRole("menuitem", { name: "Settings" }).click();
  const activeTheme = page.locator(".settings-seg.active");
  for (const theme of ["dark", "light"] as const) {
    await page.evaluate(value => {
      document.documentElement.dataset.theme = value;
    }, theme);
    await activeTheme.focus();
    await page.keyboard.press("Tab");
    await page.keyboard.press("Shift+Tab");
    await expect(activeTheme).toBeFocused();
    await expect(activeTheme).toHaveCSS(
      "outline-color",
      expected[theme].fillFocus,
    );
    await expect(activeTheme).toHaveCSS("outline-width", "2px");
    await expect(activeTheme).toHaveCSS("outline-offset", "-3px");
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

test("startup failure uses the modern foundation", async ({ page }) => {
  await page.route("**/src/dotnet-inspect.ts*", route => route.abort());
  await page.goto("/");

  const app = page.locator("#app");
  await expect(app).toContainText("Inspect Web startup failed");
  await expect(app).toHaveCSS("background-color", "rgb(16, 13, 29)");
  await expect(app).toHaveCSS("color", "rgb(240, 237, 247)");
});
