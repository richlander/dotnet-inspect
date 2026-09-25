import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { Buffer } from "node:buffer";
import {
  expect,
  test,
  type BrowserContext,
  type Page,
  type Route,
  type Worker,
} from "@playwright/test";
import type {
  BrowserAssemblyReferenceList as AssemblyReferenceList,
  BrowserAssemblyReferenceResult as AssemblyReferenceResult,
  BrowserPackageCacheStats as CacheStats,
  BrowserPackageDependencies as PackageDependencies,
  BrowserPackageLoadResult as PackageLoadResult,
  BrowserPackageSurface as PackageSurface,
  BrowserPackageVersions as PackageVersions,
  BrowserWorkspacePackageOccurrence as OccurrenceRow,
  BrowserWorkspacePackageOccurrenceActivation as OccurrenceActivation,
  BrowserWorkspacePackageOccurrenceView as OccurrenceView,
  CompiledDocumentationOutcome,
} from "../src/facades/inspect-web-package.js";
import type {
  BrowserPackageIntegrations as PackageIntegrations,
} from "../src/facades/inspect-web-analysis.js";
import type {
  BrowserLibraryApiDiffResult,
  InertString,
  InspectionShare,
} from "../src/facades/inspect-web-metadata.js";
import {
  renderLibraryApiDiff,
  type LibraryApiDiffState,
} from "../src/library-api-diff.ts";
import {
  fixtureFramework,
  galleryDownloadPath,
  healthyNupkg,
  malformedAlongsideHealthyNupkg,
  malformedAssemblyBytes,
  manifestBackedNupkg,
  manifestOnlyNupkg,
  platformReferenceNupkg,
  platformRuntimeNupkg,
  storedZip,
  type ManifestDependency,
} from "./package-adoption-nupkg.ts";
import { selectFirstExactLibrary } from "./library-subject-actions.ts";

type WorkerClientModule = typeof import("../src/engine-worker-client.ts");

function isMetadataInertString(value: unknown): value is InertString {
  return typeof value === "string";
}

function metadataInertString(value: string): InertString {
  const wireValue: unknown = value;
  if (!isMetadataInertString(wireValue)) {
    throw new TypeError("The inert string wire value must be a string.");
  }
  return wireValue;
}

const site = resolve(
  process.env.INSPECT_WEB_PACKAGE_ADOPTION_SITE
    ?? "../artifacts/inspect-web-publish/wwwroot",
);
const manifest: unknown = JSON.parse(
  readFileSync(resolve(site, "manifest.json"), "utf8"),
);
if (typeof manifest !== "object" || manifest === null
    || !("src/engine-worker-client.ts" in manifest)) {
  throw new Error("Published site is missing the production Worker client entry.");
}
const workerClientEntry = manifest["src/engine-worker-client.ts"];
if (typeof workerClientEntry !== "object" || workerClientEntry === null
    || !("file" in workerClientEntry)
    || typeof workerClientEntry.file !== "string") {
  throw new Error("Published production Worker client entry has no asset.");
}
const workerClientUrl = `/${workerClientEntry.file}`;

async function chooseInspector(
  page: Page,
  attribute: string,
  inspector: string,
) {
  const tab = page.locator(
    `[data-inspector-tab][${attribute}="${inspector}"]`,
  );
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
}

// This gate drives the actually published production DotnetInspect.Web Wasm
// artifact through the production single-runtime Worker client in Firefox. It proves
// the artifact-backed package scope adoption contract (issue #5576): ordinary
// singleton opening via queryPackage, repeated/joined requests, the four-scope
// bound with successful eviction, awaitable Workspace occurrence activation, a
// stale occurrence action after clear and after replacement, and a
// valid-reference / malformed-implementation package producing a visible
// selected rejection beside healthy evidence. It also proves exact optional
// predecessor inventory facts and the package facade's assembly-reference
// result union (issue #6191): an available list of real AssemblyRef rows, a
// manifest-only package's compile-library failure message beside healthy
// manifest dependency groups, and the production page rendering the available
// case. Package acquisition leaves the browser as ordinary NuGet Gallery CDN
// fetches, which this spec intercepts to serve deterministic local fixtures; a
// separate test exercises the immutable real
// Microsoft.Extensions.Http@10.0.0/net10.0 and the formerly oversized
// System.Text.Json@10.0.0/net10.0 coordinates over the network.

function locateFixtureAssembly(variable: string): Buffer {
  const configured = process.env[variable];
  if (!configured) {
    throw new Error(
      `${variable} must point at a built, cataloged fixture assembly resolved `
        + "through eng/test-inspect-web-package-adoption-gate.sh "
        + "(FixtureCatalog.AssemblyPath via tools/InspectWebFixtureResolver).",
    );
  }
  return readFileSync(resolve(configured));
}

// Two genuinely valid, distinct-identity cataloged fixtures (diff-asm.lib-a and
// diff-asm.lib-b). The healthy carrier supplies valid reference and
// implementation assets; the broken carrier supplies a valid reference beside a
// malformed implementation, so queryPackage's reference surface stays healthy
// for both names while the analysis facade surfaces the broken implementation's
// selected rejection.
const healthyAssembly = locateFixtureAssembly(
  "INSPECT_WEB_PACKAGE_ADOPTION_LIBA_DLL",
);
const brokenReferenceAssembly = locateFixtureAssembly(
  "INSPECT_WEB_PACKAGE_ADOPTION_LIBB_DLL",
);
const literalAssembly = locateFixtureAssembly(
  "INSPECT_WEB_PACKAGE_ADOPTION_LITERALS_DLL",
);
const libraryDiffV1Assembly = locateFixtureAssembly(
  "INSPECT_WEB_PACKAGE_ADOPTION_LIBRARY_DIFF_V1_DLL",
);
const libraryDiffV2Assembly = locateFixtureAssembly(
  "INSPECT_WEB_PACKAGE_ADOPTION_LIBRARY_DIFF_V2_DLL",
);
const documentationAssembly = locateFixtureAssembly(
  "INSPECT_WEB_PACKAGE_ADOPTION_DOCUMENTATION_DLL",
);
const documentationXml = locateFixtureAssembly(
  "INSPECT_WEB_PACKAGE_ADOPTION_DOCUMENTATION_XML",
);
const healthyAssemblyFileName = "DiffAsmLibA.dll";
const brokenAssemblyFileName = "DiffAsmLibB.dll";
const healthyTypeName = "Token";

const healthyArchive = healthyNupkg(healthyAssembly, healthyAssemblyFileName);
const malformedArchive = malformedAlongsideHealthyNupkg(
  healthyAssembly,
  healthyAssemblyFileName,
  brokenReferenceAssembly,
  brokenAssemblyFileName,
  malformedAssemblyBytes(),
);

interface FixtureCoordinate {
  readonly packageId: string;
  readonly version: string;
  readonly archive: Buffer;
  readonly manifest?: Buffer;
}

const version = "1.0.0";

function packageQueryManifest(
  packageId: string,
  packageVersion: string,
  isTool: boolean,
  dependencies: readonly string[] = [],
): Buffer {
  const dependencyMarkup = dependencies.length === 0
    ? ""
    : `<dependencies>
      <group targetFramework="net10.0">
        ${dependencies.map(
          dependency => `<dependency id="${dependency}" version="[1.0.0, )" />`,
        ).join("\n        ")}
      </group>
    </dependencies>`;
  return Buffer.from(
    `<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>${packageId}</id>
    <version>${packageVersion}</version>
    <authors>Fixture</authors>
    <description>Package Query fixture.</description>
    ${isTool
      ? `<packageTypes>
      <packageType name="DotnetTool" />
    </packageTypes>`
      : ""}
    ${dependencyMarkup}
  </metadata>
</package>
`,
    "utf8",
  );
}

const healthy: FixtureCoordinate = {
  packageId: "InspectWeb.Adoption.Healthy",
  version,
  archive: healthyArchive,
};
const malformed: FixtureCoordinate = {
  packageId: "InspectWeb.Adoption.Malformed",
  version,
  archive: malformedArchive,
};
const occurrenceOne: FixtureCoordinate = {
  packageId: "InspectWeb.Adoption.OccurrenceOne",
  version,
  archive: healthyArchive,
};
const occurrenceTwo: FixtureCoordinate = {
  packageId: "InspectWeb.Adoption.OccurrenceTwo",
  version,
  archive: healthyArchive,
};
const joinCoordinate: FixtureCoordinate = {
  packageId: "InspectWeb.Adoption.Join",
  version,
  archive: healthyArchive,
};
const literalCoordinate: FixtureCoordinate = {
  packageId: "InspectWeb.Adoption.Literals",
  version,
  archive: healthyNupkg(
    literalAssembly,
    "ILInspector.Analysis.Fixtures.dll",
  ),
};
const scopeCoordinates: readonly FixtureCoordinate[] = Array.from(
  { length: 5 },
  (_unused, index) => ({
    packageId: `InspectWeb.Adoption.Scope${index + 1}`,
    version,
    archive: healthyArchive,
  }),
);

// The assembly-reference result cases (issue #6191). Both packages declare the
// same ordinary manifest dependency group, so the manifest evidence is a
// constant across the available and unavailable reference outcomes.
const declaredDependency: ManifestDependency = {
  id: "InspectWeb.Adoption.Declared",
  versionRange: "[2.0.0]",
};
const referencesPackageId = "InspectWeb.Adoption.References";
const manifestOnlyPackageId = "InspectWeb.Adoption.ManifestOnly";
const references: FixtureCoordinate = {
  packageId: referencesPackageId,
  version,
  archive: manifestBackedNupkg(
    healthyAssembly,
    healthyAssemblyFileName,
    referencesPackageId,
    version,
    declaredDependency,
  ),
};
const manifestOnly: FixtureCoordinate = {
  packageId: manifestOnlyPackageId,
  version,
  archive: manifestOnlyNupkg(
    manifestOnlyPackageId,
    version,
    declaredDependency,
  ),
};

// DiffAsmLibA is an ordinary managed library with exactly one AssemblyRef row,
// so the available case has an exact expected list rather than a shape probe.
const healthyAssemblyName = "DiffAsmLibA";
const expectedReferenceName = "System.Runtime";
const libraryDiffPackageId = "InspectWeb.LibraryApiDiff";
const libraryDiffAssemblyFileName = "LibraryApiDiffFixture.dll";
const libraryDiffV1: FixtureCoordinate = {
  packageId: libraryDiffPackageId,
  version: "1.0.0",
  archive: healthyNupkg(
    libraryDiffV1Assembly,
    libraryDiffAssemblyFileName,
  ),
};
const libraryDiffV2: FixtureCoordinate = {
  packageId: libraryDiffPackageId,
  version: "2.0.0",
  archive: healthyNupkg(
    libraryDiffV2Assembly,
    libraryDiffAssemblyFileName,
  ),
};

const platformDocumentationVersion = "11.0.7146";
const platformDocumentationAssemblyFileName =
  "InspectWeb.DocumentationFixtures.dll";
const platformRuntimeDocumentation: FixtureCoordinate = {
  packageId: "microsoft.netcore.app.runtime.linux-x64",
  version: platformDocumentationVersion,
  archive: platformRuntimeNupkg(
    documentationAssembly,
    platformDocumentationAssemblyFileName,
  ),
};
const platformReferenceDocumentation: FixtureCoordinate = {
  packageId: "microsoft.netcore.app.ref",
  version: platformDocumentationVersion,
  archive: platformReferenceNupkg(
    documentationAssembly,
    documentationXml,
    platformDocumentationAssemblyFileName,
  ),
};

const allFixtures: readonly FixtureCoordinate[] = [
  healthy,
  malformed,
  occurrenceOne,
  occurrenceTwo,
  joinCoordinate,
  literalCoordinate,
  references,
  manifestOnly,
  libraryDiffV1,
  libraryDiffV2,
  ...scopeCoordinates,
];

class GalleryFixtureRegistry {
  readonly downloads = new Map<string, number>();
  private readonly archives = new Map<string, Buffer>();
  private readonly manifests = new Map<string, Buffer>();
  private readonly versions = new Map<string, string[]>();
  private readonly registrations = new Map<string, {
    readonly packageId: string;
    readonly versions: string[];
  }>();
  private readonly downloadKeys = new Map<string, string>();

  constructor(fixtures: readonly FixtureCoordinate[]) {
    for (const fixture of fixtures) {
      this.archives.set(
        galleryDownloadPath(fixture.packageId, fixture.version),
        fixture.archive,
      );
      const flatPath = `/v3-flatcontainer/${fixture.packageId.toLowerCase()}/${fixture.version}`
        + `/${fixture.packageId.toLowerCase()}.${fixture.version}.nupkg`;
      this.archives.set(flatPath, fixture.archive);
      this.downloadKeys.set(flatPath, galleryDownloadPath(fixture.packageId, fixture.version));
      const indexPath =
        `/v3-flatcontainer/${fixture.packageId.toLowerCase()}/index.json`;
      const versions = this.versions.get(indexPath) ?? [];
      if (!versions.includes(fixture.version)) versions.push(fixture.version);
      this.versions.set(indexPath, versions);
      const registrationPath =
        `/v3/registration5-gz-semver2/${fixture.packageId.toLowerCase()}/index.json`;
      this.registrations.set(registrationPath, {
        packageId: fixture.packageId,
        versions,
      });
      if (fixture.manifest) {
        this.manifests.set(
          `/v3-flatcontainer/${fixture.packageId.toLowerCase()}/${fixture.version}`
            + `/${fixture.packageId.toLowerCase()}.nuspec`,
          fixture.manifest,
        );
      }
    }
  }

  archiveFor(pathname: string): Buffer | undefined {
    return this.archives.get(pathname);
  }

  versionIndexFor(pathname: string): readonly string[] | undefined {
    return this.versions.get(pathname);
  }

  registrationFor(pathname: string): unknown {
    const registration = this.registrations.get(pathname);
    if (!registration) return undefined;
    return {
      items: [{
        items: registration.versions.map(packageVersion => ({
          catalogEntry: {
            id: registration.packageId,
            version: packageVersion,
            listed: true,
          },
        })),
      }],
    };
  }

