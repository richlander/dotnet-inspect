import { createHash } from "node:crypto";
import {
  readFileSync,
  statSync,
} from "node:fs";
import {
  dirname,
  isAbsolute,
  normalize,
  relative,
  resolve,
  sep,
} from "node:path";
import { createRequire } from "node:module";
import {
  API,
  DiagnosticCategory,
  ObjectFlags,
  SignatureKind,
  SymbolFlags,
  TypeFlags,
  TypePredicateKind,
  type Checker,
  type Diagnostic as TypeScriptDiagnostic,
  type IndexInfo as TypeScriptIndexInfo,
  type Project,
  type Signature as TypeScriptSignature,
  type Snapshot,
  type Symbol as TypeScriptSymbol,
  type Type as TypeScriptType,
  type TypePredicate as TypeScriptTypePredicate,
} from "typescript/unstable/sync";
import {
  SyntaxKind,
  isCallLikeExpression,
  isClassDeclaration,
  isElementAccessExpression,
  isEnumMember,
  isExportDeclaration,
  isExportSpecifier,
  isExpression,
  isExternalModuleReference,
  isFunctionDeclaration,
  isImportDeclaration,
  isImportSpecifier,
  isImportTypeNode,
  isInterfaceDeclaration,
  isLiteralTypeNode,
  isMethodDeclaration,
  isModuleDeclaration,
  isParameterDeclaration,
  isPropertyAccessExpression,
  isShorthandPropertyAssignment,
  isSourceFile,
  isStatement,
  isStringLiteralLikeNode,
  isTypeAliasDeclaration,
  isTypeNode,
  isVariableDeclaration,
  type Node as TypeScriptNode,
  type SourceFile as TypeScriptSourceFile,
} from "typescript/unstable/ast";

const pinnedTypeScriptVersion = "7.0.2";
const handleConstructionToken = Symbol("TypeScriptSemanticFactsHandle");
type HandleConstructionToken = typeof handleConstructionToken;

export const NodeKind = Object.freeze({
  SourceFile: "SourceFile",
  Identifier: "Identifier",
  StringLiteral: "StringLiteral",
  NumericLiteral: "NumericLiteral",
  BigIntLiteral: "BigIntLiteral",
  RegularExpressionLiteral: "RegularExpressionLiteral",
  NoSubstitutionTemplateLiteral: "NoSubstitutionTemplateLiteral",
  VariableDeclaration: "VariableDeclaration",
  Parameter: "Parameter",
  FunctionDeclaration: "FunctionDeclaration",
  ArrowFunction: "ArrowFunction",
  ClassDeclaration: "ClassDeclaration",
  InterfaceDeclaration: "InterfaceDeclaration",
  TypeAliasDeclaration: "TypeAliasDeclaration",
  ModuleDeclaration: "ModuleDeclaration",
  MethodDeclaration: "MethodDeclaration",
  ImportDeclaration: "ImportDeclaration",
  ExportDeclaration: "ExportDeclaration",
  ImportSpecifier: "ImportSpecifier",
  ExportSpecifier: "ExportSpecifier",
  ShorthandPropertyAssignment: "ShorthandPropertyAssignment",
  EnumMember: "EnumMember",
  PropertyAccessExpression: "PropertyAccessExpression",
  ElementAccessExpression: "ElementAccessExpression",
  CallExpression: "CallExpression",
  NewExpression: "NewExpression",
  TypeNode: "TypeNode",
  Expression: "Expression",
  Statement: "Statement",
  Token: "Token",
  JsDoc: "JsDoc",
  Other: "Other",
} as const);

export type NodeKind = typeof NodeKind[keyof typeof NodeKind];

export const SourceFileClassification = Object.freeze({
  ProjectRoot: "ProjectRoot",
  ImportedProject: "ImportedProject",
  DefaultLibrary: "DefaultLibrary",
  ExternalLibrary: "ExternalLibrary",
  OtherDeclaration: "OtherDeclaration",
} as const);

export type SourceFileClassification
  = typeof SourceFileClassification[keyof typeof SourceFileClassification];

export const SymbolCategory = Object.freeze({
  FunctionScopedVariable: "FunctionScopedVariable",
  BlockScopedVariable: "BlockScopedVariable",
  Property: "Property",
  EnumMember: "EnumMember",
  Function: "Function",
  Class: "Class",
  Interface: "Interface",
  ConstEnum: "ConstEnum",
  RegularEnum: "RegularEnum",
  ValueModule: "ValueModule",
  NamespaceModule: "NamespaceModule",
  TypeLiteral: "TypeLiteral",
  ObjectLiteral: "ObjectLiteral",
  Method: "Method",
  Constructor: "Constructor",
  GetAccessor: "GetAccessor",
  SetAccessor: "SetAccessor",
  Signature: "Signature",
  TypeParameter: "TypeParameter",
  TypeAlias: "TypeAlias",
  ExportValue: "ExportValue",
  Alias: "Alias",
  Prototype: "Prototype",
  ExportStar: "ExportStar",
  Optional: "Optional",
  Transient: "Transient",
  Assignment: "Assignment",
  ModuleExports: "ModuleExports",
  ConstEnumOnlyModule: "ConstEnumOnlyModule",
  ReplaceableByMethod: "ReplaceableByMethod",
  GlobalLookup: "GlobalLookup",
} as const);

export type SymbolCategory = typeof SymbolCategory[keyof typeof SymbolCategory];

export const TypeCategory = Object.freeze({
  Error: "Error",
  Any: "Any",
  Unknown: "Unknown",
  Undefined: "Undefined",
  Null: "Null",
  Void: "Void",
  String: "String",
  Number: "Number",
  BigInt: "BigInt",
  Boolean: "Boolean",
  ESSymbol: "ESSymbol",
  StringLiteral: "StringLiteral",
  NumberLiteral: "NumberLiteral",
  BigIntLiteral: "BigIntLiteral",
  BooleanLiteral: "BooleanLiteral",
  UniqueESSymbol: "UniqueESSymbol",
  EnumLiteral: "EnumLiteral",
  Enum: "Enum",
  NonPrimitive: "NonPrimitive",
  Never: "Never",
  TypeParameter: "TypeParameter",
  Object: "Object",
  Index: "Index",
  TemplateLiteral: "TemplateLiteral",
  StringMapping: "StringMapping",
  Substitution: "Substitution",
  IndexedAccess: "IndexedAccess",
  Conditional: "Conditional",
  Union: "Union",
  Intersection: "Intersection",
} as const);

export type TypeCategory = typeof TypeCategory[keyof typeof TypeCategory];

export const ObjectTypeCategory = Object.freeze({
  Class: "Class",
  Interface: "Interface",
  Reference: "Reference",
  Tuple: "Tuple",
  Anonymous: "Anonymous",
  Mapped: "Mapped",
  Instantiated: "Instantiated",
  ObjectLiteral: "ObjectLiteral",
  EvolvingArray: "EvolvingArray",
  ReverseMapped: "ReverseMapped",
  JsxAttributes: "JsxAttributes",
  FreshLiteral: "FreshLiteral",
  ArrayLiteral: "ArrayLiteral",
} as const);

export type ObjectTypeCategory
  = typeof ObjectTypeCategory[keyof typeof ObjectTypeCategory];

export const SignatureCategory = Object.freeze({
  Call: "Call",
  Construct: "Construct",
} as const);

export type SignatureCategory
  = typeof SignatureCategory[keyof typeof SignatureCategory];

export const TypePredicateCategory = Object.freeze({
  This: "This",
  Identifier: "Identifier",
  AssertsThis: "AssertsThis",
  AssertsIdentifier: "AssertsIdentifier",
} as const);

export type TypePredicateCategory
  = typeof TypePredicateCategory[keyof typeof TypePredicateCategory];

export const DiagnosticCategoryName = Object.freeze({
  Warning: "Warning",
  Error: "Error",
  Suggestion: "Suggestion",
  Message: "Message",
} as const);

export type DiagnosticCategoryName
  = typeof DiagnosticCategoryName[keyof typeof DiagnosticCategoryName];

export const queryApplicability = Object.freeze({
  getSourceSymbol: "ShorthandOrExportSpecifier",
  getContextualType: "Expression",
  getResolvedSignature: "CallLike",
  getUnionConstituents: "Union",
  getIntersectionConstituents: "Intersection",
  getBaseTypes: "ClassOrInterface",
  getTypeArguments: "TypeReference",
  getLiteralBaseType: "Literal",
  getConstantValue: "ConstantCandidate",
  getAliasChain: "AliasSymbol",
  getModuleExports: "ModuleSymbol",
  getModuleExport: "ModuleSymbol",
  getModuleSymbol: "StaticStringLiteralModuleReference",
} as const);

export class SourceFileHandle {
  readonly #sourceFileHandle: HandleConstructionToken;

  constructor(token: HandleConstructionToken) {
    this.#sourceFileHandle = token;
    if (this.#sourceFileHandle !== handleConstructionToken) {
      throw new TypeError("SourceFileHandle values are issued by an open semantic-facts session.");
    }
    Object.freeze(this);
  }
}

export class NodeHandle {
  readonly #nodeHandle: HandleConstructionToken;

  constructor(token: HandleConstructionToken) {
    this.#nodeHandle = token;
    if (this.#nodeHandle !== handleConstructionToken) {
      throw new TypeError("NodeHandle values are issued by an open semantic-facts session.");
    }
    Object.freeze(this);
  }
}

export class SymbolHandle {
  readonly #symbolHandle: HandleConstructionToken;

  constructor(token: HandleConstructionToken) {
    this.#symbolHandle = token;
    if (this.#symbolHandle !== handleConstructionToken) {
      throw new TypeError("SymbolHandle values are issued by an open semantic-facts session.");
    }
    Object.freeze(this);
  }
}

export class DeclarationHandle {
  readonly #declarationHandle: HandleConstructionToken;

  constructor(token: HandleConstructionToken) {
    this.#declarationHandle = token;
    if (this.#declarationHandle !== handleConstructionToken) {
      throw new TypeError("DeclarationHandle values are issued by an open semantic-facts session.");
    }
    Object.freeze(this);
  }
}

export class TypeHandle {
  readonly #typeHandle: HandleConstructionToken;

  constructor(token: HandleConstructionToken) {
    this.#typeHandle = token;
    if (this.#typeHandle !== handleConstructionToken) {
      throw new TypeError("TypeHandle values are issued by an open semantic-facts session.");
    }
    Object.freeze(this);
  }
}

export class SignatureHandle {
  readonly #signatureHandle: HandleConstructionToken;

  constructor(token: HandleConstructionToken) {
    this.#signatureHandle = token;
    if (this.#signatureHandle !== handleConstructionToken) {
      throw new TypeError("SignatureHandle values are issued by an open semantic-facts session.");
    }
    Object.freeze(this);
  }
}

export type SemanticHandle
  = SourceFileHandle
    | NodeHandle
    | SymbolHandle
    | DeclarationHandle
    | TypeHandle
    | SignatureHandle;

type HandleKind
  = "SourceFile"
    | "Node"
    | "Symbol"
    | "Declaration"
    | "Type"
    | "Signature";

interface HandleDescriptor {
  readonly session: object;
  readonly kind: HandleKind;
  readonly identity: object;
}

const handleDescriptors = new WeakMap<object, HandleDescriptor>();
const rawObjectIdentities = new WeakMap<object, object>();

function rawObjectIdentity(raw: object): object {
  const existing = rawObjectIdentities.get(raw);
  if (existing !== undefined) {
    return existing;
  }
  const identity = Object.freeze({});
  rawObjectIdentities.set(raw, identity);
  return identity;
}

function issueHandle<T extends SemanticHandle>(
  create: () => T,
  session: object,
  kind: HandleKind,
  identity: object,
): T {
  const handle = create();
  handleDescriptors.set(handle, Object.freeze({
    session,
    kind,
    identity: rawObjectIdentity(identity),
  }));
  return handle;
}

export interface SourceContentId {
  readonly algorithm: "SHA-256";
  readonly encoding: "UTF-16LECodeUnits";
  readonly hex: string;
}

export function computeSourceContentId(text: string): SourceContentId {
  const bytes = Buffer.allocUnsafe(text.length * 2);
  for (let index = 0; index < text.length; index += 1) {
    const codeUnit = text.charCodeAt(index);
    bytes[index * 2] = codeUnit & 0xff;
    bytes[(index * 2) + 1] = codeUnit >>> 8;
  }
  return Object.freeze({
    algorithm: "SHA-256",
    encoding: "UTF-16LECodeUnits",
    hex: createHash("sha256").update(bytes).digest("hex"),
  });
}

