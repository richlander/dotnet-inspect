import { dotnet } from "./_framework/dotnet.js";

let queryPackage;
let queryMemberSource;
let queryMemberCallGraph;
let queryMemberDocumentation;
let queryMemberFacts;

export async function initializeEngine(onStatus = () => {}) {
  onStatus("Loading .NET 11 WebAssembly…");
  const runtime = await dotnet.create();
  const config = runtime.getConfig();
  const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
  queryPackage = exports.BrowserInspectionEngine.QueryPackage;
  queryMemberSource = exports.BrowserInspectionEngine.QueryMemberSource;
  queryMemberCallGraph = exports.BrowserInspectionEngine.QueryMemberCallGraph;
  queryMemberDocumentation = exports.BrowserInspectionEngine.QueryMemberDocumentation;
  queryMemberFacts = exports.BrowserInspectionEngine.QueryMemberFacts;
  await runtime.runMain();
  onStatus("Querying package compile assets…");
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
    request.signature);
  return JSON.parse(json);
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
