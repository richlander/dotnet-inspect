import { fmtBytes } from "./data-bar.ts";
import type { BrowserBuildIdentity } from "./facades/inspect-web-host.d.ts";
import type { BrowserPackageCacheStats } from "./facades/inspect-web-package.d.ts";
import { renderBrand } from "./brand.ts";

export interface RuntimeStartupDiagnostics {
  downloadMs: number | null;
  startupMs: number;
  precomputeMs: number;
  totalMs: number;
  transfer: number | null;
  decoded: number | null;
  assets: number | null;
}

export type DiagnosticsRuntimeState =
  | {
      kind: "loading";
      message: string;
    }
  | {
      kind: "ready";
      message: string;
      diagnostics: RuntimeStartupDiagnostics | null;
      measurementsPending: boolean;
    }
  | {
      kind: "failed";
      message: string;
      detail: string | null;
    };

export type DiagnosticsPackageCacheState =
  | {
      kind: "loading";
    }
  | {
      kind: "ready";
      stats: BrowserPackageCacheStats;
    }
  | {
      kind: "failed";
      message: string;
    };

export type DiagnosticsBuildState =
  | {
      kind: "loading";
    }
  | {
      kind: "ready";
      identity: BrowserBuildIdentity;
    }
  | {
      kind: "failed";
      message: string;
    };

export interface DiagnosticsViewModel {
  runtime: DiagnosticsRuntimeState;
  build: DiagnosticsBuildState;
  packageCache: DiagnosticsPackageCacheState;
  capturedAtUtc: string;
}

export interface DiagnosticsViewActions {
  onBack: () => void;
  onHome: () => void;
}

function formatDuration(milliseconds: number | null): string {
  if (milliseconds === null
    || !Number.isFinite(milliseconds)
    || milliseconds < 0) return "Unavailable";
  if (milliseconds < 1000) return `${Math.round(milliseconds)} ms`;
  const seconds = milliseconds / 1000;
  return `${seconds.toFixed(seconds < 10 ? 2 : 1)} s`;
}

function formatInteger(value: number | null): string {
  return value !== null && Number.isFinite(value) && value >= 0
    ? Math.round(value).toLocaleString("en-US")
    : "Unavailable";
}

function formatBytes(value: number | null): string {
  if (value === null || !Number.isFinite(value) || value < 0)
    return "Unavailable";
  return value === 0 ? "0 B" : fmtBytes(value);
}

function formatUtcTimestamp(value: string): string {
  const timestamp = new Date(value);
  if (Number.isNaN(timestamp.getTime())) return "Unavailable";
  return timestamp.toLocaleString("en-US", {
    day: "numeric",
    hour: "numeric",
    hour12: true,
    minute: "2-digit",
    month: "short",
    second: "2-digit",
    timeZone: "UTC",
    timeZoneName: "short",
    year: "numeric",
  });
}

function factHtml(
  label: string,
  value: string,
  escapeHtml: (value: string) => string,
  options: { className?: string; valueHtml?: string } = {},
): string {
  const className = options.className
    ? ` diagnostics-fact-${options.className}`
    : "";
  return `<div class="diagnostics-fact${className}">
    <dt>${escapeHtml(label)}</dt>
    <dd>${options.valueHtml ?? escapeHtml(value)}</dd>
  </div>`;
}

function phaseWidth(value: number | null, total: number): string {
  if (value === null
    || !Number.isFinite(value)
    || value <= 0
    || total <= 0) return "0";
  return Math.max(0, Math.min(100, value / total * 100)).toFixed(3);
}

