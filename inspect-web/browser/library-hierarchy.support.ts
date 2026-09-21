import { expect, type Page } from "@playwright/test";
import { randomUUID } from "node:crypto";
import { readdir, readFile } from "node:fs/promises";
import { basename } from "node:path";
import { fileURLToPath } from "node:url";
import type {
  BrowserAssemblySurface,
  BrowserMemberSurface,
  BrowserPackageSurface,
  BrowserTypeSurface,
} from "../src/facades/inspect-web-package.d.ts";
import type {
  BrowserCallGraph,
  BrowserHomeDemoCatalogEntry,
  BrowserHomeDemoRunResult,
} from "../src/facades/inspect-web-catalog.d.ts";
import type { PlatformAssemblyRow, PlatformCatalogTarget } from "../src/platform-index.ts";

function subjectTab(page: Page, subject: string) {
  return page.locator(`[data-subject-tab][data-scope="${subject}"]`);
}

function inspectorTab(page: Page, attribute: string, inspector: string) {
  return page.locator(
    `[data-inspector-tab][${attribute}="${inspector}"]`,
  );
}

async function chooseInspector(
  page: Page,
  attribute: string,
  inspector: string,
  label: string,
) {
  const tab = inspectorTab(page, attribute, inspector);
  const trigger = page.locator("[data-navigation-trigger='inspector']");
  await expect.poll(async () =>
    await tab.isVisible() || await trigger.isVisible()).toBe(true);
  if (await tab.isVisible()) {
    await tab.click();
    return;
  }

  await trigger.click();
  await page.locator("#inspector-navigation-menu")
    .locator(`[${attribute}="${inspector}"]`)
    .click();
  if (await trigger.isVisible()) {
    await expect(trigger).toHaveAccessibleName(label);
  } else {
    await expect(tab).toHaveAttribute("aria-selected", "true");
  }
}

async function chooseSubject(page: Page, subject: string, label: string) {
  const tab = subjectTab(page, subject);
  const trigger = page.locator("[data-navigation-trigger='subject']");
  await expect.poll(async () =>
    await tab.isVisible() || await trigger.isVisible()).toBe(true);
  if (await tab.isVisible()) {
    await tab.click();
  } else {
    await trigger.click();
    await page.locator("#subject-navigation-menu")
      .locator(`[data-scope="${subject}"]`)
      .click();
  }
  await expect(tab).toHaveAttribute("aria-selected", "true");
  await expectCurrentSubjectVisible(page, subject, label);
}

async function selectLibrary(page: Page, libraryId: string) {
  if (await subjectTab(page, "library").getAttribute("aria-selected") !== "true")
    await chooseSubject(page, "library", "Library");
  const row = page.locator(
    `.library-subject-list [data-library-subject="${libraryId}"]`);
  const navigationToggle = page.getByRole(
    "button",
    { name: "Libraries", exact: true });
  await expect.poll(async () =>
    await row.isVisible() || await navigationToggle.isVisible()).toBe(true);
  if (!await row.isVisible()) await navigationToggle.click();
  await row.click();
  await expect(row).toHaveAttribute("aria-selected", "true");
}

async function expectCurrentSubjectVisible(
  page: Page,
  subject: string,
  label: string,
) {
  const tab = subjectTab(page, subject);
  const trigger = page.locator("[data-navigation-trigger='subject']");
  await expect.poll(async () =>
    await tab.isVisible() || await trigger.isVisible()).toBe(true);
  if (await tab.isVisible()) {
    await expect(tab).toBeInViewport({ ratio: 1 });
    return;
  }

  await expect(trigger).toHaveAccessibleName(label);
}

function library(id: string, name: string, count: number): BrowserAssemblySurface {
  return {
    id,
    name,
    version: "1.0.0.0",
    culture: null,
    publicKeyToken: null,
    asset: `lib/net10.0/${name}.dll`,
    publicTypes: count,
    publicMembers: count,
    platformPack: null,
  };
}

const run: BrowserMemberSurface = {
  name: "Run",
  kind: "method",
  signature: "public void Run()",
  accessibility: "public",
  isStatic: false,
  isUnsafe: false,
  isVirtual: false,
  isAbstract: false,
  isOverride: false,
  isExtension: false,
  isObsolete: false,
  genericArity: 0,
  metadataToken: 0x06000001,
  declarationMetadataToken: 0x06000001,
  returnType: "void",
  parameters: [],
  documentationId: "M:Example.Widget.Run",
  summary: "Runs the widget.",
  returns: null,
  exceptions: [],
  stableSelector: "Run",
  anchorDigest: "widget-run",
  canonicalSignature: "void Example.Widget.Run()",
  anchorTypeFullName: "Example.Widget",
  graphSelectorKey: "Run",
  bodySelectors: [{ token: 0x06000001, memberName: "Run", selectorKey: "Run" }],
};

function type(id: string, assembly: BrowserAssemblySurface): BrowserTypeSurface {
  return {
    id: `${assembly.id}:${id}`,
    definitionId: id,
    queryId: id,
    metadataId: id,
    name: id.split(".").at(-1)!,
    displayName: id,
    namespace: "Example",
    kind: "class",
    accessibility: "public",
    accessibilityId: "public",
    assembly: `${assembly.name}.dll`,
    assemblyId: assembly.id,
    assemblyName: assembly.name,
    members: 1,
    signature: `public class ${id}`,
    api: [run],
    platformPack: null,
  };
}

const core = library("asset:core", "Example.Core", 1);
const other = library("asset:other", "Example.Other", 1);
const empty = library("asset:empty", "Example.Empty", 0);
const surface: BrowserPackageSurface = {
  package: "Example.Package",
  version: "1.0.0",
  frameworks: ["net10.0"],
  activeFramework: "net10.0",
  defaultAssemblyId: core.id,
  compileLibrary: { status: "Selected", targetFramework: "net10.0", message: null },
  assemblies: [core, other, empty],
  types: [type("Example.Widget", core), type("Example.Neighbor", other)],
  accessibility: [{ id: "public", label: "Public", order: 0, isDefault: true, count: 2 }],
  totalMembers: 2,
  documents: [],
  icon: null,
  inspectionErrors: [],
  inspectionError: null,
};

