// A request lifecycle union holds its whole lifecycle in one state field.
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

import {
  parseSync,
  type Expression,
  type ObjectExpression,
  type Program,
  type PropertyKey,
  type TSInterfaceDeclaration,
  type TSPropertySignature,
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

function propertyName(key: PropertyKey, computed: boolean): string {
  if (key.type === "Identifier" && !computed) return key.name;
  if (key.type === "Literal" && typeof key.value === "string") return key.value;
  throw new Error("state ownership keys must be statically named");
}

function stateProperties(declaration: TSInterfaceDeclaration): TSPropertySignature[] {
  return declaration.body.body.filter(
    (member): member is TSPropertySignature => member.type === "TSPropertySignature");
}

function lifecycleFields(declaration: TSInterfaceDeclaration): string[] {
  return stateProperties(declaration)
    .filter(property => {
      const annotation = property.typeAnnotation?.typeAnnotation;
      return annotation?.type === "TSTypeReference"
        && annotation.typeName.type === "Identifier"
        && ["AsyncResource", "DocumentViewerState"].includes(annotation.typeName.name);
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

test("a request lifecycle union keeps exactly one state field", () => {
  const coordinatorProgram = parse("package-inspection.ts");
  const coordinatorState = inspectionState(
    coordinatorProgram,
    "PackageInspectionState");
  const documentState = inspectionState(
    parse("document-inspection.ts"),
    "DocumentInspectionState");
  const rootProgram = parse("dotnet-inspect.ts");
  const declarations = objectDeclarations(rootProgram);
  const initialState = declarations.get("initialState");
  assert.ok(initialState, "initialState must be a statically inspectable object");

  const converted = [
    ...lifecycleFields(coordinatorState),
    ...lifecycleFields(documentState),
  ];
  assert.ok(
    converted.length > 0,
    "no AsyncResource state field was found, so the anchor has stopped resolving");

  const surfaces: readonly (readonly [string, readonly string[]])[] = [
    [
      "PackageInspectionState",
      stateProperties(coordinatorState)
        .map(property => propertyName(property.key, property.computed)),
    ],
    [
      "DocumentInspectionState",
      stateProperties(documentState)
        .map(property => propertyName(property.key, property.computed)),
    ],
    ["initialState", objectPropertyNames(initialState, declarations)],
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
    "a request lifecycle union also has a parallel state field. "
    + "The union is the single source of truth for that request; a second field beside "
    + "it can disagree with it.");
});

test("the parallel-field gate sees every lifecycle surface", () => {
  // Non-vacuity, and the specific shape of the two mutations that were silent: a gate that
  // reads only one surface, or whose property scan stops matching, would pass above while
  // proving nothing.
  const coordinatorProgram = parse("package-inspection.ts");
  const coordinatorState = inspectionState(
    coordinatorProgram,
    "PackageInspectionState");
  const documentState = inspectionState(
    parse("document-inspection.ts"),
    "DocumentInspectionState");
  const rootProgram = parse("dotnet-inspect.ts");
  const declarations = objectDeclarations(rootProgram);
  const initialState = declarations.get("initialState");
  assert.ok(initialState, "initialState must be a statically inspectable object");

  assert.ok(
    stateProperties(coordinatorState)
      .some(property => propertyName(property.key, property.computed)
        === "packageOpportunities"),
    "the coordinator-state property scan no longer sees the converted lens");
  assert.ok(
    stateProperties(documentState)
      .some(property => propertyName(property.key, property.computed)
        === "docViewer"),
    "the document-state property scan no longer sees the converted viewer");
  assert.ok(
    objectPropertyNames(initialState, declarations).includes("packageOpportunities"),
    "the initial-state property scan no longer sees the converted lens");
  assert.ok(
    objectPropertyNames(initialState, declarations).includes("docViewer"),
    "the initial-state property scan no longer sees the converted viewer");
});
