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
    .forEach(button => button.addEventListener("click", actions.retry));
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
  return `<section class="overview-surface forwarded-type-overview" aria-labelledby="forwarded-type-title">
    <header class="api-surface-head overview-surface-head"><span class="overview-surface-label">Overview</span><p>Forwarded Type</p></header>
    <div class="overview-scroll">
      <header class="overview-identity">
        <div class="overview-identity-text">
          <p class="type-namespace">${e(row.namespace)}</p>
          <h1 id="forwarded-type-title">${e(row.name)}</h1>
          <p class="overview-identity-detail">Declared by ${e(assembly)}</p>
        </div>
      </header>
      <section class="document-section">
        <h2>Type forwarding</h2>
        <p>Forwarded to ${row.action && status.available !== false
          ? `<button type="button" class="type-chip" data-platform-forwarder="${e(row.id)}"${status.pending ? ' disabled aria-busy="true"' : ""}>${e(row.targetAssembly)}</button>`
          : `<span>${e(row.targetAssembly)}</span>`}</p>
        <p>This declaration forwards the Type to another Library; it does not define members here.</p>
        ${status.pending ? '<p role="status">Opening the forwarded Type...</p>' : ""}
        ${status.error ? `<p role="alert">${e(status.error)}</p>` : ""}
      </section>
    </div>
  </section>`;
}
