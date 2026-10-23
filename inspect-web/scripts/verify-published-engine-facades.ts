import assert from "node:assert/strict";
import {
  readFileSync,
  writeFileSync,
} from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { publishedRuntimeTarget } from "./publish-runtime-loader.ts";

interface FacadeIdentity {
  readonly assembly: string;
  readonly module: string;
}

interface JsExportRuntime {
  readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
  readonly runMain: (
    mainAssemblyName?: string,
    args?: string[],
  ) => Promise<number>;
}

interface FacadeModule extends Record<string, unknown> {
  createRuntime(): Promise<JsExportRuntime>;
  initializeRuntime(
    runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>,
  ): Promise<void>;
  runEntryPoint(): Promise<number>;
}

interface HostFacade extends FacadeModule {
  asyncLoweringCanary(): Promise<string>;
  buildIdentity(): unknown;
  configureHost(origin: string): Promise<void>;
}

const productionFacades: readonly FacadeIdentity[] = [
  { assembly: "DotnetInspect.Web", module: "inspect-web-host" },
  {
    assembly: "DotnetInspect.Web.Interop.Package",
    module: "inspect-web-package",
  },
  {
    assembly: "DotnetInspect.Web.Interop.Library",
    module: "inspect-web-library",
  },
  {
    assembly: "DotnetInspect.Web.Interop.Metadata",
    module: "inspect-web-metadata",
  },
  {
    assembly: "DotnetInspect.Web.Interop.Analysis",
    module: "inspect-web-analysis",
  },
  {
    assembly: "DotnetInspect.Web.Interop.Source",
    module: "inspect-web-source",
  },
  {
    assembly: "DotnetInspect.Web.Interop.CallGraph",
    module: "inspect-web-call-graph",
  },
  {
    assembly: "DotnetInspect.Web.Interop.Catalog",
    module: "inspect-web-catalog",
  },
];

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isFacadeModule(value: unknown): value is FacadeModule {
  return isRecord(value)
    && typeof value.createRuntime === "function"
    && typeof value.initializeRuntime === "function"
    && typeof value.runEntryPoint === "function";
}

function isHostFacade(value: unknown): value is HostFacade {
  return isFacadeModule(value)
    && typeof value.asyncLoweringCanary === "function"
    && typeof value.buildIdentity === "function"
    && typeof value.configureHost === "function";
}

function isFacadeIdentity(value: unknown): value is FacadeIdentity {
  return isRecord(value)
    && typeof value.assembly === "string"
    && typeof value.module === "string";
}

function isFacadeIdentityArray(
  value: unknown,
): value is readonly FacadeIdentity[] {
  return Array.isArray(value) && value.every(isFacadeIdentity);
}

function operation(
  facade: FacadeModule,
  name: string,
): (...args: readonly unknown[]) => unknown {
  const candidate = facade[name];
  assert.ok(
    isPublishedOperation(candidate),
    `published facade does not export ${name}()`);
  return (...args: readonly unknown[]) =>
    Reflect.apply(candidate, facade, args);
}

type PublishedOperation = (...args: readonly unknown[]) => unknown;

function isPublishedOperation(value: unknown): value is PublishedOperation {
  return typeof value === "function";
}

function requiredFacade(
  loaded: ReadonlyMap<string, FacadeModule>,
  module: string,
): FacadeModule {
  const facade = loaded.get(module);
  assert.ok(facade, `published facade ${module}.js was not loaded`);
  return facade;
}

interface RuntimeObservation {
  readonly createCalls: number;
  readonly runtimeCount: number;
  readonly runMainCount: number;
}

function isRuntimeObservation(value: unknown): value is RuntimeObservation {
  return isRecord(value)
    && Number.isInteger(value.createCalls)
    && Number.isInteger(value.runtimeCount)
    && Number.isInteger(value.runMainCount);
}

