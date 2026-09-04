import type {
  BrowserTypeMetadata,
} from "./facades/inspect-web-metadata.d.ts";
import type { MemberFocusSnapshot } from "./member-focus.ts";
import type {
  ExplorerState,
  ExplorerTableData,
  HeapListingData,
} from "./metadata-viewer.ts";

export interface AppExplorerState extends ExplorerState {
  isPlatform: boolean;
  pack: string | null;
  packageId: string;
  version: string;
  framework: string;
  pendingScroll: boolean;
}

export interface TypeMetadataLoadRequest {
  signature: string;
  packageId: string;
  version: string;
  framework: string;
  assembly: string;
  type: string;
  isVisible(): boolean;
}

export interface MetadataInspectionState {
  typeMetadata: BrowserTypeMetadata | null;
  typeMetadataLoading: boolean;
  typeMetadataError: string;
  typeMetadataKey: string;
  typeMetadataGeneration: number;
  explorer: AppExplorerState | null;
}

export interface MetadataInspectionDependencies {
  state: MetadataInspectionState;
  queryTypeMetadata(
    request: TypeMetadataLoadRequest,
  ): Promise<BrowserTypeMetadata>;
  queryPackageTable(
    explorer: AppExplorerState,
    index: number,
    startRowId: number,
    maxRows: number,
  ): Promise<ExplorerTableData>;
  queryPlatformTable(
    explorer: AppExplorerState,
    index: number,
    startRowId: number,
    maxRows: number,
  ): Promise<ExplorerTableData>;
  queryPackageHeap(
    explorer: AppExplorerState,
    heapName: string,
  ): Promise<HeapListingData>;
  queryPlatformHeap(
    explorer: AppExplorerState,
    heapName: string,
  ): Promise<HeapListingData>;
  describeError(error: unknown): string;
  render(): void;
  renderPreservingMemberFocus(
    fallback?: MemberFocusSnapshot | null,
  ): MemberFocusSnapshot;
  scrollExplorerToFocus(): void;
}

export interface MetadataInspectionCoordinator {
  loadTypeMetadata(request: TypeMetadataLoadRequest): Promise<void>;
  loadExplorerWindow(
    index: number,
    startRowId: number,
    maxRows: number,
  ): Promise<void>;
  loadExplorerHeap(heapName: string): Promise<void>;
}

export function createMetadataInspectionCoordinator(
  dependencies: MetadataInspectionDependencies,
): MetadataInspectionCoordinator {
  const { state } = dependencies;
  let explorerWindowRequestSequence = 0;
  const explorerWindowRequests =
    new WeakMap<AppExplorerState, Map<number, number>>();

  return {
    async loadTypeMetadata(request) {
      if (state.typeMetadataKey === request.signature
        && (state.typeMetadataLoading
          || state.typeMetadata
          || state.typeMetadataError)) {
        dependencies.renderPreservingMemberFocus();
        return;
      }
      const generation = ++state.typeMetadataGeneration;
      state.typeMetadataKey = request.signature;
      state.typeMetadata = null;
      state.typeMetadataError = "";
      state.typeMetadataLoading = true;
      const preservedFocus = dependencies.renderPreservingMemberFocus();
      const ownsRequest = () =>
        generation === state.typeMetadataGeneration
        && state.typeMetadataKey === request.signature;
      try {
        const result = await dependencies.queryTypeMetadata(request);
        if (ownsRequest()) state.typeMetadata = result;
      } catch (error) {
        if (ownsRequest()) {
          state.typeMetadataError = dependencies.describeError(error);
        }
      } finally {
        if (ownsRequest()) {
          state.typeMetadataLoading = false;
          if (request.isVisible()) {
            dependencies.renderPreservingMemberFocus(preservedFocus);
          }
        }
      }
    },

    async loadExplorerWindow(index, startRowId, maxRows) {
      const explorer = state.explorer;
      if (!explorer) return;
      const existing = explorer.windows[index];
      const sameRange = existing?.startRowId === startRowId
        && existing.maxRows === maxRows;
      if (sameRange && (existing.loading || existing.data)) {
        return;
      }
      const requests = explorerWindowRequests.get(explorer)
        ?? new Map<number, number>();
      explorerWindowRequests.set(explorer, requests);
      const requestSequence = ++explorerWindowRequestSequence;
      requests.set(index, requestSequence);
      const ownsRequest = () =>
        state.explorer === explorer
        && requests.get(index) === requestSequence;
      explorer.windows[index] = {
        loading: true,
        error: "",
        data: existing?.data || null,
        startRowId,
        maxRows,
      };
      dependencies.render();
      try {
        const result = explorer.isPlatform
          ? await dependencies.queryPlatformTable(
              explorer,
              index,
              startRowId,
              maxRows)
          : await dependencies.queryPackageTable(
              explorer,
              index,
              startRowId,
              maxRows);
        if (!ownsRequest()) return;
        const error = result.error || "";
        explorer.windows[index] = {
          loading: false,
          error,
          data: error ? null : result,
          startRowId,
          maxRows,
        };
      } catch (error) {
        if (!ownsRequest()) return;
        explorer.windows[index] = {
          loading: false,
          error: dependencies.describeError(error),
          data: null,
          startRowId,
          maxRows,
        };
      } finally {
        if (ownsRequest()) {
          dependencies.render();
          if (index === explorer.focusIndex && !explorer.focusHeap) {
            dependencies.scrollExplorerToFocus();
          }
        }
      }
    },

    async loadExplorerHeap(heapName) {
      const explorer = state.explorer;
      if (!explorer) return;
      const existing = explorer.heapWindows[heapName];
      if (existing && (existing.loading || existing.data)) return;
      explorer.heapWindows[heapName] = {
        loading: true,
        error: "",
        data: null,
      };
      dependencies.render();
      try {
        const result = explorer.isPlatform
          ? await dependencies.queryPlatformHeap(explorer, heapName)
          : await dependencies.queryPackageHeap(explorer, heapName);
        if (state.explorer !== explorer) return;
        const error = result.error || "";
        explorer.heapWindows[heapName] = {
          loading: false,
          error,
          data: error ? null : result,
        };
      } catch (error) {
        if (state.explorer !== explorer) return;
        explorer.heapWindows[heapName] = {
          loading: false,
          error: dependencies.describeError(error),
          data: null,
        };
      } finally {
        if (state.explorer === explorer) {
          dependencies.render();
          if (explorer.focusHeap === heapName) {
            dependencies.scrollExplorerToFocus();
          }
        }
      }
    },
  };
}
