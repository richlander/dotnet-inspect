import type {
  BrowserCloneCandidateDocument,
  BrowserCloneCandidateMethod,
  BrowserCloneCandidateResult,
  BrowserCloneCandidateRow,
} from "./facades/inspect-web-analysis.d.ts";
import type { EscapeHtml } from "./csharp-highlighting.ts";
import type {
  CloneCandidateInspectionState,
  CloneCandidateBreadth,
  CloneCandidateDiscovery,
} from "./clone-candidate-inspection.ts";

const breadths = [
  ["Self", "Self"],
  ["SelfAndRegisteredEcosystems", "Self + ecosystems"],
  ["Everything", "Everything"],
] as const satisfies readonly (readonly [CloneCandidateBreadth, string])[];

const discoveries = [
  ["SimilarNames", "Similar names"],
  ["All", "All methods"],
] as const satisfies readonly (readonly [CloneCandidateDiscovery, string])[];

export type CloneCandidateViewAction =
  | { readonly kind: "breadth"; readonly value: CloneCandidateBreadth }
  | { readonly kind: "discovery"; readonly value: CloneCandidateDiscovery }
  | { readonly kind: "select"; readonly rank: number }
  | {
      readonly kind: "navigate";
      readonly participantOrdinal: number;
      readonly moduleVersionId: string;
      readonly methodDefinitionToken: number;
    };

export interface CloneCandidateViewBindingActions {
  readonly onAction: (action: CloneCandidateViewAction) => void;
}

function score(value: number): string {
  return `${(value / 100).toFixed(value % 100 === 0 ? 0 : 1)}%`;
}

function similarity(value: number): string {
  return `${(value * 100).toFixed(1)}%`;
}

function token(value: number): string {
  return `0x${value.toString(16).padStart(8, "0")}`;
}

function controls(
  state: CloneCandidateInspectionState,
  escapeHtml: EscapeHtml,
): string {
  const radios = <T extends string>(
    name: string,
    values: readonly (readonly [T, string])[],
    selected: T,
  ) => values.map(([value, label]) => `
      <label>
        <input type="radio" name="${name}" value="${escapeHtml(value)}"
          data-clone-${name}${value === selected ? " checked" : ""}>
        <span>${escapeHtml(label)}</span>
      </label>`).join("");
  return `
    <div class="clone-candidate-controls">
      <fieldset>
        <legend>Breadth</legend>
        ${radios("breadth", breadths, state.breadth)}
      </fieldset>
      <fieldset>
        <legend>Candidate selection</legend>
        ${radios("discovery", discoveries, state.discovery)}
      </fieldset>
    </div>`;
}

function endpoint(
  side: "Seed" | "Candidate",
  value: BrowserCloneCandidateMethod,
  escapeHtml: EscapeHtml,
): string {
  const participant = value.participant;
  const provenance = [
    participant.provenance.packageId,
    participant.provenance.packageVersion,
    participant.provenance.tfm,
    participant.provenance.framework,
  ].filter(Boolean).join(" · ");
  return `
    <section class="clone-candidate-endpoint">
      <h4>${side}</h4>
      <p><code>${escapeHtml(value.addressDisplay)}</code></p>
      <dl>
        <div><dt>Assembly</dt><dd>${escapeHtml(participant.assembly.name)}</dd></div>
        <div><dt>Package</dt><dd>${escapeHtml(provenance)}</dd></div>
        <div><dt>MVID</dt><dd><code>${escapeHtml(value.moduleVersionId)}</code></dd></div>
        <div><dt>MethodDef</dt><dd><code>${token(value.methodDefinitionToken)}</code></dd></div>
      </dl>
      <button type="button" data-clone-navigate
        data-participant-ordinal="${participant.ordinal}"
        data-module-version-id="${escapeHtml(value.moduleVersionId)}"
        data-method-definition-token="${value.methodDefinitionToken}">
        Open ${side.toLowerCase()}
      </button>
    </section>`;
}

