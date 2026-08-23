// A lens converted to `AsyncResource` holds its whole request lifecycle in one state field.
// The parallel `…Loading`/`…Error`/`…Key` fields it replaces are gone -- but nothing proved
// they were gone, and adversarial review (GPT-5.6 Sol, Claude Opus 5) resurrected them two
// ways with the suite and the analyzer silent:
//
//   * adding optional legacy fields to `PackageInspectionState`, and
//   * adding all three concrete fields beside the resource in `initialState`.
//
// Neither is caught by the type system. `AppState` is derived *from* the `initialState`
// literal, so extra properties simply join the type, and `state` reaches the coordinator as
// a non-fresh value, so no excess-property check applies. Neither oxlint nor knip sees an
// object property as unused.
//
// A literal search for the three old names would restate today's roster. This derives the
// converted lenses from the state type instead, so converting the next lens extends the
// check automatically and a resurrected parallel field is red on either surface.
import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const sourceRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "src");

function read(file: string): string {
  return readFileSync(join(sourceRoot, file), "utf8");
}

function block(source: string, anchor: RegExp): string {
  const start = source.search(anchor);
  assert.ok(
    start >= 0, `the anchor ${anchor} no longer matches, so this gate derives nothing`);
  let depth = 0;
  let end = -1;
  for (let cursor = start; cursor < source.length && end < 0; cursor += 1) {
    const character = source[cursor];
    if (character === "{") depth += 1;
    else if (character === "}") {
      depth -= 1;
      if (depth === 0) end = cursor + 1;
    }
  }
  assert.ok(end >= 0, `the block at ${anchor} is unterminated`);
  return source.slice(start, end);
}

// The converted lenses, discovered from their declared type rather than named here.
function resourceFields(declaration: string): string[] {
  return [...declaration.matchAll(/(\w+)\s*:\s*AsyncResource</g)]
    .map(match => match[1])
    .filter((name): name is string => name !== undefined);
}

// Read the keys the block actually declares, at its own nesting level, rather than the
// keys that happen to be formatted one-per-line. Layout is not the property being gated,
// and a scan anchored on indentation misses a single-line object -- which is exactly the
// shape a spread source takes.
function propertyNames(literalOrInterface: string): string[] {
  const depths: number[] = [];
  let depth = 0;
  for (const character of literalOrInterface) {
    if (character === "}") depth -= 1;
    depths.push(depth);
    if (character === "{") depth += 1;
  }

  const names: string[] = [];
  for (const key of literalOrInterface.matchAll(/[{,;]\s*"?(\w+)"?\s*\??\s*:/g)) {
    const name = key[1];
    // Measure at the colon, not at the leading delimiter: for the first property that
    // delimiter is the opening brace itself, which sits one level out.
    if (name !== undefined && depths[key.index + key[0].length - 1] === 1) {
      names.push(name);
    }
  }
  return names;
}

// A spread carries its source object's properties into the literal, so a gate that reads
// only the literal's own keys can be walked around by moving the field into a spread
// object. Round 2 review of PR #4585 (GPT-5.6 Sol) did exactly that. Resolve each spread
// to the declaration it names and include those keys; refuse to pass on one that cannot
// be resolved, because an unresolved spread is an unread surface, not an empty one.
function spreadNames(literal: string, source: string): string[] {
  const names: string[] = [];
  for (const spread of literal.matchAll(/^\s{2}\.\.\.(\w+),/gm)) {
    const identifier = spread[1];
    if (identifier === undefined) continue;
    const declaration = new RegExp(`(?:const|let|var) ${identifier}\\b[^=]*= \\{`);
    const start = source.search(declaration);
    assert.ok(
      start >= 0,
      `the spread \`...${identifier}\` could not be resolved, so its keys are unchecked`);
    names.push(...propertyNames(block(source, declaration)));
  }
  return names;
}

test("a lens converted to AsyncResource keeps exactly one state field", () => {
  const coordinatorState = block(
    read("package-inspection.ts"), /export interface PackageInspectionState \{/);
  const initialState = block(read("dotnet-inspect.ts"), /^const initialState = \{/m);

  const converted = resourceFields(coordinatorState);
  assert.ok(
    converted.length > 0,
    "no AsyncResource state field was found, so the anchor has stopped resolving");

  const rootSource = read("dotnet-inspect.ts");
  const surfaces: readonly (readonly [string, readonly string[]])[] = [
    ["PackageInspectionState", propertyNames(coordinatorState)],
    [
      "initialState",
      [...propertyNames(initialState), ...spreadNames(initialState, rootSource)],
    ],
  ];

  const survivors: string[] = [];
  for (const [surface, names] of surfaces) {
    for (const name of names) {
      for (const lens of converted) {
        // `packageOpportunitiesKey` beside `packageOpportunities` is a parallel field;
        // `packageOpportunities` itself is the resource.
        if (name !== lens && name.startsWith(lens)) survivors.push(`${surface}.${name}`);
      }
    }
  }

  assert.deepEqual(
    survivors.sort((left, right) => left.localeCompare(right)),
    [],
    "a lens holding its lifecycle in an AsyncResource also has a parallel state field. "
    + "The resource is the single source of truth for that request; a second field beside "
    + "it can disagree with it.");
});

test("the parallel-field gate sees both surfaces", () => {
  // Non-vacuity, and the specific shape of the two mutations that were silent: a gate that
  // reads only one surface, or whose property scan stops matching, would pass above while
  // proving nothing.
  const coordinatorState = block(
    read("package-inspection.ts"), /export interface PackageInspectionState \{/);
  const initialState = block(read("dotnet-inspect.ts"), /^const initialState = \{/m);

  assert.ok(
    propertyNames(coordinatorState).includes("packageOpportunities"),
    "the coordinator-state property scan no longer sees the converted lens");
  assert.ok(
    propertyNames(initialState).includes("packageOpportunities"),
    "the initial-state property scan no longer sees the converted lens");
});