function runtimeCardHtml(
  runtime: DiagnosticsRuntimeState,
  escapeHtml: (value: string) => string,
): string {
  const stateClass = ` diagnostics-runtime-${runtime.kind}`;

  let body = "";
  if (runtime.kind === "ready" && runtime.diagnostics) {
    const diagnostics = runtime.diagnostics;
    const measuredTotal = Math.max(
      (diagnostics.downloadMs ?? 0)
        + diagnostics.startupMs
        + diagnostics.precomputeMs,
      diagnostics.totalMs,
      0,
    );
    body = `<div class="diagnostics-card-body">
      <div class="diagnostics-total">
        <span>Total startup</span>
        <strong>${escapeHtml(formatDuration(diagnostics.totalMs))}</strong>
      </div>
      <div class="diagnostics-phase-bar" aria-label="Startup phases">
        <span class="diagnostics-phase-download" style="--diagnostics-phase-width:${phaseWidth(diagnostics.downloadMs, measuredTotal)}%"></span>
        <span class="diagnostics-phase-startup" style="--diagnostics-phase-width:${phaseWidth(diagnostics.startupMs, measuredTotal)}%"></span>
        <span class="diagnostics-phase-precompute" style="--diagnostics-phase-width:${phaseWidth(diagnostics.precomputeMs, measuredTotal)}%"></span>
      </div>
      <dl class="diagnostics-phase-legend" aria-label="Startup phase legend">
        ${factHtml("Download", formatDuration(diagnostics.downloadMs), escapeHtml, { className: "phase-download" })}
        ${factHtml("Start runtime", formatDuration(diagnostics.startupMs), escapeHtml, { className: "phase-startup" })}
        ${factHtml("Prepare inspection", formatDuration(diagnostics.precomputeMs), escapeHtml, { className: "phase-precompute" })}
      </dl>
      <dl class="diagnostics-facts diagnostics-runtime-facts">
      ${factHtml("Framework assets", formatInteger(diagnostics.assets), escapeHtml)}
      ${factHtml("Transferred", formatBytes(diagnostics.transfer), escapeHtml)}
      ${factHtml("Decoded", formatBytes(diagnostics.decoded), escapeHtml)}
      </dl>
    </div>`;
  } else if (runtime.kind === "ready" && runtime.measurementsPending) {
    body = `<div class="diagnostics-inline-state">
      <span class="diagnostics-inline-spinner" aria-hidden="true"></span>
      <p>Finishing startup measurements.</p>
    </div>`;
  } else if (runtime.kind === "ready") {
    body = `<p class="diagnostics-unavailable">Startup measurements are unavailable for this session.</p>`;
  } else if (runtime.kind === "failed" && runtime.detail) {
    body = `<pre class="diagnostics-failure-detail">${escapeHtml(runtime.detail)}</pre>`;
  } else {
    body = `<div class="diagnostics-progress" aria-hidden="true"><span></span></div>`;
  }

  return `<section class="diagnostics-card diagnostics-runtime-card${stateClass}" aria-labelledby="diagnostics-runtime-heading">
    <div class="diagnostics-card-head">
      <h2 id="diagnostics-runtime-heading">Runtime startup</h2>
      <span class="diagnostics-owner">Browser Performance API</span>
    </div>
    ${body}
  </section>`;
}

function runtimeStateHtml(
  runtime: DiagnosticsRuntimeState,
  escapeHtml: (value: string) => string,
): string {
  const title = runtime.kind === "ready"
    ? "Browser inspection engine ready"
    : runtime.kind === "failed"
      ? "Browser inspection engine failed"
      : "Browser inspection engine loading";
  return `<div class="diagnostics-runtime-state diagnostics-runtime-state-${runtime.kind}" role="status">
    <span class="diagnostics-runtime-dot" aria-hidden="true"></span>
    <strong>${title}</strong>
    <span>${escapeHtml(runtime.message)}</span>
  </div>`;
}

function buildCardHtml(
  build: DiagnosticsBuildState,
  escapeHtml: (value: string) => string,
): string {
  let body = "";
  if (build.kind === "ready") {
    const facts = [];
    const { identity } = build;
    const version = identity.version.trim();
    facts.push(
      factHtml("Version", version || "Unavailable", escapeHtml),
    );
    const commit = identity.commit?.trim() ?? "";
    const commitHtml = commit && identity.commitUrl
      ? `<a id="diagnostics-commit" href="${escapeHtml(identity.commitUrl)}" target="_blank" rel="noopener noreferrer"><code>${escapeHtml(commit)}</code></a>`
      : commit
        ? `<code>${escapeHtml(commit)}</code>`
        : undefined;
    const commitOptions = commitHtml ? { valueHtml: commitHtml } : {};
    facts.push(
      factHtml("Commit", commit || "Unavailable", escapeHtml, commitOptions),
      factHtml(
        "Built",
        identity.builtAtUtc
          ? formatUtcTimestamp(identity.builtAtUtc)
          : "Unavailable",
        escapeHtml,
      ),
    );
    facts.push(factHtml("Host", "Browser / WebAssembly", escapeHtml));
    body = `<dl class="diagnostics-facts">${facts.join("")}</dl>`;
  } else if (build.kind === "failed") {
    body = `<div class="diagnostics-inline-state diagnostics-inline-failed">
      <span aria-hidden="true">!</span>
      <p>${escapeHtml(build.message)}</p>
    </div>`;
  } else {
    body = `<div class="diagnostics-inline-state">
      <span class="diagnostics-inline-spinner" aria-hidden="true"></span>
      <p>Reading product build identity.</p>
    </div>`;
  }

  return `<section class="diagnostics-card" aria-labelledby="diagnostics-build-heading">
    <div class="diagnostics-card-head">
      <h2 id="diagnostics-build-heading">Build</h2>
      <span class="diagnostics-owner">Host identity</span>
    </div>
    ${body}
  </section>`;
}

