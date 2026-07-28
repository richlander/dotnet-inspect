import { dotnet } from "./_framework/dotnet.js";

let queryPackage;
let queryMemberSource;
let queryMemberAnnotatedSource;
let queryTypeProjection;
let queryPackageDependencies;
let queryPackageIntegrations;
let queryPackageOpportunities;
let queryPackagePerformance;
let queryTypeMemberSource;
let queryTypeSource;
let queryMemberCallGraph;
let expandPlatformCallGraph;
let loadRuntimePack;
let queryMemberDocumentation;
let queryMemberFacts;
let searchTypes;
let listStyleOptions;
let packageCacheStats;

export async function initializeEngine(onStatus = () => {}) {
  onStatus("Loading .NET 11 WebAssembly…");
  const runtime = await dotnet.create();
  const config = runtime.getConfig();
  const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
  queryPackage = exports.BrowserInspectionEngine.QueryPackage;
  queryMemberSource = exports.BrowserInspectionEngine.QueryMemberSource;
  queryMemberAnnotatedSource = exports.BrowserInspectionEngine.QueryMemberAnnotatedSource;
  queryTypeProjection = exports.BrowserInspectionEngine.QueryTypeProjection;
  queryPackageDependencies = exports.BrowserInspectionEngine.QueryPackageDependencies;
  queryPackageIntegrations = exports.BrowserInspectionEngine.QueryPackageIntegrations;
  queryPackageOpportunities = exports.BrowserInspectionEngine.QueryPackageOpportunities;
  queryPackagePerformance = exports.BrowserInspectionEngine.QueryPackagePerformance;
  queryTypeMemberSource = exports.BrowserInspectionEngine.QueryTypeMemberSource;
  queryTypeSource = exports.BrowserInspectionEngine.QueryTypeSource;
  queryMemberCallGraph = exports.BrowserInspectionEngine.QueryMemberCallGraph;
  expandPlatformCallGraph = exports.BrowserInspectionEngine.ExpandPlatformCallGraph;
  loadRuntimePack = exports.BrowserInspectionEngine.LoadRuntimePack;
  queryMemberDocumentation = exports.BrowserInspectionEngine.QueryMemberDocumentation;
  queryMemberFacts = exports.BrowserInspectionEngine.QueryMemberFacts;
  searchTypes = exports.BrowserInspectionEngine.SearchTypes;
  listStyleOptions = exports.BrowserInspectionEngine.ListStyleOptions;
  packageCacheStats = exports.BrowserInspectionEngine.PackageCacheStats;
  await runtime.runMain();
  onStatus("Reading package assemblies…");
}

export async function inspectPackage(packageId, version, framework) {
  if (!queryPackage) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryPackage(packageId, version, framework);
  return JSON.parse(json);
}

export async function inspectMemberSource(request) {
  if (!queryMemberSource) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryMemberSource(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.member,
    request.signature,
    request.styleOptionsJson ?? "[]");
  return JSON.parse(json);
}

export async function inspectMemberAnnotatedSource(request) {
  if (!queryMemberAnnotatedSource) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryMemberAnnotatedSource(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.member,
    request.signature,
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
    request.assembly);
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

export async function inspectTypeSource(request) {
  if (!queryTypeSource) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryTypeSource(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.styleOptionsJson ?? "[]");
  return JSON.parse(json);
}

export async function inspectListStyleOptions() {
  if (!listStyleOptions) throw new Error("The browser inspection engine is not initialized.");
  return JSON.parse(await listStyleOptions());
}

export function inspectPackageCacheStats() {
  if (!packageCacheStats) return null;
  return JSON.parse(packageCacheStats());
}

export async function inspectMemberCallGraph(request) {
  if (!queryMemberCallGraph) throw new Error("The browser inspection engine is not initialized.");
  const json = await queryMemberCallGraph(
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.member,
    request.signature,
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
    request.paramSig ?? "");
  return JSON.parse(json);
}

export async function inspectLoadRuntimePack(framework) {
  if (!loadRuntimePack) throw new Error("The browser inspection engine is not initialized.");
  const json = await loadRuntimePack(framework ?? "");
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
