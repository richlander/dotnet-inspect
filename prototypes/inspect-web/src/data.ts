// Shared, pure data-shape and pure-function helpers used across the workspace UI: package
// identity keys, dependency-graph traversal primitives, workspace tab persistence/sharing,
// call-graph target resolution, member/source request-state machines, and small text
// helpers (mermaid label escaping, parameter titles). `dotnet-inspect.ts` owns all mutable state and
// wiring; these functions are pure transforms over explicit inputs/outputs so they can be
// unit-tested and reused (e.g. by `graph-mermaid.ts`) without the render/event-wiring layer.

// Exhaustiveness guard for the closed vocabularies below. The unions in this module are
// derived from catalogs that also drive visible UI choices, so adding a catalog entry both
// widens the union and offers the new value to users. Passing the switched value here makes
// the compiler reject that addition until every consumer states what the new value does; the
// throw is the residual runtime signal if a value ever reaches a consumer past its validator.
export function assertNever(value: never, vocabulary: string): never {
  throw new Error(`Unhandled ${vocabulary}: ${JSON.stringify(value)}`);
}

// Not exported: every consumer now goes through `typeLensesFor`, which applies the
// runtime-pack filter. A direct export would be a way to skip it.
const lenses = [
  ["api", "API"],
  ["metadata", "Metadata"],
  ["source", "Source"]
] as const;

export type TypeLens = (typeof lenses)[number][0];

export function isTypeLens(
  value: string | null | undefined,
): value is TypeLens {
  return typeof value === "string"
    && lenses.some(([id]) => id === value);
}

export const packageLenses = [
  ["overview", "Overview"],
  ["dependencies", "Dependencies"],
  ["integrations", "Integrations"],
  ["opportunities", "Opportunities"],
  ["analysis", "Analysis"],
  ["metadata", "Metadata"]
] as const;

export type PackageLens = (typeof packageLenses)[number][0];

export function isPackageLens(
  value: string | null | undefined,
): value is PackageLens {
  return typeof value === "string"
    && packageLenses.some(([id]) => id === value);
}

export const memberSectionDefinitions = [
  ["overview", "Overview"],
  ["call-graph", "Call graph"],
  ["facts", "Facts"],
  ["source", "Source"],
  ["annotated", "Annotated source"],
] as const;

export type MemberSection = (typeof memberSectionDefinitions)[number][0];

export function isMemberSection(
  value: string | null | undefined,
): value is MemberSection {
  return typeof value === "string"
    && memberSectionDefinitions.some(([id]) => id === value);
}

const workspaceScopes = ["workspace", "package", "type", "member"] as const;

export type WorkspaceScope = (typeof workspaceScopes)[number];

export function isWorkspaceScope(
  value: string | null | undefined,
): value is WorkspaceScope {
  return typeof value === "string"
    && workspaceScopes.some(scope => scope === value);
}

export const MAX_WORKSPACE_PACKAGES = 12;

/** The identity coordinate shared by every workspace/package-graph helper below. */
export interface PackageIdentity {
  id: string;
  version: string;
  activeFramework: string;
}

export function packageIdentityKey(pkg: PackageIdentity | null | undefined): string {
  if (!pkg) return "";
  return [pkg.id, pkg.version, pkg.activeFramework]
    .map(value => encodeURIComponent(value.toLowerCase()))
    .join("|");
}

export interface AssemblyDescriptor {
  id?: string;
  name?: string;
  version?: string;
  culture?: string | null;
  publicKeyToken?: string | null;
  publicMembers?: number;
  platformPack?: string | null;
}

export interface AssemblyDescribedType {
  assemblyId?: string;
  assembly?: string;
}

export function assemblyDescriptorForType(
  assemblies: readonly AssemblyDescriptor[] | null | undefined,
  type: AssemblyDescribedType | null | undefined,
): AssemblyDescriptor | null {
  if (type?.assemblyId) {
    return assemblies?.find(assembly => assembly.id === type.assemblyId) ?? null;
  }

  const name = type?.assembly || "";
  const bare = name.endsWith(".dll") ? name.slice(0, -4) : name;
  return assemblies?.find(assembly =>
    assembly.name === name
    || assembly.name === bare
    || assembly.name === `${bare}.dll`) ?? null;
}

export function accessibilityFilterIncludingType(
  filter: ReadonlySet<string> | null | undefined,
  type: { accessibilityId?: string } | null | undefined,
): Set<string> {
  const next = new Set(filter ?? []);
  if (type?.accessibilityId) next.add(type.accessibilityId);
  return next;
}

export function mergeInspectionErrors(
  current: string | null | undefined,
  next: string | null | undefined,
): string {
  return renderInspectionErrors(mergeInspectionErrorEntries(
    current ? [current] : [],
    next ? [next] : [],
  ));
}

export function mergeInspectionErrorEntries(
  current: readonly string[] | null | undefined,
  next: readonly string[] | null | undefined,
): string[] {
  const messages = [...(current ?? []), ...(next ?? [])]
    .map(value => value.trim())
    .filter(Boolean);
  return [...new Set(messages)];
}

export function renderInspectionErrors(
  entries: readonly string[] | null | undefined,
): string {
  return (entries ?? []).join("; ");
}

export type PlatformPack = "netcore.app" | "aspnetcore.app";

export function platformPackToken(value: unknown): PlatformPack | null {
  return value === "netcore.app" || value === "aspnetcore.app"
    ? value
    : null;
}

export interface PlatformPackAssembly {
  name?: string;
  platformPack?: string | null;
}

export interface PlatformPackHint {
  assembly?: string;
  pack?: string | null;
}

export function runtimePackForFramework<
  TPack extends { activeFramework?: string },
>(
  pack: TPack | null | undefined,
  framework: string,
): TPack | null {
  if (!pack || !framework) return pack ?? null;
  return (pack.activeFramework || "").toLowerCase()
      === framework.toLowerCase()
    ? pack
    : null;
}

