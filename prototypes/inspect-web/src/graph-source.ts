import { assertNever, pdbSourceLimitationHtml } from "./data.ts";
import type { GraphSourceState } from "./source-inspection.ts";

// The modal renders only when it is open, so the renderer takes the open variants and the
// composition root narrows before calling it. A closed modal is therefore not a state this
// function can be asked to draw.
export type OpenGraphSourceState = Exclude<GraphSourceState, { status: "closed" }>;

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

// Exhaustive over the open variants, terminating in `assertNever`, so a new variant is a
// compile error here until it is given a body. That is the gate for the modal never
// rendering a state it does not understand.
function graphSourceBodyHtml(
  state: OpenGraphSourceState,
  escapeHtml: (value: unknown) => string,
  highlightCSharp: (value: string) => string,
): string {
  switch (state.status) {
    case "loading":
      return `<div class="graph-source-status">Resolving source for ${escapeHtml(state.title)}…</div>`;
    case "ready": {
      const { source } = state;
      return `<div class="source-provenance"><strong>${source.provider === "pdb" ? "PDB Source" : "Decompiled source"}</strong><span>${escapeHtml(source.provenance)}</span>${source.url ? `<a href="${escapeHtml(source.url)}" target="_blank" rel="noreferrer">open source ↗</a>` : ""}${pdbSourceLimitationHtml(source)}</div>
         <pre class="language-csharp"><code class="language-csharp">${highlightCSharp(source.text)}</code></pre>`;
    }
    case "failed":
      return `<div class="graph-source-status error">${escapeHtml(state.error || "No source was returned.")}</div>`;
    case "cancelled":
      // A competing source request retired this load while its modal stayed open. The
      // previous field layout produced exactly this text for that state by falling through
      // to an empty error; the variant names the state without changing what a user sees.
      return `<div class="graph-source-status error">No source was returned.</div>`;
    default:
      return assertNever(state, "open graph source status");
  }
}

export function renderGraphSource(options: RenderGraphSourceOptions): string {
  const { state, escapeHtml, highlightCSharp } = options;
  const body = graphSourceBodyHtml(state, escapeHtml, highlightCSharp);
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
