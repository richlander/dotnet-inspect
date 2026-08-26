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
  ["member-focus.ts:active?.dataset.navOverload",
    "The focus snapshot checks whether the opaque identity exists."],
  ["member-focus.ts:active.dataset.navOverload",
    "The focus snapshot stores the opaque identity for later equality matching."],
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
  text: string;
}

interface DomAliases {
  datasets: Map<string, ScopedAlias<true>[]>;
  attributes: Map<string, ScopedAlias<string>[]>;
}

interface ScopedAlias<T> {
  value: T | null;
  scopeStart: number;
  scopeEnd: number;
}

interface AliasScope {
  readonly start: number;
  readonly end: number;
}

interface DatasetObjectUse {
  file: string;
  line: number;
  callee: string | null;
  text: string;
}

const dynamicAttribute = "<dynamic>";
const ambiguousAttribute = "<ambiguous>";

interface ImportedCallExemption {
  module: string;
  imported: string;
  reason: string;
}

const exemptDatasetObjectCalls = new Map<string, ImportedCallExemption>([
  ["dotnet-inspect.ts:createDependencyGraphPendingState:container.dataset", {
    module: "data.ts",
    imported: "createDependencyGraphPendingState",
    reason: "The dependency-graph state owner reads and writes opaque render signatures; "
      + "it never interprets a dataset field as a number.",
  }],
]);

function sourceFile(name: string, text: string): SourceFile {
  const parsed = parseSync(name, text);
  assert.deepEqual(
    parsed.errors,
    [],
    `${name} must parse before its DOM boundary can be inspected`);
  return { name, text, program: parsed.program };
}

