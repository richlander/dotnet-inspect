// The application's one runtime composition point for the production facade set.
//
// Seven independently generated modules attach to one Browser/Wasm runtime through the one
// shared `./_framework/dotnet.js` module. Startup is eager, ordered and serial: every facade
// acquires its own managed export assembly before any application operation is published as
// ready. Concurrent callers share one attempt, and the first failure is the failure every
// later caller observes, so a stale module or a missing export root fails the application
// visibly instead of leaving it partially initialized.
//
// Nothing here re-exports a managed operation. Application code calls each of the 48
// operations through the generated module that owns it; this module owns only composition.
//
// The published modules are served beside `_framework/` at the site root, so they are named
// by their absolute runtime specifier and loaded as modules rather than bundled.
let readiness: Promise<void> | undefined;
let startup: Promise<void> | undefined;

function hostFacade() {
  return import("/inspect-web-host.js");
}

async function initializeFacadeSet(): Promise<void> {
  const [
    host,
    packageFacade,
    metadataFacade,
    analysisFacade,
    sourceFacade,
    callGraphFacade,
    catalogFacade,
  ] = await Promise.all([
    hostFacade(),
    import("/inspect-web-package.js"),
    import("/inspect-web-metadata.js"),
    import("/inspect-web-analysis.js"),
    import("/inspect-web-source.js"),
    import("/inspect-web-call-graph.js"),
    import("/inspect-web-catalog.js"),
  ]);
  const runtime = host.createRuntime();
  await host.initializeRuntime(runtime);
  await packageFacade.initializeRuntime(runtime);
  await metadataFacade.initializeRuntime(runtime);
  await analysisFacade.initializeRuntime(runtime);
  await sourceFacade.initializeRuntime(runtime);
  await callGraphFacade.initializeRuntime(runtime);
  await catalogFacade.initializeRuntime(runtime);
}

// The retained promise is the single-flight record: a rejected attempt is retained too, so a
// second caller observes the first failure rather than starting a second runtime.
export function initializeFacades(): Promise<void> {
  readiness ??= initializeFacadeSet();
  return readiness;
}

async function startEngineCore(origin: string): Promise<void> {
  await initializeFacades();
  // Host policy is configured before the entry point starts application work, and only the
  // host facade's entry point runs.
  const host = await hostFacade();
  host.configureHost(origin);
  await host.runEntryPoint();
}

export function startEngine(origin: string): Promise<void> {
  startup ??= startEngineCore(origin);
  return startup;
}