function selectedRow(
  document: BrowserCloneCandidateDocument,
  selectedRank: number | null,
): BrowserCloneCandidateRow | null {
  return document.rows.find(row => row.rank === selectedRank)
    ?? document.rows[0]
    ?? null;
}

function renderRows(
  document: BrowserCloneCandidateDocument,
  selectedRank: number | null,
  escapeHtml: EscapeHtml,
): string {
  if (document.rows.length === 0) {
    return `<p class="clone-candidate-empty">No structural clone candidates were found.</p>`;
  }
  return `
    <ol class="clone-candidate-list">
      ${document.rows.map(row => `
        <li>
          <button type="button" data-clone-rank="${row.rank}"
            ${row.rank === selectedRank ? 'aria-current="true"' : ""}>
            <span class="clone-candidate-rank">#${row.rank}</span>
            <span class="clone-candidate-addresses">
              <code>${escapeHtml(row.left.addressDisplay)}</code>
              <span aria-hidden="true">→</span>
              <code>${escapeHtml(row.right.addressDisplay)}</code>
            </span>
            <strong>${score(row.similarity.score)}</strong>
          </button>
        </li>`).join("")}
    </ol>`;
}

function renderRowEvidence(
  row: BrowserCloneCandidateRow,
  escapeHtml: EscapeHtml,
): string {
  const similarityEvidence = [
    ["Overall", row.similarity.score],
    ["Operations", row.similarity.operationScore],
    ["Positions", row.similarity.positionScore],
    ["Blocks", row.similarity.blockScore],
    ["Edges", row.similarity.edgeScore],
    ["Locals", row.similarity.localScore],
  ] as const;
  return `
    <div class="clone-candidate-endpoints">
      ${endpoint("Seed", row.left, escapeHtml)}
      ${endpoint("Candidate", row.right, escapeHtml)}
    </div>
    <section class="clone-candidate-scores">
      <h4>Structural similarity</h4>
      <dl>
        ${similarityEvidence.map(([label, value]) =>
          `<div><dt>${label}</dt><dd>${score(value)}</dd></div>`).join("")}
      </dl>
      ${row.nameQualification
        ? `<p>Name admission: type ${similarity(row.nameQualification.declaringTypeSimilarity)}
            · member ${similarity(row.nameQualification.memberSimilarity)}</p>`
        : ""}
      <p>Shape: ${row.similarity.seedInstructions} → ${row.similarity.candidateInstructions} instructions
        · ${row.similarity.seedBlocks} → ${row.similarity.candidateBlocks} blocks
        · ${row.similarity.seedEdges} → ${row.similarity.candidateEdges} edges
        · ${row.similarity.seedLocals} → ${row.similarity.candidateLocals} locals</p>
    </section>`;
}

