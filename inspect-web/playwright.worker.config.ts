import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./browser",
  testMatch: ["worker-runtime.spec.ts", "content-security-policy.spec.ts"],
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  timeout: 120_000,
  reporter: "line",
  use: {
    baseURL: "http://127.0.0.1:4186",
    trace: "retain-on-failure",
  },
  projects: [{
    name: "firefox",
    use: { ...devices["Desktop Firefox"] },
  }],
  webServer: {
    command: "node scripts/serve-worker-runtime-gate.ts",
    url: "http://127.0.0.1:4186/worker-runtime-gate.html",
    reuseExistingServer: false,
  },
});
