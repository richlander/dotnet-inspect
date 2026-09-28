import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import {
  NodeKind,
  SourceFileClassification,
  openTypeScriptSemanticFacts,
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
const callableStateFields = [
  "implementationProfiles",
  "platformIndex",
  "queryNoticeRetryAction",
  "retryAction",
  "typeHeat",
] as const;

test("retained Workspace clone projects every callable AppState field", () => {
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
    const actual = propertiesContainingCallables(session, appState);

    assert.deepEqual(actual, callableStateFields);

    const clone = functionSource(
      session,
      appSource,
      text,
      "cloneCanonicalWorkspaceSnapshotForRetention",
    );
    const projection = clone.match(
      /const cloned = structuredClone\(\{([\s\S]*?)\n  \}\);/,
    )?.[1];
    assert.ok(projection !== undefined);
    for (const field of callableStateFields) {
      assert.match(projection, new RegExp(`\\b${field}:`));
    }
  } finally {
    session.dispose();
  }
});

function propertiesContainingCallables(
  session: TypeScriptSemanticFactsSession,
  objectType: TypeFact,
): readonly string[] {
  const properties = resolved(session.getProperties(objectType.handle))
    .map(property => ({ property, type: symbolType(session, property) }))
    .filter((entry): entry is { property: SymbolFact; type: TypeFact } =>
      entry.type !== null);
  const callableTypes = callableReachableTypes(
    session,
    properties.map(entry => entry.type),
  );
  return properties
    .filter(entry => callableTypes.has(entry.type.handle))
    .map(entry => entry.property.displayName)
    .sort();
}

function callableReachableTypes(
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

    if (applicable(session.getCallSignatures(type.handle)).length > 0) {
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

    if (!isBuiltInContainer(type.display)) {
      for (const property of applicable(session.getProperties(type.handle))) {
        // Default-library methods are prototype APIs, not stored state values.
        if (!hasInspectableDeclaration(session, property.declarations)) {
          continue;
        }
        const propertyType = symbolType(session, property);
        if (propertyType !== null) children.push(propertyType);
      }
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

function isBuiltInContainer(display: string): boolean {
  return display.endsWith("[]")
    || display.startsWith("Array<")
    || display.startsWith("ReadonlyArray<")
    || display.startsWith("Map<")
    || display.startsWith("ReadonlyMap<")
    || display.startsWith("Set<")
    || display.startsWith("ReadonlySet<")
    || display === "Date"
    || display === "RegExp"
    || display === "ArrayBuffer"
    || display === "Uint8Array";
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

function functionSource(
  session: TypeScriptSemanticFactsSession,
  source: SourceFileFact,
  text: string,
  name: string,
): string {
  const declaration = resolved(session.getNodes(source.handle)).find(node =>
    node.kind === NodeKind.FunctionDeclaration
    && text.slice(node.location.start, node.location.start + node.location.length)
      .startsWith(`function ${name}(`));
  assert.ok(declaration !== undefined);
  return text.slice(
    declaration.location.start,
    declaration.location.start + declaration.location.length,
  );
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
  return result.kind === "Resolved" ? result.value : [];
}

function resolved<T>(result: QueryResult<T>): T {
  assert.equal(result.kind, "Resolved");
  if (result.kind !== "Resolved") {
    throw new Error("TypeScript semantic query was not resolved.");
  }
  return result.value;
}