export function platformPackFromProvenance(
  assembly: string,
  exactPack: unknown,
  loadedAssemblies: readonly PlatformPackAssembly[] | null | undefined,
  recent: readonly PlatformPackHint[] | null | undefined,
  roster: readonly PlatformPackHint[] | null | undefined,
): PlatformPack | null {
  const exact = platformPackToken(exactPack);
  if (exact) return exact;
  const normalized = assembly.replace(/\.dll$/i, "");
  const normalizedLower = normalized.toLowerCase();

  const selectPack = (
    candidates: readonly {
      assembly?: string | undefined;
      pack?: string | null | undefined;
    }[],
  ): PlatformPack | null => {
    const packs = new Set<PlatformPack>();
    for (const candidate of candidates) {
      if ((candidate.assembly ?? "").replace(/\.dll$/i, "").toLowerCase() === normalizedLower) {
        const pack = platformPackToken(candidate.pack);
        if (pack) packs.add(pack);
      }
    }
    if (packs.size > 1) {
      throw new Error(
        `Platform assembly '${normalized}' is available from multiple platform packs; select an exact pack.`);
    }
    return packs.values().next().value ?? null;
  };

  const loaded = selectPack((loadedAssemblies || []).map(candidate => ({
    assembly: candidate.name,
    pack: candidate.platformPack,
  })));
  if (loaded) return loaded;
  const indexed = selectPack(roster || []);
  if (indexed) return indexed;
  const remembered = selectPack(recent || []);
  if (remembered) return remembered;
  return "netcore.app";
}

export function platformPackFromAcquiredProvenance(
  assembly: string,
  exactPack: unknown,
  loadedAssemblies: readonly PlatformPackAssembly[] | null | undefined,
): PlatformPack | null {
  const exact = platformPackToken(exactPack);
  if (exact) return exact;
  const normalized = assembly.replace(/\.dll$/i, "").toLowerCase();
  const acquired = (loadedAssemblies || []).filter(candidate =>
    (candidate.name ?? "").replace(/\.dll$/i, "").toLowerCase() === normalized
    && platformPackToken(candidate.platformPack));
  if (acquired.length === 0) return null;
  return platformPackFromProvenance(
    assembly,
    null,
    acquired,
    [],
    []);
}

export function platformPackForGraphAssembly(
  assembly: string,
  exactPack: unknown,
  runtimePack: {
    activeFramework?: string;
    assemblies?: readonly PlatformPackAssembly[];
  } | null | undefined,
  framework: string,
): PlatformPack | null {
  const resident = runtimePackForFramework(runtimePack, framework);
  return platformPackFromAcquiredProvenance(
    assembly,
    exactPack,
    resident?.assemblies);
}

export interface DependencyCoordinateCandidate extends PackageIdentity {
  isRuntimePack?: boolean;
}

export interface DependencyCoordinate {
  key: string;
  provenance: "PlatformRuntime" | "NuGetPackage";
  packageId: string;
  version: string;
  targetFramework: string;
}

export function dependencyCoordinateCandidates(
  packages: readonly DependencyCoordinateCandidate[],
): DependencyCoordinate[] {
  return packages.map(candidate => ({
    key: packageIdentityKey(candidate),
    provenance: candidate.isRuntimePack ? "PlatformRuntime" : "NuGetPackage",
    packageId: candidate.id,
    version: candidate.version,
    targetFramework: candidate.activeFramework
  }));
}

export function dependencyGraphPackageKey(pkg: PackageIdentity): string {
  return `open\u0000${packageIdentityKey(pkg)}`;
}

export function dependencyGraphExternalKey(packageId: string, declaredRange: string | null | undefined): string {
  return `external\u0000${packageId.toLowerCase()}\u0000${declaredRange || ""}`;
}

export interface BoundedGraphNodeResult<T> {
  node: T | null;
  truncated: boolean;
}

export function ensureBoundedGraphNode<T>(
  nodes: Map<string, T>,
  key: string,
  create: () => T,
  limit: number,
): BoundedGraphNodeResult<T> {
  const existing = nodes.get(key);
  if (existing) return { node: existing, truncated: false };
  if (nodes.size >= limit) return { node: null, truncated: true };
  const node = create();
  nodes.set(key, node);
  return { node, truncated: false };
}

export interface DependencyGraphNodeInfo {
  kind: string;
  packageKey?: string;
  id?: string;
  versionRange?: string;
}

export interface DependencyGraphResult {
  definition: string;
  nodeInfoById: Map<string, DependencyGraphNodeInfo>;
  truncated: boolean;
  nodeLimit: number;
}

export function dependencyGraphRenderSignature(graph: DependencyGraphResult | null | undefined): string {
  if (!graph) return "";
  const navigation = [...graph.nodeInfoById.entries()].map(([nodeId, info]) => [
    nodeId,
    info.kind,
    info.packageKey || "",
    info.id || "",
    info.versionRange || ""
  ]);
  return JSON.stringify([
    graph.definition,
    graph.truncated,
    graph.nodeLimit,
    navigation
  ]);
}

export interface DependencyGraphRenderSequence {
  begin(): number;
  invalidate(): void;
  isCurrent(candidate: number): boolean;
}

export function createDependencyGraphRenderSequence(): DependencyGraphRenderSequence {
  let current = 0;
  return {
    begin() {
      return ++current;
    },
    invalidate() {
      current++;
    },
    isCurrent(candidate) {
      return candidate === current;
    }
  };
}

export interface DependencyGraphPendingDataset {
  graphPending?: string;
  graphPendingSequence?: string;
}

export interface DependencyGraphPendingState {
  isPending(signature: string): boolean;
  begin(signature: string, sequence: number): void;
  invalidate(): void;
  complete(signature: string, sequence: number): boolean;
}

export function createDependencyGraphPendingState(
  dataset: DependencyGraphPendingDataset,
): DependencyGraphPendingState {
  return {
    isPending(signature) {
      return dataset.graphPending === signature;
    },
    begin(signature, sequence) {
      dataset.graphPending = signature;
      dataset.graphPendingSequence = String(sequence);
    },
    invalidate() {
      delete dataset.graphPending;
      delete dataset.graphPendingSequence;
    },
    complete(signature, sequence) {
      if (dataset.graphPending !== signature
        || dataset.graphPendingSequence !== String(sequence)) {
        return false;
      }
      this.invalidate();
      return true;
    }
  };
}

export interface WorkspaceCoordinate {
  id: string;
  version: string;
  framework: string;
  shareId?: string;
  shareKind?: "package" | "group";
  shareSource?: string;
  runtimeIdentifier?: string | null;
}

export interface RetainWorkspacePackageResult<T> {
  packages: T[];
  evicted: T[];
}