const platformVersion = "11.0.0-preview.7.26381.103";
const alternatePlatformVersion = "11.0.0-preview.8.26401.1";
function platformRow(assembly: string, kind: PlatformAssemblyRow["kind"], inReferencePack = true, hasImplementation = true): PlatformAssemblyRow {
  return {
    tfm: "net11.0", pack: "netcore.app", assembly, file: `${assembly}.dll`,
    kind, inReferencePack, hasImplementation, forwardsTo: kind === "facade" ? "System.Text.Json" : null,
    version: "11.0.0.0", packVersion: platformVersion, publicTypes: assembly === "System.Text.Json" ? 1 : 0,
  };
}
const platformTarget: PlatformCatalogTarget = {
  tfm: "net11.0", version: platformVersion,
  supplies: [],
  rows: [
    platformRow("System.Text.Json", "impl"),
    platformRow("System.Facade", "facade"),
    platformRow("System.Private.Empty", "impl", false),
    platformRow("System.ReferenceOnly", "ref", true, false),
  ],
};
const historicalPlatformTarget: PlatformCatalogTarget = {
  tfm: "netstandard2.1", version: "2.1.0",
  supplies: [],
  rows: [{
    ...platformRow("netstandard", "ref", true, false),
    tfm: "netstandard2.1", pack: "netstandard", packVersion: "2.1.0", version: "2.1.0.0",
  }],
};
interface PlatformFixture {
  warmup?: "pending" | "fail-once";
  discoveryFailure?: boolean;
  catalogFailure?: boolean;
  catalogPending?: boolean;
  wrongCatalog?: boolean;
  libraryFailure?: boolean;
  libraryPending?: boolean;
  libraryPendingPack?: PlatformAssemblyRow["pack"];
  duplicateLibrary?: boolean;
  nativeCoreLib?: boolean;
  mismatchedFile?: boolean;
}

interface HomeDemoFixture {
  catalog: readonly BrowserHomeDemoCatalogEntry[];
  results: Readonly<Record<string, BrowserHomeDemoRunResult>>;
  catalogPending?: boolean;
}

interface DiagnosticsFixture {
  runtimeFailure?: boolean;
  buildIdentity?: "ready" | "pending" | "failed";
  cacheFailure?: boolean;
  cachePending?: boolean;
  libraryApiFailure?: boolean;
  libraryApiIncomplete?: boolean;
}

interface PackageLoadingFixture {
  deferInitial?: boolean;
  deferChanges?: boolean;
  failFrameworkOnce?: string;
  failVersionOnce?: string;
  versions?: readonly string[];
  activityCatalogFailure?: boolean;
  libraryQuery?: "ready" | "empty" | "partial" | "deferred-error";
}

type LibraryUploadFixture = "available" | "rejected" | "deferred";

