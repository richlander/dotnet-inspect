import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import {
  existsSync,
  mkdtempSync,
  mkdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import {
  dirname,
  join,
  relative,
  sep,
} from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import test, { after, before } from "node:test";
import {
  DeclarationHandle,
  NodeHandle,
  NodeKind,
  ObjectTypeCategory,
  SignatureHandle,
  SourceFileClassification,
  SourceFileHandle,
  SymbolCategory,
  SymbolHandle,
  TypeCategory,
  TypeHandle,
  TypePredicateCategory,
  computeSourceContentId,
  isBigIntLiteralTypeFact,
  isBooleanLiteralTypeFact,
  isClassOrInterfaceTypeFact,
  isConditionalTypeFact,
  isErrorTypeFact,
  isIndexTypeFact,
  isIndexedAccessTypeFact,
  isIntersectionTypeFact,
  isIntrinsicTypeFact,
  isLiteralTypeFact,
  isNumberLiteralTypeFact,
  isObjectTypeFact,
  isStringLiteralTypeFact,
  isStringMappingTypeFact,
  isSubstitutionTypeFact,
  isTemplateLiteralTypeFact,
  isTupleTypeFact,
  isTypeParameterFact,
  isTypeReferenceTypeFact,
  isUnionTypeFact,
  openTypeScriptSemanticFacts,
  queryApplicability,
  semanticFactsTestSeam,
  type NodeFact,
  type OpenTypeScriptSemanticFactsResult,
  type QueryResult,
  type SemanticHandle,
  type SourceFileFact,
  type SymbolFact,
  type TypeFact,
  type TypeScriptSemanticFactsSession,
} from "../scripts/typescript-semantic-facts.ts";
import {
  javaScriptSourceExtensions,
  projectSourceFiles,
  typeScriptSourceExtensions,
} from "./project-source-inventory.ts";

const inspectWebRoot = fileURLToPath(new URL("../", import.meta.url));
const realTsconfig = join(inspectWebRoot, "tsconfig.json");
const fixtureRoot = mkdtempSync(join(tmpdir(), "dotnet-inspect-semantic-facts-"));
const fixtureTsconfig = join(fixtureRoot, "tsconfig.json");
const badTsconfig = join(fixtureRoot, "bad.tsconfig.json");
const unstablePackagePrefix = "typescript/unstable/";

const coordinateSource
  = "// 😀 leading trivia\r\n"
    + "   const coordinateValue = \"😀\";\r\n"
    + "export { coordinateValue };\r\n";

const fixtureFiles = Object.freeze({
  "base.ts": `
export interface Shared {
  label: string;
}

export interface Cycle {
  next?: Cycle;
  left: Shared;
  right: Shared;
}

export interface Base {
  base: string;
}

export interface Derived extends Base {
  derived: number;
}

export interface Dictionary {
  readonly [key: string]: number;
}

export type UnionAlias = string | number;
export type IntersectionAlias = { left: string } & { right: number };
export type LiteralAlias = "literal";
export type NumberLiteralAlias = 42;
export type BigIntLiteralAlias = 42n;
export type BooleanAlias = boolean;
export type BooleanLiteralAlias = true;
export type TupleAlias = [string, number];
export type Constrained<T extends Base> = T;
export type Key<T> = keyof T;
export type Indexed<T extends object, K extends keyof T> = T[K];
export type Conditional<T> = T extends string ? "text" : "other";
export type Template<T extends string> = \`prefix-\${T}\`;
export type Mapping<T extends string> = Uppercase<T>;
export type Mapped<T> = { [K in keyof T]: T[K] };
export type Callback = (callbackValue: string) => number;
export type AnonymousShape = { nestedField: string };
export const AnonymousClass = class { anonymousField = 1; };

export function identity<T>(value: T): T {
  return value;
}

export function overloaded(value: string): string;
export function overloaded(value: number): number;
export function overloaded(value: string | number): string | number {
  return value;
}

export function hasText(value: unknown): value is string {
  return typeof value === "string";
}

export function assertText(value: unknown): asserts value is string {
  if (typeof value !== "string") {
    throw new TypeError("text required");
  }
}

export function withThis(this: { prefix: string }, ...values: string[]): string {
  return this.prefix + values.join(",");
}

export enum Choice {
  One = 1,
  Two = 2,
}

export const namedConstant = Choice.Two;
`,
  "ambient.d.ts": `
export interface Ambient {
  readonly value: string;
}
`,
  "script.ts": `
const scriptOnly = 1;
void scriptOnly;
`,
  "dynamic.ts": `
export const dynamicValue = 1;
`,
  "entry.ts": `
import {
  Choice,
  type Conditional,
  type Constrained,
  type Cycle as ImportedCycle,
  type Derived,
  type Dictionary,
  type Indexed,
  type IntersectionAlias,
  type LiteralAlias,
  type Mapped,
  type Template,
  type UnionAlias,
  assertText,
  hasText,
  identity as genericIdentity,
  overloaded,
  withThis,
} from "./base.js";
import type { Ambient } from "./ambient.js";
import type { UserConfig } from "vite";
import "./script.js";

export { genericIdentity as exportedIdentity };

const shadow = 1;
function readShadow(): string {
  const shadow = "local";
  return shadow;
}

export const shorthandSource = { shadow };
export const contextual: (value: number) => string = value => value.toString();
export const direct = genericIdentity({ value: 42 });
export const selectedGeneric = genericIdentity(42);
export const selectedOverload = overloaded("selected");
export const cycle: ImportedCycle = {
  left: { label: "left" },
  right: { label: "right" },
};
export let unionValue: UnionAlias = Math.random() > 0.5 ? "text" : 1;
if (typeof unionValue === "string") {
  unionValue.toUpperCase();
}
export const intersectionValue: IntersectionAlias = { left: "left", right: 1 };
export const literalValue: LiteralAlias = "literal";
export const derivedValue: Derived = { base: "base", derived: 1 };
export const dictionaryValue: Dictionary = { answer: 42 };
export const readonlyValues: ReadonlyArray<string> = ["one"];
export const enumConstant = Choice.Two;
export const nonConstant = cycle.left.label;
export const ambientValue: Ambient = { value: "ambient" };
export const viteConfig: UserConfig = {};
export const constrainedValue: Constrained<Derived> = derivedValue;
export const indexedValue: Indexed<Derived, "derived"> = 1;
export const conditionalValue: Conditional<string> = "text";
export const templateValue: Template<"value"> = "prefix-value";
export const mappedValue: Mapped<Derived> = derivedValue;
export const constructedDate = new Date();

export function usePredicates(value: unknown): string {
  if (hasText(value)) {
    return value.toUpperCase();
  }
  assertText(value);
  return value.toUpperCase();
}

export const withThisResult = withThis.call({ prefix: "prefix:" }, "one", "two");

export async function loadDynamically(name: string): Promise<unknown> {
  return import(name);
}

export async function loadStaticDynamically(): Promise<unknown> {
  return import("./dynamic.js");
}

void readShadow;
`,
  "coordinates.ts": coordinateSource,
  "bad.ts": `
const badValue: string = 1;
export const unresolvedValue = doesNotExist;
export { missingExport } from "./missing.js";
export { badValue };
`,
});

function writeFixture(): void {
  for (const [name, content] of Object.entries(fixtureFiles)) {
    writeFileSync(join(fixtureRoot, name), content, "utf8");
  }
  writeFileSync(fixtureTsconfig, JSON.stringify({
    compilerOptions: {
      lib: ["DOM", "ES2022"],
      module: "NodeNext",
      moduleDetection: "legacy",
      moduleResolution: "NodeNext",
      outDir: "./out",
      paths: {
        vite: [join(inspectWebRoot, "node_modules", "vite", "dist", "node", "index.d.ts")],
      },
      skipLibCheck: true,
      strict: true,
      target: "ES2022",
      typeRoots: [join(inspectWebRoot, "node_modules", "@types")],
      types: ["node"],
    },
    files: ["entry.ts", "coordinates.ts"],
  }, undefined, 2), "utf8");
  writeFileSync(badTsconfig, JSON.stringify({
    compilerOptions: {
      module: "NodeNext",
      moduleResolution: "NodeNext",
      noEmit: true,
      strict: true,
      target: "ES2022",
    },
    files: ["bad.ts"],
  }, undefined, 2), "utf8");

  const tsc = join(inspectWebRoot, "node_modules", "typescript", "bin", "tsc");
  execFileSync(process.execPath, [tsc, "--project", fixtureTsconfig], {
    cwd: inspectWebRoot,
    stdio: "inherit",
  });
  assert.ok(existsSync(join(fixtureRoot, "out", "entry.js")));
}

before(writeFixture);
after(() => rmSync(fixtureRoot, { recursive: true, force: true }));

function expectOpened(result: OpenTypeScriptSemanticFactsResult): TypeScriptSemanticFactsSession {
  if (result.kind !== "Opened") {
    assert.fail(`expected Opened, received ${JSON.stringify(result)}`);
  }
  return result.session;
}

function expectResolved<T>(result: QueryResult<T>): T {
  if (result.kind !== "Resolved") {
    assert.fail(`expected Resolved, received ${JSON.stringify(result)}`);
  }
  return result.value;
}

function sourceByPath(
  session: TypeScriptSemanticFactsSession,
  projectPath: string,
): SourceFileFact {
  const sources = expectResolved(session.getSourceFiles());
  const source = sources.find(candidate =>
    candidate.path.kind === "ProjectRelative"
    && candidate.path.path === projectPath);
  assert.ok(source !== undefined, `missing source '${projectPath}'`);
  return source;
}

function sourceText(source: SourceFileFact): string {
  assert.equal(source.path.kind, "ProjectRelative");
  if (source.path.kind !== "ProjectRelative") {
    assert.fail("fixture source was unexpectedly external");
  }
  return readFileSync(join(fixtureRoot, source.path.path), "utf8");
}

function nodeText(node: NodeFact, text: string): string {
  return text.slice(
    node.location.start,
    node.location.start + node.location.length,
  );
}

function nodesFor(
  session: TypeScriptSemanticFactsSession,
  source: SourceFileFact,
): readonly NodeFact[] {
  return expectResolved(session.getNodes(source.handle));
}

function oneNode(
  nodes: readonly NodeFact[],
  text: string,
  kind: NodeFact["kind"],
  exactText: string,
): NodeFact {
  const matches = nodes.filter(node =>
    node.kind === kind && nodeText(node, text) === exactText);
  assert.equal(
    matches.length,
    1,
    `expected one ${kind} '${exactText}', found ${matches.length}`,
  );
  const match = matches[0];
  assert.ok(match !== undefined);
  return match;
}

function identifierNodes(
  nodes: readonly NodeFact[],
  spelling: string,
): readonly NodeFact[] {
  return nodes.filter(node =>
    node.kind === NodeKind.Identifier && node.spelling === spelling);
}

function symbolAt(
  session: TypeScriptSemanticFactsSession,
  node: NodeFact,
): SymbolFact {
  return expectResolved(session.getSymbolAtNode(node.handle));
}

function valueType(
  session: TypeScriptSemanticFactsSession,
  symbol: SymbolFact,
): TypeFact {
  return expectResolved(session.getSymbolValueType(symbol.handle));
}

test("opens the real inspect-web project and preserves DOM overload provenance", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(realTsconfig));
  try {
    const sources = expectResolved(session.getSourceFiles());
    const roots = sources.filter(source =>
      source.classification === SourceFileClassification.ProjectRoot);
    assert.ok(roots.length > 40);
    assert.ok(sources.some(source =>
      source.classification === SourceFileClassification.DefaultLibrary));

    const shell = sourceByPath(session, "src/shell-controls.ts");
    const text = readFileSync(join(inspectWebRoot, "src", "shell-controls.ts"), "utf8");
    const nodes = nodesFor(session, shell);
    const firstQuerySelector =
      text.indexOf('root.querySelector("#retry-notice")');
    const property = nodes.find(node =>
      node.kind === NodeKind.PropertyAccessExpression
      && node.location.start === firstQuerySelector
      && nodeText(node, text) === "root.querySelector");
    assert.ok(property !== undefined);
    const symbol = symbolAt(session, property);
    assert.ok(symbol.categories.includes(SymbolCategory.Method));
    const declarations = symbol.declarations.map(handle =>
      expectResolved(session.getDeclaration(handle)));
    assert.ok(declarations.length > 1);
    assert.ok(declarations.every(declaration =>
      declaration.sourceFileClassification === SourceFileClassification.DefaultLibrary));
    assert.ok(declarations.some(declaration =>
      declaration.containingDeclarations.length > 0));

    const propertyType = expectResolved(session.getTypeAtNode(property.handle));
    const candidates = expectResolved(session.getCallSignatures(propertyType.handle));
    assert.ok(candidates.length > 1);

    const call = oneNode(
      nodes,
      text,
      NodeKind.CallExpression,
      'root.querySelector("#retry-notice")',
    );
    const selected = expectResolved(session.getResolvedSignature(call.handle));
    assert.equal(selected.category, "Call");
    assert.equal(selected.parameters.length, 1);
  } finally {
    assert.equal(session.dispose().kind, "Disposed");
  }
});

