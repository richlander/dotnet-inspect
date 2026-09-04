import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import { parseSync, visitorKeys } from "oxc-parser";
import type {
  Argument,
  ArrowFunctionExpression,
  CallExpression,
  Directive,
  Expression,
  Function as SyntaxFunction,
  FunctionBody,
  ImportDeclaration,
  Node,
  ObjectExpression,
  ObjectProperty,
  Span,
  Statement,
} from "oxc-parser";

import {
  accessibilityFilterIncludingType,
  activeSourceOperationKind,
  assemblyDescriptorForType,
  pdbSourceLimitationHtml,
  beginSourceRequestState,
  cancelSourceRequestState,
  callGraphAssemblyIdentityMatches,
  callGraphDiagnosticsMessage,
  callGraphTargetMatchesType,
  callGraphTargetTypeId,
  combinedGraphTargetNavigationDisposition,
  createDependencyGraphPendingState,
  createDependencyGraphRenderSequence,
  dependencyCoordinateCandidates,
  dependencyGroupSelectionMessage,
  dependencyGraphGroupSelectionIndex,
  dependencyGraphExternalKey,
  dependencyGraphPackageKey,
  dependencyGraphRenderSignature,
  ensureBoundedGraphNode,
  graphTargetBlockedReason,
  graphTargetNavigationDisposition,
  graphMemberDeepLinkDisposition,
  graphMemberPendingMatchesView,
  graphMemberSurfaceAssembly,
  graphMemberShareTarget,
  graphMemberSelection,
  graphMemberTargetWithSelectedBody,
  graphMemberTargetFromPacket,
  graphMemberTargetFromShare,
  graphOnlyBodyTarget,
  retainGraphOnlyBodyTarget,
  MARKDOWN_SANITIZE_OPTIONS,
  MAX_WORKSPACE_PACKAGES,
  memberRequestKey,
  memberSectionDefinitions,
  memberSectionIdsFor,
  mergeInspectionErrorEntries,
  mergeInspectionErrors,
  renderInspectionErrors,
  mermaidLabel,
  packageCoordinateMatchesLocation,
  packageForView,
  packageIdentityKey,
  parameterTitleHtml,
  partitionGraphMembers,
  platformPackForGraphAssembly,
  platformPackFromAcquiredProvenance,
  platformPackFromProvenance,
  platformPackToken,
  reconcileCurrentNavigationEntry,
  removeWorkspacePackage,
  removeAppendedNotice,
  replaceCurrentNavigationEntry,
  retainGraphMemberProjection,
  retainWorkspacePackage,
  resolveLoadedGraphTargetCandidate,
  resolveOpportunitySourceCandidate,
  resolveOpportunitySourceType,
  resolvePlatformGraphTargetType,
  resolveRuntimeGraphTargetCandidate,
  runtimePackForFramework,
  runtimeGraphTargetAssemblyIsResident,
  runtimeGraphTargetNavigationDisposition,
  scopedRequestState,
  selectedDependencyGroup,
  sourceSurfaceIsVisible,
  sourceReloadKind,
  sourceRequestNeedsLoad,
  searchableMemberGroups,
  spotlightCandidateKey,
  spotlightCandidateSignature,
  typeLensesFor,
  uniqueTypeByQueryId,
  workspaceCoordinatesMatch
} from "../src/data.ts";
import type {
  CallGraphDiagnostics,
  CallGraphTarget,
  GraphMemberShareIdentity,
  NavigationState,
  SourceWorkbenchState,
} from "../src/data.ts";
import {
  buildDependencyGraphMermaid,
  buildTypeGraphMermaid
} from "../src/graph-mermaid.ts";

const packageAt = (version: string, framework: string, types = 1) => ({
  id: "Example.Package",
  version,
  activeFramework: framework,
  types: Array.from({ length: types }, (_, index) => ({ id: `Type${index}` }))
});

test("dependency candidates carry typed package provenance to the product engine", () => {
  const packageCandidate = packageAt("2.0.0", "net8.0");
  const runtimeCandidate = {
    ...packageAt("10.0.10", "net10.0"),
    id: "Microsoft.NETCore.App",
    isRuntimePack: true
  };

  assert.deepEqual(
    dependencyCoordinateCandidates([packageCandidate, runtimeCandidate]),
    [
      {
        key: packageIdentityKey(packageCandidate),
        provenance: "NuGetPackage",
        packageId: "Example.Package",
        version: "2.0.0",
        targetFramework: "net8.0"
      },
      {
        key: packageIdentityKey(runtimeCandidate),
        provenance: "PlatformRuntime",
        packageId: "Microsoft.NETCore.App",
        version: "10.0.10",
        targetFramework: "net10.0"
      }
    ]);
});

test("dependency graph keys preserve complete coordinates and declared ranges", () => {
  assert.notEqual(
    dependencyGraphPackageKey(packageAt("1.0.0", "net8.0")),
    dependencyGraphPackageKey(packageAt("2.0.0", "net8.0")));
  assert.notEqual(
    dependencyGraphPackageKey(packageAt("2.0.0", "net8.0")),
    dependencyGraphPackageKey(packageAt("2.0.0", "net9.0")));
  assert.notEqual(
    dependencyGraphExternalKey("Example.Package", "[1.0.0]"),
    dependencyGraphExternalKey("Example.Package", "2.*"));
});

test("dependency graph node insertion is bounded", () => {
  const nodes = new Map<string, { index: number }>();
  let truncated = false;
  for (let index = 0; index < 8000; index++) {
    const result = ensureBoundedGraphNode(
      nodes,
      `node-${index}`,
      () => ({ index }),
      80);
    truncated ||= result.truncated;
  }

  assert.equal(nodes.size, 80);
  assert.equal(truncated, true);
  assert.equal(
    ensureBoundedGraphNode(nodes, "node-1", () => null, 80).node,
    nodes.get("node-1"));
});

test("a successful retry removes only its appended failure notice", () => {
  const prior = "The workspace was truncated.";
  const failed = `${prior} Couldn’t load System.Net.Http: unavailable.`;

  assert.equal(removeAppendedNotice(failed, prior, failed), prior);
  assert.equal(
    removeAppendedNotice(
      `${failed} A later warning remains.`,
      prior,
      failed),
    `${prior} A later warning remains.`);
  assert.equal(
    removeAppendedNotice("A replacement warning.", prior, failed),
    "A replacement warning.");
});

test("normalizing a history entry keeps its consumed position and later entries", () => {
  const nav = {
    index: 1,
    stack: [
      { sig: "older", view: { id: "older" } },
      { sig: "stale", view: { id: "stale" } },
      { sig: "newer", view: { id: "newer" } },
    ],
  };

  replaceCurrentNavigationEntry(nav, {
    sig: "normalized",
    view: { id: "normalized" }
  });

  assert.equal(nav.index, 1);
  assert.deepEqual(nav.stack, [
    { sig: "older", view: { id: "older" } },
    { sig: "normalized", view: { id: "normalized" } },
    { sig: "newer", view: { id: "newer" } },
  ]);
});

const appSource = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
const parsedAppSource = parseSync("dotnet-inspect.ts", appSource);
const appSyntax = parsedAppSource.program;

// The helpers below read `dotnet-inspect.ts` as syntax rather than as text, so the tests
// can assert on structure. `Node` is oxc's discriminated union over every AST shape, so
// narrowing through `node.type` keeps each helper checked against the real grammar rather
// than against `any`.
type SyntaxVisitor = (node: Node) => void;

// `visitorKeys` is a runtime map from a node type to that node's child keys, which is
// what makes the walk data-driven instead of a switch over a union with hundreds of
// members. Indexing a node by a key chosen at runtime is the one operation the union
// cannot express, so the assertion is confined to this helper and every caller below
// stays narrowed.
const syntaxChildren = (node: Node): Record<string, unknown> =>
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  node as unknown as Record<string, unknown>;

// The walk only relies on a node carrying a string `type`, which is what `visitorKeys` is
// keyed by; anything else reached through a child key is skipped rather than trusted.
function isSyntaxNode(value: unknown): value is Node {
  return typeof value === "object"
    && value !== null
    && "type" in value
    && typeof value.type === "string";
}

function walkSyntax(node: Node, visit: SyntaxVisitor): void {
  visit(node);
  const children = syntaxChildren(node);
  for (const key of visitorKeys[node.type] ?? []) {
    const child = children[key];
    if (Array.isArray(child)) {
      for (const item of child as readonly unknown[]) {
        if (isSyntaxNode(item)) walkSyntax(item, visit);
      }
    } else if (isSyntaxNode(child)) {
      walkSyntax(child, visit);
    }
  }
}

function syntaxNodes(root: Node, predicate: (node: Node) => boolean): Node[] {
  const matches: Node[] = [];
  walkSyntax(root, node => {
    if (predicate(node)) matches.push(node);
  });
  return matches;
}

function onlySyntaxNode<T>(nodes: readonly T[], description: string): T {
  assert.equal(nodes.length, 1, description);
  const only = nodes[0];
  assert.ok(only !== undefined, description);
  return only;
}

// oxc models every function form with one `Function` interface whose `type` is a union,
// so a declaration is that interface with the `type` narrowed. These aliases add the two
// facts the tests below rely on and the union cannot state: a declaration that was found
// has a body, and a callback property holds an arrow function with a block body.
type DeclaredFunction = SyntaxFunction & { body: FunctionBody };
type BlockArrowFunction = ArrowFunctionExpression & { body: FunctionBody };

function hasFunctionBody(declaration: SyntaxFunction): declaration is DeclaredFunction {
  return declaration.body !== null && declaration.body !== undefined;
}

function isBlockArrowFunction(value: Expression): value is BlockArrowFunction {
  return value.type === "ArrowFunctionExpression"
    && value.body.type === "BlockStatement";
}

function functionDeclaration(name: string): DeclaredFunction {
  const declaration = onlySyntaxNode(
    appSyntax.body.filter(
      (node): node is SyntaxFunction =>
        node.type === "FunctionDeclaration" && node.id?.name === name),
    `${name} declaration`);
  // `assert.fail` returns `never`, so this narrows the declaration rather than merely
  // reporting; `assert.ok` on a predicate call would assert the boolean, not the value.
  if (!hasFunctionBody(declaration)) assert.fail(`${name} declaration must have a body`);
  return declaration;
}

function onlyCallExpressionNamed(root: Node, name: string): CallExpression {
  return onlySyntaxNode(callExpressionsNamed(root, name), `${name} call`);
}

function callArgument(
  call: CallExpression,
  index: number,
  description: string,
): Argument {
  const argument = call.arguments[index];
  assert.ok(argument !== undefined, `${description} argument ${index}`);
  return argument;
}

function objectArgument(
  call: CallExpression,
  index: number,
  description: string,
): ObjectExpression {
  const argument = callArgument(call, index, description);
  assert.ok(
    argument.type === "ObjectExpression",
    `${description} argument ${index} must be an object literal, `
      + `found ${argument.type}`);
  return argument;
}

function statementAt(
  statements: readonly (Statement | Directive)[],
  index: number,
  description: string,
): Statement | Directive {
  const statement = statements[index];
  assert.ok(statement !== undefined, `${description} statement ${index}`);
  return statement;
}

// Several tests pin that a binder is handed a specific identifier, such as `document`.
function assertIdentifierArgument(
  call: CallExpression,
  index: number,
  name: string,
  description: string,
): void {
  const argument = callArgument(call, index, description);
  assert.ok(
    argument.type === "Identifier",
    `${description} argument ${index} must be an identifier, `
      + `found ${argument.type}`);
  assert.equal(argument.name, name);
}

function callExpressionsNamed(root: Node, name: string): CallExpression[] {
  const matches: CallExpression[] = [];
  walkSyntax(root, node => {
    if (node.type === "CallExpression"
      && node.callee.type === "Identifier"
      && node.callee.name === name) {
      matches.push(node);
    }
  });
  return matches;
}

// Every AST node extends `Span`, so this accepts the span rather than the node union and
// works for expressions, statements and arguments alike.
function sourceText(node: Span): string {
  return appSource.slice(node.start, node.end).replace(/\s+/g, " ");
}

function namedProperty(actions: ObjectExpression, name: string): ObjectProperty {
  return onlySyntaxNode(
    actions.properties.filter(
      (item): item is ObjectProperty =>
        item.type === "Property"
        && item.key.type === "Identifier"
        && item.key.name === name),
    `${name} property`);
}

function callbackProperty(actions: ObjectExpression, name: string): BlockArrowFunction {
  const value = namedProperty(actions, name).value;
  if (!isBlockArrowFunction(value)) {
    assert.fail(
      `${name} callback must be an arrow function with a block body, `
        + `found ${value.type}`);
  }
  return value;
}

function directCallExpression(
  statement: Statement | Directive,
  name: string,
): CallExpression | null {
  if (statement.type !== "ExpressionStatement") return null;
  const expression = statement.expression;
  return expression.type === "CallExpression"
    && expression.callee.type === "Identifier"
    && expression.callee.name === name
    ? expression
    : null;
}

// The name of a statement's direct call, or `null` when the statement is not a bare call.
// Three tests compare the order of binder calls and each carried its own copy of this.
function directCallName(statement: Statement | Directive): string | null {
  if (statement.type !== "ExpressionStatement") return null;
  const expression = statement.expression;
  return expression.type === "CallExpression"
    && expression.callee.type === "Identifier"
    ? expression.callee.name
    : null;
}

interface IfSignature {
  readonly if: string;
  readonly whenTrue: readonly StatementSignature[];
  readonly whenFalse: readonly StatementSignature[];
}

// A statement is summarised either as a single line of text or, for a branch, as the
// condition plus the summaries of each arm, so the tests can compare control flow without
// depending on formatting.
type StatementSignature = string | IfSignature;

function statementSignatures(
  statements: readonly (Statement | Directive)[],
): StatementSignature[] {
  return statements.map(statement => statementSignature(statement));
}

function branchSignatures(branch: Statement): StatementSignature[] {
  return branch.type === "BlockStatement"
    ? statementSignatures(branch.body)
    : [statementSignature(branch)];
}

function statementSignature(statement: Statement | Directive): StatementSignature {
  if (statement.type === "ExpressionStatement") {
    const expression = statement.expression;
    if (expression.type === "AssignmentExpression") {
      return `assign:${sourceText(expression.left)} ${expression.operator} ${sourceText(expression.right)}`;
    }
    if (expression.type === "CallExpression" && expression.callee?.type === "Identifier") {
      return `call:${expression.callee.name}(${expression.arguments.map(sourceText).join(", ")})`;
    }
    return `expression:${sourceText(expression)}`;
  }
  if (statement.type === "IfStatement") {
    return {
      if: sourceText(statement.test),
      whenTrue: branchSignatures(statement.consequent),
      whenFalse: statement.alternate ? branchSignatures(statement.alternate) : [],
    };
  }
  if (statement.type === "VariableDeclaration"
      && statement.declarations.length === 1) {
    const declaration = statement.declarations[0];
    if (declaration && declaration.id.type === "Identifier" && declaration.init) {
      return `declare:${statement.kind} ${declaration.id.name} = ${sourceText(declaration.init)}`;
    }
  }
  return `statement:${statement.type}:${sourceText(statement)}`;
}

const sourceRoot = fileURLToPath(new URL("../src/", import.meta.url));
// `readdirSync` without an encoding is typed as returning buffers as well as strings, so
// the encoding is explicit here to keep the entries `string`.
const productionTypeScriptSources = readdirSync(sourceRoot, {
  recursive: true,
  encoding: "utf8",
})
  .filter(path => path.endsWith(".ts"))
  .map(path => ({
    path,
    source: readFileSync(join(sourceRoot, path), "utf8"),
  }));
const workspaceNavigationSource = readFileSync(
  new URL("../src/workspace-navigation.ts", import.meta.url),
  "utf8");
const packageAcquisitionSource = readFileSync(
  new URL("../src/package-acquisition.ts", import.meta.url),
  "utf8");
const packageInspectionSource = readFileSync(
  new URL("../src/package-inspection.ts", import.meta.url),
  "utf8");
const packageViewSource = readFileSync(
  new URL("../src/package-view.ts", import.meta.url),
  "utf8");
const libraryControlsSource = readFileSync(
  new URL("../src/library-controls.ts", import.meta.url),
  "utf8");
const shellControlsSource = readFileSync(
  new URL("../src/shell-controls.ts", import.meta.url),
  "utf8");
const graphInteractionsSource = readFileSync(
  new URL("../src/graph-interactions.ts", import.meta.url),
  "utf8");
const sourceInspectionSource = readFileSync(
  new URL("../src/source-inspection.ts", import.meta.url),
  "utf8");
const metadataInspectionSource = readFileSync(
  new URL("../src/metadata-inspection.ts", import.meta.url),
  "utf8");
const memberDetailInspectionSource = readFileSync(
  new URL("../src/member-detail-inspection.ts", import.meta.url),
  "utf8");
const callGraphInspectionSource = readFileSync(
  new URL("../src/call-graph-inspection.ts", import.meta.url),
  "utf8");
const documentInspectionSource = readFileSync(
  new URL("../src/document-inspection.ts", import.meta.url),
  "utf8");
const spotlightPackageSearchSource = readFileSync(
  new URL("../src/spotlight-package-search.ts", import.meta.url),
  "utf8");
const catalogRequestsSource = readFileSync(
  new URL("../src/catalog-requests.ts", import.meta.url),
  "utf8");
const memberFocusSource = readFileSync(
  new URL("../src/member-focus.ts", import.meta.url),
  "utf8");
const graphSource = readFileSync(
  new URL("../src/graph-mermaid.ts", import.meta.url),
  "utf8");
const graphSourceViewerSource = readFileSync(
  new URL("../src/graph-source.ts", import.meta.url),
  "utf8");
const docViewerSource = readFileSync(
  new URL("../src/doc-viewer.ts", import.meta.url),
  "utf8");
const annotatedSourceModule = readFileSync(
  new URL("../src/annotated-source.ts", import.meta.url),
  "utf8");
const typePanelSource = readFileSync(
  new URL("../src/type-panel.ts", import.meta.url),
  "utf8");
const keybindingRegistrySource = readFileSync(
  new URL("../src/keybinding-registry.ts", import.meta.url),
  "utf8");
const workbenchKeybindingsSource = readFileSync(
  new URL("../src/workbench-keybindings.ts", import.meta.url),
  "utf8");
const scopeBarSource = readFileSync(
  new URL("../src/scope-bar.ts", import.meta.url),
  "utf8");
const settingsPanelSource = readFileSync(
  new URL("../src/settings-panel.ts", import.meta.url),
  "utf8");
const packageControlsSource = readFileSync(
  new URL("../src/package-controls.ts", import.meta.url),
  "utf8");
const workspaceSubjectSource = readFileSync(
  new URL("../src/workspace-subject.ts", import.meta.url),
  "utf8");
const metadataViewerSource = readFileSync(
  new URL("../src/metadata-viewer.ts", import.meta.url),
  "utf8");
const packageOpportunitiesSource = readFileSync(
  new URL("../src/package-opportunities.ts", import.meta.url),
  "utf8");
const applicationSources =
  `${appSource}\n${graphSource}\n${packageControlsSource}\n${workspaceSubjectSource}\n${metadataViewerSource}`;
const stylesSource = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");
const indexSource = readFileSync(new URL("../index.html", import.meta.url), "utf8");
// The production facade set: seven independently generated modules over one runtime. Each
// assertion below reads the module that owns the operation it is about, so an operation that
// moves to another facade fails here instead of matching a neighbouring module's text.
const generatedFacadeModules = [
  "inspect-web-host",
  "inspect-web-package",
  "inspect-web-metadata",
  "inspect-web-analysis",
  "inspect-web-source",
  "inspect-web-call-graph",
  "inspect-web-catalog",
] as const;
type GeneratedFacadeModule = typeof generatedFacadeModules[number];
const generatedFacadeModuleUrls = new Map<GeneratedFacadeModule, URL>(
  generatedFacadeModules.map(module =>
    [module, new URL(`../engine/wwwroot/${module}.js`, import.meta.url)]));
const generatedFacadeSources = new Map<GeneratedFacadeModule, string>(
  generatedFacadeModules.map(module =>
    [module, readFileSync(generatedFacadeModuleUrls.get(module)!, "utf8")]));
const generatedFacadeSource = (module: GeneratedFacadeModule): string =>
  generatedFacadeSources.get(module)!;
const generatedFacadeSourceText = generatedFacadeModules
  .map(module => generatedFacadeSource(module))
  .join("\n");
const engineCoordinatorSource = readFileSync(
  new URL("../src/engine-facades.ts", import.meta.url),
  "utf8");
const deploySource = readFileSync(
  new URL("../../../.github/workflows/deploy-inspect-web.yml", import.meta.url),
  "utf8");
const statusBarSource = readFileSync(
  new URL("../src/status-bar.ts", import.meta.url),
  "utf8");
const spotlightSource = readFileSync(
  new URL("../src/spotlight.ts", import.meta.url),
  "utf8");
const commandBarSource = readFileSync(
  new URL("../src/command-bar.ts", import.meta.url),
  "utf8");

test("platform type and member navigation hides package-only operations", () => {
  assert.deepEqual(
    typeLensesFor({ isRuntimePack: true }).map(([id]) => id),
    ["api"]);
  assert.deepEqual(
    memberSectionIdsFor({ kind: "method" }, true),
    ["overview", "call-graph"]);
  assert.deepEqual(
    memberSectionIdsFor({ kind: "method" }, false),
    ["overview", "call-graph", "facts", "source", "annotated"]);
  assert.deepEqual(
    memberSectionIdsFor({ kind: "property" }, false, true),
    ["overview", "call-graph", "facts", "annotated"]);
});