// Exercise the production composition root and bindings with deterministic facade
// responses. Codec and participant-query behavior have separate engine outcome gates.
async function installFacades(
  page: Page,
  model = surface,
  additionalSurfaces: readonly BrowserPackageSurface[] = [],
  references: "ready" | "long" | "empty" | "query-error" | "inspection-error" | "deferred" = "ready",
  integrations: "ready" | "long" | "empty" | "partial" | "partial-empty" | "query-error" | "deferred" = "ready",
  platform?: PlatformFixture,
  opportunities: "ready" | "long" | "empty" | "partial" | "partial-empty" | "query-error" | "deferred" = "ready",
  analysis: "ready" | "long" | "empty" | "partial" | "partial-empty" | "query-error" | "deferred" = "ready",
  homeDemos?: HomeDemoFixture,
  diagnostics: DiagnosticsFixture = {},
  packageLoading: PackageLoadingFixture = {},
  libraryUpload: LibraryUploadFixture = "available",
) {
  const catalogTarget: PlatformCatalogTarget = {
    ...platformTarget,
    rows: [
      ...platformTarget.rows.map(row => platform?.mismatchedFile && row.assembly === "System.Text.Json"
        ? { ...row, file: "PhysicalPayload.dll" } : row),
      ...(platform?.duplicateLibrary ? [{ ...platformTarget.rows[0]!, pack: "aspnetcore.app" as const }] : []),
      ...(platform?.nativeCoreLib ? [{ ...platformRow("System.Private.CoreLib", "impl", false), publicTypes: 1 }] : []),
    ],
  };
  const fixtureChannelName = `inspect-web-library-fixture-${randomUUID()}`;
  await page.addInitScript(channelName => {
    const channel = new BroadcastChannel(channelName);
    channel.addEventListener("message", event => {
      const message: unknown = event.data;
      if (typeof message !== "object"
        || message === null
        || !("kind" in message)
        || !("name" in message)
        || !("value" in message)
        || message.kind !== "observe"
        || typeof message.name !== "string"
        || typeof message.value !== "string") return;
      document.documentElement.dataset[message.name] = message.value;
    });
    document.documentElement.dataset.fixtureChannelName = channelName;
  }, fixtureChannelName);
  const common = `
    const fixtureChannel = new BroadcastChannel(${JSON.stringify(fixtureChannelName)});
    const fixtureListeners = new Map();
    fixtureChannel.addEventListener("message", event => {
      const message = event.data;
      if (message?.kind !== "release") return;
      const listeners = fixtureListeners.get(message.name);
      if (!listeners) return;
      fixtureListeners.delete(message.name);
      for (const listener of listeners) listener();
    });
    const document = {
      documentElement: {
        dataset: new Proxy({}, {
          set(_target, name, value) {
            fixtureChannel.postMessage({
              kind: "observe",
              name: String(name),
              value: String(value),
            });
            return true;
          },
        }),
      },
      addEventListener(name, listener) {
        const listeners = fixtureListeners.get(name) ?? [];
        listeners.push(listener);
        fixtureListeners.set(name, listeners);
      },
    };
    export async function initializeRuntime() {}
  `;
  const surfaceLookup = `
    const surfaces = ${JSON.stringify([model, ...additionalSurfaces])};
    function surfaceFor(id, version, framework) {
      return surfaces.find(item => item.package === id
        && (!version || item.version === version)
        && (!framework || item.activeFramework === framework))
        ?? surfaces.find(item => item.package === id) ?? surfaces[0];
    }`;
  const uploadDigest = "a".repeat(64);
  const uploadAssemblyId = `sha256:${uploadDigest}`;
  const uploadAssembly = {
    id: uploadAssemblyId,
    name: "Uploaded.Library",
    version: "1.0.0.0",
    culture: null,
    publicKeyToken: null,
    asset: "Uploaded.Library.dll",
    publicTypes: model.types.length,
    publicMembers: model.totalMembers,
    platformPack: null,
  };
  const availableUploadInspection = {
    content: {
      outcome: "Available",
      declaredName: uploadAssembly.asset,
      digest: uploadDigest,
      byteLength: 4,
      provenance: {
        contentRef: "browser-upload",
        digest: uploadAssemblyId,
        declaredName: uploadAssembly.asset,
      },
      assembly: {
        name: uploadAssembly.name,
        version: uploadAssembly.version,
        culture: null,
        publicKeyToken: null,
      },
      surface: {
        assemblies: [uploadAssembly],
        types: model.types.map(item => ({
          ...item,
          assembly: uploadAssembly.asset,
          assemblyId: uploadAssemblyId,
          assemblyName: uploadAssembly.name,
        })),
        accessibility: model.accessibility,
        totalMembers: model.totalMembers,
        inspectionErrors: [],
        inspectionError: null,
        isTruncated: false,
      },
      inspectionFailures: [],
      failure: null,
      isComplete: true,
    },
    share: {
      kind: "NonProjectable",
      fullUrl: null,
      packet: null,
      path: "embedded-library/share",
      reason: "Uploaded bytes are session-local.",
    },
    diagnostics: [],
  };
  const rejectedUploadInspection = {
    ...availableUploadInspection,
    content: {
      ...availableUploadInspection.content,
      outcome: "Rejected",
      provenance: null,
      assembly: null,
      surface: null,
      failure: {
        kind: "InvalidImage",
        detail: "The dropped file is not a managed assembly.",
      },
      isComplete: false,
    },
  };
  const uploadInspection = libraryUpload === "rejected"
    ? rejectedUploadInspection
    : availableUploadInspection;
  const graphTargetType =
    model.types.find(item => item.queryId === "Example.Neighbor") ?? null;
  const graphTargetLibrary = graphTargetType
    ? model.assemblies.find(item => item.id === graphTargetType.assemblyId) ?? null
    : null;
  const graphTarget = graphTargetType && graphTargetLibrary
    ? {
        id: "n1",
        assembly: graphTargetLibrary.name,
        assemblyVersion: graphTargetLibrary.version,
        assemblyCulture: graphTargetLibrary.culture,
        assemblyPublicKeyToken: graphTargetLibrary.publicKeyToken,
        typeFullName: graphTargetType.queryId,
        typeMetadataId: graphTargetType.metadataId,
        typeDefinitionId: graphTargetType.definitionId,
        memberName: "Run",
        parameterTypes: [],
        returnType: "void",
        genericArity: 0,
        metadataToken: 0x06000001,
        selectorKey: "Run",
        kind: "method",
        platformPack: null,
        surfaceAssemblyId: graphTargetLibrary.id,
      }
    : null;
  const graphTargetNode = graphTarget
    ? {
        label: "Neighbor.Run",
        status: "Analyzed",
        inLoop: false,
        source: null,
        children: [],
        assembly: graphTarget.assembly,
        typeFullName: graphTarget.typeFullName,
        memberName: graphTarget.memberName,
      }
    : null;
  const fixtureCallGraph: BrowserCallGraph = {
    mermaid: graphTarget
      ? 'flowchart TD\n  n0["Widget.Run"] --> n1["Neighbor.Run"]'
      : 'flowchart TD\n  n0["Widget.Run"]',
    callers: {
      label: "Widget.Run",
      status: "Analyzed",
      inLoop: false,
      source: null,
      children: [],
      assembly: core.name,
      typeFullName: "Example.Widget",
      memberName: "Run",
    },
    callees: {
      label: "Widget.Run",
      status: "Analyzed",
      inLoop: false,
      source: null,
      children: graphTargetNode ? [graphTargetNode] : [],
      assembly: core.name,
      typeFullName: "Example.Widget",
      memberName: "Run",
    },
    scope: {
      packages: 1,
      assemblies: model.assemblies.length,
      callerAssemblies: model.assemblies.length,
      calleeScope: "Workspace",
    },
    targets: graphTarget ? [graphTarget] : [],
    diagnostics: {
      incompleteNodes: 0,
      incompleteEdges: 0,
      bindingIdentityConflicts: 0,
      hasUnexploredTraversalBoundary: false,
      hasAnalysisFailureBoundary: false,
      isIncomplete: false,
    },
    noBody: false,
  };
  const modules: Record<string, string> = {
    host: `
      const diagnosticsOptions = ${JSON.stringify(diagnostics)};
      export async function createRuntime() {
        if (diagnosticsOptions.runtimeFailure) {
          throw new Error("Runtime unavailable");
        }
        return {};
      }
      export function configureHost() {}
      export async function runEntryPoint() { return 0; }
      export function registerEpochWorkReporter() {}
      export async function drainEpochWorkReporter() {}
      export function unregisterEpochWorkReporter() {}
      export async function asyncLoweringCanary() {
        if (diagnosticsOptions.runtimeFailure) {
          throw new Error("Runtime unavailable");
        }
        return "inspect-web-async-lowering-ok";
      }
      export async function buildIdentity() {
        if (diagnosticsOptions.buildIdentity === "pending") {
          document.documentElement.dataset.buildIdentityPending = "true";
          await new Promise(resolve => document.addEventListener(
            "finish-build-identity", resolve, { once: true }));
        }
        if (diagnosticsOptions.buildIdentity === "failed") {
          throw new Error("Build identity unavailable");
        }
        return {
          version: "fixture",
          commit: "0123456789abcdef0123456789abcdef01234567",
          builtAtUtc: "2026-01-23T15:41:12Z",
          commitUrl: "https://github.com/richlander/dotnet-inspect/commit/0123456789abcdef0123456789abcdef01234567",
        };
      }`,
    package: `
      ${surfaceLookup}
      const platformTarget = ${JSON.stringify(catalogTarget)};
      const platformOptions = ${JSON.stringify(platform ?? {})};
      const diagnosticsOptions = ${JSON.stringify(diagnostics)};
      const packageLoading = ${JSON.stringify(packageLoading)};
      let packageFrameworkFailed = false;
      let packageVersionFailed = false;
      let warmupAttempts = 0;
      let libraryApiAttempts = 0;
      export async function getPlatformVersions(tfm) {
        document.documentElement.dataset.platformVersionsRequest = tfm;
        if (platformOptions.discoveryFailure) throw new Error("Version discovery offline");
        return ["${alternatePlatformVersion}", "${platformVersion}"];
      }
      export async function getPlatformCatalog(tfm, version) {
        document.documentElement.dataset.platformCatalogRequest = JSON.stringify([tfm, version]);
        if (platformOptions.catalogPending) await new Promise(resolve => document.addEventListener("finish-platform-catalog", resolve, { once: true }));
        if (platformOptions.catalogFailure) throw new Error("Catalog offline");
        const actualVersion = platformOptions.wrongCatalog ? platformTarget.version : version;
        return {
          tfm, version: actualVersion,
          rows: platformTarget.rows.map(row => ({
            ...row, tfm, packVersion: actualVersion,
            assembly: row.assembly === "System.Facade" ? "System.NewFacade" : row.assembly,
            file: row.assembly === "System.Facade" ? "System.NewFacade.dll" : row.file,
          }))
        };
      }
      export async function prefetchPlatformPacks(tfm, version) {
        document.documentElement.dataset.platformWarmup = JSON.stringify([tfm, version]);
        document.documentElement.dataset.platformWarmupAttempts = String(++warmupAttempts);
        if (platformOptions.warmup === "pending") await new Promise(resolve => document.addEventListener("finish-platform-warmup", resolve, { once: true }));
        if (platformOptions.warmup === "fail-once" && warmupAttempts === 1) throw new Error("Archive offline");
      }
      export async function loadRuntimePackAssembly(tfm, version, file, pack, assetFileName = file) {
        if (pack === undefined) {
          const surface = surfaceFor("Microsoft.NETCore.App");
          const selected = surface.assemblies.find(item => item.name + ".dll" === file);
          if (!selected) throw new Error("Unknown platform library: " + file);
          return JSON.stringify({
            ...surface,
            defaultAssemblyId: selected.id,
            activeFramework: tfm,
            version: version || surface.version,
          });
        }
        document.documentElement.dataset.platformLibraryRequest = JSON.stringify([tfm, version, file, pack, assetFileName]);
        if (platformOptions.libraryPending
          && (!platformOptions.libraryPendingPack || platformOptions.libraryPendingPack === pack)) {
          await new Promise(resolve => document.addEventListener("finish-platform-library", resolve, { once: true }));
        }
        if (platformOptions.libraryFailure) throw new Error("Library offline");
        const row = platformTarget.rows.find(row => row.assembly + ".dll" === file && row.pack === pack)
          ?? { assembly: file.replace(/\\.dll$/, ""), file, publicTypes: 0 };
        const assembly = {
          id: row.assembly, name: row.assembly, version: "11.0.0.0",
          culture: null, publicKeyToken: null,
          asset: file === "System.Private.CoreLib.dll" ? "runtimes/linux-x64/native/" + assetFileName : assetFileName,
          publicTypes: row.publicTypes,
          publicMembers: row.publicTypes, platformPack: pack,
        };
        const types = row.publicTypes ? [{
          ...surfaces[0].types[0], id: assembly.id + ":Example.Widget",
          assembly: file, assemblyName: assembly.name, assemblyId: assembly.id, platformPack: pack,
        }] : [];
        return JSON.stringify({
          ...surfaces[0], package: "Microsoft.NETCore.App", version, frameworks: [tfm], activeFramework: tfm,
          defaultAssemblyId: assembly.id, assemblies: [assembly], types,
          totalMembers: row.publicTypes,
        });
      }
      export async function queryPackage(id, version, framework) {
        const surface = surfaceFor(id);
        if (packageLoading.deferInitial || (packageLoading.deferChanges
          && (id !== surfaces[0].package || version !== surface.version
            || framework !== surface.activeFramework))) {
          document.documentElement.dataset.packageQueryPending =
            JSON.stringify([id, version, framework]);
          await new Promise(resolve => document.addEventListener(
            "finish-package-query", resolve, { once: true }));
          document.documentElement.dataset.packageQuerySettled =
            JSON.stringify([id, version, framework]);
          if (!packageFrameworkFailed && framework === packageLoading.failFrameworkOnce) {
            packageFrameworkFailed = true;
            throw new Error("Framework inspection failed");
          }
          if (!packageVersionFailed && version === packageLoading.failVersionOnce) {
            packageVersionFailed = true;
            throw new Error("Version inspection failed");
          }
        }
        const selectedVersion = version === "latest" ? surface.version : version;
        return {
          versionSettlement: {
            content: {
              kind: "Settled",
              result: {
                request: {
                  packageId: id.toLowerCase(),
                  version: version === "latest" ? null : selectedVersion,
                },
                coordinate: {
                  packageId: id.toLowerCase(),
                  version: selectedVersion,
                },
                includePrerelease: false,
                freshness: version === "latest" ? "RefreshedForRequest" : null,
                listings: [],
                sourceListings: [],
              },
              failure: null,
            },
            share: {
              kind: "NonProjectable",
              fullUrl: null,
              packet: null,
              path: "package-version-settlement/share",
              reason: "No canonical Workspace share projection.",
            },
            diagnostics: [],
          },
          packageInfo: {
            content: {
              status: "Measured",
              packageId: id,
              packageVersion: selectedVersion,
              compressedPackageBytes: 4096,
              selectedTargetFramework: framework || surface.activeFramework,
              availableTargetFrameworks: surface.frameworks,
              selectedTargetFrameworkFolders: ["lib", "runtimes"],
              selectedLibraryPayloadBytes: 2048,
              selectedLibraryCount: surface.assemblies.length,
              detail: null,
              unavailableReason: null,
              hasSelectedSlice: true,
            },
            share: {
              kind: "NonProjectable",
              fullUrl: null,
              packet: null,
              path: "package-info-measurements/share",
              reason: "No canonical Workspace share projection.",
            },
            diagnostics: [],
          },
          surface: {
          ...surfaceFor(id, version, framework),
          package: id,
          version: selectedVersion,
          activeFramework: framework || surface.activeFramework,
          },
        };
      }
      export async function queryPackageVersions() {
        const versions = packageLoading.versions ?? ["1.0.0", "0.9.0"];
        return {
          versions,
          currentVersionInsertionIndex: 0,
          ...(versions[1] === undefined ? {} : { previousVersion: versions[1] }),
        };
      }
      export async function queryLibraries(id, version, framework, admittedAssetIdsJson, requiredReferencesJson) {
        const queryMode = packageLoading.libraryQuery ?? "ready";
        document.documentElement.dataset.libraryQueryRequest =
          JSON.stringify([id, version, framework, admittedAssetIdsJson, requiredReferencesJson]);
        if (queryMode === "deferred-error") {
          await new Promise(resolve => document.addEventListener(
            "finish-library-query", resolve, { once: true }));
          throw new Error("Library Query offline");
        }
        const surface = surfaceFor(id, version, framework);
        const admittedAssetIds = JSON.parse(admittedAssetIdsJson);
        const admitted = surface.assemblies.filter(assembly =>
          admittedAssetIds.includes(assembly.id));
        const selected = admitted[0];
        const failed = admitted[2];
        const results = queryMode === "empty" || !selected
          ? []
          : [{
              assetId: selected.id,
              library: selected.name,
              path: selected.asset,
              source: surface.package,
              version: selected.version,
              sourceKind: "Package",
              targetFramework: surface.activeFramework,
              matchedReferences: JSON.parse(requiredReferencesJson),
            }];
        const failures = queryMode === "partial" && failed
          ? [{
              assetId: failed.id,
              library: failed.name,
              path: failed.asset,
              source: surface.package,
              kind: "InvalidMetadata",
              message: "Invalid metadata image.",
            }]
          : [];
        return {
          content: {
            results,
            failures,
            summary: {
              populationCandidates: admittedAssetIds.length,
              candidateLimit: 256,
              candidates: admittedAssetIds.length,
              matches: results.length,
              failures: failures.length,
              incompleteReasons: failures.length ? "EvaluationFailures" : "None",
              isComplete: failures.length === 0,
            },
          },
          share: {
            kind: "Unavailable",
            fullUrl: null,
            packet: null,
            path: null,
            reason: "Library Query results are not shareable.",
          },
          diagnostics: [],
        };
      }
      export async function loadRuntimePack(framework, version) {
        document.documentElement.dataset.runtimePackRequest = JSON.stringify([framework, version]);
        const surface = surfaceFor("Microsoft.NETCore.App");
        return JSON.stringify({ ...surface, activeFramework: framework, version: version || surface.version });
      }
      export function searchTypes(query, candidates) {
        const normalized = query.toLowerCase();
        return candidates
          .filter(candidate => candidate.name.toLowerCase().includes(normalized)
            || candidate.full.toLowerCase().includes(normalized))
          .map(candidate => ({ key: candidate.key, kind: "substring" }));
      }
      export function clearWorkspacePackageOccurrences() {}
      export async function queryWorkspacePackageOccurrences(json) {
        return { superseded: false, occurrences: JSON.parse(json).map(coordinate => ({
          ...coordinate, action: JSON.stringify(coordinate),
        })) };
      }
      export async function activateWorkspacePackageOccurrence(action) {
        const coordinate = JSON.parse(action);
        return { activated: true, superseded: false,
          package: (await queryPackage(
            coordinate.package,
            coordinate.version,
            coordinate.framework)).surface };
      }
      export async function packageCacheStats() {
        if (diagnosticsOptions.cachePending) {
          document.documentElement.dataset.packageCacheStatsPending = "true";
          await new Promise(resolve => document.addEventListener(
            "finish-package-cache-stats", resolve, { once: true }));
        }
        if (diagnosticsOptions.cacheFailure) throw new Error("Cache storage offline");
        return {
          packages: 1,
          resident: 1,
          maxPackageEntries: 256,
          workspaces: 1,
          maxWorkspaces: 4,
          maxWorkspaceAssembliesPerRole: 256,
          residentBytes: 0,
          maxResidentBytes: 134217728,
          maxWorkspaceRetainedImageBytes: 67108864,
        };
      }
      export function listPackageActivityPackageSets() {
        if (packageLoading.activityCatalogFailure) {
          throw new Error("Package Activity catalog offline");
        }
        return {
          version: 1,
          packageSets: [{
            id: "package-set.fixture",
            title: "Fixture packages",
            summary: "Browser fixture package set.",
            order: 10,
          }],
        };
      }
      export function listPackageQueryCatalog() { return { presets: [], terms: [] }; }
      export async function queryMemberDocumentation() {
        return { summary: "Runs the widget.", returns: null, parameters: {}, exceptions: [] };
      }
      export async function queryLibraryApi(id, version, framework, asset) {
        document.documentElement.dataset.libraryApiAttempts =
          String(++libraryApiAttempts);
        if (diagnosticsOptions.libraryApiFailure) {
          throw new Error("Library API inspection failed.");
        }
        const surface = surfaceFor(id, version, framework);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        const types = surface.types.filter(type => type.assemblyId === asset);
        const typeKinds = [...new Set(types.map(type => type.kind))].map((kind, index) => ({
          id: kind.toLowerCase(), singularLabel: kind, pluralLabel: kind + "s",
          weight: index, count: types.filter(type => type.kind === kind).length,
          isDefault: true,
        }));
        const namespaces = [...new Set(types.map(type => type.namespace))].map(name => ({
          name, count: types.filter(type => type.namespace === name).length,
        }));
        return {
          content: {
            outcome: 0, packageId: id, packageVersion: version,
            requestedTargetFramework: framework, requestedLibrary: asset,
            source: { packageId: id, packageVersion: version, producer: "fixture", framework },
            asset: {
              id: selected.id, path: selected.asset, assemblyName: selected.name,
              targetFramework: framework, kind: 0,
            },
            assembly: {
              identity: {
                name: selected.name, version: selected.version,
                culture: selected.culture, publicKeyToken: selected.publicKeyToken,
              },
              moduleVersionId: "00000000-0000-0000-0000-000000000001",
            },
            inventory: {
              publicTypeCount: selected.publicTypes,
              publicMemberCount: selected.publicMembers,
              publicMethodCount: selected.publicMembers,
              publicPropertyCount: 0,
              typeKinds,
              namespaces,
            },
            truncation: null,
            failures: diagnosticsOptions.libraryApiIncomplete
              ? [{ kind: 6, detail: "TypeDefinitions: BadImageFormat: invalid row", subjectAssembly: null }]
              : [],
            isComplete: !diagnosticsOptions.libraryApiIncomplete,
            isAvailable: true,
          },
          share: {
            kind: "Available", fullUrl: "https://dotnet-inspect.net/",
            packet: "fixture", path: null, reason: null,
          },
          diagnostics: [],
        };
      }
      export async function queryPackageDependencies(id, version, framework, asset) {
        document.documentElement.dataset.referenceRequest = asset;
        document.documentElement.dataset.packageDependenciesRequest = JSON.stringify([id, version, framework]);
        const surface = surfaceFor(id, version, framework);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        const scenario = ${JSON.stringify(references)};
        if (scenario === "deferred") {
          await new Promise(resolve => document.addEventListener(
            "fixture-references-ready:" + asset, resolve, { once: true }));
        }
        if (scenario === "query-error") throw new Error("Reference query unavailable.");
        return {
          package: id, version, activeFramework: framework, assembly: selected.name,
          dependencyGroups: [], declarationFailures: [], dependencyGroupError: null,
          assemblyReferences: scenario === "inspection-error" ? "Cannot decode AssemblyRef."
            : { references: scenario === "empty" ? [] : scenario === "long"
            ? Array.from({ length: 80 }, (_, index) => ({
                name: selected.name + "." + "LongNamespace.".repeat(20) + "Reference" + index,
                version: "1.2.3.4", culture: "x-" + Array(20).fill("private").join("-"),
                publicKeyToken: "0123456789abcdef"
              }))
            : [{ name: selected.name + ".Dependency", version: "1.0.0.0", culture: null, publicKeyToken: null }] },
          compileLibrary: surface.compileLibrary
        };
      }`,
    library: `
      const uploadMode = ${JSON.stringify(libraryUpload)};
      const uploadInspection = ${JSON.stringify(uploadInspection)};
      export async function openUploadedLibrary(declaredName, content) {
        document.documentElement.dataset.libraryUploadRequest =
          JSON.stringify([declaredName, content.length]);
        if (uploadMode === "deferred") {
          await new Promise(resolve =>
            document.addEventListener("finish-library-upload", resolve));
        }
        return {
          ...uploadInspection,
          content: {
            ...uploadInspection.content,
            declaredName,
            byteLength: content.length,
          },
        };
      }`,
    metadata: `
      ${surfaceLookup}
      export async function queryPlatformMetadata(tfm, version, file, pack) {
        document.documentElement.dataset.platformMetadataRequest = JSON.stringify([tfm, version, file, pack]);
        return {
          assemblies: [{
            assembly: file,
            metadataRoots: [{
              requestedRoot: "Cli", canonicalRoot: "Cli",
              rootRelativeVirtualAddress: 256, rootSize: 512, aliasesCliMetadata: false,
              metadataVersion: "v4.0.30319", metadataVersionTruncated: false,
              kind: "Ecma335", isAssembly: true, metadataSize: 512,
              projectedTableTotal: 1, heaps: [],
              tables: [{ index: 0, name: "Module", rowCount: 1, isProjected: true }],
              headers: {}
            }],
            cliMetadataError: null, manifestMetadataError: null,
            readyToRun: null, readyToRunError: null
          }],
          inspectionError: null, compileLibrary: null,
        };
      }
      export async function queryPackageMetadata(id, version, framework, asset) {
        document.documentElement.dataset.metadataRequest = asset;
        document.documentElement.dataset.metadataCoordinate = JSON.stringify([id, version, framework, asset]);
        const surface = surfaceFor(id, version, framework);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        return {
          assemblies: [{
            assembly: selected.name + ".dll",
            metadataRoots: [{
              requestedRoot: "Cli", canonicalRoot: "Cli",
              rootRelativeVirtualAddress: 256, rootSize: 512, aliasesCliMetadata: false,
              metadataVersion: "v4.0.30319", metadataVersionTruncated: false,
              kind: "Ecma335", isAssembly: true, metadataSize: 512,
              projectedTableTotal: 1, heaps: [],
              tables: [{ index: 0, name: "Module", rowCount: 1, isProjected: true }],
              headers: {}
            }],
            cliMetadataError: null, manifestMetadataError: null,
            readyToRun: null, readyToRunError: null
          }],
          inspectionError: null, compileLibrary: surface.compileLibrary
        };
      }
      export async function queryPackageMetadataTable(
        id, version, framework, asset, metadataRoot, index, startRowId) {
        document.documentElement.dataset.tableRequest = asset;
        return { index, name: "Module", rowCount: 1, startRowId, columns: [], rows: [], error: null };
      }
      export async function queryTypeProjection(
        id, version, framework, assembly, typeQueryId, typeDefinitionId) {
        const surface = surfaceFor(id, version, framework);
        const selected = surface.types.find(item =>
          item.definitionId === typeDefinitionId || item.queryId === typeQueryId);
        if (!selected) throw new Error("Unknown type: " + typeDefinitionId);
        const related = selected.queryId === "Example.Widget"
          ? surface.types.find(item => item.queryId === "Example.Neighbor")
          : null;
        return {
          exactTypeInspection: {
            content: {
              outcome: 0,
              isAvailable: true,
              isComplete: true,
              type: {
                fullName: selected.queryId,
                namespace: selected.namespace,
                name: selected.name,
                definitionIdentity: {
                  assembly: selected.assemblyName,
                  metadataId: selected.metadataId
                },
                introducedTypeParameterCounts: [],
                kind: selected.kind,
                accessibility: selected.accessibility,
                attributes: [],
                isSealed: false,
                isAbstract: false,
                isStatic: false,
                isByRefLike: false,
                isReadOnly: false,
                baseType: null,
                interfaces: related ? [related.queryId] : [],
                derivedTypes: [],
                typeParameters: [],
                members: [],
                enumUnderlyingType: null,
                isForwarded: false
              },
              supplierAssembly: {
                identity: {
                  name: selected.assemblyName,
                  version: "1.0.0.0",
                  culture: null,
                  publicKeyToken: null
                }
              },
              failures: []
            },
            share: {
              kind: "NonProjectable", fullUrl: null, packet: null,
              path: "type-metadata/share", reason: "Fixture projection."
            },
            diagnostics: []
          },
          derivedTypes: [],
          graphNodes: [],
          graphEdges: [],
          inspectionFailures: []
        };
      }`,
    analysis: `
      ${surfaceLookup}
      export async function queryPackageIntegrations(id, version, framework, asset) {
        document.documentElement.dataset.integrationRequest = asset;
        const surface = surfaceFor(id);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        return integrationsFor(surface, selected, version, framework, asset);
      }
      async function integrationsFor(surface, selected, version, framework, asset) {
        const scenario = ${JSON.stringify(integrations)};
        if (scenario === "deferred") {
          await new Promise(resolve => document.addEventListener(
            "fixture-integrations-ready:" + asset, resolve, { once: true }));
        }
        if (scenario === "query-error") throw new Error("Integration query unavailable.");
        const categories = scenario === "empty" || scenario === "partial-empty" ? [] : [
          { integration: "Dependency Injection", signals: [
            { name: selected.name + ".ServiceExtensions.AddWidgets(IServiceCollection services)", shape: "Method", kind: "Extension method" },
            { name: selected.name + ".WidgetService", shape: "Type", kind: "Implementation" }
          ] },
          { integration: "Logging", signals: [
            { name: selected.name + ".WidgetLogger.Write(ILogger logger)", shape: "Method", kind: "Parameter" }
          ] }
        ];
        if (scenario === "long") {
          categories[0].integration += "." + "LongCategory".repeat(30);
          categories[0].signals = Array.from({ length: 80 }, (_, index) => ({
            name: selected.name + "." + "LongNamespace.".repeat(20)
              + "Add" + "LongSignalName".repeat(15) + index + "(IServiceCollection services)",
            shape: "Method", kind: "LongKind".repeat(30)
          }));
        }
        const partial = scenario.startsWith("partial");
        return {
          package: surface.package, version, framework, categories,
          totalSignals: categories.reduce((total, category) => total + category.signals.length, 0),
          isComplete: !partial, inspectionError: partial ? "A library participant could not be inspected." : null,
          compileLibrary: surface.compileLibrary
        };
      }
      export async function queryPlatformIntegrations(framework, version, file, pack) {
        document.documentElement.dataset.platformIntegrationRequest = file + ":" + pack;
        const row = ${JSON.stringify(catalogTarget.rows)}.find(item => item.assembly + ".dll" === file && item.pack === pack);
        if (!row) throw new Error("Unknown platform library: " + file);
        const surface = {
          ...surfaces[0],
          package: "Microsoft.NETCore.App",
          compileLibrary: row.file
        };
        const selected = { id: "platform:" + pack + ":" + file, name: row.assembly, asset: row.file };
        return integrationsFor(surface, selected, version, framework, selected.id);
      }
      async function opportunitiesFor(id, surface, selected, version, framework, asset) {
        const scenario = ${JSON.stringify(opportunities)};
        if (scenario === "deferred") {
          await new Promise(resolve => document.addEventListener(
            "fixture-opportunities-ready:" + asset, resolve, { once: true }));
        }
        if (scenario === "query-error") throw new Error("Opportunity query unavailable.");
        const item = (api, integrationType, lookFor) => ({
          api, integrationType, lookFor,
          sourceDefinitionId: api,
          sourceAssembly: selected.name,
          sourceAssemblyVersion: selected.version,
          sourceAssemblyCulture: selected.culture,
          sourceAssemblyPublicKeyToken: selected.publicKeyToken
        });
        const categories = scenario === "empty" || scenario === "partial-empty" ? [] : [
          { integration: "Cloud clients", items: [
            item("Example.Widget", "Microsoft.Extensions.Http IHttpClientBuilder registration", "AddHttpClient, AddStandardResilienceHandler"),
            item(selected.name + ".LegacyCloudClient", "IServiceCollection registration", "AddCloudClient")
          ] },
          { integration: "Configuration", items: [
            item(selected.name + ".LegacyOptions", "IConfiguration binding", "AddOptions, Configure")
          ] }
        ];
        if (scenario === "long") {
          categories[0].integration += "." + "LongOpportunityArea".repeat(25);
          categories[0].items = Array.from({ length: 80 }, (_, index) => item(
            selected.name + "." + "LongNamespace.".repeat(18)
              + "LongOpportunityType".repeat(12) + index,
            "Microsoft.Extensions." + "LongSuggestedPackage".repeat(15)
              + " " + "Long integration kind ".repeat(15),
            Array.from({ length: 8 }, (_, token) => "Add" + "LongApiName".repeat(8) + token).join(", ")
          ));
        }
        const partial = scenario.startsWith("partial");
        return {
          package: id, version, activeFramework: framework, categories,
          totalOpportunities: categories.reduce((total, category) => total + category.items.length, 0),
          isComplete: !partial,
          inspectionError: partial ? "A library participant could not be inspected." : null,
          compileLibrary: surface.compileLibrary
        };
      }
      export async function queryPackageOpportunities(id, version, framework, asset) {
        document.documentElement.dataset.opportunityRequest = asset;
        const surface = surfaceFor(id);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        return opportunitiesFor(id, surface, selected, version, framework, asset);
      }
      export async function queryPlatformOpportunities(framework, version, file, pack) {
        document.documentElement.dataset.platformOpportunityRequest = file + ":" + pack;
        const row = ${JSON.stringify(catalogTarget.rows)}.find(item => item.assembly + ".dll" === file && item.pack === pack);
        if (!row) throw new Error("Unknown platform library: " + file);
        const surface = {
          ...surfaces[0],
          package: "Microsoft.NETCore.App",
          compileLibrary: row.file
        };
        const selected = {
          id: "platform:" + pack + ":" + file,
          name: row.assembly,
          asset: row.file,
          version: row.version,
          culture: null,
          publicKeyToken: null
        };
        return opportunitiesFor(
          surface.package, surface, selected, version, framework, selected.id);
      }
      async function performanceFor(surface, selected, version, framework, requestKey) {
        const selectedType = surface.types.find(item => item.assemblyId === selected.id);
        if (!selectedType) throw new Error("Library has no projected type: " + selected.asset);
        const scenario = ${JSON.stringify(analysis)};
        if (scenario === "deferred") {
          await new Promise(resolve => document.addEventListener(
            "fixture-analysis-ready:" + requestKey, resolve, { once: true }));
        }
        if (scenario === "query-error") throw new Error("Analysis query unavailable.");
        const member = (memberName, opportunityCount, inLoopCount, shapes, confidence) => ({
          assembly: selected.name + ".dll",
          typeId: selectedType.definitionId,
          memberName,
          stableSelector: "Run",
          bodyTokens: [100663297],
          opportunityCount,
          inLoopCount,
          shapes,
          confidence
        });
        const members = scenario === "empty" || scenario === "partial-empty" ? [] : [
          member("Run", 3, 1, ["box-value-type", "string-concat"], "high"),
          member("Write", 1, 0, ["array-allocation"], "medium")
        ];
        if (scenario === "long") {
          members.splice(0, members.length, ...Array.from({ length: 80 }, (_, index) =>
            member(
              "LongPerformanceMember".repeat(12) + index,
              index + 1,
              index % 3,
              ["LongAllocationShape".repeat(12), "loop-carried-allocation"],
              index % 2 ? "medium" : "high")));
        }
        const partial = scenario.startsWith("partial");
        return {
          members,
          inspectionError: partial ? "A method body could not be analyzed." : null,
          nonPublicOpportunities: 2,
          totalOpportunities: members.reduce((total, item) => total + item.opportunityCount, 0) + 2,
          compileLibrary: surface.compileLibrary
        };
      }
      export async function queryPackagePerformance(id, version, framework, asset) {
        document.documentElement.dataset.analysisRequest = asset;
        const surface = surfaceFor(id);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        return performanceFor(surface, selected, version, framework, asset);
      }
      export async function queryPlatformPerformance(framework, version, file, pack) {
        document.documentElement.dataset.platformAnalysisRequest = file + ":" + pack;
        const row = ${JSON.stringify(catalogTarget.rows)}.find(item => item.assembly + ".dll" === file && item.pack === pack);
        if (!row) throw new Error("Unknown platform library: " + file);
        const selected = {
          ...surfaces[0].assemblies[0],
          id: "platform:" + pack + ":" + file,
          name: row.assembly,
          asset: row.file,
          platformPack: pack
        };
        const selectedType = {
          ...surfaces[0].types[0],
          id: selected.id + ":Example.Widget",
          assembly: file,
          assemblyName: row.assembly,
          assemblyId: selected.id,
          platformPack: pack
        };
        const surface = {
          ...surfaces[0],
          package: "Microsoft.NETCore.App",
          compileLibrary: row.file,
          assemblies: [selected],
          types: [selectedType]
        };
        return JSON.stringify(await performanceFor(
          surface, selected, version, framework, selected.id));
      }`,
    source: "",
    "call-graph": `
      const callGraph = ${JSON.stringify(fixtureCallGraph)};
      export async function queryMemberCallGraph() {
        return callGraph;
      }
      export async function expandPlatformCallGraph() {
        return callGraph;
      }`,
    catalog: `
      const homeDemos = ${JSON.stringify(homeDemos?.catalog ?? [])};
      const homeDemoResults = ${JSON.stringify(homeDemos?.results ?? {})};
      const homeDemoCatalogPending = ${Boolean(homeDemos?.catalogPending)};
      export function listVocabulary() { return { schema_version: 1, sections: [] }; }
      export async function listHomeDemos() {
        if (homeDemoCatalogPending) {
          document.documentElement.dataset.homeDemoCatalogPending = "true";
          await new Promise(resolve => document.addEventListener(
            "finish-home-demo-catalog", resolve, { once: true }));
        }
        return { demos: homeDemos };
      }
      export async function runHomeDemo(id) {
        document.documentElement.dataset.homeDemoRun = id;
        return homeDemoResults[id] ?? {
          found: false,
          packages: [],
          activation: null,
          callGraph: null,
        };
      }
      let holdNextWorkspaceEncode = false;
      document.addEventListener("hold-workspace-encode", () => {
        holdNextWorkspaceEncode = true;
      });
      export async function encodeWorkspaceShareState(state) {
        if (holdNextWorkspaceEncode) {
          holdNextWorkspaceEncode = false;
          document.documentElement.dataset.workspaceEncodePending = "true";
          await new Promise(resolve => document.addEventListener(
            "finish-workspace-encode", resolve, { once: true }));
        }
        return {
          succeeded: true,
          packet: btoa(JSON.stringify(state)),
          failure: null,
        };
      }
      export function decodeWorkspaceShareState(packet) {
        return { succeeded: true, state: JSON.parse(atob(packet)), failure: null };
      }`,
  };
  const assetDirectory = new URL("../dist/assets/", import.meta.url);
  const workerEntryAssets = (await readdir(assetDirectory))
    .filter(name => name.startsWith("engine-worker-entry-") && name.endsWith(".js"));
  if (workerEntryAssets.length !== 1) {
    throw new Error(
      `Expected one built Worker entry asset, found ${workerEntryAssets.length}.`);
  }
  const workerEntryAsset = workerEntryAssets[0]!;
  let workerEntryBody = await readFile(
    new URL(workerEntryAsset, assetDirectory),
    "utf8",
  );
  const fixtureModuleUrls: Record<string, string> = {};
  for (const [name, moduleBody] of Object.entries(modules)) {
    const source = `${common}\n${moduleBody}`;
    fixtureModuleUrls[name] =
      `data:text/javascript;base64,${Buffer.from(source).toString("base64")}`;
    workerEntryBody = workerEntryBody.replaceAll(
      `import(\`/inspect-web-${name}.js\`)`,
      `__inspectWebFixtureImport(${JSON.stringify(name)})`,
    );
    if (workerEntryBody.includes(`/inspect-web-${name}.js`)) {
      throw new Error(`Worker facade import was not replaced: ${name}`);
    }
  }
  workerEntryBody = `
    const __inspectWebFixtureModuleUrls = ${JSON.stringify(fixtureModuleUrls)};
    const __inspectWebFixtureImport =
      name => import(__inspectWebFixtureModuleUrls[name]);
    ${workerEntryBody}`;
  await page.route("https://cdn.jsdelivr.net/**", route => route.abort());
  await page.route(/\/assets\/[^/]+\.(?:js|css|woff2?|ttf)$/, async route => {
    const assetName = basename(new URL(route.request().url()).pathname);
    const path = fileURLToPath(new URL(`../dist/assets/${assetName}`, import.meta.url));
    if (assetName !== workerEntryAsset) {
      await route.fulfill({ path });
      return;
    }
    await route.fulfill({
      contentType: "text/javascript",
      body: workerEntryBody,
    });
  });
  await page.route("**/assets/platform-index.json", route =>
    route.fulfill(platform ? {
      contentType: "application/json",
      body: JSON.stringify({ schemaVersion: 2, defaultFramework: "net11.0", targets: [catalogTarget, historicalPlatformTarget] }),
    } : { status: 404, body: "Platform catalog is not part of this fixture." }));
  await page.route("**/*", route =>
    route.request().resourceType() === "document"
      ? route.fulfill({
          path: fileURLToPath(new URL("../dist/index.html", import.meta.url)),
          contentType: "text/html",
        })
      : route.fallback());
}

