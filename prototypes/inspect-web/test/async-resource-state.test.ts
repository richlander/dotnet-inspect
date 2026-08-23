// A request lifecycle union holds its whole lifecycle in one state field. Nothing in the
// type system stops a parallel loading flag, error, key, or counter from being added beside
// it and becoming a second authority. This gate derives lifecycle fields from their declared
// types, then rejects lifecycle-prefixed fields on inherited/direct coordinator state and
// root initial state. Coordinator factory and module scopes are state-free boundaries, so
// any written binding there is also rejected rather than guessed from its name.
import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import {
  parseSync,
  visitorKeys,
  type Expression,
  type Node,
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

function parseSource(file: string, source: string): Program {
  const parsed = parseSync(file, source);
  assert.deepEqual(
    parsed.errors,
    [],
    `${file} must parse before its state ownership can be inspected`);
  return parsed.program;
}

function parse(file: string): Program {
  return parseSource(file, read(file));
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

function interfaceDeclarations(program: Program): TSInterfaceDeclaration[] {
  return program.body.flatMap(node => {
    const declaration = node.type === "ExportNamedDeclaration"
      ? node.declaration
      : node;
    return declaration?.type === "TSInterfaceDeclaration" ? [declaration] : [];
  });
}

function interfaceRegistry(
  programs: readonly Program[],
): ReadonlyMap<string, TSInterfaceDeclaration> {
  return new Map(programs.flatMap(program =>
    interfaceDeclarations(program).map(declaration =>
      [declaration.id.name, declaration] as const)));
}

function stateProperties(
  declaration: TSInterfaceDeclaration,
  interfaces: ReadonlyMap<string, TSInterfaceDeclaration>,
  seen: ReadonlySet<string> = new Set(),
): TSPropertySignature[] {
  assert.equal(
    seen.has(declaration.id.name),
    false,
    "state ownership inheritance must not form a cycle");
  const nextSeen = new Set(seen).add(declaration.id.name);
  const direct = declaration.body.body.filter(
    (member): member is TSPropertySignature => member.type === "TSPropertySignature");
  const inherited = declaration.extends.flatMap(base => {
    if (base.expression.type !== "Identifier") return [];
    const inheritedDeclaration = interfaces.get(base.expression.name);
    assert.ok(
      inheritedDeclaration,
      `${declaration.id.name} inherits unchecked state ${base.expression.name}`);
    return stateProperties(inheritedDeclaration, interfaces, nextSeen);
  });
  return [...direct, ...inherited];
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
  interfaces: ReadonlyMap<string, TSInterfaceDeclaration>,
): string[] {
  return stateProperties(declaration, interfaces)
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

function isNode(value: unknown): value is Node {
  return typeof value === "object"
    && value !== null
    && typeof Reflect.get(value, "type") === "string";
}

function visit(node: Node, action: (candidate: Node) => void): void {
  action(node);
  for (const key of visitorKeys[node.type] ?? []) {
    const child = Reflect.get(node, key) as unknown;
    if (Array.isArray(child)) {
      for (const candidate of child) {
        if (isNode(candidate)) visit(candidate, action);
      }
    } else if (isNode(child)) {
      visit(child, action);
    }
  }
}

function declaredFunctionBody(
  program: Program,
  name: string,
): readonly Node[] {
  const functions = program.body.flatMap(statement => {
    const declaration = statement.type === "ExportNamedDeclaration"
      ? statement.declaration
      : statement;
    return declaration?.type === "FunctionDeclaration"
      && declaration.id?.name === name
      ? [declaration]
      : [];
  });
  assert.equal(functions.length, 1, `${name} must have exactly one declaration`);
  const found = functions[0];
  assert.ok(found);
  return found.body?.body.filter(isNode) ?? [];
}

function bindingNames(
  declaration: Extract<Node, { type: "VariableDeclaration" }>,
): string[] {
  return declaration.declarations.flatMap(binding =>
    binding.id.type === "Identifier" ? [binding.id.name] : []);
}

function assignedRoot(node: Node): string | null {
  if (node.type === "Identifier") return node.name;
  if (node.type === "TSAsExpression"
    || node.type === "TSSatisfiesExpression"
    || node.type === "TSTypeAssertion") {
    return assignedRoot(node.expression);
  }
  if (node.type === "MemberExpression") {
    return assignedRoot(node.object);
  }
  return null;
}

function writtenBindingNames(program: Program): ReadonlySet<string> {
  const written = new Set<string>();
  visit(program, node => {
    const target = node.type === "AssignmentExpression"
      ? node.left
      : node.type === "UpdateExpression"
        ? node.argument
        : null;
    if (!isNode(target)) return;
    const root = assignedRoot(target);
    if (root) written.add(root);
  });
  return written;
}

function coordinatorOwnedMutableBindings(
  program: Program,
  factoryName: string,
): string[] {
  const moduleBindings = program.body.flatMap(statement => {
    const declaration = statement.type === "ExportNamedDeclaration"
      ? statement.declaration
      : statement;
    return declaration?.type === "VariableDeclaration"
      ? bindingNames(declaration)
      : [];
  });
  const factoryBindings = declaredFunctionBody(program, factoryName)
    .flatMap(statement =>
      statement.type === "VariableDeclaration" ? bindingNames(statement) : []);
  const written = writtenBindingNames(program);
  return [...new Set([...moduleBindings, ...factoryBindings])]
    .filter(name => written.has(name));
}

interface LifecycleOwner {
  file: string;
  name: string;
  factoryName: string;
  program: Program;
  declaration: TSInterfaceDeclaration;
}

function lifecycleOwners(): LifecycleOwner[] {
  return [
    [
      "package-inspection.ts",
      "PackageInspectionState",
      "createPackageInspectionCoordinator",
    ],
    [
      "document-inspection.ts",
      "DocumentInspectionState",
      "createDocumentInspectionCoordinator",
    ],
    [
      "source-inspection.ts",
      "SourceInspectionState",
      "createSourceInspectionCoordinator",
    ],
  ].map(([file, name, factoryName]) => {
    assert.ok(file);
    assert.ok(name);
    assert.ok(factoryName);
    const program = parse(file);
    return {
      file,
      name,
      factoryName,
      program,
      declaration: inspectionState(program, name),
    };
  });
}

test("a request lifecycle union keeps exactly one state field", () => {
  const owners = lifecycleOwners();
  const interfaces = interfaceRegistry([
    ...owners.map(owner => owner.program),
    parse("data.ts"),
  ]);
  const rootProgram = parse("dotnet-inspect.ts");
  const rootDeclarations = objectDeclarations(rootProgram);
  const initialState = rootDeclarations.get("initialState");
  assert.ok(initialState, "initialState must be a statically inspectable object");

  const unions = stateUnionTypes(owners.map(owner => owner.program));
  const converted = owners.flatMap(owner =>
    lifecycleFields(owner.declaration, unions, interfaces));
  assert.ok(
    converted.length > 0,
    "no converted state field was found, so the anchor has stopped resolving");

  const surfaces: readonly (readonly [string, readonly string[]])[] = [
    ...owners.map(owner => [
      owner.name,
      stateProperties(owner.declaration, interfaces)
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
  for (const owner of owners) {
    for (const binding of coordinatorOwnedMutableBindings(
      owner.program,
      owner.factoryName)) {
      survivors.push(`${owner.file} (coordinator ownership).${binding}`);
    }
  }

  assert.deepEqual(
    survivors.sort((left, right) => left.localeCompare(right)),
    [],
    "a request lifecycle union also has a parallel authority. "
    + "Lifecycle-prefixed state and mutable coordinator-owned bindings can disagree "
    + "with the declared union.");
});

test("the parallel-field gate sees every surface and lifecycle shape", () => {
  const owners = lifecycleOwners();
  const interfaces = interfaceRegistry([
    ...owners.map(owner => owner.program),
    parse("data.ts"),
  ]);
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
    lifecycleFields(owner.declaration, unions, interfaces));
  for (const field of expected) {
    assert.ok(
      converted.includes(field),
      `${field} is no longer discovered as a lifecycle field`);
    assert.ok(
      objectPropertyNames(initialState, rootDeclarations).includes(field),
      `initialState no longer exposes the ${field} lifecycle field`);
  }
});

test("the ownership gate sees inherited fields and externally mutable bindings", () => {
  const program = parseSource("probe.ts", `
    interface BaseState {
      graphSourceEpoch: number;
    }
    interface SourceInspectionState extends BaseState {
      graphSource: GraphSourceState;
    }
    const moduleEpoch = { value: 0 };
    function createSourceInspectionCoordinator() {
      let graphEpoch = 0;
      moduleEpoch.value++;
      graphEpoch++;
      return {};
    }
  `);
  const interfaces = interfaceRegistry([program]);
  const state = inspectionState(program, "SourceInspectionState");

  assert.ok(
    stateProperties(state, interfaces)
      .some(property => propertyName(property.key, property.computed)
        === "graphSourceEpoch"),
    "inherited state is inspected");
  assert.deepEqual(
    coordinatorOwnedMutableBindings(
      program,
      "createSourceInspectionCoordinator").sort(),
    ["graphEpoch", "moduleEpoch"],
    "written bindings outside coordinator state are inspected");
});