test("batched semantic queries safely cover every real project-root node", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(realTsconfig));
  try {
    const source = sourceByPath(session, "src/annotated-source-view.ts");
    const text = readFileSync(
      join(inspectWebRoot, "src", "annotated-source-view.ts"),
      "utf8",
    );
    const nodes = nodesFor(session, source);
    let swept = 0;
    let jsDocNodes = 0;
    const sources = expectResolved(session.getSourceFiles());
    const roots = sources.filter(candidate =>
      candidate.classification === SourceFileClassification.ProjectRoot);
    for (const root of roots) {
      const rootNodes = nodesFor(session, root);
      const handles = rootNodes.map(node => node.handle);
      const symbols = expectResolved(session.getSymbolsAtNodes(handles));
      const types = expectResolved(session.getTypesAtNodes(handles));
      assert.equal(symbols.length, handles.length);
      assert.equal(types.length, handles.length);
      assert.ok(symbols.every(result => result.kind !== "SessionFailure"));
      assert.ok(types.every(result => result.kind !== "SessionFailure"));
      swept += handles.length;
      jsDocNodes += rootNodes.filter(node => node.kind === NodeKind.JsDoc).length;
    }
    assert.ok(swept > 100_000, `expected a project-wide sweep, observed ${swept} nodes`);
    assert.ok(jsDocNodes > 0, "the project-wide sweep included no attached JSDoc nodes");
    const domLibrary = sources.find(candidate =>
      candidate.path.path.endsWith("/lib.dom.d.ts"));
    assert.ok(domLibrary !== undefined);
    assert.ok(
      nodesFor(session, domLibrary).some(node => node.kind === NodeKind.JsDoc),
      "the default-library tree included no attached JSDoc nodes",
    );

    const lineStart = text.indexOf(".map(line =>")
      + ".map(".length;
    const lineName = nodes.find(node =>
      node.kind === NodeKind.Identifier
      && node.location.start === lineStart);
    assert.ok(lineName !== undefined);
    const lineSymbol = symbolAt(session, lineName);
    const lineDeclaration = expectResolved(session.getDeclaration(
      lineSymbol.declarations[0]
        ?? assert.fail("line parameter symbol had no declaration"),
    ));
    const arrowContainer = expectResolved(session.getDeclaration(
      lineDeclaration.containingDeclarations[0]
        ?? assert.fail("line parameter had no containing declaration"),
    ));
    assert.equal(arrowContainer.kind, NodeKind.ArrowFunction);
  } finally {
    session.dispose();
  }
});

test("opens a compiled fixture and classifies roots, imports, declarations, and libraries", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  try {
    const sources = expectResolved(session.getSourceFiles());
    assert.equal(
      sourceByPath(session, "entry.ts").classification,
      SourceFileClassification.ProjectRoot,
    );
    assert.equal(
      sourceByPath(session, "coordinates.ts").classification,
      SourceFileClassification.ProjectRoot,
    );
    assert.equal(
      sourceByPath(session, "base.ts").classification,
      SourceFileClassification.ImportedProject,
    );
    assert.equal(
      sourceByPath(session, "ambient.d.ts").classification,
      SourceFileClassification.OtherDeclaration,
    );
    assert.ok(sources.some(source =>
      source.classification === SourceFileClassification.DefaultLibrary));
    assert.ok(sources.some(source =>
      source.classification === SourceFileClassification.ExternalLibrary
      && source.path.kind === "External"));
    assert.ok(sources.every(source =>
      source.contentId.algorithm === "SHA-256"
      && source.contentId.encoding === "UTF-16LECodeUnits"
      && source.contentId.hex.length === 64));
  } finally {
    session.dispose();
  }
});

