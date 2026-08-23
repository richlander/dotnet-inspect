// Round 2 review (GPT-5.6 Sol, Claude Opus 5) showed that parser unit coverage
// cannot enforce a boundary when nothing requires call sites to use those parsers.
// This gate reads TypeScript syntax, so line wrapping, comments, and look-alike
// function signatures cannot hide an unchecked DOM payload.
import assert from "node:assert/strict";
import test from "node:test";
import { readdirSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import {
  parseSync,
  visitorKeys,
  type CallExpression,
  type Node,
  type Program,
} from "oxc-parser";

import { numericDomAttributes } from "../src/dom-data.ts";

const sourceRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "src");
const parserModule = "dom-data.ts";

const exemptRawReads = new Map<string, string>([
  ["navOverload", "member-focus.ts treats every focus-restore descriptor's value as an "
    + "opaque equality key for re-finding the same element, never as an index."],
]);

interface SourceFile {
  name: string;
  text: string;
  program: Program;
}

interface AttributeRead {
  file: string;
  line: number;
  decoder: string | null;
}

function sources(): SourceFile[] {
  return readdirSync(sourceRoot)
    .filter(name => name.endsWith(".ts"))
    .map(name => {
      const text = readFileSync(join(sourceRoot, name), "utf8");
      const parsed = parseSync(name, text);
      assert.deepEqual(
        parsed.errors,
        [],
        `${name} must parse before its DOM boundary can be inspected`);
      return { name, text, program: parsed.program };
    });
}

function isNode(value: unknown): value is Node {
  return typeof value === "object"
    && value !== null
    && "type" in value
    && typeof value.type === "string";
}

function walk(
  node: Node,
  visit: (node: Node, ancestors: readonly Node[]) => void,
  ancestors: readonly Node[] = [],
) {
  visit(node, ancestors);
  const nextAncestors = [...ancestors, node];
  for (const key of visitorKeys[node.type] ?? []) {
    const child: unknown = Reflect.get(node, key);
    if (Array.isArray(child)) {
      for (const item of child) {
        if (isNode(item)) walk(item, visit, nextAncestors);
      }
    } else if (isNode(child)) {
      walk(child, visit, nextAncestors);
    }
  }
}

function memberName(node: Node): string | null {
  if (node.type !== "MemberExpression") return null;
  if (!node.computed && node.property.type === "Identifier") {
    return node.property.name;
  }
  if (node.computed
    && node.property.type === "Literal"
    && typeof node.property.value === "string") {
    return node.property.value;
  }
  return null;
}

function callName(node: CallExpression): string | null {
  if (node.callee.type === "Identifier") return node.callee.name;
  const property = memberName(node.callee);
  if (property !== null
    && node.callee.type === "MemberExpression"
    && node.callee.object.type === "Identifier") {
    return `${node.callee.object.name}.${property}`;
  }
  return null;
}

function dataAttributeName(attribute: string): string {
  return attribute.slice("data-".length)
    .replace(/-([a-z])/g, (_match, letter: string) => letter.toUpperCase());
}

function domAttribute(node: Node): string | null {
  if (node.type === "MemberExpression"
    && node.object.type === "MemberExpression"
    && memberName(node.object) === "dataset") {
    return memberName(node);
  }
  if (node.type === "CallExpression"
    && memberName(node.callee) === "getAttribute") {
    const argument = node.arguments[0];
    if (argument?.type === "Literal"
      && typeof argument.value === "string"
      && argument.value.startsWith("data-")) {
      return dataAttributeName(argument.value);
    }
  }
  return null;
}

function containsDomRead(root: Node): boolean {
  let found = false;
  walk(root, node => {
    if (domAttribute(node) !== null) found = true;
  });
  return found;
}

function isNumericCoercion(node: Node): boolean {
  if (node.type === "UnaryExpression") return node.operator === "+";
  if (node.type !== "CallExpression") return false;
  return new Set([
    "Number",
    "parseInt",
    "parseFloat",
    "Number.parseInt",
    "Number.parseFloat",
  ]).has(callName(node) ?? "");
}

function lineOf(text: string, index: number): number {
  return text.slice(0, index).split("\n").length;
}

