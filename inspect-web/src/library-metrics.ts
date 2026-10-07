import type {
  BrowserLibraryDependencyStructure,
  BrowserLibraryMetrics,
} from "./facades/inspect-web-analysis.d.ts";
import { renderAnalysisInspector } from "./analysis-inspector.ts";

const TREEMAP_WIDTH = 900;
const TREEMAP_HEIGHT = 360;
const TREEMAP_LIMIT = 72;
const RELATIONSHIP_WIDTH = 900;
const RELATIONSHIP_HEIGHT = 480;
const RELATIONSHIP_LABEL_SPACE = 190;
const RECIPROCAL_BEND_SEPARATION = 16;
const DEPENDENCY_MIN_WIDTH = 900;
const DEPENDENCY_COLUMN_WIDTH = 210;
const DEPENDENCY_NODE_WIDTH = 168;
const DEPENDENCY_NODE_HEIGHT = 42;
const DEPENDENCY_ROW_HEIGHT = 66;
const DEPENDENCY_SIDE_GUTTER = 100;
const RELATIONSHIP_INITIAL_LIMIT = 24;
const RELATIONSHIP_COLORS = [
  "#b9aaee", "#7ed8dc", "#9cc8f1", "#e5b567", "#d98a70",
  "#8ebb76", "#c795e9", "#87aeca",
];

export interface LibraryAnalysisOptions {
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
  dependencyFresh: boolean;
  dependencyLoading: boolean;
  dependencyError: string;
  dependencyData: BrowserLibraryDependencyStructure | null;
  relationshipState?: LibraryMetricsRelationshipState | null;
  dependencyState?: LibraryDependencyStructureState | null;
  escapeHtml: (value: unknown) => string;
}

export interface LibraryMetricsRelationshipState {
  readonly visibleCount: number;
  readonly selectedSourceTypeKey: string | null;
  readonly selectedTargetTypeKey: string | null;
}

export interface LibraryDependencyStructureState {
  readonly includeGlobalNamespace: boolean;
  readonly selectedSourceNamespace: string | null;
  readonly selectedTargetNamespace: string | null;
}

export interface LibraryAnalysisInteractionActions {
  activateType: (typeKey: string) => void;
  loadDependencyStructure: () => void;
  updateRelationshipState: (state: LibraryMetricsRelationshipState) => void;
  updateDependencyState: (state: LibraryDependencyStructureState) => void;
}

export type LibraryMetricsMode = "complexity" | "relationships";

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
    .filter(item => item.bodyCount > 0)
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

function initialRelationshipCount(total: number): number {
  return Math.min(RELATIONSHIP_INITIAL_LIMIT, Math.ceil(total / 2));
}

function relationshipLimitLevels(total: number): number[] {
  const initial = initialRelationshipCount(total);
  return [...new Set([
    Math.ceil(total / 4),
    Math.ceil(total / 2),
    Math.ceil(total * 3 / 4),
    total,
    initial,
  ])].sort((left, right) => left - right);
}

