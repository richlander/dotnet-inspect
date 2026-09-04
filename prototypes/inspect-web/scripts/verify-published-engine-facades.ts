import assert from "node:assert/strict";
import {
  existsSync,
  readFileSync,
  unlinkSync,
  writeFileSync,
} from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

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
  configureHost(origin: string): void;
}

const productionFacades: readonly FacadeIdentity[] = [
  { assembly: "InspectWeb.Engine", module: "inspect-web-host" },
  {
    assembly: "InspectWeb.Engine.PackageExports",
    module: "inspect-web-package",
  },
  {
    assembly: "InspectWeb.Engine.MetadataExports",
    module: "inspect-web-metadata",
  },
  {
    assembly: "InspectWeb.Engine.AnalysisExports",
    module: "inspect-web-analysis",
  },
  {
    assembly: "InspectWeb.Engine.SourceExports",
    module: "inspect-web-source",
  },
  {
    assembly: "InspectWeb.Engine.CallGraphExports",
    module: "inspect-web-call-graph",
  },
  {
    assembly: "InspectWeb.Engine.CatalogExports",
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
const index = readFileSync(resolve(site, "index.html"), "utf8");
const dotnetModule = /"\.\/_framework\/dotnet\.js": "\.\/_framework\/([^"]+\.js)"/
  .exec(index)?.[1];
assert.ok(dotnetModule, "published import map has no dotnet.js mapping");

const frameworkDirectory = resolve(site, "_framework");
const dotnetAlias = resolve(frameworkDirectory, "dotnet.js");
assert.equal(
  existsSync(dotnetAlias),
  false,
  "published framework unexpectedly contains an unhashed dotnet.js");
writeFileSync(
  dotnetAlias,
  `import { dotnet as sdkDotnet } from "./${dotnetModule}";
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

try {
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

  const sdkModule: unknown = await import(pathToFileURL(dotnetAlias).href);
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
    host.configureHost("https://dotnet-inspect.net");
    const identity: unknown = host.buildIdentity();
    assert.ok(
      isRecord(identity) && typeof identity.version === "string",
      "synchronous build identity did not cross the host facade");
    version = identity.version;
    operations.push("host.buildIdentity");

    const packageFacade = requiredFacade(loaded, "inspect-web-package");
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
      JSON.stringify([{
        key: "fixture",
        provenance: "NuGetPackage",
        packageId: "Fixture.Root",
        version: "1.5.0",
        targetFramework: framework,
      }]),
    );
    assert.ok(
      isRecord(coordinate) && coordinate.candidateKey === "fixture",
      "package facade did not resolve the bounded dependency coordinate");
    operations.push("package.matchPackageDependencyCoordinate");

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

    await expectVisibleFailure(
      operation(sourceFacade, "queryTypeSource")(
        packageId,
        packageVersion,
        framework,
        "Fixture.dll",
        "Fixture.Type",
        "[]"),
      "source.queryTypeSource");
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

    const vocabulary: unknown = operation(catalogFacade, "listVocabulary")();
    assert.ok(
      isRecord(vocabulary),
      "catalog facade did not return the product vocabulary");
    operations.push("catalog.listVocabulary");

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
  unlinkSync(dotnetAlias);
}
