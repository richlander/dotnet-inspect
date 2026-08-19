export interface BuildIdentity {
  version?: string;
  commit?: string;
  commitUrl?: string;
  builtAtUtc?: string;
}

export interface BrowserDiagnostics {
  assets: number;
  downloadMs: number;
  transfer: number;
  decoded?: number;
  startupMs: number;
  precomputeMs: number;
  totalMs: number;
}

export interface PackageCacheStats {
  packages: number;
  resident: number;
  residentBytes: number;
  workspaces: number;
}

export type PackageSource =
  | { kind: "file" }
  | { kind: "nuget.org" }
  | { kind: "feed"; host: string }
  | { kind: "platform" }
  | { kind: "unknown" };

export interface StatusBarModel {
  variant?: "workspace" | "home";
  ready?: boolean;
  statusLabel?: string;
  buildIdentity?: BuildIdentity | null;
  diagnostics?: BrowserDiagnostics | null;
  compactDiagnostics?: boolean;
  packageCache?: PackageCacheStats | null;
  source?: PackageSource;
  assembly?: string;
  framework?: string;
}

export function fmtMs(milliseconds: number | null | undefined): string {
  if (milliseconds == null) return "—";
  return milliseconds < 1000
    ? `${Math.round(milliseconds)} ms`
    : `${(milliseconds / 1000).toFixed(2)} s`;
}

export function fmtBytes(bytes: number | null | undefined): string {
  if (!bytes) return "—";
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(value < 10 && unit > 0 ? 1 : 0)} ${units[unit]}`;
}

export function buildIdentityHtml(
  identity: BuildIdentity | null | undefined,
  escapeHtml: (value: unknown) => string,
): string {
  if (!identity?.version) return "";

  const commit = identity.commit || "";
  const shortCommit = commit.slice(0, 7);
  const commitHtml = identity.commitUrl && shortCommit
    ? `<a href="${escapeHtml(identity.commitUrl)}" target="_blank" rel="noopener noreferrer">${escapeHtml(shortCommit)}</a>`
    : escapeHtml(shortCommit);
  const parsedTimestamp = Date.parse(identity.builtAtUtc || "");
  const builtAt = Number.isFinite(parsedTimestamp)
    ? new Date(parsedTimestamp).toLocaleString(undefined, {
        dateStyle: "medium",
        timeStyle: "medium",
        timeZone: "UTC",
      })
    : "";
  const title = [
    `dotnet-inspect ${identity.version}`,
    commit ? `commit ${commit}` : "",
    builtAt ? `built ${builtAt} UTC` : "",
  ].filter(Boolean).join(" · ");
  return `<span class="build-identity" title="${escapeHtml(title)}">v${escapeHtml(identity.version)}${shortCommit ? ` · ${commitHtml}` : ""}${builtAt ? ` · built ${escapeHtml(builtAt)} UTC` : ""}</span>`;
}

function diagnosticsHtml(
  diagnostics: BrowserDiagnostics | null | undefined,
): string {
  if (!diagnostics) return "";
  return `
      <span class="diag" title="Framework assets fetched over the wire — compressed → uncompressed, across ${diagnostics.assets} files">↓ download ${fmtMs(diagnostics.downloadMs)} · ${fmtBytes(diagnostics.transfer)}${diagnostics.decoded ? ` → ${fmtBytes(diagnostics.decoded)}` : ""}</span>
      <span class="diag" title="Runtime instantiation after assets arrived: WASM compile + module init + runMain">⚙ startup ${fmtMs(diagnostics.startupMs)}</span>
      <span class="diag" title="Initial package query precomputed during load">⚡ precompute ${fmtMs(diagnostics.precomputeMs)}</span>
      <span class="diag diag-total" title="Total time from navigation start to interactive">Σ ${fmtMs(diagnostics.totalMs)}</span>`;
}

function packageCacheHtml(
  cache: PackageCacheStats | null | undefined,
): string {
  if (!cache || cache.packages <= 0) return "";

  const packagePlural = cache.packages === 1 ? "" : "s";
  const evicted = cache.packages - cache.resident;
  const workspacePlural = cache.workspaces === 1 ? "" : "s";
  const title = `${cache.packages} distinct NuGet package${packagePlural} acquired this session; ${cache.resident} currently resident in the in-memory cache (${(cache.residentBytes / 1048576).toFixed(1)} MB, including archives retained by open workspaces)${evicted > 0 ? `; ${evicted} evicted under the aggregate LRU limit of 12 packages / 128 MB` : ""}; ${cache.workspaces} open workspace${workspacePlural} (LRU limit 4)`;
  return `
      <span class="diag" title="${title}">◇ ${cache.packages} package${packagePlural} · ${cache.resident} resident in cache · ${cache.workspaces} workspace${workspacePlural}</span>`;
}

export function packageSourceLabel(source: unknown): string {
  if (!source || typeof source !== "object" || !("kind" in source)) {
    return "Unknown";
  }
  switch (source.kind) {
    case "file":
      return "File";
    case "nuget.org":
      return "NuGet.org";
    case "feed":
      return "host" in source
        && typeof source.host === "string"
        && source.host.trim()
        ? source.host.trim()
        : "Unknown";
    case "platform":
      return "Platform";
    case "unknown":
      return "Unknown";
    default:
      return "Unknown";
  }
}

export function statusBarHtml(
  model: StatusBarModel,
  escapeHtml: (value: unknown) => string,
): string {
  const ready = model.ready ?? true;
  const statusLabel = model.statusLabel
    ?? (ready ? "browser wasm ready" : "browser wasm loading");
  const classes = model.variant === "home"
    ? "statusbar data-bar home-foot"
    : "statusbar data-bar";
  const diagnostics = model.compactDiagnostics && model.diagnostics
    ? `<span class="diag">⚙ ready in ${fmtMs(model.diagnostics.totalMs)}</span>`
    : diagnosticsHtml(model.diagnostics);
  const tail = model.variant !== "home" && model.assembly && model.framework
    ? `
      <span class="status-spacer"></span>
      <span>Source: ${escapeHtml(packageSourceLabel(model.source))}</span>
      <span>${escapeHtml(model.assembly)}</span>
      <span>${escapeHtml(model.framework)}</span>
      <span>public API surface</span>`
    : "";

  return `
    <footer class="${classes}">
      ${ready
        ? '<span class="ready-dot"></span>'
        : '<span class="home-wasm-spinner" aria-hidden="true"></span>'}<span>${escapeHtml(statusLabel)}</span>
      ${buildIdentityHtml(model.buildIdentity, escapeHtml)}
      ${diagnostics}
      ${packageCacheHtml(model.packageCache)}
      ${tail}
    </footer>`;
}
