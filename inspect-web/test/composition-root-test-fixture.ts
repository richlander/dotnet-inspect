import assert from "node:assert/strict";
import {
  readFileSync,
  readdirSync,
} from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  parseSync,
  visitorKeys,
} from "oxc-parser";
import type {
  Argument,
  ArrowFunctionExpression,
  CallExpression,
  Directive,
  Expression,
  Function as SyntaxFunction,
  FunctionBody,
  Node,
  ObjectExpression,
  ObjectProperty,
  Span,
  Statement,
} from "oxc-parser";

export const packageAt = (version: string, framework: string, types = 1) => ({
  id: "Example.Package",
  version,
  activeFramework: framework,
  types: Array.from({ length: types }, (_, index) => ({ id: `Type${index}` }))
});


export const appSource = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
export const graphLegendsSource =
  readFileSync(new URL("../src/graph-legends.ts", import.meta.url), "utf8");
export const parsedAppSource = parseSync("dotnet-inspect.ts", appSource);
export const appSyntax = parsedAppSource.program;

// The helpers below read `dotnet-inspect.ts` as syntax rather than as text, so the tests
// can assert on structure. `Node` is oxc's discriminated union over every AST shape, so
// narrowing through `node.type` keeps each helper checked against the real grammar rather
// than against `any`.
export type SyntaxVisitor = (node: Node) => void;

// `visitorKeys` is a runtime map from a node type to that node's child keys, which is
// what makes the walk data-driven instead of a switch over a union with hundreds of
// members. Indexing a node by a key chosen at runtime is the one operation the union
// cannot express, so the assertion is confined to this helper and every caller below
// stays narrowed.
export const syntaxChildren = (node: Node): Record<string, unknown> =>
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  node as unknown as Record<string, unknown>;

// The walk only relies on a node carrying a string `type`, which is what `visitorKeys` is
// keyed by; anything else reached through a child key is skipped rather than trusted.
export function isSyntaxNode(value: unknown): value is Node {
  return typeof value === "object"
    && value !== null
    && "type" in value
    && typeof value.type === "string";
}

