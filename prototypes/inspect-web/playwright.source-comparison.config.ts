/// <reference types="node" />
import { defineConfig, devices } from "@playwright/test";
import process from "node:process";

process.env.INSPECT_WEB_SOURCE_DIFF_URL = "http://127.0.0.1:4188/index.html";
process.env.INSPECT_WEB_SOURCE_DIFF_FIXTURE_ONLY = "1";

export default defineConfig({
  testDir: "./browser",
  testMatch: "source-comparison-production.spec.ts",
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  reporter: "line",
  use: {
    trace: "retain-on-failure",
  },
  projects: [{
    name: "firefox",
    use: { ...devices["Desktop Firefox"] },
  }],
  webServer: {
    command: "node scripts/serve-package-adoption-gate.ts",
    env: {
      INSPECT_WEB_PACKAGE_ADOPTION_SITE:
        process.env.INSPECT_WEB_SOURCE_DIFF_SITE
          ?? "../../artifacts/inspect-web-publish/wwwroot",
      INSPECT_WEB_PACKAGE_ADOPTION_PORT: "4188",
    },
    url: "http://127.0.0.1:4188/index.html",
    reuseExistingServer: false,
  },
});