test("rejects invalid opening inputs, ambiguous selection, and strict diagnostics", () => {
  assert.deepEqual(openTypeScriptSemanticFacts("tsconfig.json"), {
    kind: "InvalidInput",
    reason: "RelativePath",
    cleanupFailures: [],
  });
  assert.deepEqual(openTypeScriptSemanticFacts(fixtureRoot), {
    kind: "InvalidInput",
    reason: "DirectoryPath",
    cleanupFailures: [],
  });
  assert.deepEqual(openTypeScriptSemanticFacts(pathToFileURL(fixtureTsconfig).href), {
    kind: "InvalidInput",
    reason: "FileUrl",
    cleanupFailures: [],
  });
  assert.deepEqual(openTypeScriptSemanticFacts(
    pathToFileURL(fixtureTsconfig).href.replace("file:", "FILE:"),
  ), {
    kind: "InvalidInput",
    reason: "FileUrl",
    cleanupFailures: [],
  });
  assert.deepEqual(openTypeScriptSemanticFacts(join(fixtureRoot, "missing.json")), {
    kind: "InvalidInput",
    reason: "MissingPath",
    cleanupFailures: [],
  });

  const ambiguousHarness = semanticFactsTestSeam.createHarness({
    projectSelection: "MultipleProjects",
  });
  const ambiguous = ambiguousHarness.open(fixtureTsconfig);
  assert.equal(ambiguous.kind, "ProjectSelectionFailed");
  if (ambiguous.kind === "ProjectSelectionFailed") {
    assert.equal(ambiguous.reason, "MultipleProjects");
  }
  assert.deepEqual(ambiguousHarness.observation(), {
    apiCreated: 1,
    snapshotCreated: 1,
    snapshotDisposeCalls: 1,
    apiCloseCalls: 1,
  });

  const diagnosticsHarness = semanticFactsTestSeam.createHarness({});
  const diagnostics = diagnosticsHarness.open(badTsconfig);
  assert.equal(diagnostics.kind, "DiagnosticsRejected");
  if (diagnostics.kind === "DiagnosticsRejected") {
    assert.equal(diagnostics.phase, "Semantic");
    assert.ok(diagnostics.diagnostics.some(diagnostic =>
      diagnostic.code === 2322 && diagnostic.category === "Error"));
  }
  assert.deepEqual(diagnosticsHarness.observation(), {
    apiCreated: 1,
    snapshotCreated: 1,
    snapshotDisposeCalls: 1,
    apiCloseCalls: 1,
  });
});

test("handles are session-scoped, kind-checked, terminal-first, and disposal is latched", () => {
  const first = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  const second = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  const entry = sourceByPath(first, "entry.ts");
  const firstNode = identifierNodes(nodesFor(first, entry), "cycle")[0];
  assert.ok(firstNode !== undefined);

  assert.deepEqual(first.getNode(entry.handle), {
    kind: "InvalidHandle",
    reason: "WrongKind",
  });
  assert.deepEqual(second.getNode(firstNode.handle), {
    kind: "InvalidHandle",
    reason: "StaleSession",
  });
  const secondEntry = sourceByPath(second, "entry.ts");
  const secondNode = expectResolved(second.getNodes(secondEntry.handle))[0];
  assert.ok(secondNode !== undefined);
  const symbolBatch = expectResolved(first.getSymbolsAtNodes([
    entry.handle,
    firstNode.handle,
    secondNode.handle,
  ]));
  assert.deepEqual(symbolBatch[0], { kind: "InvalidHandle", reason: "WrongKind" });
  assert.strictEqual(
    expectResolved(symbolBatch[1] ?? assert.fail("missing symbol batch result")).handle,
    expectResolved(first.getSymbolAtNode(firstNode.handle)).handle,
  );
  assert.deepEqual(symbolBatch[2], { kind: "InvalidHandle", reason: "StaleSession" });
  const typeBatch = expectResolved(first.getTypesAtNodes([
    entry.handle,
    firstNode.handle,
    secondNode.handle,
  ]));
  assert.deepEqual(typeBatch[0], { kind: "InvalidHandle", reason: "WrongKind" });
  assert.strictEqual(
    expectResolved(typeBatch[1] ?? assert.fail("missing type batch result")).handle,
    expectResolved(first.getTypeAtNode(firstNode.handle)).handle,
  );
  assert.deepEqual(typeBatch[2], { kind: "InvalidHandle", reason: "StaleSession" });

  const disposed = first.dispose();
  assert.equal(disposed.kind, "Disposed");
  assert.strictEqual(first.dispose(), disposed);
  const secondSources = second.getSourceFiles();
  assert.deepEqual(first.getNode(secondSources.kind === "Resolved"
    ? secondSources.value[0]?.handle ?? entry.handle
    : entry.handle), {
    kind: "SessionFailure",
    reason: "SessionDisposed",
    detail: "the semantic-facts session has been disposed",
  });
  second.dispose();
});

test("each session owns one immutable snapshot and a new session observes source changes", () => {
  const coordinatePath = join(fixtureRoot, "coordinates.ts");
  const first = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  let second: TypeScriptSemanticFactsSession | undefined;
  try {
    const firstSource = sourceByPath(first, "coordinates.ts");
    const firstNodes = expectResolved(first.getNodes(firstSource.handle));
    const repeatedNodes = expectResolved(first.getNodes(firstSource.handle));
    assert.equal(repeatedNodes.length, firstNodes.length);
    for (const [index, node] of repeatedNodes.entries()) {
      assert.strictEqual(node.handle, firstNodes[index]?.handle);
    }

    writeFileSync(coordinatePath, `${coordinateSource}// changed after snapshot\r\n`, "utf8");
    second = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
    const secondSource = sourceByPath(second, "coordinates.ts");
    assert.notEqual(secondSource.handle, firstSource.handle);
    assert.notEqual(secondSource.contentId.hex, firstSource.contentId.hex);
    assert.equal(
      expectResolved(first.getSourceFile(firstSource.handle)).contentId.hex,
      firstSource.contentId.hex,
    );
  } finally {
    writeFileSync(coordinatePath, coordinateSource, "utf8");
    second?.dispose();
    first.dispose();
  }
});

test("snapshot release failure still closes the API and repeats the latched failure", () => {
  const harness = semanticFactsTestSeam.createHarness({
    snapshotReleaseFailure: "injected snapshot release failure",
  });
  const session = expectOpened(harness.open(fixtureTsconfig));
  const first = session.dispose();
  assert.deepEqual(first, {
    kind: "DisposeFailed",
    failures: [{
      kind: "SnapshotReleaseFailure",
      detail: "injected snapshot release failure",
    }],
  });
  assert.strictEqual(session.dispose(), first);
  assert.deepEqual(harness.observation(), {
    apiCreated: 1,
    snapshotCreated: 1,
    snapshotDisposeCalls: 1,
    apiCloseCalls: 1,
  });
});

test("failed-open cleanup preserves the primary process, protocol, and compatibility result", () => {
  const beforeApi = semanticFactsTestSeam.createHarness({
    openFailure: {
      at: "BeforeApi",
      reason: "ProcessFailure",
      detail: "injected process start failure",
    },
  });
  assert.deepEqual(beforeApi.open(fixtureTsconfig), {
    kind: "InfrastructureFailed",
    reason: "ProcessFailure",
    detail: "injected process start failure",
    cleanupFailures: [],
  });
  assert.deepEqual(beforeApi.observation(), {
    apiCreated: 0,
    snapshotCreated: 0,
    snapshotDisposeCalls: 0,
    apiCloseCalls: 0,
  });

  const afterSnapshot = semanticFactsTestSeam.createHarness({
    openFailure: {
      at: "AfterSnapshot",
      reason: "ProtocolFailure",
      detail: "injected protocol failure",
    },
    snapshotReleaseFailure: "release also failed",
  });
  assert.deepEqual(afterSnapshot.open(fixtureTsconfig), {
    kind: "InfrastructureFailed",
    reason: "ProtocolFailure",
    detail: "injected protocol failure",
    cleanupFailures: [{
      kind: "SnapshotReleaseFailure",
      detail: "release also failed",
    }],
  });
  assert.deepEqual(afterSnapshot.observation(), {
    apiCreated: 1,
    snapshotCreated: 1,
    snapshotDisposeCalls: 1,
    apiCloseCalls: 1,
  });

  const unsupported = semanticFactsTestSeam.createHarness({
    unsupportedResponseShapeAfterSnapshot: "injected response shape",
  });
  const unsupportedResult = unsupported.open(fixtureTsconfig);
  assert.equal(unsupportedResult.kind, "UnsupportedApi");
  if (unsupportedResult.kind === "UnsupportedApi") {
    assert.equal(unsupportedResult.reason, "UnsupportedResponseShape");
  }
  assert.equal(unsupported.observation().apiCloseCalls, 1);
});

