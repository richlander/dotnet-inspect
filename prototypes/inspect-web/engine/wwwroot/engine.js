let queryPackage;
let queryPackageVersions;
let resolvePackageDependencyVersion;
let matchPackageDependencyCoordinateExport;
let getPackageDocument;
let queryMemberSource;
let cancelSourceQuery;
let queryMemberAnnotatedSource;
let queryTypeProjection;
let queryPackageDependencies;
let queryPackageIntegrations;
let queryPlatformIntegrations;
let queryPlatformOpportunities;
let queryPlatformPerformance;
let queryPackageOpportunities;
let queryPackagePerformance;
let queryPackageMetadata;
let queryPlatformMetadata;
let queryPackageMetadataTable;
let queryPlatformMetadataTable;
let queryPackageHeapEntries;
let queryPlatformHeapEntries;
let queryTypeMemberSource;
let queryTypeSource;
let queryMemberCallGraph;
let expandPlatformCallGraph;
let loadRuntimePack;
let loadRuntimePackAssembly;
let queryMemberDocumentation;
let queryMemberFacts;
let searchTypes;
let listVocabulary;
let buildIdentity;
let packageCacheStats;

// The generated module owns the single dotnet.create() / getAssemblyExports() bootstrap and
// ConfigureHost call. Import it only when initialization begins so a bare home page can paint
// before the runtime graph starts downloading.
export async function initializeEngine(onStatus = () => {}) {
  onStatus("Loading .NET 11 WebAssembly…");
  const generated = await import("./inspect-web-engine.js");
  const exports = await generated.initializeEngine(onStatus);
  buildIdentity = generated.buildIdentity;
  packageCacheStats = generated.packageCacheStats;
  queryPackage = exports.InspectionEngine.QueryPackage;
  queryPackageVersions = exports.InspectionEngine.QueryPackageVersions;
  resolvePackageDependencyVersion = exports.InspectionEngine.ResolvePackageDependencyVersion;
  matchPackageDependencyCoordinateExport = exports.InspectionEngine.MatchPackageDependencyCoordinate;
  getPackageDocument = exports.InspectionEngine.GetPackageDocument;
  queryMemberSource = exports.InspectionEngine.QueryMemberSource;
  cancelSourceQuery = exports.InspectionEngine.CancelSourceQuery;
  queryMemberAnnotatedSource = exports.InspectionEngine.QueryMemberAnnotatedSource;
  queryTypeProjection = exports.InspectionEngine.QueryTypeProjection;
  queryPackageDependencies = exports.InspectionEngine.QueryPackageDependencies;
  queryPackageIntegrations = exports.InspectionEngine.QueryPackageIntegrations;
  queryPlatformIntegrations = exports.InspectionEngine.QueryPlatformIntegrations;
  queryPlatformOpportunities = exports.InspectionEngine.QueryPlatformOpportunities;
  queryPlatformPerformance = exports.InspectionEngine.QueryPlatformPerformance;
  queryPackageOpportunities = exports.InspectionEngine.QueryPackageOpportunities;
  queryPackagePerformance = exports.InspectionEngine.QueryPackagePerformance;
  queryPackageMetadata = exports.InspectionEngine.QueryPackageMetadata;
  queryPlatformMetadata = exports.InspectionEngine.QueryPlatformMetadata;
  queryPackageMetadataTable = exports.InspectionEngine.QueryPackageMetadataTable;
  queryPlatformMetadataTable = exports.InspectionEngine.QueryPlatformMetadataTable;
  queryPackageHeapEntries = exports.InspectionEngine.QueryPackageHeapEntries;
  queryPlatformHeapEntries = exports.InspectionEngine.QueryPlatformHeapEntries;
  queryTypeMemberSource = exports.InspectionEngine.QueryTypeMemberSource;
  queryTypeSource = exports.InspectionEngine.QueryTypeSource;
  queryMemberCallGraph = exports.InspectionEngine.QueryMemberCallGraph;
  expandPlatformCallGraph = exports.InspectionEngine.ExpandPlatformCallGraph;
  loadRuntimePack = exports.InspectionEngine.LoadRuntimePack;
  loadRuntimePackAssembly = exports.InspectionEngine.LoadRuntimePackAssembly;
  queryMemberDocumentation = exports.InspectionEngine.QueryMemberDocumentation;
  queryMemberFacts = exports.InspectionEngine.QueryMemberFacts;
  searchTypes = exports.InspectionEngine.SearchTypes;
  listVocabulary = exports.InspectionEngine.ListVocabulary;
  onStatus("Reading package assemblies…");
}