export function retainWorkspacePackage<T extends PackageIdentity>(
  packages: readonly T[],
  activePackage: T | null | undefined,
  packageModel: T,
  replacedPackage: T | null = null,
): RetainWorkspacePackageResult<T> {
  const evicted: T[] = [];
  const next = packages.filter(item => {
    if (item !== replacedPackage) return true;
    evicted.push(item);
    return false;
  });
  const existing = next.findIndex(item =>
    packageIdentityKey(item) === packageIdentityKey(packageModel));
  if (existing >= 0)
    next[existing] = packageModel;
  else
    next.push(packageModel);

  while (next.length > MAX_WORKSPACE_PACKAGES) {
    const eviction = next.findIndex(item =>
      packageIdentityKey(item) !== packageIdentityKey(activePackage)
      && packageIdentityKey(item) !== packageIdentityKey(packageModel));
    if (eviction < 0) break;
    evicted.push(...next.splice(eviction, 1));
  }
  return { packages: next, evicted };
}

export interface RemoveWorkspacePackageInput extends PackageIdentity {
  isRuntimePack?: boolean;
}

export interface RemoveWorkspacePackageResult<T> {
  packages: T[];
  active: T | null;
  closed: T | null;
}

export function removeWorkspacePackage<T extends RemoveWorkspacePackageInput>(
  packages: readonly T[],
  activePackage: T | null,
  packageKey: string,
): RemoveWorkspacePackageResult<T> {
  const index = packages.findIndex(item => packageIdentityKey(item) === packageKey);
  const closed = index >= 0 ? packages[index] : undefined;
  if (!closed || closed.isRuntimePack) {
    return { packages: [...packages], active: activePackage, closed: null };
  }

  const remaining = packages.filter((_, candidate) => candidate !== index);
  const active = packageIdentityKey(activePackage) === packageKey
    ? remaining[Math.min(index, remaining.length - 1)] ?? null
    : activePackage;
  return { packages: remaining, active, closed };
}

export interface DependencyGroup {
  index: number;
  isActive?: boolean;
  framework: string;
  dependencies?: readonly DependencyGroupDependency[];
}

interface DependencyGroupDependency {
  id: string;
  versionRange?: string;
}

export interface DependencyGroupData {
  dependencyGroups?: readonly DependencyGroup[];
  dependencyGroupError?: string | null;
}

export function dependencyGroupSelectionMessage(data: DependencyGroupData | null | undefined): string {
  return data?.dependencyGroupError || "";
}

export function dependencyGraphGroupSelectionIndex(
  data: DependencyGroupData | null | undefined,
  selectedGroupIndex: number | null,
  resolvedGroupIndex: number | null,
): number | null {
  return data?.dependencyGroupError
    ? selectedGroupIndex
    : resolvedGroupIndex;
}

export function selectedDependencyGroup(
  data: DependencyGroupData | null | undefined,
  selectedGroupIndex: number | null = null,
): DependencyGroup | null {
  const groups = data?.dependencyGroups || [];
  if (!groups.length) return null;
  const selected = groups.find(group => group.index === selectedGroupIndex);
  if (selected) return selected;
  if (data?.dependencyGroupError) return null;
  return groups.find(group => group.isActive) || null;
}

export function spotlightCandidateKey(pkg: PackageIdentity, typeId: string): string {
  return `${packageIdentityKey(pkg)}\u0000${typeId}`;
}

export interface SpotlightSignaturePackage extends PackageIdentity {
  types?: readonly unknown[];
}

export function spotlightCandidateSignature(
  activePackage: PackageIdentity,
  packages: readonly SpotlightSignaturePackage[],
): string {
  return `${packageIdentityKey(activePackage)}#${packages
    .map(pkg => `${packageIdentityKey(pkg)}:${pkg.types?.length ?? 0}`)
    .join("|")}`;
}

export interface PackageForViewCandidate extends PackageIdentity {}

export interface PackageForViewLocation {
  packageKey?: string;
  package?: string;
}

export function packageForView<T extends PackageForViewCandidate>(
  packages: readonly T[],
  view: PackageForViewLocation,
): T | null {
  if (view.packageKey) {
    return packages.find(pkg => packageIdentityKey(pkg) === view.packageKey) ?? null;
  }
  return packages.find(pkg => pkg.id === view.package) ?? null;
}

export interface PackageCoordinateLocation {
  package?: string | null;
  version?: string | null;
  framework?: string | null;
}

export function packageCoordinateMatchesLocation(
  pkg: PackageIdentity | null | undefined,
  location: PackageCoordinateLocation | null | undefined,
): boolean {
  if (!pkg || !location?.package || !location.version || !location.framework) return false;
  return pkg.id.toLowerCase() === location.package.toLowerCase()
    && pkg.version.toLowerCase() === location.version.toLowerCase()
    && pkg.activeFramework.toLowerCase() === location.framework.toLowerCase();
}

export function workspaceCoordinatesMatch(
  packages: readonly PackageIdentity[] | null | undefined,
  tabs: readonly WorkspaceCoordinate[] | null | undefined,
): boolean {
  if (!packages || !tabs || packages.length !== tabs.length)
    return false;
  return tabs.every((tab, index) =>
    packageIdentityKey(packages[index]) === packageIdentityKey({
      id: tab.id,
      version: tab.version,
      activeFramework: tab.framework
    }));
}

export function removeAppendedNotice(current: string, previous: string, appended: string): string {
  if (current === appended) return previous;
  if (!current.startsWith(`${appended} `)) return current;
  const laterNotice = current.slice(appended.length + 1);
  return [previous, laterNotice].filter(Boolean).join(" ");
}

interface NavigationEntry<TView> {
  sig: string;
  view: TView;
}

export interface NavigationState<TView = unknown> {
  index: number;
  stack: NavigationEntry<TView>[];
}

export interface CallGraphTarget {
  typeDefinitionId?: string | null;
  typeMetadataId?: string | null;
  assembly?: string | null;
  assemblyVersion?: string | null;
  assemblyCulture?: string | null;
  assemblyPublicKeyToken?: string | null;
  memberName?: string | null;
  selectorKey?: string | null;
  metadataToken?: number | null;
  kind?: string | null;
  surfaceAssemblyId?: string | null;
}

export function callGraphTargetTypeId(target: CallGraphTarget | null | undefined): string {
  return target?.typeDefinitionId || target?.typeMetadataId || "";
}

export interface CallGraphMatchableType {
  definitionId?: string;
  id?: string;
  metadataId?: string;
  queryId?: string;
}

