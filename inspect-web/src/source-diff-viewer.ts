import type {
  BrowserSourceDiff,
  BrowserSourceDiffChange,
  BrowserSourceDiffSpan,
} from "./source-diff-transport.ts";

export type SourceDiffViewerMode = "unified" | "side-by-side";

export interface SourceDiffViewerOptions {
  readonly compact?: boolean;
  readonly mode?: SourceDiffViewerMode;
}

export interface SourceDiffViewerBinding {
  readonly mode: SourceDiffViewerMode;
  readonly onModeChanged: (mode: SourceDiffViewerMode) => void;
  readonly writeClipboardText: (value: string) => Promise<void>;
}

type BrowserSourceDiffRelation = BrowserSourceDiff["relations"][number];

interface SourceRelationLookup {
  readonly before: ReadonlyMap<number, readonly BrowserSourceDiffRelation[]>;
  readonly after: ReadonlyMap<number, readonly BrowserSourceDiffRelation[]>;
}

interface SourceDiffViewerRow {
  readonly kind: "context" | "removal" | "addition";
  readonly beforeIndex: number | null;
  readonly afterIndex: number | null;
  readonly changeIndex: number | null;
  readonly change: BrowserSourceDiffChange | null;
}

interface SourceDiffViewerModel {
  readonly rows: readonly SourceDiffViewerRow[];
  readonly defects: readonly string[];
  readonly invalidChanges: ReadonlySet<number>;
}

function attributeText(
  value: unknown,
  escapeHtml: (value: unknown) => string,
): string {
  return escapeHtml(value).replaceAll("\r", "&#13;");
}

function sourceRelationLookup(diff: BrowserSourceDiff): SourceRelationLookup {
  const before = new Map<number, BrowserSourceDiffRelation[]>();
  const after = new Map<number, BrowserSourceDiffRelation[]>();
  const add = (
    index: Map<number, BrowserSourceDiffRelation[]>,
    coordinate: number,
    relation: BrowserSourceDiffRelation,
  ): void => {
    const relations = index.get(coordinate);
    if (relations === undefined) index.set(coordinate, [relation]);
    else relations.push(relation);
  };
  for (const relation of diff.relations) {
    for (const coordinate of relation.beforeCoordinates)
      add(before, coordinate, relation);
    for (const coordinate of relation.afterCoordinates)
      add(after, coordinate, relation);
  }
  return { before, after };
}

function relationDecorations(
  beforeIndex: number | null,
  afterIndex: number | null,
  lookup: SourceRelationLookup,
  escapeHtml: (value: unknown) => string,
): string {
  const relations = new Set<BrowserSourceDiffRelation>();
  if (beforeIndex !== null) {
    for (const relation of lookup.before.get(beforeIndex) ?? [])
      relations.add(relation);
  }
  if (afterIndex !== null) {
    for (const relation of lookup.after.get(afterIndex) ?? [])
      relations.add(relation);
  }
  const labels = [...relations].flatMap(relation => {
    const facts = [relation.content, relation.placement]
      .filter(fact => fact !== null);
    if (facts.length === 0) return [];
    return [
      `<span class="member-diff-source-relation-label source-diff-viewer-relation-label" data-relation-kind="${attributeText(relation.kind, escapeHtml)}" data-relation-content="${attributeText(relation.content ?? "", escapeHtml)}" data-relation-placement="${attributeText(relation.placement ?? "", escapeHtml)}">${facts.map(escapeHtml).join(" · ")}</span>`,
    ];
  });
  return labels.length === 0
    ? ""
    : `<span class="member-diff-source-relations source-diff-viewer-relations" aria-label="Relation facts">${labels.join("")}</span>`;
}

function changedSpans(
  change: BrowserSourceDiffChange | null,
  side: "before" | "after",
  line: number,
): readonly BrowserSourceDiffSpan[] {
  if (change === null) return [];
  return change.innerMappings
    .map(mapping => mapping[side])
    .filter(span => span.line === line)
    .sort((left, right) => left.start - right.start);
}

function validSpans(
  text: string,
  spans: readonly BrowserSourceDiffSpan[],
): boolean {
  let cursor = 0;
  for (const span of spans) {
    if (span.start < cursor
      || span.count < 0
      || span.start > text.length
      || span.count > text.length - span.start) {
      return false;
    }
    cursor = span.start + span.count;
  }
  return true;
}