test("content identity is lossless for UTF-16 code units including unpaired surrogates", () => {
  assert.equal(
    computeSourceContentId("\ud800").hex,
    "205022e3428b7c8276cf247b36e4e512db5651e5cb3472c253d9ee893a8ac750",
  );
  assert.equal(
    computeSourceContentId("\ud801").hex,
    "4a9868967003d43ddf0f042f7746934a6e27d3464b9b32ac9d93bab42b295696",
  );
  assert.equal(
    computeSourceContentId("A\ud800B").hex,
    "44d513da45ca5e9559e715a1f5664e65c78594b8ecb7544ae02d3a85c3ef99db",
  );
});

test("coordinates use trivia-free half-open UTF-16 spans across CRLF and non-BMP text", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  try {
    const source = sourceByPath(session, "coordinates.ts");
    const nodes = nodesFor(session, source);
    const identifier = nodes.find(node =>
      node.kind === NodeKind.Identifier
      && node.location.start === coordinateSource.indexOf("coordinateValue"));
    assert.ok(identifier !== undefined);
    assert.equal(identifier.location.start, coordinateSource.indexOf("coordinateValue"));
    assert.equal(identifier.location.length, "coordinateValue".length);
    assert.equal(identifier.location.line, 1);
    assert.equal(identifier.location.column, 9);
    const utf8ByteOffset = Buffer.byteLength(
      coordinateSource.slice(0, identifier.location.start),
      "utf8",
    );
    assert.notEqual(utf8ByteOffset, identifier.location.start);

    const statement = nodes.find(node =>
      node.kind === NodeKind.Statement
      && nodeText(node, coordinateSource).startsWith("const coordinateValue"));
    assert.ok(statement !== undefined);
    assert.equal(statement.location.start, coordinateSource.indexOf("const coordinateValue"));

    assert.deepEqual(session.correlateNode(source.handle, {
      contentId: source.contentId,
      start: identifier.location.start,
      length: identifier.location.length,
      expectedKind: NodeKind.Identifier,
    }), {
      kind: "Resolved",
      value: identifier,
    });
    assert.equal(session.correlateNode(source.handle, {
      contentId: source.contentId,
      start: identifier.location.start,
      length: identifier.location.length - 1,
      expectedKind: NodeKind.Identifier,
    }).kind, "Absent");
    assert.deepEqual(session.correlateNode(source.handle, {
      contentId: computeSourceContentId(
        coordinateSource.replace("coordinateValue", "coordinateValuf"),
      ),
      start: -1,
      length: identifier.location.length,
      expectedKind: NodeKind.Identifier,
    }), {
      kind: "InvalidCoordinate",
      reason: "SourceContentMismatch",
    });
    assert.deepEqual(session.correlateNode(source.handle, {
      contentId: source.contentId,
      start: -1,
      length: 1,
      expectedKind: NodeKind.Identifier,
    }), {
      kind: "InvalidCoordinate",
      reason: "OutOfRange",
    });
  } finally {
    session.dispose();
  }
});

test("coordinate ambiguity remains distinct from absence", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(realTsconfig));
  try {
    let source: SourceFileFact | undefined;
    let ambiguous: NodeFact[] | undefined;
    for (const candidateSource of expectResolved(session.getSourceFiles())) {
      const groups = new Map<string, NodeFact[]>();
      for (const node of nodesFor(session, candidateSource)) {
        const key = `${node.location.start}:${node.location.length}:${node.kind}`;
        const group = groups.get(key) ?? [];
        group.push(node);
        groups.set(key, group);
      }
      ambiguous = [...groups.values()].find(group => group.length > 1);
      if (ambiguous !== undefined) {
        source = candidateSource;
        break;
      }
    }
    assert.ok(source !== undefined);
    assert.ok(ambiguous !== undefined);
    const candidate = ambiguous[0];
    assert.ok(candidate !== undefined);
    const result = session.correlateNode(source.handle, {
      contentId: source.contentId,
      start: candidate.location.start,
      length: candidate.location.length,
      expectedKind: candidate.kind,
    });
    assert.equal(result.kind, "Ambiguous");
    if (result.kind === "Ambiguous") {
      assert.equal(result.candidates.length, ambiguous.length);
      assert.equal(new Set(result.candidates.map(node => node.handle)).size, ambiguous.length);
    }
  } finally {
    session.dispose();
  }
});

test("normalizes nodes, symbols, aliases, lexical shadows, and declaration provenance", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  try {
    const entry = sourceByPath(session, "entry.ts");
    const text = sourceText(entry);
    const nodes = nodesFor(session, entry);

    assert.ok(entry.handle instanceof SourceFileHandle);
    const genericAliases = identifierNodes(nodes, "genericIdentity")
      .map(node => ({ node, symbol: session.getSymbolAtNode(node.handle) }))
      .filter(candidate =>
        candidate.symbol.kind === "Resolved"
        && candidate.symbol.value.categories.includes(SymbolCategory.Alias));
    assert.ok(genericAliases.length > 0);
    const alias = genericAliases[0];
    assert.ok(alias !== undefined && alias.symbol.kind === "Resolved");
    assert.ok(alias.node.handle instanceof NodeHandle);
    assert.ok(alias.symbol.value.handle instanceof SymbolHandle);
    const chain = expectResolved(session.getAliasChain(alias.symbol.value.handle));
    assert.ok(chain.steps.length >= 1);
    assert.equal(chain.steps[0]?.alias, alias.symbol.value.handle);
    const original = expectResolved(session.getSymbol(chain.original));
    assert.equal(original.displayName, "identity");
    const originalDeclaration = original.declarations[0];
    assert.ok(originalDeclaration instanceof DeclarationHandle);
    const declaration = expectResolved(session.getDeclaration(originalDeclaration));
    assert.equal(declaration.sourceFileClassification, SourceFileClassification.ImportedProject);

    const shadows = identifierNodes(nodes, "shadow")
      .map(node => symbolAt(session, node));
    const uniqueShadows = new Set(shadows.map(symbol => symbol.handle));
    assert.ok(uniqueShadows.size >= 2);

    const shorthand = oneNode(
      nodes,
      text,
      NodeKind.ShorthandPropertyAssignment,
      "shadow",
    );
    const shorthandSource = expectResolved(session.getSourceSymbol(shorthand.handle));
    assert.ok(uniqueShadows.has(shorthandSource.handle));

    const exportSpecifier = oneNode(
      nodes,
      text,
      NodeKind.ExportSpecifier,
      "genericIdentity as exportedIdentity",
    );
    const exportSource = expectResolved(session.getSourceSymbol(exportSpecifier.handle));
    assert.equal(exportSource.handle, alias.symbol.value.handle);

    assert.deepEqual(session.getSourceSymbol(alias.node.handle), {
      kind: "NotApplicable",
      expectedSubject: queryApplicability.getSourceSymbol,
      actualSubject: NodeKind.Identifier,
    });
  } finally {
    session.dispose();
  }
});

