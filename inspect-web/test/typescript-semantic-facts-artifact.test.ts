import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import {
  join,
  resolve,
  sep,
} from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { auditedBuild, shippedArtifacts } from "./vite-audit.ts";

const inspectWebRoot = fileURLToPath(new URL("../", import.meta.url));
const unstablePackagePrefix = "typescript/unstable/";

test("the shipped Vite graph excludes TypeScript semantic tooling", async () => {
  const adapter = join(inspectWebRoot, "scripts", "typescript-semantic-facts.ts");
  const semanticTest = join(
    inspectWebRoot,
    "test",
    "typescript-semantic-facts.test.ts",
  );
  const typeScriptApi = resolve(
    inspectWebRoot,
    "node_modules",
    "typescript",
    "dist",
    "api",
    "sync",
    "api.js",
  );
  assert.ok(existsSync(adapter));
  assert.ok(existsSync(semanticTest));
  assert.ok(existsSync(typeScriptApi));

  const audited = await auditedBuild(inspectWebRoot);
  const shipped = shippedArtifacts(inspectWebRoot);
  assert.ok(audited.chunks.length > 0, "the audited build emitted no chunks");
  assert.deepEqual(shipped, audited.artifacts,
    "`npm run build` emitted a different artifact than the exclusion-audited build");

  const read = new Set(audited.readFiles.map(file => resolve(file)));
  assert.ok(audited.readFiles.length > 20);
  assert.ok(!read.has(resolve(adapter)));
  assert.ok(!read.has(resolve(semanticTest)));
  assert.ok(![...read].some(file =>
    file.includes(`${sep}node_modules${sep}typescript${sep}`)));
  assert.ok(!shipped.some(artifact => {
    const contents = artifact.contents.toString("utf8");
    return contents.includes("TypeScriptSemanticFactsHandle")
      || contents.includes(`${unstablePackagePrefix}sync`)
      || contents.includes(`${unstablePackagePrefix}ast`);
  }));
});
