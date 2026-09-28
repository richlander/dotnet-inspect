import assert from "node:assert/strict";
import {
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import {
  NodeKind,
  SourceFileClassification,
  TypeCategory,
  openTypeScriptSemanticFacts,
  semanticFactsTestSeam,
  type DeclarationHandle,
  type QueryResult,
  type SourceFileFact,
  type SymbolFact,
  type TypeFact,
  type TypeHandle,
  type TypeScriptSemanticFactsSession,
} from "../scripts/typescript-semantic-facts.ts";

const inspectWebRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const appSourcePath = join(inspectWebRoot, "src", "dotnet-inspect.ts");
const functionBearingStateFields = [
  "compareClone",
  "implementationProfiles",
  "libraryApiDiff",
  "packageIntegrations",
  "packageOpportunities",
  "platformIndex",
  "queryNoticeRetryAction",
  "retryAction",
  "typeHeat",
] as const;

test("retained Workspace clone projects function-bearing AppState fields", () => {
  const opened = openTypeScriptSemanticFacts(
    join(inspectWebRoot, "tsconfig.json"),
  );
  assert.equal(opened.kind, "Opened");
  if (opened.kind !== "Opened") return;

  const session = opened.session;
  try {
    const appSource = sourceByPath(session, "src/dotnet-inspect.ts");
    const text = readFileSync(appSourcePath, "utf8");
    const appState = declaredTypeAlias(session, appSource, text, "AppState");
    const actual = propertiesRequiringCloneProjection(session, appState);

    assert.deepEqual(actual, functionBearingStateFields);

    const projection = structuredCloneProjectionTypes(
      session,
      appSource,
      text,
      "cloneCanonicalWorkspaceSnapshotForRetention",
    );
    const projectedTypes = functionBearingStateFields.map(field => {
      const type = projection.get(field);
      assert.ok(type !== undefined, `missing clone projection for '${field}'`);
      return { field, type };
    });
    const unsafeTypes = projectionRequiredTypes(
      session,
      projectedTypes.map(entry => entry.type),
    );
    for (const field of functionBearingStateFields) {
      const projected = projectedTypes.find(entry => entry.field === field);
      assert.ok(projected !== undefined);
      assert.equal(
        unsafeTypes.has(projected.type.handle),
        false,
        `clone projection for '${field}' remains function-bearing`,
      );
    }
  } finally {
    session.dispose();
  }
});

test("constructor, opaque, and shadowed-container values remain unsafe", () => {
  const fixtureRoot = mkdtempSync(
    join(tmpdir(), "retained-workspace-cloneability-"),
  );
  try {
    writeFileSync(join(fixtureRoot, "tsconfig.json"), JSON.stringify({
      compilerOptions: {
        strict: true,
        target: "ES2022",
        noEmit: true,
        module: "ESNext",
        moduleResolution: "Bundler",
      },
      files: ["builtins.ts", "state.ts"],
    }));
    writeFileSync(join(fixtureRoot, "builtins.ts"), `
export type SafeStandardMap = Map<string, string>;
export type CallbackStandardMap = Map<string, () => boolean>;
`);
    writeFileSync(join(fixtureRoot, "state.ts"), `
import type {
  CallbackStandardMap,
  SafeStandardMap,
} from "./builtins";
class ConstructorOnly {}
type Map<T> = { callback: () => boolean; payload: T };
type SyntheticState = {
  direct: typeof ConstructorOnly;
  nested: { constructorValue: typeof ConstructorOnly };
  callback: () => boolean;
  opaqueUnknown: unknown;
  opaqueAny: any;
  opaqueObject: object;
  emptyObject: {};
  shadowedMap: Map<string>;
  safeStandardMap: SafeStandardMap;
  callbackStandardMap: CallbackStandardMap;
  plain: { value: string };
};
function cloneSyntheticState(state: SyntheticState) {
  return structuredClone({
    ...state,
    direct: state.direct,
    nested: { constructorValue: null },
    callback: state.callback,
    opaqueUnknown: state.opaqueUnknown,
    opaqueAny: state.opaqueAny,
    opaqueObject: state.opaqueObject,
    emptyObject: state.emptyObject,
    shadowedMap: state.shadowedMap,
    safeStandardMap: state.safeStandardMap,
    callbackStandardMap: state.callbackStandardMap,
  });
}
`);

    const opened = openTypeScriptSemanticFacts(
      join(fixtureRoot, "tsconfig.json"),
    );
    assert.equal(opened.kind, "Opened");
    if (opened.kind !== "Opened") return;

    const session = opened.session;
    try {
      const source = sourceByPath(session, "state.ts");
      const text = readFileSync(join(fixtureRoot, "state.ts"), "utf8");
      const state = declaredTypeAlias(
        session,
        source,
        text,
        "SyntheticState",
      );
      assert.deepEqual(
        propertiesRequiringCloneProjection(session, state),
        [
          "callback",
          "callbackStandardMap",
          "direct",
          "emptyObject",
          "nested",
          "opaqueAny",
          "opaqueObject",
          "opaqueUnknown",
          "shadowedMap",
        ],
      );
      const projection = structuredCloneProjectionTypes(
        session,
        source,
        text,
        "cloneSyntheticState",
      );
      const projectionEntries = [...projection]
        .filter(([name]) => name !== "plain");
      const unsafeTypes = projectionRequiredTypes(
        session,
        projectionEntries.map(([, type]) => type),
      );
      assert.deepEqual(
        projectionEntries
          .filter(([, type]) => unsafeTypes.has(type.handle))
          .map(([name]) => name)
          .sort(),
        [
          "callback",
          "callbackStandardMap",
          "direct",
          "emptyObject",
          "opaqueAny",
          "opaqueObject",
          "opaqueUnknown",
          "shadowedMap",
        ],
      );
    } finally {
      session.dispose();
    }

    const unavailableOpened = semanticFactsTestSeam.createHarness({
      missingApiFactOperation: "getCallSignatures",
    }).open(join(fixtureRoot, "tsconfig.json"));
    assert.equal(unavailableOpened.kind, "Opened");
    if (unavailableOpened.kind === "Opened") {
      const unavailableSession = unavailableOpened.session;
      try {
        const source = sourceByPath(unavailableSession, "state.ts");
        const text = readFileSync(join(fixtureRoot, "state.ts"), "utf8");
        const state = declaredTypeAlias(
          unavailableSession,
          source,
          text,
          "SyntheticState",
        );
        assert.throws(
          () => propertiesRequiringCloneProjection(unavailableSession, state),
          /"kind":"Unavailable"/,
        );
      } finally {
        unavailableSession.dispose();
      }
    }

    assert.throws(
      () => structuredClone({ value: ConstructorOnlyFixture }),
      { name: "DataCloneError" },
    );
  } finally {
    rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

class ConstructorOnlyFixture {
  readonly value = 0;
}

function propertiesRequiringCloneProjection(
  session: TypeScriptSemanticFactsSession,
  objectType: TypeFact,
): readonly string[] {
  const properties = resolved(session.getProperties(objectType.handle))
    .map(property => ({ property, type: symbolType(session, property) }))
    .filter((entry): entry is { property: SymbolFact; type: TypeFact } =>
      entry.type !== null);
  const unsafeTypes = projectionRequiredTypes(
    session,
    properties.map(entry => entry.type),
  );
  return properties
    .filter(entry => unsafeTypes.has(entry.type.handle))
    .map(entry => entry.property.displayName)
    .sort();
}

function projectionRequiredTypes(
  session: TypeScriptSemanticFactsSession,
  roots: readonly TypeFact[],
): ReadonlySet<TypeHandle> {
  const visited = new Set<TypeHandle>();
  const direct = new Set<TypeHandle>();
  const parents = new Map<TypeHandle, Set<TypeHandle>>();
  const pending = [...roots];

  while (pending.length > 0) {
    const type = pending.pop();
    assert.ok(type !== undefined);
    if (visited.has(type.handle)) continue;
    visited.add(type.handle);

    if (
      isOpaqueFunctionCarrier(type)
      || applicable(session.getCallSignatures(type.handle)).length > 0
      || applicable(session.getConstructSignatures(type.handle)).length > 0
    ) {
      direct.add(type.handle);
    }

    const children = [
      ...applicable(session.getUnionConstituents(type.handle)),
      ...applicable(session.getIntersectionConstituents(type.handle)),
      ...applicable(session.getBaseTypes(type.handle)),
      ...applicable(session.getTypeArguments(type.handle)),
      ...applicable(session.getIndexInfos(type.handle))
        .map(index => resolved(session.getType(index.valueType))),
    ];

    for (const property of applicable(session.getProperties(type.handle))) {
      // Default-library methods are prototype APIs, not stored state values.
      if (!hasInspectableDeclaration(session, property.declarations)) {
        continue;
      }
      const propertyType = symbolType(session, property);
      if (propertyType !== null) children.push(propertyType);
    }

    for (const child of children) {
      const childParents = parents.get(child.handle) ?? new Set<TypeHandle>();
      childParents.add(type.handle);
      parents.set(child.handle, childParents);
      pending.push(child);
    }
  }

  const reachable = new Set(direct);
  const queue = [...direct];
  while (queue.length > 0) {
    const child = queue.pop();
    assert.ok(child !== undefined);
    for (const parent of parents.get(child) ?? []) {
      if (reachable.has(parent)) continue;
      reachable.add(parent);
      queue.push(parent);
    }
  }
  return reachable;
}

function isOpaqueFunctionCarrier(type: TypeFact): boolean {
  // These types can accept a function while exposing no signatures of their own.
  return type.category === TypeCategory.Any
    || type.category === TypeCategory.Unknown
    || type.category === TypeCategory.NonPrimitive
    || type.category === TypeCategory.TypeParameter
    || (type.category === TypeCategory.Object
      && (type.display === "{}" || type.display === "Object"));
}

function symbolType(
  session: TypeScriptSemanticFactsSession,
  symbol: SymbolFact,
): TypeFact | null {
  const declaration = symbol.valueDeclaration ?? symbol.declarations[0];
  if (declaration === undefined) return null;
  return resolved(session.getSymbolTypeAtLocation(
    symbol.handle,
    resolved(session.getDeclaration(declaration)).node,
  ));
}

function hasInspectableDeclaration(
  session: TypeScriptSemanticFactsSession,
  declarations: readonly DeclarationHandle[],
): boolean {
  return declarations.some(declaration =>
    resolved(session.getDeclaration(declaration)).sourceFileClassification
      !== SourceFileClassification.DefaultLibrary);
}

function declaredTypeAlias(
  session: TypeScriptSemanticFactsSession,
  source: SourceFileFact,
  text: string,
  name: string,
): TypeFact {
  const nodes = resolved(session.getNodes(source.handle));
  const declaration = nodes.find(node =>
    node.kind === NodeKind.TypeAliasDeclaration
    && text.slice(node.location.start, node.location.start + node.location.length)
      .startsWith(`type ${name} =`));
  assert.ok(declaration !== undefined);
  const identifier = nodes.find(node =>
    node.kind === NodeKind.Identifier
    && node.spelling === name
    && node.parent === declaration.handle);
  assert.ok(identifier !== undefined);
  const symbol = resolved(session.getSymbolAtNode(identifier.handle));
  return resolved(session.getDeclaredType(symbol.handle));
}

function structuredCloneProjectionTypes(
  session: TypeScriptSemanticFactsSession,
  source: SourceFileFact,
  text: string,
  name: string,
): ReadonlyMap<string, TypeFact> {
  const nodes = resolved(session.getNodes(source.handle));
  const declaration = nodes.find(node =>
    node.kind === NodeKind.FunctionDeclaration
    && text.slice(node.location.start, node.location.start + node.location.length)
      .startsWith(`function ${name}(`));
  assert.ok(declaration !== undefined);
  const declarationEnd = declaration.location.start
    + declaration.location.length;
  const calls = nodes.filter(node =>
    node.kind === NodeKind.CallExpression
    && node.location.start >= declaration.location.start
    && node.location.start + node.location.length <= declarationEnd
    && text.slice(node.location.start, node.location.start + node.location.length)
      .startsWith("structuredClone({"));
  assert.equal(calls.length, 1);
  const call = calls[0];
  assert.ok(call !== undefined);
  const objectLiteral = call.children
    .map(handle => resolved(session.getNode(handle)))
    .find(node =>
      text.slice(node.location.start, node.location.start + node.location.length)
        .startsWith("{"));
  assert.ok(objectLiteral !== undefined);

  const projections = new Map<string, TypeFact>();
  for (const handle of objectLiteral.children) {
    const assignment = resolved(session.getNode(handle));
    const children = assignment.children.map(child =>
      resolved(session.getNode(child)));
    const property = children[0];
    const initializer = children.at(-1);
    if (
      property?.kind !== NodeKind.Identifier
      || property.spelling === undefined
      || initializer === undefined
      || initializer.handle === property.handle
    ) {
      continue;
    }
    projections.set(
      property.spelling,
      resolved(session.getTypeAtNode(initializer.handle)),
    );
  }
  return projections;
}

function sourceByPath(
  session: TypeScriptSemanticFactsSession,
  projectPath: string,
): SourceFileFact {
  const source = resolved(session.getSourceFiles()).find(candidate =>
    candidate.path.kind === "ProjectRelative"
    && candidate.path.path === projectPath);
  assert.ok(source !== undefined);
  return source;
}

function applicable<T>(result: QueryResult<readonly T[]>): readonly T[] {
  if (result.kind === "Resolved") return result.value;
  if (result.kind === "NotApplicable") return [];
  throw new Error(`TypeScript semantic query failed: ${JSON.stringify(result)}`);
}

function resolved<T>(result: QueryResult<T>): T {
  assert.equal(result.kind, "Resolved");
  if (result.kind !== "Resolved") {
    throw new Error("TypeScript semantic query was not resolved.");
  }
  return result.value;
}
