import type {
  BrowserPlatformForwarderRow,
} from "./facades/inspect-web-package.d.ts";
import type { TypeSummary } from "./type-panel.ts";

export type TypeInventoryRow = TypeSummary | BrowserPlatformForwarderRow;

export function isForwardedType(
  row: TypeInventoryRow,
): row is BrowserPlatformForwarderRow {
  return "targetAssembly" in row;
}

export function filterForwardedTypes(
  rows: readonly BrowserPlatformForwarderRow[],
  filter: { text: string; namespace: string; kind: string },
): BrowserPlatformForwarderRow[] {
  const needle = filter.text.toLowerCase();
  return rows.filter(row =>
    (!filter.kind || filter.kind === "forwarded")
    && (!filter.namespace || row.namespace === filter.namespace)
    && (!needle
      || `${row.namespace}.${row.name} ${row.targetAssembly} forwarded`
        .toLowerCase().includes(needle)));
}

export function bindPlatformForwarders(
  root: ParentNode,
  actions: { retry(): void; activate(rowId: string): void },
) {
  root.querySelectorAll<HTMLButtonElement>("[data-forwarder-inventory-retry]")
    .forEach(button => button.addEventListener("click", () => actions.retry()));
  root.querySelectorAll<HTMLButtonElement>("[data-platform-forwarder]")
    .forEach(button => button.addEventListener("click", () => {
      const rowId = button.dataset.platformForwarder;
      if (rowId) actions.activate(rowId);
    }));
}

export function renderForwardedTypeOverview(
  row: BrowserPlatformForwarderRow,
  assembly: string,
  status: { pending: boolean; error: string; available?: boolean },
  escapeHtml: (value: unknown) => string,
): string {
  const e = escapeHtml;
  const destination = row.action && status.available !== false
    ? `<button type="button" class="forwarder-destination" data-platform-forwarder="${e(row.id)}" aria-label="${e(`Open ${row.name} in ${row.targetAssembly}`)}"${status.pending ? ' disabled aria-busy="true"' : ""}>${e(row.targetAssembly)} <span aria-hidden="true">→</span></button>`
    : `<code>${e(row.targetAssembly)}</code>`;
  return `<section class="metadata-surface forwarded-type-overview" aria-labelledby="forwarded-type-title">
    <header class="metadata-surface-head">
      <h1 id="forwarded-type-title">Type forwarder</h1>
      <p>ExportedType <span>· ECMA-335 metadata</span></p>
    </header>
    <div class="metadata-surface-scroll">
      <section class="document-section metadata-shape-section">
        <div class="section-title"><h2>Declaration</h2><span>Type forwarding</span></div>
        <dl class="fact-rows">
          <div><dt>Type</dt><dd><code>${e(row.name)}</code></dd></div>
          <div><dt>Namespace</dt><dd><code>${e(row.namespace || "global")}</code></dd></div>
          <div><dt>Declaring assembly</dt><dd><code>${e(assembly)}</code></dd></div>
          <div><dt>Implementation</dt><dd>${destination}</dd></div>
        </dl>
      </section>
      ${status.pending ? '<p class="forwarder-status" role="status">Opening the forwarded Type…</p>' : ""}
      ${status.error ? `<p class="forwarder-status" role="alert">${e(status.error)}</p>` : ""}
    </div>
  </section>`;
}
