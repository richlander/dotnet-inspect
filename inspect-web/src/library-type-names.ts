import type {
  BrowserLibraryTypeNames,
} from "./facades/inspect-web-library.d.ts";

export function renderLibraryTypeNames(
  names: BrowserLibraryTypeNames | null,
  escapeHtml: (value: string) => string,
): string {
  if (!names || names.outcome !== "Available") {
    return `<section class="document-section inspection-error" role="alert">
      <h2>Type names unavailable</h2>
      <p>${escapeHtml(names?.detail ?? "The Library returned no Type names.")}</p>
    </section>`;
  }
  if (names.namespaces.length === 0) {
    return '<section class="document-section"><h2>Types</h2><p class="empty-list">No public types.</p></section>';
  }
  return `<section class="document-section library-type-names">
    <div class="section-title"><h2>Types</h2></div>
    <ul class="package-child-tree">
      ${names.namespaces.map(namespace => `<li>
        <strong>${escapeHtml(namespace.name || "(global namespace)")}</strong>
        <span>${namespace.declarationCount} ${namespace.declarationCount === 1 ? "type" : "types"}</span>
        <ul>${namespace.types.map(type => `<li>
          ${type.declarationKind === "Forwarder"
            ? `<span class="library-type-name">${escapeHtml(type.name)} (forwarded)</span>`
            : `<button type="button" class="library-type-name" data-library-type-name="${escapeHtml(type.definitionId)}">${escapeHtml(type.definitionKind?.toLowerCase() ?? "")} ${escapeHtml(type.name)}</button>`}
        </li>`).join("")}</ul>
      </li>`).join("")}
    </ul>
  </section>`;
}