test("declaration ancestry preserves anonymous and import boundaries", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  try {
    const base = sourceByPath(session, "base.ts");
    const baseNodes = nodesFor(session, base);
    const entry = sourceByPath(session, "entry.ts");
    const entryNodes = nodesFor(session, entry);
    const entryText = sourceText(entry);
    const containingKind = (
      nodes: readonly NodeFact[],
      spelling: string,
    ): NodeFact["kind"] => {
      const name = identifierNodes(nodes, spelling)[0];
      assert.ok(name !== undefined, `missing declaration name '${spelling}'`);
      const symbol = symbolAt(session, name);
      const declaration = expectResolved(session.getDeclaration(
        symbol.declarations[0]
          ?? assert.fail(`symbol '${spelling}' had no declaration`),
      ));
      const container = expectResolved(session.getDeclaration(
        declaration.containingDeclarations[0]
          ?? assert.fail(`declaration '${spelling}' had no container`),
      ));
      return container.kind;
    };

    assert.equal(
      containingKind(baseNodes, "callbackValue"),
      NodeKind.FunctionType,
    );
    assert.equal(
      containingKind(baseNodes, "nestedField"),
      NodeKind.TypeLiteral,
    );
    assert.equal(
      containingKind(baseNodes, "anonymousField"),
      NodeKind.ClassExpression,
    );
    assert.equal(
      containingKind(entryNodes, "genericIdentity"),
      NodeKind.ImportClause,
    );
    const typeOnlyClause = entryNodes.find(node =>
      node.kind === NodeKind.ImportClause
      && nodeText(node, entryText).startsWith("type {"));
    assert.ok(typeOnlyClause !== undefined);
    const unavailableType = {
      kind: "Unavailable",
      reason: "MissingApiFact",
      detail: "TypeScript 7.0.2 cannot type a type-only import clause",
    } as const;
    assert.deepEqual(session.getTypeAtNode(typeOnlyClause.handle), unavailableType);
    assert.deepEqual(
      expectResolved(session.getTypesAtNodes([typeOnlyClause.handle]))[0],
      unavailableType,
    );
  } finally {
    session.dispose();
  }
});

test("preserves direct, contextual, declared, narrowed, cyclic, and shared type identity", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  try {
    const entry = sourceByPath(session, "entry.ts");
    const text = sourceText(entry);
    const nodes = nodesFor(session, entry);
    const base = sourceByPath(session, "base.ts");
    const baseText = sourceText(base);
    const baseNodes = nodesFor(session, base);

    const arrow = oneNode(
      nodes,
      text,
      NodeKind.ArrowFunction,
      "value => value.toString()",
    );
    const contextual = expectResolved(session.getContextualType(arrow.handle));
    assert.ok(isObjectTypeFact(contextual));
    assert.equal(expectResolved(session.getCallSignatures(contextual.handle)).length, 1);

    const directCall = oneNode(
      nodes,
      text,
      NodeKind.CallExpression,
      "genericIdentity({ value: 42 })",
    );
    const direct = expectResolved(session.getTypeAtNode(directCall.handle));
    assert.ok(isObjectTypeFact(direct));
    assert.ok(expectResolved(session.getProperty(direct.handle, "value"))
      .categories.includes(SymbolCategory.Property));

    const unionDeclaration = oneNode(
      baseNodes,
      baseText,
      NodeKind.Identifier,
      "UnionAlias",
    );
    const unionSymbol = symbolAt(session, unionDeclaration);
    assert.equal(unionSymbol.valueDeclaration, undefined);
    assert.equal(session.getSymbolValueType(unionSymbol.handle).kind, "Absent");
    const union = expectResolved(session.getDeclaredType(unionSymbol.handle));
    assert.ok(isUnionTypeFact(union));
    assert.deepEqual(
      new Set(expectResolved(session.getUnionConstituents(union.handle))
        .map(type => type.category)),
      new Set([TypeCategory.String, TypeCategory.Number]),
    );

    const intersectionDeclaration = oneNode(
      baseNodes,
      baseText,
      NodeKind.Identifier,
      "IntersectionAlias",
    );
    const intersection = expectResolved(session.getDeclaredType(
      symbolAt(session, intersectionDeclaration).handle,
    ));
    assert.ok(isIntersectionTypeFact(intersection));
    assert.equal(
      expectResolved(session.getIntersectionConstituents(intersection.handle)).length,
      2,
    );

    const literalDeclaration = oneNode(
      baseNodes,
      baseText,
      NodeKind.Identifier,
      "LiteralAlias",
    );
    const literal = expectResolved(session.getDeclaredType(
      symbolAt(session, literalDeclaration).handle,
    ));
    assert.ok(isStringLiteralTypeFact(literal));
    assert.ok(isLiteralTypeFact(literal));
    assert.equal(
      expectResolved(session.getLiteralBaseType(literal.handle)).category,
      TypeCategory.String,
    );

    const cycleIdentifier = identifierNodes(nodes, "cycle")
      .find(node => nodeText(node, text) === "cycle");
    assert.ok(cycleIdentifier !== undefined);
    const cycleType = valueType(session, symbolAt(session, cycleIdentifier));
    assert.ok(isClassOrInterfaceTypeFact(cycleType));
    const left = expectResolved(session.getProperty(cycleType.handle, "left"));
    const right = expectResolved(session.getProperty(cycleType.handle, "right"));
    assert.equal(
      valueType(session, left).handle,
      valueType(session, right).handle,
      "shared property types must share one type handle",
    );
    const next = expectResolved(session.getProperty(cycleType.handle, "next"));
    const nextType = valueType(session, next);
    assert.ok(isUnionTypeFact(nextType));
    const nextCycle = expectResolved(session.getUnionConstituents(nextType.handle))
      .find(type => type.display.includes("Cycle"));
    assert.ok(nextCycle !== undefined);
    assert.equal(nextCycle.handle, cycleType.handle);

    const unionUsages = identifierNodes(nodes, "unionValue");
    const declarationUse = unionUsages[0];
    assert.ok(declarationUse !== undefined);
    const unionValueSymbol = symbolAt(session, declarationUse);
    const declaredUnion = valueType(session, unionValueSymbol);
    assert.ok(isUnionTypeFact(declaredUnion));
    const narrowedUse = unionUsages.find(node =>
      text.slice(node.location.start).startsWith("unionValue.toUpperCase"));
    assert.ok(narrowedUse !== undefined);
    const narrowed = expectResolved(session.getSymbolTypeAtLocation(
      unionValueSymbol.handle,
      narrowedUse.handle,
    ));
    assert.equal(narrowed.category, TypeCategory.String);

    assert.ok(direct.handle instanceof TypeHandle);
  } finally {
    session.dispose();
  }
});

