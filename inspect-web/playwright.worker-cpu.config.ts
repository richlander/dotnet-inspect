import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./browser",
  testMatch: ["worker-cpu-isolation.spec.ts"],
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  timeout: 120_000,
  expect: { timeout: 15_000 },
  webServer: {
    command: "node scripts/serve-worker-runtime-gate.ts",
    port: 4186,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
  use: {
    baseURL: "http://127.0.0.1:4186",
    headless: true,
    trace: "retain-on-failure",
  },
  projects: [{
    name: "firefox",
    use: { ...devices["Desktop Firefox"] },
  }],
});