async function releaseFacade(page: Page, name: string): Promise<void> {
  await page.evaluate(releaseName => {
    const channelName = document.documentElement.dataset.fixtureChannelName;
    if (channelName === undefined) {
      throw new Error("Worker fixture channel is unavailable.");
    }
    const channel = new BroadcastChannel(channelName);
    const send = channel.postMessage.bind(channel);
    send({
      kind: "release",
      name: releaseName,
    });
    channel.close();
  }, name);
}

const root = "/?package=Example.Package&version=1.0.0&framework=net10.0#pkg";

const frameworkSurface: BrowserPackageSurface = {
  ...surface,
  package: "System.Text.Json",
  version: "10.0.0",
  frameworks: ["net10.0", "net9.0"],
};
const frameworkRoot = "/?package=System.Text.Json&version=10.0.0&framework=net10.0#pkg";

async function currentWorkspaceHistoryState(page: Page): Promise<{
  id: string | null;
  session: string | null;
}> {
  return page.evaluate(() => {
    const isRecord = (
      candidate: unknown,
    ): candidate is Record<string, unknown> =>
      typeof candidate === "object" && candidate !== null;
    const value: unknown = history.state;
    if (!isRecord(value)) {
      return { id: null, session: null };
    }
    return {
      id: typeof value.inspectWorkspaceId === "string"
        ? value.inspectWorkspaceId
        : null,
      session: typeof value.inspectWorkspaceSession === "string"
        ? value.inspectWorkspaceSession
        : null,
    };
  });
}