export function callGraphTargetMatchesType(
  target: CallGraphTarget | null | undefined,
  type: CallGraphMatchableType | null | undefined,
): boolean {
  if (target?.typeDefinitionId)
    return (type?.definitionId ?? type?.id) === target.typeDefinitionId;
  if (target?.typeMetadataId)
    return (type?.metadataId ?? type?.queryId ?? type?.id)
      === target.typeMetadataId;
  return false;
}

export type GraphMemberShareTarget = readonly [
  assembly: string,
  assemblyVersion: string | null,
  assemblyCulture: string | null,
  assemblyPublicKeyToken: string | null,
  typeDefinitionId: string,
  typeMetadataId: string,
  memberName: string,
  selectorKey: string,
  metadataToken: number | null,
];

export interface GraphMemberShareIdentity extends CallGraphTarget {
  assembly: string;
  typeDefinitionId: string;
  memberName: string;
  selectorKey: string;
  metadataToken: number | null;
}

export interface GraphMemberSurfaceType {
  assembly?: string | null;
  assemblyId?: string | null;
}

export function graphMemberSurfaceAssembly(
  target: CallGraphTarget & { assembly: string },
  type: GraphMemberSurfaceType | null = null,
): string {
  return type?.assemblyId
    || target.surfaceAssemblyId
    || type?.assembly
    || (target.assembly.endsWith(".dll")
      ? target.assembly
      : `${target.assembly}.dll`);
}

export interface SelectedGraphMemberBody {
  token: number;
  memberName: string;
  selectorKey: string;
}

export function graphMemberTargetWithSelectedBody<
  TTarget extends GraphMemberTarget,
>(
  target: TTarget,
  selectedBody: SelectedGraphMemberBody,
): TTarget & Required<GraphMemberTarget> {
  return {
    ...target,
    memberName: selectedBody.memberName,
    selectorKey: selectedBody.selectorKey,
    metadataToken: selectedBody.token,
  };
}

function isMethodDefinitionToken(value: unknown): value is number {
  return typeof value === "number"
    && Number.isInteger(value)
    && value >= 0x06000001
    && value <= 0x06ffffff;
}

export function graphMemberShareTarget(
  target: CallGraphTarget | null | undefined,
): GraphMemberShareTarget | null {
  const hasAssemblyVersion = Object.prototype.hasOwnProperty.call(
    target ?? {},
    "assemblyVersion");
  if (!target?.assembly
    || !hasAssemblyVersion
    || !target.typeDefinitionId
    || !target.memberName
    || !target.selectorKey
    || (target.metadataToken != null
      && !isMethodDefinitionToken(target.metadataToken))) {
    return null;
  }
  return [
    target.assembly,
    target.assemblyVersion ?? null,
    target.assemblyCulture ?? null,
    target.assemblyPublicKeyToken ?? null,
    target.typeDefinitionId,
    target.typeMetadataId ?? "",
    target.memberName,
    target.selectorKey,
    target.metadataToken ?? null
  ];
}

export function replaceCurrentNavigationEntry<TView>(
  navigation: NavigationState<TView>,
  entry: NavigationEntry<TView>,
): void {
  if (navigation.index === -1 && navigation.stack.length === 0) {
    navigation.stack.push(entry);
    navigation.index = 0;
    return;
  }
  if (!Number.isInteger(navigation.index)
    || navigation.index < 0
    || navigation.index >= navigation.stack.length) {
    throw new Error("The current navigation entry is unavailable.");
  }
  navigation.stack[navigation.index] = entry;
}

export function reconcileCurrentNavigationEntry<TView>(
  navigation: NavigationState<TView>,
  entry: NavigationEntry<TView>,
): void {
  if (navigation.index < 0
    || navigation.stack[navigation.index]?.sig === entry.sig) {
    return;
  }
  replaceCurrentNavigationEntry(navigation, entry);
}

export function graphMemberTargetFromShare(
  value: unknown,
): GraphMemberShareIdentity | null {
  if (!Array.isArray(value) || value.length !== 9) {
    return null;
  }
  const fields: unknown[] = value;
  const [
    assembly,
    assemblyVersion,
    assemblyCulture,
    assemblyPublicKeyToken,
    typeDefinitionId,
    typeMetadataId,
    memberName,
    selectorKey,
    metadataToken,
  ] = fields;
  if (typeof assembly !== "string"
    || (assemblyVersion != null
      && (typeof assemblyVersion !== "string" || assemblyVersion.length === 0))
    || (assemblyCulture != null && typeof assemblyCulture !== "string")
    || (assemblyPublicKeyToken != null
      && typeof assemblyPublicKeyToken !== "string")
    || typeof typeDefinitionId !== "string"
    || typeof typeMetadataId !== "string"
    || typeof memberName !== "string"
    || typeof selectorKey !== "string"
    || assembly.length === 0
    || typeDefinitionId.length === 0
    || memberName.length === 0
    || selectorKey.length === 0
    || (metadataToken != null && !isMethodDefinitionToken(metadataToken))) {
    return null;
  }
  return {
    assembly,
    assemblyVersion: assemblyVersion ?? null,
    assemblyCulture: assemblyCulture ?? null,
    assemblyPublicKeyToken: assemblyPublicKeyToken ?? null,
    typeDefinitionId,
    typeMetadataId: typeMetadataId || null,
    memberName,
    selectorKey,
    metadataToken: metadataToken ?? null
  };
}

export interface GraphMemberPacketResult {
  target: GraphMemberShareIdentity | null;
  error?: string;
}

function isUnknownRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === "object";
}

export function graphMemberTargetFromPacket(
  packet: unknown,
): GraphMemberPacketResult {
  if (!isUnknownRecord(packet)
    || !Object.prototype.hasOwnProperty.call(packet, "g")) {
    return { target: null };
  }
  const target = graphMemberTargetFromShare(packet.g);
  const type = packet.y;
  const member = packet.m;
  const overload = packet.o;
  if (!target
    || typeof type !== "string"
    || type.length === 0
    || typeof member !== "string"
    || member.length === 0
    || typeof overload !== "number"
    || !Number.isInteger(overload)
    || overload < 0) {
    return {
      target: null,
      error: "The shared graph member target is invalid and was ignored."
    };
  }
  return { target };
}

export type GraphMemberDeepLinkDisposition =
  "local" | "graph" | "mismatch" | "public" | "none";