function cacheCardHtml(
  cache: DiagnosticsPackageCacheState,
  escapeHtml: (value: string) => string,
): string {
  let body = "";
  if (cache.kind === "ready") {
    body = `<dl class="diagnostics-facts">
      ${factHtml("Packages", formatInteger(cache.stats.packages), escapeHtml)}
      ${factHtml("Resident payloads", formatInteger(cache.stats.resident), escapeHtml)}
      ${factHtml("Workspaces", formatInteger(cache.stats.workspaces), escapeHtml)}
      ${factHtml("Resident bytes", formatBytes(cache.stats.residentBytes), escapeHtml, { className: "emphasis" })}
    </dl>`;
  } else if (cache.kind === "failed") {
    body = `<div class="diagnostics-inline-state diagnostics-inline-failed">
      <span aria-hidden="true">!</span>
      <p>${escapeHtml(cache.message)}</p>
    </div>`;
  } else {
    body = `<div class="diagnostics-inline-state">
      <span class="diagnostics-inline-spinner" aria-hidden="true"></span>
      <p>Reading aggregate package-cache statistics.</p>
    </div>`;
  }

  return `<section class="diagnostics-card" aria-labelledby="diagnostics-cache-heading">
    <div class="diagnostics-card-head">
      <h2 id="diagnostics-cache-heading">Package cache</h2>
      <span class="diagnostics-owner">Browser package workspace</span>
    </div>
    ${body}
  </section>`;
}

export function diagnosticsViewHtml(
  model: DiagnosticsViewModel,
  escapeHtml: (value: string) => string,
): string {
  return `<div class="diagnostics-view">
    <header class="diagnostics-header">
      ${renderBrand({
        ariaLabel: "dotnet-inspect workspace",
        href: "/",
        id: "diagnostics-product",
      })}
      <button id="diagnostics-back" class="diagnostics-back" type="button">
        Back
      </button>
    </header>
    <main class="diagnostics-main">
      <header class="diagnostics-title">
        <div>
          <p class="diagnostics-eyebrow">Current browser session</p>
          <h1 id="diagnostics-heading" tabindex="-1">Diagnostics</h1>
          <p>Runtime, build, and local package-cache evidence for this tab. Inspection runs locally in your browser.</p>
        </div>
        <p class="diagnostics-captured">Captured ${escapeHtml(formatUtcTimestamp(model.capturedAtUtc))}</p>
      </header>
      ${runtimeStateHtml(model.runtime, escapeHtml)}
      <div class="diagnostics-grid">
        ${runtimeCardHtml(model.runtime, escapeHtml)}
        ${buildCardHtml(model.build, escapeHtml)}
        ${cacheCardHtml(model.packageCache, escapeHtml)}
      </div>
    </main>
  </div>`;
}

export function bindDiagnosticsView(
  root: ParentNode,
  actions: DiagnosticsViewActions,
): void {
  const activate = (action: () => void) => (event: Event) => {
    event.preventDefault();
    action();
  };
  root.querySelector<HTMLElement>("#diagnostics-product")
    ?.addEventListener("click", activate(actions.onHome));
  root.querySelector<HTMLButtonElement>("#diagnostics-back")
    ?.addEventListener("click", activate(actions.onBack));
}