function platformWorkspaceUrl(includePackage = false) {
  const platformTabId = includePackage ? "t1" : "t0";
  const platformContextId = includePackage ? "g1" : "g0";
  const platformTab = {
    id: platformTabId,
    kind: "group",
    source: ":Platform",
    version: platformVersion,
    framework: "net11.0",
    runtimeIdentifier: null,
  };
  const state = {
    tabs: includePackage
      ? [
        {
          id: "t0",
          kind: "package",
          source: surface.package,
          version: surface.version,
          framework: surface.activeFramework,
          runtimeIdentifier: null,
        },
        platformTab,
      ]
      : [platformTab],
    contexts: includePackage
      ? [{ id: "g0", tabIds: ["t0"] }, { id: "g1", tabIds: ["t1"] }]
      : [{ id: platformContextId, tabIds: [platformTabId] }],
    activeTabId: platformTabId,
    selectedContextId: platformContextId,
    view: {
      lens: null,
      type: null,
      memberAnchor: null,
      memberSignature: null,
      section: null,
      libraries: [],
    },
  };
  const packet = Buffer.from(JSON.stringify(state)).toString("base64");
  return `/?w=${encodeURIComponent(packet)}`;
}

async function openInstalledPlatform(page: Page, includePackage = false) {
  await page.goto(platformWorkspaceUrl(includePackage));
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  await expect(page).toHaveURL(/\/\?package=&w=/);
}