function renderRelationshipCrossing(
  data: BrowserLibraryMetrics,
  escapeHtml: (value: unknown) => string,
  state: LibraryMetricsRelationshipState | null = null,
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
  const initialCount = initialRelationshipCount(edges.length);
  const limitLevels = relationshipLimitLevels(edges.length);
  const visibleCount = state && limitLevels.includes(state.visibleCount)
    ? state.visibleCount
    : initialCount;
  const visibleLevel = limitLevels.indexOf(visibleCount);
  const selectedIndex = state
    ? edges.findIndex(edge =>
      edge.sourceTypeKey === state.selectedSourceTypeKey
      && edge.targetTypeKey === state.selectedTargetTypeKey)
    : -1;
  const selectedRank = selectedIndex + 1;
  const selectedEdge = selectedRank > 0 && selectedRank <= visibleCount
    ? edges[selectedIndex] ?? null
    : null;
  const positions = new Map(
    types.map((type, index) => [
      type.typeKey,
      32 + index * (RELATIONSHIP_WIDTH - 64) / Math.max(1, types.length - 1),
    ]),
  );
  const baseline = RELATIONSHIP_HEIGHT - RELATIONSHIP_LABEL_SPACE;
  const arcs = edges.map((edge, index) => {
    const rank = index + 1;
    const hidden = rank > visibleCount ? " hidden" : "";
    const isSelected = edge === selectedEdge;
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
    const path = `M ${source.toFixed(1)} ${baseline} C ${source.toFixed(1)} ${(baseline - bend).toFixed(1)}, ${target.toFixed(1)} ${(baseline - bend).toFixed(1)}, ${target.toFixed(1)} ${baseline}`;
    const strokeWidth = Math.min(
      8,
      1.5 + Math.log2(edge.callSiteCount + 1),
    );
    return `<path class="metrics-relationship-edge" d="${path}" data-metrics-relationship data-relationship-rank="${rank}" data-source-type-key="${escapeHtml(edge.sourceTypeKey)}" data-source-type-display="${escapeHtml(edge.sourceTypeDisplay)}" data-target-type-key="${escapeHtml(edge.targetTypeKey)}" data-target-type-display="${escapeHtml(edge.targetTypeDisplay)}" data-call-site-count="${edge.callSiteCount}" stroke="transparent" stroke-width="${Math.max(14, strokeWidth + 8)}" tabindex="0" role="button" aria-pressed="${isSelected}" aria-label="${escapeHtml(`Inspect relationship ${rank} of ${edges.length}. ${tooltip}`)}"${hidden}><title>${escapeHtml(tooltip)}</title></path><path class="metrics-relationship-line" d="${path}" stroke="${color}" stroke-width="${strokeWidth}" aria-hidden="true"${hidden}></path>`;
  }).join("");
  const nodes = types.map(type => {
    const x = positions.get(type.typeKey) ?? 0;
    const onRight = x > RELATIONSHIP_WIDTH / 2;
    const angle = onRight ? -52 : 52;
    const anchor = onRight ? "end" : "start";
    return `<g class="metrics-relationship-node"><circle cx="${x.toFixed(1)}" cy="${baseline}" r="4"></circle><text x="${x.toFixed(1)}" y="${baseline + 17}" text-anchor="${anchor}" transform="rotate(${angle} ${x.toFixed(1)} ${baseline + 17})">${escapeHtml(shortTypeName(type.typeDisplay))}</text><title>${escapeHtml(type.typeDisplay)} · ${formatNumber(type.typeDegree)} connected types</title></g>`;
  }).join("");
  const limitControl = limitLevels.length > 1
    ? `<label class="metrics-relationship-limit">
        <span>Visible arcs <output data-metrics-relationship-limit-output>${formatNumber(visibleCount)} / ${formatNumber(edges.length)}</output></span>
        <input type="range" min="0" max="${limitLevels.length - 1}" value="${visibleLevel}" step="1" data-metrics-relationship-limit data-limit-levels="${limitLevels.join(",")}" aria-label="Visible relationship arcs" aria-valuetext="${formatNumber(visibleCount)} of ${formatNumber(edges.length)} arcs">
      </label>`
    : "";
  const detailEmptyHidden = selectedEdge ? " hidden" : "";
  const detailSelectionHidden = selectedEdge ? "" : " hidden";
  const selectedSourceKey = selectedEdge?.sourceTypeKey ?? "";
  const selectedSourceDisplay = selectedEdge?.sourceTypeDisplay ?? "";
  const selectedTargetKey = selectedEdge?.targetTypeKey ?? "";
  const selectedTargetDisplay = selectedEdge?.targetTypeDisplay ?? "";
  const selectedDepth = selectedEdge
    ? formatCount(selectedEdge.callSiteCount, "retained call site")
    : "";
  const selectedRankText = selectedEdge
    ? `${formatNumber(selectedRank)}/${formatNumber(edges.length)} most connected`
    : "";
  return `<section class="document-section metrics-visual-section">
    <div class="metrics-relationship-heading">
      <div class="metrics-visual-copy"><h2>Relationship Crossing</h2><p>The most entangled types are placed on one line; arcs reveal how often their implementations cross. Each color follows one retained relationship so dense crossings remain separable.</p></div>
      ${limitControl}
    </div>
    <svg class="metrics-relationship-crossing" viewBox="0 0 ${RELATIONSHIP_WIDTH} ${RELATIONSHIP_HEIGHT}" role="group" aria-label="Relationship Crossing diagram">${arcs}${nodes}</svg>
    <div class="metrics-relationship-detail" data-metrics-relationship-detail aria-live="polite">
      <p class="metrics-relationship-detail-empty" data-metrics-relationship-detail-empty${detailEmptyHidden}>Select an arc to inspect its endpoint types and retained depth.</p>
      <div class="metrics-relationship-detail-selection" data-metrics-relationship-detail-selection${detailSelectionHidden}>
        <div>
          <p class="metrics-relationship-detail-label">Selected relationship</p>
          <div class="metrics-relationship-route">
            <button type="button" class="metrics-relationship-type-link" data-metrics-relationship-source data-metrics-type-key="${escapeHtml(selectedSourceKey)}" aria-label="${escapeHtml(selectedEdge ? `Open ${selectedSourceDisplay}` : "")}">${escapeHtml(selectedSourceDisplay)}</button>
            <span class="metrics-relationship-arrow" aria-hidden="true">&rarr;</span>
            <button type="button" class="metrics-relationship-type-link" data-metrics-relationship-target data-metrics-type-key="${escapeHtml(selectedTargetKey)}" aria-label="${escapeHtml(selectedEdge ? `Open ${selectedTargetDisplay}` : "")}">${escapeHtml(selectedTargetDisplay)}</button>
          </div>
        </div>
        <div class="metrics-relationship-measures">
          <div class="metrics-relationship-measure">
            <span>Rank</span>
            <strong data-metrics-relationship-rank>${escapeHtml(selectedRankText)}</strong>
          </div>
          <div class="metrics-relationship-measure">
            <span>Relationship depth</span>
            <strong data-metrics-relationship-depth>${escapeHtml(selectedDepth)}</strong>
          </div>
        </div>
      </div>
    </div>
    <p class="metrics-visual-caption">${formatNumber(types.length)} most connected types · Showing top <span data-metrics-relationship-visible-count>${formatNumber(visibleCount)}</span> of ${formatNumber(edges.length)} retained relationships · Select an arc for details.</p>
  </section>`;
}