export interface LocalGraphMemberSelection {
  group: { key: string };
  overloadIndex: number;
}

export function graphMemberDeepLinkDisposition<TType>(
  deep: {
    member?: string | null;
    overload?: string | null;
    graphTarget?: GraphMemberShareIdentity | null;
  } | null | undefined,
  candidate: { status: string; type?: TType } | null,
  selectedType: TType,
  publicGroup: unknown,
  localSelection: LocalGraphMemberSelection | null = null,
): GraphMemberDeepLinkDisposition {
  if (deep?.member && deep.graphTarget) {
    if (candidate?.status !== "unique" || candidate.type !== selectedType)
      return "mismatch";
    if (!localSelection) return "graph";
    return localSelection.group.key === deep.member ? "local" : "mismatch";
  }
  return publicGroup ? "public" : "none";
}

export interface PendingGraphMemberView {
  packageKey: string;
  viewSignature: string;
}

export function graphMemberPendingMatchesView(
  pending: PendingGraphMemberView | null | undefined,
  packageKey: string,
  viewSignature: string,
): boolean {
  return !!pending
    && pending.packageKey === packageKey
    && pending.viewSignature === viewSignature;
}

export interface QueryIdentifiedType {
  id?: string;
  queryId?: string;
}

export function uniqueTypeByQueryId<T extends QueryIdentifiedType>(
  types: readonly T[] | null | undefined,
  queryId: string,
): T | null {
  const matches = (types ?? []).filter(type =>
    (type.queryId ?? type.id) === queryId);
  return matches.length === 1 ? matches[0] ?? null : null;
}

export interface CallGraphAssembly {
  name?: string;
  version?: string;
  culture?: string | null;
  publicKeyToken?: string | null;
}

export function callGraphAssemblyIdentityMatches(
  target: CallGraphTarget | null | undefined,
  assembly: CallGraphAssembly | null | undefined,
): boolean {
  const hasVersion = Object.prototype.hasOwnProperty.call(
    target ?? {},
    "assemblyVersion");
  if (!hasVersion) return true;
  if (!target?.assemblyVersion) return false;
  if (!assembly) return false;
  const normalizeCulture = (value: string | null | undefined) => {
    const normalized = value?.toLowerCase() ?? "";
    return normalized === "neutral" ? "" : normalized;
  };
  return (assembly.name ?? "").toLowerCase()
      === (target.assembly ?? "").toLowerCase()
    && (assembly.version ?? "") === target.assemblyVersion
    && normalizeCulture(assembly.culture) === normalizeCulture(target.assemblyCulture)
    && (assembly.publicKeyToken ?? "").toLowerCase()
      === (target.assemblyPublicKeyToken ?? "").toLowerCase();
}

export interface ResolvableGraphType extends CallGraphMatchableType {
  assemblyName?: string;
  assembly?: string;
  assemblyId?: string;
}

export interface ResolvableGraphPackage<TType extends ResolvableGraphType = ResolvableGraphType> {
  isRuntimePack?: boolean;
  assembly?: string;
  types?: readonly TType[];
  assemblies?: readonly AssemblyDescriptor[];
}

export interface OpportunitySourceIdentity {
  sourceDefinitionId?: string | null;
  sourceAssembly?: string | null;
  sourceAssemblyVersion?: string | null;
  sourceAssemblyCulture?: string | null;
  sourceAssemblyPublicKeyToken?: string | null;
}

export type GraphTargetCandidate<TPackage, TType> =
  | { status: "missing" }
  | { status: "ambiguous" }
  | { status: "skew" }
  | { status: "resident" }
  | { status: "unique"; pkg: TPackage; type: TType };

function resolveGraphTargetCandidate<
  TPackage extends ResolvableGraphPackage<TType>,
  TType extends ResolvableGraphType,
>(
  packages: readonly TPackage[],
  target: CallGraphTarget | null | undefined,
  includeRuntimePacks: boolean,
): GraphTargetCandidate<TPackage, TType> {
  const typeId = callGraphTargetTypeId(target);
  if (!typeId || !target?.assembly) return { status: "missing" };
  if (Object.prototype.hasOwnProperty.call(target, "assemblyVersion")
      && !target.assemblyVersion) {
    return { status: "missing" };
  }
  const targetAssembly = target.assembly
    .replace(/\.dll$/i, "")
    .toLowerCase();
  const matches = [];
  let exactAssemblyResident = false;
  let identitySkew = false;
  for (const pkg of packages) {
    if (!pkg || (!includeRuntimePacks && pkg.isRuntimePack)) continue;
    const descriptors = pkg.assemblies ?? [];
    const namedDescriptors = descriptors.filter(descriptor =>
      (descriptor.name ?? "")
        .replace(/\.dll$/i, "")
        .toLowerCase() === targetAssembly);
    if (namedDescriptors.some(descriptor =>
        callGraphAssemblyIdentityMatches(target, descriptor))) {
      exactAssemblyResident = true;
    } else if (namedDescriptors.length > 0) {
      identitySkew = true;
    }
    for (const type of pkg.types ?? []) {
      const assembly =
        (type.assemblyName ?? type.assembly ?? pkg.assembly ?? "")
        .replace(/\.dll$/i, "");
      const descriptor = type.assemblyId
        ? descriptors.find(candidate => candidate.id === type.assemblyId)
        : descriptors.find(candidate =>
            (candidate.name ?? "").replace(/\.dll$/i, "").toLowerCase()
                === assembly.toLowerCase()
            && callGraphAssemblyIdentityMatches(target, candidate))
          ?? descriptors.find(candidate =>
            (candidate.name ?? "").replace(/\.dll$/i, "").toLowerCase()
                === assembly.toLowerCase());
      if (assembly.toLowerCase() !== targetAssembly
          || !callGraphTargetMatchesType(target, type)) continue;
      if (!callGraphAssemblyIdentityMatches(target, descriptor)) {
        if (descriptor) identitySkew = true;
        continue;
      }
      matches.push({ pkg, type });
      if (matches.length > 1) return { status: "ambiguous" };
    }
  }
  const match = matches[0];
  return matches.length === 1 && match
    ? { status: "unique", ...match }
    : exactAssemblyResident
      ? { status: "resident" }
      : identitySkew
        ? { status: "skew" }
        : { status: "missing" };
}

export function resolveLoadedGraphTargetCandidate<
  TPackage extends ResolvableGraphPackage<TType>,
  TType extends ResolvableGraphType,