test("normalizes structural type queries and exported category guards", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  try {
    const entry = sourceByPath(session, "entry.ts");
    const nodes = nodesFor(session, entry);
    const base = sourceByPath(session, "base.ts");
    const baseText = sourceText(base);
    const baseNodes = nodesFor(session, base);
    const byName = (name: string): SymbolFact => {
      const node = identifierNodes(nodes, name)[0];
      assert.ok(node !== undefined);
      return symbolAt(session, node);
    };
    const declared = (name: string): TypeFact => {
      const node = identifierNodes(baseNodes, name)[0];
      assert.ok(node !== undefined);
      return expectResolved(session.getDeclaredType(symbolAt(session, node).handle));
    };

    const derived = valueType(session, byName("derivedValue"));
    assert.ok(isClassOrInterfaceTypeFact(derived));
    assert.ok(expectResolved(session.getBaseTypes(derived.handle))
      .some(type => type.display === "Base"));

    const dictionary = valueType(session, byName("dictionaryValue"));
    const indexes = expectResolved(session.getIndexInfos(dictionary.handle));
    assert.equal(indexes.length, 1);
    const index = indexes[0];
    assert.ok(index !== undefined);
    assert.equal(expectResolved(session.getType(index.keyType)).category, TypeCategory.String);
    assert.equal(expectResolved(session.getType(index.valueType)).category, TypeCategory.Number);

    const readonlyValues = valueType(session, byName("readonlyValues"));
    assert.ok(isTypeReferenceTypeFact(readonlyValues));
    assert.deepEqual(
      expectResolved(session.getTypeArguments(readonlyValues.handle))
        .map(type => type.category),
      [TypeCategory.String],
    );

    const constrained = valueType(session, byName("constrainedValue"));
    assert.equal(session.getBaseConstraint(constrained.handle).kind, "Absent");
    assert.equal(expectResolved(session.getProperties(constrained.handle)).length, 2);
    assert.equal(session.getProperty(constrained.handle, "missing").kind, "Absent");
    assert.deepEqual(session.getConstructSignatures(constrained.handle), {
      kind: "Resolved",
      value: [],
    });
    expectResolved(session.getApparentType(constrained.handle));
    expectResolved(session.getWidenedType(constrained.handle));
    expectResolved(session.getNonNullableType(constrained.handle));

    const tupleNode = oneNode(
      baseNodes,
      baseText,
      NodeKind.TypeNode,
      "[string, number]",
    );
    const tuple = expectResolved(session.getTypeAtNode(tupleNode.handle));
    const indexType = declared("Key");
    const indexedAccess = declared("Indexed");
    const conditional = declared("Conditional");
    const template = declared("Template");
    const mapping = declared("Mapping");
    const mapped = declared("Mapped");
    const mappedValue = valueType(session, byName("mappedValue"));
    const numberLiteral = declared("NumberLiteralAlias");
    const bigintLiteral = declared("BigIntLiteralAlias");
    const boolean = declared("BooleanAlias");
    const booleanLiteral = declared("BooleanLiteralAlias");
    const typeParameterNode = identifierNodes(baseNodes, "T").find(node =>
      node.location.start === baseText.indexOf("T extends Base"));
    assert.ok(typeParameterNode !== undefined);
    const typeParameter = expectResolved(session.getDeclaredType(
      symbolAt(session, typeParameterNode).handle,
    ));
    assert.ok(isTypeParameterFact(typeParameter));
    assert.equal(
      expectResolved(session.getBaseConstraint(typeParameter.handle)).display,
      "Base",
    );

    const facts: readonly TypeFact[] = [
      derived,
      dictionary,
      readonlyValues,
      constrained,
      tuple,
      indexType,
      indexedAccess,
      conditional,
      template,
      mapping,
      mapped,
      mappedValue,
      numberLiteral,
      bigintLiteral,
      boolean,
      booleanLiteral,
      typeParameter,
    ];
    assert.ok(facts.some(isObjectTypeFact));
    assert.ok(facts.some(isClassOrInterfaceTypeFact));
    assert.ok(facts.some(isTypeReferenceTypeFact));
    assert.ok(facts.some(isTupleTypeFact));
    assert.ok(facts.some(isIndexTypeFact));
    assert.ok(facts.some(isIndexedAccessTypeFact));
    assert.ok(facts.some(isConditionalTypeFact));
    assert.equal(facts.some(isSubstitutionTypeFact), false);
    assert.ok(facts.some(isTemplateLiteralTypeFact));
    assert.ok(facts.some(isStringMappingTypeFact));
    assert.ok(facts.some(isTypeParameterFact));
    assert.ok(expectResolved(session.getUnionConstituents(
      declared("UnionAlias").handle,
    )).some(isIntrinsicTypeFact));
    assert.ok(facts.some(isNumberLiteralTypeFact));
    assert.ok(facts.some(isBigIntLiteralTypeFact));
    assert.equal(boolean.category, TypeCategory.Boolean);
    assert.ok(facts.some(isBooleanLiteralTypeFact));
    assert.equal(
      expectResolved(session.getLiteralBaseType(booleanLiteral.handle)).category,
      TypeCategory.Boolean,
    );
    assert.deepEqual(session.getUnionConstituents(boolean.handle), {
      kind: "NotApplicable",
      expectedSubject: TypeCategory.Union,
      actualSubject: TypeCategory.Boolean,
    });
    assert.ok(mapped.objectCategories.includes(ObjectTypeCategory.Mapped));
    assert.ok(mappedValue.aliasSymbol instanceof SymbolHandle);
    assert.equal(mappedValue.aliasTypeArguments.length, 1);
    const dateNode = identifierNodes(nodes, "Date")[0];
    assert.ok(dateNode !== undefined);
    const dateType = valueType(session, symbolAt(session, dateNode));
    const constructors = expectResolved(session.getConstructSignatures(dateType.handle));
    assert.ok(constructors.length > 0);
    assert.ok(constructors.every(signature => signature.category === "Construct"));
  } finally {
    session.dispose();
  }
});

test("preserves overload selection, generic targets, this, rest, and predicates", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  try {
    const entry = sourceByPath(session, "entry.ts");
    const text = sourceText(entry);
    const nodes = nodesFor(session, entry);
    const base = sourceByPath(session, "base.ts");
    const baseNodes = nodesFor(session, base);

    const selectedGenericCall = oneNode(
      nodes,
      text,
      NodeKind.CallExpression,
      "genericIdentity(42)",
    );
    const selectedGeneric = expectResolved(
      session.getResolvedSignature(selectedGenericCall.handle),
    );
    assert.ok(selectedGeneric.handle instanceof SignatureHandle);
    assert.equal(
      expectResolved(session.getSignature(selectedGeneric.handle)).handle,
      selectedGeneric.handle,
    );
    assert.ok(selectedGeneric.target instanceof SignatureHandle);
    const target = expectResolved(session.getSignatureTarget(selectedGeneric.handle));
    assert.equal(target.handle, selectedGeneric.target);
    assert.equal(target.typeParameters.length, 1);
    assert.equal(session.getSignatureTarget(target.handle).kind, "Absent");
    assert.equal("minimumArgumentCount" in selectedGeneric, false);

    const selectedOverloadCall = oneNode(
      nodes,
      text,
      NodeKind.CallExpression,
      'overloaded("selected")',
    );
    const selectedOverload = expectResolved(
      session.getResolvedSignature(selectedOverloadCall.handle),
    );
    assert.equal(
      expectResolved(session.getSignatureParameterType(selectedOverload.handle, 0)).category,
      TypeCategory.String,
    );
    assert.deepEqual(session.getSignatureParameterType(selectedOverload.handle, 1), {
      kind: "InvalidArgument",
      reason: "OutOfRange",
    });

    const functionSignatures = (name: string) => {
      const declarationName = identifierNodes(baseNodes, name)[0];
      assert.ok(declarationName !== undefined);
      const type = valueType(session, symbolAt(session, declarationName));
      return expectResolved(session.getCallSignatures(type.handle));
    };
    assert.equal(functionSignatures("overloaded").length, 2);

    const withThis = functionSignatures("withThis")[0];
    assert.ok(withThis !== undefined);
    assert.equal(withThis.hasRestParameter, true);
    assert.ok(withThis.thisParameter instanceof SymbolHandle);
    assert.ok(withThis.restType instanceof TypeHandle);

    const hasText = functionSignatures("hasText")[0];
    assert.ok(hasText !== undefined);
    assert.equal(hasText.predicate?.category, TypePredicateCategory.Identifier);
    assert.equal(hasText.predicate?.parameterName, "value");

    const assertText = functionSignatures("assertText")[0];
    assert.ok(assertText !== undefined);
    assert.equal(
      assertText.predicate?.category,
      TypePredicateCategory.AssertsIdentifier,
    );
  } finally {
    session.dispose();
  }
});

