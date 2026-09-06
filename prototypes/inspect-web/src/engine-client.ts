type HostFacade = typeof import("./facades/inspect-web-host.d.ts");
type PackageFacade = typeof import("./facades/inspect-web-package.d.ts");
type CatalogFacade = typeof import("./facades/inspect-web-catalog.d.ts");

interface ClientFacades {
  readonly host: Pick<HostFacade, "buildIdentity">;
  readonly package: Pick<
    PackageFacade,
    "listPackageQueryFacets" | "listGalleryDiscoveryCatalog"
  >;
  readonly catalog: Pick<
    CatalogFacade,
    "listVocabulary" | "listHomeDemos" | "resolveHomeDemo"
  >;
}

// These Promise-valued bindings still dispatch on the current thread.
// Runtime readiness and each read's error policy stay with the caller.
export function createMainThreadEngineClient(facades: ClientFacades) {
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
      async resolveHomeDemo(...args: Parameters<CatalogFacade["resolveHomeDemo"]>) {
        return facades.catalog.resolveHomeDemo(...args);
      },
    },
  };
}

export type EngineClient = ReturnType<typeof createMainThreadEngineClient>;
