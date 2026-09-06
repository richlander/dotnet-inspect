type HostFacade = typeof import("./facades/inspect-web-host.d.ts");
type PackageFacade = typeof import("./facades/inspect-web-package.d.ts");
type CatalogFacade = typeof import("./facades/inspect-web-catalog.d.ts");

interface StartupFacades {
  readonly host: Pick<HostFacade, "buildIdentity">;
  readonly package: Pick<
    PackageFacade,
    "listPackageQueryFacets" | "listGalleryDiscoveryCatalog"
  >;
  readonly catalog: Pick<CatalogFacade, "listVocabulary" | "listHomeDemos">;
}

// These Promise-valued bindings still dispatch on the current thread.
// Runtime readiness and each read's error policy stay with the caller.
export function createMainThreadStartupClient(facades: StartupFacades) {
  return {
    host: {
      async buildIdentity() {
        return facades.host.buildIdentity();
      },
    },
    package: {
      async listPackageQueryFacets() {
        return facades.package.listPackageQueryFacets();
      },
      async listGalleryDiscoveryCatalog() {
        return facades.package.listGalleryDiscoveryCatalog();
      },
    },
    catalog: {
      async listVocabulary() {
        return facades.catalog.listVocabulary();
      },
      async listHomeDemos() {
        return facades.catalog.listHomeDemos();
      },
    },
  };
}

export type EngineStartupClient = ReturnType<typeof createMainThreadStartupClient>;