function sources(): SourceFile[] {
  return readdirSync(sourceRoot, { encoding: "utf8", recursive: true })
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

function bindingNames(value: unknown): string[] {
  if (!isNode(value)) return [];
  const identifier = identifierName(value);
  if (identifier !== null) return [identifier];
  if (value.type === "AssignmentPattern"
    || value.type === "RestElement") {
    return bindingNames(
      Reflect.get(value, value.type === "AssignmentPattern" ? "left" : "argument"));
  }
  if (value.type === "TSParameterProperty") {
    return bindingNames(Reflect.get(value, "parameter"));
  }
  if (value.type === "ObjectPattern") {
    const properties: unknown = Reflect.get(value, "properties");
    if (!Array.isArray(properties)) return [];
    return properties.flatMap(property =>
      isNode(property) && property.type === "Property"
        ? bindingNames(Reflect.get(property, "value"))
        : bindingNames(property));
  }
  if (value.type === "ArrayPattern") {
    const elements: unknown = Reflect.get(value, "elements");
    return Array.isArray(elements) ? elements.flatMap(bindingNames) : [];
  }
  return [];
}

function isTransparentExpression(node: Node): boolean {
  return new Set([
    "ChainExpression",
    "ParenthesizedExpression",
    "TSAsExpression",
    "TSNonNullExpression",
    "TSSatisfiesExpression",
    "TSTypeAssertion",
  ]).has(node.type);
}

function unwrapExpression(node: Node): Node {
  let current = node;
  while (isTransparentExpression(current)) {
    const expression: unknown = Reflect.get(current, "expression");
    if (!isNode(expression)) break;
    current = expression;
  }
  return current;
}

function isDatasetExpression(node: Node, aliases: DomAliases): boolean {
  const expression = unwrapExpression(node);
  const name = identifierName(expression);
  return (expression.type === "MemberExpression"
      && memberName(expression) === "dataset")
    || (name !== null
      && scopedAliasValues(aliases.datasets, name, expression.start).has(true));
}

function domAttribute(
  node: Node,
  aliases: DomAliases = { datasets: new Map(), attributes: new Map() },
): string | null {
  const expression = unwrapExpression(node);
  if (expression.type === "MemberExpression"
    && isDatasetExpression(expression.object, aliases)) {
    return memberName(expression) ?? dynamicAttribute;
  }
  const alias = identifierName(expression);
  if (alias !== null) {
    const attributes = scopedAliasValues(
      aliases.attributes,
      alias,
      expression.start);
    return attributes.size === 1
      ? [...attributes][0] ?? null
      : attributes.size > 1
        ? ambiguousAttribute
        : null;
  }
  if (expression.type === "CallExpression"
    && memberName(expression.callee) === "getAttribute") {
    const argument = expression.arguments[0];
    if (argument?.type === "Literal"
      && typeof argument.value === "string"
      && argument.value.startsWith("data-")) {
      return dataAttributeName(argument.value);
    }
    if (!(argument?.type === "Literal"
      && typeof argument.value === "string")) {
      return dynamicAttribute;
    }
  }
  return null;
}

function aliasScope(ancestors: readonly Node[]): Node {
  for (let index = ancestors.length - 1; index >= 0; index--) {
    const node = ancestors[index];
    if (node && (node.type === "BlockStatement"
      || node.type === "StaticBlock"
      || node.type === "Program")) {
      return node;
    }
  }
  return ancestors[0]!;
}

function addScopedAlias<T>(
  aliases: Map<string, ScopedAlias<T>[]>,
  local: string,
  value: T | null,
  scope: AliasScope,
) {
  const entries = aliases.get(local) ?? [];
  if (!entries.some(entry =>
    entry.value === value
      && entry.scopeStart === scope.start
      && entry.scopeEnd === scope.end)) {
    entries.push({
      value,
      scopeStart: scope.start,
      scopeEnd: scope.end,
    });
    aliases.set(local, entries);
  }
}

function scopedAliasValues<T>(
  aliases: ReadonlyMap<string, readonly ScopedAlias<T>[]>,
  name: string,
  position: number,
): Set<T | null> {
  const candidates = (aliases.get(name) ?? [])
    .filter(alias =>
      alias.scopeStart <= position && position <= alias.scopeEnd);
  if (candidates.length === 0) return new Set();
  const narrowest = Math.min(
    ...candidates.map(alias => alias.scopeEnd - alias.scopeStart));
  return new Set(
    candidates
      .filter(alias => alias.scopeEnd - alias.scopeStart === narrowest)
      .map(alias => alias.value));
}

function addDatasetProperties(
  pattern: Node,
  aliases: DomAliases,
  scope: AliasScope,
) {
  if (pattern.type !== "ObjectPattern") return;
  const properties: unknown = Reflect.get(pattern, "properties");
  if (!Array.isArray(properties)) return;
  for (const property of properties) {
    if (!isNode(property)) continue;
    if (property.type === "RestElement") {
      for (const local of bindingNames(Reflect.get(property, "argument"))) {
        addScopedAlias(aliases.datasets, local, true, scope);
      }
      continue;
    }
    if (property.type !== "Property") continue;
    const key: unknown = Reflect.get(property, "key");
    const attribute = property.computed
      && !(isNode(key)
        && key.type === "Literal"
        && typeof key.value === "string")
      ? dynamicAttribute
      : propertyName(key);
    if (attribute === null) continue;
    for (const local of bindingNames(Reflect.get(property, "value"))) {
      addScopedAlias(aliases.attributes, local, attribute, scope);
    }
  }
}

function addBindingMasks(
  aliases: DomAliases,
  patterns: readonly unknown[],
  scope: AliasScope,
) {
  for (const name of patterns.flatMap(bindingNames)) {
    addScopedAlias(aliases.datasets, name, null, scope);
    addScopedAlias(aliases.attributes, name, null, scope);
  }
}

function addNestedDatasetAliases(
  pattern: Node,
  aliases: DomAliases,
  scope: AliasScope,
): Set<string> {
  if (pattern.type === "AssignmentPattern") {
    const left: unknown = Reflect.get(pattern, "left");
    return isNode(left)
      ? addNestedDatasetAliases(left, aliases, scope)
      : new Set();
  }
  if (pattern.type === "ArrayPattern") {
    const elements: unknown = Reflect.get(pattern, "elements");
    if (!Array.isArray(elements)) return new Set();
    return new Set(elements.flatMap(element =>
      isNode(element)
        ? [...addNestedDatasetAliases(element, aliases, scope)]
        : []));
  }
  if (pattern.type !== "ObjectPattern") return new Set();
  const claimed = new Set<string>();
  const properties: unknown = Reflect.get(pattern, "properties");
  if (!Array.isArray(properties)) return claimed;
  for (const property of properties) {
    if (!isNode(property) || property.type !== "Property") continue;
    const value: unknown = Reflect.get(property, "value");
    if (!isNode(value)) continue;
    const propertyPattern: unknown = value.type === "AssignmentPattern"
      ? Reflect.get(value, "left")
      : value;
    if (!isNode(propertyPattern)) continue;
    if (propertyName(Reflect.get(property, "key")) === "dataset") {
      if (propertyPattern.type === "ObjectPattern") {
        addDatasetProperties(propertyPattern, aliases, scope);
      } else {
        for (const local of bindingNames(propertyPattern)) {
          addScopedAlias(aliases.datasets, local, true, scope);
        }
      }
      for (const local of bindingNames(propertyPattern)) claimed.add(local);
      continue;
    }
    for (const local of addNestedDatasetAliases(
      propertyPattern,
      aliases,
      scope)) {
      claimed.add(local);
    }
  }
  return claimed;
}

function addPatternAliasesAndMasks(
  patterns: readonly unknown[],
  aliases: DomAliases,
  scope: AliasScope,
) {
  for (const pattern of patterns) {
    if (!isNode(pattern)) continue;
    const claimed = addNestedDatasetAliases(pattern, aliases, scope);
    for (const local of bindingNames(pattern)) {
      if (claimed.has(local)) continue;
      addScopedAlias(aliases.datasets, local, null, scope);
      addScopedAlias(aliases.attributes, local, null, scope);
    }
  }
}

function patternContainsDatasetProperty(pattern: Node): boolean {
  if (pattern.type === "AssignmentPattern") {
    const left: unknown = Reflect.get(pattern, "left");
    return isNode(left) && patternContainsDatasetProperty(left);
  }
  if (pattern.type === "ArrayPattern") {
    const elements: unknown = Reflect.get(pattern, "elements");
    return Array.isArray(elements)
      && elements.some(element =>
        isNode(element) && patternContainsDatasetProperty(element));
  }
  if (pattern.type !== "ObjectPattern") return false;
  const properties: unknown = Reflect.get(pattern, "properties");
  return Array.isArray(properties) && properties.some(property => {
    if (!isNode(property) || property.type !== "Property") return false;
    if (propertyName(Reflect.get(property, "key")) === "dataset") return true;
    const value: unknown = Reflect.get(property, "value");
    return isNode(value) && patternContainsDatasetProperty(value);
  });
}

function domAliases(program: Program): DomAliases {
  const aliases: DomAliases = {
    datasets: new Map(),
    attributes: new Map(),
  };
  let previousSize = -1;
  const aliasCount = () =>
    [...aliases.datasets.values(), ...aliases.attributes.values()]
      .reduce((sum, entries) => sum + entries.length, 0);
  while (previousSize !== aliasCount()) {
    previousSize = aliasCount();
    walk(program, (node, ancestors) => {
      if (node.type === "FunctionDeclaration"
        || node.type === "FunctionExpression"
        || node.type === "ArrowFunctionExpression") {
        const parameters: unknown = Reflect.get(node, "params");
        if (Array.isArray(parameters)) {
          for (const parameter of parameters) {
            if (!isNode(parameter)) continue;
            // A parameter binding is visible to later defaults and the body, but not
            // to references in its own initializer.
            addPatternAliasesAndMasks(
              [parameter],
              aliases,
              { start: parameter.end, end: node.end });
          }
        }
      } else if (node.type === "CatchClause") {
        const body: unknown = Reflect.get(node, "body");
        if (isNode(body)) {
          addPatternAliasesAndMasks(
            [Reflect.get(node, "param")],
            aliases,
            body);
        }
      }

      if (node.type !== "VariableDeclarator") return;
      const id: unknown = Reflect.get(node, "id");
      const init: unknown = Reflect.get(node, "init");
      if (!isNode(id)) return;
      const scope = aliasScope(ancestors);
      if (!isNode(init)) {
        addPatternAliasesAndMasks([id], aliases, scope);
        return;
      }

      const binding = identifierName(id);
      if (binding !== null) {
        if (isDatasetExpression(init, aliases)) {
          addScopedAlias(aliases.datasets, binding, true, scope);
          return;
        }
        const attribute = domAttribute(init, aliases);
        if (attribute !== null) {
          addScopedAlias(
            aliases.attributes,
            binding,
            attribute,
            scope);
          return;
        }
        addBindingMasks(aliases, [id], scope);
        return;
      }

      if (id.type !== "ObjectPattern") {
        addBindingMasks(aliases, [id], scope);
        return;
      }
      if (isDatasetExpression(init, aliases)) {
        addDatasetProperties(id, aliases, scope);
        return;
      }
      addPatternAliasesAndMasks([id], aliases, scope);
    });
  }
  return aliases;
}

function isReferenceNode(
  node: Node,
  ancestors: readonly Node[],
): boolean {
  const parent = ancestors.at(-1);
  if (parent?.type === "AssignmentExpression"
    && parent.operator === "="
    && Reflect.get(parent, "left") === node) {
    return false;
  }
  if (parent?.type === "UnaryExpression"
    && parent.operator === "delete"
    && Reflect.get(parent, "argument") === node) {
    return false;
  }
  if (identifierName(node) === null) return true;
  for (const ancestor of ancestors) {
    let bindings: unknown[] = [];
    if (ancestor.type === "VariableDeclarator") {
      bindings = [Reflect.get(ancestor, "id")];
    } else if (ancestor.type === "FunctionDeclaration"
      || ancestor.type === "FunctionExpression"
      || ancestor.type === "ArrowFunctionExpression") {
      const parameters: unknown = Reflect.get(ancestor, "params");
      bindings = [
        ...(ancestor.type === "ArrowFunctionExpression"
          ? []
          : [Reflect.get(ancestor, "id")]),
        ...(Array.isArray(parameters) ? parameters as unknown[] : []),
      ];
    } else if (ancestor.type === "ClassDeclaration"
      || ancestor.type === "ClassExpression") {
      bindings = [Reflect.get(ancestor, "id")];
    } else if (ancestor.type === "CatchClause") {
      bindings = [Reflect.get(ancestor, "param")];
    } else if (ancestor.type === "AssignmentExpression") {
      bindings = [Reflect.get(ancestor, "left")];
    }
    if (bindings.some(binding => isBindingPosition(binding, node))) {
      return false;
    }
  }
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

function isBindingPosition(binding: unknown, node: Node): boolean {
  if (!isNode(binding)) return false;
  if (binding.type === "AssignmentPattern") {
    return isBindingPosition(Reflect.get(binding, "left"), node);
  }
  if (binding.type === "RestElement") {
    return isBindingPosition(Reflect.get(binding, "argument"), node);
  }
  if (binding.type === "TSParameterProperty") {
    return isBindingPosition(Reflect.get(binding, "parameter"), node);
  }
  if (binding.type === "ObjectPattern") {
    const properties: unknown = Reflect.get(binding, "properties");
    return Array.isArray(properties) && properties.some(property =>
      isNode(property) && property.type === "Property"
        ? isBindingPosition(Reflect.get(property, "value"), node)
        : isBindingPosition(property, node));
  }
  if (binding.type === "ArrayPattern") {
    const elements: unknown = Reflect.get(binding, "elements");
    return Array.isArray(elements)
      && elements.some(element => isBindingPosition(element, node));
  }
  return binding.start <= node.start && node.end <= binding.end;
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

function importedBindings(
  file: SourceFile,
  module: string,
  exportedNames: ReadonlySet<string>,
): Map<string, string> {
  const imported = new Map<string, string>();
  for (const node of file.program.body) {
    if (node.type !== "ImportDeclaration"
      || join(dirname(file.name), node.source.value) !== module) {
      continue;
    }
    for (const specifier of node.specifiers) {
      if (specifier.type !== "ImportSpecifier") continue;
      const exported = propertyName(specifier.imported);
      const local = identifierName(specifier.local);
      if (exported !== null
        && local !== null
        && exportedNames.has(exported)) {
        imported.set(local, exported);
      }
    }
  }
  return imported;
}

function bindingShadowViolations(
  file: SourceFile,
  imported: ReadonlySet<string>,
): string[] {
  const violations: string[] = [];
  walk(file.program, node => {
    const patterns: unknown[] = [];
    if (node.type === "VariableDeclarator") {
      patterns.push(Reflect.get(node, "id"));
    } else if (node.type === "FunctionDeclaration"
      || node.type === "FunctionExpression"
      || node.type === "ArrowFunctionExpression") {
      if (node.type !== "ArrowFunctionExpression") {
        patterns.push(Reflect.get(node, "id"));
      }
      const parameters: unknown = Reflect.get(node, "params");
      if (Array.isArray(parameters)) {
        patterns.push(...(parameters as unknown[]));
      }
    } else if (node.type === "ClassDeclaration"
      || node.type === "ClassExpression") {
      patterns.push(Reflect.get(node, "id"));
    } else if (node.type === "CatchClause") {
      patterns.push(Reflect.get(node, "param"));
    }
    for (const name of patterns.flatMap(bindingNames)) {
      if (!imported.has(name)) continue;
      violations.push(`${file.name}:${lineOf(file.text, node.start)}: ${name}`);
    }
  });
  return violations;
}

function decoderShadowViolations(
  files: readonly SourceFile[],
  decoders: ReadonlySet<string>,
): string[] {
  const violations: string[] = [];
  for (const file of files) {
    if (file.name === parserModule) continue;
    const imported = importedBindings(
      file,
      parserModule,
      decoders);
    violations.push(
      ...bindingShadowViolations(file, new Set(imported.keys())));
  }
  return violations;
}

function datasetObjectUses(files: readonly SourceFile[]): DatasetObjectUse[] {
  const uses: DatasetObjectUse[] = [];
  for (const file of files) {
    if (file.name === parserModule) continue;
    const aliases = domAliases(file.program);
    walk(file.program, (node, ancestors) => {
      if (!isReferenceNode(node, ancestors)
        || !isDatasetExpression(node, aliases)) {
        return;
      }
      const parent = ancestors.at(-1);
      if (parent && isTransparentExpression(parent)
        && Reflect.get(parent, "expression") === node) {
        return;
      }
      if (parent?.type === "MemberExpression"
        && Reflect.get(parent, "object") === node) {
        return;
      }
      if (parent?.type === "VariableDeclarator"
        && Reflect.get(parent, "init") === node) {
        return;
      }
      const callee = parent?.type === "CallExpression"
        && parent.arguments.some(argument => argument === node)
        ? callName(parent)
        : null;
      uses.push({
        file: file.name,
        line: lineOf(file.text, node.start),
        callee,
        text: file.text.slice(node.start, node.end),
      });
    });
  }
  return uses;
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
    if (isTransparentExpression(ancestor)
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
    const imported = importedBindings(
      file,
      parserModule,
      decoders);
    walk(file.program, (node, ancestors) => {
      if (!isReferenceNode(node, ancestors)) return;
      const parent = ancestors.at(-1);
      if (parent && isTransparentExpression(parent)
        && Reflect.get(parent, "expression") === node) {
        return;
      }
      const attribute = domAttribute(node, aliases);
      if (attribute === null) return;
      if (parent?.type === "VariableDeclarator"
        && Reflect.get(parent, "init") === node) {
        return;
      }
      let decoder: CallExpression | undefined;
      for (let index = ancestors.length - 1; index >= 0; index--) {
        const ancestor = ancestors[index];
        if (ancestor?.type === "CallExpression"
          && imported.has(callName(ancestor) ?? "")
          && isDirectDecoderArgument(node, ancestors, ancestor)) {
          decoder = ancestor;
          break;
        }
      }
      const site = {
        file: file.name,
        line: lineOf(file.text, node.start),
        decoder: decoder ? callName(decoder) : null,
        text: file.text.slice(node.start, node.end),
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

function assignmentAliasViolations(files: readonly SourceFile[]): string[] {
  const violations: string[] = [];
  for (const file of files) {
    if (file.name === parserModule) continue;
    const aliases = domAliases(file.program);
    walk(file.program, node => {
      if (node.type !== "AssignmentExpression" || node.operator !== "=") return;
      const left: unknown = Reflect.get(node, "left");
      const right: unknown = Reflect.get(node, "right");
      if (!isNode(right)
        || (domAttribute(right, aliases) === null
          && !isDatasetExpression(right, aliases)
          && !(isNode(left) && patternContainsDatasetProperty(left)))) {
        return;
      }
      violations.push(
        `${file.name}:${lineOf(file.text, node.start)}: `
        + file.text.slice(node.start, node.end));
    });
  }
  return violations;
}

function getAttributeEscapeViolations(files: readonly SourceFile[]): string[] {
  const violations: string[] = [];
  for (const file of files) {
    walk(file.program, (node, ancestors) => {
      const parent = ancestors.at(-1);
      if (node.type === "Property"
        && parent?.type === "ObjectPattern"
        && propertyName(Reflect.get(node, "key")) === "getAttribute") {
        violations.push(
          `${file.name}:${lineOf(file.text, node.start)}: `
          + file.text.slice(node.start, node.end));
        return;
      }
      if (node.type !== "MemberExpression"
        || memberName(node) !== "getAttribute") {
        return;
      }
      if (parent?.type === "CallExpression"
        && Reflect.get(parent, "callee") === node) {
        return;
      }
      violations.push(
        `${file.name}:${lineOf(file.text, node.start)}: `
        + file.text.slice(node.start, node.end));
    });
  }
  return violations;
}

test("no browser payload is numerically coerced outside the canonical parsers", () => {
  const files = sources();
  assert.deepEqual(
    numericCoercionViolations(files),
    [],
    "a browser payload is coerced to a number directly. Numeric DOM payloads must go "
    + `through the canonical parsers in src/${parserModule}, which reject aliases such as `
    + "\"01\", \"+1\", \" 1\", \"1e0\", and \"-0\".");
  assert.deepEqual(
    assignmentAliasViolations(files),
    [],
    "a DOM payload escaped inspection through assignment to an existing binding");
  assert.deepEqual(
    getAttributeEscapeViolations(files),
    [],
    "getAttribute escaped direct-call inspection");
});

test("the gate rejects preprocessing, dynamic reads, and defaulted aliases", () => {
  const probe = sourceFile("probe.ts", `
import { parseNonNegativeInteger } from "./dom-data.ts";
const { overload = "" } = button.dataset;
Number(overload);
parseNonNegativeInteger(button.dataset.slIndex?.trim());
parseNonNegativeInteger(button.dataset.mdeRow);
const key = "overload";
Number(button.dataset[key]);
consume(button.dataset[key]);
const { [key]: computed } = button.dataset;
consume(computed);
consume(button.getAttribute(key));
`);
  const decoders = new Set(["parseNonNegativeInteger"]);
  const reads = attributeReads([probe], decoders);

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
    reads.get(dynamicAttribute)?.map(site => site.decoder),
    [null, null, null, null]);
  assert.ok(
    numericCoercionViolations([probe]).some(site =>
      site.endsWith("Number(overload)")));
  assert.ok(
    numericCoercionViolations([probe]).some(site =>
      site.endsWith("Number(button.dataset[key])")));
  const assignmentProbe = sourceFile("assignment-probe.ts", `
let assigned;
assigned = button.dataset.overload;
let reassignedDataset: DOMStringMap;
({ dataset: reassignedDataset } = button);
`);
  const assignmentViolations = assignmentAliasViolations([assignmentProbe]);
  assert.ok(assignmentViolations.some(site =>
    site.endsWith("assigned = button.dataset.overload")));
  assert.ok(assignmentViolations.some(site =>
    site.includes("{ dataset: reassignedDataset } = button")));
  const writeProbe = sourceFile("write-probe.ts", `
button.dataset.slIndex = String(index);
delete button.dataset.mdeRow;
`);
  const writeReads = attributeReads(
    [writeProbe],
    new Set(["parseNonNegativeInteger"]));
  assert.deepEqual(writeReads.get("slIndex") ?? [], []);
  assert.deepEqual(writeReads.get("mdeRow") ?? [], []);
});

test("the gate tracks dataset destructuring and rejects object escapes", () => {
  const probe = sourceFile("probe.ts", `
import { parseNonNegativeInteger } from "./dom-data.ts";
const { dataset } = button;
Number(dataset.slIndex);
const { ...rest } = button.dataset;
Number(rest.overload);
const { dataset: { mdeRow: nestedRow } } = button;
parseNonNegativeInteger(nestedRow);
const { currentTarget: { dataset: eventDataset } } = event;
Number(eventDataset.overload);
const coerce = (data: DOMStringMap) => Number(data.overload);
coerce(button.dataset);
`);
  const reads = attributeReads(
    [probe],
    new Set(["parseNonNegativeInteger"]));

  assert.deepEqual(
    reads.get("slIndex")?.map(site => site.decoder),
    [null]);
  assert.equal(
    reads.get("overload")?.filter(site => site.decoder === null).length,
    2);
  assert.deepEqual(
    reads.get("mdeRow")?.map(site => site.decoder),
    ["parseNonNegativeInteger"]);
  assert.deepEqual(
    datasetObjectUses([probe]).map(use => use.callee),
    ["coerce"]);

  const parameterProbe = sourceFile("parameter-probe.ts", `
function fromEvent({ currentTarget: { dataset } }) {
  Number(dataset.slIndex);
}
function fromDefault({ dataset }, index = Number(dataset.overload)) {
  return index;
}
items.forEach(({ dataset: { mdeRow } }) => Number(mdeRow));
for (const { dataset: loopDataset } of items) {
  Number(loopDataset.overload);
}
`);
  assert.equal(
    numericCoercionViolations([parameterProbe]).length,
    4);

  const objectDefaultProbe = sourceFile("object-default-probe.ts", `
const rawIndex = button.dataset.slIndex;
const { picked = rawIndex } = options;
Number(picked);
`);
  const objectDefaultReads = attributeReads(
    [objectDefaultProbe],
    new Set(["parseNonNegativeInteger"]));
  assert.ok(
    objectDefaultReads.get("slIndex")?.some(site =>
      site.text === "rawIndex" && site.decoder === null));

  const getAttributeEscapeProbe = sourceFile("get-attribute-escape-probe.ts", `
const read = button.getAttribute.bind(button);
Number(read("data-overload"));
const { getAttribute: extracted } = button;
Number(extracted("data-overload"));
`);
  assert.equal(
    getAttributeEscapeViolations([getAttributeEscapeProbe]).length,
    2);
});

test("the gate requires unshadowed decoder imports", () => {
  const probe = sourceFile("probe.ts", `
import { parseNonNegativeInteger } from "./dom-data.ts";
function shadow(parseNonNegativeInteger = Number) {
  return parseNonNegativeInteger(button.dataset.mdeRow);
}
class ParameterShadow {
  constructor(private parseNonNegativeInteger = Number) {
    parseNonNegativeInteger(button.dataset.mdeRow);
  }
}
`);
  const decoders = new Set(["parseNonNegativeInteger"]);

  assert.deepEqual(
    attributeReads([probe], decoders)
      .get("mdeRow")?.map(site => site.decoder),
    ["parseNonNegativeInteger", "parseNonNegativeInteger"]);
  assert.equal(
    decoderShadowViolations([probe], decoders)
      .filter(site => site.endsWith(": parseNonNegativeInteger"))
      .length,
    2);

  const exemptionProbe = sourceFile("dotnet-inspect.ts", `
import { createDependencyGraphPendingState } from "./data.ts";
function run(createDependencyGraphPendingState = consume) {
  createDependencyGraphPendingState(button.dataset);
}
`);
  const imported = importedBindings(
    exemptionProbe,
    "data.ts",
    new Set(["createDependencyGraphPendingState"]));
  assert.equal(
    imported.get("createDependencyGraphPendingState"),
    "createDependencyGraphPendingState");
  assert.deepEqual(
    bindingShadowViolations(
      exemptionProbe,
      new Set(["createDependencyGraphPendingState"])),
    ["dotnet-inspect.ts:3: createDependencyGraphPendingState"]);
});

test("ambiguous aliases fail closed and transparent wrappers stay direct", () => {
  const probe = sourceFile("probe.ts", `
import { parseNonNegativeInteger } from "./dom-data.ts";
var shared = button.dataset.overload;
var shared = button.dataset.mdeRow;
parseNonNegativeInteger(shared);
parseNonNegativeInteger(button.dataset.mdeOpen as string);
`);
  const decoders = new Set(["parseNonNegativeInteger"]);
  const reads = attributeReads([probe], decoders);

  assert.deepEqual(
    (reads.get("mdeOpen") ?? []).map(site => site.decoder),
    ["parseNonNegativeInteger"]);
  assert.deepEqual(
    (reads.get(ambiguousAttribute) ?? []).map(site => site.decoder),
    ["parseNonNegativeInteger"]);

  const shadowProbe = sourceFile("shadow-probe.ts", `
import { parseNonNegativeInteger } from "./dom-data.ts";
const raw = button.dataset.overload;
function unrelated(raw: string, fallback = raw) {
  return Number(raw) + Number(fallback);
}
function outerFallback(value = raw) {
  return Number(value);
}
parseNonNegativeInteger(raw);
`);
  assert.deepEqual(numericCoercionViolations([shadowProbe]), []);
  const shadowReads = attributeReads([shadowProbe], decoders)
    .get("overload") ?? [];
  assert.equal(
    shadowReads.filter(site =>
      site.decoder === "parseNonNegativeInteger").length,
    1);
  assert.ok(
    shadowReads.some(site => site.text === "raw" && site.decoder === null));
});

test("dataset objects stay inside audited helpers and decoder imports stay unshadowed", () => {
  const files = sources();
  const decoders = approvedDecoders(files);
  const objectUses = datasetObjectUses(files);
  const objectUseKey = (use: DatasetObjectUse) =>
    `${use.file}:${use.callee ?? ""}:${use.text}`;
  const exemptions = objectUses
    .filter(use => exemptDatasetObjectCalls.has(objectUseKey(use)))
    .map(objectUseKey)
    .sort((left, right) => left.localeCompare(right));
  const violations = objectUses
    .filter(use => !exemptDatasetObjectCalls.has(objectUseKey(use)))
    .map(use => `${use.file}:${use.line}: ${use.text}`);

  assert.deepEqual(
    violations,
    [],
    "a dataset object escaped property-level boundary inspection");
  assert.deepEqual(
    exemptions,
    [...exemptDatasetObjectCalls.keys()]
      .sort((left, right) => left.localeCompare(right)),
    "a dataset-object exemption is missing or no longer needed");
  for (const [key, exemption] of exemptDatasetObjectCalls) {
    const [fileName, localName] = key.split(":");
    assert.ok(fileName);
    assert.ok(localName);
    const file = files.find(candidate => candidate.name === fileName);
    assert.ok(file, `${fileName} was not parsed`);
    const imported = importedBindings(
      file,
      exemption.module,
      new Set([exemption.imported]));
    assert.equal(
      imported.get(localName),
      exemption.imported,
      `${key} must resolve to its audited import`);
    assert.deepEqual(
      bindingShadowViolations(file, new Set([localName])),
      [],
      `${key} must not be shadowed by a local binding`);
  }
  assert.deepEqual(
    decoderShadowViolations(files, decoders),
    [],
    `an imported decoder from src/${parserModule} is shadowed by a local binding`);
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

  const dynamicReads = (reads.get(dynamicAttribute) ?? [])
    .map(site =>
      `${site.file}:${site.text}:${site.decoder ?? "raw"}`)
    .sort((left, right) => left.localeCompare(right));
  assert.deepEqual(
    dynamicReads,
    ["member-focus.ts:element.dataset[snapshot.dataTarget!.key]:raw"],
    "a dynamic dataset property read was added outside the one opaque focus-identity use");
  assert.deepEqual(
    reads.get(ambiguousAttribute) ?? [],
    [],
    "a reused local binding made its originating dataset attribute ambiguous");

  const unparsed = expected.flatMap(attribute =>
    (reads.get(attribute) ?? [])
      .filter(site => site.decoder === null)
      .map(site => `${site.file}:${site.text}`))
    .sort((left, right) => left.localeCompare(right));
  assert.deepEqual(
    unparsed,
    [...exemptRawReads.keys()].sort((left, right) => left.localeCompare(right)),
    "a numeric DOM attribute is also read outside a canonical decoder, or an exemption is "
    + `no longer needed. Sites: ${unparsed.join("; ")}`);
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