function approvedDecoders(files: readonly SourceFile[]): Set<string> {
  const parser = files.find(file => file.name === parserModule);
  assert.ok(parser, `${parserModule} was not parsed`);
  const names = parser.program.body.flatMap(node => {
    const declaration = node.type === "ExportNamedDeclaration"
      ? node.declaration
      : null;
    return declaration?.type === "FunctionDeclaration" && declaration.id
      ? [declaration.id.name]
      : [];
  });
  assert.deepEqual(
    [...names].sort((left, right) => left.localeCompare(right)),
    [
      "isSelectedGroupChip",
      "parseExplorerCoordinates",
      "parseMetadataToken",
      "parseNonNegativeInteger",
    ],
    "src/dom-data.ts exports are the canonical DOM decoder roster");
  return new Set(names);
}

function attributeReads(
  files: readonly SourceFile[],
  decoders: ReadonlySet<string>,
): Map<string, AttributeRead[]> {
  const reads = new Map<string, AttributeRead[]>();
  for (const file of files) {
    if (file.name === parserModule) continue;
    walk(file.program, (node, ancestors) => {
      const attribute = domAttribute(node);
      if (attribute === null) return;
      let decoder: CallExpression | undefined;
      for (let index = ancestors.length - 1; index >= 0; index--) {
        const ancestor = ancestors[index];
        if (ancestor?.type === "CallExpression"
          && decoders.has(callName(ancestor) ?? "")) {
          decoder = ancestor;
          break;
        }
      }
      const site = {
        file: file.name,
        line: lineOf(file.text, node.start),
        decoder: decoder ? callName(decoder) : null,
      };
      reads.set(attribute, [...(reads.get(attribute) ?? []), site]);
    });
  }
  return reads;
}

test("no browser payload is numerically coerced outside the canonical parsers", () => {
  const violations: string[] = [];
  for (const file of sources()) {
    if (file.name === parserModule) continue;
    walk(file.program, node => {
      if (!isNumericCoercion(node) || !containsDomRead(node)) return;
      violations.push(
        `${file.name}:${lineOf(file.text, node.start)}: `
        + file.text.slice(node.start, node.end));
    });
  }

  assert.deepEqual(
    violations,
    [],
    "a browser payload is coerced to a number directly. Numeric DOM payloads must go "
    + `through the canonical parsers in src/${parserModule}, which reject aliases such as `
    + "\"01\", \"+1\", \" 1\", \"1e0\", and \"-0\".");
});

test("every declared numeric attribute reaches a canonical parser at every numeric read", () => {
  const files = sources();
  const decoders = approvedDecoders(files);
  const reads = attributeReads(files, decoders);
  const expected = [...numericDomAttributes]
    .sort((left, right) => left.localeCompare(right));
  const parsed = [...reads]
    .filter(([, sites]) => sites.some(site => site.decoder !== null))
    .map(([attribute]) => attribute)
    .sort((left, right) => left.localeCompare(right));

  assert.deepEqual(
    parsed,
    expected,
    "the product-owned numeric DOM attribute catalog and canonical decoder call sites "
    + "must stay equal; a missing entry has lost its only parser and an extra entry has "
    + "no declared numeric contract");

  const unparsed = expected.filter(attribute =>
    (reads.get(attribute) ?? []).some(site => site.decoder === null));
  assert.deepEqual(
    unparsed,
    [...exemptRawReads.keys()].sort((left, right) => left.localeCompare(right)),
    "a numeric DOM attribute is also read outside a canonical decoder, or an exemption is "
    + "no longer needed. Sites: "
    + unparsed.map(attribute => `${attribute} at ${
      (reads.get(attribute) ?? [])
        .filter(site => site.decoder === null)
        .map(site => `${site.file}:${site.line}`)
        .join(", ")}`).join("; "));
});

test("dependency group patching stays behind its selected-chip decoder", () => {
  const files = sources();
  const reads = attributeReads(files, approvedDecoders(files));
  const sites = (reads.get("depGroup") ?? [])
    .map(site => `${site.file}:${site.decoder}`)
    .sort((left, right) => left.localeCompare(right));

  assert.deepEqual(sites, [
    "package-view.ts:isSelectedGroupChip",
    "package-view.ts:parseNonNegativeInteger",
  ]);
});