function highlightedLine(
  text: string,
  spans: readonly BrowserSourceDiffSpan[],
  escapeHtml: (value: unknown) => string,
): string {
  if (spans.length === 0 || !validSpans(text, spans))
    return escapeHtml(text);
  const parts: string[] = [];
  let cursor = 0;
  for (const span of spans) {
    parts.push(escapeHtml(text.slice(cursor, span.start)));
    parts.push(`<mark>${escapeHtml(
      text.slice(span.start, span.start + span.count),
    )}</mark>`);
    cursor = span.start + span.count;
  }
  parts.push(escapeHtml(text.slice(cursor)));
  return parts.join("");
}

function sourceDiffViewerModel(diff: BrowserSourceDiff): SourceDiffViewerModel {
  const rows: SourceDiffViewerRow[] = [];
  const defects: string[] = [];
  const invalidChanges = new Set<number>();
  let beforeCursor = 0;
  let afterCursor = 0;
  const contextRows = (beforeEnd: number, afterEnd: number): void => {
    while (beforeCursor < beforeEnd && afterCursor < afterEnd) {
      rows.push({
        kind: "context",
        beforeIndex: beforeCursor++,
        afterIndex: afterCursor++,
        changeIndex: null,
        change: null,
      });
    }
  };
  for (const [changeIndex, change] of diff.changes.entries()) {
    contextRows(change.before.start, change.after.start);
    const beforeEnd = change.before.start + change.before.count;
    while (beforeCursor < beforeEnd) {
      rows.push({
        kind: "removal",
        beforeIndex: beforeCursor++,
        afterIndex: null,
        changeIndex,
        change,
      });
    }
    const afterEnd = change.after.start + change.after.count;
    while (afterCursor < afterEnd) {
      rows.push({
        kind: "addition",
        beforeIndex: null,
        afterIndex: afterCursor++,
        changeIndex,
        change,
      });
    }
    for (const side of ["before", "after"] as const) {
      const sequence = diff[side];
      const lineSpans = new Map<number, BrowserSourceDiffSpan[]>();
      for (const mapping of change.innerMappings) {
        const span = mapping[side];
        const spans = lineSpans.get(span.line);
        if (spans === undefined) lineSpans.set(span.line, [span]);
        else spans.push(span);
      }
      for (const [line, spans] of lineSpans) {
        const text = sequence.lines[line] ?? "";
        const range = change[side];
        if (line < range.start
          || line >= range.start + range.count
          || !validSpans(
          text,
          [...spans].sort((left, right) => left.start - right.start),
        )) {
          invalidChanges.add(changeIndex);
          defects.push(
            `Change ${changeIndex + 1} has invalid ${side} intraline mappings on line ${line + 1}.`,
          );
        }
      }
    }
  }
  contextRows(diff.before.lines.length, diff.after.lines.length);
  return { rows, defects, invalidChanges };
}

function terminatorMarker(
  diff: BrowserSourceDiff,
  side: "before" | "after",
  line: number | null,
  shared = false,
): string {
  if (line === null
    || diff[side].finalLineTerminator !== "Absent"
    || line !== diff[side].lines.length - 1) {
    return "";
  }
  return `<span class="source-diff-viewer-terminator${shared ? " source-diff-viewer-terminator-shared" : ""}" data-final-line-terminator="${shared ? "both" : side}" aria-label="No newline at end">No newline at end</span>`;
}

function unifiedRow(
  row: SourceDiffViewerRow,
  rowIndex: number,
  diff: BrowserSourceDiff,
  relations: SourceRelationLookup,
  invalidChanges: ReadonlySet<number>,
  showTerminators: boolean,
  escapeHtml: (value: unknown) => string,
): string {
  const side = row.kind === "addition" ? "after" : "before";
  const index = side === "before" ? row.beforeIndex : row.afterIndex;
  if (index === null)
    throw new Error("A Source diff row has no line on its rendered side.");
  const marker = row.kind === "context"
    ? " "
    : row.kind === "removal"
      ? "−"
      : "+";
  const text = diff[side].lines[index] ?? "";
  const changeForHighlight = row.changeIndex !== null
      && invalidChanges.has(row.changeIndex)
    ? null
    : row.change;
  const sharedTerminator = row.kind === "context"
    && diff.before.finalLineTerminator === "Absent"
    && diff.after.finalLineTerminator === "Absent"
    && row.beforeIndex === diff.before.lines.length - 1
    && row.afterIndex === diff.after.lines.length - 1;
  const terminator = !showTerminators
    ? ""
    : sharedTerminator
      ? terminatorMarker(diff, "before", row.beforeIndex, true)
      : `${terminatorMarker(diff, "before", row.beforeIndex)}${terminatorMarker(diff, "after", row.afterIndex)}`;
  const change = row.changeIndex === null
    ? ""
    : ` data-source-diff-change="${row.changeIndex}" tabindex="-1"`;
  const label = row.kind === "context"
    ? "Context"
    : row.kind === "removal"
      ? "Removed"
      : "Added";
  return `<div class="member-diff-source-line member-diff-source-line-${row.kind} source-diff-viewer-row source-diff-viewer-row-${row.kind}" role="row" aria-rowindex="${rowIndex + 1}" data-row-kind="${row.kind}" data-before-line="${row.beforeIndex ?? ""}" data-after-line="${row.afterIndex ?? ""}"${change}>
    <span class="member-diff-source-number source-diff-viewer-number" role="rowheader">${row.beforeIndex === null ? "" : row.beforeIndex + 1}</span>
    <span class="member-diff-source-number source-diff-viewer-number" role="rowheader">${row.afterIndex === null ? "" : row.afterIndex + 1}</span>
    <span class="member-diff-source-marker source-diff-viewer-marker" aria-hidden="true">${marker}</span>
    <span class="visually-hidden">${label}</span>
    <code role="cell">${highlightedLine(
      text,
      changedSpans(changeForHighlight, side, index),
      escapeHtml,
    )}</code>
    ${terminator}
    ${relationDecorations(
      row.beforeIndex,
      row.afterIndex,
      relations,
      escapeHtml,
    )}
  </div>`;
}