  manifestFor(pathname: string): Buffer | undefined {
    return this.manifests.get(pathname);
  }

  recordDownload(pathname: string): void {
    const key = this.downloadKeys.get(pathname) ?? pathname;
    this.downloads.set(key, (this.downloads.get(key) ?? 0) + 1);
  }

  downloadCount(fixture: FixtureCoordinate): number {
    return this.downloads.get(
      galleryDownloadPath(fixture.packageId, fixture.version),
    ) ?? 0;
  }
}

const corsHeaders: Readonly<Record<string, string>> = {
  "access-control-allow-origin": "*",
  "access-control-allow-methods": "GET, HEAD, OPTIONS",
  "access-control-allow-headers": "*",
};

async function installGalleryRoutes(
  context: BrowserContext,
  registry: GalleryFixtureRegistry,
  beforeArchive: (pathname: string) => Promise<void> = () => Promise.resolve(),
): Promise<void> {
  const serveFixture = async (route: Route): Promise<void> => {
    const request = route.request();
    if (request.method() === "OPTIONS") {
      await route.fulfill({ status: 204, headers: corsHeaders });
      return;
    }
    const pathname = new URL(request.url()).pathname;
    const archive = registry.archiveFor(pathname);
    if (archive) {
      await beforeArchive(pathname);
      registry.recordDownload(pathname);
      await route.fulfill({
        status: 200,
        headers: { ...corsHeaders, "content-type": "application/octet-stream" },
        body: archive,
      });
      return;
    }
    const manifestBytes = registry.manifestFor(pathname);
    if (manifestBytes) {
      await route.fulfill({
        status: 200,
        headers: { ...corsHeaders, "content-type": "application/xml" },
        body: manifestBytes,
      });
      return;
    }
    const indexVersions = registry.versionIndexFor(pathname);
    if (indexVersions) {
      await route.fulfill({
        status: 200,
        headers: { ...corsHeaders, "content-type": "application/json" },
        body: JSON.stringify({ versions: indexVersions }),
      });
      return;
    }
    const registration = registry.registrationFor(pathname);
    if (registration) {
      await route.fulfill({
        status: 200,
        headers: { ...corsHeaders, "content-type": "application/json" },
        body: JSON.stringify(registration),
      });
      return;
    }
    await route.fulfill({ status: 404, headers: corsHeaders });
  };
  await context.route("https://globalcdn.nuget.org/**", serveFixture);
  await context.route("https://api.nuget.org/v3-flatcontainer/**", serveFixture);
  await context.route("https://api.nuget.org/v3/index.json", async route => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      headers: corsHeaders,
      body: JSON.stringify({
        version: "3.0.0",
        resources: [{
          "@id": "https://api.nuget.org/v3-flatcontainer/",
          "@type": "PackageBaseAddress/3.0.0",
        }],
      }),
    });
  });
}

declare global {
  interface Window {
    __adoption?: {
      queryPackage(
        packageId: string,
        version: string,
        framework: string,
      ): Promise<PackageLoadResult>;
      queryVersions(
        packageId: string,
        currentVersion: string,
      ): Promise<PackageVersions>;
      cacheStats(): Promise<CacheStats>;
      queryOccurrences(
        workspace: readonly {
          package: string;
          version: string;
          framework: string;
        }[],
      ): Promise<OccurrenceView>;
      activate(action: string): Promise<OccurrenceActivation>;
      clearOccurrences(): Promise<void>;
      queryDependencies(
        packageId: string,
        version: string,
        framework: string,
        assemblyId: string,
      ): Promise<PackageDependencies>;
      queryIntegrations(
        packageId: string,
        version: string,
        framework: string,
        libraryId: string,
      ): Promise<PackageIntegrations>;
      queryPlatformDocumentation(
        framework: string,
        platformVersion: string,
        assemblyName: string,
        platformPack: string,
        documentationId: string,
      ): Promise<CompiledDocumentationOutcome>;
      dispose(): void;
    };
    __queryResponsiveness?: {
      readonly startAt: number;
      inputAt: number | null;
      firstRowAt: number | null;
      frameAt: number | null;
      completedAt: number | null;
      renderCount: number;
      longestTimerDelay: number;
      timer: number;
      observer: MutationObserver;
    };
    __packageQueryComposingEditor?: HTMLInputElement;
  }
}

async function boot(page: Page): Promise<void> {
  await page.goto("/package-adoption-gate.html");
  await page.evaluate(async clientUrl => {
    const clientImport: unknown = await import(clientUrl);
    function isWorkerClient(value: unknown): value is WorkerClientModule {
      return typeof value === "object" && value !== null
        && "createProductionEngineWorkerClient" in value
        && typeof value.createProductionEngineWorkerClient === "function";
    }
    if (!isWorkerClient(clientImport)) {
      throw new Error("Published production Worker client exports are missing.");
    }
    const production = clientImport.createProductionEngineWorkerClient(
      location.origin,
      {
        callbacks: {
          failure: failure => {
            throw new Error(`Production Worker failure: ${failure.kind}.`);
          },
          diagnostic: diagnostic => {
            throw new Error(`Production Worker diagnostic: ${diagnostic.kind}.`);
          },
          realmReleased: () => undefined,
        },
        operationDiagnostic: diagnostic => {
          console.error(
            `INSPECT_WEB_PRODUCT_OPERATION_FAILURE:${diagnostic.kind}`,
          );
          throw new Error(`Production operation failed: ${diagnostic.kind}.`);
        },
      },
    );
    await production.ready;
    const client = production.client;
    window.__adoption = {
      queryPackage: (packageId, pkgVersion, framework) =>
        client.package.queryPackage(packageId, pkgVersion, framework),
      queryVersions: (packageId, currentVersion) =>
        client.package.queryPackageVersions(packageId, currentVersion),
      cacheStats: () => client.package.packageCacheStats(),
      queryOccurrences: workspaceJson =>
        client.package.queryWorkspacePackageOccurrences(workspaceJson),
      activate: action =>
        client.package.activateWorkspacePackageOccurrence(action),
      clearOccurrences: () =>
        client.package.clearWorkspacePackageOccurrences(),
      queryDependencies: (packageId, pkgVersion, framework, assemblyId) =>
        client.package.queryPackageDependencies(
          packageId, pkgVersion, framework, assemblyId),
      queryIntegrations: (packageId, pkgVersion, framework, libraryId) =>
        client.analysis.queryPackageIntegrations(
          packageId, pkgVersion, framework, libraryId),
      queryPlatformDocumentation: (
        framework,
        platformVersion,
        assemblyName,
        platformPack,
        documentationId,
      ) => client.package.queryPlatformMemberDocumentation(
        framework,
        platformVersion,
        assemblyName,
        platformPack,
        documentationId,
      ),
      dispose: () => production.dispose(),
    };
  }, workerClientUrl);
}

function driver(page: Page): {
  queryPackageResult(
    fixture: FixtureCoordinate,
    framework?: string,
  ): Promise<PackageLoadResult>;
  queryPackage(fixture: FixtureCoordinate, framework?: string): Promise<PackageSurface>;
  queryCoordinate(packageId: string, version: string, framework: string): Promise<PackageSurface>;
  queryVersions(packageId: string, currentVersion: string): Promise<PackageVersions>;
  cacheStats(): Promise<CacheStats>;
  queryOccurrences(workspace: readonly { package: string; version: string; framework: string }[]): Promise<OccurrenceView>;
  activate(action: string): Promise<OccurrenceActivation>;
  clearOccurrences(): Promise<void>;
  queryDependencies(packageId: string, version: string, framework: string, assemblyId: string): Promise<PackageDependencies>;
  queryIntegrations(packageId: string, version: string, framework: string, libraryId: string): Promise<PackageIntegrations>;
  queryPlatformDocumentation(
    framework: string,
    platformVersion: string,
    assemblyName: string,
    platformPack: string,
    documentationId: string,
  ): Promise<CompiledDocumentationOutcome>;
} {
  const requireSurface = (result: PackageLoadResult): PackageSurface => {
    if (result.surface === null) {
      throw new Error(
        result.versionSettlement.content.failure?.reason
          ?? "Package version settlement did not produce a surface.");
    }
    return result.surface;
  };
  return {
    queryPackageResult: (fixture, framework = fixtureFramework) =>
      page.evaluate(
        ({ packageId, version: ver, framework: tfm }) =>
          window.__adoption!.queryPackage(packageId, ver, tfm),
        { packageId: fixture.packageId, version: fixture.version, framework },
      ),
    queryPackage: async (fixture, framework = fixtureFramework) =>
      requireSurface(await page.evaluate(
        ({ packageId, version: ver, framework: tfm }) =>
          window.__adoption!.queryPackage(packageId, ver, tfm),
        { packageId: fixture.packageId, version: fixture.version, framework },
      )),
    queryCoordinate: (packageId, pkgVersion, framework) =>
      page.evaluate(
        ({ packageId: id, version: ver, framework: tfm }) =>
          window.__adoption!.queryPackage(id, ver, tfm),
        { packageId, version: pkgVersion, framework },
      ).then(requireSurface),
    queryVersions: (packageId, currentVersion) =>
      page.evaluate(
        ({ packageId: id, currentVersion: selectedVersion }) =>
          window.__adoption!.queryVersions(id, selectedVersion),
        { packageId, currentVersion },
      ),
    cacheStats: () => page.evaluate(() => window.__adoption!.cacheStats()),
    queryOccurrences: workspace =>
      page.evaluate(
        value => window.__adoption!.queryOccurrences(value),
        workspace,
      ),
    activate: action =>
      page.evaluate(token => window.__adoption!.activate(token), action),
    clearOccurrences: () =>
      page.evaluate(() => window.__adoption!.clearOccurrences()),
    queryDependencies: (packageId, pkgVersion, framework, assemblyId) =>
      page.evaluate(
        ({ packageId: id, version: ver, framework: tfm, assemblyId: selected }) =>
          window.__adoption!.queryDependencies(id, ver, tfm, selected),
        { packageId, version: pkgVersion, framework, assemblyId },
      ),
    queryIntegrations: (packageId, pkgVersion, framework, libraryId) =>
      page.evaluate(
        ({ packageId: id, version: ver, framework: tfm, libraryId: selected }) =>
          window.__adoption!.queryIntegrations(id, ver, tfm, selected),
        { packageId, version: pkgVersion, framework, libraryId },
      ),
    queryPlatformDocumentation: (
      framework,
      platformVersion,
      assemblyName,
      platformPack,
      documentationId,
    ) => page.evaluate(
      coordinates => window.__adoption!.queryPlatformDocumentation(
        coordinates.framework,
        coordinates.platformVersion,
        coordinates.assemblyName,
        coordinates.platformPack,
        coordinates.documentationId,
      ),
      {
        framework,
        platformVersion,
        assemblyName,
        platformPack,
        documentationId,
      },
    ),
  };
}

function firstOccurrence(view: OccurrenceView): OccurrenceRow {
  const [row] = view.occurrences;
  if (row === undefined) {
    throw new Error("Expected at least one workspace package occurrence.");
  }
  return row;
}

// The published facade hands back one completed assembly-reference outcome: an
// available list (the object case), a failure message (the string case), or the
// generated union's default null. These narrow that result at the consumer,
// exactly as the production renderer must, so a test that expects one case can
// never silently read the other.
function availableReferences(
  result: AssemblyReferenceResult,
): AssemblyReferenceList {
  if (result === null || typeof result === "string") {
    throw new Error(
      "Expected an available assembly-reference list; the facade returned "
        + `${JSON.stringify(result)}.`);
  }
  return result;
}

function referenceFailure(result: AssemblyReferenceResult): string {
  if (typeof result !== "string") {
    throw new Error(
      "Expected an assembly-reference failure message; the facade returned "
        + `${JSON.stringify(result)}.`);
  }
  return result;
}

test("Library API Diff preserves distinct carriage-return and newline Type identities", async ({
  page,
}) => {
  const input = {
    packageModel: {},
    packageId: "Example.Package",
    currentVersion: "2.0.0",
    targetVersion: "1.0.0",
    targetFramework: fixtureFramework,
    compileAssetId: "lib/net11.0/Example.dll",
  };
  const endpoint = (packageVersion: string) => ({
    packageId: input.packageId,
    version: packageVersion,
    framework: input.targetFramework,
    asset: {
      id: input.compileAssetId,
      path: input.compileAssetId,
      assemblyName: "Example",
    },
    assembly: {
      name: "Example",
      version: packageVersion,
      culture: null,
      publicKeyToken: null,
    },
    scope: "Public" as const,
    isComplete: true,
    issues: [],
  });
  const type = (identifier: string) => ({
    documentIdentifier: identifier,
    display: identifier,
    state: "Diff" as const,
    typeDefinitionChanged: false,
    changedMemberCount: 0,
    breakingCount: 0,
    additiveCount: 0,
    potentiallyBreakingCount: 0,
    before: {
      identifier,
      namespace: "Example",
      segments: ["A", "B"],
      display: identifier,
    },
    after: {
      identifier,
      namespace: "Example",
      segments: ["A", "B"],
      display: identifier,
    },
    members: [],
    changes: [],
  });
  const share: InspectionShare = {
    kind: "nonProjectable",
    path: "comparison/endpoints",
    reason: metadataInertString("Ordered endpoints are not shareable."),
    fullUrl: null,
    packet: null,
  };
  const result: BrowserLibraryApiDiffResult = {
    schemaVersion: 1,
    request: {
      schemaVersion: 1,
      packageId: input.packageId,
      currentVersion: input.currentVersion,
      targetVersion: input.targetVersion,
      targetFramework: input.targetFramework,
      compileAssetId: input.compileAssetId,
    },
    kind: "Succeeded",
    value: {
      libraryIdentifier: "Example",
      libraryDisplay: "Example",
      target: endpoint(input.targetVersion),
      current: endpoint(input.currentVersion),
      aggregate: {
        changedTypeCount: 2,
        addedTypeCount: 0,
        removedTypeCount: 0,
        changedMemberCount: 0,
        breakingCount: 0,
        additiveCount: 0,
        potentiallyBreakingCount: 0,
      },
      types: [type("Example.A\rB"), type("Example.A\nB")],
    },
    unavailable: null,
    rejected: null,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
    inspection: {
      content: { outcome: "available", document: {} },
      share,
      diagnostics: [],
    },
  };
  const state: LibraryApiDiffState = {
    status: "ready",
    input,
    result,
  };
  const html = renderLibraryApiDiff(state, value => String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;"));

  await page.setContent(html);
  const rows = page.locator(".library-api-diff-type");
  await expect(rows.nth(0))
    .toHaveAttribute("data-before-type-id", "Example.A\rB");
  await expect(rows.nth(1))
    .toHaveAttribute("data-before-type-id", "Example.A\nB");
});

interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
}

