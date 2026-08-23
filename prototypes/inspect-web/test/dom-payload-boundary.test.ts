// Round 2 review (GPT-5.6 Sol, Claude Opus 5) showed that the unit coverage for
// `parseNonNegativeInteger` and `parseMetadataToken` is strong -- weakening either parser
// fails several suites -- while nothing at all required a *call site* to use them. Sol
// replaced one binding's parse with a plausible inline `Number(...)` coercion and both
// advertised gates stayed green; Opus reverted an application-root read the same way, with
// 498/498 passing. A parser nobody is obliged to call is not a boundary.
//
// These gates are derived from the sources rather than restated as a roster, so a *new*
// unparsed read is red on the day it is written.
import assert from "node:assert/strict";
import test from "node:test";
import { readdirSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const sourceRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "src");

// `dom-data.ts` is the one place a browser payload legitimately meets `Number(...)`: it is
// the implementation of the canonical parsers this gate exists to require.
const parserModule = "dom-data.ts";

// Reads of a numeric attribute that are genuinely not numeric. Asserted by set equality
// below, so an entry that stops being needed is as red as a missing one -- an exemption
// roster that only ever grows is the restatement shape this stack keeps being defeated by.
const exemptRawReads = new Map<string, string>([
  ["navOverload", "member-focus.ts treats every focus-restore descriptor's value as an "
    + "opaque equality key for re-finding the same element, never as an index."],
]);

// Derived, not listed. `string | undefined` is exactly the type of a `dataset` read, so a
// function declaring that parameter is by construction a payload decoder. Adding one makes
// it approved automatically, and renaming one cannot silently empty this set, because the
// non-vacuity assertion below would fail.
function approvedDecoders(): Set<string> {
  const found = new Set<string>();
  for (const file of sources()) {
    const text = withoutComments(file.text);
    for (const match of text.matchAll(
      /function ([A-Za-z_$][\w$]*)\s*\(\s*\w+\s*:\s*string \| undefined\s*[,)]/g)) {
      if (match[1]) found.add(match[1]);
    }
  }
  return found;
}

interface SourceFile {
  name: string;
  text: string;
}

function sources(): SourceFile[] {
  return readdirSync(sourceRoot)
    .filter(name => name.endsWith(".ts"))
    .map(name => ({ name, text: readFileSync(join(sourceRoot, name), "utf8") }));
}

// Line and block comments are stripped so that prose describing a coercion -- this file's
// own commit message did exactly that -- cannot fail the gate, and so that commenting a
// violation out is not mistaken for one.
function withoutComments(text: string): string {
  return text
    .replace(/\/\*[\s\S]*?\*\//g, match => match.replace(/[^\n]/g, " "))
    .replace(/(^|[^:])\/\/[^\n]*/g, (_match, prefix: string) => prefix);
}

function lineOf(text: string, index: number): number {
  return text.slice(0, index).split("\n").length;
}

test("no browser payload is numerically coerced outside the canonical parsers", () => {
  // The property is a *derived emptiness*, not a roster: any new `Number(el.dataset.x)`,
  // `parseInt`, or unary `+` over a DOM read is a violation the moment it is written, with
  // nothing to update and no entry that can go stale.
  const domRead = /(?:\.dataset\.[A-Za-z]\w*|\.getAttribute\s*\([^)]*\))/;
  const coercion =
    /\b(?:Number|parseInt|parseFloat)\s*\(|\bNumber\.parse(?:Int|Float)\s*\(|(?<![\w$)\]])\+(?=\s*[A-Za-z_$])/g;

  const violations: string[] = [];
  for (const file of sources()) {
    if (file.name === parserModule) continue;
    const text = withoutComments(file.text);
    for (const match of text.matchAll(coercion)) {
      // Bound the inspected expression at the statement end so a coercion on one line is
      // not blamed for a DOM read several statements later.
      const rest = text.slice(match.index, match.index + 200);
      const terminator = rest.search(/[;\n]/);
      const expression = terminator >= 0 ? rest.slice(0, terminator) : rest;
      if (domRead.test(expression)) {
        violations.push(`${file.name}:${lineOf(text, match.index)}: ${expression.trim()}`);
      }
    }
  }

  assert.deepEqual(
    violations,
    [],
    "a browser payload is coerced to a number directly. Numeric DOM and URL payloads must "
    + `go through the canonical parsers in src/${parserModule}, which reject the aliases `
    + "(\"01\", \"+1\", \" 1\", \"1e0\", \"-0\") a bare coercion accepts.");
});

test("every attribute a canonical parser reads has no unparsed read anywhere", () => {
  // Which attributes carry numbers is derived from the code that already parses them, so
  // this needs no list of "numeric attributes" to drift. Reverting *one* of an attribute's
  // reads to a raw coercion -- Opus's mutation, which the whole suite missed -- is red here
  // even before the coercion gate above sees it, and reverting the *only* read is caught by
  // that gate, because a revert has to coerce.
  const decoders = approvedDecoders();
  assert.ok(
    decoders.has("parseNonNegativeInteger") && decoders.has("parseMetadataToken")
      && decoders.has("parseExplorerCoordinates") && decoders.has("isSelectedGroupChip"),
    `the decoder anchor stopped resolving; found [${[...decoders].join(", ")}]`);

  const numeric = new Map<string, string[]>();
  const reads = new Map<string, string[]>();

  for (const file of sources()) {
    if (file.name === parserModule) continue;
    const text = withoutComments(file.text);
    for (const match of text.matchAll(/\.dataset\.([A-Za-z]\w*)/g)) {
      const attribute = match[1];
      if (attribute === undefined) continue;
      const site = `${file.name}:${lineOf(text, match.index)}`;
      const decoder = enclosingCall(text, match.index);
      const bucket = decoder !== null && decoders.has(decoder) ? numeric : reads;
      bucket.set(attribute, [...(bucket.get(attribute) ?? []), site]);
    }
  }

  // Non-vacuity: if the enclosing-call walk stopped resolving, every attribute would land in
  // `reads` and the assertion below would pass while proving nothing.
  assert.ok(
    numeric.size >= 8,
    `only ${numeric.size} attributes were seen flowing into a canonical parser, so the `
    + "call-site walk has stopped resolving and this gate proves nothing.");

  const unparsed = [...numeric.keys()]
    .filter(attribute => reads.has(attribute))
    .sort((a, b) => a.localeCompare(b));

  assert.deepEqual(
    unparsed,
    [...exemptRawReads.keys()].sort((a, b) => a.localeCompare(b)),
    "an attribute that is parsed as a number somewhere is also read raw somewhere else, or "
    + "an exemption is no longer needed. Both reads see the same payload, so the unparsed "
    + "one accepts values the parsed one rejects. Sites: "
    + unparsed.map(name => `data-${name} at ${(reads.get(name) ?? []).join(", ")}`)
      .join("; "));
});

// Resolve the call an expression is an argument of, by walking backwards with bracket
// balancing. This reads the code's structure rather than matching a source pattern, so a
// reformatted or renamed call site cannot silently drop out of the enumeration.
function enclosingCall(text: string, index: number): string | null {
  let depth = 0;
  for (let cursor = index - 1; cursor >= 0; cursor -= 1) {
    const character = text[cursor];
    if (character === ")" || character === "]" || character === "}") depth += 1;
    else if (character === "(" || character === "[" || character === "{") {
      if (depth > 0) {
        depth -= 1;
        continue;
      }
      if (character !== "(") return null;
      return /([A-Za-z_$][\w$]*)\s*$/.exec(text.slice(Math.max(0, cursor - 64), cursor))?.[1]
        ?? null;
    }
  }
  return null;
}