>(
  packages: readonly TPackage[],
  target: CallGraphTarget | null | undefined,
): GraphTargetCandidate<TPackage, TType> {
  return resolveGraphTargetCandidate<TPackage, TType>(
    packages,
    target,
    false);
}

type RuntimeGraphPackage<TType extends ResolvableGraphType> =
  ResolvableGraphPackage<TType>;

export function resolveRuntimeGraphTargetCandidate<
  TType extends ResolvableGraphType,
>(
  pack: RuntimeGraphPackage<TType> | null | undefined,
  target: CallGraphTarget | null | undefined,
): GraphTargetCandidate<RuntimeGraphPackage<TType>, TType> {
  return pack?.isRuntimePack
    ? resolveGraphTargetCandidate<RuntimeGraphPackage<TType>, TType>(
        [pack],
        target,
        true)
    : { status: "missing" };
}

export function runtimeGraphTargetAssemblyIsResident(
  pack: ResolvableGraphPackage | null | undefined,
  target: CallGraphTarget | null | undefined,
): boolean {
  if (!pack?.isRuntimePack || !target?.assembly) return false;
  const targetAssembly = target.assembly
    .replace(/\.dll$/i, "")
    .toLowerCase();
  return (pack.assemblies ?? []).some(assembly =>
    (assembly.name ?? "")
      .replace(/\.dll$/i, "")
      .toLowerCase() === targetAssembly
    && callGraphAssemblyIdentityMatches(target, assembly));
}

export function resolvePlatformGraphTargetType<
  TType extends ResolvableGraphType,
>(
  pack: RuntimeGraphPackage<TType> | null | undefined,
  target: CallGraphTarget | null | undefined,
): TType | null {
  const candidate = resolveRuntimeGraphTargetCandidate(pack, target);
  return candidate.status === "unique" ? candidate.type : null;
}

export function resolveOpportunitySourceCandidate<
  TType extends ResolvableGraphType,
>(
  pack: RuntimeGraphPackage<TType> | null | undefined,
  opportunity: OpportunitySourceIdentity | null | undefined,
): GraphTargetCandidate<RuntimeGraphPackage<TType>, TType> {
  if (!pack || !opportunity?.sourceDefinitionId) return { status: "missing" };
  return resolveGraphTargetCandidate<
    RuntimeGraphPackage<TType>,
    TType
  >([pack], {
    assembly: opportunity.sourceAssembly ?? null,
    assemblyVersion: opportunity.sourceAssemblyVersion ?? null,
    assemblyCulture: opportunity.sourceAssemblyCulture ?? null,
    assemblyPublicKeyToken: opportunity.sourceAssemblyPublicKeyToken ?? null,
    typeDefinitionId: opportunity.sourceDefinitionId
  }, true);
}

export function resolveOpportunitySourceType<
  TType extends ResolvableGraphType,
>(
  pack: RuntimeGraphPackage<TType> | null | undefined,
  opportunity: OpportunitySourceIdentity | null | undefined,
): TType | null {
  const candidate = resolveOpportunitySourceCandidate(pack, opportunity);
  return candidate.status === "unique" ? candidate.type : null;
}

export type GraphTargetNavigationDisposition =
  "blocked" | "loaded" | "none" | "platform" | "resident";

export function graphTargetNavigationDisposition(
  candidate: GraphTargetCandidate<unknown, unknown>,
  target: CallGraphTarget | null | undefined,
  resident = false,
): GraphTargetNavigationDisposition {
  if (Object.prototype.hasOwnProperty.call(
      target ?? {},
      "assemblyVersion")
      && !target?.assemblyVersion) {
    return "none";
  }
  if (candidate.status === "ambiguous"
      || candidate.status === "skew"
      || candidate.status === "resident") {
    return "blocked";
  }
  if (candidate.status === "unique") return "loaded";
  return target?.kind === "external"
      && Boolean(target.assembly)
      && Boolean(callGraphTargetTypeId(target))
    ? resident ? "resident" : "platform"
    : "none";
}

export function combinedGraphTargetNavigationDisposition(
  candidate: GraphTargetCandidate<unknown, unknown>,
  runtimeCandidate: GraphTargetCandidate<unknown, unknown> | null,
  target: CallGraphTarget | null | undefined,
  runtimeResident = false,
): GraphTargetNavigationDisposition {
  const packageDisposition = graphTargetNavigationDisposition(candidate, target);
  if (packageDisposition === "none") return "none";
  if (runtimeCandidate) {
    if (runtimeCandidate.status === "ambiguous"
        || runtimeCandidate.status === "skew") {
      return "blocked";
    }
    if (runtimeCandidate.status === "unique"
        || runtimeCandidate.status === "resident"
        || runtimeResident) {
      return "resident";
    }
  }
  return packageDisposition;
}

export type RuntimeGraphTargetNavigationDisposition =
  "blocked" | "none" | "member" | "lookup" | "drill";

export function runtimeGraphTargetNavigationDisposition(
  candidate: { status?: string } | null | undefined,
  target: CallGraphTarget | null | undefined,
  hasMemberSelection: boolean,
  assemblyResident = false,
): RuntimeGraphTargetNavigationDisposition {
  if (!target?.assembly || !callGraphTargetTypeId(target)) return "none";
  if (Object.prototype.hasOwnProperty.call(target, "assemblyVersion")
      && !target.assemblyVersion) {
    return "none";
  }
  if (candidate?.status === "ambiguous" || candidate?.status === "skew")
    return "blocked";
  if (hasMemberSelection) return "member";
  return target.kind === "external"
      && candidate?.status === "missing"
      && !assemblyResident
    ? "lookup"
    : "drill";
}

export interface PdbSourceLimitationSource {
  pdbSourceLimitation?: string | null;
}

export function pdbSourceLimitationHtml(
  source: PdbSourceLimitationSource | null | undefined,
): string {
  if (!source?.pdbSourceLimitation) return "";
  const escaped = source.pdbSourceLimitation
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
  return `<span class="graph-source-status">PDB source unavailable: ${escaped}</span>`;
}

export function graphTargetBlockedReason(
  candidate: { status?: string } | null | undefined,
  scope: "runtime" | "package",
): string {
  const owner = scope === "runtime" ? "runtime" : "package";
  if (candidate?.status === "skew")
    return `the loaded ${owner} assembly identity does not match the exact target`;
  if (candidate?.status === "resident")
    return `the exact target type is not projected from the loaded ${owner} assembly`;
  return `the exact ${owner} target identity matched multiple loaded types`;
}