export interface ProjectRelativeSourcePath {
  readonly kind: "ProjectRelative";
  readonly path: string;
}

export interface ExternalSourcePath {
  readonly kind: "External";
  readonly path: string;
}

export type SourcePath = ProjectRelativeSourcePath | ExternalSourcePath;

export interface SourceLocation {
  readonly file: SourceFileHandle;
  readonly content: SourceContentId;
  readonly start: number;
  readonly length: number;
  readonly line: number;
  readonly column: number;
}

export interface SourceFileFact {
  readonly handle: SourceFileHandle;
  readonly path: SourcePath;
  readonly classification: SourceFileClassification;
  readonly contentId: SourceContentId;
  readonly length: number;
  readonly isDeclarationFile: boolean;
}

export interface NodeFact {
  readonly handle: NodeHandle;
  readonly kind: NodeKind;
  readonly location: SourceLocation;
  readonly parent: NodeHandle | undefined;
  readonly children: readonly NodeHandle[];
  readonly spelling: string | undefined;
}

export interface DeclarationFact {
  readonly handle: DeclarationHandle;
  readonly node: NodeHandle;
  readonly kind: NodeKind;
  readonly location: SourceLocation;
  readonly sourceFileClassification: SourceFileClassification;
  readonly containingDeclarations: readonly DeclarationHandle[];
}

export interface SymbolFact {
  readonly handle: SymbolHandle;
  readonly escapedName: string;
  readonly displayName: string;
  readonly categories: readonly SymbolCategory[];
  readonly declarations: readonly DeclarationHandle[];
  readonly valueDeclaration: DeclarationHandle | undefined;
  readonly parent: SymbolHandle | undefined;
  readonly exportSymbol: SymbolHandle | undefined;
}

export interface TypeFact {
  readonly handle: TypeHandle;
  readonly category: TypeCategory;
  readonly objectCategories: readonly ObjectTypeCategory[];
  readonly symbol: SymbolHandle | undefined;
  readonly aliasSymbol: SymbolHandle | undefined;
  readonly aliasTypeArguments: readonly TypeHandle[];
  readonly declarations: readonly DeclarationHandle[];
  readonly intrinsicName: string | undefined;
  readonly literalValue: string | number | boolean | bigint | undefined;
  readonly display: string;
}

export interface TypePredicateFact {
  readonly category: TypePredicateCategory;
  readonly parameterName: string | undefined;
  readonly parameterIndex: number | undefined;
  readonly type: TypeHandle | undefined;
}

export interface SignatureFact {
  readonly handle: SignatureHandle;
  readonly category: SignatureCategory;
  readonly declaration: DeclarationHandle | undefined;
  readonly typeParameters: readonly TypeHandle[];
  readonly parameters: readonly SymbolHandle[];
  readonly thisParameter: SymbolHandle | undefined;
  readonly target: SignatureHandle | undefined;
  readonly hasRestParameter: boolean;
  readonly isAbstract: boolean;
  readonly returnType: TypeHandle;
  readonly restType: TypeHandle | undefined;
  readonly predicate: TypePredicateFact | undefined;
}

export interface IndexInfoFact {
  readonly keyType: TypeHandle;
  readonly valueType: TypeHandle;
  readonly isReadonly: boolean;
  readonly declaration: DeclarationHandle | undefined;
}

export interface AliasStepFact {
  readonly alias: SymbolHandle;
  readonly declarations: readonly DeclarationHandle[];
  readonly target: SymbolHandle;
}

export interface AliasChainFact {
  readonly steps: readonly AliasStepFact[];
  readonly original: SymbolHandle;
}

export interface CoordinateQuery {
  readonly contentId: SourceContentId;
  readonly start: number;
  readonly length: number;
  readonly expectedKind: NodeKind;
}

export interface NormalizedDiagnostic {
  readonly fileName: string | undefined;
  readonly start: number;
  readonly length: number;
  readonly code: number;
  readonly category: DiagnosticCategoryName;
  readonly text: string;
}

export type QueryResult<T>
  = { readonly kind: "Resolved"; readonly value: T }
    | { readonly kind: "Absent"; readonly reason: string }
    | {
      readonly kind: "Ambiguous";
      readonly candidates: readonly T[];
      readonly reason: string;
    }
    | {
      readonly kind: "Unavailable";
      readonly reason:
        | "MissingApiFact"
        | "UnknownSymbol"
        | "UnsupportedApiValue"
        | "UnsupportedResponseShape";
      readonly detail: string;
    }
    | {
      readonly kind: "InvalidCoordinate";
      readonly reason: "SourceContentMismatch" | "OutOfRange";
    }
    | {
      readonly kind: "InvalidHandle";
      readonly reason: "StaleSession" | "WrongKind";
    }
    | {
      readonly kind: "InvalidArgument";
      readonly reason: "OutOfRange";
    }
    | {
      readonly kind: "NotApplicable";
      readonly expectedSubject: string;
      readonly actualSubject: string;
    }
    | {
      readonly kind: "SessionFailure";
      readonly reason: "SessionDisposed" | "ProcessFailure" | "ProtocolFailure";
      readonly detail: string;
    };

export interface CleanupFailure {
  readonly kind: "SnapshotReleaseFailure";
  readonly detail: string;
}

export type DisposeResult
  = { readonly kind: "Disposed" }
    | {
      readonly kind: "DisposeFailed";
      readonly failures: readonly CleanupFailure[];
    };

export interface OpenedSemanticFacts {
  readonly kind: "Opened";
  readonly session: TypeScriptSemanticFactsSession;
}

interface OpenFailureBase {
  readonly cleanupFailures: readonly CleanupFailure[];
}

export interface InvalidInputOpenFailure extends OpenFailureBase {
  readonly kind: "InvalidInput";
  readonly reason: "RelativePath" | "DirectoryPath" | "FileUrl" | "MissingPath";
}

export interface ProjectSelectionOpenFailure extends OpenFailureBase {
  readonly kind: "ProjectSelectionFailed";
  readonly reason: "NoProject" | "MultipleProjects" | "RequestedProjectMismatch";
  readonly candidates: readonly string[];
}

export interface DiagnosticsRejectedOpenFailure extends OpenFailureBase {
  readonly kind: "DiagnosticsRejected";
  readonly phase:
    | "Configuration"
    | "Program"
    | "Syntactic"
    | "Binding"
    | "Global"
    | "Semantic";
  readonly diagnostics: readonly NormalizedDiagnostic[];
}

export interface UnsupportedApiOpenFailure extends OpenFailureBase {
  readonly kind: "UnsupportedApi";
  readonly reason:
    | "UnsupportedVersion"
    | "UnsupportedApiValue"
    | "UnsupportedResponseShape";
  readonly detail: string;
}

export interface InfrastructureOpenFailure extends OpenFailureBase {
  readonly kind: "InfrastructureFailed";
  readonly reason: "ProcessFailure" | "ProtocolFailure";
  readonly detail: string;
}

export type OpenTypeScriptSemanticFactsResult
  = OpenedSemanticFacts
    | InvalidInputOpenFailure
    | ProjectSelectionOpenFailure
    | DiagnosticsRejectedOpenFailure
    | UnsupportedApiOpenFailure
    | InfrastructureOpenFailure;

export interface TypeScriptSemanticFactsSession {
  readonly configFileName: string;
  getSourceFiles(): QueryResult<readonly SourceFileFact[]>;
  getSourceFile(handle: SemanticHandle): QueryResult<SourceFileFact>;
  getNodes(sourceFile: SemanticHandle): QueryResult<readonly NodeFact[]>;
  getNode(handle: SemanticHandle): QueryResult<NodeFact>;
  correlateNode(
    sourceFile: SemanticHandle,
    coordinate: CoordinateQuery,
  ): QueryResult<NodeFact>;
  getSymbol(handle: SemanticHandle): QueryResult<SymbolFact>;
  getSymbolAtNode(node: SemanticHandle): QueryResult<SymbolFact>;
  getSourceSymbol(node: SemanticHandle): QueryResult<SymbolFact>;
  getDeclaration(handle: SemanticHandle): QueryResult<DeclarationFact>;
  getAliasChain(symbol: SemanticHandle): QueryResult<AliasChainFact>;
  getType(handle: SemanticHandle): QueryResult<TypeFact>;
  getTypeAtNode(node: SemanticHandle): QueryResult<TypeFact>;
  getContextualType(node: SemanticHandle): QueryResult<TypeFact>;
  getDeclaredType(symbol: SemanticHandle): QueryResult<TypeFact>;
  getSymbolValueType(symbol: SemanticHandle): QueryResult<TypeFact>;
  getSymbolTypeAtLocation(
    symbol: SemanticHandle,
    location: SemanticHandle,
  ): QueryResult<TypeFact>;
  getUnionConstituents(type: SemanticHandle): QueryResult<readonly TypeFact[]>;
  getIntersectionConstituents(type: SemanticHandle): QueryResult<readonly TypeFact[]>;
  getBaseTypes(type: SemanticHandle): QueryResult<readonly TypeFact[]>;
  getTypeArguments(type: SemanticHandle): QueryResult<readonly TypeFact[]>;
  getApparentType(type: SemanticHandle): QueryResult<TypeFact>;
  getWidenedType(type: SemanticHandle): QueryResult<TypeFact>;
  getNonNullableType(type: SemanticHandle): QueryResult<TypeFact>;
  getLiteralBaseType(type: SemanticHandle): QueryResult<TypeFact>;
  getBaseConstraint(type: SemanticHandle): QueryResult<TypeFact>;
  getProperties(type: SemanticHandle): QueryResult<readonly SymbolFact[]>;
  getProperty(type: SemanticHandle, name: string): QueryResult<SymbolFact>;
  getIndexInfos(type: SemanticHandle): QueryResult<readonly IndexInfoFact[]>;
  getCallSignatures(type: SemanticHandle): QueryResult<readonly SignatureFact[]>;
  getConstructSignatures(type: SemanticHandle): QueryResult<readonly SignatureFact[]>;
  getSignature(handle: SemanticHandle): QueryResult<SignatureFact>;
  getResolvedSignature(node: SemanticHandle): QueryResult<SignatureFact>;
  getSignatureParameterType(
    signature: SemanticHandle,
    index: number,
  ): QueryResult<TypeFact>;
  getSignatureTarget(signature: SemanticHandle): QueryResult<SignatureFact>;
  getModuleSymbol(node: SemanticHandle): QueryResult<SymbolFact>;
  getModuleExports(symbol: SemanticHandle): QueryResult<readonly SymbolFact[]>;
  getModuleExport(symbol: SemanticHandle, name: string): QueryResult<SymbolFact>;
  getConstantValue(node: SemanticHandle): QueryResult<string | number>;
  dispose(): DisposeResult;
}

export type CategorizedTypeFact<Category extends TypeCategory> = TypeFact & {
  readonly category: Category;
};

export function isUnionTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"Union"> {
  return fact.category === TypeCategory.Union;
}

export function isIntersectionTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"Intersection"> {
  return fact.category === TypeCategory.Intersection;
}

export function isObjectTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"Object"> {
  return fact.category === TypeCategory.Object;
}

export function isClassOrInterfaceTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"Object"> {
  return isObjectTypeFact(fact)
    && (fact.objectCategories.includes(ObjectTypeCategory.Class)
      || fact.objectCategories.includes(ObjectTypeCategory.Interface));
}

export function isIntrinsicTypeFact(fact: TypeFact): boolean {
  return fact.intrinsicName !== undefined;
}

export function isErrorTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"Error"> {
  return fact.category === TypeCategory.Error;
}

export function isLiteralTypeFact(fact: TypeFact): boolean {
  return fact.category === TypeCategory.StringLiteral
    || fact.category === TypeCategory.NumberLiteral
    || fact.category === TypeCategory.BigIntLiteral
    || fact.category === TypeCategory.BooleanLiteral;
}

export function isStringLiteralTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"StringLiteral"> {
  return fact.category === TypeCategory.StringLiteral;
}

export function isNumberLiteralTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"NumberLiteral"> {
  return fact.category === TypeCategory.NumberLiteral;
}

export function isBigIntLiteralTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"BigIntLiteral"> {
  return fact.category === TypeCategory.BigIntLiteral;
}

export function isBooleanLiteralTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"BooleanLiteral"> {
  return fact.category === TypeCategory.BooleanLiteral;
}

export function isTypeReferenceTypeFact(fact: TypeFact): boolean {
  return isObjectTypeFact(fact)
    && fact.objectCategories.includes(ObjectTypeCategory.Reference);
}