function deferred<T>(): Deferred<T> {
  let complete!: (value: T) => void;
  const promise = new Promise<T>(accept => {
    complete = accept;
  });
  return { promise, resolve: complete };
}

test("product navigation discloses startup-dependent destinations", async ({
  context,
  page,
}) => {
  await context.route("**/*.wasm", () => {});
  await page.goto("/");
  await expect(page.locator(".home-search.engine-pending")).toBeVisible();
  const historyLength = await page.evaluate(() => history.length);

  await page.locator("[data-product-navigation-button]").click();
  for (const destination of ["query", "activity"] as const) {
    const item = page.locator(
      `[data-product-destination="${destination}"]`);
    await expect(item).toHaveAttribute("aria-disabled", "true");
    await expect(item).toHaveAccessibleDescription(
      "Available after runtime startup completes");
    await item.focus();
    await page.keyboard.press("Enter");
    await expect(page).toHaveURL("http://127.0.0.1:4187/");
    await expect(page.locator(".home-title")).toBeVisible();
    await expect(page.locator("[data-product-navigation-menu]")).toBeVisible();
    await expect(item).toBeFocused();
    expect(await page.evaluate(() => history.length)).toBe(historyLength);
  }
});

test.describe("Package Query website over real Wasm", () => {
  test("qualifies package Results by decoded library literal and opens the exact Root", async ({
    page,
    context,
  }) => {
    const registry = new GalleryFixtureRegistry([literalCoordinate]);
    await installGalleryRoutes(context, registry);

    await page.goto("/query");
    const packageInput = page.locator("#package-query-prefix");
    await expect(packageInput).toBeVisible({ timeout: 120_000 });
    await packageInput.fill(literalCoordinate.packageId);
    await page.locator('[data-query-term-add="library-literal"]').click();
    const literalDraft =
      page.locator('[data-query-term-form="draft"] [data-query-term-value]');
    await literalDraft.fill("tail");
    await literalDraft.evaluate(element => {
      if (!(element instanceof HTMLTextAreaElement)) {
        throw new Error("Library literal editor is missing.");
      }
      element.setSelectionRange(0, 0);
    });
    await page.keyboard.type("head");
    await expect(literalDraft).toHaveValue("headtail");
    await literalDraft.evaluate(element => {
      if (!(element instanceof HTMLTextAreaElement)) {
        throw new Error("Library literal editor is missing.");
      }
      element.value = "n";
      element.dispatchEvent(new InputEvent("input", {
        bubbles: true,
        data: "n",
        inputType: "insertCompositionText",
        isComposing: true,
      }));
      element.value = "に";
      element.dispatchEvent(new InputEvent("input", {
        bubbles: true,
        data: "に",
        inputType: "insertCompositionText",
        isComposing: true,
      }));
      element.value = "日本";
      element.dispatchEvent(new InputEvent("input", {
        bubbles: true,
        data: "日本",
        inputType: "insertCompositionText",
        isComposing: true,
      }));
      element.dispatchEvent(new CompositionEvent("compositionend", {
        bubbles: true,
        data: "日本",
      }));
    });
    await expect(literalDraft).toHaveValue("日本");
    await page.locator(
      '[data-query-term-form="draft"] button[type="submit"]').click();
    const literalForm = page.getByRole("form", { name: /library literal/i });
    const literal = literalForm.locator("[data-query-term-value]");
    await expect(literal).toHaveValue("日本");
    await page.locator("#package-query-run").click();
    await expect(literal).toBeVisible();
    await expect(literal).toHaveValue("日本");
    await literal.fill("abcdef");
    await literal.evaluate(element => {
      if (!(element instanceof HTMLTextAreaElement)) {
        throw new Error("Library literal editor is missing.");
      }
      element.setSelectionRange(3, 6, "backward");
    });
    await page.locator(".query-main").evaluate(element =>
      element.dispatchEvent(new Event("scroll")));
    await expect.poll(() => literal.evaluate(element =>
      element instanceof HTMLTextAreaElement
        ? element.selectionDirection
        : null)).toBe("backward");
    await literal.press("Shift+ArrowLeft");
    await page.keyboard.type("X");
    await expect(literal).toHaveValue("abX");
    await literal.fill("first\nsecond");
    await expect(literal).toHaveValue("first\nsecond");
    await literal.fill("\n");
    await expect(literal).toHaveValue("\n");
    await literal.fill("\\r\n");
    await expect(literal).toHaveValue("\\r\n");
    await literal.fill("marker");
    await literal.fill("");
    await expect(literal).toBeVisible();
    await expect(literal).toBeFocused();
    await expect(literal).toHaveValue("");
    await expect(packageInput).toHaveValue(literalCoordinate.packageId);

    const targetFramework = page.locator("#package-query-library-tfm");
    await targetFramework.fill("net8.0");
    await expect(targetFramework).toBeVisible();
    await expect(targetFramework).toBeFocused();
    await expect(targetFramework).toHaveValue("net8.0");
    await expect(packageInput).toHaveValue(literalCoordinate.packageId);

    await page.locator('[data-query-term-add="depends"]').click();
    const firstDraft = page.locator("[data-query-term-draft-value]");
    await firstDraft.fill("Original.Dependency");
    await page.locator(
      '[data-query-term-form="draft"] button[type="submit"]').click();
    const dependsForm =
      page.getByRole("form", { name: /depends on package/i });
    const firstTerm = dependsForm.locator("[data-query-term-value]");
    await firstTerm.fill("Unapplied.Old.Dependency");
    await literal.fill("marker");
    await literal.fill("");
    await firstTerm.fill("New.Dependency");
    await dependsForm.getByRole("button", { name: "Apply" }).click();
    await expect(firstTerm).toHaveValue("New.Dependency");
    await dependsForm.getByRole("button", { name: "Remove" }).click();
    await expect(dependsForm).toHaveCount(0);
    await literal.fill("shared-literal-use-marker");
    await literalForm.getByRole("button", { name: "Apply" }).click();

    await targetFramework.fill("net10.0");
    await targetFramework.evaluate(element => {
      if (!(element instanceof HTMLInputElement)) {
        throw new Error("Library target-framework editor is missing.");
      }
      element.setSelectionRange(3, 5);
    });
    await page.keyboard.type("standard2");
    await expect(targetFramework).toHaveValue("netstandard2.0");
    await targetFramework.fill(fixtureFramework);
    await page.locator("#package-query-run").click();

    await expect(page.locator(".query-row h2"))
      .toHaveText(
        [literalCoordinate.packageId],
        { timeout: 30_000 });
    await expect(page.getByText(
      "Showing 2 of 2 occurrences.",
      { exact: true }))
      .toBeVisible();
    await expect(page.locator(".query-footer"))
      .toContainText("1 matching package · 2 occurrences");
    const open = page.locator("[data-query-row-open]");
    await expect(open).toHaveAttribute(
      "data-query-root-request",
      /.+/);
    await open.click();
    await expect(page.locator(".query-main")).toHaveCount(0);
    await expect(page).not.toHaveURL(
      /\/query(?:[?#]|$)/,
      { timeout: 30_000 });
    expect(registry.downloadCount(literalCoordinate))
      .toBeGreaterThanOrEqual(1);
  });

  test("keeps blank input idle and exact IDs, literal prefixes, and missing IDs distinct", async ({ page, context }) => {
    const workers: Worker[] = [];
    page.on("worker", worker => workers.push(worker));
    const exactRequests: URL[] = [];
    const searchRequests: URL[] = [];
    const enrichment: string[] = [];
    context.on("request", request => {
      const url = new URL(request.url());
      if (url.pathname.endsWith(".nuspec") || url.pathname.endsWith(".nupkg")) {
        enrichment.push(url.href);
      }
    });
    await context.route("https://globalcdn.nuget.org/**", async route => {
      const url = new URL(route.request().url());
      exactRequests.push(url);
      if (url.pathname === "/v3-flatcontainer/newtonsoft.missing/index.json") {
        await route.fulfill({ status: 404, headers: corsHeaders });
        return;
      }
      expect([
        "/v3-flatcontainer/newtonsoft.json/index.json",
        "/v3/registration5-gz-semver2/newtonsoft.json/index.json",
      ]).toContain(url.pathname);
      const data = url.pathname.includes("registration")
        ? { items: [{ items: [{ catalogEntry: {
            id: "Newtonsoft.Json", version: "1.0.0", listed: true,
          } }] }] }
        : { versions: ["1.0.0"] };
      await route.fulfill({
        status: 200, contentType: "application/json", headers: corsHeaders,
        body: JSON.stringify(data),
      });
    });
    await context.route("https://azuresearch-usnc.nuget.org/**", async route => {
      const url = new URL(route.request().url());
      expect(url.pathname).toBe("/query");
      searchRequests.push(url);
      const data = url.searchParams.get("skip") === "0"
        ? ["Newtonsoft.Json", "NewtonsoftOther"].map(id => ({
            id, version: "1.0.0", description: "Prefix fixture.",
            owners: ["Fixture"], totalDownloads: 100, verified: true,
          }))
        : [];
      await route.fulfill({
        status: 200, contentType: "application/json", headers: corsHeaders,
        body: JSON.stringify({ totalHits: 2, data }),
      });
    });

    await page.goto("/query");
    const input = page.locator("#package-query-prefix");
    await expect(input).toBeVisible({ timeout: 120_000 });
    expect(workers).toHaveLength(1);
    await expect(page.locator("#package-query-run")).toHaveText("Run query");
    await expect(page.locator("#package-query-discover")).toHaveCount(0);
    await expect(page.locator("#package-query-type")).toHaveCount(0);
    await expect(page.locator("#package-query-order")).toHaveCount(0);
    expect(exactRequests).toHaveLength(0);
    expect(searchRequests).toHaveLength(0);

    await page.locator("#package-query-run").click();
    expect(exactRequests).toHaveLength(0);
    expect(searchRequests).toHaveLength(0);

    await input.fill("Newtonsoft.Json");
    await page.locator("#package-query-run").click();
    await expect(page.locator(".query-row h2"))
      .toHaveText(["Newtonsoft.Json"], { timeout: 30_000 });
    await expect(page.locator(".query-footer"))
      .toContainText("exact package selection complete", { timeout: 30_000 });
    expect(exactRequests).toHaveLength(2);
    expect(searchRequests).toHaveLength(0);
    await expect(page.locator("#package-query-type")).toHaveCount(0);

    await input.fill("Newtonsoft.*");
    await page.locator("#package-query-run").click();
    await expect(page.locator(".query-row h2")).toHaveText(["Newtonsoft.Json"]);
    await expect(page.locator(".query-footer")).toContainText("all matches");
    expect(searchRequests.length).toBeGreaterThan(0);
    expect(exactRequests).toHaveLength(2);

    await input.fill("Newtonsoft*");
    await page.locator("#package-query-run").click();
    await expect(page.locator(".query-row h2")).toHaveText(["Newtonsoft.Json", "NewtonsoftOther"]);
    await expect(page.locator(".query-footer")).toContainText("all matches");
    const searchCount = searchRequests.length;

    await input.fill("Newtonsoft.Missing");
    await page.locator("#package-query-run").click();
    await expect(page.locator(".query-empty")).toContainText("No fallback search was used.");
    await expect(page.locator(".query-row")).toHaveCount(0);
    expect(searchRequests).toHaveLength(searchCount);
    expect(exactRequests.at(-1)?.pathname).toBe("/v3-flatcontainer/newtonsoft.missing/index.json");
    expect(enrichment).toEqual([]);
  });

  test("classifies Azure.Mcp as a CLI v2 tool through bounded package content", async ({
    page,
    context,
  }) => {
    const toolManifest = packageQueryManifest("Azure.Mcp", "2.0.5", true);
    const tool: FixtureCoordinate = {
      packageId: "Azure.Mcp",
      version: "2.0.5",
      manifest: toolManifest,
      archive: storedZip([
        { name: "Azure.Mcp.nuspec", bytes: toolManifest },
        {
          name: "tools/net10.0/any/DotnetToolSettings.xml",
          bytes: Buffer.from(
            `<DotNetCliTool Version="2">
  <Commands>
    <Command Name="azmcp" EntryPoint="Azure.Mcp.dll" Runner="dotnet" />
  </Commands>
</DotNetCliTool>
`,
            "utf8",
          ),
        },
      ]),
    };
    const libraryManifest =
      packageQueryManifest("Azure.Library", "1.0.0", false);
    const library: FixtureCoordinate = {
      packageId: "Azure.Library",
      version: "1.0.0",
      manifest: libraryManifest,
      archive: storedZip([
        { name: "Azure.Library.nuspec", bytes: libraryManifest },
      ]),
    };
    const registry = new GalleryFixtureRegistry([tool, library]);
    await installGalleryRoutes(context, registry);

    await context.route("https://azuresearch-usnc.nuget.org/**", async route => {
      const url = new URL(route.request().url());
      expect(url.pathname).toBe("/query");
      const data = url.searchParams.get("skip") === "0"
        ? [
            {
              id: tool.packageId,
              version: tool.version,
              description: "Azure MCP Server.",
              owners: ["Microsoft"],
              totalDownloads: 2_208_344,
              verified: true,
            },
            {
              id: library.packageId,
              version: library.version,
              description: "Ordinary library fixture.",
              owners: ["Fixture"],
              totalDownloads: 1,
              verified: false,
            },
          ]
        : [];
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        headers: corsHeaders,
        body: JSON.stringify({ totalHits: 2, data }),
      });
    });

    await page.goto("/query");
    const input = page.locator("#package-query-prefix");
    await expect(input).toBeVisible({ timeout: 120_000 });
    const toolFormatPreset = page.locator(
      '[data-query-preset="tool-format:eq:v2"]',
    );
    await toolFormatPreset.click();
    await expect(toolFormatPreset).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".query-preset-disclosure", {
      hasText: "Candidate bound K:",
    }))
      .toContainText("Candidate bound K: 20");

    await input.fill("Azure.*");
    await page.locator("#package-query-run").click();

    const row = page.locator(".query-row");
    await expect(row).toHaveCount(1, { timeout: 30_000 });
    await expect(row.locator("h2")).toHaveText(tool.packageId);
    await expect(row.locator(".query-tier")).toHaveText("package-content");
    await expect(row.locator(".query-evidence")).toContainText(
      ".NET tool settings version: 2",
    );
    expect(registry.downloadCount(tool)).toBe(1);
    expect(registry.downloadCount(library)).toBe(0);
  });

  test("applies, edits, repeats, and removes product-issued dependency terms", async ({
    page,
    context,
  }) => {
    const hostingManifest = packageQueryManifest(
      "Contoso.HostingConsumer",
      version,
      false,
      ["Microsoft.Extensions.Hosting"]);
    const injectionManifest = packageQueryManifest(
      "Contoso.InjectionConsumer",
      version,
      false,
      ["Microsoft.Extensions.DependencyInjection"]);
    const hosting: FixtureCoordinate = {
      packageId: "Contoso.HostingConsumer",
      version,
      manifest: hostingManifest,
      archive: healthyArchive,
    };
    const injection: FixtureCoordinate = {
      packageId: "Contoso.InjectionConsumer",
      version,
      manifest: injectionManifest,
      archive: healthyArchive,
    };
    const registry = new GalleryFixtureRegistry([hosting, injection]);
    await installGalleryRoutes(context, registry);
    let searchRequests = 0;
    await context.route("https://azuresearch-usnc.nuget.org/**", async route => {
      searchRequests++;
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        headers: corsHeaders,
        body: JSON.stringify({
          totalHits: 2,
          data: [hosting, injection].map(fixture => ({
            id: fixture.packageId,
            version: fixture.version,
            description: "Dependency term fixture.",
            owners: ["Fixture"],
            totalDownloads: 1,
            verified: false,
          })),
        }),
      });
    });

    await page.goto("/query");
    const prefix = page.locator("#package-query-prefix");
    await expect(prefix).toBeVisible({ timeout: 120_000 });
    await prefix.fill("Contoso.*");
    await page.locator('[data-query-term-add="depends"]').click();
    const draft = page.locator("[data-query-term-draft-value]");
    await expect(draft).toBeFocused();
    expect(searchRequests).toBe(0);
    await draft.fill("Microsoft.Extensions.Hosting");
    await expect(draft).toHaveValue("Microsoft.Extensions.Hosting");
    expect(searchRequests).toBe(0);
    await draft.fill("   ");
    await page.locator('[data-query-term-form="draft"] button[type="submit"]').click();
    await expect(draft).toHaveJSProperty("validationMessage", "Enter a term value.");
    expect(searchRequests).toBe(0);
    await draft.fill("Microsoft.Extensions.Hosting");
    await expect(draft).toHaveJSProperty("validationMessage", "");
    await page.locator('[data-query-term-form="draft"] button[type="submit"]').click();

    const rows = page.locator(".query-row h2");
    await expect(rows).toHaveText([hosting.packageId], { timeout: 30_000 });
    await expect(page.locator('[data-query-term-form="0"]'))
      .toContainText("depends on package");
    expect(registry.downloadCount(hosting)).toBe(0);
    expect(registry.downloadCount(injection)).toBe(0);

    const firstValue =
      page.locator('[data-query-term-form="0"] [data-query-term-value]');
    await firstValue.fill("Microsoft.Extensions.DependencyInjection");
    await firstValue.evaluate(element => {
      if (!(element instanceof HTMLInputElement)) {
        throw new Error("Active Package Query term editor is missing.");
      }
      window.__packageQueryComposingEditor = element;
      element.focus();
      element.setSelectionRange(10, 30, "backward");
      element.dispatchEvent(new CompositionEvent("compositionstart", {
        bubbles: true,
        data: "",
      }));
      element.dispatchEvent(new InputEvent("input", {
        bubbles: true,
        data: element.value,
        inputType: "insertCompositionText",
        isComposing: true,
      }));
    });
    await expect(firstValue).toBeFocused();
    await expect(firstValue)
      .toHaveAttribute("data-query-editor-composing", "true");
    await page.locator('[data-query-term-add="depends"]')
      .evaluate(element => {
        if (!(element instanceof HTMLButtonElement)) {
          throw new Error("Package Query term action is missing.");
        }
        element.click();
      });
    await expect.poll(() => page.evaluate(
      () => window.__packageQueryComposingEditor?.isConnected,
    )).toBe(true);
    await expect(page.locator("[data-query-term-draft-value]")).toHaveCount(0);
    await page.evaluate(() => {
      const element = window.__packageQueryComposingEditor;
      if (!(element instanceof HTMLInputElement)) {
        throw new Error("Composing Package Query editor is missing.");
      }
      element.dispatchEvent(new CompositionEvent("compositionend", {
        bubbles: true,
        data: element.value,
      }));
    });
    await expect.poll(() => page.evaluate(
      () => window.__packageQueryComposingEditor?.isConnected,
    )).toBe(false);
    await expect(page.locator("[data-query-term-draft-value]")).toBeVisible();
    await expect(firstValue)
      .toHaveValue("Microsoft.Extensions.DependencyInjection");
    await expect(firstValue).toHaveJSProperty("selectionStart", 10);
    await expect(firstValue).toHaveJSProperty("selectionEnd", 30);
    await expect(firstValue).toHaveJSProperty(
      "selectionDirection",
      "backward");
    await page.locator("[data-query-term-draft-cancel]").click();
    await firstValue.fill("not a package id");
    const beforeInvalid = searchRequests;
    await page.locator('[data-query-term-form="0"] button[type="submit"]').click();
    await expect(page.getByRole("region", { name: "Package query results" }))
      .toContainText("term value is invalid");
    expect(searchRequests).toBe(beforeInvalid);

    await firstValue.fill("Microsoft.Extensions.DependencyInjection");
    await page.locator('[data-query-term-form="0"] button[type="submit"]').click();
    await expect(rows).toHaveText([injection.packageId]);

    await firstValue.fill("Microsoft.Extensions.Logging");
    await page.locator(".brand").click();
    await page.locator('[data-product-destination="home"]').click();
    await expect(page.locator(".home-search")).toBeVisible();
    await page.goBack();
    await expect(firstValue)
      .toHaveValue("Microsoft.Extensions.DependencyInjection");

    await page.locator('[data-query-term-add="depends"]').click();
    await page.locator("[data-query-term-draft-value]")
      .fill("Microsoft.Extensions.Hosting");
    await page.locator('[data-query-term-form="draft"] button[type="submit"]').click();
    await expect(page.locator(".query-row")).toHaveCount(0);
    await expect(page.locator("[data-query-term-form]")).toHaveCount(2);

    await page.locator('[data-query-term-remove="1"]').click();
    await expect(rows).toHaveText([injection.packageId]);
    await page.locator('[data-query-term-remove="0"]').click();
    await expect(rows).toHaveText([
      hosting.packageId,
      injection.packageId,
    ]);
  });

  test("retains 100 prefix results while mounting a bounded row window", async ({
    page,
    context,
  }) => {
    const requests: URL[] = [];
    await context.route("https://azuresearch-usnc.nuget.org/**", async route => {
      const url = new URL(route.request().url());
      expect(url.pathname).toBe("/query");
      requests.push(url);
      const skip = Number(url.searchParams.get("skip") ?? "0");
      const data = skip === 0
        ? Array.from({ length: 100 }, (_, index) => ({
            id: `System.Package${index.toString().padStart(3, "0")}`,
            version: "1.0.0",
            description: "Windowed prefix fixture.",
            owners: ["Fixture"],
            totalDownloads: index,
            verified: false,
          }))
        : [];
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        headers: corsHeaders,
        body: JSON.stringify({ totalHits: 100, data }),
      });
    });

    await page.goto("/query");
    const input = page.locator("#package-query-prefix");
    const main = page.locator(".query-main");
    const footer = page.locator(".query-footer");
    await expect(input).toBeVisible({ timeout: 120_000 });
    await input.fill("System*");
    await page.locator("#package-query-run").click();

    await expect(footer).toContainText("20 packages");
    await expect(page.locator(".query-row")).toHaveCount(20);
    const firstOpen = page.locator("[data-query-row-open]").first();
    await firstOpen.focus();
    await expect(firstOpen).toBeFocused();
    for (let target = 30; target <= 100; target += 10) {
      await main.evaluate(element => {
        element.scrollTop = element.scrollHeight;
      });
      await expect.poll(async () => {
        const text = (await footer.textContent() ?? "").trim();
        return Number(text.match(/^(\d+) packages?/)?.[1] ?? "0");
      }).toBeGreaterThanOrEqual(target);
      expect(await page.locator(".query-row").count())
        .toBeLessThanOrEqual(30);
      if (target >= 40) {
        await expect(page.locator("#package-query-results")).toBeFocused();
      }
    }

    await expect(footer)
      .toContainText("100 packages · bounded: first 100 matches");
    await expect(page.locator(".query-row h2").last())
      .toHaveText("System.Package099");
    await main.evaluate(element => {
      element.scrollTop = 0;
    });
    await expect(page.locator(".query-row h2").first())
      .toHaveText("System.Package000");
    await expect(page.locator(".query-row")).toHaveCount(30);
    expect(requests).toHaveLength(1);
  });

});

