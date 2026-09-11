import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./browser",
  testMatch: "package-adoption.spec.ts",
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  timeout: 240_000,
  reporter: "line",
  use: {
    baseURL: "http://127.0.0.1:4187",
    trace: "retain-on-failure",
  },
  projects: [{
    name: "firefox",
    use: { ...devices["Desktop Firefox"] },
  }],
  webServer: {
    command: "node scripts/serve-package-adoption-gate.ts",
    url: "http://127.0.0.1:4187/package-adoption-gate.html",
    reuseExistingServer: false,
  },
});
