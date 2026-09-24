import type { BrowserLibraryMetrics } from "./facades/inspect-web-analysis.d.ts";

const TREEMAP_WIDTH = 900;
const TREEMAP_HEIGHT = 360;
const TREEMAP_LIMIT = 72;
const RELATIONSHIP_WIDTH = 900;
const RELATIONSHIP_HEIGHT = 480;
const RELATIONSHIP_LABEL_SPACE = 190;
const RECIPROCAL_BEND_SEPARATION = 16;
const RELATIONSHIP_COLORS = [
  "#b9aaee", "#7ed8dc", "#9cc8f1", "#e5b567", "#d98a70",
  "#8ebb76", "#c795e9", "#87aeca",
];

export interface LibraryMetricsOptions {
  libraryName: string;
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  requireLibrary: boolean;
  pickerHtml: string;
  fresh: boolean;
  loading: boolean;
  error: string;
  data: BrowserLibraryMetrics | null;
  escapeHtml: (value: unknown) => string;
}

export interface LibraryMetricsInteractionActions {
  activateType: (typeKey: string) => void;
}

function shortTypeName(typeId: string): string {
  const generic = typeId.indexOf("<");
  const head = generic < 0 ? typeId : typeId.slice(0, generic);
  const tail = generic < 0 ? "" : typeId.slice(generic);
  const dot = head.lastIndexOf(".");
  return `${dot < 0 ? head : head.slice(dot + 1)}${tail}`;
}

function formatNumber(value: number): string {
  return value.toLocaleString();
}

function formatCount(
  value: number,
  singular: string,
  plural = `${singular}s`,
): string {
  return `${formatNumber(value)} ${value === 1 ? singular : plural}`;
}

interface TreemapItem {
  readonly typeKey: string;
  readonly typeDisplay: string;
  readonly namespace: string;
  readonly name: string;
  readonly bodyCount: number;
  readonly instructionCount: number;
  readonly complexityTotal: number;
}