test.describe("Package Activity website over real Wasm", () => {
  test("streams product activity and reconciles typed partial evidence", async ({
    page,
    context,
  }) => {
    const now = new Date();
    const horizon = new Date(now.getTime() + 5 * 60 * 1_000);
    const catalogItems = Array.from({ length: 40 }, (_, index) => {
      const ordinal = index.toString().padStart(2, "0");
      return {
        "@id": `https://api.nuget.org/v3/catalog0/data/item-${ordinal}.json`,
        "@type": "nuget:PackageDetails",
        commitId: `commit-${ordinal}`,
        commitTimeStamp: new Date(
          now.getTime() - (40 - index) * 30 * 60 * 1_000).toISOString(),
        "nuget:id": "Microsoft.Extensions.AI",
        "nuget:version": `1.${index}.0`,
      };
    });
    const firstCommit = new Date(catalogItems[0]!.commitTimeStamp);
    const secondCommit = new Date(catalogItems.at(-1)!.commitTimeStamp);
    const requests: URL[] = [];
    const advisoryRequested = deferred<void>();
    const releaseAdvisory = deferred<void>();
    await context.route("https://azuresearch-usnc.nuget.org/**", route =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        headers: corsHeaders,
        body: JSON.stringify({ totalHits: 0, data: [] }),
      }));
    await context.route("**/api/package-changes/**", async route => {
      const url = new URL(route.request().url());
      requests.push(url);
      if (url.pathname.endsWith("/advisories")) {
        const affects = url.searchParams.get("affects") ?? "";
        if (url.searchParams.has("after")) {
          await route.fulfill({
            status: 503,
            contentType: "application/json",
            body: JSON.stringify({ error: "controlled partial page" }),
          });
          return;
        }
        if (!affects.split(",").includes("microsoft.extensions.ai")) {
          await route.fulfill({
            status: 200,
            contentType: "application/json",
            body: "[]",
          });
          return;
        }
        advisoryRequested.resolve();
        await releaseAdvisory.promise;
        const next = new URL("https://api.github.com/advisories");
        for (const name of [
          "ecosystem",
          "type",
          "is_withdrawn",
          "per_page",
          "affects",
        ]) {
          const value = url.searchParams.get(name);
          if (value !== null) next.searchParams.set(name, value);
        }
        next.searchParams.set("after", "Y3Vyc29yOnYyOpHOAQ==");
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          headers: {
            Link: `<${next.toString()}>; rel="next"`,
          },
          body: JSON.stringify([{
            ghsa_id: "GHSA-1234-5678-9012",
            cve_id: "CVE-2026-1234",
            type: "reviewed",
            severity: "high",
            published_at: firstCommit.toISOString(),
            updated_at: secondCommit.toISOString(),
            withdrawn_at: null,
            vulnerabilities: [{
              package: {
                ecosystem: "nuget",
                name: "Microsoft.Extensions.AI",
              },
              vulnerable_version_range: "< 1.1.0",
              first_patched_version: null,
            }],
          }]),
        });
        return;
      }
      const providerPath = url.searchParams.get("path");
      let body: string;
      if (providerPath === "/v3/index.json") {
        body = JSON.stringify({
          version: "3.0.0",
          resources: [{
            "@id": "https://api.nuget.org/v3/catalog0/index.json",
            "@type": "Catalog/3.0.0",
          }],
        });
      } else if (providerPath === "/v3/catalog0/index.json") {
        body = JSON.stringify({
          commitId: "index",
          commitTimeStamp: horizon.toISOString(),
          count: catalogItems.length,
          items: catalogItems.map((item, index) => ({
            "@id": `https://api.nuget.org/v3/catalog0/page-${index}.json`,
            commitId: `page-${index}`,
            commitTimeStamp: index === catalogItems.length - 1
              ? horizon.toISOString()
              : item.commitTimeStamp,
            count: 1,
          })),
        });
      } else if (/^\/v3\/catalog0\/page-\d+\.json$/.test(
        providerPath ?? "")) {
        const pageIndex = Number(
          /^\/v3\/catalog0\/page-(\d+)\.json$/.exec(providerPath ?? "")?.[1]);
        body = JSON.stringify({
          commitId: `page-${pageIndex}`,
          commitTimeStamp: pageIndex === catalogItems.length - 1
            ? horizon.toISOString()
            : catalogItems[pageIndex]!.commitTimeStamp,
          count: 1,
          parent: "https://api.nuget.org/v3/catalog0/index.json",
          items: [catalogItems[pageIndex]],
        });
      } else {
        await route.fulfill({ status: 404 });
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body,
      });
    });

    await page.goto("/");
    const homeSearch = page.locator("#spotlight-input");
    await expect(homeSearch).toBeVisible({ timeout: 120_000 });
    await homeSearch.fill("activity");
    // Package-search completion replaces the result list, so settle it before clicking
    // the built-in Activity route.
    await expect(page.locator(".spotlight-hint"))
      .toHaveText("Searching nuget.org…");
    await expect(page.locator(".spotlight-hint")).toHaveCount(0);
    await page.locator('[data-sl-package-activity="1"]').click();
    await expect(page).toHaveURL(/\/activity$/);
    await page.goBack();
    await expect(page).toHaveURL(/\/$/);
    await expect(homeSearch).toBeFocused();
    await page.goForward();
    await expect(page).toHaveURL(/\/activity$/);
    await expect(page.locator("#package-changes-heading"))
      .toHaveText("Package Activity");
    await page.reload();
    await expect(page.locator("#package-changes-heading"))
      .toHaveText("Package Activity", { timeout: 120_000 });
    await expect(page).toHaveTitle("Package Activity · dotnet-inspect");
    const packageSet = page.locator("#package-changes-package-set");
    await expect(packageSet).toBeVisible();
    expect(await packageSet.locator("option").count()).toBeGreaterThan(0);
    await expect(page.locator(".package-changes-package-set-summary"))
      .not.toHaveText("");

    const maximumRows = page.locator("#package-changes-limit");
    await maximumRows.fill("");
    await page.locator("#package-changes-run").click();
    expect(await maximumRows.evaluate(input => {
      if (!(input instanceof HTMLInputElement)) {
        throw new Error("Package Activity maximum rows is not an input.");
      }
      return input.validity.valueMissing;
    })).toBe(true);
    expect(requests).toHaveLength(0);
    await maximumRows.fill("100");

    const dateTimeInput = (value: Date) => value.toISOString().slice(0, 16);
    await page.locator("#package-changes-custom-interval").check();
    await page.locator("#package-changes-from")
      .fill(dateTimeInput(new Date(now.getTime() - 10 * 24 * 60 * 60 * 1_000)));
    const through = page.locator("#package-changes-through");
    await through.fill(
      dateTimeInput(new Date(now.getTime() + 40 * 24 * 60 * 60 * 1_000)));
    await page.locator("#package-changes-run").click();
    await expect(through).toHaveJSProperty(
      "validationMessage",
      "The custom interval cannot exceed 42 days.");
    expect(requests).toHaveLength(0);

    await through.fill(dateTimeInput(new Date(now.getTime() - 60 * 1_000)));
    await page.locator("#package-changes-run").click();
    await advisoryRequested.promise;
    await expect(page.locator(".package-changes-summary"))
      .toContainText("streaming");
    await expect(page.locator(".package-changes-progress")).toBeVisible();
    releaseAdvisory.resolve();

    await expect(page.locator(".package-changes-summary"))
      .toContainText("partial", { timeout: 30_000 });
    await expect(page.locator(".package-changes-coverage"))
      .toContainText("Completion and coverage");
    await expect(page.locator(".package-changes-coverage"))
      .toContainText("40 of 40 eligible");
    await expect(page.locator(".package-changes-progress li")).toHaveCount(2);
    await expect(page.locator(".package-changes-row")).toHaveCount(30);
    await expect(page.locator(".package-changes-row h2").first())
      .toHaveText("Microsoft.Extensions.AI 1.39.0");
    await expect(page.locator(".package-changes-evidence-grid").first())
      .toContainText("Partial");
    await expect(page.locator(".package-changes-failures"))
      .toContainText("Advisory provider");
    await expect(page.locator(".package-changes-row").first())
      .toContainText("commit-39");

    await page.addStyleTag({
      content: ".package-changes-row { min-height: 1760px; }",
    });
    const scroller = page.locator(".query-main");
    const geometry = await page.evaluate(() => {
      const scroll = document.querySelector<HTMLElement>(".query-main");
      const window = document.querySelector<HTMLElement>(
        "#package-changes-row-window");
      const first = document.querySelector<HTMLElement>(
        "[data-changes-row-index='0']");
      if (!scroll || !window || !first) {
        throw new Error("Package Activity window geometry is unavailable.");
      }
      const scrollRect = scroll.getBoundingClientRect();
      const windowRect = window.getBoundingClientRect();
      const extent = first.getBoundingClientRect().height + 10;
      const surfaceTop = windowRect.top - scrollRect.top + scroll.scrollTop;
      scroll.scrollTop = surfaceTop + extent * 6 - 50;
      scroll.dispatchEvent(new Event("scroll"));
      return { extent, target: scroll.scrollTop };
    });
    expect(geometry.extent).toBeGreaterThan(1_600);
    await expect(page.locator("#package-changes-row-window"))
      .toHaveAttribute("aria-label", "Activity events 1 through 30 of 40");
    const retainedLink = page.locator(
      "[data-changes-row-index='10'] [data-package-changes-focus-key='package-10']");
    await retainedLink.focus();
    await expect(retainedLink).toBeFocused();
    await scroller.evaluate(element => {
      element.scrollTop += 100;
      element.dispatchEvent(new Event("scroll"));
    });
    await expect(page.locator("#package-changes-row-window"))
      .not.toHaveAttribute("aria-label", "Activity events 1 through 30 of 40");
    expect(await scroller.evaluate(element => element.scrollTop))
      .toBeGreaterThanOrEqual(geometry.target);
    await expect(page.locator(".package-changes-row")).toHaveCount(30);
    await expect(retainedLink).toBeFocused();

    expect(requests.some(request =>
      request.pathname.endsWith("/nuget")
      && request.searchParams.get("path") === "/v3/index.json")).toBe(true);
    expect(requests.some(request =>
      request.pathname.endsWith("/advisories")
      && request.searchParams.has("after"))).toBe(true);
  });
});

