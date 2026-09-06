import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { Buffer } from "node:buffer";
import { expect, test, type BrowserContext, type Page } from "@playwright/test";
import type {
  BrowserAssemblyReferenceList as AssemblyReferenceList,
  BrowserAssemblyReferenceResult as AssemblyReferenceResult,
  BrowserPackageCacheStats as CacheStats,
  BrowserPackageDependencies as PackageDependencies,
  BrowserPackageSurface as PackageSurface,
  BrowserWorkspacePackageOccurrence as OccurrenceRow,
  BrowserWorkspacePackageOccurrenceActivation as OccurrenceActivation,
  BrowserWorkspacePackageOccurrenceView as OccurrenceView,
} from "../src/facades/inspect-web-package.js";
import type {
  BrowserPackageIntegrations as PackageIntegrations,
} from "../src/facades/inspect-web-analysis.js";
import {
  fixtureFramework,
  galleryDownloadPath,
  healthyNupkg,
  malformedAlongsideHealthyNupkg,
  malformedAssemblyBytes,
  manifestBackedNupkg,
  manifestOnlyNupkg,
  type ManifestDependency,
} from "./package-adoption-nupkg.ts";

// This gate drives the actually published production InspectWeb.Engine Wasm
// artifact and its public generated facades in a real Firefox page. It proves
// the artifact-backed package scope adoption contract (issue #5576): ordinary
// singleton opening via queryPackage, repeated/joined requests, the four-scope
// bound with successful eviction, awaitable Workspace occurrence activation, a
// stale occurrence action after clear and after replacement, and a
// valid-reference / malformed-implementation package producing a visible
// selected rejection beside healthy evidence. It also proves the package
// facade's assembly-reference result union (issue #6191): an available list of
// real AssemblyRef rows, a manifest-only package's compile-library failure
// message beside healthy manifest dependency groups, and the production page
// rendering the available case. Package acquisition leaves the browser as
// ordinary NuGet Gallery CDN fetches, which this spec intercepts to serve
// deterministic local fixtures; a separate test exercises the immutable real
// Microsoft.Extensions.Http@10.0.0/net10.0 coordinate over the network.

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
}

const version = "1.0.0";
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

const allFixtures: readonly FixtureCoordinate[] = [
  healthy,
  malformed,
  occurrenceOne,
  occurrenceTwo,
  joinCoordinate,
  references,
  manifestOnly,
  ...scopeCoordinates,
];

class GalleryFixtureRegistry {
  readonly downloads = new Map<string, number>();
  private readonly archives = new Map<string, Buffer>();
  private readonly versions = new Map<string, string>();

  constructor(fixtures: readonly FixtureCoordinate[]) {
    for (const fixture of fixtures) {
      this.archives.set(
        galleryDownloadPath(fixture.packageId, fixture.version),
        fixture.archive,
      );
      this.versions.set(
        `/v3-flatcontainer/${fixture.packageId.toLowerCase()}/index.json`,
        fixture.version,
      );
    }
  }

  archiveFor(pathname: string): Buffer | undefined {
    return this.archives.get(pathname);
  }

  versionIndexFor(pathname: string): string | undefined {
    return this.versions.get(pathname);
  }

