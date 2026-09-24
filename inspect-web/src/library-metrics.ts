import type { BrowserLibraryMetrics } from "./facades/inspect-web-analysis.d.ts";

const TREEMAP_WIDTH = 900;
const TREEMAP_HEIGHT = 360;
const TREEMAP_LIMIT = 72;
const RELATIONSHIP_WIDTH = 900;
const RELATIONSHIP_HEIGHT = 320;
const RELATIONSHIP_LIMIT = 16;
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

function metricLabel(metric: string): string {
  return metric
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/^./, character => character.toUpperCase());
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
    const tooltip = `${rectangle.item.typeDisplay} · ${formatNumber(rectangle.item.bodyCount)} bodies · ${formatNumber(rectangle.item.instructionCount)} instructions · average complexity ${density.toFixed(1)}`;
    const labelHtml = rectangle.width > 86 && rectangle.height > 28
      ? `<text class="metrics-treemap-label" x="${rectangle.x + 7}" y="${rectangle.y + 17}">${escapeHtml(label)}</text>`
      : "";
    return `<g class="metrics-treemap-cell"><title>${escapeHtml(tooltip)}</title><rect x="${rectangle.x.toFixed(2)}" y="${rectangle.y.toFixed(2)}" width="${rectangle.width.toFixed(2)}" height="${rectangle.height.toFixed(2)}" fill="hsl(265 65% ${lightness}%)"></rect>${labelHtml}</g>`;
  }).join("");
  const omittedNote = omitted.length
    ? ` Top ${TREEMAP_LIMIT} types are shown individually; ${formatNumber(omitted.length)} smaller types are grouped as Other types.`
    : "";
  return `<section class="document-section metrics-visual-section">
    <div class="metrics-visual-copy"><h2>Complexity Explorer</h2><p>This map shows the library's implementation shape. Area shows instruction volume; color shows average normal-flow complexity. It is structural evidence, not a quality score.</p></div>
    <svg class="metrics-treemap" viewBox="0 0 ${TREEMAP_WIDTH} ${TREEMAP_HEIGHT}" role="img" aria-label="Complexity Explorer implementation treemap">${cells}</svg>
    <p class="metrics-visual-caption">${formatNumber(source.length)} types · Scroll the page to inspect the map.${omittedNote}</p>
  </section>`;
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
    .slice(0, RELATIONSHIP_LIMIT)
    .map(([typeKey, typeDegree]) => ({
      typeKey,
      typeDisplay: displays.get(typeKey) ?? typeKey,
      typeDegree,
    }));
  const selected = new Set(types.map(type => type.typeKey));
  const edges = relationships.filter(relationship =>
    selected.has(relationship.sourceTypeKey)
    && selected.has(relationship.targetTypeKey));
  const positions = new Map(
    types.map((type, index) => [
      type.typeKey,
      32 + index * (RELATIONSHIP_WIDTH - 64) / Math.max(1, types.length - 1),
    ]),
  );
  const baseline = RELATIONSHIP_HEIGHT - 58;
  const arcs = edges.map((edge, index) => {
    const source = positions.get(edge.sourceTypeKey) ?? 0;
    const target = positions.get(edge.targetTypeKey) ?? 0;
    const bend = 34 + Math.abs(target - source) * .36 + (index % 4) * 10;
    const color = RELATIONSHIP_COLORS[index % RELATIONSHIP_COLORS.length];
    const tooltip = `${edge.sourceTypeDisplay} calls ${edge.targetTypeDisplay} at ${formatNumber(edge.callSiteCount)} retained sites`;
    return `<path class="metrics-relationship-edge" d="M ${source.toFixed(1)} ${baseline} C ${source.toFixed(1)} ${(baseline - bend).toFixed(1)}, ${target.toFixed(1)} ${(baseline - bend).toFixed(1)}, ${target.toFixed(1)} ${baseline}" stroke="${color}" stroke-width="${Math.min(8, 1.5 + Math.log2(edge.callSiteCount + 1))}"><title>${escapeHtml(tooltip)}</title></path>`;
  }).join("");
  const nodes = types.map(type => {
    const x = positions.get(type.typeKey) ?? 0;
    return `<g class="metrics-relationship-node"><circle cx="${x.toFixed(1)}" cy="${baseline}" r="4"></circle><text x="${x.toFixed(1)}" y="${baseline + 17}" transform="rotate(52 ${x.toFixed(1)} ${baseline + 17})">${escapeHtml(shortTypeName(type.typeDisplay))}</text><title>${escapeHtml(type.typeDisplay)} · ${formatNumber(type.typeDegree)} connected types</title></g>`;
  }).join("");
  return `<section class="document-section metrics-visual-section">
    <div class="metrics-visual-copy"><h2>Relationship Crossing</h2><p>The most entangled types are placed on one line; arcs reveal how often their implementations cross. Each color follows one retained relationship so dense crossings remain separable.</p></div>
    <svg class="metrics-relationship-crossing" viewBox="0 0 ${RELATIONSHIP_WIDTH} ${RELATIONSHIP_HEIGHT}" role="img" aria-label="Relationship Crossing diagram">${arcs}${nodes}</svg>
    <p class="metrics-visual-caption">${formatNumber(types.length)} most connected types · ${formatNumber(edges.length)} retained relationships · Hover an arc or type for evidence.</p>
  </section>`;
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
      const distributions = resolved.distributions ?? [];
      status = `${(population?.completeProfileCount ?? 0).toLocaleString()} complete bodies`;
      const rows = distributions.map(distribution => `<tr>
        <th scope="row">${escapeHtml(metricLabel(distribution.metric))}</th>
        <td>${distribution.minimum ?? "\u2014"}</td>
        <td>${distribution.p50 ?? "\u2014"}</td>
        <td>${distribution.p90 ?? "\u2014"}</td>
        <td>${distribution.maximum ?? "\u2014"}</td>
      </tr>`).join("");
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
          <details class="metrics-detail"><summary>Detailed distributions</summary><table class="metrics-table"><thead><tr><th>Metric</th><th>Min</th><th>P50</th><th>P90</th><th>Max</th></tr></thead><tbody>${rows}</tbody></table></details>
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
