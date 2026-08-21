// Shared, pure data-shape and pure-function helpers used across the workspace UI: package
// identity keys, dependency-graph traversal primitives, workspace tab persistence/sharing,
// call-graph target resolution, member/source request-state machines, and small text
// helpers (mermaid label escaping, parameter titles). `app.ts` owns all mutable state and
// wiring; these functions are pure transforms over explicit inputs/outputs so they can be
// unit-tested and reused (e.g. by `graph-mermaid.ts`) without the render/event-wiring layer.

export const lenses: readonly (readonly [string, string])[] = [
  ["api", "API"],
  ["metadata", "Metadata"],
  ["source", "Source"]
];

export const packageLenses: readonly (readonly [string, string])[] = [
  ["overview", "Overview"],
  ["dependencies", "Dependencies"],
  ["integrations", "Integrations"],
  ["opportunities", "Opportunities"],
  ["analysis", "Analysis"],
  ["metadata", "Metadata"]
];

export const MAX_WORKSPACE_PACKAGES = 12;
export const MAX_SHARE_STATE_CHARACTERS = 65536;

/** The identity coordinate shared by every workspace/package-graph helper below. */
export interface PackageIdentity {
  id: string;
  version: string;
  activeFramework: string;
}

export function packageIdentityKey(pkg: PackageIdentity | null | undefined): string {
  if (!pkg) return "";
  return [pkg.id, pkg.version, pkg.activeFramework]
    .map(value => encodeURIComponent(String(value || "").toLowerCase()))
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

export function mergeInspectionErrors(current: unknown, next: unknown): string {
  const messages = [current, next]
    .map(value => String(value || "").trim())
    .filter(Boolean);
  return [...new Set(messages)].join("; ");
}

export type PlatformPack = "netcore.app" | "aspnetcore.app";

export function platformPackToken(value: unknown): PlatformPack | null {
  return value === "netcore.app" || value === "aspnetcore.app"
    ? value
    : null;
}

export interface PlatformAssemblyProvenance {
  name?: string;
  assembly?: string;
  platformPack?: string | null;
  pack?: string | null;
}

export function platformPackFromProvenance(
  assembly: unknown,
  exactPack: unknown,
  loadedAssemblies: readonly PlatformAssemblyProvenance[] | null | undefined,
  recent: readonly PlatformAssemblyProvenance[] | null | undefined,
  roster: readonly PlatformAssemblyProvenance[] | null | undefined,
): PlatformPack {
  const exact = platformPackToken(exactPack);
  if (exact) return exact;
  const normalized = String(assembly || "").replace(/\.dll$/i, "");
  const loaded = (loadedAssemblies || []).find(candidate =>
    String(candidate.name || "").replace(/\.dll$/i, "")
      .toLowerCase() === normalized.toLowerCase());
  const loadedPack = platformPackToken(loaded?.platformPack);
  if (loadedPack) return loadedPack;
  const indexed = (roster || []).find(entry =>
    String(entry.assembly || "").toLowerCase() === normalized.toLowerCase());
  const indexedPack = platformPackToken(indexed?.pack);
  if (indexedPack) return indexedPack;
  const remembered = (recent || []).find(entry =>
    String(entry.assembly || "").toLowerCase() === normalized.toLowerCase());
  const rememberedPack = platformPackToken(remembered?.pack);
  if (rememberedPack) return rememberedPack;
  return "netcore.app";
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
    Boolean(graph.truncated),
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

export interface WorkspaceTab {
  id: string;
  version: string;
  framework: string;
}

export interface NormalizedShareTabs {
  tabs: WorkspaceTab[];
  sourceIndexes: number[];
  error: string;
}

export function normalizeShareTabs(list: unknown): NormalizedShareTabs {
  if (!Array.isArray(list)) {
    return {
      tabs: [],
      sourceIndexes: [],
      error: "The shared workspace state is invalid and was ignored."
    };
  }
  if (list.length > MAX_WORKSPACE_PACKAGES) {
    return {
      tabs: [],
      sourceIndexes: [],
      error: `The shared workspace exceeds the ${MAX_WORKSPACE_PACKAGES}-package limit and was ignored.`
    };
  }

  const tabs: WorkspaceTab[] = [];
  const sourceIndexes: number[] = [];
  const identityIndexes = new Map<string, number>();
  for (let sourceIndex = 0; sourceIndex < list.length; sourceIndex++) {
    const tuple: unknown = list[sourceIndex];
    if (!Array.isArray(tuple)
      || tuple.length < 1
      || tuple.length > 3
      || tuple.some(value => typeof value !== "string")
      || !tuple[0].trim()) {
      return {
        tabs: [],
        sourceIndexes: [],
        error: "The shared workspace state is invalid and was ignored."
      };
    }
    const tab: WorkspaceTab = {
      id: tuple[0],
      version: tuple[1] || "latest",
      framework: tuple[2] || ""
    };
    const identity = packageIdentityKey({
      id: tab.id,
      version: tab.version,
      activeFramework: tab.framework
    });
    if (!identityIndexes.has(identity)) {
      identityIndexes.set(identity, tabs.length);
      tabs.push(tab);
    }
    sourceIndexes[sourceIndex] = identityIndexes.get(identity)!;
  }
  return { tabs, sourceIndexes, error: "" };
}

export function shareStateLengthError(value: unknown): string {
  return String(value || "").length > MAX_SHARE_STATE_CHARACTERS
    ? `The shared workspace state exceeds the ${MAX_SHARE_STATE_CHARACTERS}-character limit and was ignored.`
    : "";
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
  if (index < 0 || packages[index].isRuntimePack) {
    return { packages: [...packages], active: activePackage, closed: null };
  }

  const closed = packages[index];
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

export interface DependencyGroupDependency {
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
  return String(pkg.id).toLowerCase() === String(location.package).toLowerCase()
    && String(pkg.version).toLowerCase() === String(location.version).toLowerCase()
    && String(pkg.activeFramework).toLowerCase() === String(location.framework).toLowerCase();
}

export function workspaceCoordinatesMatch(
  packages: readonly PackageIdentity[] | null | undefined,
  tabs: readonly WorkspaceTab[] | null | undefined,
): boolean {
  if (!Array.isArray(packages) || !Array.isArray(tabs) || packages.length !== tabs.length)
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

export interface NavigationEntry {
  sig: string;
  view: unknown;
}

export interface NavigationState {
  index: number;
  stack: NavigationEntry[];
}

export function replaceCurrentNavigationEntry(nav: NavigationState, sig: string, view: unknown): void {
  if (nav.index < 0 || nav.index >= nav.stack.length) return;
  nav.stack[nav.index] = { sig, view };
}

export interface CallGraphTarget {
  typeDefinitionId?: string | null;
  typeMetadataId?: string | null;
  assembly?: string | null;
  assemblyVersion?: string | null;
  assemblyCulture?: string | null;
  assemblyPublicKeyToken?: string | null;
  kind?: string | null;
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
  return matches.length === 1 ? matches[0] : null;
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
  const normalizeCulture = (value: unknown) => {
    const normalized = String(value ?? "").toLowerCase();
    return normalized === "neutral" ? "" : normalized;
  };
  return String(assembly.name ?? "").toLowerCase()
      === String(target.assembly ?? "").toLowerCase()
    && String(assembly.version ?? "") === String(target.assemblyVersion)
    && normalizeCulture(assembly.culture) === normalizeCulture(target.assemblyCulture)
    && String(assembly.publicKeyToken ?? "").toLowerCase()
      === String(target.assemblyPublicKeyToken ?? "").toLowerCase();
}

export interface ResolvableGraphType extends CallGraphMatchableType {
  assemblyName?: string;
  assembly?: string;
  assemblyId?: string;
}

export interface ResolvableGraphPackage {
  isRuntimePack?: boolean;
  assembly?: string;
  types?: readonly ResolvableGraphType[];
  assemblies?: readonly AssemblyDescriptor[];
}

export interface OpportunitySourceIdentity {
  sourceDefinitionId?: string | null;
  sourceAssembly?: string | null;
  sourceAssemblyVersion?: string | null;
  sourceAssemblyCulture?: string | null;
  sourceAssemblyPublicKeyToken?: string | null;
}

export function resolvePlatformGraphTargetType<
  TType extends ResolvableGraphType,
>(
  pack: {
    types?: readonly TType[];
    assemblies?: readonly AssemblyDescriptor[];
  } | null | undefined,
  target: CallGraphTarget | null | undefined,
): TType | null {
  const typeId = callGraphTargetTypeId(target);
  if (!pack || !typeId || !target?.assembly) return null;
  const targetAssembly = String(target.assembly)
    .replace(/\.dll$/i, "")
    .toLowerCase();
  const matches = (pack.types ?? []).filter(type => {
    const descriptor = assemblyDescriptorForType(pack.assemblies, type);
    const assembly = String(
      type.assemblyName ?? descriptor?.name ?? type.assembly ?? "")
      .replace(/\.dll$/i, "")
      .toLowerCase();
    return assembly === targetAssembly
      && callGraphAssemblyIdentityMatches(target, descriptor)
      && callGraphTargetMatchesType(target, type);
  });
  return matches.length === 1 ? matches[0] : null;
}

export function resolveOpportunitySourceType<
  TType extends ResolvableGraphType,
>(
  pack: {
    types?: readonly TType[];
    assemblies?: readonly AssemblyDescriptor[];
  } | null | undefined,
  opportunity: OpportunitySourceIdentity | null | undefined,
): TType | null {
  if (!opportunity?.sourceDefinitionId) return null;
  return resolvePlatformGraphTargetType(pack, {
    assembly: opportunity.sourceAssembly,
    assemblyVersion: opportunity.sourceAssemblyVersion,
    assemblyCulture: opportunity.sourceAssemblyCulture,
    assemblyPublicKeyToken: opportunity.sourceAssemblyPublicKeyToken,
    typeDefinitionId: opportunity.sourceDefinitionId
  });
}

export type GraphTargetCandidate<TPackage, TType> =
  | { status: "missing" }
  | { status: "ambiguous" }
  | { status: "unique"; pkg: TPackage; type: TType };

export function resolveLoadedGraphTargetCandidate<
  TPackage extends ResolvableGraphPackage,
  TType extends ResolvableGraphType,
>(
  packages: readonly TPackage[],
  target: CallGraphTarget | null | undefined,
): GraphTargetCandidate<TPackage, TType> {
  const typeId = callGraphTargetTypeId(target);
  if (!typeId || !target?.assembly) return { status: "missing" };
  const matches: { pkg: TPackage; type: TType }[] = [];
  for (const pkg of packages) {
    if (!pkg || pkg.isRuntimePack) continue;
    for (const type of (pkg.types ?? []) as readonly TType[]) {
      const assembly = String(
        type.assemblyName ?? type.assembly ?? pkg.assembly ?? "")
        .replace(/\.dll$/i, "");
      const descriptors = pkg.assemblies ?? [];
      const descriptor = type.assemblyId
        ? descriptors.find(candidate => candidate.id === type.assemblyId)
        : descriptors.find(candidate =>
            String(candidate.name ?? "").replace(/\.dll$/i, "").toLowerCase()
              === assembly.toLowerCase());
      if (assembly.toLowerCase() === target.assembly.toLowerCase()
          && callGraphAssemblyIdentityMatches(target, descriptor)
          && callGraphTargetMatchesType(target, type)) {
        matches.push({ pkg, type });
        if (matches.length > 1) return { status: "ambiguous" };
      }
    }
  }
  return matches.length === 1
    ? { status: "unique", ...matches[0] }
    : { status: "missing" };
}

export type GraphTargetNavigationDisposition = "blocked" | "loaded" | "none" | "platform";

export function graphTargetNavigationDisposition(
  candidate: GraphTargetCandidate<unknown, unknown>,
  target: CallGraphTarget | null | undefined,
): GraphTargetNavigationDisposition {
  if (candidate.status === "ambiguous") return "blocked";
  if (candidate.status === "unique") return "loaded";
  if (Object.prototype.hasOwnProperty.call(
      target ?? {},
      "assemblyVersion")
      && !target?.assemblyVersion) {
    return "none";
  }
  return target?.kind === "external"
      && Boolean(target.assembly)
      && Boolean(callGraphTargetTypeId(target))
    ? "platform"
    : "none";
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

export interface GraphMemberBodySelector {
  memberName: string;
  selectorKey: string;
  token?: number;
}

export interface GraphMemberOverload {
  bodySelectors?: readonly GraphMemberBodySelector[];
  graphSelectorKey?: string;
}

export interface GraphMemberGroup {
  overloads: readonly GraphMemberOverload[];
}

export interface GraphMemberTarget {
  memberName: string;
  selectorKey: string;
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
  const bodyMatches: GraphMemberSelection[] = [];
  for (let groupIndex = 0; groupIndex < groups.length; groupIndex++) {
    const group = groups[groupIndex];
    for (let overloadIndex = 0; overloadIndex < group.overloads.length; overloadIndex++) {
      const overload = group.overloads[overloadIndex];
      if ((overload.bodySelectors ?? []).some(body =>
        body.memberName === target.memberName
        && body.selectorKey === target.selectorKey
        && (target.metadataToken == null || body.token === target.metadataToken))) {
        bodyMatches.push({ groupIndex, overloadIndex });
      }
    }
  }
  if (bodyMatches.length === 1) return bodyMatches[0];

  const ownerMatches: GraphMemberSelection[] = [];
  for (let groupIndex = 0; groupIndex < groups.length; groupIndex++) {
    const group = groups[groupIndex];
    for (let overloadIndex = 0; overloadIndex < group.overloads.length; overloadIndex++) {
      if (group.overloads[overloadIndex].graphSelectorKey === target.selectorKey)
        ownerMatches.push({ groupIndex, overloadIndex });
    }
  }
  return ownerMatches.length === 1 ? ownerMatches[0] : null;
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
  lens?: string;
  selectedMemberKey?: string;
  memberSection?: string;
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

export function activeSourceOperationKind(state: SourceWorkbenchState): SourceOperationKind {
  if (!sourceWorkbenchIsVisible(state)) return null;
  if (state.graphSourceOpen) return "graph";
  if (state.atPackageRoot) return null;
  if (state.lens === "source") return "type";
  if (state.lens === "api"
    && state.selectedMemberKey
    && state.memberSection === "source") {
    return "member";
  }
  return null;
}

export function sourceSurfaceIsVisible(state: SourceWorkbenchState): boolean {
  return activeSourceOperationKind(state) !== null;
}

export type SourceReloadKind = "graph" | "type" | "member" | "annotated" | null;

export function sourceReloadKind(state: SourceWorkbenchState): SourceReloadKind {
  const active = activeSourceOperationKind(state);
  if (active) return active;
  if (!sourceWorkbenchIsVisible(state)
    || state.atPackageRoot
    || state.graphSourceOpen) {
    return null;
  }
  if (state.lens === "api"
    && state.selectedMemberKey
    && state.memberSection === "annotated") {
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

export function memberSectionIdsFor(
  member: SectionableMember | null | undefined,
  isRuntimePack = false,
): string[] {
  if (["property", "field", "event", "constant"].includes(member?.kind ?? ""))
    return ["overview"];
  return isRuntimePack
    ? ["overview", "call-graph", "facts"]
    : ["overview", "call-graph", "facts", "source", "annotated"];
}

export function typeLensesFor(
  pkg: { isRuntimePack?: boolean } | null | undefined,
) {
  return pkg?.isRuntimePack
    ? lenses.filter(([id]) => id === "api")
    : lenses;
}

const FORMAT_CHARACTER = /^\p{Cf}$/u;

export function mermaidLabel(value: unknown): string {
  let encoded = "";
  for (const character of String(value ?? "")) {
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