test.describe("artifact-backed package scope adoption over real Wasm", () => {
  test.describe.configure({ timeout: 240_000 });

  test("returns compact typed platform documentation through the production Worker", async ({
    page,
    context,
  }) => {
    const registry = new GalleryFixtureRegistry([
      platformRuntimeDocumentation,
      platformReferenceDocumentation,
    ]);
    await installGalleryRoutes(context, registry);
    await boot(page);

    const outcome = await driver(page).queryPlatformDocumentation(
      fixtureFramework,
      platformDocumentationVersion,
      platformDocumentationAssemblyFileName,
      "netcore.app",
      "M:InspectWeb.DocumentationFixtures.HiddenDocumentedType.Read",
    );

    expect(outcome.kind).toBe("available");
    if (outcome.kind !== "available") {
      throw new Error(`Expected available documentation, received ${outcome.kind}.`);
    }
    if (!outcome.source || !outcome.documentation) {
      throw new Error("Available documentation omitted its payload.");
    }
    expect(outcome.source.kind).toBe("Platform");
    expect(outcome.documentation.summary)
      .toBe("Reads documentation from a non-public type.");
    expect(JSON.stringify(outcome).length).toBeLessThanOrEqual(4_096);
    expect(registry.downloadCount(platformRuntimeDocumentation)).toBe(1);
    expect(registry.downloadCount(platformReferenceDocumentation)).toBe(1);
    await page.evaluate(() => window.__adoption!.dispose());
  });

  test("opens a search-hidden exact coordinate without fallback", async ({
    page,
    context,
  }) => {
    const registry = new GalleryFixtureRegistry([healthy]);
    await installGalleryRoutes(context, registry);
    let searchRequests = 0;
    await context.route("https://azuresearch-usnc.nuget.org/**", async route => {
      searchRequests++;
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        headers: corsHeaders,
        body: JSON.stringify({ totalHits: 0, data: [] }),
      });
    });

    await page.goto("/");
    let search = page.locator("#spotlight-input");
    await expect(search).toBeVisible({ timeout: 120_000 });
    await search.fill("InspectWeb.Adoption.Missing@9.9.9");
    await page.locator(
      '[data-sl-pkg-load="InspectWeb.Adoption.Missing"]',
    ).click();
    await expect(page.locator(".query-notice"))
      .toContainText("InspectWeb.Adoption.Missing", { timeout: 180_000 });
    expect(searchRequests).toBe(0);

    await page.goto("/");
    search = page.locator("#spotlight-input");
    await expect(search).toBeVisible({ timeout: 120_000 });
    await search.fill(`${healthy.packageId}@${healthy.version}`);
    const exact = page.locator(
      `[data-sl-pkg-load="${healthy.packageId}"]`,
    );
    await expect(exact).toContainText("exact coordinate · listed or unlisted");
    await exact.click();

    await expect(page.locator(".inspected-target"))
      .toContainText(healthy.packageId, { timeout: 180_000 });
    await expect.poll(() => new URL(page.url()).searchParams.get("package"))
      .toBe(healthy.packageId);
    await expect(page.getByTitle(
      `${healthy.packageId}@${healthy.version}`,
      { exact: true },
    )).toBeVisible();
    await expect(page.getByTitle(fixtureFramework, { exact: true }))
      .toBeVisible();
    expect(searchRequests).toBe(0);
    expect(registry.downloadCount(healthy)).toBe(1);
  });

  test("drives the production opening, join, occurrence, and rejection contracts", async ({
    page,
    context,
  }) => {
    page.on("console", message => {
      const text = message.text();
      if (text.startsWith("INSPECT_WEB_PRODUCT_OPERATION_FAILURE:")) {
        console.log(text);
      }
    });
    const workers: Worker[] = [];
    page.on("worker", worker => workers.push(worker));
    const registry = new GalleryFixtureRegistry(allFixtures);
    await installGalleryRoutes(context, registry);
    await boot(page);
    expect(workers).toHaveLength(1);
    const engine = driver(page);

    // Ordinary singleton opening yields healthy evidence.
    const openedResult = await engine.queryPackageResult(healthy);
    const opened = openedResult.surface;
    expect(opened).not.toBeNull();
    if (opened === null) {
      throw new Error("Exact package settlement did not produce a surface.");
    }
    expect(opened.package).toBe(healthy.packageId);
    expect(opened.version).toBe(version);
    expect(opened.activeFramework).toBe(fixtureFramework);
    expect(opened.assemblies.length).toBeGreaterThan(0);
    expect(opened.types.length).toBeGreaterThan(0);
    expect(opened.types.some(type => type.name === healthyTypeName)).toBe(true);
    expect(opened.inspectionErrors.length).toBe(0);
    expect(openedResult.versionSettlement.content.kind).toBe("Settled");
    expect(openedResult.versionSettlement.content.result?.request).toEqual({
      packageId: healthy.packageId.toLowerCase(),
      version,
    });
    expect(openedResult.versionSettlement.content.result?.coordinate).toEqual({
      packageId: healthy.packageId.toLowerCase(),
      version,
    });
    expect(openedResult.versionSettlement.share.kind).toBe("NonProjectable");
    expect(openedResult.versionSettlement.diagnostics).toEqual([]);

    // The production C# serializer, generated facade, Worker transport, and
    // authored TypeScript agree that unavailable predecessor facts are absent.
    const withPredecessor = await engine.queryVersions(
      libraryDiffPackageId,
      libraryDiffV2.version,
    );
    expect(withPredecessor).toEqual({
      versions: ["2.0.0", "1.0.0"],
      currentVersionInsertionIndex: 0,
      previousVersion: "1.0.0",
    });
    const withoutPredecessor = await engine.queryVersions(
      healthy.packageId,
      healthy.version,
    );
    expect(withoutPredecessor).toEqual({
      versions: [healthy.version],
      currentVersionInsertionIndex: 0,
    });

    // A terminal settlement failure crosses the same generated facade and
    // Worker boundary as typed Content rather than becoming a managed fault.
    const missingResult = await engine.queryPackageResult({
      ...healthy,
      packageId: "InspectWeb.PackageVersionSettlement.Missing",
      version: "latest",
    });
    expect(missingResult.surface).toBeNull();
    expect(missingResult.versionSettlement.content.kind).toBe("NotSettled");
    expect(missingResult.versionSettlement.content.result).toBeNull();
    expect(missingResult.versionSettlement.content.failure?.request).toEqual({
      packageId: "inspectweb.packageversionsettlement.missing",
      version: null,
    });
    expect(missingResult.versionSettlement.content.failure?.kind).toBe("NotFound");
    expect(
      missingResult.versionSettlement.content.failure?.reason.length,
    ).toBeGreaterThan(0);
    expect(missingResult.versionSettlement.share.kind).toBe("NonProjectable");
    expect(missingResult.versionSettlement.diagnostics).toEqual([]);

    // A repeated request for the same coordinate joins the retained scope: no
    // new workspace entry, and the archive was fetched exactly once.
    const afterFirst = await engine.cacheStats();
    const rejoined = await engine.queryPackage(healthy);
    const afterRejoin = await engine.cacheStats();
    expect(rejoined).toEqual(opened);
    expect(afterRejoin.workspaces).toBe(afterFirst.workspaces);
    expect(registry.downloadCount(healthy)).toBe(1);

    // Concurrent requests for the SAME previously unopened coordinate join a
    // single scope: both observe the same surface, the archive downloads once,
    // and exactly one workspace is counted for the coordinate.
    const beforeJoin = await engine.cacheStats();
    const [joinedA, joinedB] = await Promise.all([
      engine.queryPackage(joinCoordinate),
      engine.queryPackage(joinCoordinate),
    ]);
    const afterJoin = await engine.cacheStats();
    expect(joinedA.package).toBe(joinCoordinate.packageId);
    expect(joinedA).toEqual(joinedB);
    expect(joinedA.types.some(type => type.name === healthyTypeName)).toBe(true);
    expect(registry.downloadCount(joinCoordinate)).toBe(1);
    expect(afterJoin.workspaces).toBe(beforeJoin.workspaces + 1);

    // Awaitable occurrence activation following queryWorkspacePackageOccurrences.
    const view = await engine.queryOccurrences([
      { package: healthy.packageId, version, framework: fixtureFramework },
    ]);
    expect(view.superseded).toBe(false);
    expect(view.occurrences.length).toBe(1);
    const action = firstOccurrence(view).action;
    const activation = await engine.activate(action);
    expect(activation.activated).toBe(true);
    expect(activation.superseded).toBe(false);
    expect(activation.package?.package).toBe(healthy.packageId);

    // A stale occurrence action after clear reports a superseded rejection.
    const staleView = await engine.queryOccurrences([
      { package: healthy.packageId, version, framework: fixtureFramework },
    ]);
    const staleAction = firstOccurrence(staleView).action;
    await engine.clearOccurrences();
    const clearedActivation = await engine.activate(staleAction);
    expect(clearedActivation.activated).toBe(false);
    expect(clearedActivation.superseded).toBe(true);
    expect(clearedActivation.package).toBeNull();

    // A stale occurrence action after replacement is superseded, while the
    // replacement occurrence activates successfully.
    const firstView = await engine.queryOccurrences([
      { package: occurrenceOne.packageId, version, framework: fixtureFramework },
    ]);
    const firstAction = firstOccurrence(firstView).action;
    const secondView = await engine.queryOccurrences([
      { package: occurrenceTwo.packageId, version, framework: fixtureFramework },
    ]);
    const secondAction = firstOccurrence(secondView).action;
    const supersededActivation = await engine.activate(firstAction);
    expect(supersededActivation.activated).toBe(false);
    expect(supersededActivation.superseded).toBe(true);
    const replacementActivation = await engine.activate(secondAction);
    expect(replacementActivation.activated).toBe(true);
    expect(replacementActivation.package?.package).toBe(occurrenceTwo.packageId);

    // Valid-reference / malformed-implementation: queryPackage returns the
    // healthy reference surface for both selected names (both distinct-identity
    // reference assemblies are valid), with no inspection errors and a Selected
    // compile library. The analysis facade, initialized in the SAME runtime,
    // then surfaces the broken implementation's selected rejection beside that
    // healthy evidence: the malformed lib for the broken carrier makes the
    // integrations result incomplete with a visible rejection, while the compile
    // library selection stays Selected.
    const malformedSurface = await engine.queryPackage(malformed);
    expect(malformedSurface.assemblies.length).toBeGreaterThan(1);
    expect(malformedSurface.types.some(type => type.name === healthyTypeName)).toBe(true);
    expect(malformedSurface.inspectionErrors.length).toBe(0);
    expect(malformedSurface.inspectionError).toBeNull();
    expect(String(malformedSurface.compileLibrary.status)).toBe("Selected");

    const brokenLibrary = malformedSurface.assemblies.find(
      library => library.asset === `ref/${fixtureFramework}/${brokenAssemblyFileName}`,
    );
    const healthyLibrary = malformedSurface.assemblies.find(
      library => library.asset === `ref/${fixtureFramework}/${healthyAssemblyFileName}`,
    );
    if (brokenLibrary === undefined || healthyLibrary === undefined) {
      throw new Error("Expected both Library descriptors in the malformed package.");
    }
    const integrations = await engine.queryIntegrations(
      malformed.packageId,
      version,
      fixtureFramework,
      brokenLibrary.id,
    );
    expect(integrations.package).toBe(malformed.packageId);
    expect(String(integrations.compileLibrary.status)).toBe("Selected");
    expect(integrations.isComplete).toBe(false);
    expect(integrations.inspectionError).not.toBeNull();
    expect(integrations.inspectionError ?? "").toContain("InvalidImage");
    const healthyIntegrations = await engine.queryIntegrations(
      malformed.packageId,
      version,
      fixtureFramework,
      healthyLibrary.id,
    );
    expect(healthyIntegrations.isComplete).toBe(true);
    expect(healthyIntegrations.inspectionError).toBeNull();
    await page.evaluate(() => window.__adoption!.dispose());
  });

  test("holds the four-scope bound and evicts to admit new scopes", async ({
    page,
    context,
  }) => {
    const registry = new GalleryFixtureRegistry(allFixtures);
    await installGalleryRoutes(context, registry);
    await boot(page);
    const engine = driver(page);

    const maxOpenScopes = 4;
    const observed: number[] = [];
    for (const coordinate of scopeCoordinates) {
      const surface = await engine.queryPackage(coordinate);
      expect(surface.package).toBe(coordinate.packageId);
      expect(surface.types.length).toBeGreaterThan(0);
      observed.push((await engine.cacheStats()).workspaces);
    }

    // The bound is never exceeded, and five distinct scopes saturate it: the
    // fifth admission succeeded only by evicting a least-recently-used entry.
    for (const workspaces of observed) {
      expect(workspaces).toBeLessThanOrEqual(maxOpenScopes);
    }
    expect(Math.max(...observed)).toBe(maxOpenScopes);
    expect(observed[observed.length - 1]).toBe(maxOpenScopes);
  });

  // Issue #6191: BrowserPackageDependencies.AssemblyReferences is a native C#
  // union of a reference list and a failure message. These drive the published
  // production Wasm and the public generated queryPackageDependencies facade so
  // both cases are real engine answers, not hand-written JSON.
  test("answers the assembly-reference union's available case with real rows", async ({
    page,
    context,
  }) => {
    const registry = new GalleryFixtureRegistry(allFixtures);
    await installGalleryRoutes(context, registry);
    await boot(page);
    const engine = driver(page);

    // The selected compile library comes from the package's own surface, so the
    // reference query runs against the identity the production consumer uses.
    const surface = await engine.queryPackage(references);
    const library = surface.assemblies.find(
      candidate => candidate.name === healthyAssemblyName,
    );
    if (library === undefined) {
      throw new Error(`Expected the ${healthyAssemblyName} Library descriptor.`);
    }
    expect(String(surface.compileLibrary.status)).toBe("Selected");

    const dependencies = await engine.queryDependencies(
      references.packageId,
      version,
      fixtureFramework,
      library.id,
    );
    expect(dependencies.package).toBe(references.packageId);
    expect(dependencies.version).toBe(version);
    expect(dependencies.activeFramework).toBe(fixtureFramework);
    expect(dependencies.assembly).toBe(healthyAssemblyFileName);

    // The available case is an object carrying the existing reference rows.
    const list = availableReferences(dependencies.assemblyReferences);
    const rows = list.references;
    expect(rows.map(row => row.name)).toEqual([expectedReferenceName]);
    const [row] = rows;
    if (row === undefined) throw new Error("Expected one AssemblyRef row.");
    expect(row.version).toBe("11.0.0.0");
    expect(row.culture).toBe("neutral");
    expect(row.publicKeyToken).toBeTruthy();

    // The retired parallel field is gone from the wire result, and the outer
    // manifest, framework, and compile-library facts stay independent of it.
    expect(Object.hasOwn(dependencies, "assemblyReferenceError")).toBe(false);
    expect(Object.keys(list)).toEqual(["references"]);
    expect(dependencies.dependencyGroupError).toBeNull();
    expect(String(dependencies.compileLibrary.status)).toBe("Selected");
    const [group] = dependencies.dependencyGroups;
    if (group === undefined) {
      throw new Error("Expected the declared manifest dependency group.");
    }
    expect(group.isActive).toBe(true);
    expect(group.dependencies.map(dependency => dependency.id))
      .toEqual([declaredDependency.id]);
  });

  test("answers a manifest-only package with a reference failure beside healthy dependency groups", async ({
    page,
    context,
  }) => {
    const registry = new GalleryFixtureRegistry(allFixtures);
    await installGalleryRoutes(context, registry);
    await boot(page);
    const engine = driver(page);

    // A manifest-only package declares real dependencies and ships no compile
    // assets, so no assembly-reference list can exist. The assembly id is
    // unused on this path: the engine reports compile-library unavailability
    // rather than attempting a reference query.
    const dependencies = await engine.queryDependencies(
      manifestOnly.packageId,
      version,
      fixtureFramework,
      "",
    );
    expect(dependencies.package).toBe(manifestOnly.packageId);
    expect(dependencies.assembly).toBeNull();
    expect(String(dependencies.compileLibrary.status)).toBe("NoCompileAssets");

    // The failure case is a string carrying the compile-library message, not an
    // empty successful list and not the union's default null.
    const failure = referenceFailure(dependencies.assemblyReferences);
    expect(failure.length).toBeGreaterThan(0);
    if (dependencies.compileLibrary.message !== null) {
      expect(failure).toBe(dependencies.compileLibrary.message);
    }
    expect(Object.hasOwn(dependencies, "assemblyReferenceError")).toBe(false);

    // The manifest evidence stays healthy beside that failure.
    expect(dependencies.dependencyGroupError).toBeNull();
    const [group] = dependencies.dependencyGroups;
    if (group === undefined) {
      throw new Error("Expected the declared manifest dependency group.");
    }
    expect(group.isActive).toBe(true);
    expect(group.dependencies).toEqual([
      { id: declaredDependency.id, versionRange: declaredDependency.versionRange },
    ]);
  });

  test("renders the available reference rows on the production package page", async ({
    page,
    context,
  }) => {
    const browserErrors: string[] = [];
    page.on("console", message => {
      if (message.type() === "error") browserErrors.push(message.text());
    });
    page.on("pageerror", error => {
      browserErrors.push(`${error.name}: ${error.message}`);
    });
    const registry = new GalleryFixtureRegistry(allFixtures);
    await installGalleryRoutes(context, registry);

    // The production site served by this gate, opened on the same fixture
    // coordinate: the page consumes the generated union through its own
    // queryPackageDependencies call and renders the available case.
    await page.goto(
      `/index.html?package=${references.packageId}&version=${version}`
        + `&framework=${fixtureFramework}#pkg`);
    const librarySubject = page.locator('[data-subject-tab][data-scope="library"]');
    await expect(librarySubject.or(page.locator(".load-error")))
      .toBeVisible({ timeout: 180_000 });
    if (await page.locator(".load-error").isVisible()) {
      const details = page.locator("#toggle-error-detail");
      if (await details.isVisible()) await details.click();
      throw new Error(
        `Production page startup failed: ${
          await page.locator(".load-error-detail").textContent() ?? "No details."}`
          + `\nBrowser errors: ${browserErrors.join("\n") || "none"}`);
    }
    await selectFirstExactLibrary(page);
    await chooseInspector(page, "data-library-lens", "references");

    const panel = page.locator("#inspector-panel");
    await expect(panel.getByRole("heading", { name: "References", exact: true }))
      .toBeVisible({ timeout: 60_000 });
    await expect(panel).toContainText(expectedReferenceName);
    // Only the available case renders a reference count; a failure renders
    // "Inspection failed" instead.
    await expect(panel.locator(".api-surface-head"))
      .toContainText("1 direct reference");
    await expect(panel.locator("footer")).toContainText(healthyAssemblyFileName);
    await expect(panel).not.toContainText("Inspection failed");
  });

  test("drills Library, Type, and Member through one Compare inspector with the mode retained", async ({
    page,
    context,
  }) => {
    const browserErrors: string[] = [];
    page.on("console", message => {
      if (message.type() === "error") browserErrors.push(message.text());
    });
    page.on("pageerror", error => {
      browserErrors.push(`${error.name}: ${error.message}`);
    });
    const registry = new GalleryFixtureRegistry(allFixtures);
    await installGalleryRoutes(context, registry);

    await page.goto(
      `/index.html?package=${libraryDiffV2.packageId}`
        + `&version=${libraryDiffV2.version}`
        + `&framework=${fixtureFramework}#pkg`,
    );
    const librarySubject = page.locator('[data-subject-tab][data-scope="library"]');
    await expect(librarySubject.or(page.locator(".load-error")))
      .toBeVisible({ timeout: 180_000 });
    if (await page.locator(".load-error").isVisible()) {
      throw new Error(
        `Production page startup failed: ${
          await page.locator(".load-error").textContent() ?? "No details."}`
          + `\nBrowser errors: ${browserErrors.join("\n") || "none"}`,
      );
    }
    await selectFirstExactLibrary(page);
    await chooseInspector(page, "data-library-lens", "compare");

    // Library Compare: one frame, Diff active, Package-owned target explained.
    const panel = page.locator("#inspector-panel");
    const frame = panel.locator(".compare-surface");
    await expect(frame).toHaveClass(/compare-surface-library/, { timeout: 60_000 });
    await expect(panel.locator('[data-compare-mode="diff"]'))
      .toHaveAttribute("aria-selected", "true");
    await expect(panel.locator('[data-compare-mode="clone"]'))
      .toHaveAttribute("aria-selected", "false");
    await expect(panel.locator(".compare-target-value"))
      .toContainText(`${libraryDiffV1.version} → ${libraryDiffV2.version}`);
    await expect(panel.locator(".compare-head .compare-status"))
      .toContainText("Comparison complete", { timeout: 60_000 });
    await expect(frame.locator(":scope > .compare-status")).toHaveCount(0);
    const headerBox = await frame.locator(".compare-head").boundingBox();
    const targetBox = await frame.locator(".compare-target").boundingBox();
    const resultBox = await frame.locator(".compare-panel").boundingBox();
    expect(headerBox).not.toBeNull();
    expect(targetBox).not.toBeNull();
    expect(resultBox).not.toBeNull();
    expect(Math.abs(targetBox!.y - headerBox!.y - headerBox!.height))
      .toBeLessThanOrEqual(1);
    expect(Math.abs(resultBox!.y - targetBox!.y - targetBox!.height))
      .toBeLessThanOrEqual(1);
    await expect(panel.locator(".library-api-diff-type")).toHaveCount(8);
    await expect(panel).toContainText("LibraryApiDiffFixture.RemovedType");
    await expect(panel).toContainText("LibraryApiDiffFixture.AddedType");
    await expect(panel).toContainText(
      "LibraryApiDiffFixture.MethodConstraintChange",
    );
    await expect(panel).toContainText(
      "LibraryApiDiffFixture.TypeDefinitionOnly",
    );
    await expect(panel.locator(
      '[data-before-type-id="LibraryApiDiffFixture.RemovedType"]',
    )).toHaveAttribute("data-after-type-id", "");
    await expect(panel.locator(
      '[data-after-type-id="LibraryApiDiffFixture.AddedType"]',
    )).toHaveAttribute("data-before-type-id", "");
    // Every current-side Type row is a navigation item; the removed Type
    // remains visible with Before-side evidence and is not activatable.
    await expect(panel.locator(".library-api-diff-type button")).toHaveCount(7);
    await expect(panel.locator(
      '[data-before-type-id="LibraryApiDiffFixture.RemovedType"] [aria-disabled="true"]',
    )).toHaveCount(1);
    await expect(panel.locator(".library-api-diff-type button[data-compare-type-id]"))
      .toHaveCount(7);
    expect(registry.downloadCount(libraryDiffV1)).toBe(1);
    expect(registry.downloadCount(libraryDiffV2)).toBe(1);

    // Library -> Type keeps Compare and Diff active with the same target.
    await panel.locator(
      '[data-compare-type-id="LibraryApiDiffFixture.AddedType"]',
    ).click();
    await expect(frame).toHaveClass(/compare-surface-type/, { timeout: 60_000 });
    await expect(page.locator('[data-inspector-tab][data-lens="compare"]'))
      .toHaveAttribute("aria-selected", "true");
    await expect(panel.locator("#compare-title"))
      .toHaveText("LibraryApiDiffFixture.AddedType");
    await expect(panel.locator('[data-compare-mode="diff"]'))
      .toHaveAttribute("aria-selected", "true");
    await expect(panel.locator(".compare-target-value"))
      .toContainText(`${libraryDiffV1.version} → ${libraryDiffV2.version}`);
    // The added Type carries its implicit constructor plus First and Second.
    await expect(panel.locator(".compare-status"))
      .toContainText("3 changed Members", { timeout: 60_000 });
    await expect(panel.locator(".library-api-diff-member")).toHaveCount(3);
    await expect(panel.locator(".library-api-diff-member button")).toHaveCount(3);
    await expect(panel).not.toContainText("Whole type diff");
    // The whole-Type addition is a Type-level change; its Members carry none.
    await expect(panel.locator('[aria-label="Type-level changes"] .library-api-diff-change'))
      .toHaveCount(1);
    await expect(panel.locator('[aria-label="Type-level changes"]'))
      .toContainText("type added");
    await expect(panel.locator(".library-api-diff-member .library-api-diff-change-chip"))
      .toHaveCount(0);
    expect(registry.downloadCount(libraryDiffV1)).toBe(1);
    expect(registry.downloadCount(libraryDiffV2)).toBe(1);

    // Type -> Member is the detailed-result boundary.
    await panel.locator(".library-api-diff-member button", { hasText: "First" })
      .click();
    await expect(frame).toHaveClass(/compare-surface-member/, { timeout: 60_000 });
    await expect(page.locator('[data-inspector-tab][data-member-section="compare"]'))
      .toHaveAttribute("aria-selected", "true");
    await expect(panel.locator("#compare-title"))
      .toHaveText("LibraryApiDiffFixture.AddedType.First");
    await expect(panel.locator(".compare-status"))
      .toContainText("Member added", { timeout: 60_000 });
    await expect(panel.locator(".library-api-diff-endpoint")).toHaveCount(2);
    await expect(panel.locator(".library-api-diff-absent")).toHaveCount(1);
    await expect(panel.locator("#library-api-diff-changes-title")).toHaveText("What changed");
    await expect(panel).toContainText(
      "No Member-level change is classified: the containing Type was added as a whole.",
    );
    const explore = page.locator("#member-diff-explore");
    await expect(explore).toBeVisible();
    const memberLocation = page.url();
    const memberHistoryLength = await page.evaluate(() => history.length);
    await explore.focus();
    await explore.click();
    const memberDiffExplorer = page.locator("dialog.member-diff-explorer");
    await expect(memberDiffExplorer).toBeVisible();
    await expect(memberDiffExplorer.locator("#member-diff-explorer-title"))
      .toBeFocused();
    expect(page.url()).toBe(memberLocation);
    expect(await page.evaluate(() => history.length))
      .toBe(memberHistoryLength);
    await page.keyboard.press("ArrowDown");
    await expect(memberDiffExplorer).toBeVisible();
    await expect(memberDiffExplorer.locator("#member-diff-explorer-title"))
      .toContainText("First");
    expect(page.url()).toBe(memberLocation);
    await expect(memberDiffExplorer.locator(".member-diff-explorer-pane"))
      .toHaveCount(3);
    await expect(memberDiffExplorer.locator("#member-diff-explorer-title"))
      .toContainText("First");
    await expect(memberDiffExplorer).toContainText(
      "No Member-level change is classified: the containing Type was added as a whole.",
    );
    await expect(memberDiffExplorer.locator(".member-diff-declaration-unavailable"))
      .toBeVisible();
    await expect(memberDiffExplorer.locator(
      ".member-diff-source-endpoint",
    ).first()).toContainText("Not present on this side.");
    await expect(memberDiffExplorer.locator(".member-diff-source-unavailable"))
      .toBeVisible({ timeout: 120_000 });
    await page.setViewportSize({ width: 390, height: 844 });
    const overflow = await page.evaluate(() => ({
      document: document.documentElement.scrollWidth - window.innerWidth,
      explorer: (document.querySelector(".member-diff-explorer")?.scrollWidth
        ?? window.innerWidth) - window.innerWidth,
    }));
    expect(overflow.document).toBeLessThanOrEqual(0);
    expect(overflow.explorer).toBeLessThanOrEqual(0);
    await memberDiffExplorer.locator("[data-member-diff-close]").click();
    await expect(memberDiffExplorer).toHaveCount(0);
    await expect(explore).toBeFocused();
    await page.setViewportSize({ width: 1440, height: 900 });

    // A Member with its own classified change shows the producer's change row.
    await page.locator("#nav-back").click();
    await page.locator("#nav-back").click();
    await expect(frame).toHaveClass(/compare-surface-library/, { timeout: 60_000 });
    await panel.locator(
      '[data-compare-type-id="LibraryApiDiffFixture.HardChangedType"]',
    ).click();
    await expect(frame).toHaveClass(/compare-surface-type/, { timeout: 60_000 });
    await expect(panel.locator(".library-api-diff-member .library-api-diff-change-chip"))
      .toHaveText(["Breaking · virtual removed"]);
    await panel.locator(".library-api-diff-member button", { hasText: "First" })
      .click();
    await expect(frame).toHaveClass(/compare-surface-member/, { timeout: 60_000 });
    await expect(panel.locator(".compare-status"))
      .toContainText("Member changed", { timeout: 60_000 });
    const changeRows = panel.locator('[aria-label="What changed"] .library-api-diff-change');
    await expect(changeRows).toHaveCount(1);
    await expect(changeRows.first()).toContainText("virtual removed");
    await expect(changeRows.first().locator(".library-api-diff-change-chip"))
      .toHaveText("Breaking");
    await expect(changeRows.first().locator(".library-api-diff-change-category"))
      .toHaveText("Signature");
    await expect(explore).toBeVisible();
    await explore.click();
    await expect(memberDiffExplorer).toBeVisible();
    await expect(memberDiffExplorer.locator(
      ".member-diff-explorer-source .member-diff-source-endpoint",
    ))
      .toHaveCount(2);
    await expect(memberDiffExplorer).not.toContainText(
      "Not present on this side.",
    );
    await expect(memberDiffExplorer.locator(".member-diff-source-unavailable"))
      .toBeVisible({ timeout: 120_000 });
    await page.keyboard.press("Escape");
    await expect(memberDiffExplorer).toHaveCount(0);
    await expect(explore).toBeFocused();

    await explore.click();
    await expect(memberDiffExplorer).toBeVisible();
    await page.locator("#nav-back")
      .evaluate((button: HTMLButtonElement) => button.click());
    await expect(memberDiffExplorer).toHaveCount(0);
    await expect(frame).toHaveClass(/compare-surface-type/, { timeout: 60_000 });
    await expect(panel.locator("#compare-title")).toBeFocused();
    await page.locator("#nav-back").click();
    await expect(frame).toHaveClass(/compare-surface-library/, { timeout: 60_000 });

    // A Member the producer placed under two Types: the Before placement is
    // inert here, names its current Type, and activates it; the After
    // placement says where it came from.
    await panel.locator(
      '[data-compare-type-id="LibraryApiDiffFixture.ProjectionExtensions"]',
    ).click();
    await expect(frame).toHaveClass(/compare-surface-type/, { timeout: 60_000 });
    const movedAway = panel.locator(".library-api-diff-member", { hasText: "Transform" });
    await expect(movedAway).toHaveClass(/library-api-diff-member-inert/);
    await expect(movedAway).toContainText(
      "Now declared on LibraryApiDiffFixture.ProjectionReceiver",
    );
    await movedAway.locator(
      '.library-api-diff-counterpart[data-compare-type-id="LibraryApiDiffFixture.ProjectionReceiver"]',
    ).click();
    await expect(frame).toHaveClass(/compare-surface-type/, { timeout: 60_000 });
    await expect(panel.locator("#compare-title"))
      .toHaveText("LibraryApiDiffFixture.ProjectionReceiver");
    const movedHere = panel.locator(".library-api-diff-member", { hasText: "Transform" });
    await expect(movedHere.locator(".library-api-diff-moved"))
      .toContainText("Moved from LibraryApiDiffFixture.ProjectionExtensions");
    await movedHere.locator("button[data-compare-member-fingerprint]").click();
    await expect(frame).toHaveClass(/compare-surface-member/, { timeout: 60_000 });
    await expect(panel.locator(".library-api-diff-correspondence")).toContainText(
      "Moved from LibraryApiDiffFixture.ProjectionExtensions to LibraryApiDiffFixture.ProjectionReceiver",
    );
    await page.locator("#nav-back").click();
    await page.locator("#nav-back").click();
    await page.locator("#nav-back").click();
    await expect(frame).toHaveClass(/compare-surface-library/, { timeout: 60_000 });
    await panel.locator(
      '[data-compare-type-id="LibraryApiDiffFixture.AddedType"]',
    ).click();
    await expect(frame).toHaveClass(/compare-surface-type/, { timeout: 60_000 });
    await panel.locator(".library-api-diff-member button", { hasText: "First" })
      .click();
    await expect(frame).toHaveClass(/compare-surface-member/, { timeout: 60_000 });

    // Back restores the Type inventory with Compare and Diff still active.
    await page.locator("#nav-back").click();
    await expect(frame).toHaveClass(/compare-surface-type/, { timeout: 60_000 });
    await expect(panel.locator(".library-api-diff-member")).toHaveCount(3);
    await expect(panel.locator('[data-compare-mode="diff"]'))
      .toHaveAttribute("aria-selected", "true");

    // Switching to Clone keeps the frame and runs the Type's own query.
    await panel.locator('[data-compare-mode="clone"]').click();
    await expect(panel.locator('[data-compare-mode="clone"]'))
      .toHaveAttribute("aria-selected", "true");
    await expect(panel.locator(".compare-target-label")).toHaveText("Clone scope");
    await expect(panel.locator(".compare-status"))
      .toContainText("Clone search", { timeout: 120_000 });
    await expect(panel.locator(".compare-status"))
      .not.toContainText("Searching", { timeout: 120_000 });
    await expect(panel).not.toContainText("Whole type diff");
    // Mode is retained through Type -> Library navigation and back to Diff.
    await page.locator("#nav-back").click();
    await expect(frame).toHaveClass(/compare-surface-library/, { timeout: 60_000 });
    await expect(panel.locator('[data-compare-mode="clone"]'))
      .toHaveAttribute("aria-selected", "true");
    await panel.locator('[data-compare-mode="diff"]').click();
    await expect(panel.locator(".library-api-diff-type")).toHaveCount(8, {
      timeout: 60_000,
    });

    // Change target returns to Package Overview's Comparison targets area.
    await panel.locator("#compare-change-target").click();
    const target = page.locator("#package-diff-target");
    await expect(target).toBeVisible();
    await expect(target).toBeFocused();
    await target.selectOption("exact:2.0.0");
    await selectFirstExactLibrary(page);
    await chooseInspector(page, "data-library-lens", "compare");
    await expect(panel.locator(".compare-status"))
      .toContainText("No changed Types", { timeout: 60_000 });
    await expect(panel).toContainText("No public API changes");
    await expect(panel.locator(".library-api-diff-type")).toHaveCount(0);
  });
});