export function walkSyntax(node: Node, visit: SyntaxVisitor): void {
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

export function syntaxNodes(root: Node, predicate: (node: Node) => boolean): Node[] {
  const matches: Node[] = [];
  walkSyntax(root, node => {
    if (predicate(node)) matches.push(node);
  });
  return matches;
}

export function onlySyntaxNode<T>(nodes: readonly T[], description: string): T {
  assert.equal(nodes.length, 1, description);
  const only = nodes[0];
  assert.ok(only !== undefined, description);
  return only;
}

// oxc models every function form with one `Function` interface whose `type` is a union,
// so a declaration is that interface with the `type` narrowed. These aliases add the two
// facts the tests below rely on and the union cannot state: a declaration that was found
// has a body, and a callback property holds an arrow function with a block body.
export type DeclaredFunction = SyntaxFunction & { body: FunctionBody };
export type BlockArrowFunction = ArrowFunctionExpression & { body: FunctionBody };

export function hasFunctionBody(declaration: SyntaxFunction): declaration is DeclaredFunction {
  return declaration.body !== null && declaration.body !== undefined;
}

export function isBlockArrowFunction(value: Expression): value is BlockArrowFunction {
  return value.type === "ArrowFunctionExpression"
    && value.body.type === "BlockStatement";
}

export function functionDeclaration(name: string): DeclaredFunction {
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

export function onlyCallExpressionNamed(root: Node, name: string): CallExpression {
  return onlySyntaxNode(callExpressionsNamed(root, name), `${name} call`);
}

export function callArgument(
  call: CallExpression,
  index: number,
  description: string,
): Argument {
  const argument = call.arguments[index];
  assert.ok(argument !== undefined, `${description} argument ${index}`);
  return argument;
}

export function objectArgument(
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

export function statementAt(
  statements: readonly (Statement | Directive)[],
  index: number,
  description: string,
): Statement | Directive {
  const statement = statements[index];
  assert.ok(statement !== undefined, `${description} statement ${index}`);
  return statement;
}

// Several tests pin that a binder is handed a specific identifier, such as `document`.
export function assertIdentifierArgument(
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

export function callExpressionsNamed(root: Node, name: string): CallExpression[] {
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
export function sourceText(node: Span): string {
  return appSource.slice(node.start, node.end).replace(/\s+/g, " ");
}

export function namedProperty(actions: ObjectExpression, name: string): ObjectProperty {
  return onlySyntaxNode(
    actions.properties.filter(
      (item): item is ObjectProperty =>
        item.type === "Property"
        && item.key.type === "Identifier"
        && item.key.name === name),
    `${name} property`);
}

export function callbackProperty(actions: ObjectExpression, name: string): BlockArrowFunction {
  const value = namedProperty(actions, name).value;
  if (!isBlockArrowFunction(value)) {
    assert.fail(
      `${name} callback must be an arrow function with a block body, `
        + `found ${value.type}`);
  }
  return value;
}

export function directCallExpression(
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
export function directCallName(statement: Statement | Directive): string | null {
  if (statement.type !== "ExpressionStatement") return null;
  const expression = statement.expression;
  return expression.type === "CallExpression"
    && expression.callee.type === "Identifier"
    ? expression.callee.name
    : null;
}

export interface IfSignature {
  readonly if: string;
  readonly whenTrue: readonly StatementSignature[];
  readonly whenFalse: readonly StatementSignature[];
}

// A statement is summarised either as a single line of text or, for a branch, as the
// condition plus the summaries of each arm, so the tests can compare control flow without
// depending on formatting.
export type StatementSignature = string | IfSignature;

export function statementSignatures(
  statements: readonly (Statement | Directive)[],
): StatementSignature[] {
  return statements.map(statement => statementSignature(statement));
}

export function branchSignatures(branch: Statement): StatementSignature[] {
  return branch.type === "BlockStatement"
    ? statementSignatures(branch.body)
    : [statementSignature(branch)];
}

export function statementSignature(statement: Statement | Directive): StatementSignature {
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

export const sourceRoot = fileURLToPath(new URL("../src/", import.meta.url));
// `readdirSync` without an encoding is typed as returning buffers as well as strings, so
// the encoding is explicit here to keep the entries `string`.
export const productionTypeScriptSources = readdirSync(sourceRoot, {
  recursive: true,
  encoding: "utf8",
})
  .filter(path => path.endsWith(".ts"))
  .map(path => ({
    path,
    source: readFileSync(join(sourceRoot, path), "utf8"),
  }));
export const workspaceNavigationSource = readFileSync(
  new URL("../src/workspace-navigation.ts", import.meta.url),
  "utf8");
export const workspaceFeedActivationSource = readFileSync(
  new URL("../src/workspace-feed-activation.ts", import.meta.url),
  "utf8");
export const packageAcquisitionSource = readFileSync(
  new URL("../src/package-acquisition.ts", import.meta.url),
  "utf8");
export const packageInspectionSource = readFileSync(
  new URL("../src/package-inspection.ts", import.meta.url),
  "utf8");
export const packageViewSource = readFileSync(
  new URL("../src/package-view.ts", import.meta.url),
  "utf8");
export const libraryControlsSource = readFileSync(
  new URL("../src/library-controls.ts", import.meta.url),
  "utf8");
export const shellControlsSource = readFileSync(
  new URL("../src/shell-controls.ts", import.meta.url),
  "utf8");
export const graphInteractionsSource = readFileSync(
  new URL("../src/graph-interactions.ts", import.meta.url),
  "utf8");
export const sourceInspectionSource = readFileSync(
  new URL("../src/source-inspection.ts", import.meta.url),
  "utf8");
export const metadataInspectionSource = readFileSync(
  new URL("../src/metadata-inspection.ts", import.meta.url),
  "utf8");
export const memberDetailInspectionSource = readFileSync(
  new URL("../src/member-detail-inspection.ts", import.meta.url),
  "utf8");
export const callGraphInspectionSource = readFileSync(
  new URL("../src/call-graph-inspection.ts", import.meta.url),
  "utf8");
export const documentInspectionSource = readFileSync(
  new URL("../src/document-inspection.ts", import.meta.url),
  "utf8");
export const spotlightPackageSearchSource = readFileSync(
  new URL("../src/spotlight-package-search.ts", import.meta.url),
  "utf8");
export const catalogRequestsSource = readFileSync(
  new URL("../src/catalog-requests.ts", import.meta.url),
  "utf8");
export const memberFocusSource = readFileSync(
  new URL("../src/member-focus.ts", import.meta.url),
  "utf8");
export const graphSource = readFileSync(
  new URL("../src/graph-mermaid.ts", import.meta.url),
  "utf8");
export const graphSourceViewerSource = readFileSync(
  new URL("../src/graph-source.ts", import.meta.url),
  "utf8");
export const docViewerSource = readFileSync(
  new URL("../src/doc-viewer.ts", import.meta.url),
  "utf8");
export const annotatedSourceModule = readFileSync(
  new URL("../src/annotated-source.ts", import.meta.url),
  "utf8");
export const typePanelSource = readFileSync(
  new URL("../src/type-panel.ts", import.meta.url),
  "utf8");
export const keybindingRegistrySource = readFileSync(
  new URL("../src/keybinding-registry.ts", import.meta.url),
  "utf8");
export const workbenchKeybindingsSource = readFileSync(
  new URL("../src/workbench-keybindings.ts", import.meta.url),
  "utf8");
export const scopeBarSource = readFileSync(
  new URL("../src/scope-bar.ts", import.meta.url),
  "utf8");
export const settingsPanelSource = readFileSync(
  new URL("../src/settings-panel.ts", import.meta.url),
  "utf8");
export const packageControlsSource = readFileSync(
  new URL("../src/package-controls.ts", import.meta.url),
  "utf8");
export const workspaceSubjectSource = readFileSync(
  new URL("../src/workspace-subject.ts", import.meta.url),
  "utf8");
export const metadataViewerSource = readFileSync(
  new URL("../src/metadata-viewer.ts", import.meta.url),
  "utf8");
export const packageOpportunitiesSource = readFileSync(
  new URL("../src/package-opportunities.ts", import.meta.url),
  "utf8");
export const applicationSources =
  `${appSource}\n${graphSource}\n${packageControlsSource}\n${workspaceSubjectSource}\n${metadataViewerSource}`;
export const stylesSource = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");
export const indexSource = readFileSync(new URL("../index.html", import.meta.url), "utf8");
// The production facade set: eight independently generated modules over one runtime. Each
// assertion below reads the module that owns the operation it is about, so an operation that
// moves to another facade fails here instead of matching a neighbouring module's text.
export const generatedFacadeModules = [
  "inspect-web-host",
  "inspect-web-package",
  "inspect-web-library",
  "inspect-web-metadata",
  "inspect-web-analysis",
  "inspect-web-source",
  "inspect-web-call-graph",
  "inspect-web-catalog",
] as const;
export type GeneratedFacadeModule = typeof generatedFacadeModules[number];
export const generatedFacadeModuleUrls = new Map<GeneratedFacadeModule, URL>(
  generatedFacadeModules.map(module =>
    [module, new URL(`../DotnetInspect.Web/wwwroot/${module}.js`, import.meta.url)]));
export const generatedFacadeSources = new Map<GeneratedFacadeModule, string>(
  generatedFacadeModules.map(module =>
    [module, readFileSync(generatedFacadeModuleUrls.get(module)!, "utf8")]));
export const generatedFacadeSource = (module: GeneratedFacadeModule): string =>
  generatedFacadeSources.get(module)!;
export const generatedFacadeSourceText = generatedFacadeModules
  .map(module => generatedFacadeSource(module))
  .join("\n");
export const engineCoordinatorSource = readFileSync(
  new URL("../src/engine-facades.ts", import.meta.url),
  "utf8");
export const deploySource = readFileSync(
  new URL("../../.github/workflows/deploy-inspect-web.yml", import.meta.url),
  "utf8");
export const dataBarSource = readFileSync(
  new URL("../src/data-bar.ts", import.meta.url),
  "utf8");
export const diagnosticsViewSource = readFileSync(
  new URL("../src/diagnostics-view.ts", import.meta.url),
  "utf8");
export const diagnosticsRouteSource = readFileSync(
  new URL("../src/diagnostics-route.ts", import.meta.url),
  "utf8");
export const spotlightSource = readFileSync(
  new URL("../src/spotlight.ts", import.meta.url),
  "utf8");
export const commandBarSource = readFileSync(
  new URL("../src/command-bar.ts", import.meta.url),
  "utf8");


export const browserGraphMemberSource = readFileSync(
  new URL("../DotnetInspect.Web.Interop.Metadata/TypeAndGraphMemberExports.cs", import.meta.url),
  "utf8");