test("platform call graphs carry the target pack into lazy acquisition", () => {
  assert.equal(
    platformPackFromProvenance(
      "Microsoft.AspNetCore.Http",
      "aspnetcore.app",
      [],
      [],
      []),
    "aspnetcore.app");
  assert.equal(
    platformPackFromProvenance(
      "Microsoft.AspNetCore.Http",
      null,
      [],
      [],
      []),
    "netcore.app");
  assert.equal(
    platformPackFromAcquiredProvenance(
      "Microsoft.AspNetCore.Http",
      null,
      []),
    null);
  const acquiredRuntime = {
    activeFramework: "net10.0",
    assemblies: [{
      name: "Microsoft.AspNetCore.Http",
      platformPack: "aspnetcore.app",
    }],
  };
  assert.equal(
    platformPackForGraphAssembly(
      "Microsoft.AspNetCore.Http",
      null,
      acquiredRuntime,
      "net10.0"),
    "aspnetcore.app");
  assert.equal(
    platformPackForGraphAssembly(
      "Microsoft.AspNetCore.Http",
      null,
      acquiredRuntime,
      "net9.0"),
    null);
  assert.equal(
    platformPackForGraphAssembly(
      "Microsoft.AspNetCore.Http",
      null,
      null,
      "net10.0"),
    null);
  assert.equal(
    platformPackFromProvenance(
      "Microsoft.AspNetCore.Http",
      null,
      [{
        name: "Microsoft.AspNetCore.Http",
        platformPack: "aspnetcore.app"
      }],
      [],
      []),
    "aspnetcore.app");
  assert.match(
    appSource,
    /retainedPlatformTargetVersion\(\s*captured\.preservesBasis && runtimeIndex >= 0[\s\S]*callGraphInspection\.drill\(\{[\s\S]*platformVersion,/);
  assert.doesNotMatch(
    appSource,
    /platformVersion:\s*currentPackage\(\)\.version/);
  assert.match(
    appSource,
    /platformType:\s*type\.definitionId\s*\?\?\s*type\.metadataId[\s\S]*platformPack:\s*platformPackForAssembly\(type\.assembly,\s*type\.platformPack\)/);
  assert.match(
    callGraphInspectionSource,
    /pack:\s*request\.platformPack/);
  assert.match(
    appSource,
    /inspectExpandPlatformCallGraph\(\s*request\.framework,\s*request\.platformVersion,\s*request\.assembly,\s*request\.pack/);
});

test("platform pack inference rejects cross-family ambiguity", () => {
  assert.throws(
    () => platformPackFromProvenance(
      "Shared",
      null,
      [
        { name: "Shared", platformPack: "netcore.app" },
        { name: "Shared", platformPack: "aspnetcore.app" }
      ],
      [],
      []),
    /available from multiple platform packs/);
  assert.equal(
    platformPackFromProvenance(
      "Shared",
      null,
      [{ name: "Shared", platformPack: "netcore.app" }],
      [{ assembly: "Shared", pack: "aspnetcore.app" }],
      [{ assembly: "Shared", pack: "aspnetcore.app" }]),
    "netcore.app");
  assert.equal(
    platformPackFromProvenance(
      "Shared",
      null,
      [],
      [{ assembly: "Shared", pack: "aspnetcore.app" }],
      [{ assembly: "Shared", pack: "netcore.app" }]),
    "netcore.app");
  assert.equal(
    platformPackFromProvenance(
      "Shared",
      "aspnetcore.app",
      [{ name: "Shared", platformPack: "netcore.app" }],
      [],
      [{ assembly: "Shared", pack: "aspnetcore.app" }]),
    "aspnetcore.app");
  assert.match(
    appSource,
    /const resident = runtimeAssemblyIsResident\(\s*runtimePackPackage\(\),\s*key,\s*pack \?\? ""\)/);
});

test("runtime graph acquisition ignores a resident pack from another TFM", () => {
  const stale = {
    id: "Microsoft.NETCore.App",
    activeFramework: "net8.0",
    isRuntimePack: true,
    assemblies: [{
      id: "console",
      name: "System.Console",
      version: "8.0.0.0",
      culture: null,
      publicKeyToken: "b03f5f7f11d50a3a"
    }],
    types: [{
      id: "System.Console:System.Console",
      definitionId: "System.Console",
      assemblyId: "console",
      assemblyName: "System.Console"
    }]
  };
  const net9Target = {
    assembly: "System.Console",
    assemblyVersion: "9.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: "b03f5f7f11d50a3a",
    typeDefinitionId: "System.Console",
    kind: "external"
  };

  const usable = runtimePackForFramework(stale, "net9.0");
  assert.equal(usable, null);
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "missing" },
      usable
        ? resolveRuntimeGraphTargetCandidate(usable, net9Target)
        : null,
      net9Target),
    "platform");

  const matching = runtimePackForFramework(stale, "NET8.0");
  assert.equal(matching, stale);
  assert.equal(
    resolveRuntimeGraphTargetCandidate(
      matching,
      { ...net9Target, assemblyVersion: "8.0.0.0" }).status,
    "unique");
  assert.equal(
    appSource.match(
      /const pack = runtimePackForFramework\(\s*runtimePackPackage\(\),\s*state\.package\?\.activeFramework \|\| ""\)/g)?.length,
    2);
  assert.match(
    appSource,
    /let pack = runtimePackForFramework\(\s*runtimePackPackage\(\),\s*framework\)/);
});

test("platform library selection remains distinct from canonical Platform identity", () => {
  assert.equal(platformPackToken("aspnetcore.app"), "aspnetcore.app");
  assert.equal(platformPackToken("netcore.app"), "netcore.app");
  assert.equal(platformPackToken("unknown.app"), null);
  assert.match(
    workspaceNavigationSource,
    /tab\.kind === "group" && tab\.source === ":Platform"/);
  assert.match(
    workspaceNavigationSource,
    /id: "Microsoft\.NETCore\.App"/);
  assert.match(
    appSource,
    /platformPackForAssembly\(key,\s*libraryPack\)/);
});

test("platform inspection notices survive cumulative surface loads", () => {
  assert.equal(
    mergeInspectionErrors("", "System.Synthetic: omitted 1 metadata row."),
    "System.Synthetic: omitted 1 metadata row.");
  assert.equal(
    mergeInspectionErrors(
      "First: omitted 1 metadata row.",
      "Second: omitted 2 metadata rows."),
    "First: omitted 1 metadata row.; Second: omitted 2 metadata rows.");
  assert.equal(
    mergeInspectionErrors(
      "First: omitted 1 metadata row.",
      "First: omitted 1 metadata row."),
    "First: omitted 1 metadata row.");
  assert.equal(
    mergeInspectionErrors(
      "First: truncated; 0 assemblies were not projected.",
      "Second: omitted 2 metadata rows."),
    "First: truncated; 0 assemblies were not projected.; "
      + "Second: omitted 2 metadata rows.");
  const entries = mergeInspectionErrorEntries(
    [
      "First: truncated; 0 assemblies were not projected.",
      "Second: truncated; 0 assemblies were not projected.",
    ],
    ["Second: truncated; 0 assemblies were not projected."]);
  assert.deepEqual(entries, [
    "First: truncated; 0 assemblies were not projected.",
    "Second: truncated; 0 assemblies were not projected.",
  ]);
  assert.equal(
    renderInspectionErrors(entries),
    "First: truncated; 0 assemblies were not projected.; "
      + "Second: truncated; 0 assemblies were not projected.");
  assert.match(
    packageAcquisitionSource,
    /existing\.inspectionErrors\s*=\s*mergeInspectionErrorEntries\(/);
  assert.match(
    packageAcquisitionSource,
    /inspectionError:\s*renderInspectionErrors\(inspectionErrors\)/);
});

test("typed Spotlight owns search presentation and hosts commands", () => {
  assert.match(
    appSource,
    /createSpotlight,[\s\S]*visibleSpotlightPackageHits,[\s\S]*from "\.\/spotlight\.ts"/);
  assert.match(appSource, /openSpotlight\("", "commands"\)/);
  assert.match(appSource, /state\.spotlightOpen \? spotlight\.modalHtml\(\)/);
  assert.match(
    appSource,
    /spotlight\.inlineHtml\(enginePending, showReadyGlint\)/);
  assert.doesNotMatch(appSource, /function renderSpotlight\(/);
  assert.doesNotMatch(appSource, /commandBar\.html\(\)/);
  assert.match(spotlightSource, /const COMMAND_SCOPE = \{ id: "commands"/);
  assert.match(
    spotlightSource,
    /type HighlightRange = readonly \[start: number, end: number\]/);
  assert.match(spotlightSource, /function handleModalKeys\(event: KeyboardEvent\)/);
  assert.match(commandBarSource, /export function commandPaletteResults\(/);
});

test("workspace data bar receives package acquisition provenance", () => {
  assert.match(appSource, /source: pkg\.source/);
  assert.match(
    appSource,
    /createPackageAcquisition\(\{[\s\S]*queryPackage:[\s\S]*loadRuntimePack:[\s\S]*loadRuntimePackAssembly:/);
  assert.match(appSource, /packageAcquisition\.loadPackage\(\{/);
  assert.match(appSource, /packageAcquisition\.loadRuntimePack\(/);
  assert.match(appSource, /packageAcquisition\.loadRuntimePackAssembly\(/);
  assert.doesNotMatch(appSource, /runtimePackLoadPromise|waitForRuntimePackLoad/);
  assert.match(
    appSource,
    /interface RuntimeLoadResult \{[\s\S]*failureMessage: string;[\s\S]*const result = await packageAcquisition\.loadRuntimePack\([\s\S]*failureMessage: result\.error === null \? "" : errorMessage\(result\.error\)/);
  assert.match(packageAcquisitionSource, /source: \{ kind: "nuget\.org" \}/);
  assert.match(packageAcquisitionSource, /source: \{ kind: "platform" \}/);
  assert.doesNotMatch(appSource, /source: \{ kind: "(?:nuget\.org|platform)" \}/);
  assert.match(statusBarSource, /Source: \$\{escapeHtml\(packageSourceLabel\(model\.source\)\)\}/);
});

test("typed status bar owns its rendered toggle binding", () => {
  const binding =
    appSource.match(/function bindStatusBarEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    binding,
    /bindStatusBar\(document, \{\s*onToggle: \(\) => \{[\s\S]*state\.statusBarExpanded = !state\.statusBarExpanded;[\s\S]*render\(\);[\s\S]*\},\s*\}\)/);
  assert.equal(binding.match(/\bdocument\b/g)?.length, 1);
  assert.match(
    statusBarSource,
    /export function bindStatusBar\([\s\S]*\[data-status-bar-toggle-button\][\s\S]*actions\.onToggle/);
  assert.doesNotMatch(appSource, /\[data-status-bar-toggle-button\]/);
  assert.match(
    appSource,
    /function bindEvents\(\) \{\s*bindStatusBarEvents\(\);/);
  assert.match(
    appSource,
    /function bindHomeEvents\(\) \{\s*bindStatusBarEvents\(\);/);
  assert.equal(
    appSource.match(/\bbindStatusBarEvents\(\)/g)?.length,
    4);
});

test("typed package controls own framework and version selection bindings", () => {
  const packageControlsCreation =
    appSource.match(/const packageControls = createPackageControls\(\{[\s\S]*?\n}\);/)?.[0]
    ?? "";
  const packageControlsBinding =
    packageControlsSource.match(/  function bind\(root: ParentNode\): void \{[\s\S]*?\n  }(?=\n\n  return)/)?.[0]
    ?? "";
  const workspaceBinding =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    packageControlsCreation,
    /selectFramework: framework =>\s*observeAsync\(\s*switchPackageFramework\(framework\),\s*"Switching the package framework"\),[\s\S]*selectVersion: version => \{[\s\S]*state\.package\?\.isRuntimePack[\s\S]*observeAsync\(\s*switchPlatformVersion\(version\),\s*"Switching the platform version"\);[\s\S]*else\s*observeAsync\(\s*switchPackageVersion\(version\),\s*"Switching the package version"\)/);
  assert.match(
    packageControlsSource,
    /export function bindPackageSelections\([\s\S]*#framework[\s\S]*#package-version/);
  assert.match(
    appSource,
    /function packageCoordinateFields\(\)[\s\S]*id="package-version"[\s\S]*id="framework"/);
  assert.match(
    packageControlsBinding,
    /bindPackageSelections\(root, \{\s*onFrameworkSelect: selectFramework,\s*onVersionSelect: selectVersion,\s*\}\)/);
  assert.equal(
    workspaceBinding.match(/\bpackageControls\.bind\(document\)/g)?.length,
    1);
  assert.doesNotMatch(
    workspaceBinding,
    /document\.querySelectorAll(?:<HTMLElement>)?\("\[data-framework-chip\]"\)/);
  assert.doesNotMatch(
    workspaceBinding,
    /document\.querySelector(?:<HTMLSelectElement>)?\("#(?:framework|package-version)"\)/);
});

test("explicit coordinate changes discard a floating canonical basis", () => {
  const packageVersion = appSource.match(
    /async function switchPackageVersion\([\s\S]*?\n}/)?.[0] ?? "";
  const packageFramework = appSource.match(
    /async function switchPackageFramework\([\s\S]*?\n}/)?.[0] ?? "";
  const platformVersion = appSource.match(
    /async function switchPlatformVersion\([\s\S]*?\n}/)?.[0] ?? "";
  const packageLoader = appSource.match(
    /async function loadPackage\([\s\S]*?\n}(?=\n\nfunction )/)?.[0] ?? "";

  assert.match(packageVersion, /invalidateWorkspaceShareBasis: true/);
  assert.match(packageFramework, /invalidateWorkspaceShareBasis: true/);
  assert.match(
    packageLoader,
    /if \(options\.invalidateWorkspaceShareBasis\)\s*state\.workspaceShareBasis = null;\s*activatePackage/);
  assert.match(
    platformVersion,
    /if \(!loaded\)[\s\S]*return;[\s\S]*state\.workspaceShareBasis = null;[\s\S]*activatePackage/);
  assert.doesNotMatch(
    platformVersion.slice(0, platformVersion.indexOf("await loadRuntimePack(")),
    /state\.packages =|state\.libraryScope = null|state\.platformStack = \[\]/);
  assert.match(
    platformVersion,
    /if \(!loaded\)[\s\S]*appendQueryNotice\([\s\S]*render\(\);\s*return;/);
  assert.match(
    platformVersion,
    /state\.workspaceShareBasis = null;\s*state\.platformStack = \[\];\s*activatePackage[\s\S]*state\.libraryScope = null/);
});

test("typed package inspection owns package-root request coordination", () => {
  const dependenciesLoader =
    appSource.match(/async function loadPackageDependencies\(\) \{[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /createPackageInspectionCoordinator\(\{[\s\S]*queryDependencies:[\s\S]*queryPackageIntegrations:[\s\S]*queryPlatformMetadata:/);
  assert.match(
    appSource,
    /createPackageInspectionCoordinator\(\{[\s\S]*render: renderPreservingMemberFocus,[\s\S]*renderDependencyGraph,/);
  assert.match(
    appSource,
    /queryDependencies: packageModel => inspectPackageDependencies\(\s*packageModel\.id,\s*packageModel\.version,\s*packageModel\.activeFramework,\s*packageModel\.assemblyId\)/);
  for (const engine of [
    "inspectPackageIntegrations",
    "inspectPackageOpportunities",
    "inspectPackagePerformance",
    "inspectPackageMetadata",
  ]) {
    assert.match(
      appSource,
      new RegExp(
        `${engine}\\(\\s*packageModel\\.id,\\s*`
        + "packageModel\\.version,\\s*packageModel\\.activeFramework\\)"));
  }
  for (const engine of [
    "inspectPlatformIntegrations",
    "inspectPlatformOpportunities",
    "inspectPlatformPerformance",
    "inspectPlatformMetadata",
  ]) {
    assert.match(
      appSource,
      new RegExp(
        `${engine}\\(\\s*framework,\\s*platformVersion,\\s*`
        + "assemblyFileName,\\s*pack\\)"));
  }
  assert.match(
    dependenciesLoader,
    /function loadPackageDependencies\(\) \{\s*return packageInspection\.loadDependencies\(/);
  assert.match(appSource, /packageInspection\.ensureWorkspaceDependencies\(\)/);
  assert.match(appSource, /packageInspection\.loadIntegrations\(/);
  assert.match(appSource, /packageInspection\.loadOpportunities\(/);
  assert.match(appSource, /packageInspection\.loadPerformance\(/);
  assert.match(appSource, /packageInspection\.loadMetadata\(/);
  assert.match(
    packageInspectionSource,
    /async loadDependencies\(packageModel, signature\)[\s\S]*state\.packageDependenciesKey/);
  assert.doesNotMatch(dependenciesLoader, /state\.packageDependenciesKey/);
});

test("typed package view owns package navigation bindings", () => {
  const binding =
    appSource.match(/const packageViewActions: PackageViewBindingActions = \{[\s\S]*?\n};/)?.[0]
    ?? "";
  const workspaceBinding =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}\n\nfunction toggleTheme/)?.[0]
    ?? "";
  const packageViewBinding =
    appSource.match(/function bindPackageViewEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction bindPackageDependencyListEvents)/)?.[0]
    ?? "";
  const dependencyListBinding =
    appSource.match(/function bindPackageDependencyListEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction bindStatusBarEvents)/)?.[0]
    ?? "";
  const dependencyPatch =
    appSource.match(/function patchDependenciesGroup\(\) \{[\s\S]*?\n}/)?.[0]
    ?? "";
  const actionSource = (name: string) =>
    binding.match(
      new RegExp(`  ${name}: [\\s\\S]*?(?=\\n  on[A-Z])`))?.[0]
      ?? "";
  assert.match(
    packageViewSource,
    /export function bindPackageView\([\s\S]*\[data-dep-group\][\s\S]*\[data-kind-jump\][\s\S]*\[data-namespace-jump\][\s\S]*\[data-lib-scope\][\s\S]*\[data-graph-type\][\s\S]*\[data-perf-selector\]/);
  assert.match(
    packageViewSource,
    /export function bindPackageDependencyList\([\s\S]*\[data-dep-open\][\s\S]*\[data-dep-load\]/);
  assert.equal(
    workspaceBinding.match(/\bbindPackageViewEvents\(\)/g)?.length,
    1);
  assert.match(
    packageViewBinding,
    /bindPackageView\(document, packageViewActions\)/);
  assert.doesNotMatch(
    packageViewBinding,
    /\bquerySelector(?:All)?\b|\baddEventListener\b/);
  assert.match(
    dependencyListBinding,
    /bindPackageDependencyList\(document, packageViewActions\)/);
  assert.doesNotMatch(
    dependencyListBinding,
    /\bquerySelector(?:All)?\b|\baddEventListener\b/);
  assert.match(
    dependencyPatch,
    /listSection\.outerHTML = dependencyListSectionHtml[\s\S]*bindPackageDependencyListEvents\(\);[\s\S]*renderDependencyGraph\(\)/);
  assert.match(
    binding,
    /onDependencyGroupSelect: index => \{[\s\S]*state\.dependenciesGroupIndex === index[\s\S]*state\.dependenciesGroupIndex = index;[\s\S]*patchDependenciesGroup\(\)/);
  assert.match(
    binding,
    /onDependencyLoad: \(id, version\) =>\s*observeAsync\(\s*openDependencyPackage\(id, version\),\s*"Opening a dependency package"\),\s*onDependencyOpen: switchToPackageForDependencies,\s*onGraphTypeSelect: navigateToTypeByName/);
  const kindJump = actionSource("onKindJump");
  const libraryJump = actionSource("onLibraryScopeSelect");
  const namespaceJump = actionSource("onNamespaceJump");
  assert.match(
    kindJump,
    /state\.atPackageRoot = false;[\s\S]*state\.kindFilter = kind;[\s\S]*state\.namespaceFilter = ""/);
  assert.match(
    libraryJump,
    /state\.atPackageRoot = false;[\s\S]*if \(!library\) return;[\s\S]*state\.libraryScope = new Set\(\[library\]\);[\s\S]*state\.package\?\.isRuntimePack[\s\S]*recordPlatformRecent\(library, platformPackForAssembly\(library\)\);[\s\S]*state\.kindFilter = kind;[\s\S]*state\.namespaceFilter = ""/);
  assert.match(
    namespaceJump,
    /state\.atPackageRoot = false;[\s\S]*state\.namespaceFilter = namespace;[\s\S]*state\.kindFilter = ""/);
  for (const source of [kindJump, libraryJump, namespaceJump]) {
    assert.match(
      source,
      /state\.typeFilter = "";[\s\S]*state\.selectedMemberKey = "";[\s\S]*state\.memberBrowseTypeId = "";[\s\S]*resetMemberFilters\(\);[\s\S]*state\.typeCursor = 0;[\s\S]*const first = filteredTypes\(\)\[0\];[\s\S]*if \(first\) state\.selectedTypeId = first\.id;[\s\S]*render\(\)/);
    assert.equal(source.match(/\brender\(\)/g)?.length, 1);
  }
  assert.match(
    binding,
    /onPerformanceMemberSelect: target => \{[\s\S]*drillToPerfMember\(\s*target\.stableSelector,\s*target\.assembly,\s*target\.typeId\)/);
  assert.match(
    appSource,
    /function drillToPerfMember\([\s\S]*resetMemberSectionState\(\);[\s\S]*loadSelectedMemberDocumentation\(\)/);
  assert.doesNotMatch(
    appSource.match(
      /function drillToPerfMember\([\s\S]*?\n}/)?.[0] ?? "",
    /memberSection = "facts"|loadSelectedMemberFacts\(\)/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelectorAll<HTMLElement>\("\[data-(?:dep-group|dep-open|dep-load|kind-jump|namespace-jump|lib-scope|graph-type|perf-selector)\]"\)/);
  assert.doesNotMatch(
    workspaceBinding,
    /\[data-(?:dep-group|dep-open|dep-load|kind-jump|namespace-jump|lib-scope|graph-type|perf-selector)\]/);
  assert.doesNotMatch(appSource, /function bindDependencyListHandlers\(/);
});

test("typed library controls own library and Platform picker bindings", () => {
  const binding =
    appSource.match(/const libraryControlActions: LibraryControlBindingActions = \{[\s\S]*?\n};/)?.[0]
    ?? "";
  const wrapper =
    appSource.match(/function bindLibraryControlsEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction bindTypePanelEvents)/)?.[0]
    ?? "";
  const workspaceBinding =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}\n\nfunction toggleTheme/)?.[0]
    ?? "";
  assert.match(
    libraryControlsSource,
    /export function bindLibraryControls\([\s\S]*\[data-library-chip\][\s\S]*\[data-access-chip\][\s\S]*#library-jump[\s\S]*\[data-platform-library-select\]/);
  for (const lens of [
    "integrations",
    "opportunities",
    "analysis",
    "metadata",
  ]) {
    assert.match(
      libraryControlsSource,
      new RegExp(`\\[data-platform-${lens}-library\\]`));
  }
  assert.equal(
    workspaceBinding.match(/\bbindLibraryControlsEvents\(\)/g)?.length,
    1);
  assert.match(
    wrapper,
    /bindLibraryControls\(document, libraryControlActions\)/);
  assert.doesNotMatch(
    wrapper,
    /\bquerySelector(?:All)?\b|\baddEventListener\b/);
  assert.match(
    binding,
    /onAccessibilityChipSelect: accessibility => \{[\s\S]*toggleAccessibilityChip\(accessibility\);[\s\S]*afterLibraryScopeChange\(\)/);
  assert.match(
    binding,
    /onLibraryChipSelect: library => \{[\s\S]*toggleLibraryChip\(library\);[\s\S]*afterLibraryScopeChange\(\)/);
  assert.match(
    binding,
    /onLibraryJump: library => \{[\s\S]*state\.libraryScope = library \? new Set\(\[library\]\) : null;[\s\S]*afterLibraryScopeChange\(\)/);
  assert.match(
    binding,
    /onPlatformLibrarySelect: \(name, pack\) =>\s*observeAsync\(\s*openPlatformLibrary\(name, pack\),\s*"Opening a platform library"\)/);
  assert.match(
    appSource,
    /const requiresSelection = options\.requireSelection === true && !scoped;[\s\S]*?<option value="" selected disabled>Choose a library<\/option>/);
  assert.equal(
    appSource.match(/requireSelection: true/g)?.length,
    1);
  assert.match(
    binding,
    /onPlatformLensLibrarySelect: \(lens, name, pack\) =>\s*observeAsync\(\s*openPlatformLensLibrary\(lens, name, pack\),\s*"Opening a platform library"\)/);
  assert.doesNotMatch(
    workspaceBinding,
    /\[data-(?:library-chip|access-chip|platform-(?:library-select|integrations-library|opportunities-library|analysis-library|metadata-library))\]|#library-jump/);
  assert.doesNotMatch(
    appSource,
    /\[data-(?:library-chip|access-chip|platform-(?:library-select|integrations-library|opportunities-library|analysis-library|metadata-library))\]|#library-jump/);
  assert.doesNotMatch(appSource, /bindPlatformLensPicker/);
});

test("type accessibility controls offer an all-access selection", () => {
  const toggle =
    appSource.match(/function toggleAccessibilityChip\([\s\S]*?\n}(?=\n\n\/\/ The accessibility selector)/)?.[0]
    ?? "";
  const control =
    appSource.match(/function accessibilityControl\(\) \{[\s\S]*?\n}(?=\n\n\/\/ Options for the namespace picker)/)?.[0]
    ?? "";

  assert.match(
    toggle,
    /if \(!bucket\) \{[\s\S]*new Set\(accessibilityBuckets\(\)\.map\(descriptor => descriptor\.id\)\);[\s\S]*return;/);
  assert.match(
    control,
    /const allOn = buckets\.every\([\s\S]*data-access-chip="">all access<\/button>/);
});

test("typed shell controls own workbench, home, and load-error bindings", () => {
  const workbenchActions =
    appSource.match(/const workbenchShellActions: WorkbenchShellBindingActions = \{[\s\S]*?\n};/)?.[0]
    ?? "";
  const homeActions =
    appSource.match(/const homeShellActions: HomeShellBindingActions = \{[\s\S]*?\n};/)?.[0]
    ?? "";
  const loadErrorActions =
    appSource.match(/const loadErrorShellActions: LoadErrorShellBindingActions = \{[\s\S]*?\n};/)?.[0]
    ?? "";
  const workspaceBinding =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}\n\nfunction toggleTheme/)?.[0]
    ?? "";
  const homeBinding =
    appSource.match(/function bindHomeEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction openProductDemos)/)?.[0]
    ?? "";
  const loadingBinding =
    appSource.match(/function renderLoading\(\) \{[\s\S]*?\n}(?=\n\nasync function loadSelectedMemberDocumentation)/)?.[0]
    ?? "";
  assert.match(
    shellControlsSource,
    /export function bindWorkbenchShell\([\s\S]*\[data-subject-copy\][\s\S]*#application-menu-button[\s\S]*#application-menu[\s\S]*\[data-application-action\][\s\S]*#dismiss-notice[\s\S]*#retry-notice[\s\S]*#dismiss-package-notice[\s\S]*#nav-back[\s\S]*#nav-forward[\s\S]*#open-search[\s\S]*export function focusWorkbenchSearch\([\s\S]*#open-search/);
  assert.match(
    shellControlsSource,
    /export function bindHomeShell\([\s\S]*#home-theme[\s\S]*#dismiss-notice[\s\S]*#home-credits[\s\S]*#home-demos/);
  assert.match(
    shellControlsSource,
    /export function bindLoadErrorShell\([\s\S]*#retry-load[\s\S]*#error-package-query[\s\S]*#error-package-input[\s\S]*#toggle-error-detail[\s\S]*\.load-error-detail/);
  assert.match(
    shellControlsSource,
    /import \{\s*parsePackageQuery,\s*type ParsedPackageQuery,\s*\} from "\.\/package-controls\.ts"/);
  assert.equal(
    workspaceBinding.match(/\bbindWorkbenchShell\(\b/g)?.length,
    1);
  assert.match(
    workspaceBinding,
    /bindWorkbenchShell\(document, workbenchShellActions\)/);
  assert.match(
    homeBinding,
    /bindHomeShell\(document, homeShellActions\)[\s\S]*spotlight\.bind\(document, "inline"\)[\s\S]*#spotlight-input/);
  assert.match(
    loadingBinding,
    /app\.innerHTML = `[\s\S]*bindLoadErrorShell\(document, loadErrorShellActions\)/);
  assert.match(
    workbenchActions,
    /onApplicationAction: dispatchApplicationAction,\s*onCopySubjectSegment: index => \{[\s\S]*currentInspectedSubjectPath\(\)\[index\][\s\S]*copyText\(segment\.label, `\$\{segment\.kind\} name copied`\)[\s\S]*onDismissNotice: dismissQueryNotice,\n  onDismissPackageNotice:/);
  assert.match(
    workbenchActions,
    /onDismissPackageNotice: \(\) => \{[\s\S]*pkg\.inspectionErrors = \[\];[\s\S]*pkg\.inspectionError = "";[\s\S]*render\(\);\s*\},\n  onNavigateBack:/);
  assert.match(
    workbenchActions,
    /onNavigateBack: navBack,[\s\S]*onNavigateForward: navForward,[\s\S]*onRetryNotice: \(\) => \{[\s\S]*state\.queryNoticeRetryAction;[\s\S]*if \(retryAction\) observeAction\(retryAction, "Retrying the inspection"\);[\s\S]*onSearch: \(\) => openSpotlight\(\)/);
  assert.match(
    homeActions,
    /onDismissNotice: dismissQueryNotice,\s*onOpenDemos: openProductDemos,\s*onOpenCredits: openCredits,\s*onToggleTheme: toggleTheme/);
  assert.match(
    loadErrorActions,
    /onOpenPackage: openPackageQuery,\s*onRetry: \(\) => \{\s*if \(state\.retryAction === retryUnavailable\) return;\s*observeAction\(\s*state\.retryAction \?\? bootstrap,\s*"Retrying the inspection"\);\s*\}/);
  assert.doesNotMatch(
    appSource,
    /\bquerySelector(?:All)?(?:<[^>]+>)?\("(?:#(?:share|dismiss-notice|retry-notice|dismiss-package-notice|nav-back|nav-forward|open-search|help|home-theme|home-demos|home-credits|retry-load|error-package-query|error-package-input|toggle-error-detail)|\[data-subject-copy\]|\.load-error-detail)"\)/);
  assert.doesNotMatch(
    workspaceBinding,
    /#(?:share|dismiss-notice|retry-notice|dismiss-package-notice|nav-back|nav-forward|open-search|help)/);
  assert.doesNotMatch(homeBinding, /#(?:home-theme|dismiss-notice|home-demos|home-credits)/);
  assert.doesNotMatch(
    loadingBinding,
    /#(?:retry-load|error-package-query|error-package-input|toggle-error-detail)|"\.load-error-detail"/);
});

test("keyboard help projects available global and current graph bindings", () => {
  const openKeyboardHelp =
    appSource.match(/function openKeyboardHelp\(\) \{[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    openKeyboardHelp,
    /querySelector<HTMLElement>\("\.graph-viewport"\)[\s\S]*keybindings\.availableBindingsFor\(\)[\s\S]*keybindings\.availableBindingsFor\(graphViewport\)[\s\S]*state\.keyboardHelp = true/);
  assert.match(
    shellControlsSource,
    /\["graph\.zoom", "Zoom the current graph"\][\s\S]*\["graph\.pan-horizontal", "Pan the current graph horizontally"\][\s\S]*\["graph\.pan-vertical", "Pan the current graph vertically"\]/);
  assert.match(
    appSource,
    /const inspectionNavigationIsAvailable = \(\) =>\s*workspaceKeyboardContextIsActive\(\) && scope\(\) !== "workspace"/);
  assert.match(
    appSource,
    /const workspaceDrillInIsAvailable = \(\) =>\s*workspaceKeyboardContextIsActive\(\) && state\.package !== null/);
  const renderWorkspaceFocus =
    appSource.match(
      /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\) \{[\s\S]*?\n}\n\nfunction renderWorkspaceCatalogView/,
    )?.[0]
    ?? "";
  assert.equal(
    renderWorkspaceFocus.match(
      /restoreWorkspaceFocus\(document, workspaceFocus\)/g,
    )?.length,
    2);
  assert.match(
    renderWorkspaceFocus,
    /const workspaceFocus = captureWorkspaceFocus\(focusedElement\);[\s\S]*renderWorkspaceCatalogView\(\);[\s\S]*else if \(workspaceFocus\) \{\s*restoreWorkspaceFocus\(document, workspaceFocus\);[\s\S]*recordNav\(\);\s*return;/);
  const catalogRenderer =
    appSource.match(/function renderWorkspaceCatalogView\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    catalogRenderer,
    /applicationScopeHtml: renderApplicationScopeBar\(\s*"workspace",\s*true,\s*escapeHtml\)[\s\S]*<main id="subject-panel" class="workspace" role="tabpanel" aria-labelledby="application-scope-workspace">/);
  assert.match(
    appSource,
    /function drillIn\(\) \{\s*if \(scope\(\) === "workspace"\) \{\s*if \(!state\.package\) return;/);
  const drillInBinding =
    appSource.match(/keybindings\.register\(\{\s*id: "workspace\.drill-in"[\s\S]*?\n}\);/)?.[0]
    ?? "";
  assert.match(
    drillInBinding,
    /available: workspaceDrillInIsAvailable/);
  assert.match(drillInBinding, /run: \(\) => \{\s*drillIn\(\);/);
  assert.match(
    appSource,
    /const workspaceHistoryBackIsAvailable = \(\) =>\s*workspaceKeyboardContextIsActive\(\) && navigationHistory\.canBack\(\)/);
  assert.match(
    appSource,
    /const workspaceHistoryForwardIsAvailable = \(\) =>\s*workspaceKeyboardContextIsActive\(\) && navigationHistory\.canForward\(\)/);
  assert.match(
    appSource,
    /\["ArrowLeft", navBack, workspaceHistoryBackIsAvailable\][\s\S]*\["ArrowRight", navForward, workspaceHistoryForwardIsAvailable\][\s\S]*available,/);
});

test("delayed Share completion preserves newer focus ownership", () => {
  const share =
    appSource.match(/async function share\(\) \{[\s\S]*?\n}/)?.[0] ?? "";
  assert.match(
    share,
    /const focusOwner = captureApplicationMenuFocusOwner\(document\)/);
  assert.match(
    share,
    /requestAnimationFrame\(\(\) =>\s*restoreApplicationMenuFocusIfOwned\(document, focusOwner\)\)/);
});

test("deferred Spotlight focus preserves newer document focus", () => {
  const focusGuard =
    appSource.match(/function canRestoreWorkbenchFocus\([\s\S]*?\n}/)?.[0]
    ?? "";
  const focusTypeList =
    appSource.match(/function focusTypeList\([\s\S]*?\n}/)?.[0] ?? "";
  assert.match(focusGuard, /applicationMenuOwnsFocus\(document\)/);
  assert.equal(
    focusTypeList.match(
      /canRestoreWorkbenchFocus\(generation, focusGeneration\)/g)?.length,
    3);
});

test("the shell separates typed target and Subject navigation rows", () => {
  const renderNode = functionDeclaration("render");
  const subjectPathNode = functionDeclaration("inspectedSubjectPath");
  const subjectPathRenderer = functionDeclaration("renderInspectedSubjectPath");
  const subjectIconRenderer =
    functionDeclaration("renderInspectedSubjectIcon");
  const render = appSource.slice(renderNode.start, renderNode.end);
  const subjectPath = appSource.slice(subjectPathNode.start, subjectPathNode.end);
  const renderer =
    appSource.slice(subjectPathRenderer.start, subjectPathRenderer.end);
  const iconRenderer =
    appSource.slice(subjectIconRenderer.start, subjectIconRenderer.end);

  assert.match(
    render,
    /workbenchShellHtml\(\{[\s\S]*contextualActionsHtml:[\s\S]*class="working-surface-actions"[\s\S]*inspectedTargetHtml:[\s\S]*class="inspected-target"[\s\S]*renderInspectedSubjectIcon\(pkg\)[\s\S]*class="subject-path"[\s\S]*subjectInspectorHtml: renderScopeBar\(\)[\s\S]*titleNavigationHtml: renderTitleNavigation\([\s\S]*<main id="subject-panel" class="workspace\$\{contentFrameEnabled[\s\S]*renderApplicationMenu\(true\)/);
  assert.doesNotMatch(render, /id="copy-name"|id="taste-btn"/);
  assert.doesNotMatch(
    render,
    /<section class="detail-pane">\s*<header class="detail-head">/);
  assert.match(
    subjectPath,
    /kind: "package"[\s\S]*label: packageDisplayName\(pkg\)[\s\S]*kind: "type"[\s\S]*current\.namespace[\s\S]*kind: "member"[\s\S]*label: member\.name/);
  assert.match(
    renderer,
    /segment\.label[\s\S]*segment\.copyable[\s\S]*data-subject-copy="\$\{index\}"[\s\S]*segment\.kind/);
  assert.match(
    iconRenderer,
    /pkg\.icon[\s\S]*data:\$\{pkg\.icon\.mediaType\};base64,\$\{pkg\.icon\.base64\}[\s\S]*NUGET_DEFAULT_PACKAGE_ICON/);
  assert.doesNotMatch(iconRenderer, /⬡|iconUrl/);
  assert.match(
    appSource,
    /NUGET_DEFAULT_PACKAGE_ICON[\s\S]*default-package-icon-256x256\.png[\s\S]*data-package-icon[\s\S]*packageIcon\.onerror =[\s\S]*packageIcon\.src = NUGET_DEFAULT_PACKAGE_ICON/);
});

test("typed graph interactions own graph controls and Mermaid node bindings", () => {
  const workspaceBinding =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}\n\nfunction toggleTheme/)?.[0]
    ?? "";
  const typeGraph =
    appSource.match(/async function renderTypeGraph\(\) \{[\s\S]*?\n}(?=\n\nfunction navigateToTypeByName)/)?.[0]
    ?? "";
  const dependencyGraph =
    appSource.match(/async function renderDependencyGraph\(\) \{[\s\S]*?\n}(?=\n\nfunction switchToPackageForDependencies)/)?.[0]
    ?? "";
  const callGraph =
    appSource.match(/function renderMermaidCallGraph\(\): Promise<CallGraphRenderResult> \{[\s\S]*?\n}(?=\n\nfunction callGraphNodeBinding)/)?.[0]
    ?? "";
  const callGraphBinding =
    appSource.match(/function callGraphNodeBinding\([\s\S]*?\n}(?=\n\nfunction currentCallGraph)/)?.[0]
    ?? "";
  const dependencyNodeBinding =
    graphInteractionsSource.match(/export function bindDependencyGraphNodes\([\s\S]*?\n}(?=\n\nexport function bindGraphPanZoom)/)?.[0]
    ?? "";
  assert.match(
    graphInteractionsSource,
    /export function bindGraphBack\([\s\S]*\[data-graph-back\]/);
  assert.match(
    graphInteractionsSource,
    /function mermaidNodeId\([\s\S]*data-id[\s\S]*flowchart-/);
  assert.match(
    graphInteractionsSource,
    /export function bindGraphPanZoom\([\s\S]*"wheel"[\s\S]*"pointerdown"[\s\S]*"pointermove"[\s\S]*"pointerup"[\s\S]*"pointercancel"[\s\S]*\.graph-controls button[\s\S]*id: "graph\.zoom"[\s\S]*id: "graph\.pan-horizontal"[\s\S]*id: "graph\.pan-vertical"[\s\S]*resolveCallGraphNode/);
  assert.match(
    graphInteractionsSource,
    /export function bindTypeGraphNodes\([\s\S]*"t"[\s\S]*nav-node[\s\S]*non-nav[\s\S]*createElementNS/);
  assert.match(
    dependencyNodeBinding,
    /mermaidNodeId\(node, "d"\)[\s\S]*classList\.add\("nav-node"\)[\s\S]*style\.cursor = "pointer"[\s\S]*addEventListener\("click", binding\.onSelect\)/);
  assert.match(
    appSource,
    /const graphBackActions: GraphBackBindingActions = \{\s*onBack: popPlatformDrill,\s*};/);
  assert.match(
    workspaceBinding,
    /bindGraphBack\(document, graphBackActions\)/);
  assert.match(
    typeGraph,
    /bindGraphPanZoom\(container, viewport, \{ keybindings \}\);[\s\S]*bindTypeGraphNodes\(viewport, nodeId => \{[\s\S]*graphNodeOf\.get\(nodeId\)[\s\S]*onSelect: \(\) => navigateToType\(target\)[\s\S]*unavailableLabel/);
  assert.match(
    typeGraph,
    /const target = graphNode\.role === "self"\s*\? selectedType\(\)\s*: uniqueTypeByQueryId\(pkg\.types, fullName\)/);
  assert.match(
    dependencyGraph,
    /bindGraphPanZoom\(container, viewport, \{ keybindings \}\);[\s\S]*bindDependencyGraphNodes\(viewport, nodeId => \{[\s\S]*built\.nodeInfoById\.get\(nodeId\)[\s\S]*switchToPackageForDependencies\(info\.packageKey\)[\s\S]*openDependencyPackage\(info\.id, info\.versionRange\)/);
  assert.match(
    dependencyGraph,
    /const info = nodeId \? built\.nodeInfoById\.get\(nodeId\) : null;\s*if \(!info \|\| info\.kind === "self"\) return null/);
  assert.match(
    callGraph,
    /const mounted = currentCallGraph\(\);[\s\S]*mounted\?\.mermaid !== definition[\s\S]*bindGraphPanZoom\(targetContainer, viewport, \{[\s\S]*resolveCallGraphNode: nodeId =>[\s\S]*callGraphNodeBinding\(mounted, nodeId\)/);
  assert.match(
    callGraph,
    /active\.noBody\) return Promise\.resolve\(\{ status: "rendered" \}\);[\s\S]*pending\.definition === active\.mermaid[\s\S]*pending\.theme === theme[\s\S]*return pending\.promise/);
  assert.match(
    callGraph,
    /catch \(error\) \{[\s\S]*const message = errorMessage\(error\);[\s\S]*graph-render-error[\s\S]*return \{ status: "failed", message \}/);
  assert.match(
    callGraphBinding,
    /callGraph\.targets\?\.find\(candidate => candidate\.id === nodeId\)[\s\S]*const drilled =\s*state\.platformStack\.length > 0 \|\| Boolean\(state\.package\?\.isRuntimePack\);[\s\S]*resolveRuntimeGraphTargetCandidate\(pack, target\)[\s\S]*runtimeGraphTargetNavigationDisposition\([\s\S]*blockedCallGraphNodeBinding/);
  assert.match(
    callGraphBinding,
    /if \(disposition === "member" && pack && resident\) \{[\s\S]*navigateToRuntimeMember\([\s\S]*\} else if \(disposition === "lookup"\) \{[\s\S]*navigateOrDrillPlatform\([\s\S]*target,[\s\S]*runtimeSection,[\s\S]*failureSurface\)[\s\S]*\} else if \(destination === "member"\)[\s\S]*startPlatformDrill\(target\)/);
  assert.match(
    callGraphBinding,
    /const loaded = disposition === "loaded" && candidate\.status === "unique"\s*\? resolveLoadedGraphTarget\(target, candidate\)\s*: null/);
  assert.match(
    callGraphBinding,
    /combinedGraphTargetNavigationDisposition\(\s*candidate,\s*runtimeCandidate,\s*target,\s*runtimeResident\)/);
  assert.match(
    callGraphBinding,
    /if \(loaded\) \{[\s\S]*navigateToGraphMember\([\s\S]*loaded,[\s\S]*target,[\s\S]*loadedSection,[\s\S]*failureSurface\)[\s\S]*\} else if \(disposition === "resident"\) \{[\s\S]*startPlatformDrill\(target\)[\s\S]*\} else if \(platform\) \{[\s\S]*navigateOrDrillPlatform\([\s\S]*target,[\s\S]*runtimeSection,[\s\S]*failureSurface\)/);
  assert.match(
    graphInteractionsSource,
    /resolveCallGraphNode[\s\S]*setAttribute\("tabindex", "0"\)[\s\S]*setAttribute\("role", "button"\)[\s\S]*setAttribute\("aria-label", binding\.label\)[\s\S]*addEventListener\("click"[\s\S]*id: "call-graph-node\.activate"[\s\S]*key: \["Enter", " "\]/);
  assert.equal(appSource.match(/\bbindGraphBack\(/g)?.length, 1);
  assert.equal(appSource.match(/\bbindGraphPanZoom\(/g)?.length, 3);
  assert.equal(appSource.match(/\bbindTypeGraphNodes\(/g)?.length, 1);
  assert.equal(appSource.match(/\bbindDependencyGraphNodes\(/g)?.length, 1);
  assert.equal(appSource.match(/\bcallGraphNodeBinding\(/g)?.length, 2);
  assert.doesNotMatch(
    `${typeGraph}\n${dependencyGraph}\n${callGraph}`,
    /\.addEventListener\(|querySelectorAll<SVGGElement>\("g\.node"\)/);
  assert.doesNotMatch(
    appSource,
    /function (?:attachGraphPanZoom|graphTargetForSvgNode)\(/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelector\("\[data-graph-back\]"\)/);
  assert.match(
    appSource,
    /document\.addEventListener\("focusin", trackContentFrameFocus\)/);
  assert.match(
    appSource,
    /document\.addEventListener\("pointerdown", trackContentFramePointer\)/);
  assert.equal(appSource.match(/\.addEventListener\(/g)?.length, 5);
});

test("typed document inspection owns package document request coordination", () => {
  const documentLoader =
    appSource.match(/function openPackageDocument\(path: string\)[\s\S]*?\n}/)?.[0]
    ?? "";
  const documentCloser =
    appSource.match(/function closeDocViewer\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /createDocumentInspectionCoordinator\(\{[\s\S]*queryDocument:[\s\S]*renderMarkdown,[\s\S]*renderMarkdownInline,/);
  assert.match(
    appSource,
    /queryDocument: request => inspectPackageDocument\(\s*request\.packageId,\s*request\.version,\s*request\.document\.path\)/);
  assert.match(
    documentLoader,
    /return documentInspection\.open\(\{\s*packageId: pkg\.id,\s*version: pkg\.version,\s*document: doc,\s*\}\)/);
  assert.match(documentCloser, /documentInspection\.close\(\)/);
  assert.doesNotMatch(documentLoader, /state\.docViewer(?:Seq|Open|Loading)/);
  assert.doesNotMatch(documentCloser, /state\.docViewer(?:Seq|Open|Loading)/);
  assert.match(
    documentInspectionSource,
    /async open\(request: PackageDocumentRequest\)[\s\S]*state\.docViewerSeq/);
});

test("typed catalog requests own release and package-version coordination", () => {
  const releaseLoader =
    appSource.match(/function ensureDotnetReleases\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  const versionLoader =
    appSource.match(/function ensurePackageVersions\(pkg: AppPackage \| null\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /createCatalogRequests\(\{[\s\S]*queryDotnetReleases,[\s\S]*queryPackageVersions: packageId => inspectPackageVersions\(packageId\),[\s\S]*updatePlatformVersionSelect,[\s\S]*updatePackageVersionSelect: updateVersionSelect,/);
  assert.match(
    appSource,
    /raw\.githubusercontent\.com\/dotnet\/core\/refs\/heads\/main\/release-notes\/releases-index\.json/);
  assert.match(releaseLoader, /return catalogRequests\.ensureDotnetReleases\(\)/);
  assert.match(versionLoader, /return catalogRequests\.ensurePackageVersions\(pkg\)/);
  assert.doesNotMatch(
    `${releaseLoader}\n${versionLoader}`,
    /dotnetReleasesLoading|packageVersionsLoading|state\.packages/);
  assert.match(
    catalogRequestsSource,
    /state\.dotnetReleasesLoading = true[\s\S]*dependencies\.queryDotnetReleases\(\)[\s\S]*state\.dotnetReleasesLoading = false/);
  assert.match(
    catalogRequestsSource,
    /state\.packageVersionsLoading\[packageId\] = true[\s\S]*dependencies\.queryPackageVersions\(packageId\)[\s\S]*packageIsResident\(packageId\)/);
  assert.doesNotMatch(
    catalogRequestsSource,
    /\bfetch\(|\bdocument\b|inspectPackageVersions/);
});

test("typed type panel owns its rendered control bindings", () => {
  const binding =
    appSource.match(/function bindTypePanelEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  const rootEventBinder =
    appSource.match(/function bindEvents\(\) \{[\s\S]*?\n}\n\nfunction toggleTheme/)?.[0]
    ?? "";
  const clearFilters =
    binding.match(/onClearFilters: \(\) => \{[\s\S]*?\n    },/)?.[0]
    ?? "";
  assert.match(
    binding,
    /bindTypePanel\(document, \{/);
  assert.doesNotMatch(clearFilters, /libraryScope/);
  assert.doesNotMatch(clearFilters, /focusFilter/);
  assert.match(
    clearFilters,
    /state\.accessibilityFilter = defaultAccessibilityFilter\(state\.package\)/);
  assert.match(
    binding,
    /onTypeFilterChange: value => \{[\s\S]*?render\(\);\s*focusFilter\(\{ immediate: true \}\);\s*},/);
  assert.match(
    binding,
    /onTypeFilterDisclosureToggle: expanded => \{\s*state\.typeFiltersExpanded = expanded;\s*},/);
  assert.match(
    binding,
    /onTypeFilterEscape: \(\) => \{\s*state\.typeFilter = "";\s*render\(\);\s*focusFilter\(\{ immediate: true \}\);\s*},/);
  assert.equal(
    appSource.match(/\bbindTypePanelEvents\b/g)?.length,
    2);
  assert.equal(
    rootEventBinder.match(/\bbindTypePanelEvents\(\)/g)?.length,
    1);
  assert.equal(
    rootEventBinder.match(/^\s*bindTypePanelEvents\(\);$/gm)?.length,
    1);
  assert.match(
    appSource,
    /function bindEvents\(\) \{\s*bindStatusBarEvents\(\);\s*packageControls\.bind\(document\);\s*bindWorkspaceSubjectEvents\(\);\s*bindTypePanelEvents\(\);/);
  assert.match(
    typePanelSource,
    /export function bindTypePanel\([\s\S]*\[data-type\][\s\S]*\[data-namespace\][\s\S]*\[data-kind-filter\][\s\S]*\[data-nav-member\][\s\S]*\[data-nav-overload\][\s\S]*#nav-to-types[\s\S]*#clear-filter[\s\S]*#namespace-jump[\s\S]*#type-list[\s\S]*#type-filter/);
  assert.match(
    typePanelSource,
    /\[data-member-kind-filter\][\s\S]*\[data-member-access-filter\][\s\S]*\[data-member-trait-filter\][\s\S]*#clear-member-filter[\s\S]*#member-filter/);
  assert.match(
    typePanelSource,
    /\[data-member-jump-kind\][\s\S]*\[data-member-jump-access\][\s\S]*\[data-member-jump-trait\][\s\S]*\[data-member\][\s\S]*\[data-overload\][\s\S]*#member-back[\s\S]*#copy-signature[\s\S]*\[data-copy-anchor\][\s\S]*#copy-source[\s\S]*#copy-type-source/);
  assert.doesNotMatch(typePanelSource, /#copy-name|onCopyName/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelectorAll<HTMLElement>\("\[data-member-(?:kind|access|trait)-filter\]"\)/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelector(?:<HTMLInputElement>)?\("#(?:member-filter|clear-member-filter)"\)/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelectorAll<HTMLElement>\("\[data-(?:member-jump-(?:kind|access|trait)|member|overload|copy-anchor)\]"\)/);
  assert.doesNotMatch(
    appSource,
    /document\.querySelector\("#(?:member-back|copy-signature|copy-source|copy-type-source)"\)/);
  assert.match(
    binding,
    /const enterMemberNavigation = \(action: \(\) => void\) => \{[\s\S]*beginSpotlightNavigation\(\);[\s\S]*contentFramePane = "navigation";[\s\S]*action\(\);[\s\S]*restoreContentNavigationFocus\(focusGeneration\)/);
  const callbackSource = (name: string) =>
    binding.match(
      new RegExp(`    ${name}: [\\s\\S]*?(?=\\n    on[A-Z])`))?.[0]
      ?? "";
  for (const [name, stateField] of [
    ["onMemberCompositionAccessibilitySelect", "memberAccessibilityFilter"],
    ["onMemberCompositionKindSelect", "memberKindFilter"],
    ["onMemberCompositionTraitSelect", "memberTraitFilter"],
  ] as const) {
    const source = callbackSource(name);
    assert.match(
      source,
      new RegExp(
        `enterMemberNavigation\\(\\(\\) => \\{[\\s\\S]*resetMemberFilters\\(\\);`
        + `[\\s\\S]*state\\.${stateField} = value;`
        + "[\\s\\S]*enterMemberScope\\(\\);[\\s\\S]*render\\(\\)"));
    assert.equal(source.match(/\brender\(\)/g)?.length, 1);
  }
  assert.match(
    binding,
    /onMemberGroupOpen: memberKey => \{\s*const focusGeneration = beginSpotlightNavigation\(\);\s*showContentDetailAfterRender\(\);\s*openMemberGroup\(memberKey\);\s*if \(!contentFrameMedia\.matches\)\s*restoreContentNavigationFocus\(focusGeneration\);/);
  assert.match(
    binding,
    /onMemberBack: drillOut[\s\S]*onMemberOverloadOpen: openOverload/);
  assert.doesNotMatch(
    binding,
    /onCopyName|currentInspectedSubjectName/);
  assert.match(
    binding,
    /onCopySignature: \(\) => \{[\s\S]*void copyText\(overload\.signature, "signature copied"\)/);
  assert.match(
    binding,
    /onCopyAnchor: anchor => \{[\s\S]*selector: overload\?\.stableSelector,[\s\S]*digest: overload\?\.anchorDigest,[\s\S]*canonical: overload\?\.canonicalSignature[\s\S]*void copyText\(value, `\$\{anchor\} copied`\)/);
  assert.match(
    binding,
    /onCopyMemberSource: \(\) => \{[\s\S]*void copyText\(state\.memberSource\.text, "source copied"\)[\s\S]*onCopyTypeSource: \(\) => \{[\s\S]*void copyText\(state\.typeSource\.text, "source copied"\)/);
  assert.match(
    binding,
    /onMemberFilterClear: \(\) => \{[\s\S]*resetMemberFilters\(\);[\s\S]*renderMemberFilterAndRestoreFocus\("#clear-member-filter"\)/);
  assert.match(
    binding,
    /onMemberFilterKeyDown: \(event, value\) => \{\s*if \(event\.key === "Escape"\) \{\s*if \(navMode\(\) !== "member" && value === ""\) return false;[\s\S]*navMode\(\) === "member"[\s\S]*exitMemberScope\(\)[\s\S]*state\.memberTextFilter = ""[\s\S]*return true;[\s\S]*event\.key !== "ArrowUp" && event\.key !== "ArrowDown"\) return false;\s*stepMemberNav\(event\.key === "ArrowDown" \? 1 : -1, true\);\s*return true/);
  assert.match(
    binding,
    /bindTypePanel\(document, \{[\s\S]*}, keybindings\);/);
  const selectorCount = (selector: string) =>
    appSource.split(selector).length - 1;
  assert.deepEqual(
    Object.fromEntries([
      "[data-type]",
      "[data-namespace]",
      "[data-kind-filter]",
      "[data-nav-member]",
      "[data-nav-overload]",
      "#nav-to-types",
      "#clear-filter",
      "#namespace-jump",
    ].map(selector => [selector, selectorCount(selector)])),
    {
      "[data-type]": 0,
      "[data-namespace]": 0,
      "[data-kind-filter]": 0,
      "[data-nav-member]": 0,
      "[data-nav-overload]": 0,
      "#nav-to-types": 0,
      "#clear-filter": 0,
      "#namespace-jump": 0,
    });
  assert.equal(selectorCount("#type-filter"), 1);
  assert.equal(selectorCount("#type-list"), 5);
});

test("typed scope bar owns its rendered control bindings", () => {
  assert.deepEqual(parsedAppSource.errors, []);
  const rootEventBinder = functionDeclaration("bindEvents");
  const scopeEventBinder = functionDeclaration("bindScopeBarEvents");
  const rootScopeCalls = callExpressionsNamed(appSyntax, "bindScopeBarEvents");
  assert.equal(rootScopeCalls.length, 2);
  for (const rootScopeCall of rootScopeCalls)
    assert.equal(rootScopeCall.arguments.length, 0);
  const innerScopeCall = onlyCallExpressionNamed(appSyntax, "bindScopeBar");
  assert.equal(innerScopeCall.arguments.length, 3);
  assertIdentifierArgument(innerScopeCall, 0, "document", "bindScopeBar");
  assertIdentifierArgument(innerScopeCall, 2, "scopeBarState", "bindScopeBar");
  assert.equal(
    callExpressionsNamed(scopeEventBinder, "bindScopeBar").length,
    1);
  assert.equal(
    scopeEventBinder.body.body.filter(statement =>
      statement.type === "ExpressionStatement"
      && statement.expression.type === "AssignmentExpression"
      && statement.expression.right === innerScopeCall).length,
    1);
  assert.equal(
    callExpressionsNamed(rootEventBinder, "bindScopeBarEvents").length,
    1);
  const directRootCalls = rootEventBinder.body.body.map(directCallName);
  const typePanelIndex = directRootCalls.indexOf("bindTypePanelEvents");
  assert.notEqual(typePanelIndex, -1);
  assert.equal(directRootCalls[typePanelIndex + 1], "bindScopeBarEvents");

  const actions = objectArgument(innerScopeCall, 1, "bindScopeBar");
  const memberSection = callbackProperty(actions, "onMemberSectionSelect");
  assert.deepEqual(
    statementSignatures(memberSection.body.body),
    [
      'assign:contentFramePane = "detail"',
      "call:applyMemberSection(section)",
    ]);

  const packageLens = callbackProperty(actions, "onPackageLensSelect");
  assert.deepEqual(
    statementSignatures(packageLens.body.body),
    [
      'assign:contentFramePane = "detail"',
      "assign:state.packageLens = lens",
      "call:render()",
    ]);

  const scope = callbackProperty(actions, "onScopeSelect");
  assert.deepEqual(
    statementSignatures(scope.body.body),
    [
      'assign:contentFramePane = "detail"',
      {
        if: 'target === "workspace"',
        whenTrue: [
          "assign:state.workspaceSubjectOpen = true",
          "assign:state.atPackageRoot = true",
          'assign:state.selectedMemberKey = ""',
          'assign:state.memberBrowseTypeId = ""',
          "assign:state.selectedOverloadIndex = null",
        ],
        whenFalse: [
          {
            if: 'target === "package"',
            whenTrue: [
              "assign:state.workspaceSubjectOpen = false",
              "assign:state.atPackageRoot = true",
            ],
            whenFalse: [
              {
                if: 'target === "type"',
                whenTrue: [
                  "assign:state.workspaceSubjectOpen = false",
                  "assign:state.atPackageRoot = false",
                  {
                    if: "!state.selectedTypeId",
                    whenTrue: [
                      "declare:const first = filteredTypes()[0]",
                      {
                        if: "first",
                        whenTrue: ["assign:state.selectedTypeId = first.id"],
                        whenFalse: [],
                      },
                    ],
                    whenFalse: [],
                  },
                  'assign:state.selectedMemberKey = ""',
                  'assign:state.memberBrowseTypeId = ""',
                  "assign:state.selectedOverloadIndex = null",
                ],
                whenFalse: [
                  {
                    if: 'target === "member"',
                    whenTrue: [
                      "assign:state.workspaceSubjectOpen = false",
                      "call:enterMemberScope()",
                    ],
                    // A scope this dispatch does not handle is now a compile error rather
                    // than a silently ignored click.
                    whenFalse: ['call:assertNever(target, "workspace scope")'],
                  },
                ],
              }
            ],
          },
        ],
      },
      "call:render()",
    ]);

  const typeLens = callbackProperty(actions, "onTypeLensSelect");
  assert.deepEqual(
    statementSignatures(typeLens.body.body),
    [
      'assign:contentFramePane = "detail"',
      "assign:state.lens = lens",
      'assign:state.selectedMemberKey = ""',
      'assign:state.memberBrowseTypeId = ""',
      "call:render()",
    ]);
  assert.match(
    scopeBarSource,
    /export function bindScopeBar\([\s\S]*\[data-scope\][\s\S]*\[data-package-lens\][\s\S]*\[data-lens\][\s\S]*\[data-member-section\]/);
  for (const selector of [
    "[data-scope]",
    "[data-package-lens]",
    "[data-lens]",
    "[data-member-section]",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("typed settings panel owns its rendered control bindings", () => {
  assert.deepEqual(parsedAppSource.errors, []);
  const bindEvents = functionDeclaration("bindEvents");
  const bindHomeEvents = functionDeclaration("bindHomeEvents");
  const renderSettings = functionDeclaration("renderSettingsViewHtml");
  const settingsEventBinder = functionDeclaration("bindSettingsPanelEvents");
  const settingsPanelImport = onlySyntaxNode(
    appSyntax.body.filter(
      (node): node is ImportDeclaration =>
        node.type === "ImportDeclaration"
        && node.source.value === "./settings-panel.ts"),
    "settings panel import");
  assert.equal(
    (settingsPanelImport.specifiers ?? []).filter(
      specifier => specifier.type === "ImportSpecifier"
        && specifier.imported.type === "Identifier"
        && specifier.imported.name === "bindSettingsPanel"
        && specifier.local.name === "bindSettingsPanel").length,
    1);
  assert.equal(
    syntaxNodes(
      appSyntax,
      node => node.type === "Identifier"
        && node.name === "bindSettingsPanel").length,
    3);
  const eventBinderCalls = callExpressionsNamed(appSyntax, "bindSettingsPanelEvents");
  assert.equal(eventBinderCalls.length, 3);
  assert.equal(
    syntaxNodes(
      appSyntax,
      node => node.type === "Identifier"
        && node.name === "bindSettingsPanelEvents").length,
    4);
  const settingsBinders: readonly (readonly [
    DeclaredFunction,
    string,
    string | null,
  ])[] = [
    [bindEvents, "workbench settings binder", "bindScopeBarEvents"],
    [bindHomeEvents, "home settings binder", "bindStatusBarEvents"],
  ];
  for (const [owner, description, predecessor] of settingsBinders) {
    assert.equal(
      callExpressionsNamed(owner, "bindSettingsPanelEvents").length,
      1,
      description);
    assert.equal(
      owner.body.body
        .map(statement => directCallExpression(statement, "bindSettingsPanelEvents"))
        .filter(Boolean)
        .length,
      1,
      `${description} direct call`);
    if (predecessor) {
      const directCallNames = owner.body.body.map(directCallName);
      assert.equal(
        directCallNames.indexOf("bindSettingsPanelEvents"),
        directCallNames.indexOf(predecessor) + 1,
        `${description} order`);
    }
  }
  const directWorkbenchCalls = bindEvents.body.body.map(directCallName);
  assert.equal(
    directWorkbenchCalls.indexOf("bindSettingsPanelEvents"),
    directWorkbenchCalls.indexOf("bindScopeBarEvents") + 1);
  assert.equal(renderSettings.body.body.length, 1);
  const [renderStatement] = renderSettings.body.body;
  assert.ok(renderStatement !== undefined);
  assert.ok(
    renderStatement.type === "ReturnStatement",
    `settings view must return markup, found ${renderStatement.type}`);
  const renderCall = renderStatement.argument;
  assert.ok(
    renderCall?.type === "CallExpression",
    `settings view must return a call, found ${renderCall?.type ?? "nothing"}`);
  assert.ok(
    renderCall.callee.type === "Identifier",
    `settings view call must name a function, found ${renderCall.callee.type}`);
  assert.equal(renderCall.callee.name, "renderSettingsView");

  const innerSettingsCall = onlyCallExpressionNamed(appSyntax, "bindSettingsPanel");
  assert.equal(
    directCallExpression(
      onlySyntaxNode(settingsEventBinder.body.body, "bindSettingsPanelEvents body"),
      "bindSettingsPanel"),
    innerSettingsCall);
  assert.equal(innerSettingsCall.arguments.length, 2);
  assertIdentifierArgument(innerSettingsCall, 0, "document", "bindSettingsPanel");
  const actions = objectArgument(innerSettingsCall, 1, "bindSettingsPanel");
  assert.equal(actions.properties.length, 5);
  const settingsActions: readonly (readonly [string, string])[] = [
    ["onClose", "closeSettings"],
    ["onOpen", "openSettings"],
    ["onTasteClear", "clearTaste"],
    ["onTasteToggle", "toggleTaste"],
    ["onThemeSelect", "setTheme"],
  ];
  for (const [name, value] of settingsActions) {
    const target = namedProperty(actions, name).value;
    assert.ok(
      target.type === "Identifier",
      `${name} settings action must be an identifier, found ${target.type}`);
    assert.equal(target.name, value);
  }
  assert.match(
    appSource,
    /import \{(?=[^}]*\bbindSettingsPanel,)[^}]*} from "\.\/settings-panel\.ts";/);
  assert.doesNotMatch(
    sourceText(settingsEventBinder),
    /\bquerySelector(?:All)?\b|\baddEventListener\b/);
  assert.match(
    settingsPanelSource,
    /export function bindSettingsPanel\([\s\S]*#settings-close[\s\S]*#home-settings[\s\S]*#settings-backdrop[\s\S]*#settings-dialog[\s\S]*\.settings-seg\[data-theme\][\s\S]*\.settings-taste \[data-taste\][\s\S]*#settings-taste-clear/);
  assert.doesNotMatch(
    settingsPanelSource,
    /#taste-btn|#taste-popover|#taste-clear|renderTastePopover|onTasteOpenToggle/);
  assert.doesNotMatch(
    productionTypeScriptSources.map(({ source }) => source).join("\n"),
    /#open-settings/);
  assert.doesNotMatch(
    productionTypeScriptSources.map(({ source }) => source).join("\n"),
    /#taste-btn|#taste-popover|#taste-clear|tasteOpen/);
  for (const selector of [
    "#settings-close",
    ".settings-seg[data-theme]",
    ".settings-taste [data-taste]",
    "#settings-taste-clear",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("metadata viewer owns its rendered explorer control bindings", () => {
  assert.deepEqual(parsedAppSource.errors, []);
  const bindEvents = functionDeclaration("bindEvents");
  const renderMetadata = functionDeclaration("renderMetadataExplorer");
  const metadataEventBinder = functionDeclaration("bindMetadataViewerEvents");
  const outerCalls = callExpressionsNamed(appSyntax, "bindMetadataViewerEvents");
  assert.equal(outerCalls.length, 2);
  assert.equal(
    syntaxNodes(
      appSyntax,
      node => node.type === "Identifier"
        && node.name === "bindMetadataViewerEvents").length,
    3);
  const metadataBinders: readonly (readonly [DeclaredFunction, string])[] = [
    [bindEvents, "workbench metadata binder"],
    [renderMetadata, "metadata explorer binder"],
  ];
  for (const [owner, description] of metadataBinders) {
    assert.equal(
      callExpressionsNamed(owner, "bindMetadataViewerEvents").length,
      1,
      description);
    assert.equal(
      owner.body.body
        .map(statement => directCallExpression(statement, "bindMetadataViewerEvents"))
        .filter(Boolean)
        .length,
      1,
      `${description} direct call`);
  }
  const directWorkbenchCalls = bindEvents.body.body.map(directCallName);
  assert.equal(
    directWorkbenchCalls.indexOf("bindMetadataViewerEvents"),
    directWorkbenchCalls.indexOf("bindSettingsPanelEvents") + 1);
  const metadataRenderStatements = renderMetadata.body.body;
  const replacementIndex = metadataRenderStatements.findIndex(
    statement => statement.type === "ExpressionStatement"
      && statement.expression.type === "AssignmentExpression"
      && statement.expression.operator === "="
      && sourceText(statement.expression.left) === "app.innerHTML");
  const binderIndex = metadataRenderStatements.findIndex(
    statement => directCallExpression(statement, "bindMetadataViewerEvents"));
  assert.notEqual(replacementIndex, -1);
  assert.equal(binderIndex, replacementIndex + 1);

  const innerCall = onlyCallExpressionNamed(appSyntax, "bindMetadataExplorer");
  assert.equal(
    metadataEventBinder.body.body
      .map(statement => directCallExpression(statement, "bindMetadataExplorer"))
      .filter(Boolean)
      .length,
    1);
  assert.deepEqual(
    statementSignatures(metadataEventBinder.body.body.slice(0, 1)),
    ["declare:const ex = state.explorer"]);
  assert.equal(
    directCallExpression(
      statementAt(metadataEventBinder.body.body, 1, "bindMetadataExplorerEvents"),
      "bindMetadataExplorer"),
    innerCall);
  assert.deepEqual(
    statementSignatures(metadataEventBinder.body.body.slice(2, 3)),
    [
      {
        if: "!ex",
        whenTrue: ["statement:ReturnStatement:return;"],
        whenFalse: [],
      },
    ]);
  assert.equal(innerCall.arguments.length, 3);
  assertIdentifierArgument(innerCall, 0, "document", "bindMetadataExplorer");
  assertIdentifierArgument(innerCall, 1, "ex", "bindMetadataExplorer");
  const actions = objectArgument(innerCall, 2, "bindMetadataExplorer");
  assert.equal(actions.properties.length, 12);
  const rowFocus = callbackProperty(actions, "onRowFocus");
  assert.deepEqual(
    statementSignatures(rowFocus.body.body),
    [
      {
        if: "!ex",
        whenTrue: ["statement:ReturnStatement:return;"],
        whenFalse: [],
      },
      "declare:const already = ex.detail && ex.detail.index === index && ex.detail.rowId === rowId",
      "assign:ex.detail = already ? null : { index, rowId }",
      "assign:ex.highlight = already ? null : { index, rowId }",
      "declare:const current = ex.history[ex.historyPos]",
      {
        if: "current && current.index === index",
        whenTrue: ["assign:current.rowId = already ? 0 : rowId"],
        whenFalse: [],
      },
      "call:render()",
    ]);

  const binding = sourceText(innerCall);
  assert.match(
    binding,
    /bindMetadataExplorer\s*\(document, ex, \{[\s\S]*onClose: closeExplorer,[\s\S]*onHistoryBack: explorerHistoryBack,[\s\S]*onHistoryForward: explorerHistoryForward,[\s\S]*onHeapFocus: heap => pushExplorerFocus\(\{ heap \}\),[\s\S]*onJump: explorerJump,[\s\S]*onOpenHeap: openExplorerHeap,[\s\S]*onOpenTable: openExplorer,[\s\S]*onPage: \(index, startRowId\) =>\s*observeAsync\(\s*loadExplorerWindow\(index, startRowId\),\s*"Loading metadata table rows"\),[\s\S]*onRetryPackageMetadata: \(\) =>\s*observeAsync\(loadPackageMetadata\(\), "Retrying package metadata"\),[\s\S]*onRowFocus: \(index, rowId\) => \{[\s\S]*ex\.detail = already \? null : \{ index, rowId \};[\s\S]*ex\.highlight = already \? null : \{ index, rowId \};[\s\S]*onShowOverview: explorerShowOverview,[\s\S]*onTableFocus: \(index, rowId\) => pushExplorerFocus\(\{ index, rowId \}\),/);
  assert.doesNotMatch(
    binding,
    /\b(?:getElementById|querySelector|querySelectorAll)\s*\(|\.addEventListener\s*\(/);
  assert.match(
    metadataViewerSource,
    /export function bindMetadataExplorer\([\s\S]*\[data-package-metadata-retry\][\s\S]*#mde-exit[\s\S]*#mde-hist-back[\s\S]*#mde-hist-fwd[\s\S]*\[data-mde-open\][\s\S]*\[data-mde-open-heap\][\s\S]*\[data-mde-chip\][\s\S]*\[data-mde-jump\][\s\S]*\[data-mde-overview\][\s\S]*\[data-mde-page\][\s\S]*\[data-mde-heap-chip\][\s\S]*\.mde-wall \.mde-card\[data-mde-index\] \.mde-card-head[\s\S]*\.mde-wall \.mde-heap-card\[data-mde-heap\] \.mde-card-head[\s\S]*\.mde-wall \.mde-row\[data-mde-row\][\s\S]*#mde-canvas[\s\S]*\.mde-focus \.mde-row\[data-mde-row\]/);
  for (const selector of [
    "#mde-exit",
    "#mde-hist-back",
    "#mde-hist-fwd",
    "[data-mde-open]",
    "[data-mde-open-heap]",
    "[data-mde-chip]",
    "[data-mde-jump]",
    "[data-mde-overview]",
    "[data-mde-page]",
    "[data-mde-heap-chip]",
    ".mde-wall .mde-card[data-mde-index] .mde-card-head",
    ".mde-wall .mde-heap-card[data-mde-heap] .mde-card-head",
    ".mde-wall .mde-row[data-mde-row]",
    ".mde-focus .mde-row[data-mde-row]",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
  assert.equal(appSource.split("#mde-canvas").length - 1, 1);
});

test("package opportunities owns its rendered control bindings", () => {
  const binding =
    appSource.match(/function bindPackageOpportunitiesEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  const bindEvents =
    appSource.match(
      /function bindEvents\(\) \{[\s\S]*?\n}\n\n(?=(?:async )?function )/)?.[0]
    ?? "";
  assert.match(
    binding,
    /bindPackageOpportunities\(document, \{\s*onLookForSelect: openSpotlight,[\s\S]*onTypeSelect: opportunity => \{[\s\S]*opportunity\.sourceIdentity === "legacy"[\s\S]*exact identity is unavailable[\s\S]*resolveOpportunitySourceCandidate\(\s*currentPackage\(\),\s*opportunity\)[\s\S]*candidate\.status !== "unique"[\s\S]*navigateToType\(candidate\.type\)/);
  assert.match(
    bindEvents,
    /^\s*bindPackageOpportunitiesEvents\(\);\s*$/m);
  const compositionPrefix =
    bindEvents.slice(0, bindEvents.indexOf("bindPackageOpportunitiesEvents();"));
  assert.equal(compositionPrefix.match(/\{/g)?.length, 1);
  assert.equal(compositionPrefix.match(/\}/g)?.length ?? 0, 0);
  assert.equal(
    appSource.match(/\bbindPackageOpportunitiesEvents\b/g)?.length,
    2);
  assert.equal(
    appSource.match(/\bbindPackageOpportunities\b/g)?.length,
    2);
  assert.doesNotMatch(
    binding,
    /\b(?:getElementById|querySelector|querySelectorAll)\s*\(|\.addEventListener\s*\(/);
  assert.equal(binding.match(/\bdocument\b/g)?.length, 1);
  assert.match(
    packageOpportunitiesSource,
    /export function bindPackageOpportunities\([\s\S]*\[data-opp-type\][\s\S]*sourceDefinitionId: button\.dataset\.oppSourceDefinition[\s\S]*sourceAssembly: button\.dataset\.oppSourceAssembly[\s\S]*sourceAssemblyVersion: button\.dataset\.oppSourceVersion[\s\S]*sourceAssemblyCulture: button\.dataset\.oppSourceCulture[\s\S]*sourceAssemblyPublicKeyToken: button\.dataset\.oppSourceToken[\s\S]*\[data-opp-package\][\s\S]*\[data-opp-lookfor\]/);
  for (const selector of [
    "[data-opp-type]",
    "[data-opp-package]",
    "[data-opp-lookfor]",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("modal viewers own their rendered close bindings", () => {
  const graphBinding =
    appSource.match(/function bindGraphSourceEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  const docBinding =
    appSource.match(/function bindDocViewerEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    graphBinding,
    /bindGraphSource\(document, \{\s*onClose: closeGraphSource,\s*\}\)/);
  assert.match(
    docBinding,
    /bindDocViewer\(document, \{\s*onClose: closeDocViewer,\s*onOpenDocument: path =>\s*observeAsync\(openPackageDocument\(path\), "Opening a package document"\),\s*\}\)/);
  assert.equal(
    graphBinding.match(/\bbindGraphSource\(document\b/g)?.length,
    1);
  assert.equal(
    docBinding.match(/\bbindDocViewer\(document\b/g)?.length,
    1);
  assert.match(
    graphSourceViewerSource,
    /export function bindGraphSource\([\s\S]*#graph-source-backdrop[\s\S]*event\.target === backdrop[\s\S]*#graph-source-close/);
  assert.match(
    docViewerSource,
    /export function bindDocViewer\([\s\S]*#doc-viewer-backdrop[\s\S]*event\.target === backdrop[\s\S]*#doc-viewer-close[\s\S]*\[data-doc-path\]/);
  for (const [identifier, count] of [
    ["bindGraphSourceEvents", 2],
    ["bindDocViewerEvents", 2],
    ["bindGraphSource", 2],
    ["bindDocViewer", 2],
  ] as const) {
    assert.equal(
      appSource.match(new RegExp(`\\b${identifier}\\b`, "g"))?.length,
      count,
      identifier);
  }
  for (const selector of [
    "#graph-source-backdrop",
    "#graph-source-close",
    "#doc-viewer-backdrop",
    "#doc-viewer-close",
    "[data-doc-path]",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("annotated source owns its rendered control bindings", () => {
  const binding =
    appSource.match(/function bindAnnotatedSourceEvents\(\) \{[\s\S]*?\n}(?=\n\nconst workbenchShellActions)/)?.[0]
    ?? "";
  assert.match(
    binding,
    /bindAnnotatedSource\(document, \{\s*onAction: applyAnnotatedSourceAction,\s*}\);/);
  assert.doesNotMatch(
    binding,
    /\b(?:getElementById|querySelector|querySelectorAll)\s*\(|\.addEventListener\s*\(/);
  assert.equal(binding.match(/(?<!\.)\bdocument\b/g)?.length, 1);
  assert.match(
    annotatedSourceModule,
    /export function bindAnnotatedSource\([\s\S]*\[data-annotated-action\][\s\S]*\[data-annotated-source-start\][\s\S]*#annotated-source-backdrop[\s\S]*#annotated-source-modal/);
  for (const [identifier, count] of [
    ["bindAnnotatedSourceEvents", 2],
    ["bindAnnotatedSource", 2],
  ] as const) {
    assert.equal(
      appSource.match(new RegExp(`\\b${identifier}\\b`, "g"))?.length,
      count,
      identifier);
  }
  for (const selector of [
    "[data-annotated-action]",
    "[data-annotated-source-start]",
    "#annotated-source-backdrop",
    "#annotated-source-modal",
  ]) {
    assert.equal(appSource.split(selector).length - 1, 0, selector);
  }
});

test("annotated source validation failures stay visible at the shell boundary", () => {
  assert.match(
    appSource,
    /function renderAnnotatedSource\(result: AnnotatedSourceResult\) \{\s*try \{[\s\S]*renderAnnotatedSourcePure\([\s\S]*catch \(error\) \{\s*if \(!\(error instanceof TypeError\)\) throw error;\s*return renderAnnotatedSourceRejection\(error\)/,
  );
  assert.match(
    appSource,
    /function renderAnnotatedSourceModal\(\) \{[\s\S]*try \{[\s\S]*renderAnnotatedSourceModalPure\([\s\S]*catch \(error\) \{\s*if \(!\(error instanceof TypeError\)\) throw error;[\s\S]*Annotated source document rejected[\s\S]*data-annotated-action="close-modal"/,
  );
  assert.match(
    appSource,
    /function renderAnnotatedSourceRejection\(error: TypeError\) \{[\s\S]*Annotated source document rejected[\s\S]*escapeHtml\(errorMessage\(error\)\)/,
  );
  assert.match(
    appSource,
    /function dismissAnnotatedSourceModal\(restoreExploreFocus: boolean\) \{[\s\S]*try \{\s*model = createAnnotatedSourceViewerModel\(state\.memberAnnotated\);\s*\} catch \(error\) \{\s*if \(!\(error instanceof TypeError\)\) throw error;\s*state\.memberAnnotatedEmbedded = null;\s*state\.memberAnnotatedModal = null;[\s\S]*renderAndFocusAnnotated\("#annotated-source-rejection-title", "embedded"\);[\s\S]*return true;\s*\}[\s\S]*dismissModalSession\(model, state\.memberAnnotatedModal\)/,
  );
  assert.match(
    appSource,
    /if \(action\.kind === "close-modal"\) \{\s*dismissAnnotatedSourceModal\(true\);\s*return;\s*\}\s*const model = createAnnotatedSourceViewerModel\(result\)/,
  );
  assert.match(
    appSource,
    /catch \(error\) \{\s*if \(!\(error instanceof TypeError\) \|\| session\.surface !== "modal"\) throw error;\s*return dismissAnnotatedSourceModal\(true\);/,
  );
});

test("annotated source Escape and history ownership track the mounted surface", () => {
  assert.match(
    appSource,
    /const embeddedAnnotatedSourceDetailContextIsActive = \(\) =>\s*workspaceKeyboardContextIsActive\(\)\s*&& !workbenchOverlayOwnsFocus\(\)\s*&& state\.memberSection === "annotated"\s*&& Boolean\(state\.memberAnnotatedEmbedded\?\.detail\);\s*const annotatedSourceEscapeContextIsActive = \(\) =>\s*annotatedSourceContextIsActive\(\)\s*\|\| embeddedAnnotatedSourceDetailContextIsActive\(\)/);

  const dismiss =
    appSource.match(
      /function dismissModalsForRoutedNavigation\(\) \{[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    dismiss,
    /const dismissedAnnotatedSourceModal = dismissAnnotatedSourceModal\(false\)/);
  assert.match(dismiss, /return dismissedAnnotatedSourceModal/);

  const popstate =
    appSource.match(
      /window\.addEventListener\("popstate",[\s\S]*?\n}\);/)?.[0]
    ?? "";
  assert.match(
    popstate,
    /const dismissedAnnotatedSourceModal = dismissModalsForRoutedNavigation\(\);\s*invalidateMemberDestinationWork\(state\);\s*if \(dismissedAnnotatedSourceModal\) render\(\{ synchronizeUrl: false \}\);\s*if \(isPackageQueryPath/);
  assert.match(
    appSource,
    /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\)[\s\S]*if \(productDemosRouteVisible\) \{\s*document\.title = "Demos — dotnet-inspect";\s*\} else if \(options\.synchronizeUrl !== false\) \{\s*syncUrl\(\);\s*\}/);
});

test("leaving package search clears its pending loading state", () => {
  assert.match(
    spotlightPackageSearchSource,
    /state\.spotlightScope !== "all"[\s\S]*state\.spotlightPkgLoading = false;[\s\S]*return;/);
  assert.match(
    appSource,
    /id: "workspace\.drill-out-escape"[\s\S]*key: "Escape"[\s\S]*!isTextEntry\(\)/);
  assert.match(
    spotlightPackageSearchSource,
    /query === state\.spotlightPkgQuery[\s\S]*generation\+\+;[\s\S]*state\.spotlightPkgLoading = false;[\s\S]*return;/);
  assert.match(
    appSource,
    /visibleSpotlightPackageHits\(\s*query,\s*state\.spotlightPkgQuery,\s*state\.spotlightPkgHits,\s*\)/);
});

test("Spotlight async work is generation-gated and refreshes either mounted surface", () => {
  assert.match(
    appSource,
    /createSpotlightPackageSearch\(\{[\s\S]*queryPackages: querySpotlightPackages,[\s\S]*updateResults: \(\) => spotlight\.updateResults\(\)/);
  assert.match(
    appSource,
    /schedule: \(callback, delay\) => setTimeout\(\(\) => void callback\(\), delay\),\s*cancelScheduled: handle => clearTimeout\(handle\),/);
  assert.match(
    spotlightPackageSearchSource,
    /requestGeneration !== generation[\s\S]*state\.spotlightQuery\.trim\(\) !== query/);
  assert.doesNotMatch(appSource, /spotlightPkgGeneration|spotlightPkgTimer/);
  assert.match(
    appSource,
    /if \(!state\.spotlightOpen && !state\.home\) return undefined;[\s\S]*spotlight\.refresh\(\)/);
});

test("global workbench shortcuts respect the topmost modal", () => {
  // The composition root wires the single link-navigation owner once; it must not regain
  // a raw document click listener of its own (see the `.addEventListener` count assertion
  // in "typed graph interactions own graph controls and Mermaid node bindings").
  assert.match(
    appSource,
    /bindWorkspaceLinkNavigation\(document, \{[\s\S]*currentOrigin: \(\) => location\.origin,[\s\S]*resolve: href => new URL\(href, location\.href\),[\s\S]*navigate: navigateInAppUrl,/);
  // Modal ownership is explicit priority policy, while the reusable registry remains
  // independent of inspect-web state and attaches the only raw keydown listener.
  assert.match(
    workbenchKeybindingsSource,
    /workspace: 100,[\s\S]*element: 200,[\s\S]*spotlight: 300,[\s\S]*documentViewer: 310,[\s\S]*graphSource: 320,[\s\S]*unavailableWorkspace: 330,[\s\S]*settings: 340,[\s\S]*metadataExplorer: 350/);
  assert.match(
    keybindingRegistrySource,
    /dispatch\(event: KeyboardEvent\): KeybindingDispatchResult[\s\S]*candidates\.sort\([\s\S]*candidate\.binding\.run\(event\)[\s\S]*event\.preventDefault\(\)[\s\S]*target\.addEventListener\("keydown", listener\)/);
  assert.match(
    appSource,
    /id: "graph-source\.dismiss"[\s\S]*priority: WORKBENCH_KEYBINDING_PRIORITY\.graphSource[\s\S]*when: graphSourceContextIsActive[\s\S]*closeGraphSource\(\)/);
  assert.match(
    appSource,
    /id: "document-viewer\.dismiss"[\s\S]*priority: WORKBENCH_KEYBINDING_PRIORITY\.documentViewer[\s\S]*when: documentViewerContextIsActive[\s\S]*closeDocViewer\(\)/);
  assert.match(
    appSource,
    /id: "spotlight\.dismiss"[\s\S]*priority: WORKBENCH_KEYBINDING_PRIORITY\.spotlight[\s\S]*when: spotlightContextIsActive[\s\S]*closeSpotlight\(\)/);
  assert.match(
    appSource,
    /id: "spotlight\.open-commands"[\s\S]*commandOrControl: true[\s\S]*openSpotlight\("", "commands"\)[\s\S]*id: "spotlight\.open-all"[\s\S]*openSpotlight\(\)[\s\S]*id: "spotlight\.contain-browser-find"/);
  assert.doesNotMatch(
    appSource,
    /id: "taste\.dismiss"|state\.tasteOpen/);
  assert.match(
    appSource,
    /function openSpotlight\(seed = "", spotlightScope: SpotlightScope = "all"\) \{\s*if \(state\.loading \|\| state\.error\) return;\s*beginSpotlightNavigation\(\)/);
  assert.match(
    spotlightSource,
    /function bind\(root: ParentNode, mode: "modal" \| "inline"\)[\s\S]*if \(mode === "modal"\)[\s\S]*focus\(\);/);
  assert.match(
    appSource,
    /id: "metadata-explorer\.dismiss"[\s\S]*priority: WORKBENCH_KEYBINDING_PRIORITY\.metadataExplorer[\s\S]*Boolean\(state\.explorer\?\.open\)[\s\S]*metadata-explorer\.contain-browser-shortcut/);
  assert.match(
    appSource,
    /id: "settings\.dismiss"[\s\S]*priority: WORKBENCH_KEYBINDING_PRIORITY\.settings[\s\S]*when: \(\) => state\.settings[\s\S]*settings\.contain-browser-shortcut/);
  assert.match(
    spotlightSource,
    /aria-activedescendant="spotlight-result-\$\{state\.spotlightIndex\}"[\s\S]*syncActiveDescendant\(items\.length\)/);
  assert.match(
    appSource,
    /const unavailableWorkspaceContext = \(\) =>[\s\S]*!state\.home && \(state\.loading \|\| Boolean\(state\.error\)\)[\s\S]*unavailable-workspace\.contain-browser-shortcut[\s\S]*unavailable-workspace\.contain-filter-shortcut/);
  assert.match(
    appSource,
    /function workspaceKeyboardContextIsActive\(\)[\s\S]*!state\.explorer\?\.open[\s\S]*!state\.settings[\s\S]*!state\.home[\s\S]*!state\.packageQueryOpen[\s\S]*!state\.loading[\s\S]*!state\.error[\s\S]*!state\.graphSourceOpen[\s\S]*!state\.docViewerOpen[\s\S]*!state\.spotlightOpen/);
  assert.equal(
    keybindingRegistrySource.match(/addEventListener\("keydown"/g)?.length,
    1);
  assert.equal(
    [
      appSource,
      graphInteractionsSource,
      packageControlsSource,
      spotlightSource,
      typePanelSource,
    ].join("\n").match(/addEventListener\(\s*"keydown"/g)?.length ?? 0,
    0);
  assert.match(appSource, /keybindings\.attach\(document\)/);
  assert.match(
    appSource,
    /function focusFilter\([\s\S]*\{ immediate = false \}: \{ immediate\?: boolean \} = \{\},[\s\S]*const focus = \(\) => \{[\s\S]*"#member-filter, #type-filter"[\s\S]*if \(immediate\) \{\s*focus\(\);\s*return;\s*}\s*requestAnimationFrame\(focus\);/);
  assert.match(
    appSource,
    /function focusFilter\([\s\S]*input\.closest<HTMLDetailsElement>\(\s*"\[data-member-filter-disclosure\]"\)[\s\S]*input\.closest<HTMLDetailsElement>\(\s*"\[data-type-filter-disclosure\]"\)[\s\S]*state\.memberFiltersExpanded = true;[\s\S]*state\.typeFiltersExpanded = true;[\s\S]*disclosure\.open = true;[\s\S]*input\.focus\(\)/);
});

test("Spotlight navigation waits for selection data before restoring focus", () => {
  const typeLensLoader =
    appSource.match(/function loadSelectedTypeLensData\([\s\S]*?\n}/)?.[0];
  const selectionLoader =
    appSource.match(/function loadSelectionData\(\)[\s\S]*?\n}/)?.[0];
  assert.ok(typeLensLoader);
  assert.ok(selectionLoader);
  assert.match(typeLensLoader, /return loadSelectedTypeSource\(\)/);
  assert.match(typeLensLoader, /return loadSelectedTypeMetadata\(\)/);
  assert.match(
    selectionLoader,
    /const typeLensLoad = loadSelectedTypeLensData\(\);\s*if \(typeLensLoad !== "member"\) return typeLensLoad;/);
  assert.match(
    appSource,
    /async function loadPackageFromSpotlight[\s\S]*const navigationGeneration = beginSpotlightNavigation\(\);\s*const focusGeneration = documentFocusGeneration;[\s\S]*await loadPackage\([\s\S]*if \(loaded \|\| !catalogSnapshot\)\s*focusTypeList\(navigationGeneration, focusGeneration\)/);
  assert.match(
    appSource,
    /async function openPlatformLibrary[\s\S]*const navigationGeneration = scopeOnly \? null : beginSpotlightNavigation\(\);\s*const focusGeneration = documentFocusGeneration;[\s\S]*spotlight\.reset\(\)[\s\S]*const selectionData = loadSelectionData\(\);[\s\S]*await selectionData;[\s\S]*focusTypeList\(navigationGeneration, focusGeneration\)/);
  assert.match(
    appSource,
    /async function pickSpotlightMember[\s\S]*const navigationGeneration = beginSpotlightNavigation\(\);\s*const focusGeneration = documentFocusGeneration;[\s\S]*await loadSelectedMemberDocumentation\(\);[\s\S]*focusTypeList\(navigationGeneration, focusGeneration\)/);
  assert.match(
    appSource,
    /async function pickSpotlight\([\s\S]*packageResult:[\s\S]*typeId: string,[\s\S]*const navigationGeneration = beginSpotlightNavigation\(\);\s*const focusGeneration = documentFocusGeneration;[\s\S]*const selectionData = loadSelectionData\(\);[\s\S]*await selectionData;[\s\S]*focusTypeList\(navigationGeneration, focusGeneration\)/);
  assert.match(
    appSource,
    /let spotlightFocusGeneration = 0;\s*let documentFocusGeneration = 0[\s\S]*function canRestoreWorkbenchFocus\([\s\S]*generation === spotlightFocusGeneration[\s\S]*focusGeneration === documentFocusGeneration[\s\S]*isTextEntry\(\)[\s\S]*function focusTypeList\([\s\S]*focusGeneration = documentFocusGeneration,[\s\S]*canRestoreWorkbenchFocus\(generation, focusGeneration\)/);
  assert.match(
    appSource,
    /captureFocusAfterDismiss: \(\) => \{\s*const navigationGeneration = spotlightFocusGeneration;\s*const focusGeneration = documentFocusGeneration;\s*return \(\) => restoreContentFrameFocusAfterDismiss\(\s*navigationGeneration,\s*focusGeneration\)/);
  assert.match(
    spotlightSource,
    /const generation = interactionGeneration;[\s\S]*const focusAfterExecution = \(\) => \{[\s\S]*generation === interactionGeneration[\s\S]*Promise\.resolve\(execution\)\.then\(\s*focusAfterExecution,\s*\(error: unknown\) => \{\s*options\.reportCommandError\(error\);\s*focusAfterExecution\(\)/);
});
const browserGraphMemberSource = readFileSync(
  new URL("../engine.MetadataExports/TypeAndGraphMemberExports.cs", import.meta.url),
  "utf8");

test("dependency graph render identity includes truncation and navigation", () => {
  const graph = {
    definition: "flowchart TD\n  d0[Example]",
    nodeInfoById: new Map([[
      "d0",
      {
        kind: "open",
        packageKey: "Example.Package|1.0.0|net8.0",
        id: "Example.Package",
        versionRange: ""
      }
    ]]),
    truncated: false,
    nodeLimit: 80
  };
  const signature = dependencyGraphRenderSignature(graph);

  assert.notEqual(
    signature,
    dependencyGraphRenderSignature({ ...graph, truncated: true }));
  const rootNodeInfo = graph.nodeInfoById.get("d0");
  assert.ok(rootNodeInfo, "the graph fixture must describe node d0");
  assert.notEqual(
    signature,
    dependencyGraphRenderSignature({
      ...graph,
      nodeInfoById: new Map([[
        "d0",
        {
          ...rootNodeInfo,
          packageKey: "Example.Package|2.0.0|net8.0"
        }
      ]])
    }));
});

test("ready status shows versioned linked build provenance", () => {
  assert.match(appSource, /state\.buildIdentity = inspectBuildIdentity\(\)/);
  assert.match(
    appSource,
    /<\/main>[\s\S]{0,700}\$\{statusBarHtml\(\{/);
  assert.match(statusBarSource, /"statusbar data-bar"/);
  assert.match(statusBarSource, /buildIdentityHtml\(model\.buildIdentity/);
  assert.match(
    appSource,
    /variant: "home"[\s\S]{0,200}buildIdentity: state\.buildIdentity/);
  assert.match(
    statusBarSource,
    /identity\.commitUrl[\s\S]*target="_blank" rel="noopener noreferrer"/);
  assert.match(statusBarSource, /built \$\{escapeHtml\(builtAt\)\} UTC/);
  assert.match(
    deploySource,
    /-getProperty:VersionPrefix[\s\S]*-p:VersionPrefix="\$version"[\s\S]*-p:SourceRevisionId="\$GITHUB_SHA"[\s\S]*-p:BuildTimestampUtc="\$built_at"/);
});

test("bootstrap reconciles persisted style choices with the product catalog", () => {
  const bootstrap =
    appSource.match(
      /async function bootstrap\(\) \{[\s\S]*?\n}\n\nfunction computeDiagnostics/,
    )?.[0] ?? "";
  assert.match(
    bootstrap,
    /state\.styleOptions = \([\s\S]*reconcileStyleTaste\(\s*state\.taste,\s*state\.styleOptions\);[\s\S]*state\.taste = reconciledTaste;[\s\S]*localStorage\.setItem\("inspect-taste", JSON\.stringify\(state\.taste\)\)/);
});

test("bare home paints before wasm engine download", () => {
  const renderDispatch =
    appSource.match(
      /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\) \{[\s\S]*?const pkg = state\.package;/,
    )?.[0] ?? "";
  const bootstrap =
    appSource.match(/async function bootstrap\(\) \{[\s\S]*?\n}\n\nfunction computeDiagnostics/)?.[0] ?? "";
  const homePaintWait =
    appSource.match(/function waitForHomePaint\(\)[\s\S]*?\n}\n\nfunction loadStoredTaste/)?.[0] ?? "";
  const errorPackageRecovery =
    appSource.match(/function openPackageQuery[\s\S]*?\n}\n\nconst loadErrorShellActions/)?.[0] ?? "";
  const loadingView =
    appSource.match(/function renderLoading\(\)[\s\S]*?\n}\n\nasync function loadSelectedMemberDocumentation/)?.[0] ?? "";
  assert.doesNotMatch(appSource, /from "\/engine\.js"/);
  assert.doesNotMatch(appSource, /inspect-web-engine/);
  assert.match(
    appSource,
    /async function loadEngineModule\(\)[\s\S]*await Promise\.all\(\[/);
  for (const module of generatedFacadeModules) {
    assert.match(
      appSource,
      new RegExp(
        `async function loadEngineModule\\(\\)[\\s\\S]*import\\("/${module}\\.js"\\)`),
      `the application does not bind operations through /${module}.js`);
  }
  assert.match(
    homePaintWait,
    /first-contentful-paint[\s\S]*observer\.observe\(\{ type: "paint", buffered: true \}\)/);
  assert.match(
    homePaintWait,
    /requestAnimationFrame\(\(\) => setTimeout\(resolve, 0\)\)/);
  assert.match(
    appSource,
    /state\.loading = !state\.home;[\s\S]*render\(\);[\s\S]*if \(state\.home\) await waitForHomePaint\(\);[\s\S]*await loadEngineModule\(\);[\s\S]*reportEngineStatus\("Loading \.NET WebAssembly…"\);[\s\S]*await startEngine\(window\.location\.origin\);[\s\S]*reportEngineStatus\("Reading package assemblies…"\)/);
  assert.match(
    renderDispatch,
    /if \(state\.credits\) \{[\s\S]*renderCreditsView\(\);[\s\S]*if \(state\.loading \|\| state\.error\)/);
  assert.match(
    bootstrap,
    /state\.engineStartupFailed = false;[\s\S]*const reportEngineStatus = \(message: string\) => \{[\s\S]*if \(!state\.credits\) render\(\);[\s\S]*if \(state\.home\) \{[\s\S]*if \(!state\.credits\) render\(\);[\s\S]*catch \(error\)[\s\S]*state\.engineStartupFailed = true;[\s\S]*if \(!state\.credits\) render\(\)/);
  assert.match(
    appSource,
    /const showReadyGlint = state\.engineReady && homeReadyGlintPending;[\s\S]*homeReadyGlintPending = false;[\s\S]*homeBotAnimationStartedAt[\s\S]*--home-bot-animation-delay:/);
  assert.match(
    stylesSource,
    /animation-delay: var\(--home-bot-animation-delay, 0ms\)/);
  assert.match(
    appSource,
    /class="home-search \$\{enginePending[\s\S]*class="home-engine-status"/);
  assert.match(
    `${appSource}\n${statusBarSource}`,
    /state\.engineReady[\s\S]*browser wasm ready[\s\S]*browser wasm loading/);
  assert.match(
    appSource,
    /state\.retryAction = \(\) => window\.location\.reload\(\)/);
  assert.match(
    errorPackageRecovery,
    /findOpenPackageForQuery\(state, query\)[\s\S]*selectWorkspacePackage\(openPackage\);[\s\S]*return;[\s\S]*if \(!state\.engineReady\) \{[\s\S]*window\.location\.assign\(url\);[\s\S]*return;[\s\S]*\}\s*observeAsync\(\s*loadPackage\(query\.packageId, query\.version, ""\)/);
  assert.match(
    loadingView,
    /id="error-package-query"[\s\S]*bindLoadErrorShell\(document, loadErrorShellActions\)/);
  assert.doesNotMatch(
    loadingView,
    /id="error-package-query"[\s\S]*(?:openPackageQuery|loadPackage)\(/);
});

test("Spotlight uses local type matches until the engine is ready", () => {
  const typeMatches =
    appSource.match(/function spotlightTypeMatches[\s\S]*?\n}\n\n\/\/ Flat member index/)?.[0] ?? "";
  assert.match(
    typeMatches,
    /if \(!state\.engineReady\) return spotlightFallbackMatches\(query, cache\.pool\);[\s\S]*inspectSearchTypes\(query, cache\.candidatesJson\)/);
});

test("loading brand links back to the site root", () => {
  assert.match(
    appSource,
    /<a class="loading-brand" href="\/" aria-label="dotnet inspect home"><span>◇<\/span> dotnet-inspect<\/a>/);
  assert.match(
    stylesSource,
    /\.loading-brand\s*\{[^}]*text-decoration: none;/s);
});

test("member filters retain accessible controls and focus across rerenders", () => {
  const binding =
    appSource.match(/function bindTypePanelEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /id="clear-member-filter"[^>]*aria-label="Clear member filters"/);
  assert.match(
    memberDetailInspectionSource,
    /async loadDocumentation\(request\)[\s\S]*const preservedFocus = dependencies\.renderPreservingMemberFocus\(\);[\s\S]*state\.memberDocumentationLoading = false;[\s\S]*dependencies\.renderPreservingMemberFocus\(preservedFocus\)/);
  assert.match(
    stylesSource,
    /\.type-library-context \.namespace-chips, \.pane-footer \{ display: none; \}/);
  assert.match(
    typePanelSource,
    /memberFilter\?\.addEventListener\(\s*"input",\s*\(\) => actions\.onMemberFilterChange\(memberFilter\.value\)\)/);
  assert.match(
    binding,
    /onMemberFilterChange: value => \{[\s\S]*state\.memberTextFilter = value;[\s\S]*renderPreservingMemberFocus\(\)/);
  assert.match(
    typePanelSource,
    /id: "member-filter\.navigate"[\s\S]*key: \["Escape", "ArrowUp", "ArrowDown"\][\s\S]*run: event => actions\.onMemberFilterKeyDown\(event, memberFilter\.value\)/);
  assert.match(
    binding,
    /onMemberFilterKeyDown: \(event, value\) => \{[\s\S]*event\.key === "Escape"[\s\S]*if \(navMode\(\) !== "member" && value === ""\) return false;[\s\S]*if \(navMode\(\) === "member"\)[\s\S]*exitMemberScope\(\)[\s\S]*state\.memberTextFilter = ""[\s\S]*renderMemberFilterAndRestoreFocus\("#member-filter"\)[\s\S]*return true[\s\S]*stepMemberNav/);
  assert.match(
    appSource,
    /id: "workspace\.drill-out-escape"[\s\S]*key: "Escape"[\s\S]*!isTextEntry\(\)[\s\S]*if \(navMode\(\) === "member"\) exitMemberScope\(\)/);
  assert.match(
    appSource,
    /function exitMemberScope\(\) \{\s*const focusGeneration = beginSpotlightNavigation\(\);\s*contentFramePane = "navigation";[\s\S]*render\(\);\s*restoreContentNavigationFocus\(focusGeneration\);\s*return true;/);
  assert.match(appSource, /onShowTypes: exitMemberScope,/);
  assert.match(appSource, /<summary id="member-filter-summary">/);
  assert.match(typePanelSource, /<summary id="type-filter-summary">/);
  assert.match(
    appSource,
    /const renderMemberFilterAndRestoreFocus = \(selector = ""\) => \{[\s\S]*renderWithMemberFocus\(preserved\)/);
  assert.match(
    memberFocusSource,
    /active\?\.id === "type-list"[\s\S]*selector = "#type-list"/);
  const platformDrill =
    appSource.match(/async function drillPlatformNode\([\s\S]*?\n}\n\nfunction popPlatformDrill/)?.[0]
    ?? "";
  assert.match(
    platformDrill,
    /return callGraphInspection\.drill\(\{[\s\S]*type: callGraphTargetTypeId\(node\)[\s\S]*metadataToken: node\.metadataToken \?\? 0/);
  const coordinatedDrill =
    callGraphInspectionSource.match(/async drill\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  assert.equal(
    [...coordinatedDrill.matchAll(
      /dependencies\.renderPreservingMemberFocus\(preservedFocus\)/g)].length,
    2);
  const platformNavigation =
    appSource.match(/async function navigateOrDrillPlatform\([\s\S]*?\n}\n\n\/\/ Enter the resident runtime pack/)?.[0]
    ?? "";
  assert.match(
    platformNavigation,
    /const owner = captureViewOperation\(seq\);[\s\S]*ownsViewOperation\(owner, state\.memberCallGraphSeq\)[\s\S]*const discardIfStale = \([\s\S]*loadRuntimePackAssembly\([\s\S]*navigationIsCurrent[\s\S]*runtimeResult\.failureMessage[\s\S]*state\.runtimePackError[\s\S]*renderPreservingMemberFocus\(preservedFocus\)/);
  assert.match(
    appSource,
    /function applyMemberSection\(id: MemberSection\) \{[\s\S]*state\.memberSection === "call-graph" && id !== "call-graph"[\s\S]*invalidateMemberCallGraphWork\(state\)/);
  assert.match(
    appSource,
    /function navigateToRuntimeMember\([\s\S]*const targetLibrary = libraryKey\(type\);\s*state\.libraryScope = targetLibrary \? new Set\(\[targetLibrary\]\) : null;[\s\S]*state\.typeCursor = Math\.max\(0, filteredTypes\(\)/);
});

test("Type inventory filters preserve their focused control across rerenders", () => {
  const binding =
    appSource.match(/function bindTypePanelEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  for (const name of [
    "onClearFilters",
    "onKindSelect",
    "onNamespaceSelect",
  ]) {
    const callback =
      binding.match(new RegExp(`    ${name}: [\\s\\S]*?(?=\\n    on[A-Z])`))
        ?.[0] ?? "";
    assert.match(callback, /renderPreservingMemberFocus\(\)/);
    assert.doesNotMatch(callback, /\brender\(\)/);
  }
  assert.match(
    appSource,
    /function afterLibraryScopeChange\(\) \{\s*normalizeLibrarySelection\(\);\s*renderPreservingMemberFocus\(\)/);
});

test("shared member views use portable product identity and omit UI-local filters", () => {
  const capture = appSource.match(
    /function captureWorkspaceUrlState\(\)[\s\S]*?\n}\n\nfunction buildStateUrl/)?.[0] ?? "";
  const encoder = workspaceNavigationSource.match(
    /function encodeWorkspaceShareState\([\s\S]*?\n}\n\nfunction decodeWorkspaceShareState/)?.[0] ?? "";
  const deepLink = appSource.match(
    /function applyDeepLink\([\s\S]*?\n}\n\n\/\/ Kick off/)?.[0] ?? "";
  assert.match(
    encoder,
    /tabs: state\.tabs,[\s\S]*contexts: state\.contexts,[\s\S]*view: state\.view/);
  assert.match(
    capture,
    /memberAnchor = overload\.anchorDigest \|\| null;[\s\S]*memberSignature = memberAnchor \? null : overload\.canonicalSignature \|\| null/);
  assert.match(capture, /libraries = state\.libraryScope/);
  assert.match(
    capture,
    /state\.libraryScope && state\.libraryScope\.size > 1[\s\S]*Select one library/);
  assert.match(
    capture,
    /overload\.bodySelectors\.length > 1[\s\S]*accessor-specific section/);
  assert.match(
    capture,
    /overload\.graphOnly[\s\S]*Graph-discovered members cannot be shared/);
  assert.match(capture, /package: state\.package\.id/);
  assert.doesNotMatch(capture, /memberTextFilter:/);
  assert.doesNotMatch(capture, /memberKindFilter:/);
  assert.doesNotMatch(capture, /memberAccessibilityFilter:/);
  assert.doesNotMatch(capture, /memberTraitFilter:/);
  assert.match(
    deepLink,
    /deep\.memberAnchor \|\| deep\.memberSignature[\s\S]*portableMatches\.length === 1[\s\S]*solePortableBodyTarget\(selection\.overload\)[\s\S]*state\.selectedBodyTarget = portableBodyTarget/);
  assert.match(
    appSource,
    /function selectMemberNavEntry\(entry: MemberNavEntry, focusList: boolean\) \{\s*const preservedFocus = captureMemberFocus\(document\);[\s\S]*scheduleMemberFocusAfterRender\(preservedFocus, replacementAuthority\)/);
  assert.match(
    appSource,
    /window\.addEventListener\("popstate"[\s\S]*const deep = loc;[\s\S]*restoreWorkspaceFromLocation\(\s*loc,\s*deep,\s*navigationSeq,\s*canonicalSnapshot\)/);
});

test("the frontend delegates compact packet syntax to the product codec", () => {
  assert.doesNotMatch(workspaceNavigationSource, /\batob\b|\bbtoa\b/);
  assert.doesNotMatch(
    workspaceNavigationSource,
    /\b(?:packet|raw)\.(?:f|t|g|a|x|v|y|m|s|c|l)\b/);
  assert.doesNotMatch(
    workspaceNavigationSource,
    /WorkspaceSharePacket|encodeBase64Url|decodeBase64Url/);
  assert.match(
    workspaceNavigationSource,
    /const result = decode\(value\)/);
  assert.match(
    workspaceNavigationSource,
    /const result = encode\(JSON\.stringify\(\{/);
});

test("the selected canonical context bounds call graph workspace membership", () => {
  const selection = appSource.match(
    /function selectedCallGraphWorkspacePackages\(\)[\s\S]*?\n}/)?.[0] ?? "";
  const loader = appSource.match(
    /async function loadSelectedMemberCallGraph\([\s\S]*?\n}/)?.[0] ?? "";

  assert.match(
    selection,
    /selectedBrowserCallGraphPackageTabIds\(basis\)/);
  assert.match(
    selection,
    /packageTabIds\.includes\(activeTab\.id\)/);
  assert.match(
    loader,
    /workspacePackages = selectedCallGraphWorkspacePackages\(\)/);
  assert.match(
    appSource,
    /callGraphCaptureTopology\(\s*captured\.tabs,\s*activeIndex,\s*participantTabIds\)/);
});

test("canonical restoration is atomic and history adopts the active packet basis", () => {
  const restore = appSource.match(
    /async function restoreWorkspaceFromLocation\([\s\S]*?\n}\n\nfunction failCanonicalWorkspaceRestore/)?.[0] ?? "";
  const history = appSource.match(
    /window\.addEventListener\("popstate"[\s\S]*?\n}\);/)?.[0] ?? "";
  const sync = appSource.match(
    /function syncUrl\(\)[\s\S]*?\n}/)?.[0] ?? "";
  const stateUrl = appSource.match(
    /function buildStateUrl\([\s\S]*?\n}/)?.[0] ?? "";
  const scopePlatform = appSource.match(
    /async function openPlatformLibrary\([\s\S]*?\n}/)?.[0] ?? "";
  const validateView = appSource.match(
    /function canonicalViewRestorationFailure\([\s\S]*?\n}/)?.[0] ?? "";
  const initialRestore = appSource.match(
    /async function restoreInitialWorkspace\(\)[\s\S]*?\n}/)?.[0] ?? "";

  assert.match(
    restore,
    /canonicalTabCountPreserved[\s\S]*canonicalTabsPreserved[\s\S]*failedTabCount > 0 \|\| !canonicalTabsPreserved[\s\S]*failCanonicalWorkspaceRestore/);
  assert.match(
    restore,
    /canonicalViewRestorationFailure\(targetModel, deep, loc\.lens\)[\s\S]*failCanonicalWorkspaceRestore/);
  assert.match(
    restore,
    /canonicalSnapshot = loc\.hasWorkspaceState[\s\S]*captureCanonicalWorkspaceRestoreSnapshot/);
  assert.match(
    history,
    /canonicalSnapshot = loc\.hasWorkspaceState[\s\S]*commitWorkspaceShareBasis\(loc\.shareState\)/);
  assert.match(
    restore,
    /loc\.hasWorkspaceState && !loc\.shareState[\s\S]*failCanonicalWorkspaceRestore\(/);
  assert.match(
    history,
    /loc\.hasWorkspaceState && !loc\.shareState[\s\S]*failCanonicalWorkspaceRestore\(/);
  assert.match(
    initialRestore,
    /loc\.hasWorkspaceState && !loc\.shareState[\s\S]*restoreWorkspaceFromLocation\([\s\S]*return;[\s\S]*const packageId = loc\.package/);
  assert.match(
    appSource,
    /function failCanonicalWorkspaceRestore\([\s\S]*const failedUrl = location\.href;[\s\S]*bindWorkspaceRetryToUrl\(\s*failedUrl,\s*\(\) => location\.href,\s*url => workspaceLocation\.replace\(url, history\.state\),\s*retryAction\)[\s\S]*snapshot\?\.hasWorkspace[\s\S]*restoreCanonicalWorkspaceRestoreSnapshot\(snapshot\)[\s\S]*appendQueryNotice\([\s\S]*ownedRetryAction\);[\s\S]*failedWorkspaceUrlState = \{\s*kind: "canonical",\s*url: failedUrl,[\s\S]*projection: workspaceUrlProjection\(\)[\s\S]*render\(\);\s*return/);
  assert.match(
    restore,
    /loc\.hasWorkspaceState && !loc\.shareState[\s\S]*canonicalSnapshot,\s*null\)/);
  assert.match(
    history,
    /loc\.hasWorkspaceState && !loc\.shareState[\s\S]*canonicalSnapshot,\s*null\)/);
  assert.match(
    history,
    /if \(isCreditsPath\(location\.pathname\)\) \{[\s\S]*render\(\);\s*return;\s*\}\s*if \(isProductHomeDemosPath\(location\.pathname\)\) \{[\s\S]*state\.workspaceSubjectOpen = true;[\s\S]*render\(\);[\s\S]*return;\s*\}/);
  assert.match(
    history,
    /isProductHomeDemosPath\(location\.pathname\)[\s\S]*const focusWorkspaceOnEntry =\s*!state\.packageQueryReturnFocusPending;\s*render\(\);\s*if \(state\.engineReady && focusWorkspaceOnEntry\)/);
  assert.doesNotMatch(
    appSource,
    /preserveUrlThroughNextRender/);
  assert.match(
    sync,
    /if \(retainFailedWorkspaceUrl\(\)\) return;/);
  assert.match(
    appSource,
    /function retainFailedWorkspaceUrl\(\) \{\s*const failedState = failedWorkspaceUrlState;\s*const retainedState = retainWorkspaceUrlPreservation\(\s*failedState,\s*location\.href,\s*workspaceUrlProjection\(\)\);\s*if \(retainedState\) return true;\s*if \(failedState\?\.kind === "route"\s*&& !recoverWorkspaceRouteFailure\(\s*failedState,\s*location,\s*url => workspaceLocation\.replace\(url, history\.state\)\)\) \{\s*return true;\s*\}\s*failedWorkspaceUrlState = null;\s*return false;\s*\}/);
  assert.match(
    appSource,
    /if \(state\.loading \|\| state\.error\) \{[\s\S]*return;\s*\}\s*retainFailedWorkspaceUrl\(\);\s*if \(state\.home\)/);
  assert.match(
    appSource,
    /navigation: navigationHistory\.snapshot\(\),\s*failedWorkspaceUrlState: failedWorkspaceUrlState[\s\S]*structuredClone\(failedWorkspaceUrlState\)[\s\S]*navigationHistory\.restore\(snapshot\.navigation\);[\s\S]*failedWorkspaceUrlState = snapshot\.failedWorkspaceUrlState[\s\S]*structuredClone\(snapshot\.failedWorkspaceUrlState\)/);
  assert.match(
    appSource,
    /captureCanonicalWorkspaceRestoreSnapshot\(\)[\s\S]*sourceInspection\.cancelCurrentRequest\(\);\s*cancelAnnotatedSourceRequest\(state\)[\s\S]*structuredClone\(state\.packages\)/);
  assert.match(
    appSource,
    /function commitWorkspaceShareBasis\([\s\S]*state\.workspaceShareBasis = basis;[\s\S]*sourceInspection\.clearGraphSource\(\)/);
  assert.match(
    history,
    /invalidateMemberDestinationWork\(state\)[\s\S]*captureCanonicalWorkspaceRestoreSnapshot/);
  assert.match(
    appSource,
    /const \{ tabs, preservesBasis \} = capturedShareTabs\(\);[\s\S]*browserCreatedCallGraphTabIds\(tabs, activeIndex\)/);
  assert.match(
    appSource,
    /captured\.preservesBasis,[\s\S]*state\.memberSection === "call-graph"/);
  assert.match(sync, /state\.atPackageRoot/);
  assert.match(
    sync,
    /const pushFromProductDemos =\s*isProductHomeDemosPath\(location\.pathname\);[\s\S]*const destination = buildStateUrl\(\)\.toString\(\);[\s\S]*if \(pushFromProductDemos\) \{\s*workspaceLocation\.push\(destination\);\s*\} else \{\s*workspaceLocation\.replace\(destination, history\.state\);/);
  assert.match(
    sync,
    /if \(pushFromProductDemos\) \{\s*workspaceLocation\.push\(\s*workspaceLocation\.build\(snapshot\)\.toString\(\)\);\s*\} else \{\s*workspaceLocation\.sync\(snapshot, history\.state\);/);
  assert.match(
    appSource,
    /const productDemosRouteVisible =\s*scope\(\) === "workspace"\s*&& isProductHomeDemosPath\(location\.pathname\);[\s\S]*document\.title = "Demos — dotnet-inspect";[\s\S]*else if \(options\.synchronizeUrl !== false\) \{\s*syncUrl\(\)/);
  assert.match(
    stateUrl,
    /state\.atPackageRoot && state\.package[\s\S]*buildPackageRootStateUrl/);
  assert.match(
    scopePlatform,
    /candidate\.toLowerCase\(\) === key\.toLowerCase\(\)[\s\S]*scopeOnly\) return hasLib \? pkg : undefined/);
  assert.match(
    validateView,
    /typeLensesFor\(pkg\)[\s\S]*deep\.section && !hasPortableMember/);
});

test("initial workspace packet resolution waits for the engine phase", () => {
  assert.match(
    appSource,
    /const initialWorkspace = workspaceLocation\.preflightCurrent\(\);\s*const initialLocation = initialWorkspace\.visible/);
  assert.match(
    appSource,
    /state\.packageQueryOpen = isPackageQueryPath\(location\.pathname\);[\s\S]*const productHomeDemosOpen = isProductHomeDemosPath\(location\.pathname\);[\s\S]*state\.home = state\.credits\s*\|\| \(!state\.packageQueryOpen\s*&& !productHomeDemosOpen\s*&& !initialLocation\.package\s*&& !initialWorkspace\.hasWorkspaceState\s*&& !initialLocation\.routeFailure\)/);
  const restore = appSource.match(
    /async function restoreInitialWorkspace\(\)[\s\S]*?\n}\n\nfunction isStyleTier/)?.[0]
    ?? "";
  assert.match(
    restore,
    /const navigationSeq = navigationSequence\.current\(\);\s*const loc = workspaceLocation\.preflightCurrent\(\)\.resolve\(\);[\s\S]*framework: loc\.framework \|\| DEFAULT_REQUESTED_FRAMEWORK[\s\S]*state\.requestedPackage = resolvedLocation\.package;[\s\S]*state\.requestedVersion = resolvedLocation\.version;[\s\S]*state\.requestedFramework = resolvedLocation\.framework;[\s\S]*restoreWorkspaceFromLocation\(\s*resolvedLocation,\s*deepLinkFromLocation\(resolvedLocation\),\s*navigationSeq\)/);
  const bootstrap = appSource.match(
    /async function bootstrap\(\)[\s\S]*?\n}\n\nobserveAsync\(bootstrap\(\)/)?.[0]
    ?? "";
  const initializeAt =
    bootstrap.indexOf("await startEngine(window.location.origin);");
  const restoreAt = bootstrap.indexOf("await restoreInitialWorkspace();");
  assert.notEqual(initializeAt, -1);
  assert.notEqual(restoreAt, -1);
  assert.ok(initializeAt < restoreAt);
  assert.match(
    bootstrap,
    /if \(isProductHomeDemosPath\(location\.pathname\)\) \{[\s\S]*state\.workspaceSubjectOpen = true;[\s\S]*render\(\);[\s\S]*return;/);
});

test("malformed package routes use the contained restore failure path", () => {
  const restore = appSource.match(
    /async function restoreWorkspaceFromLocation\([\s\S]*?\n}\n\nfunction failWorkspaceRoute/)?.[0]
    ?? "";
  assert.match(
    restore,
    /canonicalSnapshot = loc\.hasWorkspaceState\s*\? captureCanonicalWorkspaceRestoreSnapshot\(\)[\s\S]*if \(loc\.routeFailure\) \{[\s\S]*if \(failureHandler\) \{\s*failureHandler\(loc\.routeFailure\.message\);\s*\} else \{\s*failWorkspaceRoute\(loc\.routeFailure\.message\);[\s\S]*return;\s*\}\s*if \(!clearWorkspaceRouteFailure\(\)\) \{\s*if \(failureHandler\) \{\s*failureHandler\("The existing package route could not be cleared\."\);[\s\S]*render\(\);\s*return;\s*\}/);

  const initial = appSource.match(
    /async function restoreInitialWorkspace\(\)[\s\S]*?\n}\n\nfunction isStyleTier/)?.[0]
    ?? "";
  assert.match(
    initial,
    /if \(loc\.routeFailure\) \{\s*await restoreWorkspaceFromLocation\(\s*loc,\s*deepLinkFromLocation\(loc\),\s*navigationSeq\);\s*return;/);

  const popstate =
    appSource.match(/window\.addEventListener\("popstate"[\s\S]*?\n\}\);/)?.[0]
    ?? "";
  assert.match(
    popstate,
    /if \(loc\.routeFailure\) \{\s*failWorkspaceRoute\(loc\.routeFailure\.message\);\s*return;\s*\}\s*if \(!clearWorkspaceRouteFailure\(\)\) \{\s*render\(\);\s*return;\s*\}\s*const canonicalSnapshot = loc\.hasWorkspaceState[\s\S]*state\.queryNotice = loc\.workspaceNotice \|\| "";[\s\S]*const bareHome/);

  const failure = appSource.match(
    /function failWorkspaceRoute\([\s\S]*?\n}\n\nfunction failCanonicalWorkspaceRestore/)?.[0]
    ?? "";
  assert.match(
    failure,
    /function failWorkspaceRoute\(message: string\) \{\s*if \(state\.package\)[\s\S]*failedWorkspaceUrlState = \{\s*kind: "route",\s*notice: `Package route failed: \$\{message\}`,[\s\S]*pathname: location\.pathname,\s*search: location\.search,\s*recoveryUrl: buildPackageRootStateUrl\(location\.href,[\s\S]*state\.errorTitle = "Package route failed";[\s\S]*state\.error = message;[\s\S]*state\.retryAction = retryUnavailable/);
  assert.match(
    appSource,
    /function goHome\(\) \{\s*navigationSequence\.begin\(\);\s*state\.loading = false;[\s\S]*invalidateGraphMemberNavigation\(\);\s*clearNavigationError\(\);\s*if \(!clearWorkspaceRouteFailure\(\)\) \{\s*render\(\);\s*return;\s*\}[\s\S]*workspaceLocation\.push\("\/"\);[\s\S]*render\(\)/);
  assert.match(
    appSource,
    /function visibleQueryNotice\(\) \{\s*const routeNotice = failedWorkspaceUrlState\?\.kind === "route"\s*\? failedWorkspaceUrlState\.notice\s*: null;\s*return \[state\.queryNotice, routeNotice\]\s*\.filter\(Boolean\)\s*\.join\(" "\);\s*\}/);
  assert.match(
    appSource,
    /function clearWorkspaceRouteFailure\(recoveryUrl\?: string\) \{\s*if \(failedWorkspaceUrlState\?\.kind !== "route"\) return true;\s*if \(!recoverWorkspaceRouteFailure\(\s*failedWorkspaceUrlState,\s*location,\s*url => workspaceLocation\.replace\(url, history\.state\),\s*recoveryUrl\)\) \{\s*return false;\s*\}\s*failedWorkspaceUrlState = null;\s*return true;\s*\}[\s\S]*function dismissQueryNotice\(\) \{\s*const routeFailureOnHome =\s*failedWorkspaceUrlState\?\.kind === "route" && state\.home;\s*state\.queryNotice = "";\s*state\.queryNoticeRetryAction = null;\s*if \(!clearWorkspaceRouteFailure\(routeFailureOnHome \? "\/" : undefined\)\) \{\s*render\(\);\s*return;\s*\}\s*failedWorkspaceUrlState = null;\s*render\(\);\s*\}/);
  assert.match(
    appSource,
    /const workbenchShellActions: WorkbenchShellBindingActions = \{\s*onApplicationAction: dispatchApplicationAction,\s*onCopySubjectSegment: index => \{[\s\S]*onDismissNotice: dismissQueryNotice,/);
  assert.match(
    appSource,
    /const homeShellActions: HomeShellBindingActions = \{\s*onDismissNotice: dismissQueryNotice,\s*onOpenDemos: openProductDemos,/);
  assert.equal(
    appSource.match(
      /<span class="query-notice-text">\${escapeHtml\(visibleQueryNotice\(\)\)}<\/span>/g,
    )?.length,
    1);
  assert.equal(
    appSource.match(/\${renderQueryNotice\(\)}/g)?.length,
    2);
  assert.match(
    appSource,
    /state\.queryNotice && state\.queryNoticeRetryAction\s*\? '<button id="retry-notice"/);
  assert.match(
    appSource,
    /function openCredits\(\) \{\s*if \(!clearWorkspaceRouteFailure\(\)\) \{\s*render\(\);\s*return;\s*\}/);
  assert.match(
    appSource,
    /state\.retryAction === retryUnavailable\s*\? ""\s*: `<button id="retry-load" type="button">retry<\/button>`/);
});

test("member entry controls choose the resulting content-frame pane", () => {
  const bindings =
    appSource.match(/function bindTypePanelEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";
  assert.match(
    bindings,
    /const enterMemberNavigation = \(action: \(\) => void\) => \{\s*const focusGeneration = beginSpotlightNavigation\(\);\s*contentFramePane = "navigation";\s*action\(\);\s*restoreContentNavigationFocus\(focusGeneration\);/);
  assert.match(
    bindings,
    /onMemberCompositionAccessibilitySelect: value => \{[\s\S]*enterMemberNavigation\(\(\) => \{[\s\S]*enterMemberScope\(\);[\s\S]*onMemberCompositionKindSelect: value => \{[\s\S]*enterMemberNavigation\(\(\) => \{[\s\S]*onMemberCompositionTraitSelect: value => \{[\s\S]*enterMemberNavigation\(\(\) => \{[\s\S]*onMemberGroupOpen: memberKey => \{\s*const focusGeneration = beginSpotlightNavigation\(\);\s*showContentDetailAfterRender\(\);\s*openMemberGroup\(memberKey\);[\s\S]*restoreContentNavigationFocus\(focusGeneration\);/);
});

test("render invalidates focus ownership before replacing content-frame DOM", () => {
  const render = functionDeclaration("render");
  const source = appSource.slice(render.start, render.end);

  assert.match(
    source,
    /const focusedElement = document\.activeElement instanceof HTMLElement[\s\S]*contentFrameFocusOwner = null;\s*contentFrameReplacementAuthority = null;[\s\S]*app\.innerHTML = `/);
});

test("content-frame focus ownership clears after focus settles outside both panes", () => {
  assert.match(
    appSource,
    /function trackContentFrameFocus\(event: FocusEvent\) \{\s*documentFocusGeneration\+\+;\s*contentFrameReplacementAuthority = null;[\s\S]*contentFrameFocusOwner = contentFrameFocusOwnerFor\(focused\)/);
  assert.match(
    appSource,
    /function trackContentFramePointer\(event: PointerEvent\) \{\s*documentFocusGeneration\+\+;\s*contentFrameReplacementAuthority = null;[\s\S]*contentFrameFocusOwner = contentFrameFocusOwnerFor\(pointed\)/);
  assert.match(
    appSource,
    /function releaseContentFrameFocusOwner\(\) \{\s*requestAnimationFrame\(\(\) => \{\s*requestAnimationFrame\(\(\) => \{[\s\S]*contentFrameFocusOwnerFor\(focused\) === null[\s\S]*contentFrameFocusOwner = null/);
  assert.match(
    appSource,
    /document\.addEventListener\("pointerdown", trackContentFramePointer\);\s*document\.addEventListener\("focusin", trackContentFrameFocus\);\s*document\.addEventListener\("focusout", releaseContentFrameFocusOwner\)/);
});

test("focus-preserving renders retain pane authority until restoration", () => {
  const capture = functionDeclaration(
    "captureContentFrameReplacementAuthority");
  const captureSource = appSource.slice(capture.start, capture.end);
  const schedule = functionDeclaration("scheduleMemberFocusAfterRender");
  const scheduleSource = appSource.slice(schedule.start, schedule.end);

  assert.match(
    appSource,
    /const resizeFocusOwner = contentFrameResizeFocusOwner\(\s*focused,\s*contentFrameFocusOwner,\s*replacementFocusOwner\);[\s\S]*contentFrameReplacementAuthority = null;[\s\S]*decideContentFrameResize\([\s\S]*resizeFocusOwner\)/);
  assert.match(
    captureSource,
    /owner: contentFrameFocusOwnerFor\(focused\),\s*focusGeneration: documentFocusGeneration/);
  assert.match(
    scheduleSource,
    /replacementAuthority\.owner !== null[\s\S]*replacementAuthority\.focusGeneration === documentFocusGeneration[\s\S]*contentFrameReplacementAuthority = replacementAuthority/);
  assert.match(
    scheduleSource,
    /memberFocusRestorer\.schedule\([\s\S]*requestAnimationFrame,\s*\(\) => replacementAuthority\.focusGeneration === documentFocusGeneration\);[\s\S]*requestAnimationFrame\(\(\) => \{[\s\S]*contentFrameReplacementAuthority === replacementAuthority[\s\S]*contentFrameReplacementAuthority = null/);
  assert.match(
    appSource,
    /function selectMemberNavEntry\([\s\S]*captureContentFrameReplacementAuthority\(\)[\s\S]*scheduleMemberFocusAfterRender\(preservedFocus, replacementAuthority\)/);
});

test("package-root Open and selected-Type activation preserve local frame state", () => {
  const drillIn = functionDeclaration("drillIn");
  const drillInSource = appSource.slice(drillIn.start, drillIn.end);
  const bindings =
    appSource.match(/function bindTypePanelEvents\(\) \{[\s\S]*?\n}(?=\n\nfunction )/)?.[0]
    ?? "";

  assert.match(
    drillInSource,
    /if \(state\.atPackageRoot\) \{\s*state\.atPackageRoot = false;\s*showContentDetailAfterRender\(\);\s*render\(\);/);
  assert.match(
    drillInSource,
    /if \(navMode\(\) === "type"\) \{\s*const focusGeneration = beginSpotlightNavigation\(\);\s*if \(enterMemberScope\(\)\) \{\s*contentFramePane = "navigation";\s*render\(\);\s*restoreContentNavigationFocus\(focusGeneration\);/);
  assert.match(
    bindings,
    /onTypeSelect: typeId => \{\s*if \(scope\(\) === "type" && typeId === state\.selectedTypeId\) \{\s*if \(contentFrameMedia\.matches\) showContentDetail\(\);\s*return;\s*}\s*showContentDetailAfterRender\(\);[\s\S]*resetMemberFilters\(\)/);
});

test("workspace package selection resets type-specific member filters", () => {
  const selection =
    appSource.match(/function selectWorkspacePackage\([\s\S]*?\n}\n\nfunction activatePackage/)?.[0]
    ?? "";
  assert.match(
    selection,
    /state\.selectedTypeId = defaultVisibleTypeId\(packageModel\);[\s\S]*resetMemberFilters\(\);[\s\S]*resetMemberSectionState\(\)/);
});

test("loaded-package Spotlight selection resets type-specific member filters", () => {
  const selection =
    appSource.match(/function pickSpotlightLoadedPackage\([\s\S]*?\n}\n\nasync function pickSpotlightMember/)?.[0]
    ?? "";
  assert.match(
    selection,
    /state\.selectedTypeId = "";[\s\S]*resetMemberFilters\(\);[\s\S]*resetMemberSectionState\(\)/);
});

test("foreground package reload resets filters before selecting its first type", () => {
  const loadPackage =
    appSource.match(/async function loadPackage\([\s\S]*?\n}\n\nfunction runtimePackLoaded/)?.[0]
    ?? "";
  assert.match(
    loadPackage,
    /if \(deep && \(deep\.type \|\| deep\.member\)\) \{[\s\S]*applyDeepLink\(deep\);[\s\S]*\} else \{\s*resetMemberFilters\(\);\s*state\.selectedTypeId = defaultVisibleTypeId\(packageModel\);/);
});

test("home demos restore the complete parsed location", () => {
  const runHomeDemo =
    appSource.match(/function runHomeDemo\([\s\S]*?\n}\n\n\/\/ Return to the intro/)?.[0]
    ?? "";
  assert.match(
    runHomeDemo,
    /const snapshot = captureCanonicalWorkspaceRestoreSnapshot\(\);[\s\S]*destination = new URL\(link, location\.href\)\.toString\(\);\s*loc = parseWorkspaceHref\(destination\);[\s\S]*const navigationSeq = beginDemoNavigation\(destination\);[\s\S]*restoreWorkspaceFromLocation\(\s*loc,\s*loc,\s*navigationSeq,\s*snapshot,\s*true,\s*message => failDemoWorkspaceOpen\(/);
  assert.match(
    runHomeDemo,
    /async function restoreHomeDemoWorkspace\([\s\S]*finally \{\s*cancelDemoNavigation\(navigationSeq\);[\s\S]*function failDemoWorkspaceOpen\([\s\S]*failWorkspaceCatalogAction\(\s*`Demo failed: \$\{message\}`,\s*snapshot,\s*retryable \? \(\) => runHomeDemo\(demoId\) : null,\s*\(\) => restoreWorkspaceFocus\(document, \{ kind: "demo", id: demoId \}\)/);
  assert.match(
    runHomeDemo,
    /try \{\s*destination = new URL\(link, location\.href\)\.toString\(\);\s*loc = parseWorkspaceHref\(destination\);\s*\} catch \(error\) \{[\s\S]*failDemoWorkspaceOpen\([\s\S]*\}\s*const navigationSeq = beginDemoNavigation\(destination\)/);
  assert.doesNotMatch(runHomeDemo, /workspaceLocation\.replace\("\/demos"/);
  assert.doesNotMatch(runHomeDemo, /type: loc\.type/);
  const restoreWorkspace =
    appSource.match(/async function restoreWorkspaceFromLocation\([\s\S]*?\n}\n\nfunction applyLocationView/)?.[0]
    ?? "";
  assert.match(
    restoreWorkspace,
    /applyLocationView\(loc\);[\s\S]*await applyPlatformLibraryScope\([\s\S]*applyLocationView\(loc\);[\s\S]*applyDeepLink\(deep\)/);
  assert.match(
    appSource,
    /function applyLocationView\(loc: ParsedLocation\) \{\s*state\.lens = loc\.lens \|\| "api";\s*state\.atPackageRoot = loc\.atPackageRoot \|\| false;\s*state\.workspaceSubjectOpen =\s*loc\.workspaceSubjectOpen && state\.atPackageRoot;\s*state\.packageLens = loc\.packageLens \|\| "overview";/);
  const callGraphDemo =
    appSource.match(/async function runCallGraphDemo\([\s\S]*?\n}\n\n\/\/ Loads the full/)?.[0]
    ?? "";
  assert.match(callGraphDemo, /result = await inspectRunHomeDemo\(demoId\)/);
  assert.doesNotMatch(callGraphDemo, /callGraphDemoRunnerSpec|loadPackage\(/);
  assert.match(
    callGraphDemo,
    /clearWorkspacePackages\(\);\s*for \(const packageModel of packages\)/);
  assert.match(
    callGraphDemo,
    /state\.loading = false;\s*stageDemoNavigation\(navigationSeq, buildStateUrl\(\)\.toString\(\)\);\s*render\(\);\s*let renderResult = await renderMermaidCallGraph\(\);\s*while \(renderResult\.status === "superseded"[\s\S]*renderResult = await renderMermaidCallGraph\(\);[\s\S]*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) \{[\s\S]*if \(renderResult\.status === "superseded"\) \{\s*cancelDemoNavigation\(navigationSeq\);\s*syncUrl\(\);[\s\S]*if \(renderResult\.status === "failed"\) \{\s*throw new Error\(renderResult\.message\);[\s\S]*if \(!commitDemoNavigation\(navigationSeq\)\)/);
  assert.match(
    callGraphDemo,
    /state\.selectedTypeId = type\.id;\s*state\.atPackageRoot = false;\s*state\.lens = "api";\s*state\.packageLens = "overview";\s*resetMemberFilters\(\);\s*resetMemberSectionState\(\);\s*state\.platformStack = \[\];\s*state\.memberBrowseTypeId = type\.id;[\s\S]*state\.selectedMemberKey = member\.key;[\s\S]*state\.selectedOverloadIndex = overloadIndex;[\s\S]*state\.memberSection = "call-graph";[\s\S]*state\.memberCallGraph = result\.callGraph;[\s\S]*await renderMermaidCallGraph\(\)/);
  assert.match(
    restoreWorkspace,
    /await loadSelectionData\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;\s*if \(failureHandler\) \{\s*if \(!commitDemoNavigation\(navigationSeq\)\) return;\s*syncUrl\(\);\s*\}\s*if \(focusResult\) \{\s*focusInspectionResult\(navigationSeq\);/);
  assert.match(
    restoreWorkspace,
    /commitWorkspaceShareBasis\(loc\.shareState\);\s*state\.loading = false;\s*render\(\);\s*await loadSelectionData\(\);\s*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;\s*if \(failureHandler\) \{\s*if \(!commitDemoNavigation\(navigationSeq\)\) return;\s*syncUrl\(\);/);
  assert.match(
    restoreWorkspace,
    /if \(!clearWorkspaceRouteFailure\(\)\) \{\s*if \(failureHandler\) \{\s*failureHandler\("The existing package route could not be cleared\."\);[\s\S]*if \(!loc\.package\) \{\s*failureHandler\?\.\(\s*"The resolved product demo did not identify a package\."\)/);
  assert.match(
    restoreWorkspace,
    /const retryRestore = \(\) => restoreWorkspaceFromLocation\(\s*loc,\s*deep,\s*undefined,\s*undefined,\s*focusResult,\s*failureHandler\)/);
  assert.match(
    restoreWorkspace,
    /failCanonicalWorkspaceRestore\([\s\S]*canonicalSnapshot,\s*retryRestore\)/);
  assert.match(
    restoreWorkspace,
    /applyPlatformLibraryScope\([\s\S]*\(\) => restoreWorkspaceFromLocation\(\s*loc,\s*deep,\s*undefined,\s*canonicalSnapshot,\s*focusResult,\s*failureHandler\)/);
  assert.match(
    restoreWorkspace,
    /const loaded = await loadPackage\([\s\S]*if \(loaded && focusResult && navigationSequence\.isCurrent\(navigationSeq\)\) \{[\s\S]*focusInspectionResult\(navigationSeq\);[\s\S]*state\.retryAction = retryRestore/);
  const platformHistory =
    appSource.match(/async function restorePlatformScopeThenDeepLink\([\s\S]*?\n}\n\n\/\/ Load and scope/)?.[0]
    ?? "";
  assert.match(
    platformHistory,
    /await applyPlatformLibraryScope\([\s\S]*applyLocationView\(loc\);\s*applyDeepLink\(loc\)/);
  const runtimeHistory =
    appSource.match(/async function restoreRuntimePackFromHistory\([\s\S]*?\n}\n\nobserveAsync\(bootstrap\(\)/)?.[0]
    ?? "";
  assert.match(
    runtimeHistory,
    /await applyPlatformLibraryScope\([\s\S]*applyLocationView\(loc\);\s*applyDeepLink\(deep\)/);
});

test("catalog package acquisition failure restores warm and cold workspaces locally", () => {
  const spotlightPackageLoad =
    appSource.match(/async function loadPackageFromSpotlight\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    spotlightPackageLoad,
    /const openedFromProductDemos =\s*isProductHomeDemosPath\(location\.pathname\);\s*spotlight\.reset\(\);\s*const catalogSnapshot = openedFromProductDemos\s*\? captureCanonicalWorkspaceRestoreSnapshot\(\)\s*: null;/);
  assert.match(
    spotlightPackageLoad,
    /failureHandler: message =>\s*failWorkspaceCatalogAction\(\s*message,\s*catalogSnapshot,\s*\(\) => loadPackageFromSpotlight\(id, version, framework\),\s*focusWorkbenchSearchOrHeading,\s*\)/);
  assert.match(
    spotlightPackageLoad,
    /if \(loaded \|\| !catalogSnapshot\)\s*focusTypeList\(navigationGeneration, focusGeneration\)/);

  const catalogFailure =
    appSource.match(/function failWorkspaceCatalogAction\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    catalogFailure,
    /restoreCanonicalWorkspaceRestoreSnapshot\(snapshot\);[\s\S]*state\.loading = false;[\s\S]*state\.home = false;[\s\S]*state\.error = "";[\s\S]*state\.retryAction = null;[\s\S]*state\.workspaceSubjectOpen = true;[\s\S]*state\.atPackageRoot = true;[\s\S]*appendQueryNotice\(message, retry\);[\s\S]*render\(\);[\s\S]*if \(!restoreFocus\(\)\) \{\s*focusWorkspace\(document\)/);
  assert.doesNotMatch(catalogFailure, /workspaceLocation\.(?:push|replace)/);

  const loadPackage =
    appSource.match(/async function loadPackage\([\s\S]*?\n}\n\nfunction runtimePackLoaded/)?.[0]
    ?? "";
  assert.match(
    loadPackage,
    /if \(options\.failureHandler\) \{\s*options\.failureHandler\(friendly\.message\);\s*return null;\s*\}\s*if \(prevPackage\)/);

  const platformLibraryLoad =
    appSource.match(/async function openPlatformLibrary\([\s\S]*?\n}\n\nfunction pickSpotlightLoadedPackage/)?.[0]
    ?? "";
  assert.match(
    platformLibraryLoad,
    /const openedFromProductDemos =\s*!scopeOnly && isProductHomeDemosPath\(location\.pathname\);\s*spotlight\.reset\(\);\s*const catalogSnapshot = openedFromProductDemos\s*\? captureCanonicalWorkspaceRestoreSnapshot\(\)\s*: null;/);
  assert.match(
    platformLibraryLoad,
    /if \(!loaded\) \{[\s\S]*const message = failureMessage[\s\S]*if \(catalogSnapshot\) \{\s*failWorkspaceCatalogAction\(\s*message,\s*catalogSnapshot,\s*\(\) => openPlatformLibrary\(assembly, pack\),\s*focusWorkbenchSearchOrHeading,\s*\);[\s\S]*return undefined;[\s\S]*\}\s*state\.error = message;/);
  assert.match(
    appSource,
    /function focusWorkbenchSearchOrHeading\(\): boolean \{\s*return focusWorkbenchSearch\(document\) \|\| focusLevelOneHeading\(\);\s*}/);
});

test("catalog rollback reacquires Workspace occurrences with current authority", () => {
  const restoreSnapshot =
    appSource.match(/function restoreCanonicalWorkspaceRestoreSnapshot\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    restoreSnapshot,
    /clearWorkspaceOccurrenceView\(\);[\s\S]*Object\.assign\(state, snapshot\.state\);[\s\S]*state\.workspaceOccurrenceSignature = "";[\s\S]*state\.workspaceOccurrenceLoading = false;[\s\S]*state\.workspaceOccurrences = null;[\s\S]*state\.workspaceOccurrenceError = "";/);

  const ensureOccurrence =
    appSource.match(/function ensureWorkspaceOccurrenceView\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    ensureOccurrence,
    /const signature = JSON\.stringify\(workspaceOccurrenceRequest\(\)\);\s*if \(state\.workspaceOccurrenceLoading\) return;\s*if \(signature === state\.workspaceOccurrenceSignature\) return;/);
  const occurrenceQuery =
    appSource.match(/async function queryWorkspaceOccurrenceView\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    occurrenceQuery,
    /!superseded[\s\S]*revision === workspaceOccurrenceRevision[\s\S]*signature === state\.workspaceOccurrenceSignature[\s\S]*signature === JSON\.stringify\(workspaceOccurrenceRequest\(\)\)/);
  assert.match(
    occurrenceQuery,
    /const ownsCurrentRequest =\s*revision === workspaceOccurrenceRevision\s*&& signature === state\.workspaceOccurrenceSignature;\s*if \(ownsCurrentRequest\) state\.workspaceOccurrenceLoading = false;\s*const desiredSignature = JSON\.stringify\(workspaceOccurrenceRequest\(\)\);[\s\S]*&& !state\.workspaceOccurrenceLoading[\s\S]*state\.workspaceOccurrenceSignature !== desiredSignature[\s\S]*state\.workspaceOccurrenceSignature = "";\s*ensureWorkspaceOccurrenceView\(\);/);
  const clearOccurrence =
    appSource.match(/function clearWorkspaceOccurrenceView\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    clearOccurrence,
    /workspaceOccurrenceRevision\+\+;[\s\S]*state\.workspaceOccurrenceSignature = "";[\s\S]*state\.workspaceOccurrenceLoading = false;[\s\S]*state\.workspaceOccurrences = null;/);
});

test("Workspace occurrence rerenders preserve catalog failure focus", () => {
  const render =
    appSource.match(
      /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\) \{[\s\S]*?\n}\n\nfunction renderWorkspaceCatalogView/,
    )?.[0]
    ?? "";
  assert.match(
    render,
    /const workbenchSearchHadFocus = focusedElement\?\.id === "open-search";\s*const levelOneHeadingHadFocus =\s*focusedElement\?\.matches\("main h1"\) === true;/);
  assert.equal(
    render.match(
      /else if \(workbenchSearchHadFocus\) \{\s*focusWorkbenchSearch\(document\);\s*} else if \(levelOneHeadingHadFocus\) \{\s*focusLevelOneHeading\(\);/g,
    )?.length,
    2);
  const occurrenceQuery =
    appSource.match(/async function queryWorkspaceOccurrenceView\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(occurrenceQuery, /finally \{[\s\S]*render\(\);\s*}/);
});

test("lens-scoped Platform library changes reset type-specific member state", () => {
  const picker =
    appSource.match(/async function openPlatformLensLibrary\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    picker,
    /originPackage: AppPackage = currentPackage\(\),[\s\S]*noticeRetryState: NoticeRetryState \| null = null[\s\S]*if \(!state\.packages\.includes\(originPackage\)[\s\S]*!packageIdentityEquals\(state\.package, originPackage\)[\s\S]*state\.queryNoticeRetryAction === noticeRetryState\.action[\s\S]*state\.queryNotice = removeAppendedNotice\([\s\S]*state\.queryNoticeRetryAction = null;[\s\S]*const pack = selectedPack \|\| platformPackForAssembly\(key\);[\s\S]*const runtimeResult = await loadRuntimePackAssembly\([\s\S]*\(\) => state\.packages\.includes\(originPackage\),[\s\S]*originPackage\.version\);[\s\S]*const loaded = runtimeResult\.packageModel;[\s\S]*previous: state\.queryNotice[\s\S]*const retryAction = \(\) =>\s*openPlatformLensLibrary\([\s\S]*noticeState\);[\s\S]*runtimeResult\.failureMessage[\s\S]*noticeState\.appended = state\.queryNotice;[\s\S]*if \(!isCurrent\(\)\) return;[\s\S]*state\.libraryScope = new Set\(\[key\]\);[\s\S]*normalizeLibrarySelection\(\);[\s\S]*lens === "integrations"[\s\S]*loadPackageIntegrations\(\)[\s\S]*lens === "opportunities"[\s\S]*loadPackageOpportunities\(\)[\s\S]*lens === "analysis"[\s\S]*loadPackagePerformance\(\)[\s\S]*loadPackageMetadata\(\)/);
  assert.doesNotMatch(picker, /select\.isConnected/);
  assert.match(
    appSource,
    /function normalizeLibrarySelection\(\) \{[\s\S]*state\.selectedTypeId = first\?\.id \|\| "";[\s\S]*state\.selectedMemberKey = "";[\s\S]*state\.selectedOverloadIndex = null;[\s\S]*resetMemberFilters\(\)[\s\S]*function afterLibraryScopeChange\(\) \{\s*normalizeLibrarySelection\(\);\s*renderPreservingMemberFocus\(\)/);
});

test("package Metadata retries remain explicit rather than render-driven", () => {
  const autoLoad =
    appSource.match(/function maybeAutoLoadPackageMetadata\(\) \{[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    autoLoad,
    /state\.packageMetadataKey === packageScopeSignature\(\)\) return;[\s\S]*observeAsync\(loadPackageMetadata\(\)/);
  assert.doesNotMatch(autoLoad, /packageMetadataError/);
});

test("Platform Spotlight distinguishes resident content from core readiness", () => {
  // The runtime scope moved out of `spotlightResults` into its own renderer when the
  // scope dispatch became exhaustive, so this scans the function that now owns the two
  // predicates rather than the one that used to.
  const results =
    appSource.match(/function runtimeSpotlightResults\(query: string\): SpotlightResult\[\] \{[\s\S]*?\n}\n/)?.[0]
    ?? "";
  assert.ok(results, "runtimeSpotlightResults was not found");
  assert.match(
    results,
    /if \(platformSurfaceLoaded\(\)\) \{[\s\S]*spotlightTypeMatches\(query\)/);
  assert.match(
    results,
    /if \(!roster\.length && !runtimePackLoaded\(\)\)/);
});

test("Package query is a routed Spotlight action with typed workspace handoff", () => {
  const results =
    appSource.match(/function spotlightResults\(\): SpotlightResult\[\] \{[\s\S]*?\n}\n\ninterface NugetSearchResult/)?.[0]
    ?? "";
  const route =
    appSource.match(/function openPackageQueryRoute\([\s\S]*?\n}\n\nfunction closePackageQueryRoute/)?.[0]
    ?? "";
  const handoff =
    appSource.match(/async function openPackageQueryRow\([\s\S]*?\n}\n\nconst packageQueryActions/)?.[0]
    ?? "";
  const syncUrl =
    appSource.match(/function syncUrl\(\)[\s\S]*?\n}/)?.[0]
    ?? "";

  assert.match(
    results,
    /kind: "package-query",[\s\S]*prefix: validPackageQueryPrefix\(query\),/);
  assert.match(
    appSource,
    /case "package-query":\s*openPackageQueryRoute\(result\.prefix\);\s*break;/);
  assert.match(
    route,
    /const predecessorEntryId = ensureCurrentHistoryEntryId\(\);[\s\S]*state\.packageQueryOpen = true;[\s\S]*workspaceLocation\.push\([\s\S]*"\/query",[\s\S]*packageQueryHistoryState\([\s\S]*predecessorEntryId,[\s\S]*returnFocus[\s\S]*focusPackageQueryInput\(\)/);
  assert.doesNotMatch(route, /packageQueryController\.run/);
  assert.match(
    appSource,
    /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\) \{\s*sourceInspection\.cancelHiddenRequest\(\);[\s\S]*?document\.body\.classList\.remove\("package-query-route"\);[\s\S]*if \(state\.packageQueryOpen\s*&& state\.engineReady\s*&& !state\.loading\s*&& !state\.error\) \{\s*document\.body\.classList\.add\("package-query-route"\)/);
  assert.match(
    stylesSource,
    /@media \(max-width: 860px\) \{\s*body\.package-query-route \{ min-width: 0; \}/);
  assert.match(
    handoff,
    /packageQueryController\.cancel\(\);\s*state\.packageQueryOpen = false;\s*const navigationSeq = navigationSequence\.begin\(\);\s*packageQueryHandoffNavigationSeq = navigationSeq;[\s\S]*await loadPackage\([\s\S]*\{ navigationSeq }\);[\s\S]*if \(!navigationSequence\.isCurrent\(navigationSeq\)\) \{\s*if \(packageQueryHandoffNavigationSeq === navigationSeq\)\s*packageQueryHandoffNavigationSeq = null;\s*return;\s*\}[\s\S]*packageQueryHandoffNavigationSeq = null;\s*workspaceLocation\.push\(buildStateUrl\(\)\.toString\(\)\)/);
  assert.match(
    syncUrl,
    /function syncUrl\(\) \{\s*if \(currentPackageQueryHandoff\(\)\) return;\s*if \(pendingDemoNavigation[\s\S]*navigationSequence\.isCurrent\(pendingDemoNavigation\.navigationSeq\)\) return;\s*if \(retainFailedWorkspaceUrl\(\)\) return;/);
  assert.match(
    handoff,
    /state\.packageQueryNavigationError = failure;[\s\S]*data-query-row-open=/);
  assert.doesNotMatch(handoff, /state\.packageQueryOpenedFromApp = false/);
  assert.doesNotMatch(handoff, /state\.packageQueryReturnFocus = null/);
  assert.doesNotMatch(
    handoff,
    /state\.packageQueryReturnFocusPending = true/);
  assert.match(
    appSource,
    /function renderPackageQueryPage\(\) \{\s*const focus = capturePackageQueryFocus\(document\);\s*const scrollTop = capturePackageQueryScroll\(document\);[\s\S]*app\.innerHTML = renderPackageQueryView\([\s\S]*bindPackageQueryView\(document, packageQueryActions\);\s*const focusRestoration = restorePackageQueryFocus\(document, focus\);\s*if \(focusRestoration !== "fallback"\) \{\s*restorePackageQueryScroll\(document, scrollTop\)/);
  const popstate =
    appSource.match(/window\.addEventListener\("popstate",[\s\S]*?\n}\);/)?.[0]
    ?? "";
  assert.match(
    popstate,
    /const leftPackageQueryHandoff = currentPackageQueryHandoff\(\);\s*const navigationSeq = navigationSequence\.begin\(\)/);
  assert.match(
    popstate,
    /const navigationSeq = navigationSequence\.begin\(\);\s*let leftPackageQueryForWorkspaceSuccessor = false;\s*const dismissedAnnotatedSourceModal = dismissModalsForRoutedNavigation\(\)/);
  assert.match(
    appSource,
    /function dismissModalsForRoutedNavigation\(\) \{\s*const dismissedAnnotatedSourceModal = dismissAnnotatedSourceModal\(false\);\s*state\.settings = false;\s*state\.keyboardHelp = false;\s*state\.explorer = null;\s*spotlight\.reset\(\);\s*sourceInspection\.clearGraphSource\(\);\s*documentInspection\.clear\(\);\s*return dismissedAnnotatedSourceModal/);
  assert.match(
    route,
    /dismissModalsForRoutedNavigation\(\);\s*navigationSequence\.begin\(\)/);
  assert.match(
    popstate,
    /if \(isPackageQueryPath\(location\.pathname\)\) \{[\s\S]*applyPackageQueryHistory\(history\.state\)/);
  assert.match(
    popstate,
    /state\.loading = !state\.engineReady;\s*render\(\);\s*if \(state\.engineReady\) focusPackageQueryInput\(\)/);
  assert.match(
    popstate,
    /if \(state\.packageQueryOpen \|\| leftPackageQueryHandoff\) \{[\s\S]*packageQueryHandoffNavigationSeq = null;[\s\S]*state\.packageQueryReturnFocusPending =\s*state\.packageQueryReturnFocus !== null[\s\S]*isPackageQueryPredecessor\(\s*history\.state,\s*state\.packageQueryPredecessorEntryId\)/);
  assert.match(
    popstate,
    /if \(!state\.engineReady\) \{\s*const pendingWorkspace = workspaceLocation\.preflightCurrent\(\);\s*const pendingLocation = pendingWorkspace\.visible;[\s\S]*state\.loading = !state\.home;[\s\S]*render\(\);\s*return;\s*\}\s*const loc = parseLocation\(\)/);
  assert.match(
    popstate,
    /if \(leftPackageQueryForWorkspaceSuccessor\) \{\s*packageQueryWorkspaceFocusNavigationSeq = navigationSeq;\s*\}\s*if \(!state\.engineReady\)/);
  assert.match(
    appSource,
    /function closePackageQueryRoute\(\) \{\s*navigationSequence\.begin\(\);[\s\S]*history\.back\(\)/);
  assert.match(
    appSource,
    /function closePackageQueryRoute\(\) \{[\s\S]*state\.packageQueryReturnFocusPending =\s*state\.packageQueryReturnFocus !== null;[\s\S]*history\.back\(\)/);
  assert.match(
    appSource,
    /function openCredits\(\) \{[\s\S]*?navigationSequence\.begin\(\);\s*state\.loading = false;\s*state\.packageQueryOpen = false/);
  assert.match(
    appSource,
    /function restorePackageQueryReturnFocus\(\) \{\s*if \(!state\.packageQueryReturnFocusPending\) return;[\s\S]*state\.packageQueryReturnFocus === "application-query"[\s\S]*if \(state\.packageQueryReturnFocus !== "package-search"\) return;[\s\S]*focusWorkbenchSearch\(document\)[\s\S]*focusLevelOneHeading\(\)/);
  assert.match(
    appSource,
    /if \(isProductHomeDemosPath\(location\.pathname\)\) \{[\s\S]*state\.diag = computeDiagnostics\([\s\S]*render\(\);\s*if \(!state\.packageQueryReturnFocusPending\) \{\s*afterCurrentNavigationFrame\(\(\) =>\s*focusWorkspace\(document\)\)/);
  assert.match(
    appSource,
    /state\.packageQueryReturnFocus === "application-query"[\s\S]*data-application-scope="query"[\s\S]*focusRenderedElement\(queryScope\)[\s\S]*else if \(focusLevelOneHeading\(\)\)/);
  assert.match(
    appSource,
    /function afterCurrentNavigationFrame\(action: \(\) => void\) \{\s*const navigationSeq = navigationSequence\.current\(\);[\s\S]*if \(navigationSequence\.isCurrent\(navigationSeq\)\) action\(\)/);
  assert.match(
    appSource,
    /function focusTypeList\([\s\S]*afterCurrentNavigationFrame\(\(\) => \{[\s\S]*"#type-list"/);
  assert.match(
    appSource,
    /workspaceLocation\.sync\(snapshot, history\.state\)/);
  assert.equal(
    appSource.match(
      /\? withScopeQuery\(state\.packageQueryState\.request, validPrefix\)/g)
      ?.length,
    2);
  assert.equal(
    appSource.match(
      /packageQueryLiveAnnouncer\.reset\(\);\s*void packageQueryController\.run/g)
      ?.length,
    2);
  assert.match(
    appSource,
    /state\.packageQueryCatalogError =\s*`Package-query facets are unavailable/);
  assert.match(
    appSource,
    /navigationError: \[\s*state\.packageQueryCatalogError,\s*state\.packageQueryNavigationError/);
  assert.match(
    appSource,
    /const announcement = takePackageQueryAnnouncement\(\);[\s\S]*packageQueryLiveAnnouncer\.enqueue\(announcement\)/);
  assert.match(
    appSource,
    /createPackageQueryLiveAnnouncer\(\s*\(\) => document\.querySelector<HTMLElement>\("#package-query-announcement"\)\)/);
  assert.match(
    indexSource,
    /id="package-query-announcement"[\s\S]*class="query-announcement"[\s\S]*role="alert"[\s\S]*aria-live="assertive"[\s\S]*aria-atomic="true"/);
  assert.match(
    appSource,
    /workspaceAvailable: state\.package !== null/);
  assert.match(
    appSource,
    /renderApplicationScopeBar\(\s*activeScope === "workspace" \? "workspace" : null,\s*true,\s*escapeHtml\)/);
  assert.match(
    appSource,
    /openPackageQueryRoute\("", \{\s*preserveState: true,\s*returnFocus: "application-query"/);
  assert.match(
    appSource,
    /function selectWorkspaceApplicationScope\(fromPackageQuery = false\) \{\s*const pkg = state\.package;\s*if \(!pkg\) return;\s*const navigationSeq = navigationSequence\.begin\(\);[\s\S]*resolvePackageQueryWorkspaceSuccessor\(\s*\(\) => buildStateUrl\(\),[\s\S]*fallback\.hash = "workspace";[\s\S]*appendQueryNotice\([\s\S]*complete state could not be saved in the address bar[\s\S]*if \(fromPackageQuery\) \{\s*packageQueryWorkspaceFocusNavigationSeq = navigationSeq;\s*\}\s*workspaceLocation\.push\(successor\.url\.toString\(\)\);\s*render\(\)/);
  assert.match(
    appSource,
    /onApplicationScopeSelect: applicationScope => \{[\s\S]*applicationScope === "query"[\s\S]*else if \(scope\(\) !== "workspace"\) \{\s*selectWorkspaceApplicationScope\(\)/);
  assert.match(
    appSource,
    /const focusWorkspaceAfterQuery = state\.packageQueryOpen;\s*if \(focusWorkspaceAfterQuery\) \{\s*state\.packageQueryOpen = false;\s*packageQueryController\.cancel\(\);\s*state\.packageQueryNavigationError = "";\s*\}\s*const navigationSeq = navigationSequence\.begin\(\);\s*if \(focusWorkspaceAfterQuery\) \{\s*packageQueryWorkspaceFocusNavigationSeq = navigationSeq;\s*\}\s*workspaceLocation\.push/);
  assert.match(
    appSource,
    /function restorePackageQueryWorkspaceFocus\(\) \{\s*const navigationSeq = packageQueryWorkspaceFocusNavigationSeq;[\s\S]*navigationSequence\.isCurrent\(navigationSeq\)[\s\S]*afterCurrentNavigationFrame\(\(\) => \{\s*if \(!focusLevelOneHeading\(\)\) \{\s*document\.querySelector<HTMLElement>\("#type-list"\)\?\.focus\(\)/);
  assert.match(
    appSource,
    /closest\("\[data-scope-bar\], \[data-application-scope-strip\]"\)[\s\S]*bindEvents\(\);[\s\S]*if \(scopeBarOwnsFocus\) \{\s*let restored = false;\s*if \(scopeBarFocus\) \{\s*scopeBarBinding\?\.revealFocusTarget\(scopeBarFocus\);\s*restored = restoreScopeBarFocus\(document, scopeBarFocus\);\s*\}\s*if \(!restored\) \{\s*document\.querySelector<HTMLElement>\("\.brand"\)\s*\?\.focus\(\{ preventScroll: true \}\);\s*\}\s*app\.removeAttribute\("tabindex"\);\s*\}\s*restorePackageQueryReturnFocus\(\);\s*restorePackageQueryWorkspaceFocus\(\)/);
  assert.match(
    popstate,
    /leftPackageQueryForWorkspaceSuccessor =\s*!state\.packageQueryReturnFocusPending;[\s\S]*if \(leftPackageQueryForWorkspaceSuccessor\) \{\s*packageQueryWorkspaceFocusNavigationSeq = navigationSeq/);
  assert.match(
    appSource,
    /url => workspaceLocation\.replace\(url, history\.state\)/);
});

test("authoritative location restore clears filters and applies aggregate Platform scope", () => {
  assert.match(
    appSource,
    /function resetLocationFilters\(\) \{\s*state\.typeFilter = "";\s*state\.namespaceFilter = "";\s*state\.kindFilter = "";\s*state\.libraryScope = null;\s*state\.typeCursor = 0;\s*resetMemberFilters\(\)/);
  const workspaceRestore =
    appSource.match(/async function restoreWorkspaceFromLocation\([\s\S]*?\n}\n\nfunction applyLocationView/)?.[0]
    ?? "";
  assert.match(workspaceRestore, /resetLocationFilters\(\);\s*clearWorkspacePackages\(\)/);
  assert.match(
    workspaceRestore,
    /if \(isRuntimePackId\(targetModel\.id\)\) \{\s*const scoped = await applyPlatformLibraryScope\(\s*loc\.library/);
  const popstate =
    appSource.match(/window\.addEventListener\("popstate",[\s\S]*?\n}\);/)?.[0]
    ?? "";
  assert.match(popstate, /if \(bareHome\)[\s\S]*resetLocationFilters\(\);\s*const deep = loc/);
  const runtimeHistory =
    appSource.match(/async function restoreRuntimePackFromHistory\([\s\S]*?\n}\n\nobserveAsync\(bootstrap\(\)/)?.[0]
    ?? "";
  assert.match(
    runtimeHistory,
    /activatePackage\(pack,[\s\S]*await applyPlatformLibraryScope\(\s*loc\.library,[\s\S]*applyLocationView\(loc\)/);
});

test("type projection completions render only while current and preserve navigation focus", () => {
  const typeSource =
    sourceInspectionSource.match(/async loadTypeSource\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  const typeSourceAuthority =
    sourceInspectionSource.match(
      /const publishTypeSourceEvent = \(event: TypeSourceFeatureEvent\)[\s\S]*?const typeSourceSession:/,
    )?.[0]
    ?? "";
  assert.match(
    typeSourceAuthority,
    /case "started":[\s\S]*case "replaced":[\s\S]*context\.preservedFocus =\s*dependencies\.renderPreservingMemberFocus\(\);[\s\S]*case "terminal":[\s\S]*state\.typeSourceLoading = false;[\s\S]*if \(context\.request\.isVisible\(\)\) \{\s*dependencies\.renderPreservingMemberFocus\(\s*context\.preservedFocus,/);
  assert.match(
    typeSource,
    /typeSourceSession\.start\(request, typeSourceAdapter\)[\s\S]*await result\.handle\.quiesced/);
  assert.doesNotMatch(typeSource, /sourceRequestGeneration|ownsRequest/);
  assert.doesNotMatch(typeSource, /finally \{[\s\S]*dependencies\.render\(\)/);
  const typeMetadata =
    appSource.match(/async function loadSelectedTypeMetadata\([\s\S]*?\n}\n\n\/\/ Projects/)?.[0]
    ?? "";
  assert.match(
    typeMetadata,
    /return metadataInspection\.loadTypeMetadata\(\{[\s\S]*packageId: pkg\.id,[\s\S]*assembly: type\.assembly,[\s\S]*type: type\.queryId \?\? type\.id,[\s\S]*isVisible: \(\) => \{[\s\S]*!state\.home[\s\S]*!state\.settings[\s\S]*!state\.explorer\?\.open[\s\S]*!state\.loading[\s\S]*!state\.error[\s\S]*!workbenchOverlayOwnsFocus\(\)[\s\S]*typeMetadataSignature\(currentType, pkg\) === signature/);
  assert.doesNotMatch(typeMetadata, /typeMetadataGeneration|inspectTypeProjection/);
  const typeMetadataCoordinator =
    metadataInspectionSource.match(/async loadTypeMetadata\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  assert.match(
    typeMetadataCoordinator,
    /const generation = \+\+state\.typeMetadataGeneration;[\s\S]*const preservedFocus = dependencies\.renderPreservingMemberFocus\(\);[\s\S]*generation === state\.typeMetadataGeneration[\s\S]*if \(ownsRequest\(\)\) state\.typeMetadata = result;[\s\S]*if \(request\.isVisible\(\)\) \{\s*dependencies\.renderPreservingMemberFocus\(preservedFocus\);/);
  assert.doesNotMatch(
    typeMetadataCoordinator,
    /renderPreservingMemberFocus\(preservedFocus\);\s*if \(state\.typeMetadata\?\.graphNodes/);
});

test("metadata explorer request coordination stays outside the composition root", () => {
  const windowLoader =
    appSource.match(/async function loadExplorerWindow\([\s\S]*?\n}\n\n\/\/ Lists/)?.[0]
    ?? "";
  const heapLoader =
    appSource.match(/async function loadExplorerHeap\([\s\S]*?\n}\n\/\/ ref->def/)?.[0]
    ?? "";
  const focus =
    appSource.match(/function applyExplorerFocus\(\)[\s\S]*?\n}\n\n\/\/ Center/)?.[0]
    ?? "";
  const pageSizer =
    appSource.match(/function syncExplorerPageSize\(\)[\s\S]*?\n}\n\n\/\/ Renders/)?.[0]
    ?? "";
  assert.match(
    windowLoader,
    /return metadataInspection\.loadExplorerWindow\(index, startRowId, maxRows\)/);
  assert.match(
    heapLoader,
    /return metadataInspection\.loadExplorerHeap\(heapName\)/);
  assert.doesNotMatch(
    `${windowLoader}\n${heapLoader}`,
    /inspectPackageMetadataTable|inspectPlatformMetadataTable|inspectPackageHeapEntries|inspectPlatformHeapEntries/);
  assert.match(
    metadataInspectionSource,
    /const ownsRequest = \(\) =>[\s\S]*state\.explorer === explorer[\s\S]*requests\.get\(index\) === requestSequence[\s\S]*dependencies\.queryPlatformTable[\s\S]*dependencies\.queryPackageTable[\s\S]*if \(!ownsRequest\(\)\) return;[\s\S]*index === explorer\.focusIndex && !explorer\.focusHeap/);
  assert.match(
    metadataInspectionSource,
    /dependencies\.queryPlatformHeap[\s\S]*dependencies\.queryPackageHeap[\s\S]*state\.explorer !== explorer[\s\S]*explorer\.focusHeap === heapName/);
  assert.match(
    focus,
    /const heapWindow = ex\.heapWindows\[entry\.heap\];\s*if \(!heapWindow \|\| \(!heapWindow\.loading && !heapWindow\.data\)\)\s*observeAsync\(loadExplorerHeap\(entry\.heap\), "Loading metadata heap rows"\);\s*else render\(\)/);
  assert.match(
    focus,
    /const onScreen = win && !win\.loading && win\.data &&/);
  assert.match(
    pageSizer,
    /if \(win\?\.data && !win\.loading[\s\S]*loadExplorerWindow\(ex\.focusIndex, win\.data\.startRowId, fit\)/);
  assert.match(
    appSource,
    /state\.explorer\.focusHeap && !state\.explorer\.heapWindows\[state\.explorer\.focusHeap\]/);
});

test("call graph request coordination stays outside the composition root", () => {
  const loader =
    appSource.match(/async function loadSelectedMemberCallGraph\([\s\S]*?\n}\n\n\/\/ Update just/)?.[0]
    ?? "";
  assert.match(
    loader,
    /return callGraphInspection\.load\(\{[\s\S]*type: type\.queryId \?\? type\.id,[\s\S]*typeIdentity: type\.definitionId \?\? type\.id,[\s\S]*platformType:\s*type\.definitionId \?\? type\.metadataId \?\? type\.queryId \?\? type\.id,[\s\S]*platformPack:\s*platformPackForAssembly\(type\.assembly, type\.platformPack\) \?\? "",[\s\S]*isCurrent: \(\) => memberRequestIsCurrent\(signature, true\)/);
  assert.doesNotMatch(
    loader,
    /memberCallGraphSeq|inspectMemberCallGraph|inspectExpandPlatformCallGraph/);
  assert.match(
    callGraphInspectionSource,
    /dependencies\.queryWorkspace\(request, \[\]\)[\s\S]*dependencies\.nextPaint\(\)[\s\S]*request\.workspacePackages[\s\S]*dependencies\.patchCallGraphSection\(previousMermaid\)/);
  assert.match(
    callGraphInspectionSource,
    /request\.isRuntimePack[\s\S]*loadPlatformGraph\(request\)/);
});

test("typeless member lookup and request guards stay empty", () => {
  assert.match(
    appSource,
    /function memberGroups\([\s\S]*type: AppTypeSurface \| null \| undefined,[\s\S]*for \(const member of type\?\.api \?\? \[\]\)/);
  assert.match(
    appSource,
    /function memberRequestIsCurrent\([\s\S]*const type = selectedType\(\);\s*if \(!type\) return false;\s*const member = selectedMember\(type\)/);
});

test("history validates saved type and member identity before restoring Member state", () => {
  const applyView =
    appSource.match(/function applyView\(view: WorkspaceView\) \{[\s\S]*?\n}\n\nconst navigationHistory/)?.[0]
    ?? "";
  assert.match(applyView, /const type = pkg\.types\.find\(item => item\.id === view\.selectedTypeId\)/);
  assert.match(
    applyView,
    /const memberHistory = restoreMemberHistoryState\(\s*view,\s*type,\s*member/);
  assert.match(
    applyView,
    /state\.selectedTypeId = type\?\.id \?\? pkg\.types\[0\]\?\.id \?\? "";[\s\S]*state\.selectedMemberKey = memberHistory\.selectedMemberKey;[\s\S]*state\.memberBrowseTypeId = memberHistory\.memberBrowseTypeId;[\s\S]*state\.memberKindFilter = memberHistory\.memberKindFilter;[\s\S]*state\.memberAccessibilityFilter = memberHistory\.memberAccessibilityFilter;[\s\S]*state\.memberTraitFilter = memberHistory\.memberTraitFilter;[\s\S]*state\.memberTextFilter = memberHistory\.memberTextFilter/);
  assert.match(
    applyView,
    /state\.selectedOverloadIndex = memberHistory\.selectedOverloadIndex;[\s\S]*state\.memberSection = memberHistory\.memberSection;[\s\S]*state\.selectedBodyTarget = memberHistory\.selectedBodyTarget/);
  assert.match(
    applyView,
    /navigationHistory\.normalizeCurrent\(\);[\s\S]*loadSelectedMemberSource\(\)[\s\S]*else \{\s*render\(\)/);
  assert.match(
    appSource,
    /const navigationHistory = createNavigationHistory\(\{\s*capture: captureView,\s*signature: workspaceViewSignature,\s*apply: applyView/);
  assert.match(
    workspaceNavigationSource,
    /function workspaceViewSignature\([\s\S]*b: graphTarget \? null : encodeBodyTarget\(view\.bodyTarget\),[\s\S]*g: graphTarget/);
  assert.match(
    appSource,
    /function captureView\(\): WorkspaceView \| null \{[\s\S]*bodyTarget: state\.selectedBodyTarget/);
  assert.match(
    appSource,
    /else if \(state\.selectedTypeId !== current\.id\) \{\s*state\.selectedTypeId = current\.id;\s*state\.selectedMemberKey = "";\s*state\.memberBrowseTypeId = "";\s*state\.selectedOverloadIndex = null;\s*resetMemberFilters\(\);\s*resetMemberSectionState\(\)/);
});

test("Platform scope restoration defers selection, rendering, and data loading", () => {
  const openPlatformLibrary =
    appSource.match(/async function openPlatformLibrary\([\s\S]*?\n}\n\nfunction pickSpotlightLoadedPackage/)?.[0]
    ?? "";
  assert.match(
    openPlatformLibrary,
    /const scopeOnly = options\.scopeOnly === true;[\s\S]*candidate\.toLowerCase\(\) === key\.toLowerCase\(\)[\s\S]*state\.libraryScope = actualKey \? new Set\(\[actualKey\]\) : null;[\s\S]*if \(scopeOnly\) return hasLib \? pkg : undefined;[\s\S]*const selectionData = loadSelectionData\(\);[\s\S]*render\(\);/);
  const applyScope =
    appSource.match(/async function applyPlatformLibraryScope\([\s\S]*?\n}\n\n\/\/ History/)?.[0]
    ?? "";
  assert.match(applyScope, /scopeOnly: true/);
});

test("Type Source completion settles behind workbench overlays", () => {
  const typeSource =
    sourceInspectionSource.match(/async loadTypeSource\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  const typeSourceAuthority =
    sourceInspectionSource.match(
      /const publishTypeSourceEvent = \(event: TypeSourceFeatureEvent\)[\s\S]*?const typeSourceSession:/,
    )?.[0]
    ?? "";
  assert.match(
    appSource,
    /function workbenchOverlayOwnsFocus\(\) \{\s*return workbenchModalOwnsFocus\(\);[\s\S]*function workbenchModalOwnsFocus\(\) \{\s*return state\.spotlightOpen\s*\|\| state\.graphSourceOpen\s*\|\| state\.docViewerOpen\s*\|\| state\.memberAnnotatedModal !== null;/);
  assert.match(
    appSource,
    /sourceInspection\.loadTypeSource\(\{[\s\S]*isVisible: \(\) =>\s*currentSourceOperationKind\(\) === "type"\s*&& !workbenchModalOwnsFocus\(\)/);
  assert.match(
    typeSourceAuthority,
    /case "terminal":[\s\S]*state\.typeSourceLoading = false;[\s\S]*if \(context\.request\.isVisible\(\)\) \{\s*dependencies\.renderPreservingMemberFocus\(\s*context\.preservedFocus,/);
  assert.match(
    typeSource,
    /typeSourceSession\.start\(request, typeSourceAdapter\)[\s\S]*await result\.handle\.quiesced/);
  assert.match(
    appSource,
    /function isInteractiveElement\(element: Element \| null\)[\s\S]*"button, a\[href\], input, select, textarea, summary, "[\s\S]*\[role=button\][\s\S]*id: "workspace\.drill-in"[\s\S]*key: "Enter"[\s\S]*!isInteractiveElement\([\s\S]*event\.target instanceof Element \? event\.target : null\)/);
});

test("member-less Metadata omits the empty composition call to action", () => {
  const composition =
    appSource.match(
      /function renderMemberComposition\(type: AppTypeSurface\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    composition,
    /if \(!kinds && !accessibilities && !traits\) return "";/);
});

test("Metadata composition excludes graph-projected implementation members", () => {
  const composition =
    appSource.match(
      /function renderMemberComposition\(type: AppTypeSurface\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    composition,
    /const \{ publicMembers \} = partitionGraphMembers\(type\.api\);/);
  assert.match(
    composition,
    /memberKinds\(publicSurface\)/);
  assert.match(
    composition,
    /memberAccessibilities\(publicSurface\)/);
  assert.match(
    composition,
    /availableMemberTraits\(publicSurface\)/);
});

test("settings keep a viewport-bounded scroll region", () => {
  const settingsDialogRule =
    stylesSource.match(/\.application-dialog\s*\{([^}]*)\}/s)?.[1] ?? "";
  const settingsMainRule =
    stylesSource.match(/\.settings-main\s*\{([^}]*)\}/s)?.[1] ?? "";
  assert.match(
    settingsDialogRule,
    /(?:^|\n)\s*max-height: min\(760px, calc\(100vh - 24px\)\);/);
  assert.match(
    settingsDialogRule,
    /(?:^|\n)\s*grid-template-rows: auto minmax\(0, 1fr\);/);
  assert.match(
    settingsMainRule,
    /(?:^|\n)\s*min-height: 0;/);
  assert.match(
    settingsMainRule,
    /(?:^|\n)\s*overflow-y: auto;/);
});

test("home keeps a viewport-bounded scroll region and reachable footer", () => {
  const homeRule =
    stylesSource.match(/\.home\s*\{([^}]*)\}/s)?.[1] ?? "";
  const homeHeroRule =
    stylesSource.match(/\.home-hero\s*\{([^}]*)\}/s)?.[1] ?? "";
  assert.match(
    homeRule,
    /(?:^|\n)\s*height: 100vh;/);
  assert.doesNotMatch(
    homeRule,
    /(?:^|\n)\s*min-height:/);
  assert.match(
    homeRule,
    /(?:^|\n)\s*grid-template-rows: auto auto minmax\(0, 1fr\) auto;/);
  assert.match(
    homeHeroRule,
    /(?:^|\n)\s*min-height: 0;/);
  assert.match(
    homeHeroRule,
    /(?:^|\n)\s*overflow-y: auto;/);
  assert.match(
    stylesSource,
    /\.home > \.query-notice\s*\{\s*grid-row: 2;\s*\}/);
  assert.match(
    homeHeroRule,
    /(?:^|\n)\s*grid-row: 3;/);
  assert.match(
    stylesSource,
    /\.home-foot\s*\{[^}]*grid-row: 4;/s);
});

test("all dependency navigation paths use one product-owned coordinate matcher", () => {
  assert.equal(
    [...applicationSources.matchAll(/uniqueCompatiblePackage\(/g)].length,
    6);
  assert.match(
    generatedFacadeSource("inspect-web-package"),
    /\$requireManagedExports\(\)\["PackageExports"\]\["MatchPackageDependencyCoordinate\.-?\d+"\]/);
  assert.match(
    appSource,
    /matchPackageDependencyCoordinate\([\s\S]*?JSON\.stringify\(dependencyCoordinateCandidates\(packages\)\)/);
  assert.doesNotMatch(
    generatedFacadeSourceText,
    /PackageVersionSatisfiesDependencyRange/);
  assert.doesNotMatch(appSource, /dependencyVersionSatisfies/);
});

test("empty dependency graph invalidates an in-flight render", () => {
  const sequence = createDependencyGraphRenderSequence();
  const stale = sequence.begin();

  sequence.invalidate();

  assert.equal(sequence.isCurrent(stale), false);
  assert.match(
    appSource,
    /if \(!built\) \{\s*depGraphRenderSequence\.invalidate\(\);/);
});

test("stale dependency graph cleanup preserves a replacement with the same signature", () => {
  const sequence = createDependencyGraphRenderSequence();
  const dataset = {};
  const pending = createDependencyGraphPendingState(dataset);
  const signature = "same graph";

  const stale = sequence.begin();
  pending.begin(signature, stale);
  sequence.invalidate();
  pending.invalidate();
  const replacement = sequence.begin();
  pending.begin(signature, replacement);

  assert.equal(pending.complete(signature, stale), false);
  assert.equal(pending.isPending(signature), true);
  assert.equal(pending.complete(signature, replacement), true);
  assert.equal(pending.isPending(signature), false);
});

test("dependency graph binds navigation to generated node identities", () => {
  assert.match(
    graphSource,
    /const nodeInfoById = new Map<string, DependencyGraphNodeInfo>\(\);[\s\S]*for \(const key of keys\)[\s\S]*if \(id && info\) nodeInfoById\.set\(id, info\)/);
  assert.match(
    graphInteractionsSource,
    /const dataId = node\.getAttribute\("data-id"\);[\s\S]*return dataId \|\| idMatch\?\.\[1\] \|\| ""/);
  assert.match(
    appSource,
    /bindDependencyGraphNodes\(viewport, nodeId => \{[\s\S]*built\.nodeInfoById\.get\(nodeId\)/);
  assert.doesNotMatch(appSource, /nodeInfoByLabel/);
  assert.match(
    appSource,
    /Dependency graph truncated at \$\{built\.nodeLimit\} nodes/);
  assert.match(
    appSource,
    /const signature = dependencyGraphRenderSignature\(built\)/);
});

test("dependency navigation reserves identity and surfaces resolution failures", () => {
  assert.doesNotMatch(appSource, /state\.navigationSeq/);
  assert.match(
    appSource,
    /const navigationSeq = navigationSequence\.begin\(\);\s+state\.loading = true;[\s\S]*?await resolveDependencyVersion/);
  assert.match(
    appSource,
    /if \(!navigationSequence\.isCurrent\(navigationSeq\)\) return;\s+state\.loading = false;\s+appendQueryNotice/);
  assert.match(
    graphSource,
    /packageIdentityKey\(uniqueCompatiblePackage\(\s+model\.packages,\s+dependency\.id,\s+dependency\.versionRange\)\) === target\.packageKey/);
  assert.match(
    appSource,
    /matchPackageDependencyCoordinate\(\s+packageId,\s+declaredRange \?\? null,\s+JSON\.stringify\(dependencyCoordinateCandidates\(packages\)\)\)/);
  assert.doesNotMatch(appSource, /dependencyVersionSatisfies/);
});

test("spotlight candidate identity includes version and framework", () => {
  const net8 = packageAt("1.0.0", "net8.0");
  const net9 = packageAt("1.0.0", "net9.0");
  const v2 = packageAt("2.0.0", "net8.0");

  assert.notEqual(
    spotlightCandidateKey(net8, "Example.Type"),
    spotlightCandidateKey(net9, "Example.Type"));
  assert.notEqual(
    spotlightCandidateKey(net8, "Example.Type"),
    spotlightCandidateKey(v2, "Example.Type"));
});

test("spotlight cache signature changes when a coordinate is replaced", () => {
  const oldPackage = packageAt("1.0.0", "net8.0", 4);
  const newVersion = packageAt("2.0.0", "net8.0", 4);
  const newFramework = packageAt("1.0.0", "net9.0", 4);

  const oldSignature = spotlightCandidateSignature(oldPackage, [oldPackage]);
  assert.notEqual(
    oldSignature,
    spotlightCandidateSignature(newVersion, [newVersion]));
  assert.notEqual(
    oldSignature,
    spotlightCandidateSignature(newFramework, [newFramework]));
});

test("member cache signatures use the same complete coordinates", () => {
  const oldPackage = packageAt("1.0.0", "net8.0", 4);
  const newVersion = packageAt("2.0.0", "net8.0", 4);

  assert.notEqual(
    spotlightCandidateSignature(oldPackage, [oldPackage]),
    spotlightCandidateSignature(newVersion, [newVersion]));
});

test("member source request identity includes decompiler taste", () => {
  const request = ["Example.Package", "1.0.0", "net8.0", "Example.dll", "Example.Widget", "M:Run"];

  assert.notEqual(
    memberRequestKey(request),
    memberRequestKey(request, ["prefer-var"]));
  assert.notEqual(
    memberRequestKey(request, ["prefer-var"]),
    memberRequestKey(request, ["prefer-explicit-types"]));
});

test("member request identity distinguishes colliding type queries", () => {
  const memberSignature =
    appSource.match(/function memberRequestSignature\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    memberSignature,
    /type\?\.queryId \?\? type\?\.id,\s+type\?\.definitionId \?\? type\?\.id/);

  const request = [
    "Example.Package",
    "1.0.0",
    "net8.0",
    "Example.dll",
    "Example.Outer.Inner"
  ];
  assert.notEqual(
    memberRequestKey([...request, "Example.Outer+Inner", "M:Run"]),
    memberRequestKey([...request, "Example.Outer\\.Inner", "M:Run"]));
});

test("annotated source request identity includes the selected body", () => {
  const annotatedLoader =
    appSource.match(
      /async function loadSelectedMemberAnnotatedSource\(\)[\s\S]*?\n}\n\nfunction memberRequestSignature/)?.[0]
    ?? "";
  assert.match(
    annotatedLoader,
    /const signature = memberRequestSignature\(type, overload, true, true\)/);
  assert.match(
    annotatedLoader,
    /isCurrent: \(\) => memberRequestIsCurrent\(signature, true, true\)/);
  assert.match(
    annotatedLoader,
    /state\.selectedBodyTarget\?\.selectorKey \?\? overload\.graphSelectorKey,[\s\S]*?state\.selectedBodyTarget\?\.metadataToken \?\? overload\.metadataToken/);
  const annotatedCoordinator =
    memberDetailInspectionSource.match(/async loadAnnotated\(request\)[\s\S]*?\n    },/)?.[0]
    ?? "";
  assert.equal(
    [...annotatedCoordinator.matchAll(/request\.isCurrent\(\)/g)].length,
    3);

  const request = [
    "Example.Package",
    "1.0.0",
    "net8.0",
    "Example.dll",
    "Example.Outer.Inner",
    "Example.Outer+Inner",
    "M:Run"
  ];
  // The product stringifies the metadata token before building the key
  // (`dotnet-inspect.ts` pushes `String(state.selectedBodyTarget?.metadataToken ?? "")`),
  // so the fixture spells the token the same way rather than relying on `join` to coerce
  // a raw number.
  assert.notEqual(
    memberRequestKey([...request, String(0x06000001), "M:Run"]),
    memberRequestKey([...request, String(0x06000002), "M:<Run>b__0_0"]));
});

test("member detail adapters preserve exact engine coordinates", () => {
  const coordinator =
    appSource.match(
      /const memberDetailInspection = createMemberDetailInspectionCoordinator\(\{[\s\S]*?\n}\);/)?.[0]
    ?? "";
  const documentationLoader =
    appSource.match(
      /async function loadSelectedMemberDocumentation\(\)[\s\S]*?\n}\n\nasync function loadSelectedMemberSource/)?.[0]
    ?? "";
  const annotatedLoader =
    appSource.match(
      /async function loadSelectedMemberAnnotatedSource\(\)[\s\S]*?\n}\n\nfunction memberRequestSignature/)?.[0]
    ?? "";
  const factsLoader =
    appSource.match(
      /async function loadSelectedMemberFacts\(\)[\s\S]*?\n}\n\ninterface LoadPackageOptions/)?.[0]
    ?? "";
  const factsRenderer =
    appSource.match(
      /function renderMemberFacts\([\s\S]*?\n}\n\ntype FactTableColumn/)?.[0]
    ?? "";

  assert.match(
    coordinator,
    /inspectMemberDocumentation\(\s*request\.packageId,\s*request\.version,\s*request\.framework,\s*request\.assembly,\s*documentationId\)/);
  assert.match(
    coordinator,
    /inspectMemberAnnotatedSource\(\s*request\.packageId,\s*request\.version,\s*request\.framework,\s*request\.assembly,\s*request\.typeIdentity,\s*request\.type,\s*request\.member,\s*request\.memberSignature,\s*request\.selectorKey,\s*request\.metadataToken,\s*request\.taste\)/);
  assert.match(
    coordinator,
    /const document = result\.document;\s*validateAnnotatedSourceDocument\(document\);\s*return \{ \.\.\.result, document \};/);
  assert.match(
    coordinator,
    /inspectMemberFacts\(\s*request\.packageId,\s*request\.version,\s*request\.framework,\s*request\.assembly,\s*request\.typeIdentity,\s*request\.member,\s*request\.memberSignature,\s*request\.selectorKey,\s*request\.metadataToken,\s*request\.implementationBodySelected\)/);
  assert.match(
    documentationLoader,
    /const signature = memberRequestSignature\(type, overload\)/);
  assert.match(
    documentationLoader,
    /return memberDetailInspection\.loadDocumentation\(\{\s*signature,\s*packageId: pkg\.id,\s*version: pkg\.version,\s*framework: pkg\.activeFramework,\s*assembly: type\.assembly,\s*overload,\s*isRuntimePack: Boolean\(state\.package\?\.isRuntimePack\),\s*isCurrent: \(\) => memberRequestIsCurrent\(signature\)/);
  assert.match(
    annotatedLoader,
    /loadAnnotated\(\{\s*signature,\s*packageId: pkg\.id,\s*version: pkg\.version,\s*framework: pkg\.activeFramework,\s*assembly: type\.assembly,\s*typeIdentity: type\.definitionId \?\? type\.id,\s*type: type\.queryId \?\? type\.id,\s*member: state\.selectedBodyTarget\?\.memberName \?\? overload\.name,\s*memberSignature: overload\.signature,[\s\S]*taste: JSON\.stringify\(state\.taste\)/);
  assert.match(
    factsLoader,
    /const signature = memberRequestSignature\(type, overload, true\)/);
  assert.match(
    factsLoader,
    /const implementationBody = graphOnlyImplementationBody\(overload\);\s*const implementationMetadataToken = implementationBody\?\.token \?\? 0;\s*const implementationBodySelected = implementationMetadataToken !== 0;\s*return memberDetailInspection\.loadFacts\(\{\s*signature,\s*packageId: pkg\.id,\s*version: pkg\.version,\s*framework: pkg\.activeFramework,\s*assembly: type\.assembly,\s*type: type\.queryId \?\? type\.id,\s*typeIdentity: type\.definitionId \?\? type\.id,\s*member: implementationBody\?\.memberName\s*\?\? state\.selectedBodyTarget\?\.memberName\s*\?\? overload\.name,\s*memberSignature: overload\.signature,\s*selectorKey: implementationBody\?\.selectorKey\s*\?\? state\.selectedBodyTarget\?\.selectorKey\s*\?\? overload\.graphSelectorKey,\s*metadataToken: implementationMetadataToken,\s*implementationBodySelected,\s*isCurrent: \(\) => memberRequestIsCurrent\(signature, true\)/);
  assert.match(
    packageAcquisitionSource,
    /implementationBody\?: InspectedMemberBodySelector/);
  assert.match(
    packageAcquisitionSource,
    /function retainGraphOnlyImplementationBody[\s\S]*overload\.bodySelectors\.find\([\s\S]*overload\.implementationBody = selectedBody;[\s\S]*graphMemberTargetWithSelectedBody\(target, selectedBody\)/);
  assert.match(
    appSource,
    /const selectedTarget = graphMemberTargetWithSelectedBody\(\s*target,\s*projection\.selectedBody\);[\s\S]*singleProjectedGraphMember\(projection\.type\)[\s\S]*stageGraphMemberSelection\([\s\S]*selectedTarget,[\s\S]*projectedMember\);[\s\S]*commitGraphMemberSelection\([\s\S]*selectedTarget,[\s\S]*staged\)/);
  assert.doesNotMatch(
    factsLoader,
    /state\.selectedBodyTarget\?\.metadataToken \?\? overload\.metadataToken/);
  assert.match(
    factsRenderer,
    /const heapAllocations = facts\.allocations\.filter\(a => a\.countedAsHeap\);\s*const allocOffsets = heapAllocations\.map\(a => a\.offset\)/);
  assert.match(
    factsRenderer,
    /\["Heap", row => row\.countedAsHeap \? "yes" : "no"\][\s\S]*No allocation occurrences were found in this method/);
  assert.match(
    factsRenderer,
    /\["Operation", "operation"\],[\s\S]*\["Requirement", "requirement"\],[\s\S]*\["Evidence", "evidence"\]/);
});

test("type source identity includes decompiler taste", () => {
  const typeSignature =
    typePanelSource.match(/export function typeSourceSignature\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(typeSignature, /memberRequestKey\(/);
  assert.match(typeSignature, /taste/);
});

test("source operations cancel when superseded or hidden", () => {
  assert.match(
    generatedFacadeSource("inspect-web-source"),
    /\$requireManagedExports\(\)\["SourceExports"\]\["CancelSourceQuery\.-?\d+"\]/);
  assert.match(
    generatedFacadeSource("inspect-web-source"),
    /export function cancelSourceQuery\(\)[\s\S]*?return \$requireManagedExports\(\)/);
  assert.match(
    appSource,
    /cancelSourceQuery: cancelSourceInspection/);
  assert.match(
    appSource,
    /const operationAuthority = createOperationAuthorityPage\(\);[\s\S]*createSourceInspectionCoordinator\(\{[\s\S]*operationAuthority,/);

  const renderBody =
    appSource.match(
      /function render\(options: \{ synchronizeUrl\?: boolean \} = \{\}\)[\s\S]*?\n}/,
    )?.[0]
    ?? "";
  assert.match(renderBody, /sourceInspection\.cancelHiddenRequest\(\)/);
  assert.match(
    appSource,
    /createSourceInspectionCoordinator\(\{[\s\S]*memberSourceHasConcreteOverload,[\s\S]*cancelEngineSourceRequest: \(\) => cancelSourceInspection\?\.\(\)/);
  assert.match(
    sourceInspectionSource,
    /const cancelCurrentRequest = \(\) => \{[\s\S]*cancelSourceRequestState\(state\)[\s\S]*cancelHiddenRequest\(\)[\s\S]*sourceSurfaceIsVisible\(\s*state,\s*dependencies\.memberSourceHasConcreteOverload\(\)\)[\s\S]*cancelCurrentRequest\(\)/);
  assert.match(
    sourceInspectionSource,
    /dependencies\.operationAuthority\.createSession\([\s\S]*typeSourceSession\.start\(request, typeSourceAdapter\)[\s\S]*await result\.handle\.quiesced/);
  assert.match(appSource, /sourceInspection\.loadMemberSource\(\{/);
  assert.match(appSource, /sourceInspection\.loadTypeSource\(\{/);
  assert.match(appSource, /sourceInspection\.openGraphSource\(request, title\)/);
  assert.match(appSource, /sourceInspection\.closeGraphSource\(\)/);
  const reloadBody =
    appSource.match(/function reloadVisibleSource\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(reloadBody, /switch \(currentSourceReloadKind\(\)\)/);
  const autoLoadBody =
    appSource.match(
      /function maybeAutoLoadVisibleSource\(\)[\s\S]*?\n}\n\nfunction maybeAutoLoadTypeMetadata/)?.[0]
    ?? "";
  assert.match(
    autoLoadBody,
    /const kind = currentSourceOperationKind\(\)/);
  assert.match(autoLoadBody, /kind === "type"/);
  assert.match(autoLoadBody, /kind === "member"/);
  assert.match(autoLoadBody, /kind === "graph"/);
  assert.match(autoLoadBody, /loadSelectedTypeSource\(\)/);
  assert.match(autoLoadBody, /loadSelectedMemberSource\(\)/);
  assert.match(autoLoadBody, /openGraphSource\(/);
  const annotatedLoader =
    appSource.match(
      /async function loadSelectedMemberAnnotatedSource\(\)[\s\S]*?\n}\n\nfunction memberRequestSignature/)?.[0]
    ?? "";
  assert.match(
    annotatedLoader,
    /return memberDetailInspection\.loadAnnotated\(\{/);
  assert.doesNotMatch(
    annotatedLoader,
    /sourceRequestNeedsLoad|memberAnnotatedLoading/);
  assert.match(
    memberDetailInspectionSource,
    /async loadAnnotated\(request\)[\s\S]*sourceRequestNeedsLoad\([\s\S]*state\.memberAnnotatedLoading[\s\S]*state\.memberAnnotatedError/);

  // Annotating the fixture contextually types `lens` and `memberSection` against their
  // literal unions instead of widening them to `string`.
  const visible: SourceWorkbenchState = {
    settings: false,
    explorer: null,
    loading: false,
    error: "",
    home: false,
    package: {},
    atPackageRoot: false,
    graphSourceOpen: false,
    lens: "source",
    selectedMemberKey: "",
    memberSection: "overview"
  };
  assert.equal(sourceSurfaceIsVisible(visible), true);
  const hiddenOverrides: readonly SourceWorkbenchState[] = [
    { home: true },
    { atPackageRoot: true },
    { settings: true },
    { loading: true },
    { error: "failed" },
    { explorer: { open: true } },
    { package: null }
  ];
  for (const hidden of hiddenOverrides) {
    assert.equal(sourceSurfaceIsVisible({ ...visible, ...hidden }), false);
  }
  assert.equal(
    activeSourceOperationKind({
      ...visible,
      atPackageRoot: true,
      graphSourceOpen: true
    }),
    "graph");
  assert.equal(
    activeSourceOperationKind({
      ...visible,
      atPackageRoot: true
    }),
    null);
  assert.equal(
    sourceReloadKind({
      ...visible,
      lens: "api",
      selectedMemberKey: "M",
      memberSection: "annotated"
    }),
    "annotated");
  assert.equal(
    activeSourceOperationKind({
      ...visible,
      lens: "api",
      selectedMemberKey: "M",
      memberSection: "source"
    }, false),
    null);
  assert.equal(
    sourceReloadKind({
      ...visible,
      lens: "api",
      selectedMemberKey: "M",
      memberSection: "annotated"
    }, false),
    null);
  assert.equal(
    sourceReloadKind({
      ...visible,
      settings: true,
      lens: "api",
      selectedMemberKey: "M",
      memberSection: "annotated"
    }),
    null);
  assert.equal(
    sourceRequestNeedsLoad(true, false, null, ""),
    true);
  assert.equal(
    sourceRequestNeedsLoad(true, true, null, ""),
    false);
  assert.equal(
    sourceRequestNeedsLoad(true, false, { text: "source" }, ""),
    false);
  assert.equal(
    sourceRequestNeedsLoad(true, false, null, "failed"),
    false);
  assert.equal(
    sourceRequestNeedsLoad(false, true, { text: "stale" }, ""),
    true);

  const requestState = {
    sourceRequestGeneration: 4,
    memberSourceLoading: true,
    memberSourceKey: "member",
    memberSourceError: "",
    typeSourceLoading: false,
    typeSourceKey: "",
    typeSourceError: "",
    graphSourceLoading: false,
    graphSourceError: "",
    graphSourceSeq: 0
  };
  assert.equal(beginSourceRequestState(requestState), 5);
  assert.equal(requestState.memberSourceLoading, false);
  assert.equal(requestState.memberSourceKey, "");
  requestState.typeSourceLoading = true;
  requestState.typeSourceKey = "type";
  assert.equal(cancelSourceRequestState(requestState), true);
  assert.equal(requestState.sourceRequestGeneration, 6);
  assert.equal(requestState.typeSourceLoading, false);
  assert.equal(requestState.typeSourceKey, "");
  assert.equal(requestState.typeSourceError, "");
});

test("browser consumer explicitly sequences same-origin host configuration", () => {
  // The coordinator owns composition: every facade initializes, in order, before host policy
  // is configured and the one entry point runs.
  for (const module of generatedFacadeModules) {
    assert.match(
      engineCoordinatorSource,
      new RegExp(`import\\("/${module}\\.js"\\)`),
      `the coordinator does not compose /${module}.js`);
  }
  assert.match(
    engineCoordinatorSource,
    /const runtime = host\.createRuntime\(\);/,
    "the coordinator must create the shared runtime through the host facade");
  // Every facade receives the same runtime promise, one after another, before readiness
  // resolves.
  assert.deepEqual(
    [...engineCoordinatorSource.matchAll(
      /await (\w+)\.initializeRuntime\(runtime\);/g)]
      .map(match => match[1]),
    [
      "host",
      "packageFacade",
      "metadataFacade",
      "analysisFacade",
      "sourceFacade",
      "callGraphFacade",
      "catalogFacade",
    ]);
  assert.match(
    engineCoordinatorSource,
    /readiness \?\?= initializeFacadeSet\(\);/);
  assert.match(
    engineCoordinatorSource,
    /await initializeFacades\(\);[\s\S]*host\.configureHost\(origin\);[\s\S]*await host\.runEntryPoint\(\);/);
  assert.match(
    engineCoordinatorSource,
    /startup \?\?= startEngineCore\(origin\);/);
  // Only the host facade's entry point runs, and no other module's does.
  assert.deepEqual(
    [...engineCoordinatorSource.matchAll(/(\w+)\.runEntryPoint\(\)/g)]
      .map(match => match[1]),
    ["host"]);
  assert.match(
    appSource,
    /await startEngine\(window\.location\.origin\);/);
  assert.doesNotMatch(generatedFacadeSourceText, /\bwindow\b/);
});

test("every generated browser engine module is syntactically valid", () => {
  for (const module of generatedFacadeModules) {
    const modulePath = fileURLToPath(generatedFacadeModuleUrls.get(module)!);
    const result = spawnSync(
      process.execPath,
      ["--check", modulePath],
      { encoding: "utf8" });
    assert.equal(
      result.status,
      0,
      `${modulePath} failed syntax validation:\n${result.stderr}`);
  }
});

test("generated source wrappers parse their JSON envelopes", () => {
  const wrapper = (name: string) => {
    const pattern = new RegExp(`\\nexport (?:async )?function ${name}\\(`);
    const owners = generatedFacadeModules
      .filter(module => pattern.test(generatedFacadeSource(module)));
    assert.equal(owners.length, 1,
      `${name} must be published by exactly one facade; found ${owners.join(", ")}`);
    const source = generatedFacadeSource(owners[0]!);
    const start = source.search(pattern);
    const end = source.indexOf("\nexport ", start + 1);
    return source.slice(start, end < 0 ? undefined : end);
  };

  for (const name of [
    "queryMemberAnnotatedSource",
    "queryMemberFacts",
    "queryMemberSource",
    "queryTypeMemberSource",
  ]) {
    assert.match(
      wrapper(name),
      /const \$parsed = JSON\.parse\(\$result\);[\s\S]*return \$parsed;/);
  }
});

test("MethodDef-only member sections are hidden for bodiless APIs", () => {
  for (const kind of ["property", "field", "event", "constant"]) {
    assert.deepEqual(
      memberSectionIdsFor({ kind }),
      ["overview"]);
  }
  assert.deepEqual(
    memberSectionIdsFor({ kind: "method" }),
    ["overview", "call-graph", "facts", "source", "annotated"]);
});

// Arrowing between members keeps the active section (e.g. Source) sticky, the same way
// arrowing between types never disturbs the type-level lens. openMemberGroup/openOverload
// (the two entry points arrow-key nav uses) must clear cached per-member content without
// resetting memberSection, and only fall back to Overview when the newly selected member
// doesn't support the section that was showing.
test("moving between members keeps the active section sticky, falling back to Overview only when unsupported", () => {
  const openMemberGroupBody =
    appSource.match(/function openMemberGroup\(key: string\) \{[\s\S]*?\n}\n/)?.[0] ?? "";
  assert.match(openMemberGroupBody, /clearMemberContentCache\(\)/);
  assert.doesNotMatch(openMemberGroupBody, /resetMemberSectionState\(\)/);
  assert.match(
    openMemberGroupBody,
    /const preserveSection =\s*state\.memberBrowseTypeId === type\?\.id && Boolean\(state\.selectedMemberKey\)/);
  assert.match(
    openMemberGroupBody,
    /state\.selectedBodyTarget = graphOnlyTarget;[\s\S]*if \(!preserveSection\) \{\s*state\.memberSection = "overview"/);
  assert.match(
    openMemberGroupBody,
    /state\.memberSection !== "overview"[\s\S]*group\.overloads\.length > 1[\s\S]*state\.selectedOverloadIndex = 0;[\s\S]*retainMemberSectionIfSupported\(group\)/);
  assert.match(
    openMemberGroupBody,
    /const retainedSection = state\.memberSection;[\s\S]*let selectedFirstOverload = false;[\s\S]*selectedFirstOverload = true;[\s\S]*if \(selectedFirstOverload && state\.memberSection !== retainedSection\) \{\s*state\.selectedOverloadIndex = null;\s*state\.selectedBodyTarget = null/);
  assert.match(openMemberGroupBody, /loadMemberSectionContent\(state\.memberSection\)/);

  const openOverloadBody =
    appSource.match(/function openOverload\(index: number\) \{[\s\S]*?\n}\n/)?.[0] ?? "";
  assert.match(openOverloadBody, /clearMemberContentCache\(\)/);
  assert.doesNotMatch(openOverloadBody, /resetMemberSectionState\(\)/);
  assert.match(
    openOverloadBody,
    /state\.selectedBodyTarget = graphTarget;[\s\S]*retainMemberSectionIfSupported\(selectedMember\(selectedType\(\)\)\)/);
  assert.match(openOverloadBody, /loadMemberSectionContent\(state\.memberSection\)/);

  const retainBody =
    appSource.match(/function retainMemberSectionIfSupported\([\s\S]*?\n}\n/)?.[0] ?? "";
  assert.match(retainBody, /memberSectionsFor\(member\)/);
  assert.match(retainBody, /state\.memberSection = "overview"/);

  const resetBody =
    appSource.match(/function resetMemberSectionState\(\) \{[\s\S]*?\n}\n/)?.[0] ?? "";
  assert.match(resetBody, /state\.memberSection = "overview"/);
  assert.match(resetBody, /clearMemberContentCache\(\)/);

  const selectEntryBody =
    appSource.match(/function selectMemberNavEntry\([\s\S]*?\n}\n\nfunction stepMemberNav/)?.[0]
    ?? "";
  assert.match(
    selectEntryBody,
    /entry\.group\.key === state\.selectedMemberKey[\s\S]*entry\.group\.overloads\.length === 1[\s\S]*state\.selectedOverloadIndex = null;\s*clearMemberContentCache\(\);\s*render\(\)/);
});

test("every overload-specific member loader leaves a multi-overload picker inert", () => {
  for (const name of [
    "loadSelectedMemberDocumentation",
    "loadSelectedMemberSource",
    "loadSelectedMemberAnnotatedSource",
    "loadSelectedMemberCallGraph",
    "loadSelectedMemberFacts",
  ]) {
    const body =
      appSource.match(new RegExp(`async function ${name}\\(\\)[\\s\\S]*?\\n}`))?.[0]
      ?? "";
    assert.match(body, /selectedConcreteOverload\(member\.overloads, state\.selectedOverloadIndex\)/);
    assert.match(body, /if \(!overload\) \{\s*render\(\);\s*return;\s*}/);
    assert.doesNotMatch(body, /selectedOverloadIndex \?\? 0/);
  }
});

// `memberSectionIdsFor` is the admission set for the member strip, for the URL `?section=`
// token, and for the share packet's `c` token, so a section the catalog defines but this
// function omits is defined and never reachable. It used to restate the roster, which the
// compiler could not check in that direction. This is the gate for it deriving instead:
// restoring a hand-written list makes a catalog addition stop appearing here.
test("the full member-section roster is derived from the catalog, not restated", () => {
  assert.deepEqual(
    memberSectionIdsFor({ kind: "method" }),
    memberSectionDefinitions.map(([id]) => id));
});

test("source requests carry exact type and member identities", () => {
  const memberBridge =
    generatedFacadeSource("inspect-web-source")
      .match(/export async function queryMemberSource\([\s\S]*?\n}/)?.[0]
    ?? "";
  const memberLoader =
    appSource.match(/async function loadSelectedMemberSource\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    memberBridge,
    /typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson/);
  assert.match(
    memberLoader,
    /type\.definitionId \?\? type\.id,[\s\S]*?state\.selectedBodyTarget\?\.memberName[\s\S]*?state\.selectedBodyTarget\?\.selectorKey[\s\S]*?state\.selectedBodyTarget\?\.metadataToken/);
  assert.doesNotMatch(memberLoader, /signature:/);
});

test("call graph source identity prefers the structured type definition", () => {
  assert.equal(
    callGraphTargetTypeId({
      typeDefinitionId: "Example.Outer\\+Literal",
      typeMetadataId: ""
    }),
    "Example.Outer\\+Literal");
  assert.equal(
    callGraphTargetTypeId({ typeMetadataId: "Example.Legacy" }),
    "Example.Legacy");

  const nested = {
    id: "Example.Outer+Inner",
    definitionId: "Example.Outer+Inner",
    metadataId: "Example.Outer+Inner",
    assembly: "Example"
  };
  const literal = {
    id: "Example.Outer\\+Inner",
    definitionId: "Example.Outer\\+Inner",
    metadataId: "Example.Outer+Inner",
    assembly: "Example"
  };
  const target = {
    assembly: "Example",
    typeDefinitionId: literal.definitionId
  };
  const candidate = resolveLoadedGraphTargetCandidate(
    [{ id: "Example", types: [nested, literal] }],
    target);
  assert.equal(candidate.status, "unique");
  assert.equal(candidate.type, literal);
  assert.equal(callGraphTargetMatchesType(target, nested), false);
  assert.equal(callGraphTargetMatchesType(target, literal), true);
});

test("decompiled source discloses the PDB-source limitation", () => {
  const html = pdbSourceLimitationHtml({
    pdbSourceLimitation: "<img src=x onerror=alert(1)>"
  });
  assert.match(html, /PDB source unavailable:/);
  assert.doesNotMatch(html, /<img/);
  assert.match(html, /&lt;img/);
  assert.match(
    typePanelSource,
    /renderSourceResult[\s\S]*pdbSourceLimitationHtml\(source\)/);
  assert.match(
    appSource,
    /state\.memberSource\s*\?\s*renderSourceResult\(\{/);
});

test("history never applies a selection to another coordinate", () => {
  const oldPackage = packageAt("1.0.0", "net8.0");
  const newVersion = packageAt("2.0.0", "net8.0");
  const view = {
    package: oldPackage.id,
    packageKey: packageIdentityKey(oldPackage)
  };

  assert.equal(packageForView([newVersion], view), null);
  assert.equal(packageForView([oldPackage, newVersion], view), oldPackage);
  assert.equal(packageCoordinateMatchesLocation(oldPackage, {
    package: oldPackage.id,
    version: oldPackage.version,
    framework: oldPackage.activeFramework
  }), true);
  assert.equal(packageCoordinateMatchesLocation(oldPackage, {
    package: oldPackage.id,
    version: oldPackage.version,
    framework: "net9.0"
  }), false);
  assert.equal(packageCoordinateMatchesLocation(oldPackage, {
    package: oldPackage.id,
    version: oldPackage.version
  }), false);
});

test("history restores the complete saved workspace coordinate set", () => {
  const first = packageAt("1.0.0", "net8.0");
  const second = packageAt("2.0.0", "net9.0");
  const tabs = [
    { id: first.id, version: first.version, framework: first.activeFramework },
    { id: second.id, version: second.version, framework: second.activeFramework }
  ];

  assert.equal(workspaceCoordinatesMatch([first, second], tabs), true);
  assert.equal(workspaceCoordinatesMatch([first], tabs), false);
  assert.equal(workspaceCoordinatesMatch([second, first], tabs), false);
});

// A real call-graph node carries more than the identity view in `data.ts` declares:
// `typeFullName` is part of the call-graph facade's own DTO in
// `facades/inspect-web-call-graph.d.ts`. Passing the wider payload is exactly what the
// product does, so the fixtures below keep the field --
// it is what makes "prefers metadata identity over the display name" a real claim -- and
// this widening keeps the excess property check, which only fires on fresh object
// literals, from rejecting it.
const engineCallGraphTarget = (
  fixture: CallGraphTarget & { typeFullName?: string },
): CallGraphTarget => fixture;

test("call graph navigation prefers exact metadata type identity", () => {
  assert.equal(
    callGraphTargetTypeId(engineCallGraphTarget({
      typeFullName: "Example.Outer.Inner",
      typeMetadataId: "Example.Outer`1+Inner`1"
    })),
    "Example.Outer`1+Inner`1");
  assert.equal(
    callGraphTargetTypeId(engineCallGraphTarget({ typeFullName: "Example.Legacy" })),
    "");
});

test("call graph navigation resolves accessor selectors across image-local token skew", () => {
  const group = {
    overloads: [{
      graphSelectorKey: "property-selector",
      bodySelectors: [{
        token: 123,
        memberName: "get_P",
        selectorKey: "getter-selector"
      }]
    }]
  };

  assert.deepEqual(
    graphMemberSelection([group], {
      metadataToken: 456,
      memberName: "get_P",
      selectorKey: "getter-selector"
    }),
    { groupIndex: 0, overloadIndex: 0 });
  assert.deepEqual(
    graphMemberSelection([group, group], {
      metadataToken: 456,
      memberName: "get_P",
      selectorKey: "getter-selector"
    }),
    { groupIndex: 0, overloadIndex: 0 });
  assert.equal(
    graphMemberSelection([
      group,
      {
        overloads: [{
          graphSelectorKey: "getter-selector",
          bodySelectors: [{
            token: 124,
            memberName: "get_P",
            selectorKey: "getter-selector"
          }]
        }]
      }
    ], {
      metadataToken: 456,
      memberName: "get_P",
      selectorKey: "getter-selector"
    }),
    null);
  assert.equal(
    graphMemberSelection([
      {
        overloads: [{
          bodySelectors: [{
            memberName: "get_P",
            selectorKey: "getter-selector"
          }]
        }]
      },
      {
        overloads: [{
          bodySelectors: [{
            memberName: "get_P",
            selectorKey: "getter-selector"
          }]
        }]
      }
    ], {
      metadataToken: 456,
      memberName: "get_P",
      selectorKey: "getter-selector"
    }),
    null);

  const runtimeResolver =
    appSource.match(/function findRuntimeMemberSelection[\s\S]*?\n\}/)?.[0]
    ?? "";
  assert.match(runtimeResolver, /graphMemberSelection\(groups, node\)/);
  assert.doesNotMatch(runtimeResolver, /node\.metadataToken != null/);
  assert.match(runtimeResolver, /resolveRuntimeGraphTargetCandidate\(pack, node\)/);
});

test("graph-only member targets round-trip through shared URLs", () => {
  const target = {
    assembly: "Example",
    assemblyVersion: "1.2.3.4",
    assemblyCulture: null,
    assemblyPublicKeyToken: "0011223344556677",
    typeDefinitionId: "Example.Widget",
    typeMetadataId: "Example.Widget",
    memberName: "Run",
    selectorKey: "opaque-selector",
    metadataToken: 0x06000001
  };
  const encoded = graphMemberShareTarget(target);
  assert.ok(encoded, "the fixture target must encode to a share tuple");

  assert.deepEqual(graphMemberTargetFromShare(encoded), target);
  assert.equal(graphMemberShareTarget({
    ...target,
    typeDefinitionId: ""
  }), null);
  for (const metadataToken of [
    -1,
    0,
    0x02000001,
    0x06000000,
    0x07000000,
    0x106000001,
  ]) {
    assert.equal(
      graphMemberShareTarget({ ...target, metadataToken }),
      null);
    assert.equal(
      graphMemberTargetFromShare([
        ...encoded.slice(0, 8),
        metadataToken,
      ]),
      null);
  }
  assert.equal(graphMemberTargetFromShare([
    "Example",
    "1.2.3.4",
    null,
    "0011223344556677",
    "Example.Widget",
    "Example.Widget",
    "Run",
    "opaque-selector",
    "not-a-token"
  ]), null);
  assert.deepEqual(
    graphMemberTargetFromPacket({
      y: "Example.Widget",
      m: "method:Run",
      o: 0,
      g: encoded
    }),
    { target });
  assert.match(
    graphMemberTargetFromPacket({
      y: "Example.Widget",
      m: "method:Run",
      o: 0,
      g: [...encoded.slice(0, 8), "not-a-token"]
    }).error ?? "",
    /shared graph member target is invalid/);
  assert.match(
    graphMemberTargetFromPacket({
      y: "Example.Widget",
      m: "method:Run",
      g: encoded
    }).error ?? "",
    /shared graph member target is invalid/);
});

test("shared graph targets require explicit assembly version provenance", () => {
  const target = {
    assembly: "Example",
    typeDefinitionId: "Example.Widget",
    typeMetadataId: "Example.Widget",
    memberName: "Run",
    selectorKey: "opaque-selector",
    metadataToken: 0x06000001
  };
  assert.equal(graphMemberShareTarget(target), null);
  assert.equal(graphMemberTargetFromShare([
    target.assembly,
    "",
    null,
    null,
    target.typeDefinitionId,
    target.typeMetadataId,
    target.memberName,
    target.selectorKey,
    target.metadataToken
  ]), null);

  const explicitUnknown = {
    ...target,
    assemblyVersion: null
  };
  const explicitUnknownRoundTrip = graphMemberTargetFromShare(
    graphMemberShareTarget(explicitUnknown));
  assert.ok(explicitUnknownRoundTrip, "an explicit unknown version must round-trip");
  assert.equal(explicitUnknownRoundTrip.assemblyVersion, null);
});

test("graph-only members open through the typed member surface", () => {
  const binding =
    appSource.match(/function callGraphNodeBinding\([\s\S]*?\n}(?=\n\nfunction blockedCallGraphNodeBinding)/)?.[0]
    ?? "";
  const openMember =
    appSource.match(/function openMemberGroup\([\s\S]*?\n}(?=\n\nfunction enterMemberScope)/)?.[0]
    ?? "";
  assert.match(
    binding,
    /navigateToGraphMember\([\s\S]*loaded,[\s\S]*target,[\s\S]*loadedSection,[\s\S]*failureSurface\)/);
  assert.doesNotMatch(binding, /openGraphSource\(/);
  assert.match(
    openMember,
    /const graphOnlyTarget =[\s\S]*clearMemberContentCache\(\);[\s\S]*state\.selectedBodyTarget = graphOnlyTarget;[\s\S]*retainMemberSectionIfSupported\(group\)/);
  assert.match(
    generatedFacadeSource("inspect-web-metadata"),
    /\$requireManagedExports\(\)\["MetadataExports"\]\["QueryGraphMemberSurface\.-?\d+"\]/);
  assert.match(
    generatedFacadeSource("inspect-web-metadata"),
    /export async function queryGraphMemberSurface\(packageId, version, targetFramework/);
  assert.match(appSource, /solid border: no platform lookup/);
  assert.match(
    appSource,
    /dashed border: external assembly \(platform lookup on click\)/);
});

test("graph-only deep links win over colliding public member groups", () => {
  const selectedType = { id: "Example.Widget" };
  const publicGroup = { key: "method:Run", overloads: [{ name: "Run" }] };
  const deepLinkGraphTarget = (
    selectorKey: string,
  ): GraphMemberShareIdentity => ({
    assembly: "Example.dll",
    typeDefinitionId: "Example.Widget",
    memberName: "Run",
    selectorKey,
    metadataToken: 0x06000001
  });
  assert.equal(
    graphMemberDeepLinkDisposition(
      {
        member: publicGroup.key,
        graphTarget: deepLinkGraphTarget("private-overload")
      },
      { status: "unique", type: selectedType },
      selectedType,
      publicGroup),
    "graph");
  assert.equal(
    graphMemberDeepLinkDisposition(
      {
        member: publicGroup.key,
        overload: "99",
        graphTarget: deepLinkGraphTarget("public-overload")
      },
      { status: "unique", type: selectedType },
      selectedType,
      publicGroup,
      { group: publicGroup, overloadIndex: 0 }),
    "local");

  const deepLink =
    appSource.match(/function applyDeepLink\([^)]*\) \{[\s\S]*?\n\}/)?.[0]
    ?? "";
  assert.match(
    deepLink,
    /graphMemberDeepLinkDisposition\(\s*deep,\s*graphCandidate,\s*type,\s*group,\s*localGraphSelection\)/);
  assert.match(deepLink, /else if \(disposition === "graph"/);
  assert.match(deepLink, /else if \(disposition === "public" && group && deep\.member\)/);
  assert.match(
    deepLink,
    /The shared graph member no longer matches this package and was not opened/);
});

test("pending graph-member restoration is bound to its exact view", () => {
  const pending = {
    packageKey: "Example\u00001.0.0\u0000net10.0",
    viewSignature: "{\"t\":\"Example.Widget\"}"
  };
  assert.equal(
    graphMemberPendingMatchesView(
      pending,
      pending.packageKey,
      pending.viewSignature),
    true);
  assert.equal(
    graphMemberPendingMatchesView(
      pending,
      pending.packageKey,
      "{\"t\":\"Example.Other\"}"),
    false);
  assert.match(
    appSource,
    /if \(state\.pendingGraphMemberDeepLink\s*&& !graphMemberPendingMatchesView\(/);
  assert.match(
    appSource,
    /The shared graph member's declaring type is no longer available/);
  assert.match(
    appSource,
    /else if \(disposition === "graph"[\s\S]*?state\.selectedMemberKey = deep\.member;[\s\S]*?state\.selectedBodyTarget = deep\.graphTarget;[\s\S]*?viewSignature: viewSignature\(\)/);
  assert.match(
    appSource,
    /function currentPendingGraphMember\(\) \{[\s\S]*?graphMemberPendingMatchesView\([\s\S]*?viewSignature\(\)/);
  assert.match(
    appSource,
    /function renderApiLens\([^)]*\) \{\s*const pending = currentPendingGraphMember\(\);[\s\S]*?return renderGraphMemberPendingHtml\(item, title\)/);
  assert.match(
    appSource,
    /async function restorePendingGraphMember\([\s\S]*type\.id !== pending\.type[\s\S]*declaring type is no longer available[\s\S]*loadGraphMemberSurface/);
});

test("stale graph-only navigation clears progress without surfacing its error", () => {
  const navigation =
    appSource.match(/async function navigateToGraphMemberProjection[\s\S]*?\n\}/)?.[0]
    ?? "";

  assert.match(navigation, /const navigationIsCurrent = \(\) =>/);
  assert.match(
    navigation,
    /const owner = captureViewOperation\(seq\);[\s\S]*?ownsViewOperation\(owner, state\.graphMemberNavigationSeq\)/);
  assert.equal(
    navigation.match(/if \(!navigationIsCurrent\(\)\)/g)?.length,
    2);
  assert.match(
    navigation,
    /if \(seq === state\.graphMemberNavigationSeq\) \{\s*state\.graphMemberNavigationTitle = "";\s*render\(\);/);
  assert.match(
    navigation,
    /showGraphMemberNavigationError\([\s\S]*errorMessage\(error\),[\s\S]*failureSurface\)/);
  assert.match(
    appSource,
    /const callGraphError = callGraphErrorForView\(state\);/);
  assert.match(
    appSource,
    /function popPlatformDrill\(\) \{\s*invalidateGraphMemberNavigation\(\);/);
});

test("shared package graph navigation retains portable accessor identity", () => {
  const shareState =
    appSource.match(/function captureWorkspaceUrlState\(\)[\s\S]*?\n}(?=\n\nfunction buildStateUrl)/)?.[0]
    ?? "";

  assert.match(
    shareState,
    /memberAnchor = overload\.anchorDigest \|\| null/);
  assert.match(
    shareState,
    /memberSignature = memberAnchor \? null : overload\.canonicalSignature \|\| null/);
  assert.doesNotMatch(shareState, /selectedBodyTarget:/);
  assert.match(appSource, /solid border: no platform lookup/);
});

test("stale graph member loads cannot mutate the visible member surface", () => {
  const navigation =
    appSource.match(/async function navigateToGraphMemberProjection[\s\S]*?\n\}/)?.[0]
    ?? "";
  const restoration =
    appSource.match(/async function restorePendingGraphMember[\s\S]*?\n\}/)?.[0]
    ?? "";

  assert.ok(
    navigation.indexOf("if (!navigationIsCurrent())")
      < navigation.indexOf("commitGraphMemberSelection("));
  assert.ok(
    restoration.indexOf("if (!restorationIsCurrent())")
      < restoration.indexOf("commitGraphMemberSelection("));
});

test("platform graph navigation supersedes package member loading immediately", () => {
  const navigation =
    appSource.match(/async function navigateOrDrillPlatform[\s\S]*?\n\}/)?.[0]
    ?? "";

  assert.match(
    navigation,
    /invalidateGraphMemberNavigation\(\);\s*const seq = \+\+state\.memberCallGraphSeq;/);
  assert.match(
    navigation,
    /state\.platformDrillLoading = false;\s*state\.platformDrillError = "";/);
  assert.match(navigation, /state\.memberCallGraphExpanding = false;/);
});

test("projected members remain distinct from the public API surface", () => {
  const publicMember = {
    name: "M",
    graphOnly: false
  };
  const projectedMember = {
    name: "M",
    graphOnly: true
  };
  const { publicMembers, graphMembers } =
    partitionGraphMembers([publicMember, projectedMember]);
  const publicGroup = {
    key: "method:M",
    overloads: [publicMember]
  };
  const projectedGroup = {
    key: "graph:method:M",
    overloads: [projectedMember]
  };

  assert.deepEqual(publicMembers, [publicMember]);
  assert.deepEqual(graphMembers, [projectedMember]);
  assert.deepEqual(
    searchableMemberGroups([publicGroup, projectedGroup]),
    [publicGroup]);
  assert.match(
    appSource,
    /Graph-discovered implementation members/);
  assert.match(
    appSource,
    /partitionGraphMembers\(item\.api\)/);
  assert.match(
    appSource,
    /\$\{member\.graphOnly \? "graph:" : ""\}\$\{member\.kind\}:\$\{member\.name\}/);
  assert.match(
    appSource,
    /memberSectionIdsFor\(\s*member,\s*state\.package\?\.isRuntimePack,\s*memberHasSelectedBody\(member\)\)/);
  assert.match(
    appSource,
    /searchableMemberGroups\(memberGroups\(type\)\)/);
});

test("shared graph projection validates before committing API state", () => {
  const restoration =
    appSource.match(/async function restorePendingGraphMember[\s\S]*?\n\}/)?.[0]
    ?? "";
  const validation = restoration.indexOf("staged.selection.group.key !== pending.member");
  const commit = restoration.indexOf("commitGraphMemberSelection(");

  assert.ok(validation >= 0);
  assert.ok(commit > validation);
  assert.doesNotMatch(restoration, /staged\.selection\.overloadIndex !== pendingOverloadIndex/);
  assert.match(
    appSource,
    /group\?\.overloads\.length === 1\s*\? graphOnlyBodyTarget\(group\.overloads\[0\]\)/);
  assert.doesNotMatch(
    appSource,
    /group\.overloads\[overloadIndex\]\.graphTarget = bodyTarget/);
});

test("selector-only accessors use body-aware implementation queries", () => {
  const annotatedLoader =
    appSource.match(
      /async function loadSelectedMemberAnnotatedSource\(\)[\s\S]*?\n}/)?.[0]
    ?? "";
  // The absent rejection is claimed across the managed export assemblies that could carry
  // it, not just the host, now that call-graph and source operations have their own owners.
  for (const managedSource of [
    "../engine/InspectionEngine.cs",
    "../engine.CallGraphExports/CallGraphExports.cs",
    "../engine.SourceExports/SourceExports.cs",
    "../engine.SourceExports/AnnotatedSourceExports.cs",
  ]) {
    assert.doesNotMatch(
      readFileSync(new URL(managedSource, import.meta.url), "utf8"),
      /A call graph needs the selected overload's method-body token/);
  }
  assert.match(
    annotatedLoader,
    /member: state\.selectedBodyTarget\?\.memberName \?\? overload\.name/);
  assert.deepEqual(
    memberSectionIdsFor({ kind: "event" }, false, true),
    ["overview", "call-graph", "facts", "annotated"]);
});

test("platform graph borders reflect actual resident lookup", () => {
  const binding =
    appSource.match(/function callGraphNodeBinding\([\s\S]*?\n}(?=\n\nfunction blockedCallGraphNodeBinding)/)?.[0]
    ?? "";
  const packageBinding = binding.slice(binding.indexOf("const packages ="));

  assert.match(binding, /resolveRuntimeGraphTargetCandidate\(pack, target\)/);
  assert.match(binding, /platform: disposition === "lookup"/);
  assert.match(binding, /if \(disposition === "member" && pack && resident\) \{\s*navigateToRuntimeMember\(/);
  assert.match(binding, /else \{[\s\S]*startPlatformDrill\(target\)/);
  assert.match(
    packageBinding,
    /const runtimeCandidate = \(candidate\.status === "missing"[\s\S]*?\|\| candidate\.status === "skew"\) && pack\s*\? resolveRuntimeGraphTargetCandidate\(pack, target\)/);
  assert.match(
    packageBinding,
    /const disposition = combinedGraphTargetNavigationDisposition\(\s*candidate,\s*runtimeCandidate,\s*target,\s*runtimeResident\);[\s\S]*?if \(disposition === "blocked"/);
  assert.match(
    packageBinding,
    /else if \(disposition === "resident"\) \{\s*if \(pack && resident\) \{[\s\S]*?navigateToRuntimeMember\([\s\S]*?\} else \{[\s\S]*?startPlatformDrill\(target\)/);
  assert.match(
    appSource,
    /if \(candidate\.status === "resident"\s*\|\| \(candidate\.status === "missing"\s*&& assemblyResident\)\) \{[\s\S]*?await drillPlatformNode\(/);
});

test("runtime graph nodes separate member, drill, and lookup disposition", () => {
  const normalTarget = {
    kind: "normal",
    assembly: "System.Private.CoreLib",
    typeDefinitionId: "System.RuntimeType",
    memberName: "PrivateHelper",
    selectorKey: "private-helper"
  };
  const externalTarget = { ...normalTarget, kind: "external" };

  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "unique" },
      normalTarget,
      true),
    "member");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "unique" },
      normalTarget,
      false),
    "drill");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "missing" },
      normalTarget,
      false),
    "drill");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "unique" },
      externalTarget,
      false),
    "drill");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "missing" },
      externalTarget,
      false),
    "lookup");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "ambiguous" },
      externalTarget,
      false),
    "blocked");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "missing" },
      externalTarget,
      false,
      true),
    "drill");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "resident" },
      externalTarget,
      false),
    "drill");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      { status: "missing" },
      { ...externalTarget, assemblyVersion: null },
      false),
    "none");

  const runtimeNavigation =
    appSource.match(/function navigateToRuntimeMember[\s\S]*?\n\}/)?.[0]
    ?? "";
  assert.match(
    runtimeNavigation,
    /state\.libraryScope = targetLibrary \? new Set\(\[targetLibrary\]\) : null/);
});

test("runtime graph identities restore through exact resident candidates", () => {
  const type = {
    id: "System.Console",
    definitionId: "System.Console",
    metadataId: "System.Console",
    assembly: "System.Console.dll",
    assemblyId: "runtime:System.Console"
  };
  const pack = {
    id: "Microsoft.NETCore.App",
    isRuntimePack: true,
    types: [type],
    assemblies: [{
      id: "runtime:System.Console",
      name: "System.Console",
      version: "10.0.0.0",
      culture: null,
      publicKeyToken: "b03f5f7f11d50a3a"
    }]
  };
  const target = {
    kind: "external",
    assembly: "System.Console",
    assemblyVersion: "10.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: "b03f5f7f11d50a3a",
    typeDefinitionId: "System.Console",
    memberName: "get_Out",
    selectorKey: "getter-selector",
    metadataToken: null
  };

  assert.deepEqual(resolveRuntimeGraphTargetCandidate(pack, target), {
    status: "unique",
    pkg: pack,
    type
  });
  assert.equal(runtimeGraphTargetAssemblyIsResident(pack, target), true);
  assert.equal(
    runtimeGraphTargetAssemblyIsResident({ ...pack, types: [] }, target),
    true);
  assert.equal(
    runtimeGraphTargetAssemblyIsResident(
      pack,
      { ...target, assemblyVersion: "10.0.0.1" }),
    false);
  assert.equal(
    graphTargetNavigationDisposition({ status: "missing" }, target, true),
    "resident");
  assert.doesNotMatch(
    appSource,
    /if \(!state\.package\?\.isRuntimePack\s*&& packet\.y/);
  assert.match(
    appSource,
    /resolveRuntimeGraphTargetCandidate\(\s*pkg,\s*deep\.graphTarget\)/);
});

test("home navigation invalidates pending graph work", () => {
  const home =
    appSource.match(/function goHome\(\) \{[\s\S]*?\n\}/)?.[0]
    ?? "";
  const history =
    appSource.match(/window\.addEventListener\("popstate"[\s\S]*?\n\}\);/)?.[0]
    ?? "";

  assert.match(home, /invalidateGraphMemberNavigation\(\)/);
  assert.match(home, /state\.memberCallGraphExpanding = false/);
  assert.match(history, /invalidateMemberDestinationWork\(state\)/);
});

test("graph navigation restores scope and supersedes local drills", () => {
  const capture =
    appSource.match(/function captureView[\s\S]*?(?=\nfunction recordNav)/)?.[0]
    ?? "";
  const apply =
    appSource.match(/function applyView[\s\S]*?(?=\nfunction navBack)/)?.[0]
    ?? "";
  const startDrill =
    appSource.match(/async function startPlatformDrill[\s\S]*?\n\}/)?.[0]
    ?? "";
  const navigation =
    appSource.match(/function navigateToMember[\s\S]*?(?=\nasync function loadSelectedMemberFacts)/)?.[0]
    ?? "";

  assert.match(capture, /libraryScope: captureLibraryScope\(state\.libraryScope\)/);
  assert.match(
    apply,
    /state\.libraryScope = restoreLibraryScope\(\s*view\.libraryScope,\s*pkg\.types\.map\(type => libraryKey\(type\)\)\)/);
  assert.match(
    callGraphInspectionSource,
    /state\.memberCallGraphSeq\+\+;\s*state\.memberCallGraphExpanding = false;\s*state\.platformDrillLoading = false;/);
  assert.match(
    startDrill,
    /invalidateGraphMemberNavigation\(\);\s*const owner = captureViewOperation\(\+\+state\.memberCallGraphSeq\);[\s\S]*?const navigationIsCurrent = \(\) =>\s*ownsViewOperation\(owner, state\.memberCallGraphSeq\);[\s\S]*?await drillPlatformNode\(node, navigationIsCurrent\)/);
  assert.match(
    appSource,
    /else if \(disposition === "resident"\)[\s\S]*?startPlatformDrill\(target\)/);
  assert.match(
    navigation,
    /state\.typeFilter = "";\s*state\.namespaceFilter = "";\s*state\.kindFilter = "";\s*state\.libraryScope = null;/);
  assert.match(
    navigation,
    /state\.accessibilityFilter = accessibilityFilterIncludingType\(\s*state\.accessibilityFilter,\s*type\)/);
});

test("restored selections reveal their accessibility bucket", () => {
  const original = new Set(["public"]);
  const revealed = accessibilityFilterIncludingType(
    original,
    { accessibilityId: "private" });
  assert.deepEqual([...original], ["public"]);
  assert.deepEqual([...revealed], ["public", "private"]);

  const apply =
    appSource.match(/function applyView[\s\S]*?(?=\nfunction navBack)/)?.[0]
    ?? "";
  const deepLink =
    appSource.match(/function applyDeepLink\([^)]*\) \{[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  const reveal =
    appSource.match(/function revealTypeInFilters[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  assert.match(
    apply,
    /const type = pkg\.types\.find[\s\S]*?if \(!state\.atPackageRoot\) revealTypeInFilters\(type\)/);
  assert.match(
    deepLink,
    /const type = pkg\.types\.find[\s\S]*?revealTypeInFilters\(type\)[\s\S]*?state\.typeCursor = Math\.max/);
  assert.match(
    reveal,
    /typeMatchesFilterText[\s\S]*?state\.typeFilter = ""[\s\S]*?state\.namespaceFilter = ""[\s\S]*?state\.kindFilter = ""[\s\S]*?state\.libraryScope = null/);
  assert.match(
    appSource,
    /function navigateToType\([^)]*\) \{[\s\S]*?revealTypeInFilters\(target\)[\s\S]*?state\.typeCursor = filteredTypes\(\)\.findIndex/);
});

test("runtime lookup refuses ambiguous or unresolved exact targets", () => {
  const navigation =
    appSource.match(/async function navigateOrDrillPlatform[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  assert.match(
    navigation,
    /candidate = resolveRuntimeGraphTargetCandidate\(pack, node\)[\s\S]*?candidate\.status === "ambiguous"[\s\S]*?showPlatformTargetError/);
  assert.match(
    navigation,
    /candidate\.status !== "unique"[\s\S]*?loaded platform assembly does not contain the exact target identity/);
  assert.match(
    navigation,
    /assemblyResident = runtimeGraphTargetAssemblyIsResident\(pack, node\)[\s\S]*?candidate\.status === "missing" && assemblyResident[\s\S]*?drillPlatformNode\(node, navigationIsCurrent\)/);
  assert.match(
    navigation,
    /const owner = captureViewOperation\(seq\);\s*const navigationIsCurrent = \(\) =>\s*ownsViewOperation\(owner, state\.memberCallGraphSeq\)/);
  assert.match(
    navigation,
    /const discardIfStale = \(\s*preservedFocus: MemberFocusSnapshot \| null = null,\s*\) => \{[\s\S]*?seq === state\.memberCallGraphSeq[\s\S]*?state\.platformDrillLoading = false;[\s\S]*?if \(preservedFocus\) renderPreservingMemberFocus\(preservedFocus\);\s*else render\(\)/);
  assert.match(
    appSource,
    /async function drillPlatformNode\(\s*node: InspectedCallGraphTarget,\s*navigationIsCurrent: \(\) => boolean = \(\) => true,\s*\)[\s\S]*?isCurrent: navigationIsCurrent/);
  assert.match(
    callGraphInspectionSource,
    /async drill\(request\)[\s\S]*?const ownsRequest = \(\) =>\s*sequence === state\.memberCallGraphSeq && request\.isCurrent\(\)[\s\S]*?const abandonStaleRequest = \(\) => \{[\s\S]*?if \(sequence !== state\.memberCallGraphSeq\) return;[\s\S]*?state\.platformDrillLoading = false;[\s\S]*?dependencies\.renderPreservingMemberFocus\(\);[\s\S]*?if \(!ownsRequest\(\)\) \{\s*abandonStaleRequest\(\);\s*return;/);
  assert.match(
    appSource,
    /if \(disposition === "blocked"\) \{[\s\S]*?blockedCallGraphNodeBinding\([\s\S]*?if \(disposition === "none"\) return null/);
});

test("history rebuilds graph-only members through exact pending identity", () => {
  const apply =
    appSource.match(/function applyView[\s\S]*?(?=\nfunction navBack)/)?.[0]
    ?? "";
  const restore =
    appSource.match(/async function restorePendingGraphMember[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  assert.match(
    apply,
    /graphSelection\?\.group\.key !== view\.selectedMemberKey[\s\S]*?state\.pendingGraphMemberDeepLink = \{[\s\S]*?packageKey: packageIdentityKey\(pkg\)[\s\S]*?member: view\.selectedMemberKey[\s\S]*?target: historyGraphTarget[\s\S]*?restorePendingGraphMember\(\)/);
  assert.match(
    restore,
    /state\.graphMemberNavigationTitle =[\s\S]*?render\(\);[\s\S]*?loadGraphMemberSurface/);
  assert.match(
    restore,
    /const owner = captureViewOperation\(seq\);[\s\S]*?ownsViewOperation\(owner, state\.graphMemberNavigationSeq\)/);
  assert.equal(
    restore.match(/normalizeCurrentNavEntry\(\);/g)?.length,
    2);
  assert.match(
    apply,
    /const requestedOverloadIndex = view\.selectedOverloadIndex;[\s\S]*?overload: requestedOverloadIndex/);
  assert.match(
    apply,
    /const hasSelectedBody =\s*graphSelection\?\.group\.key === view\.selectedMemberKey;[\s\S]*?memberSectionIdsFor\(member, pkg\.isRuntimePack, hasSelectedBody\)/);
  assert.match(
    apply,
    /state\.selectedBodyTarget = retainGraphOnlyImplementationBody\(\s*graphSelection\.group\.overloads\[graphSelection\.overloadIndex\],\s*view\.bodyTarget\)/);
  assert.match(
    apply,
    /memberSectionIdsFor\(\s*graphSelection\.group,\s*pkg\.isRuntimePack,\s*true\)\.includes\(view\.memberSection\)/);
  assert.match(
    appSource,
    /const hasSelectedBody = bodyTargetMatchesOverload\([\s\S]*?memberSectionIdsFor\(\s*group,\s*state\.package\?\.isRuntimePack,\s*hasSelectedBody\)/);
  assert.match(
    appSource,
    /function renderMember\(type: AppTypeSurface, member: AppMemberGroup\) \{[\s\S]*?const selectedOverloadIndex = state\.selectedOverloadIndex;[\s\S]*?const hasSelectedOverload =[\s\S]*?selectedOverloadIndex < member\.overloads\.length[\s\S]*?const overloadIndex = hasSelectedOverload \? selectedOverloadIndex \?\? 0 : 0;/);
});

test("member navigation excludes graph-only projections from ordinary filters", () => {
  const filters =
    appSource.match(/function visibleMemberGroups\([\s\S]*?\n}\n\nfunction renderMemberFilterControls\([\s\S]*?\n}/)?.[0]
    ?? "";
  assert.match(
    filters,
    /filterMemberGroups\(publicMemberGroups\(type\), memberFilterState\(\)\)/);
  assert.match(
    filters,
    /function publicMemberGroups\([\s\S]*?searchableMemberGroups\(memberGroups\(type\)\)/);
  assert.match(
    filters,
    /publicMemberGroups\(type\)\s*\.flatMap\(group => group\.overloads\)/);

  const entries =
    appSource.match(/function memberNavEntries\([\s\S]*?\n}\n\nfunction memberNavCursor/)?.[0]
    ?? "";
  assert.match(
    entries,
    /for \(const group of visibleMemberGroups\(type\)\)[\s\S]*?const graphGroup = selectedGraphMemberGroup\(type\);[\s\S]*?entries\.push\(\{ kind: "member", group: graphGroup }\)/);

  const pane =
    appSource.match(/function renderMemberNavPane\([\s\S]*?\n}\n\n\/\/ The scope switcher/)?.[0]
    ?? "";
  assert.match(pane, /memberCount: publicMemberGroups\(type\)\.length/);
});

test("type API reports the filtered member count once in its header", () => {
  const renderApi =
    appSource.match(/function renderApiLens\([\s\S]*?\n}\n\nfunction renderMember/)?.[0]
    ?? "";
  assert.match(
    renderApi,
    /<h1 id="api-surface-title">Members<\/h1>/);
  assert.match(
    renderApi,
    /<p>\$\{visibleGroups\.length} of \$\{publicGroups\.length} member groups/);
  assert.doesNotMatch(renderApi, /member-filter-result/);
  assert.doesNotMatch(renderApi, /member groups visible/);
});

test("member API uses full-area overload and selected-member surfaces", () => {
  const renderApi =
    appSource.match(/function renderApiLens\([\s\S]*?\n}\n\nfunction renderMember/)?.[0]
    ?? "";
  const renderMember =
    appSource.match(/function renderMember\([\s\S]*?\n}\n\n\/\/ The annotated section/)?.[0]
    ?? "";
  const memberOverview =
    renderMember.match(/if \(state\.memberSection === "overview"\) \{[\s\S]*?\n  \} else if \(state\.memberSection === "call-graph"\)/)?.[0]
    ?? "";
  const emptyMember =
    renderApi.match(/if \(state\.memberBrowseTypeId === item\.id\) \{[\s\S]*?\n  \}/)?.[0]
    ?? "";
  assert.match(
    emptyMember,
    /class="member-surface member-empty-surface"[\s\S]*?<h1 id="member-surface-title">Members<\/h1>[\s\S]*?No member selected/);
  assert.doesNotMatch(emptyMember, /typeHeadingHtml/);
  assert.match(
    renderMember,
    /class="member-surface member-overload-surface"[\s\S]*?<h1 id="member-surface-title">\$\{escapeHtml\(member\.name\)}<\/h1>[\s\S]*?\$\{member\.overloads\.length} overloads/);
  assert.match(
    renderMember,
    /class="member-surface-scroll"[\s\S]*?class="api-list api-surface-list member-surface-list"/);
  assert.match(
    renderMember,
    /class="api-surface-footer member-surface-footer"[\s\S]*?id="member-back"[\s\S]*?Choose an overload to inspect/);
  assert.match(
    renderMember,
    /if \(!memberSectionUsesWorkingSurface\(state\.memberSection\)\) return content;[\s\S]*?class="member-surface"[\s\S]*?<p>\$\{escapeHtml\(member\.kind\)} <span>· \$\{overloadIndex \+ 1} of \$\{member\.overloads\.length}<\/span><\/p>/);
  assert.match(
    memberOverview,
    /class="learn-section member-overview-intro">\s*<section class="signature-panel"[\s\S]*?class="member-documentation"[\s\S]*?class="member-identity"/);
  assert.doesNotMatch(
    memberOverview,
    /class="learn-section member-overview-intro">\s*\$\{documentationSummary\}[\s\S]*?class="signature-panel"/);
  assert.match(
    memberOverview,
    /const documentationSummary = documentationLoading[\s\S]*?Documentation query failed:[\s\S]*?overload\.summary[\s\S]*?No summary was found in the package XML documentation/);
  assert.match(
    memberOverview,
    /aria-labelledby="member-declaration-title"[\s\S]*?aria-label="Copy declaration"[\s\S]*?aria-label="Copy stable selector"[\s\S]*?aria-label="Copy digest"[\s\S]*?aria-label="Copy canonical signature"/);
  assert.match(
    memberOverview,
    /renderMemberContractSections\(\{[\s\S]*?parameters,[\s\S]*?returnType: overload\.returnType,[\s\S]*?returns: overload\.returns,[\s\S]*?exceptions: overload\.exceptions,[\s\S]*?activeFramework: pkg\.activeFramework,[\s\S]*?documentationStatus:/);
  assert.doesNotMatch(renderMember, /class="learn-title"/);
  assert.doesNotMatch(
    renderMember,
    /<dt>Namespace:<\/dt>|<dt>Assembly:<\/dt>|<dt>Package:<\/dt>/);
  assert.match(
    appSource,
    /const memberOverloadPicker =[\s\S]*?!selectedConcreteOverload\([\s\S]*?const memberWorkingSurface =[\s\S]*?currentPendingGraphMember\(\) === null[\s\S]*?memberOverloadPicker[\s\S]*?memberSectionUsesWorkingSurface\(state\.memberSection\)/);
  assert.match(
    stylesSource,
    /\.detail-scroll\.api-working-surface,\s*\.detail-scroll\.metadata-working-surface,\s*\.detail-scroll\.member-working-surface \{[^}]*overflow: hidden;[^}]*padding: 0;/s);
  assert.match(
    stylesSource,
    /\.member-surface \{[^}]*height: 100%;[^}]*grid-template-rows: 40px minmax\(0, 1fr\);/s);
  assert.match(
    stylesSource,
    /\.member-surface \.learn-overview \{ max-width: none; \}/);
  assert.match(
    stylesSource,
    /\.member-surface \.learn-overview > \.learn-section:not\(\.member-overview-intro\),\s*\.member-applicability \{ max-width: 900px; \}[\s\S]*?\.member-overview-intro \.signature-panel \{ margin-top: 0; \}/);
  assert.match(
    stylesSource,
    /\.member-documentation \{ max-width: 760px;/);
  assert.match(
    stylesSource,
    /\.member-surface-scroll \{ container: member-surface \/ inline-size;[\s\S]*?@container member-surface \(max-width: 575px\) \{[\s\S]*?\.member-identity dl > div \{ grid-template-columns: minmax\(0, 1fr\); \}/);
  assert.match(
    stylesSource,
    /\.member-contract-list > div \{[^}]*grid-template-columns: minmax\(190px, 32%\) minmax\(0, 1fr\);[\s\S]*?@container member-surface \(max-width: 575px\) \{[\s\S]*?\.member-contract-list > div \{ grid-template-columns: minmax\(0, 1fr\); \}/);
  assert.match(
    stylesSource,
    /\.api-surface-head p,\s*\.metadata-surface-head p \{[^}]*overflow: hidden;[^}]*text-overflow: ellipsis;/s);
  assert.doesNotMatch(
    stylesSource,
    /\.api-surface-head p span \{[^}]*display: none;/s);
});

test("type metadata uses a full-area working surface without the inset type heading", () => {
  const renderLens =
    appSource.match(/function renderLens\([\s\S]*?\n}\n\nfunction renderApiLens/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /const metadataWorkingSurface =\s*activeScope === "type" && state\.lens === "metadata"/);
  assert.match(
    appSource,
    /metadataWorkingSurface \? " metadata-working-surface" : ""/);
  assert.match(
    renderLens,
    /case "metadata":\s*return renderTypeMetadataHtml\(item\);/);
  assert.doesNotMatch(
    renderLens,
    /case "metadata":[\s\S]*?typeHeadingHtml\(item\)/);
  assert.match(
    stylesSource,
    /\.detail-scroll\.api-working-surface,\s*\.detail-scroll\.metadata-working-surface,\s*\.detail-scroll\.member-working-surface \{[^}]*overflow: hidden;[^}]*padding: 0;/s);
  assert.match(
    stylesSource,
    /\.metadata-surface \{[^}]*height: 100%;[^}]*grid-template-rows: 40px minmax\(0, 1fr\) 34px;/s);
  assert.match(
    stylesSource,
    /\.metadata-surface-scroll \{[^}]*overflow: auto;/s);
});

test("package metadata uses compact coordinates in a full-area working surface", () => {
  const renderPackage =
    appSource.match(/function renderPackageView\([\s\S]*?\n}\n\nfunction renderWorkspaceView/)?.[0]
    ?? "";
  const renderMetadata =
    appSource.match(/function renderPackageMetadata\([\s\S]*?\n}\n\nasync function loadPackageMetadata/)?.[0]
    ?? "";
  assert.match(
    appSource,
    /const packageMetadataWorkingSurface =\s*activeScope === "package" && state\.packageLens === "metadata"/);
  assert.match(
    appSource,
    /packageMetadataWorkingSurface \? " package-metadata-working-surface" : ""/);
  assert.match(
    appSource,
    /const contentNavigationIntegrated =[\s\S]*?\|\| packageMetadataWorkingSurface[\s\S]*?;/);
  assert.match(
    renderPackage,
    /if \(state\.packageLens === "metadata"\) return body;/);
  assert.match(
    renderMetadata,
    /data-platform-metadata-library[\s\S]*?requireSelection: true[\s\S]*?controlsHtml:[\s\S]*?package-metadata-controls[\s\S]*?packageCoordinateFields\(\)/);
  assert.match(
    stylesSource,
    /\.detail-scroll\.package-metadata-working-surface \{[^}]*overflow: hidden;[^}]*padding: 0;/s);
  assert.match(
    stylesSource,
    /\.package-metadata-surface \{[^}]*height: 100%;[^}]*grid-template-rows: 40px auto minmax\(0, 1fr\) 34px;/s);
  assert.match(
    stylesSource,
    /\.package-metadata-scroll \{[^}]*overflow: auto;/s);
});

test("graph member projections stay transport- and package-bounded", () => {
  assert.match(
    browserGraphMemberSource,
    /QueryGraphMemberSurface[\s\S]*?BrowserSurfaceTextBudget\([\s\S]*?MaxRetainedTextCharacters[\s\S]*?BrowserSurfaceProjection\.Type\([\s\S]*?textBudget,[\s\S]*?selectedMembers: \[resolution\.Member\]\)[\s\S]*?textBudget\.CommitParticipant\(\)/);

  const publicMember = { name: "Public" };
  const selected = { name: "Selected", graphOnly: true };
  const removed = { name: "Removed", graphOnly: true };
  const types = [
    { api: [publicMember, removed] },
    { api: [selected] },
  ];
  retainGraphMemberProjection(types, selected);
  assert.deepEqual(types, [
    { api: [publicMember] },
    { api: [selected] },
  ]);

  const commit = sourceText(functionDeclaration("commitGraphMemberSelection"));
  assert.match(commit, /retainGraphMemberProjection\(pkg\.types, staged\.member\)/);
  assert.ok(
    commit.indexOf("retainGraphMemberProjection(pkg.types, staged.member)")
      < commit.indexOf("type.api.push(staged.member)"));
});

test("member filters retain an exact selected graph target", () => {
  const availability = sourceText(functionDeclaration("memberSelectionIsAvailable"));
  assert.match(
    availability,
    /visible\.some\(group => group\.key === state\.selectedMemberKey\)[\s\S]*selectedGraphMemberGroup\(type\) != null/);

  for (const name of ["enterMemberScope", "normalizeMemberSelection"]) {
    assert.match(
      sourceText(functionDeclaration(name)),
      /memberSelectionIsAvailable\(type, visible\)/);
  }

  const typePanelCall = onlyCallExpressionNamed(appSyntax, "bindTypePanel");
  const actions = objectArgument(typePanelCall, 1, "bindTypePanel");
  for (const name of [
    "onMemberAccessibilityFilterSelect",
    "onMemberFilterChange",
    "onMemberFilterClear",
    "onMemberFilterKeyDown",
    "onMemberKindFilterSelect",
    "onMemberTraitFilterSelect",
  ]) {
    assert.match(
      sourceText(callbackProperty(actions, name)),
      /normalizeMemberSelection\(\)/);
  }
});

test("pending graph restoration replaces its current history entry", () => {
  const navigation = {
    stack: [
      { sig: "provisional", view: { selectedOverloadIndex: 1 } },
      { sig: "forward", view: { selectedOverloadIndex: 2 } }
    ],
    index: 0
  };
  const resolved = {
    sig: "resolved",
    view: { selectedOverloadIndex: 0 }
  };

  replaceCurrentNavigationEntry(navigation, resolved);

  assert.equal(navigation.index, 0);
  assert.equal(navigation.stack.length, 2);
  assert.deepEqual(navigation.stack[0], resolved);
  assert.deepEqual(
    navigation.stack.map(entry => entry.sig),
    ["resolved", "forward"]);
});

test("history normalization preserves the current index and forward entries", () => {
  const navigation: NavigationState<{ selectedOverloadIndex?: number }> = {
    stack: [
      { sig: "older", view: {} },
      { sig: "recorded", view: { selectedOverloadIndex: 0 } },
      { sig: "forward", view: {} }
    ],
    index: 1
  };
  const normalized = {
    sig: "normalized",
    view: { selectedOverloadIndex: 1 }
  };

  reconcileCurrentNavigationEntry(navigation, normalized);

  assert.equal(navigation.index, 1);
  assert.deepEqual(
    navigation.stack.map(entry => entry.sig),
    ["older", "normalized", "forward"]);

  reconcileCurrentNavigationEntry(navigation, normalized);
  assert.deepEqual(
    navigation.stack.map(entry => entry.sig),
    ["older", "normalized", "forward"]);
});

test("restored views reconcile normalization before rendering", () => {
  const apply =
    appSource.match(/function applyView[\s\S]*?(?=\nfunction navBack)/)?.[0]
    ?? "";
  assert.match(
    apply,
    /if \(!state\.atPackageRoot\) revealTypeInFilters\(type\)/);
  assert.match(
    apply,
    /graphSelection\?\.group\.key !== view\.selectedMemberKey\) \{[\s\S]*?navigationHistory\.normalizeCurrent\(\);[\s\S]*?restorePendingGraphMember\(\)/);
  assert.match(
    apply,
    /state\.memberSection = memberHistory\.memberSection;[\s\S]*?navigationHistory\.normalizeCurrent\(\);/);
});

test("ambiguous call graph targets expose a visible refusal", () => {
  const binding =
    appSource.match(/function callGraphNodeBinding\([\s\S]*?\n}(?=\n\nfunction blockedCallGraphNodeBinding)/)?.[0]
    ?? "";
  assert.match(
    binding,
    /return blockedCallGraphNodeBinding\([\s\S]*graphTargetBlockedReason/);
  assert.match(
    appSource,
    /function blockedCallGraphNodeBinding\([\s\S]*label: `Cannot open \$\{target\.typeFullName\}\.\$\{target\.memberName\}: \$\{reason\}`[\s\S]*blocked: true/);
  assert.match(
    appSource,
    /invalidateGraphMemberNavigation\(\);\s*state\.memberCallGraphSeq\+\+;[\s\S]*?state\.graphMemberNavigationError\s*=\s*`Could not open \$\{target\.typeFullName\}\.\$\{target\.memberName\}: \$\{reason\}\.`;[\s\S]*?render\(\)/);
  assert.match(
    graphInteractionsSource,
    /node\.setAttribute\("tabindex", "0"\);[\s\S]*node\.setAttribute\("role", "button"\)[\s\S]*node\.addEventListener\("click"[\s\S]*id: "call-graph-node\.activate"[\s\S]*key: \["Enter", " "\]/);
});

test("navigable call graph targets share mouse and keyboard activation", () => {
  const binding =
    appSource.match(/function callGraphNodeBinding\([\s\S]*?\n}(?=\n\nfunction blockedCallGraphNodeBinding)/)?.[0]
    ?? "";

  assert.equal(
    binding.match(/`Open \$\{target\.typeFullName\}\.\$\{target\.memberName\}`/g)?.length,
    3);
  assert.match(
    graphInteractionsSource,
    /node\.setAttribute\("tabindex", "0"\);[\s\S]*node\.setAttribute\("role", "button"\);[\s\S]*node\.setAttribute\("aria-label", binding\.label\)/);
  assert.match(
    stylesSource,
    /\.graph-viewport g\.node\.nav-node:focus-visible rect,[\s\S]*?stroke: var\(--blue\); stroke-width: 3px;/);
  assert.match(
    appSource,
    /id="platform-drill-error" class="graph-drill-error" role="alert" tabindex="-1"/);
  assert.match(
    appSource,
    /function showPlatformTargetError[\s\S]*?render\(\);\s*focusPlatformGraphError\(document\);\s*await renderMermaidCallGraph\(\)/);
});

test("async graph work uses one source-view ownership contract", () => {
  const ownership =
    appSource.match(/function captureViewOperation[\s\S]*?(?=\nfunction invalidateGraphMemberNavigation)/)?.[0]
    ?? "";
  assert.match(
    ownership,
    /navigationSequence: navigationSequence\.current\(\),[\s\S]*?sourceView: viewSignature\(\)/);
  assert.match(
    ownership,
    /owner\.sequence === currentSequence[\s\S]*?owner\.navigationSequence === navigationSequence\.current\(\)[\s\S]*?owner\.sourceView === viewSignature\(\)/);
  assert.equal(
    appSource.match(/captureViewOperation\(/g)?.length,
    5);
});

test("call graph navigation rejects ambiguous loaded package coordinates", () => {
  const target = {
    assembly: "Example",
    typeMetadataId: "Example.Widget",
    kind: "external"
  };
  const first = {
    ...packageAt("1.0.0", "net8.0"),
    types: [{ assembly: "Example", metadataId: "Example.Widget" }]
  };
  const second = {
    ...packageAt("2.0.0", "net9.0"),
    types: [{ assembly: "Example", metadataId: "Example.Widget" }]
  };

  assert.deepEqual(resolveLoadedGraphTargetCandidate([first], target), {
    status: "unique",
    pkg: first,
    type: first.types[0]
  });
  assert.deepEqual(
    resolveLoadedGraphTargetCandidate([first, second], target),
    { status: "ambiguous" });
  assert.deepEqual(
    resolveLoadedGraphTargetCandidate([], target),
    { status: "missing" });
  assert.equal(
    graphTargetNavigationDisposition({ status: "ambiguous" }, target),
    "blocked");
  assert.equal(
    graphTargetNavigationDisposition({ status: "missing" }, target),
    "platform");
  assert.equal(
    graphTargetNavigationDisposition(
      { status: "missing" },
      { ...target, assemblyVersion: null }),
    "none");
});

test("call graph navigation rejects assembly identity skew", () => {
  const target = {
    assembly: "Example",
    assemblyVersion: "1.0.0.0",
    assemblyCulture: "neutral",
    assemblyPublicKeyToken: "0011223344556677",
    typeMetadataId: "Example.Widget",
    kind: "external"
  };
  const exact = {
    name: "Example",
    version: "1.0.0.0",
    culture: null,
    publicKeyToken: "0011223344556677"
  };

  assert.equal(callGraphAssemblyIdentityMatches(target, exact), true);
  assert.equal(
    callGraphAssemblyIdentityMatches(target, { ...exact, version: "2.0.0.0" }),
    false);
  assert.equal(
    callGraphAssemblyIdentityMatches(
      { ...target, assemblyVersion: null },
      exact),
    false);
  assert.equal(
    callGraphAssemblyIdentityMatches(
      { assembly: "Example", typeMetadataId: "Example.Widget" },
      exact),
    true);

  const skewedPackage = {
    ...packageAt("2.0.0", "net8.0"),
    types: [{
      assemblyId: "example",
      assemblyName: "Example",
      metadataId: "Example.Widget"
    }],
    assemblies: [{
      id: "example",
      ...exact,
      version: "2.0.0.0"
    }]
  };
  const candidate = resolveLoadedGraphTargetCandidate(
    [skewedPackage],
    target);
  assert.deepEqual(candidate, { status: "skew" });
  assert.equal(
    graphTargetNavigationDisposition(candidate, target),
    "blocked");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      candidate,
      target,
      false),
    "blocked");
  assert.equal(
    graphTargetBlockedReason(candidate, "package"),
    "the loaded package assembly identity does not match the exact target");

  const noProjectedTarget = {
    ...skewedPackage,
    types: [{
      assemblyId: "example",
      assemblyName: "Example",
      metadataId: "Example.Other"
    }]
  };
  assert.deepEqual(
    resolveLoadedGraphTargetCandidate([noProjectedTarget], target),
    { status: "skew" });

  const exactResident = {
    ...noProjectedTarget,
    assemblies: [{ id: "example", ...exact }]
  };
  const residentCandidate = resolveLoadedGraphTargetCandidate(
    [exactResident],
    target);
  assert.deepEqual(residentCandidate, { status: "resident" });
  assert.equal(
    graphTargetNavigationDisposition(residentCandidate, target),
    "blocked");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      residentCandidate,
      target,
      false),
    "drill");
  assert.equal(
    graphTargetBlockedReason(residentCandidate, "package"),
    "the exact target type is not projected from the loaded package assembly");
});

test("surface asset currency makes repeated graph navigation reuse its type", () => {
  const target = {
    assembly: "Example",
    assemblyVersion: "1.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: null,
    typeMetadataId: "Example.Internal",
    kind: "external"
  };
  const pkg = {
    ...packageAt("1.0.0", "net8.0"),
    assemblies: [{
      id: "compile:ref/net8.0/Example.dll",
      name: "Example",
      version: "1.0.0.0",
      culture: null,
      publicKeyToken: null
    }],
    types: [] as Array<{
      assemblyId: string;
      assemblyName: string;
      metadataId: string;
    }>
  };

  assert.deepEqual(
    resolveLoadedGraphTargetCandidate([pkg], target),
    { status: "resident" });

  const projectedType = {
    assemblyId: "compile:ref/net8.0/Example.dll",
    assemblyName: "Example",
    metadataId: "Example.Internal"
  };
  pkg.types.push(projectedType);

  for (let attempt = 0; attempt < 2; attempt++) {
    assert.deepEqual(resolveLoadedGraphTargetCandidate([pkg], target), {
      status: "unique",
      pkg,
      type: projectedType
    });
  }
  assert.equal(pkg.types.length, 1);
});

test("graph-member projection carries exact surface currency and a collision-safe id", () => {
  assert.match(
    appSource,
    /inspectGraphMemberSurface\([\s\S]*graphMemberSurfaceAssembly\(target, type\)/,
  );
  assert.match(
    browserGraphMemberSource,
    /BrowserSurfaceProjection\.Type\([\s\S]*qualifyId: true,[\s\S]*selectedMembers:/,
  );

  const projected = {
    id: "Surface.A:Shared.Internal",
    definitionId: "Shared.Internal",
    assemblyId: "compile:ref/net11.0/Surface.A.dll",
  };
  const types = [
    {
      id: "Shared.Internal",
      definitionId: "Shared.Internal",
      assemblyId: "compile:ref/net11.0/Surface.B.dll",
    },
    projected,
  ];

  assert.equal(
    types.find(type => type.id === projected.id)?.assemblyId,
    projected.assemblyId);
});

test("restored graph members recover dotted routing from loaded type currency", () => {
  const original = {
    assembly: "System.Text.Json",
    assemblyVersion: "10.0.0.0",
    assemblyCulture: "",
    assemblyPublicKeyToken: "cc7b13ffcd2ddd51",
    typeDefinitionId: "System.Text.Json.JsonReaderHelper",
    typeMetadataId: "System.Text.Json.JsonReaderHelper",
    memberName: "UnescapeAndCompareBothInputs",
    selectorKey: "method:UnescapeAndCompareBothInputs",
    metadataToken: 0x06000123,
    surfaceAssemblyId: "compile:ref/net10.0/System.Text.Json.dll",
  };
  const restored = graphMemberTargetFromShare(
    graphMemberShareTarget(original));

  assert.ok(restored);
  assert.equal(restored.surfaceAssemblyId, undefined);
  assert.equal(
    graphMemberSurfaceAssembly(restored, {
      assembly: "System.Text.Json.dll",
      assemblyId: "compile:ref/net10.0/System.Text.Json.dll",
    }),
    "compile:ref/net10.0/System.Text.Json.dll");
  assert.equal(
    graphMemberSurfaceAssembly(restored),
    "System.Text.Json.dll");
});

test("an exact resident runtime target wins over package identity skew", () => {
  const target = {
    assembly: "Example",
    assemblyVersion: "1.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: null,
    typeMetadataId: "Example.Widget",
    kind: "external"
  };

  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "skew" },
      { status: "unique", pkg: null, type: null },
      target,
      true),
    "resident");
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "skew" },
      { status: "resident" },
      target,
      true),
    "resident");
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "skew" },
      { status: "missing" },
      target),
    "blocked");
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "missing" },
      { status: "ambiguous" },
      target,
      true),
    "blocked");
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "skew" },
      { status: "ambiguous" },
      target),
    "blocked");
  assert.equal(
    combinedGraphTargetNavigationDisposition(
      { status: "missing" },
      { status: "unique", pkg: null, type: null },
      { ...target, assemblyVersion: null },
      true),
    "none");
});

test("graph-only overloads retain the latest graph-selected body", () => {
  const getter = {
    memberName: "get_Item",
    selectorKey: "getter-selector"
  };
  const setter = {
    memberName: "set_Item",
    selectorKey: "setter-selector"
  };
  const graphOnlyOverload = {
    graphOnly: true,
    graphTarget: getter
  };
  const publicOverload = {
    graphOnly: false
  };

  retainGraphOnlyBodyTarget(graphOnlyOverload, setter);
  retainGraphOnlyBodyTarget(publicOverload, setter);

  assert.equal(graphOnlyBodyTarget(graphOnlyOverload), setter);
  assert.equal(graphOnlyBodyTarget(publicOverload), null);
  assert.equal(
    Object.prototype.hasOwnProperty.call(publicOverload, "graphTarget"),
    false);
  assert.match(
    appSource,
    /selectedBodyTarget = retainGraphOnlyImplementationBody\(\s*overload,\s*bodyTarget\)/);
  assert.match(
    appSource,
    /const selectedTarget = retainGraphOnlyImplementationBody\(\s*staged\.member,\s*target\)/);
  assert.doesNotMatch(
    appSource,
    /group\.overloads\[overloadIndex\]\.graphTarget = bodyTarget/);
});

test("selected graph bodies preserve the full navigation identity", () => {
  const selected = graphMemberTargetWithSelectedBody({
    assembly: "Example.dll",
    assemblyVersion: "1.2.3.4",
    assemblyCulture: null,
    assemblyPublicKeyToken: "abcdef",
    typeDefinitionId: "T:Example.Widget",
    typeMetadataId: "Example.Widget",
    memberName: "stale",
    selectorKey: "stale-selector",
    metadataToken: 0x06000002,
  }, {
    token: 0x06000001,
    memberName: "get_Value",
    selectorKey: "getter-selector",
  });

  assert.deepEqual(graphMemberShareTarget(selected), [
    "Example.dll",
    "1.2.3.4",
    null,
    "abcdef",
    "T:Example.Widget",
    "Example.Widget",
    "get_Value",
    "getter-selector",
    0x06000001,
  ]);
});

test("call graph navigation keeps identity-unknown targets inert", () => {
  const target = {
    assembly: "Example",
    assemblyVersion: null,
    assemblyCulture: null,
    assemblyPublicKeyToken: null,
    typeMetadataId: "Example.Widget",
    kind: "external"
  };
  const pack = {
    ...packageAt("1.0.0", "net8.0"),
    types: [{
      assemblyId: "example",
      assemblyName: "Example",
      metadataId: "Example.Widget"
    }],
    assemblies: [{
      id: "example",
      name: "Example",
      version: "1.0.0.0",
      culture: null,
      publicKeyToken: null
    }]
  };

  const candidate = resolveLoadedGraphTargetCandidate([pack], target);
  assert.deepEqual(candidate, { status: "missing" });
  assert.equal(
    graphTargetNavigationDisposition(candidate, target),
    "none");
  assert.equal(
    runtimeGraphTargetNavigationDisposition(
      candidate,
      target,
      false),
    "none");
});

test("failed graph restoration uses the canonical empty member identity", () => {
  const restore =
    appSource.match(/async function restorePendingGraphMember[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  assert.match(
    restore,
    /catch \(error\)[\s\S]*?state\.selectedMemberKey = "";/);
  assert.doesNotMatch(
    restore,
    /state\.selectedMemberKey = null/);
});

test("call graph navigation joins asset names through metadata identity", () => {
  const type = {
    assembly: "Physical.dll",
    assemblyId: "asset:physical",
    assemblyName: "Logical",
    metadataId: "Example.Widget"
  };
  const pkg = {
    ...packageAt("1.0.0", "net8.0"),
    types: [type],
    assemblies: [{
      id: "asset:physical",
      name: "Logical",
      version: "1.0.0.0",
      culture: null,
      publicKeyToken: null
    }]
  };
  const target = {
    assembly: "Logical",
    assemblyVersion: "1.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: null,
    typeMetadataId: "Example.Widget"
  };

  assert.deepEqual(resolveLoadedGraphTargetCandidate([pkg], target), {
    status: "unique",
    pkg,
    type
  });
});

test("package overview joins member counts by exact asset identity", () => {
  const descriptors = [
    { id: "asset:a", name: "Logical", publicMembers: 3 },
    { id: "asset:b", name: "Logical", publicMembers: 7 }
  ];

  assert.equal(
    assemblyDescriptorForType(descriptors, {
      assembly: "Physical.dll",
      assemblyId: "asset:b"
    }),
    descriptors[1]);
  assert.equal(
    assemblyDescriptorForType(descriptors, {
      assembly: "Physical.dll",
      assemblyId: "asset:missing"
    }),
    null);
});

test("call graph navigation joins duplicate metadata names by asset identity", () => {
  const firstType = {
    assembly: "A.dll",
    assemblyId: "asset:a",
    assemblyName: "Logical",
    metadataId: "Example.Widget"
  };
  const secondType = {
    assembly: "B.dll",
    assemblyId: "asset:b",
    assemblyName: "Logical",
    metadataId: "Example.Widget"
  };
  const pkg = {
    ...packageAt("1.0.0", "net8.0"),
    types: [firstType, secondType],
    assemblies: [
      { id: "asset:a", name: "Logical", version: "1.0.0.0" },
      { id: "asset:b", name: "Logical", version: "2.0.0.0" }
    ]
  };
  const target = {
    assembly: "Logical",
    assemblyVersion: "2.0.0.0",
    typeMetadataId: "Example.Widget"
  };

  assert.deepEqual(resolveLoadedGraphTargetCandidate([pkg], target), {
    status: "unique",
    pkg,
    type: secondType
  });
});

test("platform call graph navigation requires the target assembly identity", () => {
  const consoleType = {
    assembly: "System.Console.dll",
    assemblyId: "console",
    assemblyName: "System.Console",
    definitionId: "Interop+ErrorInfo"
  };
  const processType = {
    assembly: "System.Diagnostics.Process.dll",
    assemblyId: "process",
    assemblyName: "System.Diagnostics.Process",
    definitionId: "Interop+ErrorInfo"
  };
  const pack = {
    isRuntimePack: true,
    types: [consoleType, processType],
    assemblies: [
      {
        id: "console",
        name: "System.Console",
        version: "11.0.0.0",
        culture: null,
        publicKeyToken: "b03f5f7f11d50a3a"
      },
      {
        id: "process",
        name: "System.Diagnostics.Process",
        version: "11.0.0.0",
        culture: null,
        publicKeyToken: "b03f5f7f11d50a3a"
      }
    ]
  };
  const target = {
    assembly: "System.Diagnostics.Process",
    assemblyVersion: "11.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: "b03f5f7f11d50a3a",
    typeDefinitionId: "Interop+ErrorInfo"
  };

  assert.equal(resolvePlatformGraphTargetType(pack, target), processType);
  assert.equal(
    resolvePlatformGraphTargetType({
      ...pack,
      types: [...pack.types, { ...processType }]
    }, target),
    null);
  assert.equal(
    resolvePlatformGraphTargetType(pack, {
      ...target,
      assemblyVersion: "12.0.0.0"
    }),
    null);
});

test("opportunity navigation uses exact source assembly and definition identity", () => {
  const first = {
    assembly: "A.dll",
    assemblyId: "a",
    assemblyName: "Shared",
    definitionId: "Example.Widget"
  };
  const second = {
    assembly: "B.dll",
    assemblyId: "b",
    assemblyName: "Shared",
    definitionId: "Example.Widget"
  };
  const pack = {
    types: [first, second],
    assemblies: [
      { id: "a", name: "Shared", version: "1.0.0.0" },
      { id: "b", name: "Shared", version: "2.0.0.0" }
    ]
  };

  assert.equal(resolveOpportunitySourceType(pack, {
    sourceDefinitionId: "Example.Widget",
    sourceAssembly: "Shared",
    sourceAssemblyVersion: "2.0.0.0",
    sourceAssemblyCulture: null,
    sourceAssemblyPublicKeyToken: null
  }), second);
  assert.equal(resolveOpportunitySourceType(pack, {
    sourceDefinitionId: "Example.Widget",
    sourceAssembly: "Shared",
    sourceAssemblyVersion: "3.0.0.0",
    sourceAssemblyCulture: null,
    sourceAssemblyPublicKeyToken: null
  }), null);
  assert.equal(resolveOpportunitySourceCandidate(pack, {
    sourceDefinitionId: "Example.Widget",
    sourceAssembly: "Shared",
    sourceAssemblyVersion: "3.0.0.0",
    sourceAssemblyCulture: null,
    sourceAssemblyPublicKeyToken: null
  }).status, "skew");
  assert.equal(resolveOpportunitySourceCandidate({
    ...pack,
    assemblies: pack.assemblies.map(assembly => ({
      ...assembly,
      version: "2.0.0.0"
    }))
  }, {
    sourceDefinitionId: "Example.Widget",
    sourceAssembly: "Shared",
    sourceAssemblyVersion: "2.0.0.0",
    sourceAssemblyCulture: null,
    sourceAssemblyPublicKeyToken: null
  }).status, "ambiguous");
  assert.match(
    appSource,
    /opportunity\.sourceIdentity === "legacy"[\s\S]*?openSpotlight\(shortTypeName\(opportunity\.typeId\)\)[\s\S]*?opportunity\.sourceIdentity !== "exact"[\s\S]*?!opportunity\.sourceDefinitionId[\s\S]*?exact identity is unavailable[\s\S]*?if \(candidate\.status !== "unique"\) \{[\s\S]*?appendQueryNotice\(\s*`The opportunity source could not be opened: \$\{reason\}\.`\);[\s\S]*?navigateToType\(candidate\.type\)/);
});

test("cold platform graph navigation acquires the exact assembly before any default runtime", () => {
  const navigation =
    appSource.match(/async function navigateOrDrillPlatform[\s\S]*?(?=\n\})/)?.[0]
    ?? "";
  const coldLoad =
    navigation.match(/if \(!pack\) \{[\s\S]*?(?=\n  let candidate)/)?.[0]
    ?? "";

  assert.match(
    coldLoad,
    /platformPackForGraphAssembly\(\s*node\.assembly,\s*node\.platformPack,\s*runtimePackPackage\(\),\s*framework\)[\s\S]*?loadRuntimePackAssembly\([\s\S]*?targetPack \?\? ""/);
  assert.doesNotMatch(coldLoad, /\bloadRuntimePack\(/);
});

test("relationship navigation rejects ambiguous dotted identities", () => {
  const first = { id: "A:N.T", queryId: "N.T" };
  const second = { id: "B:N.T", queryId: "N.T" };

  assert.equal(uniqueTypeByQueryId([first], "N.T"), first);
  assert.equal(uniqueTypeByQueryId([first, second], "N.T"), null);
  assert.equal(uniqueTypeByQueryId([], "N.T"), null);
});

// Same widening as `engineCallGraphTarget`: the engine's diagnostics payload also carries
// `isIncomplete` and `hasUnexploredTraversalBoundary`, which the message view in `data.ts`
// deliberately ignores in favour of the counted evidence. Keeping them in the fixtures is
// what proves the message is driven by the counts rather than by the summary flag.
const engineCallGraphDiagnostics = (
  fixture: CallGraphDiagnostics & {
    isIncomplete?: boolean;
    hasUnexploredTraversalBoundary?: boolean;
  },
): CallGraphDiagnostics => fixture;

test("call graph diagnostics distinguish failures from expected bounds", () => {
  assert.equal(callGraphDiagnosticsMessage(engineCallGraphDiagnostics({
    isIncomplete: true,
    incompleteNodes: 2,
    incompleteEdges: 1,
    bindingIdentityConflicts: 3,
    hasUnexploredTraversalBoundary: true
  })), "Partial call graph: 2 incomplete nodes, 1 incomplete edge, and 3 binding identity conflicts.");
  assert.equal(callGraphDiagnosticsMessage(engineCallGraphDiagnostics({
    isIncomplete: true,
    incompleteNodes: 0,
    incompleteEdges: 0,
    bindingIdentityConflicts: 0,
    hasUnexploredTraversalBoundary: true
  })), "");
  assert.equal(callGraphDiagnosticsMessage(engineCallGraphDiagnostics({
    isIncomplete: true,
    incompleteNodes: 0,
    incompleteEdges: 0,
    bindingIdentityConflicts: 0,
    hasAnalysisFailureBoundary: true
  })), "Partial call graph: one or more method bodies could not be analyzed.");
  assert.equal(callGraphDiagnosticsMessage(engineCallGraphDiagnostics({
    isIncomplete: true,
    incompleteNodes: 1,
    incompleteEdges: 0,
    bindingIdentityConflicts: 0,
    hasUnexploredTraversalBoundary: true,
    hasAnalysisFailureBoundary: true
  })), "Partial call graph: 1 incomplete node and one or more method bodies could not be analyzed.");
  assert.equal(
    callGraphDiagnosticsMessage(engineCallGraphDiagnostics({ isIncomplete: false })),
    "");
});

test("parameter titles preserve generic identities and contain metadata text", () => {
  assert.equal(
    parameterTitleHtml([
      { type: "System.Collections.Generic.Dictionary<System.String, Example.Widget>" },
      { type: '<img src=x onerror="alert(1)">' }
    ]),
    "(System.Collections.Generic.Dictionary&lt;System.String, Example.Widget&gt;, &lt;img src=x onerror=&quot;alert(1)&quot;&gt;)");
});

test("package Markdown has no styling or resource-loading authority", () => {
  for (const tag of ["style", "img", "iframe", "video", "audio", "source", "link", "svg"]) {
    assert.equal(MARKDOWN_SANITIZE_OPTIONS.ALLOWED_TAGS.includes(tag), false);
  }
  for (const attribute of ["style", "src", "srcset", "href", "poster", "class", "id"]) {
    assert.equal(MARKDOWN_SANITIZE_OPTIONS.ALLOWED_ATTR.includes(attribute), false);
  }
});

test("workspace package models retain the active and newest coordinates within the limit", () => {
  const packages = Array.from(
    { length: MAX_WORKSPACE_PACKAGES },
    (_, index) => packageAt(`${index}.0.0`, "net10.0"));
  const active = packages[0];
  assert.ok(active, "the workspace fixture must hold at least one package");
  const incoming = packageAt("13.0.0", "net10.0");

  const retained = retainWorkspacePackage(packages, active, incoming);

  assert.equal(retained.packages.length, MAX_WORKSPACE_PACKAGES);
  assert.equal(retained.packages.includes(active), true);
  assert.equal(retained.packages.includes(incoming), true);
  assert.deepEqual(retained.evicted, [packages[1]]);
});

test("workspace package replacement reuses its slot at the package limit", () => {
  const packages = Array.from(
    { length: MAX_WORKSPACE_PACKAGES },
    (_, index) => packageAt(`${index}.0.0`, "net10.0"));
  const active = packages[0];
  assert.ok(active, "the workspace fixture must hold at least one package");
  const replacement = packageAt("99.0.0", "net10.0");

  const retained = retainWorkspacePackage(
    packages,
    active,
    replacement,
    active);

  assert.equal(retained.packages.length, MAX_WORKSPACE_PACKAGES);
  assert.equal(retained.packages.includes(active), false);
  assert.equal(retained.packages.includes(replacement), true);
  assert.deepEqual(retained.evicted, [active]);
});

test("closing a package removes its coordinate and selects the adjacent coordinate", () => {
  const first = packageAt("1.0.0", "net8.0");
  const active = packageAt("2.0.0", "net9.0");
  const last = packageAt("3.0.0", "net10.0");

  const removed = removeWorkspacePackage(
    [first, active, last],
    active,
    packageIdentityKey(active));

  assert.deepEqual(removed.packages, [first, last]);
  assert.equal(removed.active, last);
  assert.equal(removed.closed, active);

  const only = removeWorkspacePackage(
    [active],
    active,
    packageIdentityKey(active));
  assert.deepEqual(only.packages, []);
  assert.equal(only.active, null);

  const missing = removeWorkspacePackage(
    [first, active, last],
    active,
    "Missing.Package\u00001.0.0\u0000net10.0");
  assert.deepEqual(missing.packages, [first, active, last]);
  assert.equal(missing.active, active);
  assert.equal(missing.closed, null);
});

test("workspace UI routes replacements and restore notices through bounded paths", () => {
  assert.match(
    packageControlsSource,
    /onFrameworkSelect\(framework\.value\)/);
  assert.match(
    appSource,
    /selectFramework: framework =>\s*observeAsync\(\s*switchPackageFramework\(framework\),\s*"Switching the package framework"\)/);
  assert.match(appSource, /switchPackageFramework\(argument\)/);
  assert.doesNotMatch(
    appSource,
    /loadPackage\(state\.package\.id, state\.package\.version, (?:button\.dataset\.frameworkChip|argument)\)/);
  assert.match(
    appSource,
    /deepLink: deep,\s+navigationSeq,\s+queryNotice: state\.queryNotice/);
  assert.match(
    appSource,
    /clearWorkspacePackages\(\);\s+render\(\);/);
  assert.match(
    appSource,
    /if \(loc\.tabs\?\.length && !workspaceCoordinatesMatch\(state\.packages, loc\.tabs\)\) \{\s+observeAsync\(\s*restoreWorkspaceFromLocation/);
  assert.match(
    appSource,
    /for \(const packageModel of discarded\)\s+releasePackageModelCaches\(packageModel\);/);
  assert.match(
    appSource,
    /type: type\.queryId \?\? type\.id,\s+typeIdentity: type\.definitionId \?\? type\.id/);
  assert.match(
    workspaceNavigationSource,
    /if \(!pkg && tabs\.length\) \{\s+const target = tabs\[/);
  assert.match(
    appSource,
    /function clearNavigationError\(\) \{\s+if \(state\.engineStartupFailed\) return;\s+state\.error = "";\s+state\.errorTitle = "";\s+state\.errorDetail = "";\s+state\.retryAction = null;\s+\}/);
  assert.match(
    appSource,
    /if \(isCreditsPath\(location\.pathname\)\) \{\s+clearNavigationError\(\);[\s\S]*if \(bareHome\) \{[\s\S]*clearNavigationError\(\)/);
  assert.match(
    appSource,
    /function toggleCreditsTheme\(\): "light" \| "dark" \{\s+setTheme\(state\.theme === "dark" \? "light" : "dark", false\);\s+return state\.theme === "light" \? "light" : "dark";\s+\}[\s\S]*onToggleTheme: toggleCreditsTheme/);
  assert.match(
    appSource,
    /onRetry: \(\) => \{\s*if \(state\.retryAction === retryUnavailable\) return;\s*observeAction\(\s*state\.retryAction \?\? bootstrap,\s*"Retrying the inspection"\);\s*\}/);
  assert.match(
    appSource,
    /state\.retryAction = options\.retryAction/);
  assert.match(
    appSource,
    /state\.retryAction = retry/);
  assert.match(
    appSource,
    /appendQueryNotice\(\s+friendly\.message,\s+options\.retryAction/);
  assert.doesNotMatch(
    workspaceSubjectSource,
    /data-workspace-close=/);
  assert.doesNotMatch(
    appSource,
    /onClose: closeWorkspacePackage/);
  assert.match(
    appSource,
    /onSelect: openDefaultWorkspace,\s+onActivate: action =>\s+observeAction\(\s+\(\) => activateWorkspacePackageOccurrence\(action\)/);
  assert.match(
    appSource,
    /const revision = workspaceOccurrenceRevision;[\s\S]*superseded = view\.superseded;[\s\S]*const ownsCurrentRequest =\s*revision === workspaceOccurrenceRevision\s*&& signature === state\.workspaceOccurrenceSignature;[\s\S]*const desiredSignature = JSON\.stringify\(workspaceOccurrenceRequest\(\)\);[\s\S]*!state\.workspaceOccurrenceLoading[\s\S]*state\.workspaceOccurrenceSignature !== desiredSignature/);
  assert.match(
    appSource,
    /if \(!workspaceOccurrenceViewIsVisible\(\)\s*&& \(state\.workspaceOccurrenceSignature\s*\|\| state\.workspaceOccurrences\)\) \{\s*clearWorkspaceOccurrenceView\(\)/);
  assert.match(
    appSource,
    /const key = assemblyId \|\| `legacy:\$\{asm\}`/);
  assert.match(
    appSource,
    /assemblyDescriptorForType\(pkg\.assemblies, stat\)/);
  assert.match(
    appSource,
    /activatePackage\(targetPackage, \{ resetAccessibility: true \}\)/);
});

test("member documentation state is scoped to the exact request", () => {
  assert.deepEqual(
    scopedRequestState("package-a\u0000member-a", "package-b\u0000member-b", false, "bad XML"),
    { loading: false, error: "" });
  assert.deepEqual(
    scopedRequestState("same", "same", true, ""),
    { loading: true, error: "" });
});

test("dependency selection exposes a missing exact framework", () => {
  assert.equal(
    dependencyGroupSelectionMessage({
      dependencyGroupError: "No exact dependency group."
    }),
    "No exact dependency group.");
  assert.equal(dependencyGroupSelectionMessage({}), "");
});

test("dependency group selection resets when package identity changes", () => {
  assert.match(
    appSource,
    /const changed = !packageIdentityEquals\(state\.package, pkg\);\s+state\.workspaceSubjectOpen = false;\s+state\.package = pkg;\s+if \(changed\)\s+state\.dependenciesGroupIndex = null;/);
});

test("missing exact dependency groups never create graph edges", () => {
  const data = {
    dependencyGroupError: "No exact dependency group.",
    dependencyGroups: [{
      index: 0,
      framework: "net9.0",
      isActive: false,
      dependencies: [{ id: "Wrong.Dependency", versionRange: "1.0.0" }]
    }]
  };

  assert.equal(selectedDependencyGroup(data), null);
});

test("dependency graph honors explicit selection after an exact group miss", () => {
  const data = {
    dependencyGroupError: "No exact dependency group.",
    dependencyGroups: [
      { index: 0, framework: "net8.0", isActive: false, dependencies: [] },
      { index: 1, framework: "net9.0", isActive: false, dependencies: [] }
    ]
  };

  assert.equal(
    selectedDependencyGroup(data, 0),
    data.dependencyGroups[0]);
});

test("dependency graph does not turn display fallback into explicit selection", () => {
  const missingExact = {
    dependencyGroupError: "No exact dependency group."
  };

  assert.equal(
    dependencyGraphGroupSelectionIndex(missingExact, null, 0),
    null);
  assert.equal(
    dependencyGraphGroupSelectionIndex(missingExact, 1, 0),
    1);
  assert.equal(
    dependencyGraphGroupSelectionIndex({}, null, 1),
    1);
  assert.match(
    graphSource,
    /dependencyGraphGroupSelectionIndex\(\s*model\.packageDependencies,\s*model\.dependenciesGroupIndex,\s*fallbackGroupIndex\)/);
});

test("dependency graph uses each cached package's product-selected group", () => {
  const data = {
    dependencyGroupError: "",
    dependencyGroups: [
      { index: 0, framework: "any", isActive: false, dependencies: [] },
      { index: 1, framework: "any", isActive: true, dependencies: [] }
    ]
  };

  assert.equal(
    selectedDependencyGroup(data),
    data.dependencyGroups[1]);
});

test("dependency graph uses the active package's explicitly selected group", () => {
  const data = {
    dependencyGroupError: "",
    dependencyGroups: [
      { index: 0, framework: "net8.0", isActive: false, dependencies: [] },
      { index: 1, framework: "net9.0", isActive: true, dependencies: [] }
    ]
  };

  assert.equal(
    selectedDependencyGroup(data, 0),
    data.dependencyGroups[0]);
  assert.match(
    graphSource,
    /selectedDependencyGroup\(\s*model\.packageDependencies,\s*selectedGroupIndex\)/);
});

test("Mermaid labels contain grammar-significant metadata", () => {
  const encoded = mermaidLabel(
    "A\"B\n<x>&\\\u2028\u202E\u200D\uD800X\uDC00\u{E0001}-Caf\u00E9\u{1F600}");

  assert.equal(
    encoded,
    "A&quot;B&#92;u000A&lt;x&gt;&amp;&#92;&#92;u2028"
      + "&#92;u202E&#92;u200D&#92;uD800X&#92;uDC00"
      + "&#92;uDB40&#92;uDC01-Caf\u00E9\u{1F600}");
  for (const character of [
    '"', "\n", "<", ">", "\\", "\u2028", "\u202E", "\u200D", "\uD800", "\uDC00"
  ]) {
    assert.equal(encoded.includes(character), false);
  }
  assert.equal(encoded.endsWith("-Caf\u00E9\u{1F600}"), true);
});

test("type graph rendering contains artifact labels", () => {
  const definition = buildTypeGraphMermaid({
    graphNodes: [
      {
        id: "self",
        displayName: "Example.A\u202E\uD800-Caf\u00E9\u{1F600}",
        role: "self"
      },
      { id: "base", displayName: "Example.Base", role: "base" }
    ],
    graphEdges: [{ fromId: "self", toId: "base" }]
  });
  assert.ok(definition, "the fixture graph must render a mermaid definition");

  assert.match(
    definition,
    /t0\["A&#92;u202E&#92;uD800-Café😀"\]:::self/);
  assert.equal(definition.includes("\u202E"), false);
  assert.equal(definition.includes("\uD800"), false);
});

test("dependency graph rendering contains artifact labels", () => {
  const root = packageAt("1.0.0", "net8.0");
  const definition = buildDependencyGraphMermaid(
    {
      package: root,
      packages: [root],
      packageDependencies: {
        dependencyGroupError: "",
        dependencyGroups: [{
          index: 0,
          framework: "net8.0",
          isActive: true,
          dependencies: [{
            id: "Dependency\u200D\uDC00-Caf\u00E9\u{1F600}",
            versionRange: ""
          }]
        }]
      },
      dependenciesGroupIndex: 0,
      workspaceDependencies: {}
    },
    () => null);
  assert.ok(definition, "the fixture graph must render a mermaid definition");

  assert.match(
    definition.definition,
    /d1\["Dependency&#92;u200D&#92;uDC00-Café😀"\]:::external/);
  assert.equal(definition.definition.includes("\u200D"), false);
  assert.equal(definition.definition.includes("\uDC00"), false);
});
