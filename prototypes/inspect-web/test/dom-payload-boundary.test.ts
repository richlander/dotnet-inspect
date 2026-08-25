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

interface DomAliases {
  datasets: Set<string>;
  attributes: Map<string, string>;
}

function sourceFile(name: string, text: string): SourceFile {
  const parsed = parseSync(name, text);
  assert.deepEqual(
    parsed.errors,
    [],
    `${name} must parse before its DOM boundary can be inspected`);
  return { name, text, program: parsed.program };
}

function sources(): SourceFile[] {
  return readdirSync(sourceRoot)
    .filter(name => name.endsWith(".ts"))
    .map(name => sourceFile(name, readFileSync(join(sourceRoot, name), "utf8")));
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

function identifierName(value: unknown): string | null {
  return isNode(value)
    && value.type === "Identifier"
    && typeof value.name === "string"
    ? value.name
    : null;
}

function propertyName(value: unknown): string | null {
  const identifier = identifierName(value);
  if (identifier !== null) return identifier;
  return isNode(value)
    && value.type === "Literal"
    && typeof Reflect.get(value, "value") === "string"
    ? String(Reflect.get(value, "value"))
    : null;
}

function isDatasetExpression(node: Node, aliases: DomAliases): boolean {
  const name = identifierName(node);
  return (node.type === "MemberExpression" && memberName(node) === "dataset")
    || (name !== null && aliases.datasets.has(name));
}

function domAttribute(
  node: Node,
  aliases: DomAliases = { datasets: new Set(), attributes: new Map() },
): string | null {
  if (node.type === "MemberExpression"
    && node.object.type === "MemberExpression"
    && memberName(node.object) === "dataset") {
    return memberName(node);
  }
  const objectName = node.type === "MemberExpression"
    ? identifierName(node.object)
    : null;
  if (node.type === "MemberExpression"
    && objectName !== null
    && aliases.datasets.has(objectName)) {
    return memberName(node);
  }
  const alias = identifierName(node);
  if (alias !== null && aliases.attributes.has(alias)) {
    return aliases.attributes.get(alias) ?? null;
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

function domAliases(program: Program): DomAliases {
  const aliases: DomAliases = {
    datasets: new Set(),
    attributes: new Map(),
  };
  let previousSize = -1;
  while (previousSize !== aliases.datasets.size + aliases.attributes.size) {
    previousSize = aliases.datasets.size + aliases.attributes.size;
    walk(program, node => {
      if (node.type !== "VariableDeclarator") return;
      const id: unknown = Reflect.get(node, "id");
      const init: unknown = Reflect.get(node, "init");
      if (!isNode(id) || !isNode(init)) return;

      const binding = identifierName(id);
      if (binding !== null) {
        if (isDatasetExpression(init, aliases)) {
          aliases.datasets.add(binding);
          return;
        }
        const attribute = domAttribute(init, aliases);
        if (attribute !== null) aliases.attributes.set(binding, attribute);
        return;
      }

      if (id.type !== "ObjectPattern"
        || !isDatasetExpression(init, aliases)) {
        return;
      }
      const properties: unknown = Reflect.get(id, "properties");
      if (!Array.isArray(properties)) return;
      for (const property of properties) {
        if (!isNode(property) || property.type !== "Property") continue;
        const attribute = propertyName(Reflect.get(property, "key"));
        const local = identifierName(Reflect.get(property, "value"));
        if (attribute !== null && local !== null) {
          aliases.attributes.set(local, attribute);
        }
      }
    });
  }
  return aliases;
}

function isReferenceNode(
  node: Node,
  ancestors: readonly Node[],
): boolean {
  if (identifierName(node) === null) return true;
  if (ancestors.some(ancestor => ancestor.type === "ObjectPattern")) {
    return false;
  }
  const parent = ancestors.at(-1);
  if (parent?.type === "VariableDeclarator"
    && Reflect.get(parent, "id") === node) {
    return false;
  }
  if (parent?.type === "MemberExpression"
    && Reflect.get(parent, "property") === node
    && !parent.computed) {
    return false;
  }
  return !(parent?.type === "Property"
    && Reflect.get(parent, "key") === node
    && !parent.computed
    && Reflect.get(parent, "value") !== node);
}

function containsDomRead(root: Node, aliases: DomAliases): boolean {
  let found = false;
  walk(root, (node, ancestors) => {
    if (isReferenceNode(node, ancestors)
      && domAttribute(node, aliases) !== null) {
      found = true;
    }
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

function isDirectDecoderArgument(
  read: Node,
  ancestors: readonly Node[],
  decoder: CallExpression,
): boolean {
  let expression = read;
  for (let index = ancestors.length - 1; index >= 0; index--) {
    const ancestor = ancestors[index];
    if (!ancestor) continue;
    if (ancestor === decoder) {
      return decoder.arguments.some(argument => argument === expression);
    }
    if (new Set([
      "ChainExpression",
      "ParenthesizedExpression",
      "TSNonNullExpression",
    ]).has(ancestor.type)
      && Reflect.get(ancestor, "expression") === expression) {
      expression = ancestor;
      continue;
    }
    return false;
  }
  return false;
}

function attributeReads(
  files: readonly SourceFile[],
  decoders: ReadonlySet<string>,
): Map<string, AttributeRead[]> {
  const reads = new Map<string, AttributeRead[]>();
  for (const file of files) {
    if (file.name === parserModule) continue;
    const aliases = domAliases(file.program);
    walk(file.program, (node, ancestors) => {
      if (!isReferenceNode(node, ancestors)) return;
      const attribute = domAttribute(node, aliases);
      if (attribute === null) return;
      let decoder: CallExpression | undefined;
      for (let index = ancestors.length - 1; index >= 0; index--) {
        const ancestor = ancestors[index];
        if (ancestor?.type === "CallExpression"
          && decoders.has(callName(ancestor) ?? "")
          && isDirectDecoderArgument(node, ancestors, ancestor)) {
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

function numericCoercionViolations(files: readonly SourceFile[]): string[] {
  const violations: string[] = [];
  for (const file of files) {
    if (file.name === parserModule) continue;
    const aliases = domAliases(file.program);
    walk(file.program, node => {
      if (!isNumericCoercion(node) || !containsDomRead(node, aliases)) return;
      violations.push(
        `${file.name}:${lineOf(file.text, node.start)}: `
        + file.text.slice(node.start, node.end));
    });
  }
  return violations;
}

test("no browser payload is numerically coerced outside the canonical parsers", () => {
  assert.deepEqual(
    numericCoercionViolations(sources()),
    [],
    "a browser payload is coerced to a number directly. Numeric DOM payloads must go "
    + `through the canonical parsers in src/${parserModule}, which reject aliases such as `
    + "\"01\", \"+1\", \" 1\", \"1e0\", and \"-0\".");
});

test("the gate rejects preprocessing and destructured payload aliases", () => {
  const probe = sourceFile("probe.ts", `
const { overload } = button.dataset;
Number(overload);
parseNonNegativeInteger(button.dataset.slIndex?.trim());
parseNonNegativeInteger(button.dataset.mdeRow);
`);
  const reads = attributeReads(
    [probe],
    new Set(["parseNonNegativeInteger"]));

  assert.deepEqual(
    reads.get("overload")?.map(site => site.decoder),
    [null]);
  assert.deepEqual(
    reads.get("slIndex")?.map(site => site.decoder),
    [null]);
  assert.deepEqual(
    reads.get("mdeRow")?.map(site => site.decoder),
    ["parseNonNegativeInteger"]);
  assert.deepEqual(
    numericCoercionViolations([probe]),
    ["probe.ts:3: Number(overload)"]);
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
