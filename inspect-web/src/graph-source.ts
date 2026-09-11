import { assertNever, pdbSourceLimitationHtml } from "./data.ts";
import type { OpenGraphSourceState } from "./source-inspection.ts";

export interface RenderGraphSourceOptions {
  state: OpenGraphSourceState;
  escapeHtml: (value: unknown) => string;
  highlightCSharp: (value: string) => string;
}

export interface GraphSourceBindingActions {
  onClose: () => void;
}

export function bindGraphSource(
  root: ParentNode,
  actions: GraphSourceBindingActions,
) {
  const backdrop =
    root.querySelector<HTMLElement>("#graph-source-backdrop");
  backdrop?.addEventListener("mousedown", event => {
    if (event.target === backdrop) actions.onClose();
  });
  root.querySelector("#graph-source-close")?.addEventListener(
    "click",
    actions.onClose);
}

export function renderGraphSource(options: RenderGraphSourceOptions): string {
  const { state, escapeHtml, highlightCSharp } = options;
  let body: string;
  switch (state.status) {
    case "loading":
      body = `<div class="graph-source-status">Resolving source for ${escapeHtml(state.title)}…</div>`;
      break;
    case "ready": {
      const { source } = state;
      body = `<div class="source-provenance"><strong>${source.provider === "pdb" ? "PDB Source" : "Decompiled source"}</strong><span>${escapeHtml(source.provenance)}</span>${source.url ? `<a href="${escapeHtml(source.url)}" target="_blank" rel="noreferrer">open source ↗</a>` : ""}${pdbSourceLimitationHtml(source)}</div>
         <pre class="language-csharp"><code class="language-csharp">${highlightCSharp(source.text)}</code></pre>`;
      break;
    }
    case "failed":
      body = `<div class="graph-source-status error">${escapeHtml(state.error || "No source was returned.")}</div>`;
      break;
    case "cancelled":
      body = `<div class="graph-source-status error">No source was returned.</div>`;
      break;
    default:
      return assertNever(state, "open graph source state");
  }
  return `
    <div class="graph-source-backdrop" id="graph-source-backdrop">
      <div class="graph-source" role="dialog" aria-modal="true" aria-label="Member source">
        <div class="graph-source-head">
          <span class="graph-source-title">${escapeHtml(state.title)}</span>
          <button id="graph-source-close" type="button" aria-label="Close">esc</button>
        </div>
        <div class="graph-source-body">${body}</div>
      </div>
    </div>`;
}