test("resolves module symbols, exports, source symbols, and exact constant outcomes", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  try {
    const entry = sourceByPath(session, "entry.ts");
    const text = sourceText(entry);
    const nodes = nodesFor(session, entry);
    const base = sourceByPath(session, "base.ts");
    const baseText = sourceText(base);
    const baseNodes = nodesFor(session, base);

    const baseSpecifier = oneNode(
      nodes,
      text,
      NodeKind.StringLiteral,
      '"./base.js"',
    );
    const moduleSymbol = expectResolved(session.getModuleSymbol(baseSpecifier.handle));
    assert.ok(moduleSymbol.declarations.length > 0);
    assert.equal(
      expectResolved(session.getDeclaration(moduleSymbol.declarations[0]
        ?? assert.fail("module symbol had no declaration"))).sourceFileClassification,
      SourceFileClassification.ImportedProject,
    );
    const exports = expectResolved(session.getModuleExports(moduleSymbol.handle));
    assert.ok(exports.some(symbol => symbol.displayName === "identity"));
    assert.equal(
      expectResolved(session.getModuleExport(moduleSymbol.handle, "Choice")).displayName,
      "Choice",
    );
    assert.equal(session.getModuleExport(moduleSymbol.handle, "missing").kind, "Absent");

    const sideEffect = oneNode(
      nodes,
      text,
      NodeKind.ImportDeclaration,
      'import "./script.js";',
    );
    const sideEffectSpecifier = oneNode(
      nodes,
      text,
      NodeKind.StringLiteral,
      '"./script.js"',
    );
    assert.equal(session.getModuleSymbol(sideEffectSpecifier.handle).kind, "Absent");
    assert.deepEqual(session.getModuleSymbol(sideEffect.handle), {
      kind: "NotApplicable",
      expectedSubject: queryApplicability.getModuleSymbol,
      actualSubject: NodeKind.ImportDeclaration,
    });

    const dynamicSpecifier = oneNode(
      nodes,
      text,
      NodeKind.StringLiteral,
      '"./dynamic.js"',
    );
    assert.deepEqual(session.getModuleSymbol(dynamicSpecifier.handle), {
      kind: "NotApplicable",
      expectedSubject: queryApplicability.getModuleSymbol,
      actualSubject: NodeKind.StringLiteral,
    });

    const dynamic = oneNode(
      nodes,
      text,
      NodeKind.CallExpression,
      "import(name)",
    );
    assert.deepEqual(session.getModuleSymbol(dynamic.handle), {
      kind: "NotApplicable",
      expectedSubject: queryApplicability.getModuleSymbol,
      actualSubject: NodeKind.CallExpression,
    });

    const enumMember = oneNode(
      baseNodes,
      baseText,
      NodeKind.EnumMember,
      "Two = 2",
    );
    assert.deepEqual(session.getConstantValue(enumMember.handle), {
      kind: "Resolved",
      value: 2,
    });
    const enumMemberName = identifierNodes(baseNodes, "Two")[0];
    assert.ok(enumMemberName !== undefined);
    const enumMemberSymbol = symbolAt(session, enumMemberName);
    const enumMemberDeclaration = expectResolved(session.getDeclaration(
      enumMemberSymbol.declarations[0]
        ?? assert.fail("enum member symbol had no declaration"),
    ));
    const enumContainer = expectResolved(session.getDeclaration(
      enumMemberDeclaration.containingDeclarations[0]
        ?? assert.fail("enum member declaration had no containing declaration"),
    ));
    assert.equal(enumContainer.kind, NodeKind.EnumDeclaration);
    const enumName = identifierNodes(baseNodes, "Choice")[0];
    assert.ok(enumName !== undefined);
    const enumType = expectResolved(session.getDeclaredType(
      symbolAt(session, enumName).handle,
    ));
    assert.equal(enumType.category, TypeCategory.Union);
    assert.ok(isUnionTypeFact(enumType));
    assert.equal(isLiteralTypeFact(enumType), false);
    assert.equal(
      expectResolved(session.getUnionConstituents(enumType.handle)).length,
      2,
    );
    const importedEnumAccess = oneNode(
      nodes,
      text,
      NodeKind.PropertyAccessExpression,
      "Choice.Two",
    );
    assert.equal(session.getConstantValue(importedEnumAccess.handle).kind, "Absent");
    const enumMemberType = expectResolved(session.getTypeAtNode(importedEnumAccess.handle));
    assert.equal(enumMemberType.category, TypeCategory.EnumLiteral);
    assert.ok(isLiteralTypeFact(enumMemberType));
    assert.equal(session.getLiteralBaseType(enumMemberType.handle).kind, "Resolved");
    const nonConstant = oneNode(
      nodes,
      text,
      NodeKind.PropertyAccessExpression,
      "cycle.left.label",
    );
    assert.equal(session.getConstantValue(nonConstant.handle).kind, "Absent");
    const identifier = identifierNodes(nodes, "cycle")[0];
    assert.ok(identifier !== undefined);
    assert.deepEqual(session.getConstantValue(identifier.handle), {
      kind: "NotApplicable",
      expectedSubject: queryApplicability.getConstantValue,
      actualSubject: NodeKind.Identifier,
    });
  } finally {
    session.dispose();
  }
});

test("query applicability coverage is derived from the facade declaration table", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  try {
    const entry = sourceByPath(session, "entry.ts");
    const text = sourceText(entry);
    const nodes = nodesFor(session, entry);
    const identifier = identifierNodes(nodes, "cycle")[0];
    const genericCall = oneNode(
      nodes,
      text,
      NodeKind.CallExpression,
      "genericIdentity(42)",
    );
    const primitiveCall = oneNode(
      nodes,
      text,
      NodeKind.CallExpression,
      "Math.random()",
    );
    assert.ok(identifier !== undefined);
    const symbol = symbolAt(session, identifier);
    const primitiveType = expectResolved(session.getTypeAtNode(primitiveCall.handle));
    assert.equal(session.getContextualType(primitiveCall.handle).kind, "Absent");
    const symbolLessLiteral = nodes.find(node =>
      node.kind === NodeKind.NumericLiteral
      && session.getSymbolAtNode(node.handle).kind === "Absent");
    assert.ok(symbolLessLiteral !== undefined);

    const checks = {
      getSourceSymbol: () => session.getSourceSymbol(identifier.handle),
      getContextualType: () => session.getContextualType(
        oneNode(nodes, text, NodeKind.ImportDeclaration, 'import "./script.js";').handle,
      ),
      getResolvedSignature: () => session.getResolvedSignature(identifier.handle),
      getUnionConstituents: () => session.getUnionConstituents(primitiveType.handle),
      getIntersectionConstituents: () =>
        session.getIntersectionConstituents(primitiveType.handle),
      getBaseTypes: () => session.getBaseTypes(primitiveType.handle),
      getTypeArguments: () => session.getTypeArguments(primitiveType.handle),
      getLiteralBaseType: () => session.getLiteralBaseType(primitiveType.handle),
      getConstantValue: () => session.getConstantValue(identifier.handle),
      getAliasChain: () => session.getAliasChain(symbol.handle),
      getModuleExports: () => session.getModuleExports(symbol.handle),
      getModuleExport: () => session.getModuleExport(symbol.handle, "missing"),
      getModuleSymbol: () => session.getModuleSymbol(genericCall.handle),
    } satisfies Readonly<Record<keyof typeof queryApplicability, () => QueryResult<unknown>>>;

    for (const [name, check] of Object.entries(checks)) {
      assert.equal(check().kind, "NotApplicable", `${name} did not reject its wrong subject`);
    }
  } finally {
    session.dispose();
  }
});

