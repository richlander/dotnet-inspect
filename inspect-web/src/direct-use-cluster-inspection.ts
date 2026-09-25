import type {
  BrowserDirectUseClusterInspection,
} from "./facades/inspect-web-call-graph.d.ts";
import type {
  InspectedCallGraph,
  InspectedCallGraphBoundary,
} from "./call-graph-inspection.ts";

export interface DirectUseClusterInspectionState {
  directUseClusterGraphKey: string;
  directUseClusterBoundary: InspectedCallGraphBoundary | null;
  directUseClusterResult: BrowserDirectUseClusterInspection | null;
  directUseClusterLoading: boolean;
  directUseClusterError: string;
  directUseClusterSeq: number;
}

interface DirectUseClusterInspectionRequest {
  sourcePackageId: string;
  sourceVersion: string;
  sourceFramework: string;
  sourceAssembly: string;
  targetPackageId: string;
  targetVersion: string;
  targetFramework: string;
  targetAssembly: string;
  selectedCluster: number;
}

export interface DirectUseClusterInspectionDependencies {
  state: DirectUseClusterInspectionState;
  query(
    request: DirectUseClusterInspectionRequest,
  ): Promise<BrowserDirectUseClusterInspection>;
  describeError(error: unknown): string;
  render(): void;
}

export interface DirectUseClusterInspectionController {
  reset(): void;
  selectBoundary(
    graphKey: string,
    boundary: InspectedCallGraphBoundary,
  ): Promise<void>;
  selectCluster(ordinal: number): Promise<void>;
}

export function createDirectUseClusterInspectionController(
  dependencies: DirectUseClusterInspectionDependencies,
): DirectUseClusterInspectionController {
  const { state } = dependencies;

  const load = async (
    graphKey: string,
    boundary: InspectedCallGraphBoundary,
    selectedCluster: number,
  ) => {
    const sequence = ++state.directUseClusterSeq;
    const changesBoundary = state.directUseClusterGraphKey !== graphKey
      || state.directUseClusterBoundary?.id !== boundary.id;
    state.directUseClusterGraphKey = graphKey;
    state.directUseClusterBoundary = boundary;
    if (changesBoundary) state.directUseClusterResult = null;
    else if (selectedCluster > 0 && state.directUseClusterResult) {
      state.directUseClusterResult = {
        ...state.directUseClusterResult,
        selectedCluster: null,
        callSites: [],
      };
    }
    state.directUseClusterLoading = true;
    state.directUseClusterError = "";
    dependencies.render();
    try {
      const result = await dependencies.query({
        sourcePackageId: boundary.sourcePackageId,
        sourceVersion: boundary.sourcePackageVersion,
        sourceFramework: boundary.sourcePackageFramework,
        sourceAssembly: boundary.sourceAssembly,
        targetPackageId: boundary.targetPackageId,
        targetVersion: boundary.targetPackageVersion,
        targetFramework: boundary.targetPackageFramework,
        targetAssembly: boundary.targetAssembly,
        selectedCluster,
      });
      if (sequence !== state.directUseClusterSeq
        || state.directUseClusterGraphKey !== graphKey
        || state.directUseClusterBoundary?.id !== boundary.id) return;
      state.directUseClusterResult = result;
      state.directUseClusterLoading = false;
      dependencies.render();
    } catch (error) {
      if (sequence !== state.directUseClusterSeq
        || state.directUseClusterGraphKey !== graphKey
        || state.directUseClusterBoundary?.id !== boundary.id) return;
      state.directUseClusterLoading = false;
      state.directUseClusterError = dependencies.describeError(error);
      dependencies.render();
    }
  };

  return {
    reset() {
      state.directUseClusterSeq++;
      state.directUseClusterGraphKey = "";
      state.directUseClusterBoundary = null;
      state.directUseClusterResult = null;
      state.directUseClusterLoading = false;
      state.directUseClusterError = "";
    },

    selectBoundary(graphKey, boundary) {
      return load(graphKey, boundary, 0);
    },

    selectCluster(ordinal) {
      const boundary = state.directUseClusterBoundary;
      if (!boundary || !state.directUseClusterGraphKey) {
        return Promise.resolve();
      }
      return load(state.directUseClusterGraphKey, boundary, ordinal);
    },
  };
}

