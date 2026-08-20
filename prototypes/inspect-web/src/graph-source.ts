import { pdbSourceLimitationHtml } from "./data.js";

export interface GraphSourceResult {
  provider: string;
  provenance: string;
  url: string | null | undefined;
  pdbSourceLimitation?: string | null;
  text: string;
}

export interface RenderGraphSourceOptions {
  title: string;
  loading: boolean;
  source: GraphSourceResult | null;
  error: string;
  escapeHtml: (value: unknown) => string;
  highlightCSharp: (value: unknown) => string;
}

export function renderGraphSource(options: RenderGraphSourceOptions): string {
  const { title, loading, source, error, escapeHtml, highlightCSharp } = options;
  const body = loading
    ? `<div class="graph-source-status">Resolving source for ${escapeHtml(title)}…</div>`
    : source
      ? `<div class="source-provenance"><strong>${source.provider === "pdb" ? "PDB Source" : "Decompiled source"}</strong><span>${escapeHtml(source.provenance)}</span>${source.url ? `<a href="${escapeHtml(source.url)}" target="_blank" rel="noreferrer">open source ↗</a>` : ""}${pdbSourceLimitationHtml(source)}</div>
         <pre class="language-csharp"><code class="language-csharp">${highlightCSharp(source.text)}</code></pre>`
      : `<div class="graph-source-status error">${escapeHtml(error || "No source was returned.")}</div>`;
  return `
    <div class="graph-source-backdrop" id="graph-source-backdrop">
      <div class="graph-source" role="dialog" aria-modal="true" aria-label="Member source">
        <div class="graph-source-head">
          <span class="graph-source-title">${escapeHtml(title)}</span>
          <button id="graph-source-close" type="button" aria-label="Close">esc</button>
        </div>
        <div class="graph-source-body">${body}</div>
      </div>
    </div>`;
}