interface TreemapRectangle {
  readonly item: TreemapItem;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

function layoutTreemap(
  items: readonly TreemapItem[],
  x: number,
  y: number,
  width: number,
  height: number,
): TreemapRectangle[] {
  if (!items.length || width <= 0 || height <= 0) return [];
  if (items.length === 1) {
    const item = items[0];
    return item ? [{ item, x, y, width, height }] : [];
  }
  const total = items.reduce(
    (sum, item) => sum + Math.max(1, item.instructionCount),
    0,
  );
  const target = total / 2;
  let splitIndex = 1;
  let accumulated = 0;
  for (let index = 0; index < items.length - 1; index++) {
    const item = items[index];
    if (!item) continue;
    accumulated += Math.max(1, item.instructionCount);
    if (accumulated >= target) {
      splitIndex = index + 1;
      break;
    }
  }
  const first = items.slice(0, splitIndex);
  const second = items.slice(splitIndex);
  const firstTotal = first.reduce(
    (sum, item) => sum + Math.max(1, item.instructionCount),
    0,
  );
  const ratio = firstTotal / total;
  if (width >= height) {
    const firstWidth = width * ratio;
    return [
      ...layoutTreemap(first, x, y, firstWidth, height),
      ...layoutTreemap(second, x + firstWidth, y, width - firstWidth, height),
    ];
  }
  const firstHeight = height * ratio;
  return [
    ...layoutTreemap(first, x, y, width, firstHeight),
    ...layoutTreemap(second, x, y + firstHeight, width, height - firstHeight),
  ];
}

function renderTreemap(
  data: BrowserLibraryMetrics,
  escapeHtml: (value: unknown) => string,
): string {
  const source = [...(data.typeSummaries ?? [])]
    .sort((left, right) => right.instructionCount - left.instructionCount);
  if (!source.length) {
    return `<section class="document-section empty-document"><h2>No type-level implementation evidence</h2><p>The library report did not issue type summaries for this population.</p></section>`;
  }
  const visible = source.slice(0, TREEMAP_LIMIT);
  const omitted = source.slice(TREEMAP_LIMIT);
  if (omitted.length) {
    visible.push({
      typeKey: "other-types",
      typeDisplay: "Other types",
      namespace: "",
      name: "Other types",
      bodyCount: omitted.reduce((sum, item) => sum + item.bodyCount, 0),
      instructionCount: omitted.reduce(
        (sum, item) => sum + item.instructionCount,
        0,
      ),
      complexityTotal: omitted.reduce(
        (sum, item) => sum + item.complexityTotal,
        0,
      ),
      loopCount: omitted.reduce((sum, item) => sum + item.loopCount, 0),
      directCallCount: omitted.reduce(
        (sum, item) => sum + item.directCallCount,
        0,
      ),
      allocationCount: omitted.reduce(
        (sum, item) => sum + item.allocationCount,
        0,
      ),
    });
  }
  const maximumDensity = Math.max(
    1,
    ...visible.map(item => item.complexityTotal / Math.max(1, item.bodyCount)),
  );
  const rectangles = layoutTreemap(
    visible,
    0,
    0,
    TREEMAP_WIDTH,
    TREEMAP_HEIGHT,
  );
  const cells = rectangles.map(rectangle => {
    const density = rectangle.item.complexityTotal
      / Math.max(1, rectangle.item.bodyCount);
    const lightness = 78 - Math.round(34 * density / maximumDensity);
    const label = shortTypeName(rectangle.item.typeDisplay);
    const tooltip = `${rectangle.item.typeDisplay} · ${formatCount(rectangle.item.bodyCount, "body", "bodies")} · ${formatCount(rectangle.item.instructionCount, "instruction")} · average complexity ${density.toFixed(1)}`;
    const labelHtml = rectangle.width > 86 && rectangle.height > 28
      ? `<text class="metrics-treemap-label" x="${rectangle.x + 7}" y="${rectangle.y + 17}">${escapeHtml(label)}</text>`
      : "";
    const aggregate = rectangle.item.typeKey === "other-types";
    const interaction = aggregate
      ? ` role="img" aria-label="${escapeHtml(tooltip)}"`
      : ` data-metrics-type-key="${escapeHtml(rectangle.item.typeKey)}" tabindex="0" role="button" aria-label="${escapeHtml(`Open ${rectangle.item.typeDisplay}. ${tooltip}`)}"`;
    return `<g class="metrics-treemap-cell" data-metrics-treemap-cell data-metrics-evidence="${escapeHtml(tooltip)}"${interaction}><title>${escapeHtml(tooltip)}</title><rect x="${rectangle.x}" y="${rectangle.y}" width="${rectangle.width}" height="${rectangle.height}" fill="hsl(265 65% ${lightness}%)"></rect>${labelHtml}</g>`;
  }).join("");
  const omittedNote = omitted.length
    ? ` Top ${TREEMAP_LIMIT} types are shown individually; ${formatNumber(omitted.length)} smaller types are grouped as Other types.`
    : "";
  return `<section class="document-section metrics-visual-section">
    <div class="metrics-visual-copy"><h2>Complexity Explorer</h2><p>This map shows the library's implementation shape. Area shows instruction volume; color shows average normal-flow complexity. It is structural evidence, not a quality score.</p></div>
    <svg class="metrics-treemap" viewBox="0 0 ${TREEMAP_WIDTH} ${TREEMAP_HEIGHT}" role="group" aria-label="Complexity Explorer implementation treemap">${cells}</svg>
    <p class="metrics-treemap-evidence" data-metrics-treemap-evidence data-default-text="Hover or focus a type for implementation evidence. Select a type to open it.">Hover or focus a type for implementation evidence. Select a type to open it.</p>
    <p class="metrics-visual-caption">${formatNumber(source.length)} types · Scroll the page to inspect the map.${omittedNote}</p>
  </section>`;
}

function relationshipDirectionKey(source: string, target: string): string {
  return JSON.stringify([source, target]);
}

function renderRelationshipCrossing(
  data: BrowserLibraryMetrics,
  escapeHtml: (value: unknown) => string,
): string {
  const relationships = [...(data.entangledRelationships ?? [])];
  if (!relationships.length) {
    return `<section class="document-section empty-document"><h2>No internal crossings found</h2><p>The available call evidence did not produce a bounded set of cross-type relationships.</p></section>`;
  }
  const degree = new Map<string, number>();
  const displays = new Map<string, string>();
  for (const relationship of relationships) {
    displays.set(relationship.sourceTypeKey, relationship.sourceTypeDisplay);
    displays.set(relationship.targetTypeKey, relationship.targetTypeDisplay);
    degree.set(relationship.sourceTypeKey, Math.max(
      degree.get(relationship.sourceTypeKey) ?? 0,
      relationship.sourceDegree,
    ));
    degree.set(relationship.targetTypeKey, Math.max(
      degree.get(relationship.targetTypeKey) ?? 0,
      relationship.targetDegree,
    ));
  }
  const types = [...degree.entries()]
    .sort((left, right) => right[1] - left[1] || left[0].localeCompare(right[0]))
    .map(([typeKey, typeDegree]) => ({
      typeKey,
      typeDisplay: displays.get(typeKey) ?? typeKey,
      typeDegree,
    }));
  const selected = new Set(types.map(type => type.typeKey));
  const edges = relationships.filter(relationship =>
    selected.has(relationship.sourceTypeKey)
    && selected.has(relationship.targetTypeKey));
  const directions = new Set(edges.map(edge =>
    relationshipDirectionKey(edge.sourceTypeKey, edge.targetTypeKey)));
  const positions = new Map(
    types.map((type, index) => [
      type.typeKey,
      32 + index * (RELATIONSHIP_WIDTH - 64) / Math.max(1, types.length - 1),
    ]),
  );
  const baseline = RELATIONSHIP_HEIGHT - RELATIONSHIP_LABEL_SPACE;
  const arcs = edges.map((edge, index) => {
    const source = positions.get(edge.sourceTypeKey) ?? 0;
    const target = positions.get(edge.targetTypeKey) ?? 0;
    const reciprocal = directions.has(relationshipDirectionKey(
      edge.targetTypeKey,
      edge.sourceTypeKey,
    ));
    const bendOffset = reciprocal
      ? (edge.sourceTypeKey < edge.targetTypeKey
          ? 0
          : RECIPROCAL_BEND_SEPARATION)
      : (index % 4) * 10;
    const bend = 34 + Math.abs(target - source) * .36 + bendOffset;
    const color = RELATIONSHIP_COLORS[index % RELATIONSHIP_COLORS.length];
    const tooltip = `${edge.sourceTypeDisplay} calls ${edge.targetTypeDisplay} at ${formatCount(edge.callSiteCount, "retained site")}`;
    return `<path class="metrics-relationship-edge" d="M ${source.toFixed(1)} ${baseline} C ${source.toFixed(1)} ${(baseline - bend).toFixed(1)}, ${target.toFixed(1)} ${(baseline - bend).toFixed(1)}, ${target.toFixed(1)} ${baseline}" stroke="${color}" stroke-width="${Math.min(8, 1.5 + Math.log2(edge.callSiteCount + 1))}"><title>${escapeHtml(tooltip)}</title></path>`;
  }).join("");
  const nodes = types.map(type => {
    const x = positions.get(type.typeKey) ?? 0;
    const onRight = x > RELATIONSHIP_WIDTH / 2;
    const angle = onRight ? -52 : 52;
    const anchor = onRight ? "end" : "start";
    return `<g class="metrics-relationship-node"><circle cx="${x.toFixed(1)}" cy="${baseline}" r="4"></circle><text x="${x.toFixed(1)}" y="${baseline + 17}" text-anchor="${anchor}" transform="rotate(${angle} ${x.toFixed(1)} ${baseline + 17})">${escapeHtml(shortTypeName(type.typeDisplay))}</text><title>${escapeHtml(type.typeDisplay)} · ${formatNumber(type.typeDegree)} connected types</title></g>`;
  }).join("");
  return `<section class="document-section metrics-visual-section">
    <div class="metrics-visual-copy"><h2>Relationship Crossing</h2><p>The most entangled types are placed on one line; arcs reveal how often their implementations cross. Each color follows one retained relationship so dense crossings remain separable.</p></div>
    <svg class="metrics-relationship-crossing" viewBox="0 0 ${RELATIONSHIP_WIDTH} ${RELATIONSHIP_HEIGHT}" role="img" aria-label="Relationship Crossing diagram">${arcs}${nodes}</svg>
    <p class="metrics-visual-caption">${formatNumber(types.length)} most connected types · ${formatNumber(edges.length)} retained relationships · Hover an arc or type for evidence.</p>
  </section>`;
}

export function bindLibraryMetricsInteractions(
  root: ParentNode,
  actions: LibraryMetricsInteractionActions,
): void {
  const evidence = root.querySelector<HTMLElement>(
    "[data-metrics-treemap-evidence]",
  );
  const defaultEvidence = evidence?.dataset.defaultText ?? "";
  const showEvidence = (cell: SVGGElement) => {
    if (evidence) evidence.textContent = cell.dataset.metricsEvidence ?? "";
  };
  const resetEvidence = () => {
    if (evidence) evidence.textContent = defaultEvidence;
  };

  for (const cell of root.querySelectorAll<SVGGElement>(
    "[data-metrics-treemap-cell]",
  )) {
    cell.addEventListener("pointerenter", () => showEvidence(cell));
    cell.addEventListener("pointerleave", () => {
      if (cell.ownerDocument.activeElement !== cell) resetEvidence();
    });
    cell.addEventListener("focus", () => showEvidence(cell));
    cell.addEventListener("blur", resetEvidence);

    const typeKey = cell.dataset.metricsTypeKey;
    if (!typeKey) continue;
    const activate = () => actions.activateType(typeKey);
    cell.addEventListener("click", activate);
    cell.addEventListener("keydown", event => {
      if (event.key !== "Enter" && event.key !== " ") return;
      event.preventDefault();
      activate();
    });
  }
}

export function renderLibraryMetricsSurface(
  options: LibraryMetricsOptions,
): string {
  const {
    libraryName, assemblyIdentity, assetPath, coordinate,
    requireLibrary, pickerHtml, fresh, loading, error, data, escapeHtml,
  } = options;
  let status: string;
  let content: string;
  if (requireLibrary) {
    status = "Select a library";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Pick a library to measure</h2><p>Choose a .NET platform library above to summarize compiled implementation metrics.</p></section>`;
  } else if (loading && fresh) {
    status = "Measuring library\u2026";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Measuring library&hellip;</h2><p>Computing Research-owned structural distributions across this library's method bodies.</p></section>`;
  } else if (fresh && error) {
    status = "Metrics failed";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Metrics failed</h2><p>${escapeHtml(error)}</p></section>`;
  } else {
    const resolved = fresh ? data : null;
    if (!resolved) {
      status = "Loading\u2026";
      content = `<section class="document-section empty-document"><span class="loader"></span><h2>Loading&hellip;</h2></section>`;
    } else if (resolved.outcome !== "available") {
      status = resolved.outcome === "failed" ? "Metrics failed" : "Metrics unavailable";
      content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>${escapeHtml(status)}</h2><p>${escapeHtml(resolved.failure || "The Research document could not be produced.")}</p></section>`;
    } else {
      const population = resolved.population;
      status = `${(population?.completeProfileCount ?? 0).toLocaleString()} complete bodies`;
      const coverageGap = population
        && population.profiledPhysicalEvidenceBodyCount
          < population.physicalEvidenceBodyCount;
      const incomplete = population && (
        coverageGap || population.incompleteProfileCount > 0)
        ? `<section class="document-section metadata-warning"><strong>&#x26A0; Metrics are qualified</strong><p>${
          coverageGap
            ? `${population.profiledPhysicalEvidenceBodyCount.toLocaleString()} of ${population.physicalEvidenceBodyCount.toLocaleString()} physical bodies were profiled.`
            : ""
        }${
          population.incompleteProfileCount > 0
            ? ` ${population.incompleteProfileCount.toLocaleString()} profiled bodies have incomplete metrics.`
            : ""
        }</p>${
          resolved.diagnostics.length > 0
            ? `<ul>${resolved.diagnostics.map(diagnostic =>
              `<li>${escapeHtml(diagnostic)}</li>`).join("")}</ul>`
            : ""
        }</section>`
        : "";
      content = `${incomplete}
        ${renderTreemap(resolved, escapeHtml)}
        ${renderRelationshipCrossing(resolved, escapeHtml)}
        <section class="document-section">
          <p>Compiled IL metrics for <strong>${escapeHtml(libraryName)}</strong>. These are structural implementation measures, not authored-source complexity.</p>
        </section>`;
    }
  }
  const identity = assetPath ? `${assetPath} \u00b7 ${assemblyIdentity}` : assemblyIdentity;
  return `<section class="library-analysis-surface${pickerHtml ? " library-analysis-with-controls" : ""}" aria-labelledby="library-metrics-title">
    <header class="api-surface-head">
      <h1 id="library-metrics-title">Complexity Explorer</h1>
      <p title="${escapeHtml(status)}">${escapeHtml(status)}</p>
    </header>
    ${pickerHtml ? `<section class="library-analysis-controls" aria-label="Metrics library">${pickerHtml}</section>` : ""}
    <div class="library-analysis-scroll">${content}</div>
    <footer class="metadata-surface-footer">
      <span title="${escapeHtml(identity)}">${escapeHtml(identity)}</span>
      <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
    </footer>
  </section>`;
}
