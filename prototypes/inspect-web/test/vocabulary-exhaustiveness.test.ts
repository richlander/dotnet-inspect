// The closed vocabularies in `src/data.ts` and `src/spotlight.ts` are derived from catalogs
// that also drive visible UI choices, so adding a catalog entry both widens the union and
// immediately offers the new value to users. Nothing at runtime can observe that: the value
// simply takes whichever branch the consumer falls through to. The gate for that property is
// therefore the compiler, and this test is that gate — it widens each catalog in a throwaway
// copy of `src/` and asserts `tsc` rejects the result at the `assertNever` call in every
// consumer. Deleting an exhaustive dispatch makes the corresponding case below go green,
// which fails the assertion.
import assert from "node:assert/strict";
import test from "node:test";
import { spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const tscBin = join(projectRoot, "node_modules", "typescript", "bin", "tsc");

// Each entry widens one catalog and names the token the widened union gains. `src/` imports
// nothing outside itself and the project sets `"types": []`, so a copy of `src/` plus
// `tsconfig.json` compiles standalone without `node_modules` present beside it.
const widenings = [
  {
    vocabulary: "TypeLens",
    file: "data.ts",
    find: '  ["source", "Source"]\n] as const;\n\nexport type TypeLens',
    replace: '  ["source", "Source"],\n  ["probe-type-lens", "Probe"]\n] as const;\n\nexport type TypeLens',
    token: "probe-type-lens",
    // `renderLens`.
    exhaustiveConsumers: 1,
  },
  {
    vocabulary: "MemberSection",
    file: "data.ts",
    find: '  ["annotated", "Annotated source"],\n] as const;',
    replace: '  ["annotated", "Annotated source"],\n  ["probe-member-section", "Probe"],\n] as const;',
    token: "probe-member-section",
    // The member-section render dispatch and the member-section loader dispatch.
    exhaustiveConsumers: 2,
  },
  {
    vocabulary: "SpotlightScope",
    file: "spotlight.ts",
    find: '  { id: "runtime", label: "Platform" },\n] as const;',
    replace: '  { id: "runtime", label: "Platform" },\n  { id: "probe-spotlight-scope", label: "Probe" },\n] as const;',
    token: "probe-spotlight-scope",
    // `spotlightResults`.
    exhaustiveConsumers: 1,
  },
] as const;

function compileWidenedSource(): string {
  const scratch = mkdtempSync(join(tmpdir(), "inspect-web-exhaustiveness-"));
  try {
    cpSync(join(projectRoot, "src"), join(scratch, "src"), { recursive: true });
    cpSync(join(projectRoot, "tsconfig.json"), join(scratch, "tsconfig.json"));
    for (const widening of widenings) {
      const path = join(scratch, "src", widening.file);
      const original = readFileSync(path, "utf8");
      assert.ok(
        original.includes(widening.find),
        `${widening.vocabulary}: catalog anchor not found in src/${widening.file}. `
        + "The catalog moved; update this gate's anchor rather than deleting the case.");
      writeFileSync(path, original.replace(widening.find, widening.replace));
    }
    const result = spawnSync(process.execPath, [tscBin, "--noEmit", "-p", join(scratch, "tsconfig.json")], {
      encoding: "utf8",
      cwd: scratch,
    });
    return `${result.stdout ?? ""}${result.stderr ?? ""}`;
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
}

test("widening a UI vocabulary catalog fails compilation until every consumer handles it", () => {
  const diagnostics = compileWidenedSource();
  assert.ok(
    diagnostics.trim().length > 0,
    "Widening every vocabulary catalog compiled cleanly, so no consumer is exhaustive.");
  for (const widening of widenings) {
    // `assertNever` takes `never`, so an unhandled member is reported as the new string literal
    // being unassignable, and it is the only parameter in the prototype with that type. Counting
    // those diagnostics therefore counts the vocabulary's exhaustive consumers exactly, which is
    // what a per-vocabulary match cannot do: with two dispatches over `MemberSection`, deleting
    // either one still leaves the other reporting.
    const pattern = new RegExp(
      `Argument of type '"${widening.token}"' is not assignable to parameter of type 'never'`,
      "g");
    const reported = diagnostics.match(pattern)?.length ?? 0;
    assert.equal(
      reported,
      widening.exhaustiveConsumers,
      `${widening.vocabulary}: expected ${widening.exhaustiveConsumers} exhaustive consumer(s) to reject a new `
      + `catalog entry, but ${reported} did. Fewer means a consumer accepts an unknown `
      + `${widening.vocabulary} and silently gives it another member's behavior; more means a new `
      + "exhaustive consumer was added and this count needs raising.");
  }
});
