// A request lifecycle union holds its whole lifecycle in one state field. Nothing in the
// type system stops a parallel loading flag, error, key, or counter from being added beside
// it and becoming a second authority. This gate derives lifecycle fields from their declared
// types, then rejects every parallel field on coordinator state, root initial state, and
// module-level mutable bindings.
import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import {
  parseSync,
  type Expression,
  type ObjectExpression,
  type Program,
  type PropertyKey,
  type TSInterfaceDeclaration,
  type TSPropertySignature,
  type TSTypeAliasDeclaration,
  type TSTypeLiteral,
} from "oxc-parser";

const sourceRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "src");

function read(file: string): string {
  return readFileSync(join(sourceRoot, file), "utf8");
}

function parse(file: string): Program {
  const parsed = parseSync(file, read(file));
  assert.deepEqual(
    parsed.errors,
    [],
    `${file} must parse before its state ownership can be inspected`);
  return parsed.program;
}

function inspectionState(
  program: Program,
  name: string,
): TSInterfaceDeclaration {
  const declarations = program.body.flatMap(node => {
    const declaration = node.type === "ExportNamedDeclaration"
      ? node.declaration
      : node;
    return declaration?.type === "TSInterfaceDeclaration"
      && declaration.id.name === name
      ? [declaration]
      : [];
  });
  assert.equal(
    declarations.length,
    1,
    `${name} must have exactly one declaration`);
  const declaration = declarations[0];
  assert.ok(declaration);
  return declaration;
}

function typeAliases(program: Program): TSTypeAliasDeclaration[] {
  return program.body.flatMap(node => {
    const declaration = node.type === "ExportNamedDeclaration"
      ? node.declaration
      : node;
    return declaration?.type === "TSTypeAliasDeclaration"
      ? [declaration]
      : [];
  });
}

function propertyName(key: PropertyKey, computed: boolean): string {
  if (key.type === "Identifier" && !computed) return key.name;
  if (key.type === "Literal" && typeof key.value === "string") return key.value;
  throw new Error("state ownership keys must be statically named");
}

function stateProperties(declaration: TSInterfaceDeclaration): TSPropertySignature[] {
  return declaration.body.body.filter(
    (member): member is TSPropertySignature => member.type === "TSPropertySignature");
}

function isStatusVariant(type: TSTypeLiteral): boolean {
  return type.members.some(member => {
    if (member.type !== "TSPropertySignature"
      || propertyName(member.key, member.computed) !== "status") {
      return false;
    }
    const annotation = member.typeAnnotation?.typeAnnotation;
    return annotation?.type === "TSLiteralType"
      && annotation.literal.type === "Literal"
      && typeof annotation.literal.value === "string";
  });
}

// A status-discriminated object union is another lifecycle owner, just like
// `AsyncResource<T>`. Discovering it from the AST means a new status spelling or variant
// cannot walk past a hand-written suffix or member roster.
function stateUnionTypes(programs: readonly Program[]): ReadonlySet<string> {
  const found = new Set<string>();
  for (const program of programs) {
    for (const alias of typeAliases(program)) {
      const annotation = alias.typeAnnotation;
      if (annotation.type === "TSUnionType"
        && annotation.types.length >= 2
        && annotation.types.every(
          type => type.type === "TSTypeLiteral" && isStatusVariant(type))) {
        found.add(alias.id.name);
      }
    }
  }
  return found;
}

function lifecycleFields(
  declaration: TSInterfaceDeclaration,
  unionTypes: ReadonlySet<string>,
): string[] {
  return stateProperties(declaration)
    .filter(property => {
      const annotation = property.typeAnnotation?.typeAnnotation;
      if (annotation?.type !== "TSTypeReference"
        || annotation.typeName.type !== "Identifier") {
        return false;
      }
      return annotation.typeName.name === "AsyncResource"
        || unionTypes.has(annotation.typeName.name);
    })
    .map(property => propertyName(property.key, property.computed));
}

function objectExpression(expression: Expression | null): ObjectExpression | null {
  if (expression?.type === "ObjectExpression") return expression;
  if (expression?.type === "TSAsExpression"
    || expression?.type === "TSSatisfiesExpression"
    || expression?.type === "TSTypeAssertion") {
    return objectExpression(expression.expression);
  }
  return null;
}

function objectDeclarations(program: Program): Map<string, ObjectExpression> {
  const declarations = new Map<string, ObjectExpression>();
  for (const statement of program.body) {
    if (statement.type !== "VariableDeclaration") continue;
    for (const declaration of statement.declarations) {
      if (declaration.id.type !== "Identifier") continue;
      const object = objectExpression(declaration.init);
      if (object) declarations.set(declaration.id.name, object);
    }
  }
  return declarations;
}