export function isTupleTypeFact(fact: TypeFact): boolean {
  return isObjectTypeFact(fact)
    && fact.objectCategories.includes(ObjectTypeCategory.Tuple);
}

export function isIndexTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"Index"> {
  return fact.category === TypeCategory.Index;
}

export function isIndexedAccessTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"IndexedAccess"> {
  return fact.category === TypeCategory.IndexedAccess;
}

export function isConditionalTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"Conditional"> {
  return fact.category === TypeCategory.Conditional;
}

export function isSubstitutionTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"Substitution"> {
  return fact.category === TypeCategory.Substitution;
}

export function isTemplateLiteralTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"TemplateLiteral"> {
  return fact.category === TypeCategory.TemplateLiteral;
}

export function isStringMappingTypeFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"StringMapping"> {
  return fact.category === TypeCategory.StringMapping;
}

export function isTypeParameterFact(
  fact: TypeFact,
): fact is CategorizedTypeFact<"TypeParameter"> {
  return fact.category === TypeCategory.TypeParameter;
}

export type InfrastructureFailureReason = "ProcessFailure" | "ProtocolFailure";

export interface SemanticFactsTestFaults {
  readonly openFailure?: {
    readonly at: "BeforeApi" | "AfterSnapshot";
    readonly reason: InfrastructureFailureReason;
    readonly detail: string;
  };
  readonly projectSelection?: "NoProject" | "MultipleProjects" | "RequestedProjectMismatch";
  readonly unsupportedResponseShapeAfterSnapshot?: string;
  readonly snapshotReleaseFailure?: string;
  readonly queryFailure?: {
    readonly operation: string;
    readonly reason: InfrastructureFailureReason;
    readonly detail: string;
  };
  readonly unknownAliasedSymbol?: boolean;
  readonly errorType?: boolean;
  readonly unsupportedNodeKind?: boolean;
  readonly unsupportedSymbolFlags?: boolean;
  readonly unsupportedTypeValue?: boolean;
  readonly unsupportedSignatureValue?: boolean;
  readonly missingApiFactOperation?: string;
  readonly unsupportedResponseShapeOperation?: string;
}

export interface SemanticFactsTestObservation {
  readonly apiCreated: number;
  readonly snapshotCreated: number;
  readonly snapshotDisposeCalls: number;
  readonly apiCloseCalls: number;
}

export interface SemanticFactsTestHarness {
  open(tsconfigPath: string): OpenTypeScriptSemanticFactsResult;
  observation(): SemanticFactsTestObservation;
}

interface MutableTestObservation {
  apiCreated: number;
  snapshotCreated: number;
  snapshotDisposeCalls: number;
  apiCloseCalls: number;
}

interface OpenContext {
  readonly faults: SemanticFactsTestFaults;
  readonly observation: MutableTestObservation;
}

function createOpenContext(faults: SemanticFactsTestFaults = {}): OpenContext {
  return {
    faults: Object.freeze({ ...faults }),
    observation: {
      apiCreated: 0,
      snapshotCreated: 0,
      snapshotDisposeCalls: 0,
      apiCloseCalls: 0,
    },
  };
}

export const semanticFactsTestSeam = Object.freeze({
  createHarness(faults: SemanticFactsTestFaults): SemanticFactsTestHarness {
    const context = createOpenContext(faults);
    return Object.freeze({
      open(tsconfigPath: string) {
        return openTypeScriptSemanticFactsCore(tsconfigPath, context);
      },
      observation() {
        return Object.freeze({ ...context.observation });
      },
    });
  },
});

class OpenFailure extends Error {
  readonly failure:
    | Omit<ProjectSelectionOpenFailure, "cleanupFailures">
    | Omit<DiagnosticsRejectedOpenFailure, "cleanupFailures">
    | Omit<UnsupportedApiOpenFailure, "cleanupFailures">
    | Omit<InfrastructureOpenFailure, "cleanupFailures">;

  constructor(
    failure:
      | Omit<ProjectSelectionOpenFailure, "cleanupFailures">
      | Omit<DiagnosticsRejectedOpenFailure, "cleanupFailures">
      | Omit<UnsupportedApiOpenFailure, "cleanupFailures">
      | Omit<InfrastructureOpenFailure, "cleanupFailures">,
  ) {
    super(failure.kind);
    this.failure = failure;
  }
}

class QueryFailure extends Error {
  readonly result: QueryResult<never>;

  constructor(result: QueryResult<never>) {
    super(result.kind);
    this.result = result;
  }
}

class CompatibilityFailure extends Error {
  readonly reason: "UnsupportedApiValue" | "UnsupportedResponseShape";

  constructor(
    reason: "UnsupportedApiValue" | "UnsupportedResponseShape",
    detail: string,
  ) {
    super(detail);
    this.reason = reason;
  }
}

function resolved<T>(value: T): QueryResult<T> {
  return Object.freeze({ kind: "Resolved", value });
}

function absent<T>(reason: string): QueryResult<T> {
  return Object.freeze({ kind: "Absent", reason });
}

function unavailable<T>(
  reason: "MissingApiFact" | "UnknownSymbol" | "UnsupportedApiValue" | "UnsupportedResponseShape",
  detail: string,
): QueryResult<T> {
  return Object.freeze({ kind: "Unavailable", reason, detail });
}

function notApplicable<T>(expectedSubject: string, actualSubject: string): QueryResult<T> {
  return Object.freeze({
    kind: "NotApplicable",
    expectedSubject,
    actualSubject,
  });
}

function normalizedDetail(error: unknown): string {
  if (error instanceof Error) {
    return error.message || error.name;
  }
  return String(error);
}

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null;
}

function classifyInfrastructureFailure(error: unknown): InfrastructureFailureReason {
  const detail = normalizedDetail(error).toLowerCase();
  const code = isRecord(error) && typeof error.code === "string"
    ? error.code.toLowerCase()
    : "";
  const syscall = isRecord(error) && typeof error.syscall === "string"
    ? error.syscall.toLowerCase()
    : "";
  if (
    syscall.startsWith("spawn")
    || syscall.startsWith("kill")
    || code === "epipe"
    || code === "econnreset"
    || code === "err_ipc_channel_closed"
    || detail.includes("spawn")
    || detail.includes("child process")
    || detail.includes("process exited")
    || detail.includes("broken pipe")
  ) {
    return "ProcessFailure";
  }
  return "ProtocolFailure";
}

function normalizedPath(path: string): string {
  const result = normalize(resolve(path));
  return process.platform === "win32" ? result.toLowerCase() : result;
}

function portablePath(path: string): string {
  return path.split(sep).join("/");
}

