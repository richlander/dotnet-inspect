type LensDefinition = readonly [id: string, label: string];

export type Scope = "package" | "type" | "member";

export interface RenderScopeBarOptions {
  scope: Scope;
  strip: readonly LensDefinition[];
  activeStripId: string | null;
  stripAttribute: string;
  escapeHtml: (value: unknown) => string;
}

function lensButton(
  id: string,
  label: string,
  active: boolean,
  attribute: string,
  index: number,
  escapeHtml: (value: unknown) => string,
): string {
  return `<button class="lens ${active ? "active" : ""}" ${attribute}="${id}">${escapeHtml(label)}<kbd>${index + 1}</kbd></button>`;
}

function scopeSegment(id: string, label: string, active: boolean): string {
  return `<button class="scope-seg ${active ? "active" : ""}" data-scope="${id}" role="tab" aria-selected="${active}">${label}</button>`;
}

// The scope switcher + lens strip. The leading segmented control is the scope ladder —
// Package (whole package), Types (one public type), and Member (a member of that type,
// shown only once you drill in). Each segment is selectable and swaps the strip beside it:
//   package → package lenses   type → type lenses   member → member sections
// Keeping all three families of buttons on one strip means the member modes (Overview,
// Call graph, …) live here too instead of inside the detail pane.
export function renderScopeBar(options: RenderScopeBarOptions): string {
  const { scope, strip, activeStripId, stripAttribute, escapeHtml } = options;
  const stripHtml = strip
    .map(([id, label], i) => lensButton(id, label, activeStripId === id, stripAttribute, i, escapeHtml))
    .join("");
  return `
    <nav class="lensbar" aria-label="Scope and lenses">
      <div class="scope-switch" role="tablist" aria-label="Scope">
        ${scopeSegment("package", "Package", scope === "package")}
        ${scopeSegment("type", "Types", scope === "type")}
        ${scope === "member" ? scopeSegment("member", "Member", true) : ""}
      </div>
      <span class="lens-separator"></span>
      ${stripHtml}
    </nav>`;
}
