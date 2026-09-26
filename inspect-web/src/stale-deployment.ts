/**
 * Detects that the deployed site replaced this tab's hashed code chunks.
 *
 * Vite dispatches `vite:preloadError` when a dynamic import or its preloaded
 * dependencies fail to load. After a redeploy, the old chunk hashes that this
 * tab references no longer exist, so retrying in place repeats the failure;
 * only a reload picks up the current build. The event is observed, not
 * prevented, so the original failure still reaches its caller.
 */

export const STALE_DEPLOYMENT_NOTICE =
  "dotnet-inspect was updated after this page loaded; retry reloads the page.";

let detected = false;

export function installStaleDeploymentDetection(target: EventTarget): void {
  target.addEventListener("vite:preloadError", () => {
    detected = true;
  });
}

export function staleDeploymentDetected(): boolean {
  return detected;
}

/** Test-only reset of the process-wide detection flag. */
export function resetStaleDeploymentDetectionForTest(): void {
  detected = false;
}

/**
 * Memoizes a lazy module import only while it has not failed, so a later
 * attempt re-requests the chunk instead of replaying a cached rejection.
 */
export function retainSuccessfulImport<T>(
  load: () => Promise<T>,
): () => Promise<T> {
  let pending: Promise<T> | undefined;
  return () => {
    pending ??= load().catch((error: unknown) => {
      pending = undefined;
      throw error;
    });
    return pending;
  };
}