function splitSourceCell(
  side: "before" | "after",
  index: number | null,
  changed: boolean,
  change: BrowserSourceDiffChange | null,
  diff: BrowserSourceDiff,
  relations: SourceRelationLookup,
  escapeHtml: (value: unknown) => string,
): string {
  const sideLabel = side === "before" ? "Before" : "After";
  const columnIndex = side === "before" ? 1 : 2;
  if (index === null) {
    return `<span class="source-diff-viewer-split-empty" role="cell" aria-colindex="${columnIndex}">
      <span class="visually-hidden">${sideLabel}; no line</span>
    </span>`;
  }
  const changeLabel = !changed
    ? "Context"
    : side === "before"
      ? "Removed"
      : "Added";
  const text = diff[side].lines[index] ?? "";
  return `<span class="source-diff-viewer-split-source" role="cell" aria-colindex="${columnIndex}">
    <span class="visually-hidden">${sideLabel}; ${changeLabel}; line ${index + 1}</span>
    <span class="member-diff-source-number source-diff-viewer-number" aria-hidden="true">${index + 1}</span>
    <code>${highlightedLine(
      text,
      changedSpans(change, side, index),
      escapeHtml,
    )}</code>
    ${terminatorMarker(diff, side, index)}
    ${relationDecorations(
      side === "before" ? index : null,
      side === "after" ? index : null,
      relations,
      escapeHtml,
    )}
  </span>`;
}

function splitRowCount(model: SourceDiffViewerModel): number {
  let count = 0;
  let index = 0;
  while (index < model.rows.length) {
    const row = model.rows[index]!;
    if (row.changeIndex === null) {
      count++;
      index++;
      continue;
    }
    const changeIndex = row.changeIndex;
    let removals = 0;
    let additions = 0;
    while (index < model.rows.length
      && model.rows[index]!.changeIndex === changeIndex) {
      if (model.rows[index]!.kind === "removal") removals++;
      else additions++;
      index++;
    }
    count += Math.max(removals, additions);
  }
  return count;
}

function splitRows(
  model: SourceDiffViewerModel,
  diff: BrowserSourceDiff,
  relations: SourceRelationLookup,
  escapeHtml: (value: unknown) => string,
): string {
  const rows: string[] = [];
  let index = 0;
  while (index < model.rows.length) {
    const row = model.rows[index]!;
    if (row.changeIndex === null) {
      rows.push(`<div class="source-diff-viewer-split-row source-diff-viewer-row-context" role="row" aria-rowindex="${rows.length + 1}">
        ${splitSourceCell(
          "before",
          row.beforeIndex,
          false,
          null,
          diff,
          relations,
          escapeHtml,
        )}
        ${splitSourceCell(
          "after",
          row.afterIndex,
          false,
          null,
          diff,
          relations,
          escapeHtml,
        )}
      </div>`);
      index++;
      continue;
    }
    const changeIndex = row.changeIndex;
    const changedRows: SourceDiffViewerRow[] = [];
    while (index < model.rows.length
      && model.rows[index]!.changeIndex === changeIndex) {
      changedRows.push(model.rows[index]!);
      index++;
    }
    const removals = changedRows.filter(candidate =>
      candidate.kind === "removal");
    const additions = changedRows.filter(candidate =>
      candidate.kind === "addition");
    const count = Math.max(removals.length, additions.length);
    const changeForHighlight = model.invalidChanges.has(changeIndex)
      ? null
      : removals[0]?.change ?? additions[0]?.change ?? null;
    for (let offset = 0; offset < count; offset++) {
      const removal = removals[offset] ?? null;
      const addition = additions[offset] ?? null;
      rows.push(`<div class="source-diff-viewer-split-row source-diff-viewer-row-change" role="row" aria-rowindex="${rows.length + 1}" data-source-diff-change="${changeIndex}" tabindex="-1">
        ${splitSourceCell(
          "before",
          removal?.beforeIndex ?? null,
          true,
          changeForHighlight,
          diff,
          relations,
          escapeHtml,
        )}
        ${splitSourceCell(
          "after",
          addition?.afterIndex ?? null,
          true,
          changeForHighlight,
          diff,
          relations,
          escapeHtml,
        )}
      </div>`);
    }
  }
  return rows.join("");
}