test("unknown symbols, error types, unsupported values, and poisoned sessions stay distinct", () => {
  const rejectedHarness = semanticFactsTestSeam.createHarness({
    allowRejectedDiagnostics: true,
  });
  const rejectedSession = expectOpened(rejectedHarness.open(badTsconfig));
  const rejectedSource = sourceByPath(rejectedSession, "bad.ts");
  const rejectedNodes = nodesFor(rejectedSession, rejectedSource);
  const aliasNode = identifierNodes(rejectedNodes, "missingExport")[0];
  assert.ok(aliasNode !== undefined);
  const aliasSymbol = symbolAt(rejectedSession, aliasNode);
  const unknownAlias = rejectedSession.getAliasChain(aliasSymbol.handle);
  assert.equal(unknownAlias.kind, "Unavailable");
  if (unknownAlias.kind === "Unavailable") {
    assert.equal(unknownAlias.reason, "UnknownSymbol");
  }
  const errorNode = identifierNodes(rejectedNodes, "doesNotExist")[0];
  assert.ok(errorNode !== undefined);
  const errorType = expectResolved(rejectedSession.getTypeAtNode(errorNode.handle));
  assert.ok(isErrorTypeFact(errorType));
  rejectedSession.dispose();

  const characterization = semanticFactsTestSeam.createHarness({});
  for (const result of [
    characterization.characterizeUnsupportedNodeKind(),
    characterization.characterizeUnsupportedSymbolFlags(),
    characterization.characterizeUnsupportedTypeFlags(),
    characterization.characterizeUnsupportedDiagnosticCategory(),
  ]) {
    assert.equal(result.kind, "Unavailable");
    if (result.kind === "Unavailable") {
      assert.equal(result.reason, "UnsupportedApiValue");
    }
  }

  for (const [faults, reason] of [
    [{ missingApiFactOperation: "getSourceFiles" }, "MissingApiFact"],
    [{ unsupportedResponseShapeOperation: "getSourceFiles" }, "UnsupportedResponseShape"],
  ] as const) {
    const harness = semanticFactsTestSeam.createHarness(faults);
    const session = expectOpened(harness.open(fixtureTsconfig));
    const result = session.getSourceFiles();
    assert.equal(result.kind, "Unavailable");
    if (result.kind === "Unavailable") {
      assert.equal(result.reason, reason);
    }
    session.dispose();
  }

  const checkerHarness = semanticFactsTestSeam.createHarness({
    checkerFailure: {
      operation: "getDeclaredType",
      reason: "ProcessFailure",
      detail: "injected checker child-process failure",
    },
  });
  const checkerSession = expectOpened(checkerHarness.open(fixtureTsconfig));
  const checkerEntry = sourceByPath(checkerSession, "entry.ts");
  const checkerNode = identifierNodes(nodesFor(checkerSession, checkerEntry), "cycle")[0];
  assert.ok(checkerNode !== undefined);
  const checkerSymbol = symbolAt(checkerSession, checkerNode);
  assert.deepEqual(checkerSession.getDeclaredType(checkerSymbol.handle), {
    kind: "SessionFailure",
    reason: "ProcessFailure",
    detail: "injected checker child-process failure",
  });
  assert.deepEqual(checkerSession.getSourceFiles(), {
    kind: "SessionFailure",
    reason: "ProcessFailure",
    detail: "injected checker child-process failure",
  });
  checkerSession.dispose();

  const batchCheckerHarness = semanticFactsTestSeam.createHarness({
    checkerFailure: {
      operation: "getTypesAtNodes",
      reason: "ProcessFailure",
      detail: "injected batch checker child-process failure",
    },
  });
  const batchCheckerSession = expectOpened(batchCheckerHarness.open(fixtureTsconfig));
  const batchEntry = sourceByPath(batchCheckerSession, "entry.ts");
  const batchNode = nodesFor(batchCheckerSession, batchEntry)[0];
  assert.ok(batchNode !== undefined);
  assert.deepEqual(batchCheckerSession.getTypesAtNodes([batchNode.handle]), {
    kind: "SessionFailure",
    reason: "ProcessFailure",
    detail: "injected batch checker child-process failure",
  });
  assert.deepEqual(batchCheckerSession.getSourceFiles(), {
    kind: "SessionFailure",
    reason: "ProcessFailure",
    detail: "injected batch checker child-process failure",
  });
  batchCheckerSession.dispose();

  const protocolHarness = semanticFactsTestSeam.createHarness({
    queryFailure: {
      operation: "getSourceFiles",
      reason: "ProtocolFailure",
      detail: "injected query protocol failure",
    },
  });
  const protocolSession = expectOpened(protocolHarness.open(fixtureTsconfig));
  assert.deepEqual(protocolSession.getSourceFiles(), {
    kind: "SessionFailure",
    reason: "ProtocolFailure",
    detail: "injected query protocol failure",
  });
  assert.deepEqual(protocolSession.getSourceFiles(), {
    kind: "SessionFailure",
    reason: "ProtocolFailure",
    detail: "injected query protocol failure",
  });
  protocolSession.dispose();

  const poisonHarness = semanticFactsTestSeam.createHarness({
    queryFailure: {
      operation: "getSourceFiles",
      reason: "ProcessFailure",
      detail: "injected process failure",
    },
  });
  const handleSession = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  const foreignHandle = sourceByPath(handleSession, "entry.ts").handle;
  const poisonSession = expectOpened(poisonHarness.open(fixtureTsconfig));
  assert.deepEqual(poisonSession.getSourceFiles(), {
    kind: "SessionFailure",
    reason: "ProcessFailure",
    detail: "injected process failure",
  });
  assert.deepEqual(poisonSession.getNode(foreignHandle), {
    kind: "SessionFailure",
    reason: "ProcessFailure",
    detail: "injected process failure",
  });
  poisonSession.dispose();
  handleSession.dispose();
  assert.deepEqual(poisonSession.getSourceFiles(), {
    kind: "SessionFailure",
    reason: "SessionDisposed",
    detail: "the semantic-facts session has been disposed",
  });
});

function unstableImports(
  files: Readonly<Record<string, string>>,
): readonly string[] {
  const unstablePattern
    = /(["'`])typescript\/unstable\/[A-Za-z0-9._/-]+\1/u;
  return Object.entries(files)
    .filter(([, content]) => unstablePattern.test(content))
    .map(([path]) => path)
    .sort();
}

test("only the adapter imports unstable TypeScript packages and the scan is non-vacuous", () => {
  const unstableSync = `"${unstablePackagePrefix}sync"`;
  assert.deepEqual(unstableImports({
    "ordinary.ts": 'import { value } from "./value.js";',
    "first-forbidden.ts": `import { API } from ${unstableSync};`,
    "second-forbidden.ts": `import{API}from${unstableSync};`,
    "side-effect-forbidden.ts": `import ${unstableSync};`,
    "dynamic-forbidden.ts": `void import(${unstableSync});`,
    "require-forbidden.cjs": `require(${unstableSync});`,
    "create-require-forbidden.mts":
      `createRequire(import.meta.url)(${unstableSync});`,
    "dynamic-template-forbidden.ts":
      `void import(\`${unstablePackagePrefix}sync\`);`,
    "require-template-forbidden.cjs":
      `require(\`${unstablePackagePrefix}ast\`);`,
    "create-require-template-forbidden.mts":
      `createRequire(import.meta.url)(\`${unstablePackagePrefix}sync\`);`,
    "async-subpath-forbidden.ts":
      `import { API } from "${unstablePackagePrefix}async";`,
    "ast-is-subpath-forbidden.ts":
      `import { isImportClause } from "${unstablePackagePrefix}ast/is";`,
  }), [
    "ast-is-subpath-forbidden.ts",
    "async-subpath-forbidden.ts",
    "create-require-forbidden.mts",
    "create-require-template-forbidden.mts",
    "dynamic-forbidden.ts",
    "dynamic-template-forbidden.ts",
    "first-forbidden.ts",
    "require-forbidden.cjs",
    "require-template-forbidden.cjs",
    "second-forbidden.ts",
    "side-effect-forbidden.ts",
  ]);

  const inventoryRoot = mkdtempSync(join(tmpdir(), "dotnet-inspect-source-inventory-"));
  try {
    const scripts = join(inventoryRoot, "scripts");
    mkdirSync(scripts);
    const names = [
      "probe.ts",
      "probe.mts",
      "probe.cts",
      "probe.tsx",
      "probe.js",
      "probe.mjs",
      "probe.cjs",
      "probe.jsx",
      "probe.TS",
    ];
    for (const name of names) {
      writeFileSync(join(scripts, name), "", "utf8");
    }
    assert.equal(projectSourceFiles(
      inventoryRoot,
      [...typeScriptSourceExtensions, ...javaScriptSourceExtensions],
      ["scripts"],
    ).length, names.length);
  } finally {
    rmSync(inventoryRoot, { recursive: true, force: true });
  }

  const files = Object.fromEntries(projectSourceFiles(
    inspectWebRoot,
    [...typeScriptSourceExtensions, ...javaScriptSourceExtensions],
    ["public", "src", "test", "scripts"],
  ).map(path => [
    relative(inspectWebRoot, path).split(sep).join("/"),
    readFileSync(path, "utf8"),
  ]));
  const imports = unstableImports(files);
  assert.deepEqual(imports, ["scripts/typescript-semantic-facts.ts"]);
  const adapter = files["scripts/typescript-semantic-facts.ts"];
  assert.ok(adapter !== undefined);
  assert.match(adapter, /typescript\/unstable\/sync/u);
  assert.match(adapter, /typescript\/unstable\/ast/u);
  assert.match(
    readFileSync(join(inspectWebRoot, "package.json"), "utf8"),
    /"typescript": "7\.0\.2"/u,
  );
});

test("public facts expose opaque repository handles rather than upstream values", () => {
  const session = expectOpened(openTypeScriptSemanticFacts(fixtureTsconfig));
  try {
    const entry = sourceByPath(session, "entry.ts");
    const node = nodesFor(session, entry)[0];
    assert.ok(node !== undefined);
    const handles: readonly SemanticHandle[] = [
      entry.handle,
      node.handle,
    ];
    assert.ok(handles[0] instanceof SourceFileHandle);
    assert.ok(handles[1] instanceof NodeHandle);
    assert.deepEqual(Object.keys(entry.handle), []);
    assert.deepEqual(Object.keys(node.handle), []);
    assert.equal(JSON.stringify(entry.handle), "{}");
    assert.equal(JSON.stringify(node.handle), "{}");
    assert.ok(dirname(fileURLToPath(import.meta.url)).endsWith(`${sep}test`));
  } finally {
    session.dispose();
  }
});