function renderSearchEvidence(
  document: BrowserCloneCandidateDocument,
  escapeHtml: EscapeHtml,
): string {
  const receipt = document.receipt;
  const notices = [
    document.scopeChangedDuringSearch
      ? "Workspace scope changed during the search."
      : "",
    !document.coverageIsComplete
      ? "Coverage is incomplete; inspect the coverage evidence below."
      : "",
    receipt.suppressedPairs > 0
      ? `${receipt.suppressedPairs} candidate pairs were suppressed.`
      : "",
    document.resultLimitReached
      ? `The result limit was reached; ${document.resultLimitOmittedPairs} ranked pairs were omitted.`
      : "",
  ].filter(Boolean);
  const coverageDetails = (
    failures: readonly { readonly detail: string }[],
    blockers: readonly { readonly detail: string }[],
  ) => [
    ...failures.map(failure => failure.detail),
    ...blockers.map(blocker => blocker.detail),
  ];
  return `
    <section class="clone-candidate-receipt">
      <h4>Search receipt</h4>
      ${notices.length
        ? `<ul>${notices.map(notice => `<li>${escapeHtml(notice)}</li>`).join("")}</ul>`
        : `<p>Coverage completed within the requested bounds.</p>`}
      <dl>
        <div><dt>Seed methods</dt><dd>${receipt.seedMethods}</dd></div>
        <div><dt>Candidate methods</dt><dd>${receipt.candidateMethods}</dd></div>
        <div><dt>Discovered methods</dt><dd>${receipt.discoveredMethods}</dd></div>
        <div><dt>Libraries</dt><dd>${receipt.admittedLibraries} admitted · ${receipt.excludedLibraries} excluded</dd></div>
        <div><dt>Ranked pairs</dt><dd>${receipt.rankedPairs}</dd></div>
        <div><dt>Returned pairs</dt><dd>${receipt.returnedPairs}</dd></div>
        <div><dt>Name comparisons</dt><dd>${receipt.nameComparisonWork}</dd></div>
        <div><dt>Retrieval pairs</dt><dd>${receipt.retrievalPairs}</dd></div>
      </dl>
      <details>
        <summary>Coverage and work limits</summary>
        <p>Threshold ${similarity(document.nameSimilarityThreshold)}
          · at most ${document.limits.maximumSeedMethods} seeds
          · ${document.limits.maximumCandidateMethods} candidates
          · ${document.limits.maximumParticipants} participants
          · ${document.limits.maximumRetrievalPairs} retrieval pairs
          · ${document.limits.maximumResults} results.</p>
        <h5>Seed retrieval</h5>
        <ul>
          ${document.seeds.map(seed => {
            const details = coverageDetails(
              seed.failures,
              seed.blockers);
            return `<li>
              <strong>${escapeHtml(seed.seed.addressDisplay)}</strong>:
              ${escapeHtml(seed.disposition)}, ${seed.rankedPairs} ranked,
              ${seed.suppressedPairs} suppressed,
              ${seed.isComplete ? "complete" : "incomplete"}
              ${details.length
                ? `<small>${details.map(escapeHtml).join("; ")}</small>`
                : ""}
            </li>`;
          }).join("") || "<li>No seed methods were produced.</li>"}
        </ul>
        <h5>Library coverage</h5>
        <ul>
          ${document.libraries.map(library => {
            const participant = library.participant;
            const label = participant.assembly.name
              || `Participant ${participant.ordinal}`;
            const failures = coverageDetails(
              library.failures,
              library.analysisBlockers);
            return `<li>
              <strong>${escapeHtml(label)}</strong>:
              ${escapeHtml(library.membership)}, ${library.admitted ? "admitted" : "excluded"};
              ${library.discoveredMethods} discovered of ${library.candidateMethods} candidates
              ${failures.length
                ? `<small>${failures.map(escapeHtml).join("; ")}</small>`
                : ""}
            </li>`;
          }).join("")}
        </ul>
      </details>
    </section>`;
}

function renderAvailable(
  document: BrowserCloneCandidateDocument,
  selectedRank: number | null,
  escapeHtml: EscapeHtml,
): string {
  const row = selectedRow(document, selectedRank);
  return `
    <p class="working-surface-note">
      Scores rank retrieval candidates; they do not establish clone identity,
      rename, move, provenance, or historical correspondence.
    </p>
    <div class="clone-candidate-workbench">
      <section aria-label="Clone candidate ranking">
        <h3>Globally ranked candidates</h3>
        ${renderRows(document, row?.rank ?? null, escapeHtml)}
      </section>
      <section class="clone-candidate-detail" aria-label="Selected clone candidate">
        ${row
          ? renderRowEvidence(row, escapeHtml)
          : `<p>No candidate pair is available to inspect.</p>`}
        ${renderSearchEvidence(document, escapeHtml)}
      </section>
    </div>`;
}

