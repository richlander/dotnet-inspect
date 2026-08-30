import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import { ENTRY_DOCUMENT_PATHS } from "../src/entry-routes.ts";

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? { ...value }
    : null;
}

test("every routed entry document is explicitly non-cacheable", () => {
  const configPath = new URL("../staticwebapp.config.json", import.meta.url);
  const parsed: unknown = JSON.parse(readFileSync(configPath, "utf8"));
  const routes = record(parsed)?.routes;
  assert.ok(Array.isArray(routes));

  const actual = routes.map(route => {
    const routeRecord = record(route);
    assert.ok(routeRecord);
    const headers = record(routeRecord.headers);
    assert.ok(headers);
    assert.equal(
      headers["Cache-Control"],
      "no-cache, no-store, must-revalidate");
    return routeRecord.route;
  });

  assert.deepEqual(actual, ENTRY_DOCUMENT_PATHS);
});