function copyText(
  diff: BrowserSourceDiff,
  side: "before" | "after",
): string {
  const sequence = diff[side];
  const final = sequence.finalLineTerminator === "Present" ? "\n" : "";
  return `${sequence.lines.join("\n")}${final}`;
}

export function renderSourceDiffViewer(
  diff: BrowserSourceDiff,
  escapeHtml: (value: unknown) => string,
  options: SourceDiffViewerOptions = {},
): string {
  const compact = options.compact ?? false;
  const mode = options.mode ?? "unified";
  const model = sourceDiffViewerModel(diff);
  const relations = sourceRelationLookup(diff);
  const statistics = diff.statistics;
  const summary = compact
    ? ""
    : `<div class="source-diff-viewer-summary" aria-label="Source diff statistics">
      <span>${statistics.added.toLocaleString()} added</span>
      <span>${statistics.removed.toLocaleString()} removed</span>
      <span>${statistics.changedBefore.toLocaleString()} Before changed</span>
      <span>${statistics.changedAfter.toLocaleString()} After changed</span>
      <span>${statistics.movedBefore.toLocaleString()} Before moved</span>
      <span>${statistics.movedAfter.toLocaleString()} After moved</span>
    </div>`;
  const position = diff.changes.length === 0
    ? "No navigable changes"
    : `1 of ${diff.changes.length.toLocaleString()}`;
  const controls = compact
    ? ""
    : `<div class="source-diff-viewer-controls">
      <fieldset class="source-diff-viewer-modes">
        <legend>Diff layout</legend>
        <button type="button" data-source-diff-mode="unified" aria-pressed="${mode === "unified"}">Unified</button>
        <button type="button" data-source-diff-mode="side-by-side" aria-pressed="${mode === "side-by-side"}">Side by side</button>
      </fieldset>
      <div class="source-diff-viewer-navigation" aria-label="Change navigation">
        <button type="button" data-source-diff-previous disabled>Previous</button>
        <span data-source-diff-position>${position}</span>
        <button type="button" data-source-diff-next${diff.changes.length <= 1 ? " disabled" : ""}>Next</button>
      </div>
      <div class="source-diff-viewer-copy" aria-label="Copy Source">
        <button type="button" data-source-diff-copy="before">Copy Before</button>
        <button type="button" data-source-diff-copy="after">Copy After</button>
      </div>
    </div>`;
  const defects = model.defects.length === 0
    ? ""
    : `<div class="source-diff-viewer-defects" role="alert">
      <strong>Some intraline highlights could not be shown.</strong>
      <ul>${model.defects.map(defect => `<li>${escapeHtml(defect)}</li>`).join("")}</ul>
    </div>`;
  const unifiedRows = model.rows.map((row, index) => unifiedRow(
    row,
    index,
    diff,
    relations,
    model.invalidChanges,
    !compact,
    escapeHtml,
  )).join("");
  const split = compact
    ? ""
    : `<div class="member-diff-source-diff source-diff-viewer-split" role="table" aria-label="Side-by-side authored Source diff" aria-rowcount="${splitRowCount(model)}" aria-colcount="2">
      ${splitRows(model, diff, relations, escapeHtml)}
    </div>`;
  const responsiveNote = compact
    ? ""
    : '<p class="source-diff-viewer-responsive-note">Side-by-side preference retained; unified layout shown at this width.</p>';
  return `<section class="source-diff-viewer${compact ? " source-diff-viewer-compact" : ""}" data-source-diff-viewer data-mode="${mode}" tabindex="0">
    ${summary}
    ${controls}
    ${defects}
    ${responsiveNote}
    <div class="member-diff-source-diff source-diff-viewer-unified" role="table" aria-label="Unified authored Source diff" aria-rowcount="${model.rows.length}">
      ${unifiedRows}
    </div>
    ${split}
    <p class="visually-hidden" aria-live="polite" aria-atomic="true" data-source-diff-announcement></p>
  </section>`;
}