async function withTimeout<T>(
  promise: Promise<T>,
  label: string,
): Promise<T> {
  const timeoutMilliseconds = Number(
    process.env.INSPECT_WEB_SMOKE_TIMEOUT_MS ?? 180_000);
  assert.ok(
    Number.isSafeInteger(timeoutMilliseconds) && timeoutMilliseconds > 0,
    "INSPECT_WEB_SMOKE_TIMEOUT_MS must be a positive integer");
  let timeout: NodeJS.Timeout | undefined;
  const expired = new Promise<never>((_, reject) => {
    timeout = setTimeout(
      () => reject(new Error(
        `${label} did not complete within ${timeoutMilliseconds} milliseconds`)),
      timeoutMilliseconds);
  });
  try {
    return await Promise.race([promise, expired]);
  } finally {
    if (timeout) clearTimeout(timeout);
  }
}

async function expectVisibleFailure(
  value: unknown,
  label: string,
): Promise<void> {
  try {
    await withTimeout(Promise.resolve(value), label);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    assert.equal(
      message.includes("did not complete within"),
      false,
      `${label} timed out instead of reporting its failure`);
    assert.ok(message.length > 0, `${label} reported an empty failure`);
    return;
  }
  assert.fail(`${label} unexpectedly succeeded for a rejected fixture coordinate`);
}

const [siteArgument, modeArgument, domainArgument, resultArgument] =
  process.argv.slice(2);
if (!siteArgument
    || (modeArgument !== "deployment" && modeArgument !== "production")) {
  throw new Error(
    "Usage: verify-published-engine-facades.ts "
      + "<published-wwwroot> <deployment|production> "
      + "[facade-domain.json] [smoke-result.json]");
}

const parsedDomain: unknown = domainArgument
  ? JSON.parse(readFileSync(resolve(domainArgument), "utf8"))
  : productionFacades;
assert.ok(
  isFacadeIdentityArray(parsedDomain),
  "published facade domain has an unexpected shape");
const facades = parsedDomain;
assert.deepEqual(
  [...facades].sort((left, right) => left.assembly.localeCompare(right.assembly)),
  [...productionFacades].sort(
    (left, right) => left.assembly.localeCompare(right.assembly)),
  "published facade domain does not match the production facade set");

const site = resolve(siteArgument);
const dotnetModule = publishedRuntimeTarget(site);
const stableDotnetModule = "./_framework/dotnet.js";
const runtimeLoader = resolve(site, "runtime-loader.js");
const originalLoader = readFileSync(runtimeLoader);
assert.equal(
  originalLoader.toString("utf8"),
  `export { dotnet } from ${JSON.stringify(stableDotnetModule)};\n`,
  "published runtime loader does not address the stable Worker runtime module");
assert.deepEqual(
  readFileSync(resolve(site, stableDotnetModule)),
  readFileSync(resolve(site, dotnetModule)),
  "stable Worker runtime module does not match the SDK import-map target");