function compareText(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

function readPinnedTypeScriptVersion(): string {
  const require = createRequire(import.meta.url);
  const packagePath = require.resolve("typescript/package.json");
  const parsed: unknown = JSON.parse(readFileSync(packagePath, "utf8"));
  if (!isRecord(parsed) || typeof parsed.version !== "string") {
    throw new CompatibilityFailure(
      "UnsupportedResponseShape",
      "typescript/package.json does not contain a string version",
    );
  }
  return parsed.version;
}

function diagnosticCategory(category: DiagnosticCategory): DiagnosticCategoryName {
  switch (category) {
    case DiagnosticCategory.Warning:
      return DiagnosticCategoryName.Warning;
    case DiagnosticCategory.Error:
      return DiagnosticCategoryName.Error;
    case DiagnosticCategory.Suggestion:
      return DiagnosticCategoryName.Suggestion;
    case DiagnosticCategory.Message:
      return DiagnosticCategoryName.Message;
    default:
      throw new CompatibilityFailure(
        "UnsupportedApiValue",
        `unsupported diagnostic category ${String(category)}`,
      );
  }
}

function normalizeDiagnostic(diagnostic: TypeScriptDiagnostic): NormalizedDiagnostic {
  return Object.freeze({
    fileName: diagnostic.fileName === undefined
      ? undefined
      : portablePath(normalize(diagnostic.fileName)),
    start: diagnostic.pos,
    length: diagnostic.end - diagnostic.pos,
    code: diagnostic.code,
    category: diagnosticCategory(diagnostic.category),
    text: diagnostic.text,
  });
}

function sourceContentIdsEqual(left: SourceContentId, right: SourceContentId): boolean {
  return left.algorithm === right.algorithm
    && left.encoding === right.encoding
    && left.hex === right.hex;
}

function cleanup(
  api: API | undefined,
  snapshot: Snapshot | undefined,
  context: OpenContext,
): readonly CleanupFailure[] {
  const failures: CleanupFailure[] = [];
  try {
    if (snapshot !== undefined) {
      context.observation.snapshotDisposeCalls += 1;
      try {
        snapshot.dispose();
        if (context.faults.snapshotReleaseFailure !== undefined) {
          throw new Error(context.faults.snapshotReleaseFailure);
        }
      } catch (error) {
        failures.push(Object.freeze({
          kind: "SnapshotReleaseFailure",
          detail: normalizedDetail(error),
        }));
      }
    }
  } finally {
    if (api !== undefined) {
      context.observation.apiCloseCalls += 1;
      api.close();
    }
  }
  return Object.freeze(failures);
}

function invalidInput(
  reason: InvalidInputOpenFailure["reason"],
): InvalidInputOpenFailure {
  return Object.freeze({ kind: "InvalidInput", reason, cleanupFailures: Object.freeze([]) });
}

export function openTypeScriptSemanticFacts(
  tsconfigPath: string,
): OpenTypeScriptSemanticFactsResult {
  return openTypeScriptSemanticFactsCore(tsconfigPath, createOpenContext());
}

function openTypeScriptSemanticFactsCore(
  tsconfigPath: string,
  context: OpenContext,
): OpenTypeScriptSemanticFactsResult {
  if (tsconfigPath.slice(0, 5).toLowerCase() === "file:") {
    return invalidInput("FileUrl");
  }
  if (!isAbsolute(tsconfigPath)) {
    return invalidInput("RelativePath");
  }
  try {
    if (statSync(tsconfigPath).isDirectory()) {
      return invalidInput("DirectoryPath");
    }
  } catch (error) {
    if (isRecord(error) && error.code === "ENOENT") {
      return invalidInput("MissingPath");
    }
    return Object.freeze({
      kind: "InfrastructureFailed",
      reason: "ProtocolFailure",
      detail: `input validation failed: ${normalizedDetail(error)}`,
      cleanupFailures: Object.freeze([]),
    });
  }

  const requestedPath = normalizedPath(tsconfigPath);
  let api: API | undefined;
  let snapshot: Snapshot | undefined;
  try {
    const actualVersion = readPinnedTypeScriptVersion();
    if (actualVersion !== pinnedTypeScriptVersion) {
      throw new OpenFailure({
        kind: "UnsupportedApi",
        reason: "UnsupportedVersion",
        detail: `expected TypeScript ${pinnedTypeScriptVersion}, found ${actualVersion}`,
      });
    }
    if (context.faults.openFailure?.at === "BeforeApi") {
      throw new OpenFailure({
        kind: "InfrastructureFailed",
        reason: context.faults.openFailure.reason,
        detail: context.faults.openFailure.detail,
      });
    }

    api = new API();
    context.observation.apiCreated += 1;
    snapshot = api.updateSnapshot({ openProjects: [requestedPath] });
    context.observation.snapshotCreated += 1;

    if (context.faults.openFailure?.at === "AfterSnapshot") {
      throw new OpenFailure({
        kind: "InfrastructureFailed",
        reason: context.faults.openFailure.reason,
        detail: context.faults.openFailure.detail,
      });
    }
    if (context.faults.unsupportedResponseShapeAfterSnapshot !== undefined) {
      throw new OpenFailure({
        kind: "UnsupportedApi",
        reason: "UnsupportedResponseShape",
        detail: context.faults.unsupportedResponseShapeAfterSnapshot,
      });
    }

    const projects = snapshot.getProjects();
    const candidates = Object.freeze(
      projects.map(project => normalizedPath(project.configFileName)).sort(),
    );
    const selectionFault = context.faults.projectSelection;
    if (selectionFault === "NoProject" || projects.length === 0) {
      throw new OpenFailure({
        kind: "ProjectSelectionFailed",
        reason: "NoProject",
        candidates,
      });
    }
    if (selectionFault === "MultipleProjects" || projects.length > 1) {
      throw new OpenFailure({
        kind: "ProjectSelectionFailed",
        reason: "MultipleProjects",
        candidates,
      });
    }
    const project = projects[0];
    if (project === undefined) {
      throw new CompatibilityFailure(
        "UnsupportedResponseShape",
        "snapshot returned one project count without a project value",
      );
    }
    if (
      selectionFault === "RequestedProjectMismatch"
      || normalizedPath(project.configFileName) !== requestedPath
    ) {
      throw new OpenFailure({
        kind: "ProjectSelectionFailed",
        reason: "RequestedProjectMismatch",
        candidates,
      });
    }
    const selected = snapshot.getProject(requestedPath);
    if (selected !== project) {
      throw new CompatibilityFailure(
        "UnsupportedResponseShape",
        "Snapshot.getProject did not return the selected project object",
      );
    }

    validateDiagnostics(project);
    validateRoots(project);
    const session = new SemanticFactsSession(api, snapshot, project, context);
    api = undefined;
    snapshot = undefined;
    return Object.freeze({ kind: "Opened", session });
  } catch (error) {
    const cleanupFailures = cleanup(api, snapshot, context);
    if (error instanceof OpenFailure) {
      return Object.freeze({ ...error.failure, cleanupFailures });
    }
    if (error instanceof CompatibilityFailure) {
      return Object.freeze({
        kind: "UnsupportedApi",
        reason: error.reason,
        detail: error.message,
        cleanupFailures,
      });
    }
    return Object.freeze({
      kind: "InfrastructureFailed",
      reason: classifyInfrastructureFailure(error),
      detail: normalizedDetail(error),
      cleanupFailures,
    });
  }
}

function validateDiagnostics(project: Project): void {
  const phases = [
    ["Configuration", () => project.program.getConfigFileParsingDiagnostics()],
    ["Program", () => project.program.getProgramDiagnostics()],
    ["Syntactic", () => project.program.getSyntacticDiagnostics()],
    ["Binding", () => project.program.getBindDiagnostics()],
    ["Global", () => project.program.getGlobalDiagnostics()],
    ["Semantic", () => project.program.getSemanticDiagnostics()],
  ] as const;

  for (const [phase, read] of phases) {
    const diagnostics = read();
    if (diagnostics.length !== 0) {
      throw new OpenFailure({
        kind: "DiagnosticsRejected",
        phase,
        diagnostics: Object.freeze(diagnostics.map(normalizeDiagnostic)),
      });
    }
  }
}

function validateRoots(project: Project): void {
  for (const root of project.rootFiles) {
    if (project.program.getSourceFile(root) === undefined) {
      throw new CompatibilityFailure(
        "UnsupportedResponseShape",
        `project root '${portablePath(root)}' is absent from its program`,
      );
    }
  }
}

const symbolCategoryEntries = [
  [SymbolFlags.FunctionScopedVariable, SymbolCategory.FunctionScopedVariable],
  [SymbolFlags.BlockScopedVariable, SymbolCategory.BlockScopedVariable],
  [SymbolFlags.Property, SymbolCategory.Property],
  [SymbolFlags.EnumMember, SymbolCategory.EnumMember],
  [SymbolFlags.Function, SymbolCategory.Function],
  [SymbolFlags.Class, SymbolCategory.Class],
  [SymbolFlags.Interface, SymbolCategory.Interface],
  [SymbolFlags.ConstEnum, SymbolCategory.ConstEnum],
  [SymbolFlags.RegularEnum, SymbolCategory.RegularEnum],
  [SymbolFlags.ValueModule, SymbolCategory.ValueModule],
  [SymbolFlags.NamespaceModule, SymbolCategory.NamespaceModule],
  [SymbolFlags.TypeLiteral, SymbolCategory.TypeLiteral],
  [SymbolFlags.ObjectLiteral, SymbolCategory.ObjectLiteral],
  [SymbolFlags.Method, SymbolCategory.Method],
  [SymbolFlags.Constructor, SymbolCategory.Constructor],
  [SymbolFlags.GetAccessor, SymbolCategory.GetAccessor],
  [SymbolFlags.SetAccessor, SymbolCategory.SetAccessor],
  [SymbolFlags.Signature, SymbolCategory.Signature],
  [SymbolFlags.TypeParameter, SymbolCategory.TypeParameter],
  [SymbolFlags.TypeAlias, SymbolCategory.TypeAlias],
  [SymbolFlags.ExportValue, SymbolCategory.ExportValue],
  [SymbolFlags.Alias, SymbolCategory.Alias],
  [SymbolFlags.Prototype, SymbolCategory.Prototype],
  [SymbolFlags.ExportStar, SymbolCategory.ExportStar],
  [SymbolFlags.Optional, SymbolCategory.Optional],
  [SymbolFlags.Transient, SymbolCategory.Transient],
  [SymbolFlags.Assignment, SymbolCategory.Assignment],
  [SymbolFlags.ModuleExports, SymbolCategory.ModuleExports],
  [SymbolFlags.ConstEnumOnlyModule, SymbolCategory.ConstEnumOnlyModule],
  [SymbolFlags.ReplaceableByMethod, SymbolCategory.ReplaceableByMethod],
  [SymbolFlags.GlobalLookup, SymbolCategory.GlobalLookup],
] as const;

const knownSymbolFlags = symbolCategoryEntries.reduce(
  (flags, [flag]) => flags | flag,
  SymbolFlags.None,
);

const objectCategoryEntries = [
  [ObjectFlags.Class, ObjectTypeCategory.Class],
  [ObjectFlags.Interface, ObjectTypeCategory.Interface],
  [ObjectFlags.Reference, ObjectTypeCategory.Reference],
  [ObjectFlags.Tuple, ObjectTypeCategory.Tuple],
  [ObjectFlags.Anonymous, ObjectTypeCategory.Anonymous],
  [ObjectFlags.Mapped, ObjectTypeCategory.Mapped],
  [ObjectFlags.Instantiated, ObjectTypeCategory.Instantiated],
  [ObjectFlags.ObjectLiteral, ObjectTypeCategory.ObjectLiteral],
  [ObjectFlags.EvolvingArray, ObjectTypeCategory.EvolvingArray],
  [ObjectFlags.ReverseMapped, ObjectTypeCategory.ReverseMapped],
  [ObjectFlags.JsxAttributes, ObjectTypeCategory.JsxAttributes],
  [ObjectFlags.FreshLiteral, ObjectTypeCategory.FreshLiteral],
  [ObjectFlags.ArrayLiteral, ObjectTypeCategory.ArrayLiteral],
] as const;

function rawSyntaxName(kind: SyntaxKind): string {
  const value: number = kind;
  const count: number = SyntaxKind.Count;
  if (!Number.isInteger(value) || value < 0 || value >= count) {
    throw new CompatibilityFailure(
      "UnsupportedApiValue",
      `unsupported syntax kind ${String(kind)}`,
    );
  }
  const name = SyntaxKind[value];
  if (typeof name !== "string") {
    throw new CompatibilityFailure(
      "UnsupportedApiValue",
      `syntax kind ${String(kind)} has no stable name`,
    );
  }
  return name;
}

function nodeKind(node: TypeScriptNode): NodeKind {
  rawSyntaxName(node.kind);
  switch (node.kind) {
    case SyntaxKind.SourceFile:
      return NodeKind.SourceFile;
    case SyntaxKind.Identifier:
      return NodeKind.Identifier;
    case SyntaxKind.StringLiteral:
      return NodeKind.StringLiteral;
    case SyntaxKind.NumericLiteral:
      return NodeKind.NumericLiteral;
    case SyntaxKind.BigIntLiteral:
      return NodeKind.BigIntLiteral;
    case SyntaxKind.RegularExpressionLiteral:
      return NodeKind.RegularExpressionLiteral;
    case SyntaxKind.NoSubstitutionTemplateLiteral:
      return NodeKind.NoSubstitutionTemplateLiteral;
    case SyntaxKind.VariableDeclaration:
      return NodeKind.VariableDeclaration;
    case SyntaxKind.Parameter:
      return NodeKind.Parameter;
    case SyntaxKind.FunctionDeclaration:
      return NodeKind.FunctionDeclaration;
    case SyntaxKind.ArrowFunction:
      return NodeKind.ArrowFunction;
    case SyntaxKind.ClassDeclaration:
      return NodeKind.ClassDeclaration;
    case SyntaxKind.InterfaceDeclaration:
      return NodeKind.InterfaceDeclaration;
    case SyntaxKind.TypeAliasDeclaration:
      return NodeKind.TypeAliasDeclaration;
    case SyntaxKind.ModuleDeclaration:
      return NodeKind.ModuleDeclaration;
    case SyntaxKind.MethodDeclaration:
      return NodeKind.MethodDeclaration;
    case SyntaxKind.ImportDeclaration:
      return NodeKind.ImportDeclaration;
    case SyntaxKind.ExportDeclaration:
      return NodeKind.ExportDeclaration;
    case SyntaxKind.ImportSpecifier:
      return NodeKind.ImportSpecifier;
    case SyntaxKind.ExportSpecifier:
      return NodeKind.ExportSpecifier;
    case SyntaxKind.ShorthandPropertyAssignment:
      return NodeKind.ShorthandPropertyAssignment;
    case SyntaxKind.EnumMember:
      return NodeKind.EnumMember;
    case SyntaxKind.PropertyAccessExpression:
      return NodeKind.PropertyAccessExpression;
    case SyntaxKind.ElementAccessExpression:
      return NodeKind.ElementAccessExpression;
    case SyntaxKind.CallExpression:
      return NodeKind.CallExpression;
    case SyntaxKind.NewExpression:
      return NodeKind.NewExpression;
    default:
      if (isTypeNode(node)) {
        return NodeKind.TypeNode;
      }
      if (isExpression(node)) {
        return NodeKind.Expression;
      }
      if (isStatement(node)) {
        return NodeKind.Statement;
      }
      if (node.kind >= SyntaxKind.FirstToken && node.kind <= SyntaxKind.LastToken) {
        return NodeKind.Token;
      }
      if (node.kind >= SyntaxKind.FirstJSDocNode && node.kind <= SyntaxKind.LastJSDocNode) {
        return NodeKind.JsDoc;
      }
      return NodeKind.Other;
  }
}

function isDeclarationNode(node: TypeScriptNode): boolean {
  return isVariableDeclaration(node)
    || isParameterDeclaration(node)
    || isFunctionDeclaration(node)
    || isClassDeclaration(node)
    || isInterfaceDeclaration(node)
    || isTypeAliasDeclaration(node)
    || isModuleDeclaration(node)
    || isMethodDeclaration(node)
    || isImportDeclaration(node)
    || isExportDeclaration(node)
    || isImportSpecifier(node)
    || isExportSpecifier(node)
    || isShorthandPropertyAssignment(node)
    || isEnumMember(node)
    || node.kind === SyntaxKind.PropertyDeclaration
    || node.kind === SyntaxKind.PropertySignature
    || node.kind === SyntaxKind.MethodSignature
    || node.kind === SyntaxKind.Constructor
    || node.kind === SyntaxKind.GetAccessor
    || node.kind === SyntaxKind.SetAccessor
    || node.kind === SyntaxKind.CallSignature
    || node.kind === SyntaxKind.ConstructSignature
    || node.kind === SyntaxKind.IndexSignature
    || node.kind === SyntaxKind.FunctionExpression;
}

function typeCategory(type: TypeScriptType): TypeCategory {
  if (type.isErrorType()) {
    return TypeCategory.Error;
  }
  const categories = [
    [TypeFlags.Any, TypeCategory.Any],
    [TypeFlags.Unknown, TypeCategory.Unknown],
    [TypeFlags.Undefined, TypeCategory.Undefined],
    [TypeFlags.Null, TypeCategory.Null],
    [TypeFlags.Void, TypeCategory.Void],
    [TypeFlags.String, TypeCategory.String],
    [TypeFlags.Number, TypeCategory.Number],
    [TypeFlags.BigInt, TypeCategory.BigInt],
    [TypeFlags.Boolean, TypeCategory.Boolean],
    [TypeFlags.UniqueESSymbol, TypeCategory.UniqueESSymbol],
    [TypeFlags.EnumLiteral, TypeCategory.EnumLiteral],
    [TypeFlags.StringLiteral, TypeCategory.StringLiteral],
    [TypeFlags.NumberLiteral, TypeCategory.NumberLiteral],
    [TypeFlags.BigIntLiteral, TypeCategory.BigIntLiteral],
    [TypeFlags.BooleanLiteral, TypeCategory.BooleanLiteral],
    [TypeFlags.ESSymbol, TypeCategory.ESSymbol],
    [TypeFlags.Enum, TypeCategory.Enum],
    [TypeFlags.NonPrimitive, TypeCategory.NonPrimitive],
    [TypeFlags.Never, TypeCategory.Never],
    [TypeFlags.TypeParameter, TypeCategory.TypeParameter],
    [TypeFlags.Object, TypeCategory.Object],
    [TypeFlags.Index, TypeCategory.Index],
    [TypeFlags.TemplateLiteral, TypeCategory.TemplateLiteral],
    [TypeFlags.StringMapping, TypeCategory.StringMapping],
    [TypeFlags.Substitution, TypeCategory.Substitution],
    [TypeFlags.IndexedAccess, TypeCategory.IndexedAccess],
    [TypeFlags.Conditional, TypeCategory.Conditional],
    [TypeFlags.Union, TypeCategory.Union],
    [TypeFlags.Intersection, TypeCategory.Intersection],
  ] as const;
  for (const [flag, category] of categories) {
    if ((type.flags & flag) !== 0) {
      return category;
    }
  }
  throw new CompatibilityFailure(
    "UnsupportedApiValue",
    `unsupported type flags ${String(type.flags)}`,
  );
}

function symbolCategories(symbol: TypeScriptSymbol): readonly SymbolCategory[] {
  const unknownFlags = symbol.flags & ~knownSymbolFlags;
  if (unknownFlags !== 0) {
    throw new CompatibilityFailure(
      "UnsupportedApiValue",
      `unsupported symbol flags ${String(unknownFlags)}`,
    );
  }
  const categories = symbolCategoryEntries.flatMap(([flag, category]) =>
    (symbol.flags & flag) !== 0 ? [category] : []);
  return Object.freeze(categories);
}

function objectCategories(type: TypeScriptType): readonly ObjectTypeCategory[] {
  if (!type.isObjectType()) {
    return Object.freeze([]);
  }
  const categories = objectCategoryEntries.flatMap(([flag, category]) =>
    (type.objectFlags & flag) !== 0 ? [category] : []);
  if (type.isTypeReference() && !categories.includes(ObjectTypeCategory.Reference)) {
    categories.push(ObjectTypeCategory.Reference);
  }
  if (type.isTupleType() && !categories.includes(ObjectTypeCategory.Tuple)) {
    categories.push(ObjectTypeCategory.Tuple);
  }
  return Object.freeze(categories);
}

function typePredicateCategory(
  predicate: TypeScriptTypePredicate,
): TypePredicateCategory {
  const kind: number = predicate.kind;
  const thisKind: number = TypePredicateKind.This;
  const identifierKind: number = TypePredicateKind.Identifier;
  const assertsThisKind: number = TypePredicateKind.AssertsThis;
  const assertsIdentifierKind: number = TypePredicateKind.AssertsIdentifier;
  switch (kind) {
    case thisKind:
      return TypePredicateCategory.This;
    case identifierKind:
      return TypePredicateCategory.Identifier;
    case assertsThisKind:
      return TypePredicateCategory.AssertsThis;
    case assertsIdentifierKind:
      return TypePredicateCategory.AssertsIdentifier;
    default:
      throw new CompatibilityFailure(
        "UnsupportedApiValue",
        `unsupported type predicate kind ${String(kind)}`,
      );
  }
}

function isModuleSymbol(symbol: TypeScriptSymbol): boolean {
  return (symbol.flags & SymbolFlags.Module) !== 0;
}

interface SemanticFactsResources {
  readonly api: API;
  readonly snapshot: Snapshot;
  readonly project: Project;
  readonly program: Project["program"];
  readonly checker: Checker;
}

class SemanticFactsSession implements TypeScriptSemanticFactsSession {
  readonly configFileName: string;
  readonly #sessionIdentity = Object.freeze({});
  #resources: SemanticFactsResources | undefined;
  readonly #projectDirectory: string;
  readonly #context: OpenContext;
  readonly #rootFiles: ReadonlySet<string>;
  readonly #sourceByHandle = new Map<SourceFileHandle, TypeScriptSourceFile>();
  readonly #sourceHandleByRaw = new WeakMap<TypeScriptSourceFile, SourceFileHandle>();
  readonly #sourceFacts = new Map<SourceFileHandle, SourceFileFact>();
  readonly #nodeByHandle = new Map<NodeHandle, TypeScriptNode>();
  readonly #nodeHandleByRaw = new WeakMap<TypeScriptNode, NodeHandle>();
  readonly #nodeFacts = new Map<NodeHandle, NodeFact>();
  readonly #declarationByHandle = new Map<DeclarationHandle, TypeScriptNode>();
  readonly #declarationHandleByRaw = new WeakMap<TypeScriptNode, DeclarationHandle>();
  readonly #declarationFacts = new Map<DeclarationHandle, DeclarationFact>();
  readonly #symbolByHandle = new Map<SymbolHandle, TypeScriptSymbol>();
  readonly #symbolHandleByRaw = new WeakMap<TypeScriptSymbol, SymbolHandle>();
  readonly #symbolFacts = new Map<SymbolHandle, SymbolFact>();
  readonly #typeByHandle = new Map<TypeHandle, TypeScriptType>();
  readonly #typeHandleByRaw = new WeakMap<TypeScriptType, TypeHandle>();
  readonly #typeFacts = new Map<TypeHandle, TypeFact>();
  readonly #signatureByHandle = new Map<SignatureHandle, TypeScriptSignature>();
  readonly #signatureHandleByRaw = new WeakMap<TypeScriptSignature, SignatureHandle>();
  readonly #signatureFacts = new Map<SignatureHandle, SignatureFact>();
  readonly #orderedSourceFacts: readonly SourceFileFact[];
  #disposeResult: DisposeResult | undefined;
  #poisoned: Extract<QueryResult<never>, { readonly kind: "SessionFailure" }> | undefined;
  #queryFaultRaised = false;
  #errorTypeFaultRaised = false;
  #unsupportedNodeFaultRaised = false;
  #unsupportedSymbolFaultRaised = false;
  #unsupportedTypeFaultRaised = false;
  #unsupportedSignatureFaultRaised = false;

  constructor(api: API, snapshot: Snapshot, project: Project, context: OpenContext) {
    this.configFileName = normalizedPath(project.configFileName);
    this.#resources = Object.freeze({
      api,
      snapshot,
      project,
      program: project.program,
      checker: project.checker,
    });
    this.#projectDirectory = dirname(this.configFileName);
    this.#context = context;
    this.#rootFiles = new Set(project.rootFiles.map(normalizedPath));
    this.#orderedSourceFacts = this.#initializeSourceFacts();
    Object.freeze(this);
  }

  get #api(): API {
    return this.#activeResources().api;
  }

  get #snapshot(): Snapshot {
    return this.#activeResources().snapshot;
  }

  get #project(): Project {
    return this.#activeResources().project;
  }

  get #program(): Project["program"] {
    return this.#activeResources().program;
  }

  get #checker(): Checker {
    return this.#activeResources().checker;
  }

  #activeResources(): SemanticFactsResources {
    const resources = this.#resources;
    if (resources === undefined) {
      throw new Error("semantic-facts resources were accessed after disposal");
    }
    return resources;
  }

  #initializeSourceFacts(): readonly SourceFileFact[] {
    const facts = this.#program.getSourceFileNames().map(fileName => {
      const source = this.#program.getSourceFile(fileName);
      if (source === undefined) {
        throw new CompatibilityFailure(
          "UnsupportedResponseShape",
          `program source '${portablePath(fileName)}' could not be resolved`,
        );
      }
      const handle = this.#registerSource(source);
      return this.#sourceFacts.get(handle) ?? this.#createSourceFact(handle, source);
    });
    return Object.freeze(facts.sort((left, right) => {
      const leftKey = `${left.path.path}:${left.classification}`;
      const rightKey = `${right.path.path}:${right.classification}`;
      return compareText(leftKey, rightKey);
    }));
  }

  #registerSource(source: TypeScriptSourceFile): SourceFileHandle {
    const existing = this.#sourceHandleByRaw.get(source);
    if (existing !== undefined) {
      return existing;
    }
    const handle = issueHandle(
      () => new SourceFileHandle(handleConstructionToken),
      this.#sessionIdentity,
      "SourceFile",
      source,
    );
    this.#sourceHandleByRaw.set(source, handle);
    this.#sourceByHandle.set(handle, source);
    return handle;
  }

  #registerNode(node: TypeScriptNode): NodeHandle {
    const existing = this.#nodeHandleByRaw.get(node);
    if (existing !== undefined) {
      return existing;
    }
    const handle = issueHandle(
      () => new NodeHandle(handleConstructionToken),
      this.#sessionIdentity,
      "Node",
      node,
    );
    this.#nodeHandleByRaw.set(node, handle);
    this.#nodeByHandle.set(handle, node);
    return handle;
  }

  #registerDeclaration(node: TypeScriptNode): DeclarationHandle {
    const existing = this.#declarationHandleByRaw.get(node);
    if (existing !== undefined) {
      return existing;
    }
    const handle = issueHandle(
      () => new DeclarationHandle(handleConstructionToken),
      this.#sessionIdentity,
      "Declaration",
      node,
    );
    this.#declarationHandleByRaw.set(node, handle);
    this.#declarationByHandle.set(handle, node);
    return handle;
  }

  #registerSymbol(symbol: TypeScriptSymbol): SymbolHandle {
    const existing = this.#symbolHandleByRaw.get(symbol);
    if (existing !== undefined) {
      return existing;
    }
    const handle = issueHandle(
      () => new SymbolHandle(handleConstructionToken),
      this.#sessionIdentity,
      "Symbol",
      symbol,
    );
    this.#symbolHandleByRaw.set(symbol, handle);
    this.#symbolByHandle.set(handle, symbol);
    return handle;
  }

  #registerType(type: TypeScriptType): TypeHandle {
    const existing = this.#typeHandleByRaw.get(type);
    if (existing !== undefined) {
      return existing;
    }
    const handle = issueHandle(
      () => new TypeHandle(handleConstructionToken),
      this.#sessionIdentity,
      "Type",
      type,
    );
    this.#typeHandleByRaw.set(type, handle);
    this.#typeByHandle.set(handle, type);
    return handle;
  }

  #registerSignature(signature: TypeScriptSignature): SignatureHandle {
    const existing = this.#signatureHandleByRaw.get(signature);
    if (existing !== undefined) {
      return existing;
    }
    const handle = issueHandle(
      () => new SignatureHandle(handleConstructionToken),
      this.#sessionIdentity,
      "Signature",
      signature,
    );
    this.#signatureHandleByRaw.set(signature, handle);
    this.#signatureByHandle.set(handle, signature);
    return handle;
  }

  #requireDescriptor(handle: unknown, expectedKind: HandleKind): HandleDescriptor {
    if (typeof handle !== "object" || handle === null) {
      throw new QueryFailure(Object.freeze({
        kind: "InvalidHandle",
        reason: "StaleSession",
      }));
    }
    const descriptor = handleDescriptors.get(handle);
    if (descriptor === undefined || descriptor.session !== this.#sessionIdentity) {
      throw new QueryFailure(Object.freeze({
        kind: "InvalidHandle",
        reason: "StaleSession",
      }));
    }
    if (descriptor.kind !== expectedKind) {
      throw new QueryFailure(Object.freeze({
        kind: "InvalidHandle",
        reason: "WrongKind",
      }));
    }
    return descriptor;
  }

  #requireSource(handle: unknown): TypeScriptSourceFile {
    const descriptor = this.#requireDescriptor(handle, "SourceFile");
    if (!(handle instanceof SourceFileHandle)) {
      throw new QueryFailure(Object.freeze({ kind: "InvalidHandle", reason: "WrongKind" }));
    }
    const raw = this.#sourceByHandle.get(handle);
    if (raw === undefined || rawObjectIdentities.get(raw) !== descriptor.identity) {
      throw new QueryFailure(Object.freeze({
        kind: "InvalidHandle",
        reason: "StaleSession",
      }));
    }
    return raw;
  }

  #requireNode(handle: unknown): TypeScriptNode {
    const descriptor = this.#requireDescriptor(handle, "Node");
    if (!(handle instanceof NodeHandle)) {
      throw new QueryFailure(Object.freeze({ kind: "InvalidHandle", reason: "WrongKind" }));
    }
    const raw = this.#nodeByHandle.get(handle);
    if (raw === undefined || rawObjectIdentities.get(raw) !== descriptor.identity) {
      throw new QueryFailure(Object.freeze({ kind: "InvalidHandle", reason: "StaleSession" }));
    }
    return raw;
  }

  #requireDeclaration(handle: unknown): TypeScriptNode {
    const descriptor = this.#requireDescriptor(handle, "Declaration");
    if (!(handle instanceof DeclarationHandle)) {
      throw new QueryFailure(Object.freeze({ kind: "InvalidHandle", reason: "WrongKind" }));
    }
    const raw = this.#declarationByHandle.get(handle);
    if (raw === undefined || rawObjectIdentities.get(raw) !== descriptor.identity) {
      throw new QueryFailure(Object.freeze({ kind: "InvalidHandle", reason: "StaleSession" }));
    }
    return raw;
  }

  #requireSymbol(handle: unknown): TypeScriptSymbol {
    const descriptor = this.#requireDescriptor(handle, "Symbol");
    if (!(handle instanceof SymbolHandle)) {
      throw new QueryFailure(Object.freeze({ kind: "InvalidHandle", reason: "WrongKind" }));
    }
    const raw = this.#symbolByHandle.get(handle);
    if (raw === undefined || rawObjectIdentities.get(raw) !== descriptor.identity) {
      throw new QueryFailure(Object.freeze({ kind: "InvalidHandle", reason: "StaleSession" }));
    }
    return raw;
  }

  #requireType(handle: unknown): TypeScriptType {
    const descriptor = this.#requireDescriptor(handle, "Type");
    if (!(handle instanceof TypeHandle)) {
      throw new QueryFailure(Object.freeze({ kind: "InvalidHandle", reason: "WrongKind" }));
    }
    const raw = this.#typeByHandle.get(handle);
    if (raw === undefined || rawObjectIdentities.get(raw) !== descriptor.identity) {
      throw new QueryFailure(Object.freeze({ kind: "InvalidHandle", reason: "StaleSession" }));
    }
    return raw;
  }

  #requireSignature(handle: unknown): TypeScriptSignature {
    const descriptor = this.#requireDescriptor(handle, "Signature");
    if (!(handle instanceof SignatureHandle)) {
      throw new QueryFailure(Object.freeze({ kind: "InvalidHandle", reason: "WrongKind" }));
    }
    const raw = this.#signatureByHandle.get(handle);
    if (raw === undefined || rawObjectIdentities.get(raw) !== descriptor.identity) {
      throw new QueryFailure(Object.freeze({ kind: "InvalidHandle", reason: "StaleSession" }));
    }
    return raw;
  }

  #run<T>(operation: string, query: () => QueryResult<T>): QueryResult<T> {
    if (this.#disposeResult !== undefined) {
      return Object.freeze({
        kind: "SessionFailure",
        reason: "SessionDisposed",
        detail: "the semantic-facts session has been disposed",
      });
    }
    if (this.#poisoned !== undefined) {
      return this.#poisoned;
    }
    const fault = this.#context.faults.queryFailure;
    if (!this.#queryFaultRaised && fault?.operation === operation) {
      this.#queryFaultRaised = true;
      this.#poisoned = Object.freeze({
        kind: "SessionFailure",
        reason: fault.reason,
        detail: fault.detail,
      });
      return this.#poisoned;
    }
    if (this.#context.faults.missingApiFactOperation === operation) {
      return unavailable("MissingApiFact", `injected missing API fact for ${operation}`);
    }
    if (this.#context.faults.unsupportedResponseShapeOperation === operation) {
      return unavailable(
        "UnsupportedResponseShape",
        `injected unsupported response shape for ${operation}`,
      );
    }
    try {
      return query();
    } catch (error) {
      if (error instanceof QueryFailure) {
        return error.result;
      }
      if (error instanceof CompatibilityFailure) {
        return unavailable(error.reason, error.message);
      }
      this.#poisoned = Object.freeze({
        kind: "SessionFailure",
        reason: classifyInfrastructureFailure(error),
        detail: `${operation}: ${normalizedDetail(error)}`,
      });
      return this.#poisoned;
    }
  }

  #createSourceFact(
    handle: SourceFileHandle,
    source: TypeScriptSourceFile,
  ): SourceFileFact {
    const absolutePath = normalizedPath(source.fileName);
    const relativePath = relative(this.#projectDirectory, absolutePath);
    const insideProject = relativePath !== ".."
      && !relativePath.startsWith(`..${sep}`)
      && !isAbsolute(relativePath);
    const classification = this.#rootFiles.has(absolutePath)
      ? SourceFileClassification.ProjectRoot
      : this.#program.isSourceFileDefaultLibrary(source)
        ? SourceFileClassification.DefaultLibrary
        : this.#program.isSourceFileFromExternalLibrary(source)
          ? SourceFileClassification.ExternalLibrary
          : source.isDeclarationFile
            ? SourceFileClassification.OtherDeclaration
            : SourceFileClassification.ImportedProject;
    const path: SourcePath = insideProject
      && classification !== SourceFileClassification.DefaultLibrary
      && classification !== SourceFileClassification.ExternalLibrary
      ? Object.freeze({ kind: "ProjectRelative", path: portablePath(relativePath) })
      : Object.freeze({ kind: "External", path: portablePath(absolutePath) });
    const fact = Object.freeze({
      handle,
      path,
      classification,
      contentId: computeSourceContentId(source.text),
      length: source.text.length,
      isDeclarationFile: source.isDeclarationFile,
    });
    this.#sourceFacts.set(handle, fact);
    return fact;
  }

  #sourceFact(source: TypeScriptSourceFile): SourceFileFact {
    const handle = this.#registerSource(source);
    return this.#sourceFacts.get(handle) ?? this.#createSourceFact(handle, source);
  }

  #sourceLocation(node: TypeScriptNode): SourceLocation {
    const source = node.getSourceFile();
    const sourceFact = this.#sourceFact(source);
    const start = node.getStart(source);
    const end = node.getEnd();
    if (start < 0 || end < start || end > source.text.length) {
      throw new CompatibilityFailure(
        "UnsupportedResponseShape",
        `node ${rawSyntaxName(node.kind)} has invalid span ${start}..${end}`,
      );
    }
    const lineAndCharacter = source.getLineAndCharacterOfPosition(start);
    return Object.freeze({
      file: sourceFact.handle,
      content: sourceFact.contentId,
      start,
      length: end - start,
      line: lineAndCharacter.line,
      column: lineAndCharacter.character,
    });
  }

  #children(node: TypeScriptNode): readonly TypeScriptNode[] {
    const children: TypeScriptNode[] = [];
    node.forEachChild(child => {
      children.push(child);
      return undefined;
    });
    return children;
  }

  #nodeFact(node: TypeScriptNode): NodeFact {
    const handle = this.#registerNode(node);
    const existing = this.#nodeFacts.get(handle);
    if (existing !== undefined) {
      return existing;
    }
    if (this.#context.faults.unsupportedNodeKind && !this.#unsupportedNodeFaultRaised) {
      this.#unsupportedNodeFaultRaised = true;
      throw new CompatibilityFailure(
        "UnsupportedApiValue",
        "injected unsupported syntax kind",
      );
    }
    const kind = nodeKind(node);
    const source = node.getSourceFile();
    const parent = isSourceFile(node) ? undefined : this.#registerNode(node.parent);
    const spelling = kind === NodeKind.Identifier
      || kind === NodeKind.StringLiteral
      || kind === NodeKind.NumericLiteral
      || kind === NodeKind.BigIntLiteral
      || kind === NodeKind.RegularExpressionLiteral
      || kind === NodeKind.NoSubstitutionTemplateLiteral
      ? node.getText(source)
      : undefined;
    const fact = Object.freeze({
      handle,
      kind,
      location: this.#sourceLocation(node),
      parent,
      children: Object.freeze(this.#children(node).map(child => this.#registerNode(child))),
      spelling,
    });
    this.#nodeFacts.set(handle, fact);
    return fact;
  }

  #allNodes(source: TypeScriptSourceFile): readonly TypeScriptNode[] {
    const nodes: TypeScriptNode[] = [];
    const visit = (node: TypeScriptNode): void => {
      nodes.push(node);
      node.forEachChild(child => {
        visit(child);
        return undefined;
      });
    };
    visit(source);
    return nodes;
  }

  #declarationFact(node: TypeScriptNode): DeclarationFact {
    const handle = this.#registerDeclaration(node);
    const existing = this.#declarationFacts.get(handle);
    if (existing !== undefined) {
      return existing;
    }
    const containing: DeclarationHandle[] = [];
    let parent = isSourceFile(node) ? undefined : node.parent;
    while (parent !== undefined && !isSourceFile(parent)) {
      if (isDeclarationNode(parent)) {
        containing.push(this.#registerDeclaration(parent));
      }
      parent = parent.parent;
    }
    const sourceFact = this.#sourceFact(node.getSourceFile());
    const fact = Object.freeze({
      handle,
      node: this.#registerNode(node),
      kind: nodeKind(node),
      location: this.#sourceLocation(node),
      sourceFileClassification: sourceFact.classification,
      containingDeclarations: Object.freeze(containing),
    });
    this.#declarationFacts.set(handle, fact);
    return fact;
  }

  #resolveDeclaration(
    declaration: TypeScriptSymbol["declarations"][number],
  ): TypeScriptNode {
    const node = declaration.resolve(this.#project);
    if (node === undefined) {
      throw new CompatibilityFailure(
        "UnsupportedResponseShape",
        "TypeScript declaration handle did not resolve in its project",
      );
    }
    return node;
  }

  #declarationHandles(symbol: TypeScriptSymbol): readonly DeclarationHandle[] {
    const declarations = symbol.declarations
      .map(declaration => this.#resolveDeclaration(declaration))
      .sort((left, right) => {
        const leftSource = left.getSourceFile();
        const rightSource = right.getSourceFile();
        const pathOrder = compareText(
          portablePath(leftSource.fileName),
          portablePath(rightSource.fileName),
        );
        if (pathOrder !== 0) {
          return pathOrder;
        }
        return left.getStart(leftSource) - right.getStart(rightSource)
          || left.getEnd() - right.getEnd()
          || compareText(nodeKind(left), nodeKind(right));
      });
    return Object.freeze(declarations.map(declaration =>
      this.#registerDeclaration(declaration)));
  }

  #optionalDeclaration(
    declaration: TypeScriptSymbol["valueDeclaration"],
  ): DeclarationHandle | undefined {
    return declaration === undefined
      ? undefined
      : this.#registerDeclaration(this.#resolveDeclaration(declaration));
  }

  #checkedSymbol(symbol: TypeScriptSymbol | undefined): TypeScriptSymbol | undefined {
    if (symbol !== undefined && this.#checker.isUnknownSymbol(symbol)) {
      throw new QueryFailure(unavailable("UnknownSymbol", "TypeScript returned its unknown symbol"));
    }
    return symbol;
  }

  #symbolFact(symbol: TypeScriptSymbol): SymbolFact {
    if (this.#checker.isUnknownSymbol(symbol)) {
      throw new QueryFailure(unavailable("UnknownSymbol", "TypeScript returned its unknown symbol"));
    }
    const handle = this.#registerSymbol(symbol);
    const existing = this.#symbolFacts.get(handle);
    if (existing !== undefined) {
      return existing;
    }
    if (this.#context.faults.unsupportedSymbolFlags && !this.#unsupportedSymbolFaultRaised) {
      this.#unsupportedSymbolFaultRaised = true;
      throw new CompatibilityFailure(
        "UnsupportedApiValue",
        "injected unsupported symbol flags",
      );
    }
    const parent = this.#checkedSymbol(symbol.getParent());
    const exportSymbol = this.#checkedSymbol(symbol.getExportSymbol());
    const fact = Object.freeze({
      handle,
      escapedName: String(symbol.escapedName),
      displayName: symbol.name,
      categories: symbolCategories(symbol),
      declarations: this.#declarationHandles(symbol),
      valueDeclaration: this.#optionalDeclaration(symbol.valueDeclaration),
      parent: parent === undefined ? undefined : this.#registerSymbol(parent),
      exportSymbol: exportSymbol === undefined || exportSymbol === symbol
        ? undefined
        : this.#registerSymbol(exportSymbol),
    });
    this.#symbolFacts.set(handle, fact);
    return fact;
  }

  #typeFact(type: TypeScriptType): TypeFact {
    const handle = this.#registerType(type);
    const existing = this.#typeFacts.get(handle);
    if (existing !== undefined) {
      return existing;
    }
    if (this.#context.faults.unsupportedTypeValue && !this.#unsupportedTypeFaultRaised) {
      this.#unsupportedTypeFaultRaised = true;
      throw new CompatibilityFailure(
        "UnsupportedApiValue",
        "injected unsupported type value",
      );
    }
    const category = this.#context.faults.errorType && !this.#errorTypeFaultRaised
      ? (this.#errorTypeFaultRaised = true, TypeCategory.Error)
      : typeCategory(type);
    const symbol = this.#checkedSymbol(type.getSymbol());
    const aliasSymbol = this.#checkedSymbol(type.getAliasSymbol());
    const identitySymbol = aliasSymbol ?? symbol;
    const declarations = identitySymbol === undefined
      ? Object.freeze([])
      : this.#declarationHandles(identitySymbol);
    const literalValue = type.isLiteralType() ? type.value : undefined;
    const intrinsicName = type.isIntrinsicType() ? type.intrinsicName : undefined;
    const aliasTypeArguments = type.getAliasTypeArguments()
      .map(argument => this.#registerType(argument));
    const normalizedObjectCategories = [...objectCategories(type)];
    if (this.#checker.isTupleType(type)
      && !normalizedObjectCategories.includes(ObjectTypeCategory.Tuple)) {
      normalizedObjectCategories.push(ObjectTypeCategory.Tuple);
    }
    const fact = Object.freeze({
      handle,
      category,
      objectCategories: Object.freeze(normalizedObjectCategories),
      symbol: symbol === undefined ? undefined : this.#registerSymbol(symbol),
      aliasSymbol: aliasSymbol === undefined ? undefined : this.#registerSymbol(aliasSymbol),
      aliasTypeArguments: Object.freeze(aliasTypeArguments),
      declarations,
      intrinsicName,
      literalValue,
      display: this.#checker.typeToString(type),
    });
    this.#typeFacts.set(handle, fact);
    return fact;
  }

  #typePredicateFact(
    predicate: TypeScriptTypePredicate | undefined,
  ): TypePredicateFact | undefined {
    if (predicate === undefined) {
      return undefined;
    }
    return Object.freeze({
      category: typePredicateCategory(predicate),
      parameterName: predicate.parameterName,
      parameterIndex: predicate.parameterIndex,
      type: predicate.type === undefined ? undefined : this.#registerType(predicate.type),
    });
  }

  #signatureFact(signature: TypeScriptSignature): SignatureFact {
    const handle = this.#registerSignature(signature);
    const existing = this.#signatureFacts.get(handle);
    if (existing !== undefined) {
      return existing;
    }
    if (this.#context.faults.unsupportedSignatureValue
      && !this.#unsupportedSignatureFaultRaised) {
      this.#unsupportedSignatureFaultRaised = true;
      throw new CompatibilityFailure(
        "UnsupportedApiValue",
        "injected unsupported signature value",
      );
    }
    const returnType = this.#checker.getReturnTypeOfSignature(signature);
    if (returnType === undefined) {
      throw new QueryFailure(unavailable(
        "MissingApiFact",
        "TypeScript returned no return type for a signature",
      ));
    }
    const restType = this.#checker.getRestTypeOfSignature(signature);
    const thisParameter = signature.getThisParameter();
    const target = signature.getTarget();
    const declarationNode = signature.declaration?.resolve(this.#project);
    if (signature.declaration !== undefined && declarationNode === undefined) {
      throw new CompatibilityFailure(
        "UnsupportedResponseShape",
        "signature declaration handle did not resolve",
      );
    }
    const fact = Object.freeze({
      handle,
      category: signature.isConstruct ? SignatureCategory.Construct : SignatureCategory.Call,
      declaration: declarationNode === undefined
        ? undefined
        : this.#registerDeclaration(declarationNode),
      typeParameters: Object.freeze(
        signature.getTypeParameters().map(type => this.#registerType(type)),
      ),
      parameters: Object.freeze(
        signature.getParameters().map(symbol => this.#registerSymbol(symbol)),
      ),
      thisParameter: thisParameter === undefined
        ? undefined
        : this.#registerSymbol(thisParameter),
      target: target === undefined
        ? undefined
        : this.#registerSignature(target),
      hasRestParameter: signature.hasRestParameter,
      isAbstract: signature.isAbstract,
      returnType: this.#registerType(returnType),
      restType: restType === undefined ? undefined : this.#registerType(restType),
      predicate: this.#typePredicateFact(this.#checker.getTypePredicateOfSignature(signature)),
    });
    this.#signatureFacts.set(handle, fact);
    return fact;
  }

  #sortedSymbolFacts(symbols: readonly TypeScriptSymbol[]): readonly SymbolFact[] {
    return Object.freeze(symbols.map(symbol => this.#symbolFact(symbol)).sort((left, right) =>
      compareText(this.#symbolSortKey(left), this.#symbolSortKey(right))));
  }

  #symbolSortKey(fact: SymbolFact): string {
    const declaration = fact.declarations[0];
    if (declaration === undefined) {
      return `~/${fact.escapedName}`;
    }
    const node = this.#declarationByHandle.get(declaration);
    if (node === undefined) {
      throw new CompatibilityFailure(
        "UnsupportedResponseShape",
        "registered symbol declaration is absent from the declaration registry",
      );
    }
    const source = node.getSourceFile();
    return `${portablePath(source.fileName)}:`
      + `${String(node.getStart(source)).padStart(12, "0")}:`
      + `${String(node.getEnd()).padStart(12, "0")}:${fact.escapedName}`;
  }

  #notApplicableNode<T>(expected: string, node: TypeScriptNode): QueryResult<T> {
    return notApplicable(expected, nodeKind(node));
  }

  getSourceFiles(): QueryResult<readonly SourceFileFact[]> {
    return this.#run("getSourceFiles", () => resolved(this.#orderedSourceFacts));
  }

  getSourceFile(handle: SemanticHandle): QueryResult<SourceFileFact> {
    return this.#run("getSourceFile", () => resolved(this.#sourceFact(this.#requireSource(handle))));
  }

  getNodes(sourceFile: SemanticHandle): QueryResult<readonly NodeFact[]> {
    return this.#run("getNodes", () => {
      const source = this.#requireSource(sourceFile);
      const facts = this.#allNodes(source).map(node => this.#nodeFact(node));
      return resolved(Object.freeze(facts));
    });
  }

  getNode(handle: SemanticHandle): QueryResult<NodeFact> {
    return this.#run("getNode", () => resolved(this.#nodeFact(this.#requireNode(handle))));
  }

  correlateNode(
    sourceFile: SemanticHandle,
    coordinate: CoordinateQuery,
  ): QueryResult<NodeFact> {
    return this.#run("correlateNode", () => {
      const source = this.#requireSource(sourceFile);
      const sourceFact = this.#sourceFact(source);
      if (!sourceContentIdsEqual(sourceFact.contentId, coordinate.contentId)) {
        return Object.freeze({
          kind: "InvalidCoordinate",
          reason: "SourceContentMismatch",
        });
      }
      if (
        !Number.isSafeInteger(coordinate.start)
        || !Number.isSafeInteger(coordinate.length)
        || coordinate.start < 0
        || coordinate.length < 0
        || coordinate.start + coordinate.length > source.text.length
      ) {
        return Object.freeze({ kind: "InvalidCoordinate", reason: "OutOfRange" });
      }
      if (!Object.values(NodeKind).includes(coordinate.expectedKind)) {
        return unavailable(
          "UnsupportedApiValue",
          `unsupported repository node kind '${coordinate.expectedKind}'`,
        );
      }
      const candidates = this.#allNodes(source)
        .filter(node => {
          const start = node.getStart(source);
          return start === coordinate.start
            && node.getEnd() - start === coordinate.length
            && nodeKind(node) === coordinate.expectedKind;
        })
        .map(node => this.#nodeFact(node));
      if (candidates.length === 0) {
        return absent("no node has the requested canonical span and kind");
      }
      if (candidates.length > 1) {
        return Object.freeze({
          kind: "Ambiguous",
          candidates: Object.freeze(candidates),
          reason: "multiple nodes have the requested canonical span and kind",
        });
      }
      const candidate = candidates[0];
      if (candidate === undefined) {
        throw new CompatibilityFailure(
          "UnsupportedResponseShape",
          "one coordinate candidate count had no candidate",
        );
      }
      return resolved(candidate);
    });
  }

  getSymbol(handle: SemanticHandle): QueryResult<SymbolFact> {
    return this.#run("getSymbol", () => resolved(this.#symbolFact(this.#requireSymbol(handle))));
  }

  getSymbolAtNode(node: SemanticHandle): QueryResult<SymbolFact> {
    return this.#run("getSymbolAtNode", () => {
      const rawNode = this.#requireNode(node);
      const symbol = this.#checkedSymbol(this.#checker.getSymbolAtLocation(rawNode));
      return symbol === undefined
        ? absent("TypeScript reports no symbol at this syntax")
        : resolved(this.#symbolFact(symbol));
    });
  }

  getSourceSymbol(node: SemanticHandle): QueryResult<SymbolFact> {
    return this.#run("getSourceSymbol", () => {
      const rawNode = this.#requireNode(node);
      let symbol: TypeScriptSymbol | undefined;
      if (isShorthandPropertyAssignment(rawNode)) {
        symbol = this.#checker.getShorthandAssignmentValueSymbol(rawNode);
      } else if (isExportSpecifier(rawNode)) {
        symbol = this.#checker.getExportSpecifierLocalTargetSymbol(rawNode);
      } else {
        return this.#notApplicableNode(
          queryApplicability.getSourceSymbol,
          rawNode,
        );
      }
      const checked = this.#checkedSymbol(symbol);
      return checked === undefined
        ? absent("TypeScript reports no source symbol")
        : resolved(this.#symbolFact(checked));
    });
  }

  getDeclaration(handle: SemanticHandle): QueryResult<DeclarationFact> {
    return this.#run("getDeclaration", () =>
      resolved(this.#declarationFact(this.#requireDeclaration(handle))));
  }

  getAliasChain(symbol: SemanticHandle): QueryResult<AliasChainFact> {
    return this.#run("getAliasChain", () => {
      let current = this.#requireSymbol(symbol);
      if ((current.flags & SymbolFlags.Alias) === 0) {
        return notApplicable(
          queryApplicability.getAliasChain,
          symbolCategories(current).join("|") || "UnflaggedSymbol",
        );
      }
      if (this.#context.faults.unknownAliasedSymbol) {
        return unavailable("UnknownSymbol", "injected TypeScript unknown alias target");
      }
      const steps: AliasStepFact[] = [];
      const seen = new Set<TypeScriptSymbol>();
      while ((current.flags & SymbolFlags.Alias) !== 0) {
        if (seen.has(current)) {
          return unavailable(
            "UnsupportedResponseShape",
            "TypeScript returned a cyclic immediate alias chain",
          );
        }
        seen.add(current);
        let next: TypeScriptSymbol | undefined;
        try {
          next = this.#checker.getImmediateAliasedSymbol(current);
          if (next === undefined) {
            next = this.#checker.getAliasedSymbol(current);
          }
        } catch (error) {
          return unavailable(
            "MissingApiFact",
            `TypeScript could not provide an alias target: ${normalizedDetail(error)}`,
          );
        }
        if (this.#checker.isUnknownSymbol(next)) {
          return unavailable("UnknownSymbol", "TypeScript returned its unknown alias target");
        }
        steps.push(Object.freeze({
          alias: this.#registerSymbol(current),
          declarations: this.#declarationHandles(current),
          target: this.#registerSymbol(next),
        }));
        current = next;
      }
      return resolved(Object.freeze({
        steps: Object.freeze(steps),
        original: this.#registerSymbol(current),
      }));
    });
  }

  getType(handle: SemanticHandle): QueryResult<TypeFact> {
    return this.#run("getType", () => resolved(this.#typeFact(this.#requireType(handle))));
  }

  getTypeAtNode(node: SemanticHandle): QueryResult<TypeFact> {
    return this.#run("getTypeAtNode", () => {
      const type = this.#checker.getTypeAtLocation(this.#requireNode(node));
      return type === undefined
        ? absent("TypeScript reports no type at this syntax")
        : resolved(this.#typeFact(type));
    });
  }

  getContextualType(node: SemanticHandle): QueryResult<TypeFact> {
    return this.#run("getContextualType", () => {
      const rawNode = this.#requireNode(node);
      if (!isExpression(rawNode)) {
        return this.#notApplicableNode(queryApplicability.getContextualType, rawNode);
      }
      const type = this.#checker.getContextualType(rawNode);
      return type === undefined
        ? absent("TypeScript reports no contextual type")
        : resolved(this.#typeFact(type));
    });
  }

  getDeclaredType(symbol: SemanticHandle): QueryResult<TypeFact> {
    return this.#run("getDeclaredType", () => {
      const rawSymbol = this.#requireSymbol(symbol);
      let type: TypeScriptType;
      try {
        type = this.#checker.getDeclaredTypeOfSymbol(rawSymbol);
      } catch (error) {
        return unavailable(
          "MissingApiFact",
          `TypeScript could not provide a declared type: ${normalizedDetail(error)}`,
        );
      }
      return resolved(this.#typeFact(type));
    });
  }

  getSymbolValueType(symbol: SemanticHandle): QueryResult<TypeFact> {
    return this.#run("getSymbolValueType", () => {
      const rawSymbol = this.#requireSymbol(symbol);
      if (rawSymbol.valueDeclaration === undefined) {
        return absent("symbol has no value declaration");
      }
      const type = this.#checker.getTypeOfSymbol(rawSymbol);
      return type === undefined
        ? absent("symbol has no value type")
        : resolved(this.#typeFact(type));
    });
  }

  getSymbolTypeAtLocation(
    symbol: SemanticHandle,
    location: SemanticHandle,
  ): QueryResult<TypeFact> {
    return this.#run("getSymbolTypeAtLocation", () => {
      const rawSymbol = this.#requireSymbol(symbol);
      const rawLocation = this.#requireNode(location);
      let type: TypeScriptType;
      try {
        type = this.#checker.getTypeOfSymbolAtLocation(rawSymbol, rawLocation);
      } catch (error) {
        return unavailable(
          "MissingApiFact",
          `TypeScript could not provide a narrowed symbol type: ${normalizedDetail(error)}`,
        );
      }
      return resolved(this.#typeFact(type));
    });
  }

  getUnionConstituents(type: SemanticHandle): QueryResult<readonly TypeFact[]> {
    return this.#run("getUnionConstituents", () => {
      const rawType = this.#requireType(type);
      if (!rawType.isUnionType()) {
        return notApplicable(
          queryApplicability.getUnionConstituents,
          typeCategory(rawType),
        );
      }
      return resolved(Object.freeze(rawType.getTypes().map(member => this.#typeFact(member))));
    });
  }

  getIntersectionConstituents(type: SemanticHandle): QueryResult<readonly TypeFact[]> {
    return this.#run("getIntersectionConstituents", () => {
      const rawType = this.#requireType(type);
      if (!rawType.isIntersectionType()) {
        return notApplicable(
          queryApplicability.getIntersectionConstituents,
          typeCategory(rawType),
        );
      }
      return resolved(Object.freeze(rawType.getTypes().map(member => this.#typeFact(member))));
    });
  }

  getBaseTypes(type: SemanticHandle): QueryResult<readonly TypeFact[]> {
    return this.#run("getBaseTypes", () => {
      const rawType = this.#requireType(type);
      if (!rawType.isClassOrInterface()) {
        return notApplicable(queryApplicability.getBaseTypes, typeCategory(rawType));
      }
      return resolved(Object.freeze(
        this.#checker.getBaseTypes(rawType)
          .map(base => this.#typeFact(base)),
      ));
    });
  }

  getTypeArguments(type: SemanticHandle): QueryResult<readonly TypeFact[]> {
    return this.#run("getTypeArguments", () => {
      const rawType = this.#requireType(type);
      if (!rawType.isTypeReference()) {
        return notApplicable(queryApplicability.getTypeArguments, typeCategory(rawType));
      }
      return resolved(Object.freeze(
        this.#checker.getTypeArguments(rawType)
          .map(argument => this.#typeFact(argument)),
      ));
    });
  }

  #requiredType(
    operation: string,
    read: () => TypeScriptType | undefined,
  ): QueryResult<TypeFact> {
    const type = read();
    return type === undefined
      ? unavailable("MissingApiFact", `TypeScript returned no ${operation} type`)
      : resolved(this.#typeFact(type));
  }

  getApparentType(type: SemanticHandle): QueryResult<TypeFact> {
    return this.#run("getApparentType", () => {
      const rawType = this.#requireType(type);
      return this.#requiredType("apparent", () => this.#checker.getApparentType(rawType));
    });
  }

  getWidenedType(type: SemanticHandle): QueryResult<TypeFact> {
    return this.#run("getWidenedType", () => {
      const rawType = this.#requireType(type);
      return this.#requiredType("widened", () => this.#checker.getWidenedType(rawType));
    });
  }

  getNonNullableType(type: SemanticHandle): QueryResult<TypeFact> {
    return this.#run("getNonNullableType", () => {
      const rawType = this.#requireType(type);
      return this.#requiredType(
        "non-nullable",
        () => this.#checker.getNonNullableType(rawType),
      );
    });
  }

  getLiteralBaseType(type: SemanticHandle): QueryResult<TypeFact> {
    return this.#run("getLiteralBaseType", () => {
      const rawType = this.#requireType(type);
      if (!rawType.isLiteralType()) {
        return notApplicable(queryApplicability.getLiteralBaseType, typeCategory(rawType));
      }
      return this.#requiredType(
        "literal-base",
        () => this.#checker.getBaseTypeOfLiteralType(rawType),
      );
    });
  }

  getBaseConstraint(type: SemanticHandle): QueryResult<TypeFact> {
    return this.#run("getBaseConstraint", () => {
      const constraint = this.#checker.getBaseConstraintOfType(this.#requireType(type));
      return constraint === undefined
        ? absent("type has no base constraint")
        : resolved(this.#typeFact(constraint));
    });
  }

  getProperties(type: SemanticHandle): QueryResult<readonly SymbolFact[]> {
    return this.#run("getProperties", () => resolved(
      this.#sortedSymbolFacts(this.#checker.getPropertiesOfType(this.#requireType(type))),
    ));
  }

  getProperty(type: SemanticHandle, name: string): QueryResult<SymbolFact> {
    return this.#run("getProperty", () => {
      const property = this.#checker.getPropertyOfType(this.#requireType(type), name);
      return property === undefined
        ? absent(`type has no property '${name}'`)
        : resolved(this.#symbolFact(property));
    });
  }

  getIndexInfos(type: SemanticHandle): QueryResult<readonly IndexInfoFact[]> {
    return this.#run("getIndexInfos", () => {
      const infos = this.#checker.getIndexInfosOfType(this.#requireType(type));
      return resolved(Object.freeze(infos.map(info => this.#indexInfoFact(info))));
    });
  }

  #indexInfoFact(info: TypeScriptIndexInfo): IndexInfoFact {
    const declaration = info.declaration?.resolve(this.#project);
    if (info.declaration !== undefined && declaration === undefined) {
      throw new CompatibilityFailure(
        "UnsupportedResponseShape",
        "index declaration handle did not resolve",
      );
    }
    return Object.freeze({
      keyType: this.#registerType(info.keyType),
      valueType: this.#registerType(info.valueType),
      isReadonly: info.isReadonly,
      declaration: declaration === undefined
        ? undefined
        : this.#registerDeclaration(declaration),
    });
  }

  #signaturesOfType(
    type: TypeScriptType,
    kind: SignatureKind,
  ): readonly SignatureFact[] {
    return Object.freeze(
      this.#checker.getSignaturesOfType(type, kind)
        .map(signature => this.#signatureFact(signature)),
    );
  }

  getCallSignatures(type: SemanticHandle): QueryResult<readonly SignatureFact[]> {
    return this.#run("getCallSignatures", () => resolved(
      this.#signaturesOfType(this.#requireType(type), SignatureKind.Call),
    ));
  }

  getConstructSignatures(type: SemanticHandle): QueryResult<readonly SignatureFact[]> {
    return this.#run("getConstructSignatures", () => resolved(
      this.#signaturesOfType(this.#requireType(type), SignatureKind.Construct),
    ));
  }

  getSignature(handle: SemanticHandle): QueryResult<SignatureFact> {
    return this.#run("getSignature", () =>
      resolved(this.#signatureFact(this.#requireSignature(handle))));
  }

  getResolvedSignature(node: SemanticHandle): QueryResult<SignatureFact> {
    return this.#run("getResolvedSignature", () => {
      const rawNode = this.#requireNode(node);
      if (!isCallLikeExpression(rawNode)) {
        return this.#notApplicableNode(queryApplicability.getResolvedSignature, rawNode);
      }
      const signature = this.#checker.getResolvedSignature(rawNode);
      return signature === undefined
        ? absent("TypeScript reports no resolved signature")
        : resolved(this.#signatureFact(signature));
    });
  }

  getSignatureParameterType(
    signature: SemanticHandle,
    index: number,
  ): QueryResult<TypeFact> {
    return this.#run("getSignatureParameterType", () => {
      const rawSignature = this.#requireSignature(signature);
      if (
        !Number.isSafeInteger(index)
        || index < 0
        || index >= rawSignature.getParameters().length
      ) {
        return Object.freeze({ kind: "InvalidArgument", reason: "OutOfRange" });
      }
      const type = this.#checker.getParameterType(rawSignature, index);
      return type === undefined
        ? unavailable("MissingApiFact", "TypeScript returned no signature parameter type")
        : resolved(this.#typeFact(type));
    });
  }

  getSignatureTarget(signature: SemanticHandle): QueryResult<SignatureFact> {
    return this.#run("getSignatureTarget", () => {
      const target = this.#requireSignature(signature).getTarget();
      return target === undefined
        ? absent("signature has no generic target")
        : resolved(this.#signatureFact(target));
    });
  }

  #staticModuleSpecifier(node: TypeScriptNode): TypeScriptNode | undefined {
    if (isStringLiteralLikeNode(node)) {
      return node;
    }
    if (isImportDeclaration(node)) {
      return node.moduleSpecifier;
    }
    if (isExportDeclaration(node)) {
      return node.moduleSpecifier;
    }
    if (isExternalModuleReference(node)) {
      return node.expression;
    }
    if (isImportTypeNode(node)
      && isLiteralTypeNode(node.argument)) {
      return node.argument.literal;
    }
    return undefined;
  }

  getModuleSymbol(node: SemanticHandle): QueryResult<SymbolFact> {
    return this.#run("getModuleSymbol", () => {
      const rawNode = this.#requireNode(node);
      const specifier = this.#staticModuleSpecifier(rawNode);
      if (specifier === undefined || !isStringLiteralLikeNode(specifier)) {
        return this.#notApplicableNode(queryApplicability.getModuleSymbol, rawNode);
      }
      const symbol = this.#checkedSymbol(this.#checker.getSymbolAtLocation(specifier));
      return symbol === undefined
        ? absent("static module reference has no module symbol")
        : resolved(this.#symbolFact(symbol));
    });
  }

  getModuleExports(symbol: SemanticHandle): QueryResult<readonly SymbolFact[]> {
    return this.#run("getModuleExports", () => {
      const rawSymbol = this.#requireSymbol(symbol);
      if (!isModuleSymbol(rawSymbol)) {
        return notApplicable(
          queryApplicability.getModuleExports,
          symbolCategories(rawSymbol).join("|") || "UnflaggedSymbol",
        );
      }
      return resolved(this.#sortedSymbolFacts(this.#checker.getExportsOfModule(rawSymbol)));
    });
  }

  getModuleExport(symbol: SemanticHandle, name: string): QueryResult<SymbolFact> {
    return this.#run("getModuleExport", () => {
      const rawSymbol = this.#requireSymbol(symbol);
      if (!isModuleSymbol(rawSymbol)) {
        return notApplicable(
          queryApplicability.getModuleExport,
          symbolCategories(rawSymbol).join("|") || "UnflaggedSymbol",
        );
      }
      const exported = this.#checker.getMemberInModuleExports(rawSymbol, name);
      return exported === undefined
        ? absent(`module has no export '${name}'`)
        : resolved(this.#symbolFact(exported));
    });
  }

  getConstantValue(node: SemanticHandle): QueryResult<string | number> {
    return this.#run("getConstantValue", () => {
      const rawNode = this.#requireNode(node);
      if (
        !isEnumMember(rawNode)
        && !isPropertyAccessExpression(rawNode)
        && !isElementAccessExpression(rawNode)
      ) {
        return this.#notApplicableNode(queryApplicability.getConstantValue, rawNode);
      }
      const value = this.#checker.getConstantValue(rawNode);
      return value === undefined
        ? absent("TypeScript reports no constant value")
        : resolved(value);
    });
  }

  dispose(): DisposeResult {
    if (this.#disposeResult !== undefined) {
      return this.#disposeResult;
    }
    const failures = cleanup(this.#api, this.#snapshot, this.#context);
    this.#resources = undefined;
    this.#sourceByHandle.clear();
    this.#sourceFacts.clear();
    this.#nodeByHandle.clear();
    this.#nodeFacts.clear();
    this.#declarationByHandle.clear();
    this.#declarationFacts.clear();
    this.#symbolByHandle.clear();
    this.#symbolFacts.clear();
    this.#typeByHandle.clear();
    this.#typeFacts.clear();
    this.#signatureByHandle.clear();
    this.#signatureFacts.clear();
    this.#disposeResult = failures.length === 0
      ? Object.freeze({ kind: "Disposed" })
      : Object.freeze({ kind: "DisposeFailed", failures });
    return this.#disposeResult;
  }
}
