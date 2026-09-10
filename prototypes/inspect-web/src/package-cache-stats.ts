import type {
  BrowserPackageCacheStats,
} from "./facades/inspect-web-package.d.ts";

export interface PackageCacheStatsRefreshDependencies {
  load(): Promise<BrowserPackageCacheStats | null | undefined>;
  update(stats: BrowserPackageCacheStats): void;
  publish(): void;
}

export interface PackageCacheStatsRefresh {
  refresh(): Promise<void>;
  refreshAndPublish(): Promise<void>;
}

export function createPackageCacheStatsRefresh(
  dependencies: PackageCacheStatsRefreshDependencies,
): PackageCacheStatsRefresh {
  let generation = 0;

  async function refresh(publish: boolean): Promise<void> {
    const requestGeneration = ++generation;
    const stats = await dependencies.load();
    if (!stats || generation !== requestGeneration) return;
    dependencies.update(stats);
    if (publish) dependencies.publish();
  }

  return {
    refresh: () => refresh(false),
    refreshAndPublish: () => refresh(true),
  };
}
