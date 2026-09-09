/// <reference types="node" />
import { defineConfig, devices } from "@playwright/test";
import process from "node:process";

process.env.INSPECT_WEB_LIBRARY_API_DIFF_URL = "http://127.0.0.1:4189/index.html";

export default defineConfig({
  testDir: "./browser",
  testMatch: "library-api-diff-production.spec.ts",
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
        process.env.INSPECT_WEB_LIBRARY_API_DIFF_SITE
          ?? "../../artifacts/inspect-web-publish/wwwroot",
      INSPECT_WEB_PACKAGE_ADOPTION_PORT: "4189",
    },
    url: "http://127.0.0.1:4189/index.html",
    reuseExistingServer: false,
  },
});
