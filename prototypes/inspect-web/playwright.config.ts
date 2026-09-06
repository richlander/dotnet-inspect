import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./browser",
  testMatch: "*.spec.ts",
  testIgnore: ["worker-runtime.spec.ts", "package-adoption.spec.ts"],
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: "line",
  use: {
    baseURL: "http://127.0.0.1:4175",
    trace: "retain-on-failure",
  },
  projects: [{
    name: "firefox",
    use: {
      ...devices["Desktop Firefox"],
    },
  }],
  webServer: {
    command: "npm run dev -- --host 127.0.0.1 --port 4175 --strictPort",
    url: "http://127.0.0.1:4175/browser/annotated-source.html",
    reuseExistingServer: !process.env.CI,
  },
});
