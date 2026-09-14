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
