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
import { cpSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import {
  parseSync,
  visitorKeys,
  type Node,
  type Program,
  type TSTypeAliasDeclaration,
} from "oxc-parser";

const projectRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const tscBin = join(projectRoot, "node_modules", "typescript", "bin", "tsc");

// Each entry widens one catalog and names the token the widened union gains. `src/` imports
// nothing outside itself and the project sets `"types": []`, so a copy of `src/` plus
// `tsconfig.json` compiles standalone without `node_modules` present beside it.
// `dispatches` names every function the widened member must be rejected in. It is a set of
// locations, not a count: adversarial review broke one dispatch and added an unrelated
// consumer receiving the same `never`, which a count cannot tell apart from the original.
const widenings = [
  {
    vocabulary: "TypeLens",
    file: "data.ts",
    find: '  ["source", "Source"]\n] as const;\n\nexport type TypeLens',
    replace: '  ["source", "Source"],\n  ["probe-type-lens", "Probe"]\n] as const;\n\nexport type TypeLens',
    token: "probe-type-lens",
    dispatches: [
      "typeLensPresentation",
      "renderLens",
      "loadSelectedTypeLensData",
    ],
  },
  {
    vocabulary: "PackageLens",
    file: "data.ts",
    find: '  ["metadata", "Metadata"]\n] as const;',
    replace: '  ["metadata", "Metadata"],\n  ["probe-package-lens", "Probe"]\n] as const;',
    token: "probe-package-lens",
    dispatches: ["packageLensBody", "packageLensPresentation"],
  },
  {
    vocabulary: "MemberSection",
    file: "data.ts",
    find: '  ["annotated", "Annotated source"],\n] as const;',
    replace: '  ["annotated", "Annotated source"],\n  ["probe-member-section", "Probe"],\n] as const;',
    token: "probe-member-section",
    dispatches: [
      "memberSectionPresentation",
      "loadMemberSectionContent",
      "applyView",
      "renderMember",
      "loadSelectionData",
    ],
  },
  {
    vocabulary: "WorkspaceScope",
    file: "data.ts",
    find: 'const workspaceScopes = ["workspace", "package", "type", "member"] as const;',
    replace: 'const workspaceScopes = ["workspace", "package", "type", "member", "probe-workspace-scope"] as const;',
    token: "probe-workspace-scope",
    dispatches: ["onScopeSelect", "renderScopeBar", "selectScopeLensByIndex"],
  },
  {
    vocabulary: "SpotlightScope",
    file: "spotlight.ts",
    find: '  { id: "runtime", label: "Platform" },\n] as const;',
    replace: '  { id: "runtime", label: "Platform" },\n  { id: "probe-spotlight-scope", label: "Probe" },\n] as const;',
    token: "probe-spotlight-scope",
    dispatches: ["spotlightResults"],
  },
] as const;

interface WidenedCompilation {
  diagnostics: string;
  // Diagnostics point into the scratch copy, which is deleted on the way out, so the
  // sources have to be captured while it still exists.
  sources: Map<string, string>;
}

function compileWidenedSource(): WidenedCompilation {
  const scratch = mkdtempSync(join(tmpdir(), "inspect-web-exhaustiveness-"));
  const compilationRoot = join(scratch, "inspect-web");
  try {
    cpSync(join(projectRoot, "src"), join(compilationRoot, "src"), { recursive: true });
    cpSync(join(projectRoot, "tsconfig.json"), join(compilationRoot, "tsconfig.json"));
    // `src/` dynamically imports bundled packages by bare specifier, so the throwaway copy
    // needs the same module resolution the real project has. Without this the compile fails
    // with TS2307 and the exhaustiveness evidence below is invalid rather than merely noisy.
    //
    // A Windows "dir" symlink needs Developer Mode or an elevated shell, which would make this
    // test fail for an ordinary Windows checkout. A junction needs no privilege and resolves
    // the same way for a directory target that is already absolute.
    symlinkSync(
      join(projectRoot, "node_modules"),
      join(compilationRoot, "node_modules"),
      process.platform === "win32" ? "junction" : "dir");
    cpSync(
      join(projectRoot, "..", "annotated-source-viewer", "src"),
      join(scratch, "annotated-source-viewer", "src"),
      { recursive: true });
    for (const widening of widenings) {
      const path = join(compilationRoot, "src", widening.file);
      const original = readFileSync(path, "utf8");
      assert.ok(
        original.includes(widening.find),
        `${widening.vocabulary}: catalog anchor not found in src/${widening.file}. `
        + "The catalog moved; update this gate's anchor rather than deleting the case.");
      writeFileSync(path, original.replace(widening.find, widening.replace));
    }
    const result = spawnSync(process.execPath, [
      tscBin,
      "--noEmit",
      "-p",
      join(compilationRoot, "tsconfig.json"),
    ], {
      encoding: "utf8",
      cwd: compilationRoot,
    });
    const diagnostics = `${result.stdout ?? ""}${result.stderr ?? ""}`;
    const sources = new Map<string, string>();
    for (const match of diagnostics.matchAll(/^(\S+)\(\d+,\d+\): error TS/gm)) {
      const file = match[1];
      if (!file || sources.has(file)) continue;
      const absolute = file.startsWith("/") ? file : join(compilationRoot, file);
      sources.set(file, readFileSync(absolute, "utf8"));
    }
    return { diagnostics, sources };
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
}

// Map a diagnostic location back to the function that contains it, so the gate can name
// *which* dispatch rejected the widened member.
//
// Counting diagnostics is not enough. Adversarial review (Claude Opus 5) broke `renderLens`
// and added an unrelated but reachable guard receiving the same `never`, which held the
// count at 1 and kept the suite green while an unhandled lens rendered blank. A location is
// what a count cannot fake.
function enclosingFunction(source: string, line: number): string {
  const lines = source.split("\n");
  for (let index = Math.min(line, lines.length) - 1; index >= 0; index -= 1) {
    const text = lines[index] ?? "";
    const declaration = /^(?:async )?function ([A-Za-z_$][\w$]*)\s*[(<]/.exec(text);
    if (declaration?.[1]) return declaration[1];
    // Object-literal callbacks such as `onScopeSelect: target => {` are dispatch sites too.
    const property = /^\s{0,8}([A-Za-z_$][\w$]*): (?:async )?(?:\([^)]*\)|[A-Za-z_$][\w$]*) =>/
      .exec(text);
    if (property?.[1]) return property[1];
  }
  return "<unknown>";
}

function parse(file: string): Program {
  const source = readFileSync(join(projectRoot, "src", file), "utf8");
  const parsed = parseSync(file, source);
  assert.deepEqual(
    parsed.errors,
    [],
    `${file} must parse before its vocabulary declarations can be inspected`);
  return parsed.program;
}

function isNode(value: unknown): value is Node {
  return typeof value === "object"
    && value !== null
    && typeof Reflect.get(value, "type") === "string";
}

function containsTypeQuery(node: Node): boolean {
  if (node.type === "TSTypeQuery") return true;
  for (const key of visitorKeys[node.type] ?? []) {
    const child = Reflect.get(node, key) as unknown;
    if (Array.isArray(child)) {
      if (child.some(candidate => isNode(candidate) && containsTypeQuery(candidate)))
        return true;
    } else if (isNode(child) && containsTypeQuery(child)) {
      return true;
    }
  }
  return false;
}

function catalogTypeAliases(program: Program): TSTypeAliasDeclaration[] {
  return program.body.flatMap(node => {
    const declaration = node.type === "ExportNamedDeclaration"
      ? node.declaration
      : null;
    return declaration?.type === "TSTypeAliasDeclaration"
      && containsTypeQuery(declaration.typeAnnotation)
      ? [declaration]
      : [];
  });
}

test("every closed UI vocabulary is covered by this gate", () => {
  // Derive the roster from every exported type alias that queries a catalog, independent of
  // formatting, tuple indexing, or union shape. A new catalog-derived vocabulary that nobody
  // adds here fails rather than sitting silently uncovered -- which is exactly how
  // `PackageLens` and `WorkspaceScope` went ungated through three rounds.
  const declared = new Set(
    ["data.ts", "spotlight.ts"]
      .flatMap(file => catalogTypeAliases(parse(file)))
      .map(declaration => declaration.id.name));

  // Non-vacuity: an anchor that stopped matching would otherwise turn this into a test that
  // derives an empty roster and passes.
  assert.deepEqual(
    [...declared].sort((a, b) => a.localeCompare(b)),
    ["MemberSection", "PackageLens", "SpotlightScope", "TypeLens", "WorkspaceScope"],
    "the catalog-union anchor stopped matching the vocabularies it is meant to discover");

  const covered = new Set<string>(widenings.map(widening => widening.vocabulary));
  assert.deepEqual(
    [...declared].filter(name => !covered.has(name)).sort((a, b) => a.localeCompare(b)),
    [],
    "a closed vocabulary is declared in src/ but has no case in this gate");
});

test("widening a UI vocabulary catalog fails compilation until every consumer handles it", () => {
  const { diagnostics, sources } = compileWidenedSource();
  assert.ok(
    diagnostics.trim().length > 0,
    "Widening every vocabulary catalog compiled cleanly, so no consumer is exhaustive.");
  assert.deepEqual(
    [...diagnostics.matchAll(/error TS(\d+):/g)]
      .map(match => match[1])
      .filter(code => code !== undefined)
      .filter((code, index, codes) => codes.indexOf(code) === index),
    ["2345"],
    "The widened compile has an unrelated diagnostic, so its exhaustiveness evidence is invalid.");

  for (const widening of widenings) {
    // `assertNever` takes `never`, so an unhandled member is reported as the new string
    // literal being unassignable, and it is the only parameter in the prototype with that
    // type. Collect the *functions* those diagnostics land in.
    const pattern = new RegExp(
      `^(\\S+)\\((\\d+),\\d+\\): error TS2345: Argument of type '"${widening.token}"' `
      + "is not assignable to parameter of type 'never'",
      "gm");
    const reported = new Set<string>();
    for (const match of diagnostics.matchAll(pattern)) {
      const file = match[1];
      const line = Number(match[2]);
      const source = file === undefined ? undefined : sources.get(file);
      if (source === undefined || !Number.isFinite(line)) continue;
      reported.add(enclosingFunction(source, line));
    }

    assert.deepEqual(
      [...reported].sort((a, b) => a.localeCompare(b)),
      [...widening.dispatches].sort((a, b) => a.localeCompare(b)),
      `${widening.vocabulary}: the dispatches that reject a new catalog entry are not the `
      + "ones this gate expects. A missing name means that dispatch silently gives an "
      + "unknown member another member's behavior; an unexpected name means a new "
      + "exhaustive dispatch appeared and belongs in this list.");
  }
});