function textField(target: EventTarget | null): boolean {
  if (target === null || typeof target !== "object" || !("tagName" in target))
    return false;
  const tagName = target.tagName;
  return tagName === "INPUT"
    || tagName === "TEXTAREA"
    || ("isContentEditable" in target && target.isContentEditable === true);
}

export function bindSourceDiffViewer(
  root: ParentNode,
  diff: BrowserSourceDiff,
  binding: SourceDiffViewerBinding,
): void {
  const viewer = root.querySelector<HTMLElement>("[data-source-diff-viewer]");
  if (viewer === null) return;
  viewer.dataset.mode = binding.mode;
  let activeChange = -1;
  const position = viewer.querySelector<HTMLElement>(
    "[data-source-diff-position]",
  );
  const previous = viewer.querySelector<HTMLButtonElement>(
    "[data-source-diff-previous]",
  );
  const next = viewer.querySelector<HTMLButtonElement>(
    "[data-source-diff-next]",
  );
  const announcement = viewer.querySelector<HTMLElement>(
    "[data-source-diff-announcement]",
  );
  const updateNavigation = (): void => {
    if (position !== null) {
      position.textContent = diff.changes.length === 0
        ? "No navigable changes"
        : activeChange < 0
          ? `0 of ${diff.changes.length}`
        : `${activeChange + 1} of ${diff.changes.length}`;
    }
    if (previous !== null)
      previous.disabled = activeChange <= 0;
    if (next !== null)
      next.disabled = diff.changes.length === 0
        || activeChange >= diff.changes.length - 1;
  };
  const move = (delta: -1 | 1): void => {
    const candidate = activeChange + delta;
    if (candidate < 0 || candidate >= diff.changes.length) return;
    activeChange = candidate;
    updateNavigation();
    const rows = [...viewer.querySelectorAll<HTMLElement>(
      `[data-source-diff-change="${activeChange}"]`,
    )];
    const row = rows.find(visibleRow => visibleRow.offsetParent !== null)
      ?? rows[0]
      ?? null;
    row?.scrollIntoView({ block: "center" });
    row?.focus({ preventScroll: true });
    const change = diff.changes[activeChange]!;
    if (announcement !== null) {
      const range = (
        label: string,
        start: number,
        count: number,
      ): string => count === 0
        ? `no ${label} lines`
        : `${label} lines ${start + 1} through ${start + count}`;
      announcement.textContent =
        `${activeChange + 1} of ${diff.changes.length}. `
        + `${range("Before", change.before.start, change.before.count)}; `
        + `${range("After", change.after.start, change.after.count)}.`;
    }
  };
  previous?.addEventListener("click", () => move(-1));
  next?.addEventListener("click", () => move(1));
  viewer.addEventListener("keydown", event => {
    if (textField(event.target)) return;
    if (event.key !== "n" && event.key !== "p") return;
    event.preventDefault();
    move(event.key === "n" ? 1 : -1);
  });
  viewer.querySelectorAll<HTMLButtonElement>("[data-source-diff-mode]")
    .forEach(button => button.addEventListener("click", () => {
      const mode = button.dataset.sourceDiffMode;
      if (mode !== "unified" && mode !== "side-by-side") return;
      viewer.dataset.mode = mode;
      viewer.querySelectorAll<HTMLButtonElement>("[data-source-diff-mode]")
        .forEach(candidate => candidate.setAttribute(
          "aria-pressed",
          String(candidate.dataset.sourceDiffMode === mode),
        ));
      binding.onModeChanged(mode);
    }));
  viewer.querySelectorAll<HTMLButtonElement>("[data-source-diff-copy]")
    .forEach(button => button.addEventListener("click", () => {
      const side = button.dataset.sourceDiffCopy;
      if (side !== "before" && side !== "after") return;
      void binding.writeClipboardText(copyText(diff, side)).then(() => {
        if (announcement !== null)
          announcement.textContent = `${side === "before" ? "Before" : "After"} source copied.`;
        return undefined;
      }, () => {
        if (announcement !== null)
          announcement.textContent = `Could not copy ${side === "before" ? "Before" : "After"} source.`;
        return undefined;
      });
    }));
  updateNavigation();
}