interface DependencyNodePosition {
  readonly x: number;
  readonly y: number;
}

function dependencyNamespaceLabel(namespace: string): string {
  if (!namespace) return "(global)";
  return namespace.length <= 25
    ? namespace
    : `\u2026${namespace.slice(-24)}`;
}

function renderDependencyStructure(
  dependency: BrowserLibraryDependencyStructure,
  escapeHtml: (value: unknown) => string,
  state?: LibraryDependencyStructureState | null,
): string {
  if (dependency.outcome !== "available") {
    const status = dependency.outcome === "failed"
      ? "Dependency structure failed"
      : "Dependency structure unavailable";
    return `<section class="document-section empty-document"><h2>${status}</h2><p>${escapeHtml(dependency.failure || "The dependency structure document could not be produced.")}</p></section>`;
  }
  if (!dependency.namespaces.length) {
    return `<section class="document-section empty-document"><h2>No namespace dependencies found</h2><p>The available call evidence did not issue any namespace nodes for this library.</p></section>`;
  }

  const includeGlobalNamespace = state?.includeGlobalNamespace ?? false;
  const globalNamespaces = new Set(dependency.namespaces
    .filter(node => node.isGlobalNamespace)
    .map(node => node.namespace));
  const visibleNamespaces = includeGlobalNamespace
    ? dependency.namespaces
    : dependency.namespaces.filter(node => !node.isGlobalNamespace);
  const visibleEdges = includeGlobalNamespace
    ? dependency.namespaceEdges
    : dependency.namespaceEdges.filter(edge =>
      !globalNamespaces.has(edge.sourceNamespace)
      && !globalNamespaces.has(edge.targetNamespace));
  const visibleCycleIndexes = new Set(dependency.cycles.flatMap(
    (cycle, index) =>
      includeGlobalNamespace
      || cycle.namespaces.every(namespaceName =>
        !globalNamespaces.has(namespaceName))
        ? [index]
        : [],
  ));
  const visibleCycles = dependency.cycles.filter((_, index) =>
    visibleCycleIndexes.has(index));
  const hiddenGlobalEdgeCount =
    dependency.namespaceEdges.length - visibleEdges.length;
  const hiddenGlobalCycleCount =
    dependency.cycles.length - visibleCycles.length;
  const levelValues = [...new Set(
    visibleNamespaces.map(node => node.level),
  )].sort((left, right) => left - right);
  const nodesByLevel = new Map(levelValues.map(level => [
    level,
    visibleNamespaces
      .filter(node => node.level === level)
      .sort((left, right) => left.namespace.localeCompare(right.namespace)),
  ]));
  const maximumRows = Math.max(
    1,
    ...[...nodesByLevel.values()].map(nodes => nodes.length),
  );
  const width = Math.max(
    DEPENDENCY_MIN_WIDTH,
    2 * DEPENDENCY_SIDE_GUTTER
      + levelValues.length * DEPENDENCY_COLUMN_WIDTH,
  );
  const height = Math.max(300, 76 + maximumRows * DEPENDENCY_ROW_HEIGHT);
  const columnSpacing = (width - 2 * DEPENDENCY_SIDE_GUTTER
    - DEPENDENCY_NODE_WIDTH)
    / Math.max(1, levelValues.length - 1);
  const positions = new Map<string, DependencyNodePosition>();
  levelValues.forEach((level, column) => {
    const nodes = nodesByLevel.get(level) ?? [];
    nodes.forEach((node, row) => {
      positions.set(node.namespace, {
        x: DEPENDENCY_SIDE_GUTTER + column * columnSpacing,
        y: 54 + row * DEPENDENCY_ROW_HEIGHT,
      });
    });
  });

  const columns = levelValues.map((level, index) => {
    const x = DEPENDENCY_SIDE_GUTTER + index * columnSpacing
      + DEPENDENCY_NODE_WIDTH / 2;
    return `<text class="metrics-dependency-level" x="${x.toFixed(1)}" y="26" text-anchor="middle">Level ${formatNumber(level)}</text>`;
  }).join("");
  const directions = new Set(visibleEdges.map(edge =>
    `${edge.sourceNamespace}\u0000${edge.targetNamespace}`));
  const selectedIndex = state
    ? visibleEdges.findIndex(edge =>
      edge.sourceNamespace === state.selectedSourceNamespace
      && edge.targetNamespace === state.selectedTargetNamespace)
    : -1;
  const edgePaths = visibleEdges.map((edge, index) => {
    const source = positions.get(edge.sourceNamespace);
    const target = positions.get(edge.targetNamespace);
    if (!source || !target) return "";
    const sourceY = source.y + DEPENDENCY_NODE_HEIGHT / 2;
    const targetY = target.y + DEPENDENCY_NODE_HEIGHT / 2;
    const sameColumn = Math.abs(source.x - target.x) < 1;
    let sourceX: number;
    let targetX: number;
    let controlX: number;
    if (sameColumn) {
      const reciprocal = directions.has(
        `${edge.targetNamespace}\u0000${edge.sourceNamespace}`,
      );
      const routeLeft = reciprocal &&
        edge.sourceNamespace.localeCompare(edge.targetNamespace) > 0;
      sourceX = source.x + (routeLeft ? 0 : DEPENDENCY_NODE_WIDTH);
      targetX = target.x + (routeLeft ? 0 : DEPENDENCY_NODE_WIDTH);
      controlX = sourceX + (routeLeft ? -1 : 1) *
        (62 + index % 3 * 14);
    } else {
      const travelsRight = source.x < target.x;
      sourceX = source.x + (travelsRight ? DEPENDENCY_NODE_WIDTH : 0);
      targetX = target.x + (travelsRight ? 0 : DEPENDENCY_NODE_WIDTH);
      controlX = (sourceX + targetX) / 2;
    }
    const path = `M ${sourceX.toFixed(1)} ${sourceY.toFixed(1)} C ${controlX.toFixed(1)} ${sourceY.toFixed(1)}, ${controlX.toFixed(1)} ${targetY.toFixed(1)}, ${targetX.toFixed(1)} ${targetY.toFixed(1)}`;
    const label = `${edge.sourceNamespace || "(global)"} depends on ${edge.targetNamespace || "(global)"} through ${formatCount(edge.counts.total, "relationship")}`;
    return `<path class="metrics-dependency-edge" d="${path}" marker-end="url(#metrics-dependency-arrow)" data-dependency-edge-index="${index}" data-source-namespace="${escapeHtml(edge.sourceNamespace)}" data-target-namespace="${escapeHtml(edge.targetNamespace)}" tabindex="0" role="button" aria-pressed="${index === selectedIndex}" aria-label="${escapeHtml(`${label}. Show explaining types.`)}"><title>${escapeHtml(label)}</title></path>`;
  }).join("");
  const nodes = visibleNamespaces.map(node => {
    const position = positions.get(node.namespace);
    if (!position) return "";
    const visibleCycleIndex = node.cycleIndex !== null
      && visibleCycleIndexes.has(node.cycleIndex)
      ? node.cycleIndex
      : null;
    const cycle = visibleCycleIndex === null
      ? ""
      : `<text class="metrics-dependency-cycle-label" x="${position.x + DEPENDENCY_NODE_WIDTH - 8}" y="${position.y + 14}" text-anchor="end">cycle ${formatNumber(visibleCycleIndex + 1)}</text>`;
    const classes = visibleCycleIndex === null
      ? "metrics-dependency-node"
      : "metrics-dependency-node metrics-dependency-node-cycle";
    const evidence = `${node.namespace || "(global)"} · level ${formatNumber(node.level)} · ${formatCount(node.typeCount, "type")} · ${formatCount(node.intraNamespaceRelationshipCount, "internal relationship")}${visibleCycleIndex === null ? "" : ` · cycle ${formatNumber(visibleCycleIndex + 1)}`}`;
    return `<g class="${classes}"><title>${escapeHtml(evidence)}</title><rect x="${position.x.toFixed(1)}" y="${position.y.toFixed(1)}" width="${DEPENDENCY_NODE_WIDTH}" height="${DEPENDENCY_NODE_HEIGHT}" rx="5"></rect><text class="metrics-dependency-node-label" x="${(position.x + 9).toFixed(1)}" y="${(position.y + 26).toFixed(1)}">${escapeHtml(dependencyNamespaceLabel(node.namespace))}</text>${cycle}</g>`;
  }).join("");

  const details = visibleEdges.map((edge, index) => {
    const source = edge.sourceNamespace || "(global)";
    const target = edge.targetNamespace || "(global)";
    const explanations = edge.explainingTypeEdges.length
      ? `<ul class="metrics-dependency-explanations">${edge.explainingTypeEdges.map(explanation =>
        `<li><span><button type="button" class="metrics-dependency-type" data-dependency-type-key="${escapeHtml(explanation.sourceTypeKey)}">${escapeHtml(explanation.sourceTypeDisplay)}</button> <span aria-hidden="true">\u2192</span> <button type="button" class="metrics-dependency-type" data-dependency-type-key="${escapeHtml(explanation.targetTypeKey)}">${escapeHtml(explanation.targetTypeDisplay)}</button></span><span>${formatCount(explanation.counts.total, "relationship")}</span></li>`).join("")}</ul>`
      : `<p>No explaining type edge was retained for this namespace relationship.</p>`;
    const remaining = edge.remainingContributorCount > 0
      ? `<p class="metrics-dependency-remaining">${formatCount(edge.remainingContributorCount, "additional contributing type edge")} not shown.</p>`
      : "";
    return `<section class="metrics-dependency-detail" data-dependency-edge-detail="${index}"${index === selectedIndex ? "" : " hidden"}>
      <div class="metrics-dependency-detail-heading">
        <div>
          <p class="metrics-dependency-detail-label">Selected dependency</p>
          <h3 tabindex="-1" data-dependency-edge-heading>${escapeHtml(source)} <span aria-hidden="true">\u2192</span> ${escapeHtml(target)}</h3>
        </div>
        <strong>${formatCount(edge.counts.total, "relationship")}</strong>
      </div>
      <p>${formatCount(edge.counts.invocations, "invocation")} · ${formatCount(edge.counts.functionReferences, "function reference")} · ${formatCount(edge.contributingTypeEdgeCount, "contributing type edge")}</p>
      ${explanations}${remaining}
    </section>`;
  }).join("");
  const population = dependency.population;
  const qualified = dependency.completeness !== "Complete"
    ? `<section class="document-section metadata-warning"><strong>&#x26A0; Dependency evidence is qualified</strong><p>${formatCount(population?.unresolvedCallCount ?? 0, "unresolved call")} and ${formatCount(population?.incompleteBodyCount ?? 0, "incomplete body")} may hide relationships.</p>${dependency.diagnostics.length ? `<ul>${dependency.diagnostics.map(diagnostic => `<li>${escapeHtml(diagnostic)}</li>`).join("")}</ul>` : ""}</section>`
    : "";
  const retained = visibleEdges.length;
  const selection = retained < dependency.totalNamespaceEdgeCount
    ? ` Showing ${formatNumber(retained)} of ${formatNumber(dependency.totalNamespaceEdgeCount)} issued edges; select an arc for its bounded Type contributors.`
    : " Select an arc for its bounded Type contributors.";
  const globalDisclosure = globalNamespaces.size
    ? `<div class="metrics-dependency-global-options">
        <label class="metrics-dependency-global-control">
          <input type="checkbox" data-dependency-include-global${includeGlobalNamespace ? " checked" : ""}>
          <span>Include global namespace</span>
        </label>
        ${includeGlobalNamespace
          ? ""
          : `<p class="metrics-dependency-global-note">${formatCount(hiddenGlobalEdgeCount, "retained relationship")} and ${formatCount(hiddenGlobalCycleCount, "issued cycle")} involving compiler/global namespace evidence hidden.</p>`}
      </div>`
    : "";
  const detailEmptyHidden = selectedIndex >= 0 ? " hidden" : "";
  const detailEmpty = details
    ? "Select an arc to inspect its exact namespace dependency and contributing Types."
    : "No visible cross-namespace dependency edges were issued.";

  return `${qualified}<section class="document-section metrics-visual-section">
    <div class="metrics-dependency-heading">
      <div class="metrics-visual-copy"><h2>Dependency Structure</h2><p>Namespaces are arranged by analysis-issued level. Cycle badges show owner-issued cycles that remain fully visible; arrowheads show dependency direction.</p></div>
      ${globalDisclosure}
    </div>
    <div class="metrics-dependency-viewport"><svg class="metrics-dependency-structure" viewBox="0 0 ${width} ${height}" width="${width}" height="${height}" role="group" aria-label="Levelized namespace dependency structure"><defs><marker id="metrics-dependency-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse"><path d="M 0 0 L 10 5 L 0 10 z"></path></marker></defs>${columns}${edgePaths}${nodes}</svg></div>
    <p class="metrics-visual-caption">${formatCount(visibleNamespaces.length, "visible namespace")} · ${formatCount(visibleCycles.length, "visible cycle")} · ${formatCount(retained, "visible edge")}.${selection}</p>
    <div class="metrics-dependency-details" data-dependency-details aria-live="polite">
      <p data-dependency-detail-empty${detailEmptyHidden}>${detailEmpty}</p>
      ${details}
    </div>
  </section>`;
}