export function renderDirectUseClusterInspection(
  graph: InspectedCallGraph,
  state: DirectUseClusterInspectionState,
  escapeHtml: (value: string) => string,
): string {
  if (graph.boundaries.length === 0) return "";
  const graphKey = graph.mermaid;
  const selected = state.directUseClusterGraphKey === graphKey
    ? state.directUseClusterBoundary
    : null;
  const result = selected ? state.directUseClusterResult : null;
  const boundaries = graph.boundaries.map(boundary => {
    const active = selected?.id === boundary.id;
    return `<button type="button" class="direct-use-boundary${active ? " selected" : ""}" data-direct-use-boundary="${escapeHtml(boundary.id)}" aria-pressed="${active}">
      <strong>${escapeHtml(boundary.sourceAssembly)} → ${escapeHtml(boundary.targetAssembly)}</strong>
      <span>${escapeHtml(boundary.sourcePackageId)}@${escapeHtml(boundary.sourcePackageVersion)} → ${escapeHtml(boundary.targetPackageId)}@${escapeHtml(boundary.targetPackageVersion)}</span>
    </button>`;
  }).join("");

  let detail = "";
  if (selected) {
    const status = state.directUseClusterLoading
      ? `<div class="direct-use-cluster-status" role="status"><span class="loader"></span>Inspecting exact Library use…</div>`
      : "";
    const error = state.directUseClusterError
      ? `<div class="graph-drill-error" role="alert">${escapeHtml(state.directUseClusterError)}</div>`
      : "";
    if (!result) {
      detail = `${status}${error}`;
    } else {
      const qualification = result.isComplete
        ? ""
        : `<div class="graph-drill-error" role="status">Results are incomplete. ${escapeHtml(result.diagnostics.map(item => item.summary).join(" "))}</div>`;
      const failure = result.failure
        ? `<div class="graph-drill-error" role="alert">${escapeHtml(result.failure)}</div>`
        : "";
      const clusters = result.outcome === "rejected"
        ? ""
        : result.clusters.length === 0
        ? `<p class="direct-use-cluster-empty">No direct use was observed between these Libraries.</p>`
        : `<div class="direct-use-cluster-list">${result.clusters.map(cluster => `
          <button type="button" class="direct-use-cluster${result.selectedCluster === cluster.ordinal ? " selected" : ""}" data-direct-use-cluster="${cluster.ordinal}" aria-pressed="${result.selectedCluster === cluster.ordinal}">
            <strong>Cluster ${cluster.ordinal}</strong>
            <span>${cluster.sourceMembers} source · ${cluster.providerTypes} provider types · ${cluster.targetMembers} targets · ${cluster.extensionMethods} extensions · ${cluster.callSites} call sites</span>
          </button>`).join("")}
        </div>`;
      const calls = result.outcome !== "available"
          || result.selectedCluster === null
        ? ""
        : `<section class="direct-use-call-sites" aria-labelledby="direct-use-call-sites-title">
          <h4 id="direct-use-call-sites-title">Exact Call Sites</h4>
          ${result.callSites.length === 0
            ? `<p>No call sites were returned for this cluster.</p>`
            : `<div class="direct-use-call-site-list">${result.callSites.map(call => `
                <article class="direct-use-call-site">
                  <div><code>${escapeHtml(call.sourceMember)}</code> <span>${hexToken(call.sourceToken)}</span></div>
                  <small>${escapeHtml(call.source.name)} · MVID ${escapeHtml(call.source.moduleVersionId)}</small>
                  <div class="direct-use-call-arrow">${escapeHtml(call.callKind)} · IL ${hexOffset(call.ilOffset)}</div>
                  <div><code>${escapeHtml(call.targetMember)}</code> <span>${hexToken(call.targetToken)}</span></div>
                  <small>${escapeHtml(call.target.name)} · MVID ${escapeHtml(call.target.moduleVersionId)}</small>
                  <small>Evidence ${escapeHtml(call.evidenceMethod)} · MVID ${escapeHtml(call.evidenceModuleVersionId)} · ${hexToken(call.evidenceToken)}</small>
                </article>`).join("")}</div>`}
        </section>`;
      detail = `${status}${error}${qualification}${failure}${clusters}${calls}`;
    }
  }

  return `<section class="direct-use-cluster-inspection" aria-labelledby="direct-use-cluster-title">
    <div class="section-title"><h3 id="direct-use-cluster-title">Library boundaries</h3><span>Direct-Use Clusters</span></div>
    <div class="direct-use-boundary-list">${boundaries}</div>
    ${detail}
  </section>`;
}

export function bindDirectUseClusterInspection(
  root: ParentNode,
  actions: {
    selectBoundary(id: string): void;
    selectCluster(ordinal: number): void;
  },
): void {
  for (const button of root.querySelectorAll<HTMLButtonElement>(
    "[data-direct-use-boundary]",
  )) {
    button.addEventListener("click", () => {
      const id = button.dataset.directUseBoundary;
      if (id) actions.selectBoundary(id);
    });
  }
  for (const button of root.querySelectorAll<HTMLButtonElement>(
    "[data-direct-use-cluster]",
  )) {
    button.addEventListener("click", () => {
      const ordinal = Number(button.dataset.directUseCluster);
      if (Number.isInteger(ordinal) && ordinal > 0) {
        actions.selectCluster(ordinal);
      }
    });
  }
}

function hexToken(value: number): string {
  return `0x${value.toString(16).toUpperCase().padStart(8, "0")}`;
}

function hexOffset(value: number): string {
  return `0x${value.toString(16).toUpperCase().padStart(4, "0")}`;
}