  recordDownload(pathname: string): void {
    this.downloads.set(pathname, (this.downloads.get(pathname) ?? 0) + 1);
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
): Promise<void> {
  await context.route("https://globalcdn.nuget.org/**", async route => {
    const request = route.request();
    if (request.method() === "OPTIONS") {
      await route.fulfill({ status: 204, headers: corsHeaders });
      return;
    }
    const pathname = new URL(request.url()).pathname;
    const archive = registry.archiveFor(pathname);
    if (archive) {
      registry.recordDownload(pathname);
      await route.fulfill({
        status: 200,
        headers: { ...corsHeaders, "content-type": "application/octet-stream" },
        body: archive,
      });
      return;
    }
    const indexVersion = registry.versionIndexFor(pathname);
    if (indexVersion) {
      await route.fulfill({
        status: 200,
        headers: { ...corsHeaders, "content-type": "application/json" },
        body: JSON.stringify({ versions: [indexVersion] }),
      });
      return;
    }
    await route.fulfill({ status: 404, headers: corsHeaders });
  });
}

declare global {
  interface Window {
    __adoption?: {
      queryPackage(
        packageId: string,
        version: string,
        framework: string,
      ): Promise<PackageSurface>;
      cacheStats(): CacheStats;
      queryOccurrences(workspaceJson: string): Promise<OccurrenceView>;
      activate(action: string): Promise<OccurrenceActivation>;
      clearOccurrences(): void;
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
    };
  }
}

type HostFacadeModule = Pick<
  typeof import("../src/facades/inspect-web-host.js"),
  "createRuntime" | "initializeRuntime" | "configureHost"
>;

type PackageFacadeModule = Pick<
  typeof import("../src/facades/inspect-web-package.js"),
  "initializeRuntime" | "queryPackage" | "packageCacheStats"
  | "queryWorkspacePackageOccurrences" | "activateWorkspacePackageOccurrence"
  | "clearWorkspacePackageOccurrences" | "queryPackageDependencies"
>;

type AnalysisFacadeModule = Pick<
  typeof import("../src/facades/inspect-web-analysis.js"),
  "initializeRuntime" | "queryPackageIntegrations"
>;

async function boot(page: Page): Promise<void> {
  await page.goto("/package-adoption-gate.html");
  await page.evaluate(async () => {
    const hostImport: unknown = await import("/inspect-web-host.js");
    const pkgImport: unknown = await import("/inspect-web-package.js");
    const analysisImport: unknown = await import("/inspect-web-analysis.js");
    function isHostFacade(value: unknown): value is HostFacadeModule {
      return typeof value === "object" && value !== null
        && "createRuntime" in value && typeof value.createRuntime === "function"
        && "initializeRuntime" in value && typeof value.initializeRuntime === "function"
        && "configureHost" in value && typeof value.configureHost === "function";
    }
    function isPackageFacade(value: unknown): value is PackageFacadeModule {
      return typeof value === "object" && value !== null
        && "initializeRuntime" in value && typeof value.initializeRuntime === "function"
        && "queryPackage" in value && typeof value.queryPackage === "function"
        && "packageCacheStats" in value && typeof value.packageCacheStats === "function"
        && "queryWorkspacePackageOccurrences" in value
          && typeof value.queryWorkspacePackageOccurrences === "function"
        && "activateWorkspacePackageOccurrence" in value
          && typeof value.activateWorkspacePackageOccurrence === "function"
        && "clearWorkspacePackageOccurrences" in value
          && typeof value.clearWorkspacePackageOccurrences === "function"
        && "queryPackageDependencies" in value
          && typeof value.queryPackageDependencies === "function";
    }
    function isAnalysisFacade(value: unknown): value is AnalysisFacadeModule {
      return typeof value === "object" && value !== null
        && "initializeRuntime" in value && typeof value.initializeRuntime === "function"
        && "queryPackageIntegrations" in value
          && typeof value.queryPackageIntegrations === "function";
    }
    if (!isHostFacade(hostImport)) {
      throw new Error("Published host facade exports are missing.");
    }
    if (!isPackageFacade(pkgImport)) {
      throw new Error("Published package facade exports are missing.");
    }
    if (!isAnalysisFacade(analysisImport)) {
      throw new Error("Published analysis facade exports are missing.");
    }
    const host = hostImport;
    const pkg = pkgImport;
    const analysis = analysisImport;
    const runtime = host.createRuntime();
    await host.initializeRuntime(runtime);
    await pkg.initializeRuntime(runtime);
    await analysis.initializeRuntime(runtime);
    host.configureHost(location.origin);
    window.__adoption = {
      queryPackage: (packageId, pkgVersion, framework) =>
        pkg.queryPackage(packageId, pkgVersion, framework),
      cacheStats: () => pkg.packageCacheStats(),
      queryOccurrences: workspaceJson =>
        pkg.queryWorkspacePackageOccurrences(workspaceJson),
      activate: action => pkg.activateWorkspacePackageOccurrence(action),
      clearOccurrences: () => {
        pkg.clearWorkspacePackageOccurrences();
      },
      queryDependencies: (packageId, pkgVersion, framework, assemblyId) =>
        pkg.queryPackageDependencies(packageId, pkgVersion, framework, assemblyId),
      queryIntegrations: (packageId, pkgVersion, framework, libraryId) =>
        analysis.queryPackageIntegrations(packageId, pkgVersion, framework, libraryId),
    };
  });
}

function driver(page: Page): {
  queryPackage(fixture: FixtureCoordinate, framework?: string): Promise<PackageSurface>;
  queryCoordinate(packageId: string, version: string, framework: string): Promise<PackageSurface>;
  cacheStats(): Promise<CacheStats>;
  queryOccurrences(workspace: readonly { package: string; version: string; framework: string }[]): Promise<OccurrenceView>;
  activate(action: string): Promise<OccurrenceActivation>;
  clearOccurrences(): Promise<void>;
  queryDependencies(packageId: string, version: string, framework: string, assemblyId: string): Promise<PackageDependencies>;
  queryIntegrations(packageId: string, version: string, framework: string, libraryId: string): Promise<PackageIntegrations>;
} {
  return {
    queryPackage: (fixture, framework = fixtureFramework) =>
      page.evaluate(
        ({ packageId, version: ver, framework: tfm }) =>
          window.__adoption!.queryPackage(packageId, ver, tfm),
        { packageId: fixture.packageId, version: fixture.version, framework },
      ),
    queryCoordinate: (packageId, pkgVersion, framework) =>
      page.evaluate(
        ({ packageId: id, version: ver, framework: tfm }) =>
          window.__adoption!.queryPackage(id, ver, tfm),
        { packageId, version: pkgVersion, framework },
      ),
    cacheStats: () => page.evaluate(() => window.__adoption!.cacheStats()),
    queryOccurrences: workspace =>
      page.evaluate(
        json => window.__adoption!.queryOccurrences(json),
        JSON.stringify(workspace),
      ),
    activate: action =>
      page.evaluate(token => window.__adoption!.activate(token), action),
    clearOccurrences: () =>
      page.evaluate(() => {
        window.__adoption!.clearOccurrences();
      }),
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

test.describe("Gallery Package Query website over real Wasm", () => {
  test("browses by source type and searches without implicit enrichment", async ({
    page,
    context,
  }) => {
    const requests: URL[] = [];
    const enrichment: string[] = [];
    context.on("request", request => {
      if (new URL(request.url()).hostname === "globalcdn.nuget.org") {
        enrichment.push(request.url());
      }
    });
    const row = (id: string, downloads?: number) => ({
      PackageRegistration: {
        Id: id,
        ...(downloads === undefined ? {} : { DownloadCount: downloads }),
        Verified: true,
        Owners: ["Contoso"],
      },
      Version: "1.0.0",
      NormalizedVersion: "1.0.0",
      Listed: true,
      Description: "Gallery website fixture.",
      DownloadCount: 3,
    });
    await context.route("https://azuresearch-usnc.nuget.org/**", async route => {
      const url = new URL(route.request().url());
      expect(url.pathname).toBe("/search/query");
      requests.push(url);
      const type = url.searchParams.get("packageType")?.toLowerCase();
      const data = url.searchParams.get("q")
        ? [row("Contoso.Parser")]
        : type === "dotnettool"
          ? [
            row("Contoso.ToolA", 1_000),
            row("Contoso.ToolB", 500),
            ...Array.from({ length: 18 }, (_, index) =>
              row(`Contoso.Tool${index + 3}`, 100 - index)),
          ]
          : type === "template"
            ? [row("Contoso.Template", 400)]
            : [row("Contoso.Package", 2_000)];
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        headers: corsHeaders,
        body: JSON.stringify({ totalHits: data.length, data }),
      });
    });

    await page.goto("/query");
    await expect(page.locator("#package-query-type")).toBeVisible({ timeout: 120_000 });
    await expect(page.locator("#package-query-prefix")).toHaveValue("");
    expect(requests).toHaveLength(0);

    await page.locator("#package-query-type").selectOption({ label: ".NET tools" });
    await expect(page.locator(".query-row")).toHaveCount(20);
    await expect(page.locator(".query-row h2")).toContainText(["Contoso.ToolA", "Contoso.ToolB"]);
    await expect(page.locator(".query-row").first()).toContainText("1,000");
    await expect(page.locator(".query-row-description").first()).toHaveText("Gallery website fixture.");
    await expect(page.locator(".query-footer")).toContainText("200");
    await expect(page.locator(".query-footer")).not.toContainText("all matches");
    expect(requests.at(-1)?.searchParams.get("take")).toBe("200");
    expect(requests.at(-1)?.searchParams.get("sortBy")).toBe("totalDownloads-desc");
    const lastFacet = page.locator("[data-query-facet]").last();
    await lastFacet.focus();
    await expect(lastFacet).toBeInViewport();

    await page.locator("#package-query-type").selectOption({ label: "Templates" });
    await expect(page.locator(".query-row h2")).toHaveText(["Contoso.Template"]);
    await page.locator("#package-query-order").selectOption({ label: "Relevance" });
    await expect.poll(() => requests.at(-1)?.searchParams.get("sortBy")).toBe("relevance");

    await page.locator("#package-query-type").selectOption("");
    await expect(page.locator(".query-row h2")).toHaveText(["Contoso.Package"]);
    await page.locator("#package-query-order").selectOption("");
    await expect.poll(() => requests.at(-1)?.searchParams.get("sortBy")).toBe("totalDownloads-desc");
    await page.locator("#package-query-prefix").fill("json parser");
    await page.locator("#package-query-run").click();
    await expect(page.locator(".query-row h2")).toHaveText(["Contoso.Parser"]);
    await expect(page.locator(".query-row")).toContainText("unavailable");
    expect(requests.at(-1)?.searchParams.get("q")).toBe("json parser");
    expect(requests.at(-1)?.searchParams.get("sortBy")).toBe("relevance");
    expect(requests.every(request => request.searchParams.get("take") === "200")).toBe(true);
    expect(enrichment).toEqual([]);
  });

  test("live Gallery tool browse uses the production page and CORS path", async ({ page }) => {
    test.skip(process.env.INSPECT_WEB_GALLERY_LIVE !== "1", "Opt-in live provider observation.");
    await page.goto("/query");
    await expect(page.locator("#package-query-type")).toBeVisible({ timeout: 120_000 });
    await page.locator("#package-query-type").selectOption({ label: ".NET tools" });
    await expect(page.locator(".query-row").first()).toBeVisible({ timeout: 60_000 });
    await expect(page.locator(".query-row").first()).toContainText("Open in workspace");
    await expect(page.locator(".query-failures")).toHaveCount(0);
    await page.screenshot({ path: test.info().outputPath("gallery-tools.png"), fullPage: true });
    await page.locator("[data-query-cancel]").first().click();
  });
});

test.describe("artifact-backed package scope adoption over real Wasm", () => {
  test.describe.configure({ timeout: 240_000 });

  test("drives the production opening, join, occurrence, and rejection contracts", async ({
    page,
    context,
  }) => {
    const registry = new GalleryFixtureRegistry(allFixtures);
    await installGalleryRoutes(context, registry);
    await boot(page);
    const engine = driver(page);

    // Ordinary singleton opening yields healthy evidence.
    const opened = await engine.queryPackage(healthy);
    expect(opened.package).toBe(healthy.packageId);
    expect(opened.version).toBe(version);
    expect(opened.activeFramework).toBe(fixtureFramework);
    expect(opened.assemblies.length).toBeGreaterThan(0);
    expect(opened.types.length).toBeGreaterThan(0);
    expect(opened.types.some(type => type.name === healthyTypeName)).toBe(true);
    expect(opened.inspectionErrors.length).toBe(0);

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
    const registry = new GalleryFixtureRegistry(allFixtures);
    await installGalleryRoutes(context, registry);

    // The production site served by this gate, opened on the same fixture
    // coordinate: the page consumes the generated union through its own
    // queryPackageDependencies call and renders the available case.
    await page.goto(
      `/index.html?package=${references.packageId}&version=${version}`
        + `&framework=${fixtureFramework}#pkg`);
    const libraryRow = page.locator(".library-list [data-lib-scope]").first();
    await expect(libraryRow).toBeVisible({ timeout: 180_000 });
    await libraryRow.click();
    await page.locator('[data-library-lens="references"]').click();

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
});

test.describe("bounded network-backed two-host demo", () => {
  test.describe.configure({ timeout: 240_000 });

  test("opens Microsoft.Extensions.Http@10.0.0/net10.0 over the real Gallery CDN", async ({
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
});