async function openPlatform(page: Page, options: PlatformFixture = {}) {
  await installFacades(page, surface, [], "ready", "ready", options);
  await openInstalledPlatform(page);
}

async function installLibraryQueryFacades(
  page: Page,
  libraryQuery: NonNullable<PackageLoadingFixture["libraryQuery"]>,
) {
  await installFacades(
    page,
    surface,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    undefined,
    {},
    { libraryQuery },
  );
}

async function installLibraryUploadFacades(
  page: Page,
  libraryUpload: LibraryUploadFixture,
) {
  await installFacades(
    page,
    surface,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    undefined,
    {},
    {},
    libraryUpload,
  );
}

export {
  subjectTab,
  inspectorTab,
  chooseInspector,
  chooseSubject,
  selectLibrary,
  expectCurrentSubjectVisible,
  library,
  run,
  type as createType,
  core,
  other,
  empty,
  surface,
  platformVersion,
  alternatePlatformVersion,
  platformRow,
  platformTarget,
  historicalPlatformTarget,
  installFacades,
  installLibraryQueryFacades,
  installLibraryUploadFacades,
  releaseFacade,
  root,
  frameworkSurface,
  frameworkRoot,
  currentWorkspaceHistoryState,
  platformWorkspaceUrl,
  openInstalledPlatform,
  openPlatform,
};

export type {
  PlatformFixture,
  HomeDemoFixture,
  DiagnosticsFixture,
  PackageLoadingFixture,
  LibraryUploadFixture,
  BrowserAssemblySurface,
  BrowserMemberSurface,
  BrowserPackageSurface,
  BrowserTypeSurface,
  BrowserCallGraph,
  BrowserHomeDemoCatalogEntry,
  BrowserHomeDemoRunResult,
  PlatformAssemblyRow,
  PlatformCatalogTarget,
};