export function inspectBuildIdentity() {
  if (!buildIdentity) throw new Error("The browser inspection engine is not initialized.");
  return buildIdentity();
}

export function inspectPackageCacheStats() {
  if (!packageCacheStats) throw new Error("The browser inspection engine is not initialized.");
  return packageCacheStats();
}

export async function inspectPackage(packageId, version, framework) {
  if (!queryPackage) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPackage(packageId, version, framework);
  return JSON.parse(json);
}

export async function inspectPackageVersions(packageId) {
  if (!queryPackageVersions) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPackageVersions(packageId);
  return JSON.parse(json);
}

export async function inspectPackageDocument(request) {
  if (!getPackageDocument) throw new Error("The browser inspection engine is not initialized.");
  const json = await getPackageDocument(request.packageId, request.version, request.path);
  return JSON.parse(json);
}

export async function inspectMemberSource(request) {
  if (!queryMemberSource) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryMemberSource(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.typeIdentity ?? request.type,
    request.member,
    request.selectorKey ?? "",
    request.metadataToken ?? 0,
    request.styleOptionsJson ?? "[]");
  return JSON.parse(json);
}

export function cancelSourceInspection() {
  cancelSourceQuery?.();
}

export async function inspectMemberAnnotatedSource(request) {
  if (!queryMemberAnnotatedSource) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryMemberAnnotatedSource(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.typeIdentity ?? request.type,
    request.type,
    request.member,
    request.signature,
    request.selectorKey ?? "",
    request.metadataToken ?? 0,
    request.styleOptionsJson ?? "[]");
  return JSON.parse(json);
}

export async function inspectTypeMemberSource(request) {
  if (!queryTypeMemberSource) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryTypeMemberSource(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.member,
    request.selectorKey,
    request.metadataToken ?? 0,
    request.styleOptionsJson ?? "[]");
  return JSON.parse(json);
}

export async function inspectTypeProjection(request) {
  if (!queryTypeProjection) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryTypeProjection(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type);
  return JSON.parse(json);
}

export async function inspectPackageDependencies(request) {
  if (!queryPackageDependencies) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPackageDependencies(
    request.packageId,
    request.version,
    request.framework,
    request.assemblyId);
  return JSON.parse(json);
}

export async function resolveDependencyVersion(packageId, declaredRange) {
  if (!resolvePackageDependencyVersion) throw new Error("The browser inspection engine is not initialized.");
  return resolvePackageDependencyVersion(packageId, declaredRange || "");
}

export function matchPackageDependencyCoordinate(packageId, declaredRange, candidates) {
  if (!matchPackageDependencyCoordinateExport) throw new Error("The browser inspection engine is not initialized.");
  const json = matchPackageDependencyCoordinateExport(
    packageId,
    declaredRange || "",
    JSON.stringify(candidates));
  return JSON.parse(json);
}

export async function inspectPackageIntegrations(request) {
  if (!queryPackageIntegrations) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPackageIntegrations(
    request.packageId,
    request.version,
    request.framework);
  return JSON.parse(json);
}

export async function inspectPlatformIntegrations(request) {
  if (!queryPlatformIntegrations) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPlatformIntegrations(
    request.targetFramework,
    request.assemblyFileName,
    request.pack);
  return JSON.parse(json);
}

export async function inspectPlatformOpportunities(request) {
  if (!queryPlatformOpportunities) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPlatformOpportunities(
    request.targetFramework,
    request.assemblyFileName,
    request.pack);
  return JSON.parse(json);
}

export async function inspectPlatformPerformance(request) {
  if (!queryPlatformPerformance) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPlatformPerformance(
    request.targetFramework,
    request.assemblyFileName,
    request.pack);
  return JSON.parse(json);
}