function renderDependencyStructureState(
  options: LibraryAnalysisOptions,
): string {
  if (!options.dependencyFresh || options.dependencyLoading) {
    return `<section class="document-section source-progress metrics-dependency-demand"><span class="loader"></span><h2>Building dependency structure&hellip;</h2><p>Resolving call relationships and deriving namespace levels and cycles.</p></section>`;
  }
  if (options.dependencyError) {
    return `<section class="document-section empty-document metrics-dependency-demand"><span class="large-glyph">&#x25B3;</span><h2>Dependency structure failed</h2><p>${options.escapeHtml(options.dependencyError)}</p><button type="button" class="primary-action" data-load-dependency-structure>Try again</button></section>`;
  }
  if (!options.dependencyData) {
    return `<section class="document-section empty-document metrics-dependency-demand"><h2>Dependency structure unavailable</h2><p>No dependency document was returned.</p><button type="button" class="primary-action" data-load-dependency-structure>Try again</button></section>`;
  }
  return renderDependencyStructure(
    options.dependencyData,
    options.escapeHtml,
    options.dependencyState,
  );
}

export function bindLibraryMetricsInteractions(
  root: ParentNode,
  actions: LibraryAnalysisInteractionActions,
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

  const relationshipDetail = root.querySelector<HTMLElement>(
    "[data-metrics-relationship-detail]",
  );
  const relationshipDetailEmpty = relationshipDetail
    ?.querySelector<HTMLElement>("[data-metrics-relationship-detail-empty]");
  const relationshipDetailSelection = relationshipDetail
    ?.querySelector<HTMLElement>(
      "[data-metrics-relationship-detail-selection]",
    );
  const relationshipSource = relationshipDetail
    ?.querySelector<HTMLButtonElement>("[data-metrics-relationship-source]");
  const relationshipTarget = relationshipDetail
    ?.querySelector<HTMLButtonElement>("[data-metrics-relationship-target]");
  const relationshipDepth = relationshipDetail
    ?.querySelector<HTMLElement>("[data-metrics-relationship-depth]");
  const relationshipRank = relationshipDetail
    ?.querySelector<HTMLElement>("[data-metrics-relationship-rank]");
  const relationshipEdges = root.querySelectorAll<SVGPathElement>(
    "[data-metrics-relationship]",
  );
  const relationshipLimit = root.querySelector<HTMLInputElement>(
    "[data-metrics-relationship-limit]",
  );
  const relationshipLimitOutput = root.querySelector<HTMLOutputElement>(
    "[data-metrics-relationship-limit-output]",
  );
  const relationshipVisibleCount = root.querySelector<HTMLElement>(
    "[data-metrics-relationship-visible-count]",
  );
  const currentVisibleCount = () => {
    const levels = relationshipLimit?.dataset.limitLevels
      ?.split(",").map(Number) ?? [];
    return relationshipLimit
      ? levels[Number(relationshipLimit.value)] ?? relationshipEdges.length
      : relationshipEdges.length;
  };
  const saveRelationshipState = () => {
    const selected = [...relationshipEdges].find(edge =>
      edge.getAttribute("aria-pressed") === "true");
    actions.updateRelationshipState({
      visibleCount: currentVisibleCount(),
      selectedSourceTypeKey: selected?.dataset.sourceTypeKey ?? null,
      selectedTargetTypeKey: selected?.dataset.targetTypeKey ?? null,
    });
  };
  const clearRelationship = () => {
    for (const candidate of relationshipEdges) {
      candidate.setAttribute("aria-pressed", "false");
    }
    if (relationshipDetailEmpty) relationshipDetailEmpty.hidden = false;
    if (relationshipDetailSelection) {
      relationshipDetailSelection.hidden = true;
    }
  };
  const selectRelationship = (edge: SVGPathElement) => {
    for (const candidate of relationshipEdges) {
      candidate.setAttribute(
        "aria-pressed",
        candidate === edge ? "true" : "false",
      );
    }
    const sourceKey = edge.dataset.sourceTypeKey ?? "";
    const sourceDisplay = edge.dataset.sourceTypeDisplay ?? sourceKey;
    const targetKey = edge.dataset.targetTypeKey ?? "";
    const targetDisplay = edge.dataset.targetTypeDisplay ?? targetKey;
    const callSiteCount = Number(edge.dataset.callSiteCount ?? 0);
    const rank = Number(edge.dataset.relationshipRank ?? 0);
    if (relationshipSource) {
      relationshipSource.textContent = sourceDisplay;
      relationshipSource.dataset.metricsTypeKey = sourceKey;
      relationshipSource.setAttribute("aria-label", `Open ${sourceDisplay}`);
    }
    if (relationshipTarget) {
      relationshipTarget.textContent = targetDisplay;
      relationshipTarget.dataset.metricsTypeKey = targetKey;
      relationshipTarget.setAttribute("aria-label", `Open ${targetDisplay}`);
    }
    if (relationshipDepth) {
      relationshipDepth.textContent = formatCount(
        callSiteCount,
        "retained call site",
      );
    }
    if (relationshipRank) {
      relationshipRank.textContent =
        `${formatNumber(rank)}/${formatNumber(relationshipEdges.length)} most connected`;
    }
    if (relationshipDetailEmpty) relationshipDetailEmpty.hidden = true;
    if (relationshipDetailSelection) {
      relationshipDetailSelection.hidden = false;
    }
    saveRelationshipState();
  };
  for (const edge of relationshipEdges) {
    edge.addEventListener("click", () => selectRelationship(edge));
    edge.addEventListener("keydown", event => {
      if (event.key !== "Enter" && event.key !== " ") return;
      event.preventDefault();
      selectRelationship(edge);
    });
  }
  relationshipLimit?.addEventListener("input", () => {
    const levels = relationshipLimit.dataset.limitLevels
      ?.split(",").map(Number) ?? [];
    const visible = levels[Number(relationshipLimit.value)];
    if (visible === undefined) return;
    let selectedWasHidden = false;
    for (const edge of relationshipEdges) {
      const hidden = Number(edge.dataset.relationshipRank) > visible;
      edge.toggleAttribute("hidden", hidden);
      if (edge.getAttribute("aria-pressed") === "true" && hidden) {
        selectedWasHidden = true;
      }
      const line = edge.nextElementSibling;
      if (line instanceof SVGPathElement
          && line.classList.contains("metrics-relationship-line")) {
        line.toggleAttribute("hidden", hidden);
      }
    }
    if (selectedWasHidden) clearRelationship();
    const visibleText = formatNumber(visible);
    const totalText = formatNumber(relationshipEdges.length);
    if (relationshipLimitOutput) {
      relationshipLimitOutput.textContent = `${visibleText} / ${totalText}`;
    }
    if (relationshipVisibleCount) {
      relationshipVisibleCount.textContent = visibleText;
    }
    relationshipLimit.setAttribute(
      "aria-valuetext",
      `${visibleText} of ${totalText} arcs`,
    );
    saveRelationshipState();
  });
  for (const endpoint of [relationshipSource, relationshipTarget]) {
    endpoint?.addEventListener("click", () => {
      const typeKey = endpoint.dataset.metricsTypeKey;
      if (typeKey) actions.activateType(typeKey);
    });
  }

  const dependencyEdges = root.querySelectorAll<SVGPathElement>(
    "[data-dependency-edge-index]",
  );
  const dependencyDetails = root.querySelectorAll<HTMLElement>(
    "[data-dependency-edge-detail]",
  );
  const dependencyDetailEmpty = root.querySelector<HTMLElement>(
    "[data-dependency-detail-empty]",
  );
  const saveDependencyState = () => {
    const selected = [...dependencyEdges].find(edge =>
      edge.getAttribute("aria-pressed") === "true");
    const includeGlobal = root.querySelector<HTMLInputElement>(
      "[data-dependency-include-global]",
    )?.checked ?? false;
    actions.updateDependencyState({
      includeGlobalNamespace: includeGlobal,
      selectedSourceNamespace: selected?.dataset.sourceNamespace ?? null,
      selectedTargetNamespace: selected?.dataset.targetNamespace ?? null,
    });
  };
  for (const edge of dependencyEdges) {
    const selectDependency = () => {
      const index = edge.dataset.dependencyEdgeIndex;
      if (index === undefined) return;
      for (const candidate of dependencyEdges) {
        candidate.setAttribute(
          "aria-pressed",
          candidate === edge ? "true" : "false",
        );
      }
      let details: HTMLElement | null = null;
      for (const candidate of dependencyDetails) {
        const selected =
          candidate.dataset.dependencyEdgeDetail === index;
        candidate.hidden = !selected;
        if (selected) details = candidate;
      }
      if (!details) return;
      if (dependencyDetailEmpty) dependencyDetailEmpty.hidden = true;
      details.querySelector<HTMLElement>(
        "[data-dependency-edge-heading]",
      )?.focus({ preventScroll: true });
      details.scrollIntoView({ block: "nearest" });
      saveDependencyState();
    };
    edge.addEventListener("click", selectDependency);
    edge.addEventListener("keydown", event => {
      if (event.key !== "Enter" && event.key !== " ") return;
      event.preventDefault();
      selectDependency();
    });
  }
  root.querySelector<HTMLInputElement>(
    "[data-dependency-include-global]",
  )?.addEventListener("change", saveDependencyState);

  for (const button of root.querySelectorAll<HTMLButtonElement>(
    "[data-dependency-type-key]",
  )) {
    const typeKey = button.dataset.dependencyTypeKey;
    if (!typeKey) continue;
    button.addEventListener("click", () => actions.activateType(typeKey));
  }
  for (const button of root.querySelectorAll<HTMLButtonElement>(
    "[data-load-dependency-structure]",
  )) {
    button.addEventListener("click", actions.loadDependencyStructure);
  }
}