function objectPropertyNames(
  object: ObjectExpression,
  declarations: ReadonlyMap<string, ObjectExpression>,
  seen: ReadonlySet<ObjectExpression> = new Set(),
): string[] {
  assert.equal(
    seen.has(object),
    false,
    "state ownership spreads must not form a cycle");
  const nextSeen = new Set(seen).add(object);
  const names: string[] = [];
  for (const property of object.properties) {
    if (property.type === "Property") {
      names.push(propertyName(property.key, property.computed));
      continue;
    }
    const inline = objectExpression(property.argument);
    const named = property.argument.type === "Identifier"
      ? declarations.get(property.argument.name)
      : undefined;
    const spread = inline ?? named;
    assert.ok(
      spread,
      "a spread in initialState could not be resolved, so its keys are unchecked");
    names.push(...objectPropertyNames(spread, declarations, nextSeen));
  }
  return names;
}

function mutableBindingNames(program: Program): string[] {
  return program.body.flatMap(statement => {
    const declaration = statement.type === "ExportNamedDeclaration"
      ? statement.declaration
      : statement;
    if (declaration?.type !== "VariableDeclaration"
      || declaration.kind === "const") {
      return [];
    }
    return declaration.declarations.flatMap(binding =>
      binding.id.type === "Identifier" ? [binding.id.name] : []);
  });
}

interface LifecycleOwner {
  file: string;
  name: string;
  program: Program;
  declaration: TSInterfaceDeclaration;
}

function lifecycleOwners(): LifecycleOwner[] {
  return [
    ["package-inspection.ts", "PackageInspectionState"],
    ["document-inspection.ts", "DocumentInspectionState"],
    ["source-inspection.ts", "SourceInspectionState"],
  ].map(([file, name]) => {
    assert.ok(file);
    assert.ok(name);
    const program = parse(file);
    return {
      file,
      name,
      program,
      declaration: inspectionState(program, name),
    };
  });
}

test("a request lifecycle union keeps exactly one state field", () => {
  const owners = lifecycleOwners();
  const rootProgram = parse("dotnet-inspect.ts");
  const rootDeclarations = objectDeclarations(rootProgram);
  const initialState = rootDeclarations.get("initialState");
  assert.ok(initialState, "initialState must be a statically inspectable object");

  const unions = stateUnionTypes(owners.map(owner => owner.program));
  const converted = owners.flatMap(owner =>
    lifecycleFields(owner.declaration, unions));
  assert.ok(
    converted.length > 0,
    "no converted state field was found, so the anchor has stopped resolving");

  const surfaces: readonly (readonly [string, readonly string[]])[] = [
    ...owners.map(owner => [
      owner.name,
      stateProperties(owner.declaration)
        .map(property => propertyName(property.key, property.computed)),
    ] as const),
    ["initialState", objectPropertyNames(initialState, rootDeclarations)],
    ...[
      ...owners.map(owner => [owner.file, owner.program] as const),
      ["dotnet-inspect.ts", rootProgram] as const,
    ].map(([file, program]) => [
      `${file} (module scope)`,
      mutableBindingNames(program),
    ] as const),
  ];

  const survivors: string[] = [];
  for (const [surface, names] of surfaces) {
    for (const name of names) {
      for (const lifecycle of converted) {
        if (name !== lifecycle && name.startsWith(lifecycle))
          survivors.push(`${surface}.${name}`);
      }
    }
  }

  assert.deepEqual(
    survivors.sort((left, right) => left.localeCompare(right)),
    [],
    "a request lifecycle union also has a parallel state field. "
    + "The union is the single source of truth for that request; a second field beside "
    + "it -- a counter, a key, or a cached copy -- can disagree with it.");
});

test("the parallel-field gate sees every surface and lifecycle shape", () => {
  const owners = lifecycleOwners();
  const rootProgram = parse("dotnet-inspect.ts");
  const rootDeclarations = objectDeclarations(rootProgram);
  const initialState = rootDeclarations.get("initialState");
  assert.ok(initialState, "initialState must be a statically inspectable object");

  const unions = stateUnionTypes(owners.map(owner => owner.program));
  assert.ok(
    unions.has("DocumentViewerState"),
    "the document status union is no longer discovered");
  assert.ok(
    unions.has("GraphSourceState"),
    "the graph-source status union is no longer discovered");

  const expected = ["packageOpportunities", "docViewer", "graphSource"];
  const converted = owners.flatMap(owner =>
    lifecycleFields(owner.declaration, unions));
  for (const field of expected) {
    assert.ok(
      converted.includes(field),
      `${field} is no longer discovered as a lifecycle field`);
    assert.ok(
      objectPropertyNames(initialState, rootDeclarations).includes(field),
      `initialState no longer exposes the ${field} lifecycle field`);
  }
});