export function graphOnlyBodyTarget<TTarget>(
  overload: {
    graphOnly?: boolean;
    graphTarget?: TTarget | null;
  } | null | undefined,
): TTarget | null {
  return overload?.graphOnly ? overload.graphTarget ?? null : null;
}

export function retainGraphOnlyBodyTarget<TTarget>(
  overload: {
    graphOnly?: boolean;
    graphTarget?: TTarget | null;
  } | null | undefined,
  target: TTarget | null | undefined,
): void {
  if (overload?.graphOnly && target) overload.graphTarget = target;
}

export interface CallGraphDiagnostics {
  incompleteNodes?: number;
  incompleteEdges?: number;
  bindingIdentityConflicts?: number;
  hasAnalysisFailureBoundary?: boolean;
}

export function callGraphDiagnosticsMessage(diagnostics: CallGraphDiagnostics | null | undefined): string {
  if (!diagnostics) return "";
  const evidence: string[] = [];
  if ((diagnostics.incompleteNodes ?? 0) > 0)
    evidence.push(`${diagnostics.incompleteNodes} incomplete node${diagnostics.incompleteNodes === 1 ? "" : "s"}`);
  if ((diagnostics.incompleteEdges ?? 0) > 0)
    evidence.push(`${diagnostics.incompleteEdges} incomplete edge${diagnostics.incompleteEdges === 1 ? "" : "s"}`);
  if ((diagnostics.bindingIdentityConflicts ?? 0) > 0)
    evidence.push(`${diagnostics.bindingIdentityConflicts} binding identity conflict${diagnostics.bindingIdentityConflicts === 1 ? "" : "s"}`);
  if (diagnostics.hasAnalysisFailureBoundary)
    evidence.push("one or more method bodies could not be analyzed");
  if (!evidence.length) return "";
  const detail = evidence.length === 1
    ? evidence[0]
    : evidence.length === 2
    ? `${evidence[0]} and ${evidence[1]}`
    : `${evidence.slice(0, -1).join(", ")}, and ${evidence.at(-1)}`;
  return `Partial call graph: ${detail}.`;
}

export interface TitledParameter {
  type?: string;
}

export function parameterTitleHtml(parameters: readonly TitledParameter[]): string {
  if (!parameters.length) return "()";
  const escape = (value: unknown) => String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
  return `(${parameters.map(parameter => escape(parameter.type || "")).join(", ")})`;
}

export const MARKDOWN_SANITIZE_OPTIONS = Object.freeze({
  ALLOWED_TAGS: Object.freeze([
    "a", "blockquote", "br", "code", "del", "em", "h1", "h2", "h3", "h4", "h5", "h6",
    "hr", "li", "ol", "p", "pre", "strong", "table", "tbody", "td", "th", "thead", "tr", "ul"
  ]),
  ALLOWED_ATTR: Object.freeze(["title"]),
  ALLOW_ARIA_ATTR: false,
  ALLOW_DATA_ATTR: false
});

interface GraphMemberBodySelector {
  memberName: string;
  selectorKey: string;
  token?: number;
}

interface GraphMemberOverload {
  bodySelectors?: readonly GraphMemberBodySelector[];
  graphSelectorKey?: string;
}

export interface GraphMemberGroup {
  overloads: readonly GraphMemberOverload[];
}

export interface GraphMemberTarget {
  memberName?: string | null;
  selectorKey?: string | null;
  metadataToken?: number | null;
}

export interface GraphMemberSelection {
  groupIndex: number;
  overloadIndex: number;
}

export function graphMemberSelection(
  groups: readonly GraphMemberGroup[],
  target: GraphMemberTarget,
): GraphMemberSelection | null {
  const bodyMatches: (
    GraphMemberSelection & { token: number | undefined }
  )[] = [];
  for (const [groupIndex, group] of groups.entries()) {
    for (const [overloadIndex, overload] of group.overloads.entries()) {
      for (const body of overload.bodySelectors ?? []) {
        if (body.memberName === target.memberName
          && body.selectorKey === target.selectorKey) {
          bodyMatches.push({ groupIndex, overloadIndex, token: body.token });
        }
      }
    }
  }
  if (bodyMatches.length > 0) {
    const first = bodyMatches[0];
    if (!first) return null;
    if (bodyMatches.length === 1
      || (first.token != null
        && bodyMatches.every(match => match.token === first.token))) {
      return {
        groupIndex: first.groupIndex,
        overloadIndex: first.overloadIndex,
      };
    }
    return null;
  }

  const ownerMatches: GraphMemberSelection[] = [];
  for (const [groupIndex, group] of groups.entries()) {
    for (const [overloadIndex, overload] of group.overloads.entries()) {
      if (overload.graphSelectorKey === target.selectorKey)
        ownerMatches.push({ groupIndex, overloadIndex });
    }
  }
  return ownerMatches.length === 1 ? ownerMatches[0] ?? null : null;
}

export function searchableMemberGroups<
  T extends { overloads: readonly { graphOnly?: boolean }[] },
>(groups: readonly T[]): T[] {
  return groups.filter(group =>
    !group.overloads.some(overload => overload.graphOnly));
}

export function partitionGraphMembers<T extends { graphOnly?: boolean }>(
  members: readonly T[] | null | undefined,
): { publicMembers: T[]; graphMembers: T[] } {
  const publicMembers: T[] = [];
  const graphMembers: T[] = [];
  for (const member of members ?? []) {
    (member.graphOnly ? graphMembers : publicMembers).push(member);
  }
  return { publicMembers, graphMembers };
}

export function retainGraphMemberProjection<
  TMember extends { name: string; graphOnly?: boolean },
>(
  types: readonly { api: TMember[] }[],
  selected: TMember,
): void {
  for (const type of types) {
    if (!type.api.some(member => member.graphOnly && member !== selected))
      continue;
    type.api = type.api.filter(
      member => !member.graphOnly || member === selected);
  }
}

export interface ScopedRequestState {
  loading: boolean;
  error: string;
}

export function scopedRequestState(
  activeKey: string,
  requestKey: string,
  loading: boolean,
  error: string,
): ScopedRequestState {
  return activeKey === requestKey
    ? { loading, error }
    : { loading: false, error: "" };
}