test.describe("bounded network-backed two-host demo", () => {
  test.describe.configure({ timeout: 240_000 });

  test("saves and reopens System.Text.Json through retained production activation", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/");
    const search = page.locator("#spotlight-input");
    await expect(search).toBeVisible({ timeout: 120_000 });
    await search.fill("System.Text.Json@9.0.4");
    const exactPackage = page.locator(
      '[data-sl-pkg-load="System.Text.Json"][data-sl-pkg-version="9.0.4"]',
    );
    await expect(exactPackage)
      .toContainText("9.0.4 · exact coordinate · listed or unlisted");
    await exactPackage.dispatchEvent("click");
    await expect(page.locator(".inspected-target"))
      .toContainText("System.Text.Json", { timeout: 180_000 });
    await expect(page.getByTitle("System.Text.Json@9.0.4", { exact: true }))
      .toBeVisible({ timeout: 180_000 });
    await expect(page.locator("#app"))
      .not.toHaveAttribute("aria-busy", "true", { timeout: 180_000 });

    await page.locator("[data-product-navigation-button]")
      .dispatchEvent("click");
    await page.locator(
      '[data-product-navigation-menu]:not([hidden])'
        + ':has([data-product-destination="query"]:not([aria-disabled="true"])) '
        + '[data-product-destination="workspace"]',
    )
      .dispatchEvent("click");
    await page.getByRole(
      "button",
      { name: "Save Workspace", exact: true },
    ).dispatchEvent("click");
    await page.getByLabel("Workspace name", { exact: true })
      .fill("System.Text.Json 9.0.4");
    await page.getByRole("button", { name: "Save", exact: true }).click();
    const open = page.getByRole("button", {
      name: "Open saved Workspace System.Text.Json 9.0.4",
      exact: true,
    });
    await expect(open).toBeVisible({ timeout: 180_000 });

    const persisted = await page.evaluate((): {
      version: number;
      entries: Array<{ name: string; packet: string; kind: string }>;
    } => {
      const raw = localStorage.getItem("inspect-saved-workspaces");
      if (raw === null) {
        throw new Error(
          `Saved Workspace storage is empty (${Object.keys(localStorage).join(", ")}).`,
        );
      }
      const parsed: unknown = JSON.parse(raw);
      if (typeof parsed !== "object" || parsed === null
        || !("version" in parsed) || typeof parsed.version !== "number"
        || !("entries" in parsed) || !Array.isArray(parsed.entries)) {
        throw new Error("Saved Workspace storage has an invalid envelope.");
      }
      const entries = parsed.entries.map((entry: unknown) => {
        if (typeof entry !== "object" || entry === null
          || !("name" in entry) || typeof entry.name !== "string"
          || !("packet" in entry) || typeof entry.packet !== "string"
          || !("kind" in entry) || typeof entry.kind !== "string") {
          throw new Error("Saved Workspace storage has an invalid entry.");
        }
        return {
          name: entry.name,
          packet: entry.packet,
          kind: entry.kind,
        };
      });
      return { version: parsed.version, entries };
    });
    expect(persisted.version).toBe(2);
    expect(persisted.entries).toHaveLength(1);
    expect(persisted.entries[0]).toMatchObject({
      name: "System.Text.Json 9.0.4",
      kind: "complete",
    });

    const compatibilityUrl = page.url();
    await open.focus();
    await expect(open).toBeFocused();
    await open.click();
    await expect(page.locator("[data-navigation-order]"))
      .toContainText("System.Text.Json", { timeout: 180_000 });
    await expect(page.locator("[data-navigation-order]"))
      .toContainText("9.0.4");
    await expect(page.locator(".workspace-row")
      .filter({ hasText: "System.Text.Json 9.0.4" })
      .locator("small"))
      .toHaveText("Active", { timeout: 180_000 });
    await expect(page.getByRole("button", {
      name: "Delete System.Text.Json 9.0.4",
      exact: true,
    })).toBeEnabled({ timeout: 180_000 });
    await expect(page.locator('[data-workspace-select]').first())
      .toBeFocused({ timeout: 180_000 });
    await expect(page.locator(".workspace-list .workspace-row"))
      .toHaveCount(2);
    await page.getByRole(
      "button",
      { name: "Save Workspace", exact: true },
    ).dispatchEvent("click");
    await page.getByLabel("Workspace name", { exact: true })
      .fill("Re-saved System.Text.Json 9.0.4");
    await page.getByRole("button", { name: "Save", exact: true }).click();
    const resavedPacket = await page.evaluate<string | null>(() => {
      const raw = localStorage.getItem("inspect-saved-workspaces");
      if (raw === null) return null;
      const value: unknown = JSON.parse(raw);
      if (typeof value !== "object" || value === null
        || !("entries" in value) || !Array.isArray(value.entries)) {
        return null;
      }
      const entries: unknown[] = Array.from(value.entries);
      const entry = entries.find(candidate =>
        typeof candidate === "object"
        && candidate !== null
        && "name" in candidate
        && candidate.name === "Re-saved System.Text.Json 9.0.4");
      return entry
        && typeof entry === "object"
        && "packet" in entry
        && typeof entry.packet === "string"
        ? entry.packet
        : null;
    });
    expect(resavedPacket).toBe(persisted.entries[0]!.packet);
    await expect.poll(
      () => new URL(page.url()).searchParams.get("w"),
      { timeout: 180_000 },
    )
      .toBe(persisted.entries[0]!.packet);
    const managedUrl = page.url();
    expect(managedUrl).not.toBe(compatibilityUrl);
    await expect(page.locator("[data-workspace-add-package]")).toHaveCount(0);

    await page.locator("[data-product-navigation-button]").click();
    await page.locator('[data-product-destination="query"]').click();
    await expect(page).toHaveURL(/\/query$/);
    await expect(page.locator("#package-query-heading"))
      .toHaveText("Package query");
    await page.evaluate(() => history.back());
    await expect.poll(() => page.url()).toBe(managedUrl);
    await expect(page.locator("[data-navigation-order]"))
      .toContainText("System.Text.Json");
    await expect(page.locator("#package-query-heading")).toHaveCount(0);
    await expect(page.locator(".workspace-list .workspace-row"))
      .toHaveCount(2);
    await page.evaluate(() => history.forward());
    await expect(page).toHaveURL(/\/query$/);
    await expect(page.locator("#package-query-heading"))
      .toHaveText("Package query");
    await page.evaluate(() => history.back());
    await expect.poll(() => page.url()).toBe(managedUrl);
    await expect(page.locator(".workspace-list .workspace-row"))
      .toHaveCount(2);

    await page.locator("[data-product-navigation-button]").click();
    await page.locator('[data-product-destination="activity"]').click();
    await expect(page).toHaveURL(/\/activity$/);
    await expect(page.locator("#package-changes-heading"))
      .toHaveText("Package Activity");
    const productNavigationButton =
      page.locator("[data-product-navigation-button]");
    await productNavigationButton.click();
    await page.locator('[data-product-destination="workspace"]').focus();
    await expect(page.locator(".product-navigation-menu")).toBeVisible();
    await page.evaluate(() => history.back());
    await expect.poll(() => page.url()).toBe(managedUrl);
    await expect(page.locator(".product-navigation-menu")).toBeHidden();
    await expect(productNavigationButton).toBeFocused();
    await expect(page.locator("[data-navigation-order]"))
      .toContainText("System.Text.Json");
    await expect(page.locator("#package-changes-heading")).toHaveCount(0);
    await expect(page.locator(".workspace-list .workspace-row"))
      .toHaveCount(2);
    await page.evaluate(() => history.forward());
    await expect(page).toHaveURL(/\/activity$/);
    await expect(page.locator("#package-changes-heading"))
      .toHaveText("Package Activity");
    await page.evaluate(() => history.back());
    await expect.poll(() => page.url()).toBe(managedUrl);
    await expect(page.locator(".workspace-list .workspace-row"))
      .toHaveCount(2);

    await page.locator("[data-product-navigation-button]").click();
    await page.locator('[data-product-destination="home"]').click();
    await expect(page).toHaveURL(/\/$/);
    await expect(page.locator("#spotlight-input")).toBeVisible();
    await page.evaluate(() => history.back());
    await expect.poll(() => page.url()).toBe(managedUrl);
    await expect(page.locator("[data-navigation-order]"))
      .toContainText("System.Text.Json");
    await expect(page.locator("#spotlight-input")).toHaveCount(0);
    await page.evaluate(() => history.forward());
    await expect(page).toHaveURL(/\/$/);
    await expect(page.locator("#spotlight-input")).toBeVisible();
    await page.evaluate(() => history.back());
    await expect.poll(() => page.url()).toBe(managedUrl);

    await page.getByRole("link", { name: "Credits", exact: true }).click();
    await expect(page).toHaveURL(/\/credits$/);
    await expect(page.getByRole("heading", { name: "Credits", level: 1 }))
      .toBeVisible();
    await page.evaluate(() => history.back());
    await expect.poll(() => page.url()).toBe(managedUrl);
    await expect(page.locator("[data-navigation-order]"))
      .toContainText("System.Text.Json");
    await expect(page.getByRole("heading", { name: "Credits", level: 1 }))
      .toHaveCount(0);
    await page.evaluate(() => history.forward());
    await expect(page).toHaveURL(/\/credits$/);
    await expect(page.getByRole("heading", { name: "Credits", level: 1 }))
      .toBeVisible();
    await page.evaluate(() => history.back());
    await expect.poll(() => page.url()).toBe(managedUrl);

    await page.evaluate(() => history.back());
    await expect.poll(() => page.url()).toBe(compatibilityUrl);
    await expect(page.locator(".workspace-list .workspace-row"))
      .toHaveCount(1, { timeout: 180_000 });
    await expect(page.locator(".workspace-occurrence-row"))
      .toContainText("System.Text.Json");

    await page.evaluate(() => history.forward());
    await expect(page.locator(".toast"))
      .toContainText("That retained Workspace is no longer available.");
    await expect.poll(() => page.url()).toBe(compatibilityUrl);
  });

  test("opens an unlisted package through visible exact-coordinate search", async ({
    page,
  }) => {
    await page.goto("/");
    const search = page.locator("#spotlight-input");
    await expect(search).toBeVisible({ timeout: 120_000 });
    await search.fill("WrongTurn@0.1.14");
    const exact = page.locator('[data-sl-pkg-load="WrongTurn"]');
    await expect(exact)
      .toContainText("0.1.14 · exact coordinate · listed or unlisted");
    await exact.click();

    await expect(page.locator(".inspected-target"))
      .toContainText("WrongTurn", { timeout: 180_000 });
    await expect(page.getByTitle("WrongTurn@0.1.14", { exact: true }))
      .toBeVisible();
    await expect(page.getByTitle("net11.0", { exact: true })).toBeVisible();
  });

  test("opens ordinary and pathological packages over the real Gallery CDN", async ({
    page,
  }) => {
    await boot(page);
    const engine = driver(page);

    // The CLI pilot's default selection resolves net10.0 for this coordinate;
    // the browser host selects the same TFM against the immutable real package.
    const surface = await engine.queryCoordinate(
      "Microsoft.Extensions.Http",
      "10.0.0",
      "net10.0",
    );
    expect(surface.package).toBe("Microsoft.Extensions.Http");
    expect(surface.version).toBe("10.0.0");
    expect(surface.activeFramework).toBe("net10.0");
    expect(surface.assemblies.length).toBeGreaterThan(0);
    expect(surface.types.length).toBeGreaterThan(0);

    // System.Text.Json is an ordinary supported package whose complete surface
    // crossed both former ordinary-transport bounds. The published Worker must
    // carry that generated result without weakening its finite envelope.
    const largeSurface = await engine.queryCoordinate(
      "System.Text.Json",
      "10.0.0",
      "net10.0",
    );
    expect(largeSurface.package).toBe("System.Text.Json");
    expect(largeSurface.version).toBe("10.0.0");
    expect(largeSurface.activeFramework).toBe("net10.0");
    expect(largeSurface.assemblies.length).toBeGreaterThan(0);
    expect(largeSurface.types.length).toBeGreaterThan(0);

    // Aspire.Hosting is the current ordinary-package pathological case. Its
    // complete net8.0 surface crosses both former transport bounds.
    const aspireSurface = await engine.queryCoordinate(
      "Aspire.Hosting",
      "13.5.4",
      "net8.0",
    );
    expect(aspireSurface.package).toBe("Aspire.Hosting");
    expect(aspireSurface.version).toBe("13.5.4");
    expect(aspireSurface.activeFramework).toBe("net8.0");
    expect(aspireSurface.assemblies.length).toBeGreaterThan(0);
    expect(aspireSurface.types.length).toBeGreaterThan(0);

    // The awaitable Workspace occurrence for the same real coordinate activates
    // and yields the same package surface.
    const view = await engine.queryOccurrences([
      { package: "Microsoft.Extensions.Http", version: "10.0.0", framework: "net10.0" },
    ]);
    expect(view.superseded).toBe(false);
    expect(view.occurrences.length).toBe(1);
    const [occurrence] = view.occurrences;
    if (occurrence === undefined) {
      throw new Error("Expected one Microsoft.Extensions.Http occurrence.");
    }
    const activation = await engine.activate(occurrence.action);
    expect(activation.activated).toBe(true);
    expect(activation.superseded).toBe(false);
    expect(activation.package?.package).toBe("Microsoft.Extensions.Http");

    // HTTP Client integration evidence matches the CLI default demo
    // (IHttpClientFactory / AddHttpClient) for the same real coordinate.
    const library = surface.assemblies.find(
      candidate => candidate.name === "Microsoft.Extensions.Http",
    );
    if (library === undefined) {
      throw new Error("Expected the Microsoft.Extensions.Http Library descriptor.");
    }
    const integrations = await engine.queryIntegrations(
      "Microsoft.Extensions.Http",
      "10.0.0",
      "net10.0",
      library.id,
    );
    expect(integrations.isComplete).toBe(true);
    expect(String(integrations.compileLibrary.status)).toBe("Selected");
    const httpClient = integrations.categories.find(
      category => category.integration === "HTTP Client",
    );
    expect(httpClient).toBeDefined();
    expect((httpClient?.signals.length ?? 0)).toBeGreaterThan(0);
    const signalNames = (httpClient?.signals ?? []).map(signal => signal.name);
    const flattened = signalNames.join(" ");
    expect(flattened).toContain("IHttpClientFactory");
    expect(flattened).toContain("AddHttpClient");
  });

  test("opens Avalonia over the ordinary Worker boundary", async ({ page }) => {
    await boot(page);
    // This real result crosses both former ordinary Worker limits.
    const surface = await driver(page).queryCoordinate(
      "Avalonia",
      "12.1.3",
      "net8.0",
    );
    expect(surface.package).toBe("Avalonia");
    expect(surface.version).toBe("12.1.3");
    expect(surface.activeFramework).toBe("net8.0");
    expect(surface.assemblies.length).toBeGreaterThan(0);
    expect(surface.types.length).toBeGreaterThan(0);
  });
});