export async function inspectPackageOpportunities(request) {
  if (!queryPackageOpportunities) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPackageOpportunities(
    request.packageId,
    request.version,
    request.framework);
  return JSON.parse(json);
}

export async function inspectPackagePerformance(request) {
  if (!queryPackagePerformance) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPackagePerformance(
    request.packageId,
    request.version,
    request.framework);
  return JSON.parse(json);
}

export async function inspectPackageMetadata(request) {
  if (!queryPackageMetadata) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPackageMetadata(
    request.packageId,
    request.version,
    request.framework);
  return JSON.parse(json);
}

export async function inspectPlatformMetadata(request) {
  if (!queryPlatformMetadata) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPlatformMetadata(
    request.targetFramework,
    request.assemblyFileName,
    request.pack);
  return JSON.parse(json);
}

export async function inspectPackageMetadataTable(request) {
  if (!queryPackageMetadataTable) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPackageMetadataTable(
    request.packageId,
    request.version,
    request.framework,
    request.assemblyFileName,
    request.tableIndex,
    request.startRowId,
    request.maxRows);
  return JSON.parse(json);
}

export async function inspectPlatformMetadataTable(request) {
  if (!queryPlatformMetadataTable) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPlatformMetadataTable(
    request.targetFramework,
    request.assemblyFileName,
    request.pack,
    request.tableIndex,
    request.startRowId,
    request.maxRows);
  return JSON.parse(json);
}

export async function inspectPackageHeapEntries(request) {
  if (!queryPackageHeapEntries) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPackageHeapEntries(
    request.packageId,
    request.version,
    request.framework,
    request.assemblyFileName,
    request.heap);
  return JSON.parse(json);
}

export async function inspectPlatformHeapEntries(request) {
  if (!queryPlatformHeapEntries) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPlatformHeapEntries(
    request.targetFramework,
    request.assemblyFileName,
    request.pack,
    request.heap);
  return JSON.parse(json);
}

export async function inspectTypeSource(request) {
  if (!queryTypeSource) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryTypeSource(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.typeIdentity ?? request.type,
    request.styleOptionsJson ?? "[]");
  return JSON.parse(json);
}

export async function inspectVocabulary() {
  if (!listVocabulary) throw new Error("The browser inspection engine is not initialized.");
  return JSON.parse(await listVocabulary());
}

export async function inspectMemberCallGraph(request) {
  if (!queryMemberCallGraph) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryMemberCallGraph(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.typeIdentity ?? request.type,
    request.type,
    request.member,
    request.signature,
    request.selectorKey ?? "",
    request.metadataToken ?? 0,
    JSON.stringify(request.workspace ?? []));
  return JSON.parse(json);
}

export async function inspectExpandPlatformCallGraph(request) {
  if (!expandPlatformCallGraph) throw new Error("The browser inspection engine is not initialized.");
  const json = await expandPlatformCallGraph(
    request.framework,
    request.assembly ?? "",
    request.type,
    request.member,
    request.selectorKey,
    request.metadataToken ?? 0);
  return JSON.parse(json);
}

export async function inspectLoadRuntimePack(framework) {
  if (!loadRuntimePack) throw new Error("The browser inspection engine is not initialized.");
  const json = await loadRuntimePack(framework ?? "");
  return JSON.parse(json);
}

export async function inspectLoadRuntimePackAssembly(framework, assemblyFileName, pack) {
  if (!loadRuntimePackAssembly) throw new Error("The browser inspection engine is not initialized.");
  const json = await loadRuntimePackAssembly(framework ?? "", assemblyFileName ?? "", pack ?? "");
  return JSON.parse(json);
}

export async function inspectMemberDocumentation(request) {
  if (!queryMemberDocumentation) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryMemberDocumentation(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.documentationId);
  return JSON.parse(json);
}

export async function inspectMemberFacts(request) {
  if (!queryMemberFacts) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryMemberFacts(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.member,
    request.signature);
  return JSON.parse(json);
}

export function isEngineReady() {
  return Boolean(searchTypes);
}

export function inspectSearchTypes(query, candidatesJson) {
  if (!searchTypes) return null;
  return JSON.parse(searchTypes(query ?? "", candidatesJson));
}