export function memberRequestKey(parts: readonly string[], taste: readonly string[] = []): string {
  return [...parts, ...taste].join("\u0000");
}

export interface SourceWorkbenchState {
  settings?: boolean;
  explorer?: { open?: boolean } | null;
  loading?: boolean;
  error?: string;
  home?: boolean;
  package?: unknown;
  graphSourceOpen?: boolean;
  atPackageRoot?: boolean;
  lens?: TypeLens;
  selectedMemberKey?: string;
  memberSection?: MemberSection;
}

function sourceWorkbenchIsVisible(state: SourceWorkbenchState): boolean {
  if (state.settings
    || state.explorer?.open
    || state.loading
    || state.error
    || state.home
    || !state.package) {
    return false;
  }
  return true;
}

export type SourceOperationKind = "graph" | "type" | "member" | null;

export function activeSourceOperationKind(
  state: SourceWorkbenchState,
  memberSourceHasConcreteOverload = true,
): SourceOperationKind {
  if (!sourceWorkbenchIsVisible(state)) return null;
  if (state.graphSourceOpen) return "graph";
  if (state.atPackageRoot) return null;
  if (state.lens === "source") return "type";
  if (state.lens === "api"
    && state.selectedMemberKey
    && state.memberSection === "source"
    && memberSourceHasConcreteOverload) {
    return "member";
  }
  return null;
}

export function sourceSurfaceIsVisible(
  state: SourceWorkbenchState,
  memberSourceHasConcreteOverload = true,
): boolean {
  return activeSourceOperationKind(
    state,
    memberSourceHasConcreteOverload) !== null;
}

export type SourceReloadKind = "graph" | "type" | "member" | "annotated" | null;

export function sourceReloadKind(
  state: SourceWorkbenchState,
  memberSourceHasConcreteOverload = true,
): SourceReloadKind {
  const active = activeSourceOperationKind(
    state,
    memberSourceHasConcreteOverload);
  if (active) return active;
  if (!sourceWorkbenchIsVisible(state)
    || state.atPackageRoot
    || state.graphSourceOpen) {
    return null;
  }
  if (state.lens === "api"
    && state.selectedMemberKey
    && state.memberSection === "annotated"
    && memberSourceHasConcreteOverload) {
    return "annotated";
  }
  return null;
}

export function sourceRequestNeedsLoad(
  sameRequest: boolean,
  loading: boolean,
  result: unknown,
  error: unknown,
): boolean {
  return !sameRequest || (!loading && !result && !error);
}

export interface SourceRequestState {
  sourceRequestGeneration?: number;
  memberSourceLoading?: boolean;
  memberSourceKey?: string;
  memberSourceError?: string;
  typeSourceLoading?: boolean;
  typeSourceKey?: string;
  typeSourceError?: string;
  graphSourceLoading?: boolean;
  graphSourceError?: string;
  graphSourceSeq?: number;
}

export function beginSourceRequestState(state: SourceRequestState): number {
  state.sourceRequestGeneration = (state.sourceRequestGeneration ?? 0) + 1;
  clearInFlightSourceState(state);
  return state.sourceRequestGeneration;
}

export function cancelSourceRequestState(state: SourceRequestState): boolean {
  if (!state.memberSourceLoading
    && !state.typeSourceLoading
    && !state.graphSourceLoading) {
    return false;
  }
  state.sourceRequestGeneration = (state.sourceRequestGeneration ?? 0) + 1;
  clearInFlightSourceState(state);
  return true;
}

function clearInFlightSourceState(state: SourceRequestState): void {
  if (state.memberSourceLoading) {
    state.memberSourceLoading = false;
    state.memberSourceKey = "";
    state.memberSourceError = "";
  }
  if (state.typeSourceLoading) {
    state.typeSourceLoading = false;
    state.typeSourceKey = "";
    state.typeSourceError = "";
  }
  if (state.graphSourceLoading) {
    state.graphSourceLoading = false;
    state.graphSourceError = "";
    state.graphSourceSeq = (state.graphSourceSeq ?? 0) + 1;
  }
}

export interface SectionableMember {
  kind?: string;
}

// The full roster is derived from the catalog rather than restated, so a new section is
// offered as soon as it is defined. Restating it was the durable defect: the compiler
// rejects a *removal* from the catalog, because the literal would stop being assignable,
// but nothing caught an *addition*, which silently never reached the UI at all.
const allMemberSections: readonly MemberSection[] =
  memberSectionDefinitions.map(([id]) => id);

const packageOnlyMemberSections: ReadonlySet<MemberSection> =
  new Set<MemberSection>(["facts", "source", "annotated"]);

export function memberSectionIdsFor(
  member: SectionableMember | null | undefined,
  isRuntimePack = false,
  hasSelectedBody = false,
): MemberSection[] {
  if (["property", "field", "event", "constant"].includes(member?.kind ?? "")
    && !hasSelectedBody) {
    return ["overview"];
  }
  const sections = isRuntimePack
    ? allMemberSections.filter(section => !packageOnlyMemberSections.has(section))
    : [...allMemberSections];
  return hasSelectedBody
    && ["property", "event"].includes(member?.kind ?? "")
    ? sections.filter(id => id !== "source")
    : sections;
}

export function typeLensesFor(
  pkg: { isRuntimePack?: boolean } | null | undefined,
): readonly (readonly [TypeLens, string])[] {
  return pkg?.isRuntimePack
    ? lenses.filter(([id]) => id === "api")
    : lenses;
}

const FORMAT_CHARACTER = /^\p{Cf}$/u;

export function mermaidLabel(value: string): string {
  let encoded = "";
  for (const character of value) {
    const scalar = character.codePointAt(0)!;
    if (character === "&") encoded += "&amp;";
    else if (character === "<") encoded += "&lt;";
    else if (character === ">") encoded += "&gt;";
    else if (character === '"') encoded += "&quot;";
    else if (character === "\\") encoded += "&#92;";
    else if (scalar < 0x20 || (scalar >= 0x7f && scalar <= 0x9f)
      || scalar === 0x2028 || scalar === 0x2029
      || (scalar >= 0xd800 && scalar <= 0xdfff)
      || FORMAT_CHARACTER.test(character)) {
      for (let index = 0; index < character.length; index++) {
        encoded += `&#92;u${character.charCodeAt(index)
          .toString(16).toUpperCase().padStart(4, "0")}`;
      }
    } else encoded += character;
  }
  return encoded;
}
