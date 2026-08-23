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
import {
  cpSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
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
    dispatches: ["renderLens", "loadSelectedTypeLensData"],
  },
  {
    vocabulary: "PackageLens",
    file: "data.ts",
    find: '  ["metadata", "Metadata"]\n] as const;',
    replace: '  ["metadata", "Metadata"],\n  ["probe-package-lens", "Probe"]\n] as const;',
    token: "probe-package-lens",
    dispatches: ["packageLensBody"],
  },
  {
    vocabulary: "MemberSection",
    file: "data.ts",
    find: '  ["annotated", "Annotated source"],\n] as const;',
    replace: '  ["annotated", "Annotated source"],\n  ["probe-member-section", "Probe"],\n] as const;',
    token: "probe-member-section",
    dispatches: ["applyMemberSection", "applyView", "renderMember", "loadSelectionData"],
  },
  {
    vocabulary: "WorkspaceScope",
    file: "data.ts",
    find: 'const workspaceScopes = ["package", "type", "member"] as const;',
    replace: 'const workspaceScopes = ["package", "type", "member", "probe-workspace-scope"] as const;',
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

// A vocabulary can be closed by the compiler without going through `assertNever`. A
// bidirectional `Covers<>` pair ties the vocabulary to a union declared elsewhere, so a
// value added to *either* side fails to compile -- which is strictly stronger than a
// widening case here, because the widening cases only catch additions to the catalog.
// `GraphSourceStatus` is gated that way: its union carries per-variant payloads, so it
// cannot be generated from the catalog, and the pair is what keeps the two in step.
//
// This discovers that mechanism rather than exempting a name. Both directions are
// required, so a single-direction pair does not count, and if this discovery ever stops
// matching, the vocabulary it was covering becomes uncovered and the assertion below
// fails -- the safe direction for a decayed anchor.
function typeLevelCoveredVocabularies(): Set<string> {
  const directions = new Set<string>();
  const names = new Set<string>();
  for (const entry of readdirSync(join(projectRoot, "test"), { withFileTypes: true })) {
    if (!entry.isFile() || !entry.name.endsWith(".test.ts")) continue;
    const source = readFileSync(join(entry.parentPath, entry.name), "utf8");
    for (const match of source.matchAll(/Covers<\s*([^,]+?)\s*,\s*([^>]+?)\s*>/g)) {
      const from = match[1]?.trim();
      const to = match[2]?.trim();
      if (!from || !to) continue;
      directions.add(`${from}=>${to}`);
      for (const side of [from, to]) {
        if (/^[A-Za-z_$][\w$]*$/.test(side)) names.add(side);
      }
    }
  }
  const covered = new Set<string>();
  for (const name of names) {
    for (const direction of directions) {
      const [from, to] = direction.split("=>");
      if (to !== name || !from) continue;
      if (directions.has(`${name}=>${from}`)) covered.add(name);
    }
  }
  return covered;
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
    [
      "GraphSourceStatus",
      "MemberSection",
      "PackageLens",
      "SpotlightScope",
      "TypeLens",
      "WorkspaceScope",
    ],
    "the catalog-union anchor stopped matching the vocabularies it is meant to discover");

  const covered = new Set<string>([
    ...widenings.map(widening => widening.vocabulary),
    ...typeLevelCoveredVocabularies(),
  ]);
  assert.deepEqual(
    [...declared].filter(name => !covered.has(name)).sort((a, b) => a.localeCompare(b)),
    [],
    "a closed vocabulary is declared in src/ but is gated by neither a widening case here "
      + "nor a bidirectional Covers<> pair");
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
