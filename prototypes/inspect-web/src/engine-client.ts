import type {
  EngineWorkerPackageQueryAdapter,
  EngineWorkerTypeSourceAdapter,
} from "./engine-worker-client.ts";
import type { bindEngineWorkerOperations } from "./engine-worker-operations.ts";
import type { EngineStartupClient } from "./engine-worker-startup.ts";
import type { registerEngineWorkerComparisonAdapters } from "./engine-worker-comparison.ts";

export type EngineClient = ReturnType<typeof bindEngineWorkerOperations>
  & EngineStartupClient & {
    readonly ready: Promise<void>;
    readonly source: { readonly typeSourceAdapter: EngineWorkerTypeSourceAdapter }
      & ReturnType<typeof registerEngineWorkerComparisonAdapters>;
    readonly package: { readonly queryAdapter: EngineWorkerPackageQueryAdapter };
  };