try {
  writeFileSync(
    runtimeLoader,
    `import { dotnet as sdkDotnet } from ${JSON.stringify(stableDotnetModule)};
const runtimes = new Set();
let createCalls = 0;
let runMainCount = 0;
async function create(...args) {
  createCalls++;
  const runtime = await Reflect.apply(sdkDotnet.create, sdkDotnet, args);
  runtimes.add(runtime);
  return new Proxy(runtime, {
    get(target, property, receiver) {
      const member = Reflect.get(target, property, receiver);
      if (property !== "runMain" || typeof member !== "function") return member;
      return (...runArgs) => {
        runMainCount++;
        return Reflect.apply(member, target, runArgs);
      };
    },
  });
}
export const dotnet = new Proxy(sdkDotnet, {
  get(target, property, receiver) {
    return property === "create"
      ? create
      : Reflect.get(target, property, receiver);
  },
});
export function inspectWebRuntimeObservation() {
  return {
    createCalls,
    runtimeCount: runtimes.size,
    runMainCount,
  };
}
`);

  assert.equal(
    Reflect.has(globalThis, "window"),
    false,
    "published facade smoke must not depend on a window global");

  const loaded = new Map<string, FacadeModule>();
  for (const facade of facades) {
    const modulePath = resolve(site, `${facade.module}.js`);
    const source = readFileSync(modulePath, "utf8");
    assert.deepEqual(
      [...source.matchAll(/getAssemblyExports\("([^"]+)"\)/g)]
        .map(match => match[1]),
      [facade.assembly],
      `${facade.module}.js does not initialize its context-issued assembly`);
    const imported: unknown = await import(pathToFileURL(modulePath).href);
    assert.ok(
      isFacadeModule(imported),
      `${facade.module}.js has an unexpected public shape`);
    loaded.set(facade.module, imported);
  }

  let readiness: Promise<void> | undefined;
  function initializeFacades(): Promise<void> {
    readiness ??= (async () => {
      const ownerIdentity = facades.at(0);
      assert.ok(ownerIdentity, "published facade domain has no runtime owner");
      const owner = loaded.get(ownerIdentity.module)!;
      const runtime = owner.createRuntime();
      for (const facade of facades)
        await loaded.get(facade.module)!.initializeRuntime(runtime);
    })();
    return readiness;
  }
  await withTimeout(
    Promise.all([initializeFacades(), initializeFacades()]).then(() => undefined),
    "facade initialization");

  const sdkModule: unknown = await import(pathToFileURL(runtimeLoader).href);
  assert.ok(
    isRecord(sdkModule)
      && typeof sdkModule.inspectWebRuntimeObservation === "function",
    "published SDK observation module has an unexpected shape");
  const observationFunction = sdkModule.inspectWebRuntimeObservation;
  if (typeof observationFunction !== "function")
    throw new TypeError("published SDK observation is not callable");
  function observe(): RuntimeObservation {
    const observed: unknown = Reflect.apply(
      observationFunction,
      sdkModule,
      []);
    assert.ok(
      isRuntimeObservation(observed),
      "published SDK observation has an unexpected shape");
    return observed;
  }
  assert.deepEqual(
    observe(),
    { createCalls: 1, runtimeCount: 1, runMainCount: 0 },
    "published facades did not compose over exactly one SDK runtime");

  const host = loaded.get("inspect-web-host");
  assert.ok(isHostFacade(host), "published host facade has an unexpected shape");
  const canary = await host.asyncLoweringCanary();
  assert.equal(
    canary,
    "inspect-web-async-lowering-ok",
    "awaited lowering canary did not cross the host facade");

  const operations: string[] = [];
  let version = "deployment";
  if (modeArgument === "production") {
    await host.configureHost("https://dotnet-inspect.net");
    const identity: unknown = host.buildIdentity();
    assert.ok(
      isRecord(identity) && typeof identity.version === "string",
      "synchronous build identity did not cross the host facade");
    version = identity.version;
    operations.push("host.buildIdentity");

    const packageFacade = requiredFacade(loaded, "inspect-web-package");
    const libraryFacade = requiredFacade(loaded, "inspect-web-library");
    const metadataFacade = requiredFacade(loaded, "inspect-web-metadata");
    const analysisFacade = requiredFacade(loaded, "inspect-web-analysis");
    const sourceFacade = requiredFacade(loaded, "inspect-web-source");
    const callGraphFacade = requiredFacade(loaded, "inspect-web-call-graph");
    const catalogFacade = requiredFacade(loaded, "inspect-web-catalog");
    const packageId = "";
    const packageVersion = "1.0.0";
    const framework = "net11.0";

    const coordinate: unknown = operation(
      packageFacade,
      "matchPackageDependencyCoordinate",
    )(
      "Fixture.Root",
      "[1.0.0,2.0.0)",
      [{
        key: "fixture",
        provenance: "NuGetPackage",
        packageId: "Fixture.Root",
        version: "1.5.0",
        targetFramework: framework,
      }],
    );
    assert.ok(
      isRecord(coordinate) && coordinate.candidateKey === "fixture",
      "package facade did not resolve the bounded dependency coordinate");
    operations.push("package.matchPackageDependencyCoordinate");

    const uploadedLibrary: unknown = await operation(
      libraryFacade,
      "openUploadedLibrary",
    )("broken.dll", [0x4d, 0x5a, 0x00, 0x01]);
    assert.ok(
      isRecord(uploadedLibrary)
        && isRecord(uploadedLibrary.content)
        && uploadedLibrary.content.outcome === "Rejected",
      "library facade did not return a visible typed rejection");
    operations.push("library.openUploadedLibrary.visibleRejection");

    await expectVisibleFailure(
      operation(metadataFacade, "queryPackageMetadata")(
        packageId,
        packageVersion,
        framework),
      "metadata.queryPackageMetadata");
    operations.push("metadata.queryPackageMetadata.visibleFailure");

    await expectVisibleFailure(
      operation(analysisFacade, "queryPackageOpportunities")(
        packageId,
        packageVersion,
        framework),
      "analysis.queryPackageOpportunities");
    operations.push("analysis.queryPackageOpportunities.visibleFailure");

    const typeSourceFailure = await withTimeout(Promise.resolve(
      operation(sourceFacade, "queryTypeSource")(
        crypto.randomUUID(),
        packageId,
        packageVersion,
        framework,
        "Fixture.dll",
        "Fixture.Type",
        "[]",
        null,
        "source")),
      "source.queryTypeSource");
    assert.ok(
      typeof typeSourceFailure === "object" && typeSourceFailure !== null
        && "kind" in typeSourceFailure && typeSourceFailure.kind === "Failed"
        && "error" in typeSourceFailure && typeof typeSourceFailure.error === "string"
        && typeSourceFailure.error.length > 0,
      "source.queryTypeSource must report an explicit managed failure");
    operations.push("source.queryTypeSource.visibleFailure");

    await expectVisibleFailure(
      operation(callGraphFacade, "queryMemberCallGraph")(
        packageId,
        packageVersion,
        framework,
        "Fixture.dll",
        "Fixture.Type",
        "Fixture.Type",
        "Call",
        "void Call()",
        "method:Call",
        1,
        "[]"),
      "callGraph.queryMemberCallGraph");
    operations.push("callGraph.queryMemberCallGraph.visibleFailure");

    function queryExplanation(path: string, keys: readonly string[] | null): Record<string, unknown> {
      const result = operation(packageFacade, "explainPackageQuery")(path, keys);
      assert.ok(isRecord(result) && result.outcome === "Explained" && isRecord(result.content),
        `package query explanation failed: ${path}`);
      return result.content;
    }
    const queryRoot = "package-query/query";
    const fullQuery = queryExplanation(queryRoot, null);
    const selectedQuery = queryExplanation(queryRoot, ["depends", "dependency-target"]);
    assert.ok(isRecord(fullQuery._embedded) && Array.isArray(fullQuery._embedded.facets));
    assert.ok(isRecord(selectedQuery._embedded) && Array.isArray(selectedQuery._embedded.facets));
    const fullFacets = fullQuery._embedded.facets;
    assert.deepEqual(selectedQuery._embedded.facets.map(facet => {
      assert.ok(isRecord(facet));
      assert.deepEqual(facet, fullFacets.find(original =>
        isRecord(original) && original.key === facet.key));
      return facet.key;
    }), ["dependency-target", "depends"]);
    assert.ok(isRecord(selectedQuery._links) && isRecord(selectedQuery._links.collection));
    assert.ok(typeof selectedQuery._links.collection.href === "string");
    assert.deepEqual(queryExplanation(selectedQuery._links.collection.href, null), fullQuery);
    const directQuery = queryExplanation(`inspect-resource:/${queryRoot}?projection=data`,
      ["depends", "dependency-target"]);
    assert.ok(isRecord(directQuery.facets));
    assert.deepEqual(Object.keys(directQuery.facets).sort(), ["dependency-target", "depends"]);
    const literalQuery = queryExplanation(queryRoot, ["library-literal"]);
    assert.ok(isRecord(literalQuery._embedded) && Array.isArray(literalQuery._embedded.facets));
    assert.deepEqual(literalQuery._embedded.facets.map(facet => {
      assert.ok(isRecord(facet));
      return facet.key;
    }), ["library-literal", "library-target"]);
    const invalidQuery = operation(packageFacade, "explainPackageQuery")(queryRoot, ["unknown"]);
    assert.ok(isRecord(invalidQuery) && invalidQuery.outcome === "InvalidSelection"
      && invalidQuery.content === null && typeof invalidQuery.message === "string");
    operations.push("package.explainPackageQuery.selectedContextAndNavigation");

    const vocabulary: unknown = operation(catalogFacade, "inspectVocabulary")();
    assert.ok(
      isRecord(vocabulary),
      "catalog facade did not return the product vocabulary");
    operations.push("catalog.inspectVocabulary");

    function explanation(path: string, depth = 0,
      valueIds: readonly string[] | null = null): Record<string, unknown> {
      const result = operation(catalogFacade, "explainVocabularies")(path, depth, valueIds);
      assert.ok(isRecord(result) && result.outcome === "Explained"
        && isRecord(result.explanation) && isRecord(result.explanation.content),
      `catalog explanation failed: ${path}`);
      assert.ok(isRecord(result.explanation.share));
      assert.deepEqual(result.explanation.diagnostics, []);
      return result.explanation.content;
    }
    const resource = "vocabularies/csharp.style-choices";
    const compact = explanation(resource, 1);
    assert.ok(isRecord(compact.facts) && isRecord(compact._links));
    assert.equal(Reflect.has(compact, "schemas"), false);
    const contract = explanation(`inspect-resource:/${resource}?projection=contract`, 1);
    assert.ok(Array.isArray(contract.schemas) && Array.isArray(contract.resources));
    assert.ok(JSON.stringify(compact).length < JSON.stringify(contract).length);
    const data = explanation(`inspect-resource:/${resource}?projection=data`);
    assert.ok(isRecord(data.vocabularies));
    const choices = data.vocabularies["csharp.style-choices"];
    assert.ok(isRecord(choices) && isRecord(choices.values)
      && Object.keys(choices.values).length > 0 && isRecord(choices.property_sets));
    const hal = explanation(`inspect-resource:/${resource}?projection=hal`);
    function halVocabulary(content: Record<string, unknown>, id: string): Record<string, unknown> {
      assert.ok(isRecord(content._embedded) && Array.isArray(content._embedded.vocabularies));
      const entries: readonly unknown[] = content._embedded.vocabularies;
      const found = entries.find(value => isRecord(value) && value.id === id);
      assert.ok(isRecord(found));
      return found;
    }
    const halChoices = halVocabulary(hal, "csharp.style-choices");
    assert.ok(isRecord(halChoices._embedded) && Array.isArray(halChoices._embedded.values));
    assert.equal(halChoices._embedded.values.length, Object.keys(choices.values).length);
    function hrefs(value: unknown): string[] {
      if (Array.isArray(value)) return value.flatMap(hrefs);
      if (!isRecord(value)) return [];
      return Object.entries(value).flatMap(([name, child]) =>
        name === "href" && typeof child === "string" ? [child] : hrefs(child));
    }
    const links = [...new Set(hrefs(hal))];
    assert.equal(links.length, 1);
    for (const href of links) {
      const followed = explanation(href);
      if (href.endsWith("?projection=hal")) assert.ok(isRecord(followed.scope));
      if (href.endsWith("?projection=contract")) assert.ok(Array.isArray(followed.schemas));
    }
    const address = `inspect-resource:/${resource}?projection=hal`;
    const selected = explanation(address, 0, ["slot-local-names"]);
    const selectedChoices = halVocabulary(selected, "csharp.style-choices");
    assert.ok(isRecord(selectedChoices._embedded) && Array.isArray(selectedChoices._embedded.values));
    assert.deepEqual(selectedChoices._embedded.values,
      halChoices._embedded.values.filter(value => isRecord(value) && value.id === "slot-local-names"));
    const selectedTiers = halVocabulary(selected, "csharp.style-tiers");
    assert.ok(isRecord(selectedTiers._embedded) && Array.isArray(selectedTiers._embedded.values));
    assert.deepEqual(selectedTiers._embedded.values.map(value => {
      assert.ok(isRecord(value));
      return value.id;
    }), ["Synthesis"]);
    assert.ok(isRecord(selected._links) && isRecord(selected._links.collection));
    assert.equal(selected._links.collection.href, address);
    assert.deepEqual(explanation(address), hal);
    const selectedData = explanation(`inspect-resource:/${resource}?projection=data`, 0, ["slot-local-names"]);
    assert.ok(isRecord(selectedData.vocabularies));
    const selectedDirectChoices = selectedData.vocabularies["csharp.style-choices"];
    assert.ok(isRecord(selectedDirectChoices) && isRecord(selectedDirectChoices.values));
    assert.deepEqual(Object.keys(selectedDirectChoices.values), ["slot-local-names"]);
    assert.deepEqual(selectedDirectChoices.values["slot-local-names"], choices.values["slot-local-names"]);
    const body = explanation("inspect-resource:/vocabularies/csharp.body-kinds?projection=hal", 0,
      ["ObjectCreationExpression", "AddressExpression", "ObjectCreationExpression"]);
    const bodyKinds = halVocabulary(body, "csharp.body-kinds");
    assert.ok(isRecord(bodyKinds._embedded) && Array.isArray(bodyKinds._embedded.values));
    assert.deepEqual(bodyKinds._embedded.values.map(value => {
      assert.ok(isRecord(value));
      return value.id;
    }), ["AddressExpression", "ObjectCreationExpression"]);
    for (const [path, depth, ids] of [
      [address, 1, null], [address, 0, []], [address, 0, ["unknown"]],
      ["inspect-resource:/vocabularies/csharp.body-kinds?projection=hal", 0, ["objectcreationexpression"]],
      [resource, 0, ["slot-local-names"]],
    ] as const) {
      const rejected = operation(catalogFacade, "explainVocabularies")(path, depth, ids);
      assert.ok(isRecord(rejected) && rejected.outcome === "InvalidSelection"
        && rejected.explanation === null && isRecord(rejected.rejection));
    }
    operations.push("catalog.explainVocabularies.compactSelectedContractAndNavigation");

    assert.equal(await host.runEntryPoint(), 0);
    assert.deepEqual(
      observe(),
      { createCalls: 1, runtimeCount: 1, runMainCount: 1 },
      "production smoke did not run the one host entry point exactly once");
    operations.push("host.runEntryPoint");
  }

  if (resultArgument) {
    writeFileSync(
      resolve(resultArgument),
      `${JSON.stringify({
        initialized_facades: facades,
        sdk_create_count: observe().createCalls,
        sdk_runtime_count: observe().runtimeCount,
        entry_point_count: observe().runMainCount,
        async_lowering_canary: canary,
        production_operations: operations,
      })}\n`);
  }

  console.log(
    `inspect-web published ${modeArgument} facade smoke passed `
      + `(${version}; ${facades.length} facades; one SDK runtime).`);
} finally {
  writeFileSync(runtimeLoader, originalLoader);
}