function renderResult(
  result: BrowserCloneCandidateResult | null,
  selectedRank: number | null,
  escapeHtml: EscapeHtml,
): string {
  if (!result) return "";
  switch (result.kind) {
    case "Available":
      return result.document
        ? renderAvailable(result.document, selectedRank, escapeHtml)
        : `<p class="clone-candidate-failure" role="alert">The available result did not contain a document.</p>`;
    case "Rejected":
      return `<p class="clone-candidate-failure" role="alert">${escapeHtml(
        result.detail
          ?? result.failure?.detail
          ?? "The Clone Candidates request was rejected.")}</p>`;
    case "Failed":
      return `<p class="clone-candidate-failure" role="alert">${escapeHtml(
        result.failure?.detail
          ?? result.detail
          ?? "Clone candidate discovery failed.")}</p>`;
    case "Unrepresentable":
      return `<p class="clone-candidate-failure" role="alert">${escapeHtml(
        [
          result.presentationRejectionKind ?? "Unrepresentable",
          result.detail ?? result.failure?.detail ?? "",
        ].filter(Boolean).join(": "))}</p>`;
    default:
      return `<p class="clone-candidate-failure" role="alert">The Clone Candidates result used an unsupported outcome.</p>`;
  }
}

export function renderCloneCandidateInspection(
  state: CloneCandidateInspectionState,
  escapeHtml: EscapeHtml,
): string {
  return `
    <section class="clone-candidate-surface">
      <header>
        <div>
          <p class="eyebrow">Analysis</p>
          <h2>Clone Candidates</h2>
        </div>
        <p>Find structurally similar method bodies across the selected Workspace.</p>
      </header>
      ${controls(state, escapeHtml)}
      ${state.loading
        ? `<p class="clone-candidate-progress"><span class="loader"></span>Searching structural candidates…</p>`
        : ""}
      ${state.navigationLoading
        ? `<p class="clone-candidate-progress"><span class="loader"></span>Opening the exact method…</p>`
        : ""}
      ${state.error
        ? `<p class="clone-candidate-failure" role="alert">${escapeHtml(state.error)}</p>`
        : `${state.navigationError
            ? `<p class="clone-candidate-failure" role="alert">${escapeHtml(state.navigationError)}</p>`
            : ""}
          ${renderResult(state.result, state.selectedRank, escapeHtml)}`}
    </section>`;
}

function isBreadth(value: string): value is CloneCandidateBreadth {
  return breadths.some(([candidate]) => candidate === value);
}

function isDiscovery(value: string): value is CloneCandidateDiscovery {
  return discoveries.some(([candidate]) => candidate === value);
}

export function bindCloneCandidateInspection(
  root: ParentNode,
  actions: CloneCandidateViewBindingActions,
): void {
  root.querySelectorAll<HTMLInputElement>("[data-clone-breadth]")
    .forEach(input => {
      input.onchange = () => {
        if (input.checked && isBreadth(input.value)) {
          actions.onAction({ kind: "breadth", value: input.value });
        }
      };
    });
  root.querySelectorAll<HTMLInputElement>("[data-clone-discovery]")
    .forEach(input => {
      input.onchange = () => {
        if (input.checked && isDiscovery(input.value)) {
          actions.onAction({ kind: "discovery", value: input.value });
        }
      };
    });
  root.querySelectorAll<HTMLButtonElement>("[data-clone-rank]")
    .forEach(button => {
      button.onclick = () => {
        const rank = Number(button.dataset.cloneRank);
        if (Number.isInteger(rank) && rank > 0) {
          actions.onAction({ kind: "select", rank });
        }
      };
    });
  root.querySelectorAll<HTMLButtonElement>("[data-clone-navigate]")
    .forEach(button => {
      button.onclick = () => {
        const participantOrdinal = Number(button.dataset.participantOrdinal);
        const methodDefinitionToken =
          Number(button.dataset.methodDefinitionToken);
        const moduleVersionId = button.dataset.moduleVersionId ?? "";
        if (Number.isInteger(participantOrdinal)
          && participantOrdinal >= 0
          && Number.isInteger(methodDefinitionToken)
          && methodDefinitionToken > 0
          && moduleVersionId) {
          actions.onAction({
            kind: "navigate",
            participantOrdinal,
            moduleVersionId,
            methodDefinitionToken,
          });
        }
      };
    });
}