export function renderLibraryMetricsSurface(
  options: LibraryAnalysisOptions,
  mode: LibraryMetricsMode,
): string {
  const {
    requireLibrary, fresh, loading, error, data, escapeHtml,
  } = options;
  let status: string;
  let content: string;
  if (requireLibrary) {
    status = "Select a library";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Pick a library to analyze</h2><p>Choose a .NET platform library above to inspect its compiled implementation evidence.</p></section>`;
  } else if (loading && fresh) {
    status = "Analyzing library\u2026";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Analyzing library&hellip;</h2><p>Computing Research-owned structural evidence across this library's method bodies.</p></section>`;
  } else if (fresh && error) {
    status = "Analysis failed";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Analysis failed</h2><p>${escapeHtml(error)}</p></section>`;
  } else {
    const resolved = fresh ? data : null;
    if (!resolved) {
      status = "Loading\u2026";
      content = `<section class="document-section empty-document"><span class="loader"></span><h2>Loading&hellip;</h2></section>`;
    } else if (resolved.outcome !== "available") {
      status = resolved.outcome === "failed"
        ? "Analysis failed"
        : "Analysis unavailable";
      content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>${escapeHtml(status)}</h2><p>${escapeHtml(resolved.failure || "The Research document could not be produced.")}</p></section>`;
    } else {
      const population = resolved.population;
      status = `${(population?.completeProfileCount ?? 0).toLocaleString()} complete bodies`;
      const coverageGap = population
        && population.profiledPhysicalEvidenceBodyCount
          < population.physicalEvidenceBodyCount;
      const incomplete = population && (
        coverageGap || population.incompleteProfileCount > 0)
        ? `<section class="document-section metadata-warning"><strong>&#x26A0; Analysis is qualified</strong><p>${
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
      const visualization = mode === "complexity"
        ? renderTreemap(resolved, escapeHtml)
        : renderRelationshipCrossing(
          resolved,
          escapeHtml,
          options.relationshipState,
        );
      content = `${incomplete}${visualization}`;
    }
  }
  return renderAnalysisInspector(options, mode, status, content);
}

export function renderLibraryDependencyStructureSurface(
  options: LibraryAnalysisOptions,
): string {
  let status: string;
  let content: string;
  if (options.requireLibrary) {
    status = "Select a library";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Pick a library to analyze</h2><p>Choose a .NET platform library above to inspect its dependency structure.</p></section>`;
  } else {
    const dependency = options.dependencyFresh
      ? options.dependencyData
      : null;
    status = options.dependencyLoading || !options.dependencyFresh
      ? "Building dependency structure\u2026"
      : dependency?.outcome === "available"
        ? formatCount(dependency.namespaceEdges.length, "issued edge")
        : options.dependencyError
          ? "Dependency structure failed"
          : "Dependency structure unavailable";
    content = renderDependencyStructureState(options);
  }
  return renderAnalysisInspector(options, "dependencies", status, content);
}
