import type {
  BrowserCloneCandidateAnalysisBlocker,
  BrowserCloneCandidateDocument,
  BrowserCloneCandidateFailure,
  BrowserCloneCandidateMethod,
  BrowserCloneCandidatePackage,
  BrowserCloneCandidateRequest,
  BrowserCloneCandidateResult,
  BrowserCloneCandidateRow,
  BrowserCloneCandidateSeedCoverage,
  BrowserCloneCandidateSeedRequest,
} from "./facades/inspect-web-analysis.d.ts";
import {
  renderCompareEmpty,
  renderCompareFrame,
  renderCompareLoading,
  renderCompareRetry,
  type CompareSubjectKind,
} from "./compare-surface.ts";
import type {
  OperationAuthorityPage,
  OperationDiagnostic,
  OperationFeatureEvent,
  OperationId,
  OperationProducerAdapter,
  OperationSession,
} from "./operation-authority.ts";

// Compare's Clone mode. Each subject (Library, Type, Member) runs its own
// scoped Clone query through the shared managed transport; a child result is
// never a filtered, regrouped, or reranked view of its parent's bounded rows.
// Library and Type rows are formed from the document's complete per-seed
// coverage joined to exact loaded Type and Member identity; Member is the first
// subject that shows ranked candidate pairs.

// Package Overview owns breadth and discovery through its Clone search scope;
// Compare renders no second editor and uses the producer defaults here.
const cloneBreadth = "Everything" as const;
const cloneDiscovery = "SimilarNames" as const;

export interface CompareCloneSelection {
  readonly packageModel: object;
  readonly packages: readonly BrowserCloneCandidatePackage[];
  readonly selectedPackageIndex: number;
  readonly assembly: string;
  readonly seed: BrowserCloneCandidateSeedRequest;
}

interface CompareCloneOperationInput {
  readonly packageModel: object;
  readonly request: BrowserCloneCandidateRequest;
  readonly requestSignature: string;
}

export type CompareCloneState =
  | { readonly status: "idle" }
  | { readonly status: "unavailable"; readonly message: string }
  | { readonly status: "loading"; readonly input: CompareCloneOperationInput }
  | {
      readonly status: "ready";
      readonly input: CompareCloneOperationInput;
      readonly result: BrowserCloneCandidateResult;
    }
  | {
      readonly status: "failed";
      readonly input: CompareCloneOperationInput;
      readonly error: string;
    };

export interface CompareCloneStateHost {
  compareClone: CompareCloneState;
}

export type CompareCloneReconcileTarget =
  | { readonly kind: "selection"; readonly selection: CompareCloneSelection }
  | { readonly kind: "unavailable"; readonly message: string }
  | null;

export interface CompareCloneDependencies {
  readonly state: CompareCloneStateHost;
  readonly operationAuthority: OperationAuthorityPage;
  query(request: BrowserCloneCandidateRequest): Promise<unknown>;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  render(): void;
}

export interface CompareCloneCoordinator {
  reconcile(target: CompareCloneReconcileTarget): void;
  retry(selection: CompareCloneSelection): void;
  cancelCurrentRequest(): boolean;
}

export function buildCompareCloneRequest(
  selection: CompareCloneSelection,
): BrowserCloneCandidateRequest {
  return {
    schemaVersion: 1,
    packages: selection.packages.map(item => ({
      packageId: item.packageId,
      version: item.version,
      targetFramework: item.targetFramework,
    })),
    selectedPackageIndex: selection.selectedPackageIndex,
    assembly: selection.assembly,
    seed: selection.seed,
    breadth: cloneBreadth,
    discovery: cloneDiscovery,
  };
}

function operationInput(
  selection: CompareCloneSelection,
): CompareCloneOperationInput {
  const request = buildCompareCloneRequest(selection);
  return {
    packageModel: selection.packageModel,
    request,
    requestSignature: JSON.stringify(request),
  };
}

function sameInput(
  left: CompareCloneOperationInput,
  right: CompareCloneOperationInput,
): boolean {
  return left.packageModel === right.packageModel
    && left.requestSignature === right.requestSignature;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function validateResult(
  result: unknown,
  input: CompareCloneOperationInput,
): asserts result is BrowserCloneCandidateResult {
  if (!isRecord(result))
    throw new Error("Clone result must be an object.");
  if (result.schemaVersion !== 1)
    throw new Error("Unsupported Clone result schema.");
  if (JSON.stringify(result.request) !== input.requestSignature)
    throw new Error("Clone result does not match its request.");
  switch (result.kind) {
    case "Available": {
      if (!isRecord(result.document))
        throw new Error("Clone available result has no document.");
      const document = result.document;
      for (const property of ["rows", "seeds", "libraries"]) {
        if (!Array.isArray(document[property]))
          throw new Error(`Clone document ${property} must be an array.`);
      }
      if (typeof document.coverageIsComplete !== "boolean"
        || typeof document.resultLimitReached !== "boolean") {
        throw new Error("Clone document coverage flags must be booleans.");
      }
      return;
    }
    case "Rejected":
    case "Failed":
    case "Unrepresentable":
      if (result.document !== null)
        throw new Error("A non-available Clone result carries no document.");
      return;
    default:
      throw new Error("Unknown Clone result kind.");
  }
}

export function createCompareCloneCoordinator(
  dependencies: CompareCloneDependencies,
): CompareCloneCoordinator {
  type FeatureEvent =
    OperationFeatureEvent<BrowserCloneCandidateResult, unknown, never>;
  type Session = OperationSession<
    CompareCloneOperationInput,
    BrowserCloneCandidateResult,
    unknown,
    never,
    never
  >;

  const inputs = new Map<OperationId, CompareCloneOperationInput>();
  const inputFor = (operationId: OperationId) => {
    const input = inputs.get(operationId);
    if (input === undefined)
      throw new Error("Clone operation context is unavailable.");
    return input;
  };
  const publish = (event: FeatureEvent): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced":
        dependencies.state.compareClone = {
          status: "loading",
          input: inputFor(event.operation.id),
        };
        break;
      case "terminal": {
        const input = inputFor(event.operationId);
        dependencies.state.compareClone =
          event.outcome.kind === "succeeded"
            ? { status: "ready", input, result: event.outcome.value }
            : {
                status: "failed",
                input,
                error: dependencies.describeError(event.outcome.error),
              };
        dependencies.render();
        break;
      }
      case "canceled":
      case "disposed":
        dependencies.state.compareClone = { status: "idle" };
        break;
      case "progress":
        break;
    }
    return undefined;
  };
  const session: Session = dependencies.operationAuthority.createSession({
    feature: { publish },
    diagnostic: {
      report: diagnostic => dependencies.reportOperationDiagnostic(diagnostic),
    },
  });
  const adapter: OperationProducerAdapter<
    CompareCloneOperationInput,
    BrowserCloneCandidateResult,
    unknown,
    never,
    never
  > = {
    prepare: (identity, input, sink) => {
      inputs.set(identity.id, input);
      const quiesce = (): undefined => {
        inputs.delete(identity.id);
        sink.reportQuiesced();
        return undefined;
      };
      const boundaryFailure = (error: unknown): undefined => {
        sink.reportUnexpectedTerminal(error, error);
        return quiesce();
      };
      const finish = (result: unknown): undefined => {
        try {
          validateResult(result, input);
          sink.reportTerminal({ kind: "succeeded", value: result });
        } catch (error: unknown) {
          sink.reportUnexpectedTerminal(error, error);
        }
        return quiesce();
      };
      return {
        kind: "prepared",
        binding: {
          // The managed Clone query exposes no cancellation; a superseded
          // request is retired by Operation Authority and its late completion
          // publishes nothing.
          requestCancellation: () => undefined,
          activate: () => {
            let query: Promise<unknown>;
            try {
              query = dependencies.query(input.request);
            } catch (error: unknown) {
              return boundaryFailure(error);
            }
            void query.then(finish, boundaryFailure);
            return undefined;
          },
          abandon: () => {
            inputs.delete(identity.id);
            return undefined;
          },
        },
      };
    },
  };

  const cancelCurrentRequest = (): boolean => {
    const cancellation = session.cancelCurrent("superseded");
    if (cancellation.kind === "rejected") {
      dependencies.reportOperationDiagnostic({
        kind: "producer-contract",
        operationId: null,
        error: new Error(
          "Clone cancellation was attempted during feature publication.",
        ),
      });
      return false;
    }
    dependencies.state.compareClone = { status: "idle" };
    return cancellation.kind === "applied";
  };

  const start = (selection: CompareCloneSelection): void => {
    const input = operationInput(selection);
    const started = session.start(input, adapter);
    if (started.kind === "rejected") {
      dependencies.state.compareClone = {
        status: "failed",
        input,
        error: `Clone search could not start: ${started.reason.kind}.`,
      };
    }
  };

  const reconcile = (target: CompareCloneReconcileTarget): void => {
    const current = dependencies.state.compareClone;
    if (target === null) {
      cancelCurrentRequest();
      return;
    }
    if (target.kind === "unavailable") {
      if (current.status === "unavailable"
        && current.message === target.message) {
        return;
      }
      cancelCurrentRequest();
      dependencies.state.compareClone = {
        status: "unavailable",
        message: target.message,
      };
      return;
    }
    const input = operationInput(target.selection);
    if (current.status !== "idle"
      && current.status !== "unavailable"
      && sameInput(current.input, input)) {
      return;
    }
    start(target.selection);
  };

  return {
    reconcile,
    retry(selection) {
      cancelCurrentRequest();
      start(selection);
    },
    cancelCurrentRequest,
  };
}

// The loaded Navigation subjects a seed body may resolve to. The Browser builds
// this from the current Library's typed surface (MethodDef tokens carried by
// member body selectors), never from display text.
export interface CompareCloneJoinedMethod {
  readonly typeIdentifier: string;
  readonly typeDisplay: string;
  readonly memberFingerprint: string;
  readonly memberDisplay: string;
}

export interface CompareCloneJoin {
  readonly containingLibrary: {
    readonly packageId: string;
    readonly version: string;
    readonly targetFramework: string;
    readonly assemblyName: string;
  };
  readonly methods: ReadonlyMap<number, CompareCloneJoinedMethod>;
}

function sameText(left: string | null | undefined, right: string): boolean {
  return typeof left === "string"
    && left.localeCompare(right, undefined, { sensitivity: "accent" }) === 0;
}

// A seed joins only through exact participant provenance, assembly identity,
// consistent MVID, and MethodDef token. Anything else stays visible but inert.
export function joinCloneSeed(
  seed: BrowserCloneCandidateMethod,
  join: CompareCloneJoin,
): CompareCloneJoinedMethod | null {
  const participant = seed.participant;
  const provenance = participant.provenance;
  if (provenance.kind !== "Package"
    || !sameText(provenance.packageId, join.containingLibrary.packageId)
    || !sameText(provenance.packageVersion, join.containingLibrary.version)
    || !sameText(provenance.tfm, join.containingLibrary.targetFramework)
    || participant.assembly.name !== join.containingLibrary.assemblyName
    || participant.moduleVersionId === null
    || participant.moduleVersionId !== seed.moduleVersionId) {
    return null;
  }
  return join.methods.get(seed.methodDefinitionToken) ?? null;
}

interface SeedGroup {
  readonly key: string;
  readonly display: string;
  seeds: number;
  rankedPairs: number;
  suppressedPairs: number;
  incomplete: number;
  unsupported: number;
}

function groupSeeds(
  seeds: readonly BrowserCloneCandidateSeedCoverage[],
  join: CompareCloneJoin,
  keyOf: (method: CompareCloneJoinedMethod) => { key: string; display: string },
): { groups: SeedGroup[]; unjoined: BrowserCloneCandidateSeedCoverage[] } {
  const groups = new Map<string, SeedGroup>();
  const unjoined: BrowserCloneCandidateSeedCoverage[] = [];
  for (const coverage of seeds) {
    const joined = joinCloneSeed(coverage.seed, join);
    if (joined === null) {
      unjoined.push(coverage);
      continue;
    }
    const { key, display } = keyOf(joined);
    let group = groups.get(key);
    if (group === undefined) {
      group = {
        key,
        display,
        seeds: 0,
        rankedPairs: 0,
        suppressedPairs: 0,
        incomplete: 0,
        unsupported: 0,
      };
      groups.set(key, group);
    }
    group.seeds += 1;
    group.rankedPairs += coverage.rankedPairs;
    group.suppressedPairs += coverage.suppressedPairs;
    if (!coverage.isComplete) group.incomplete += 1;
    if (coverage.disposition === "Unsupported") group.unsupported += 1;
  }
  return { groups: [...groups.values()], unjoined };
}

function groupSummary(group: SeedGroup): string {
  return [
    `${group.seeds.toLocaleString()} seed ${group.seeds === 1 ? "body" : "bodies"}`,
    `${group.rankedPairs.toLocaleString()} ranked ${group.rankedPairs === 1 ? "pair" : "pairs"}`,
    group.suppressedPairs > 0
      ? `${group.suppressedPairs.toLocaleString()} suppressed`
      : "",
    group.incomplete > 0
      ? `${group.incomplete.toLocaleString()} incomplete`
      : "",
    group.unsupported > 0
      ? `${group.unsupported.toLocaleString()} unsupported`
      : "",
  ].filter(Boolean).join(" · ");
}

function coverageDetails(
  failures: readonly BrowserCloneCandidateFailure[],
  blockers: readonly BrowserCloneCandidateAnalysisBlocker[],
): string[] {
  return [
    ...failures.map(failure => `${String(failure.kind)}${
      failure.subject ? ` [${failure.subject.name}]` : ""}: ${failure.detail}`),
    ...blockers.map(blocker => `${String(blocker.kind)}: ${blocker.detail}`),
  ];
}

function renderUnjoinedSeeds(
  unjoined: readonly BrowserCloneCandidateSeedCoverage[],
  escapeHtml: (value: unknown) => string,
): string {
  if (unjoined.length === 0) return "";
  return `<details class="library-api-diff-evidence compare-clone-unjoined">
    <summary>Seed bodies not joined to a loaded subject (${unjoined.length})</summary>
    <ul>${unjoined.map(coverage => `<li><code>${escapeHtml(coverage.seed.addressDisplay)}</code> · ${escapeHtml(String(coverage.disposition))}, ${coverage.rankedPairs.toLocaleString()} ranked${coverage.isComplete ? "" : ", incomplete"}</li>`).join("")}</ul>
  </details>`;
}

function renderCoverageEvidence(
  document: BrowserCloneCandidateDocument,
  escapeHtml: (value: unknown) => string,
): string {
  const receipt = document.receipt;
  const seedItems = document.seeds.map(seed => {
    const details = coverageDetails(seed.failures, seed.blockers);
    return `<li><code>${escapeHtml(seed.seed.addressDisplay)}</code>: ${escapeHtml(String(seed.disposition))}, ${seed.rankedPairs.toLocaleString()} ranked, ${seed.suppressedPairs.toLocaleString()} suppressed, ${seed.isComplete ? "complete" : "incomplete"}${
      details.length ? ` <small>${details.map(escapeHtml).join("; ")}</small>` : ""}</li>`;
  }).join("") || "<li>No seed bodies were produced.</li>";
  const libraryItems = document.libraries.map(library => {
    const details = coverageDetails(library.failures, library.analysisBlockers);
    return `<li><strong>${escapeHtml(library.participant.assembly.name)}</strong> · ${escapeHtml(String(library.membership))}, ${library.admitted ? "admitted" : "excluded"}; ${library.discoveredMethods.toLocaleString()} discovered of ${library.candidateMethods.toLocaleString()} candidates, ${library.isComplete ? "complete" : "incomplete"}${
      details.length ? ` <small>${details.map(escapeHtml).join("; ")}</small>` : ""}</li>`;
  }).join("");
  return `<details class="library-api-diff-evidence compare-clone-coverage">
    <summary>Coverage and limits</summary>
    <dl class="compare-clone-receipt">
      <div><dt>Seed methods</dt><dd>${receipt.seedMethods.toLocaleString()}</dd></div>
      <div><dt>Candidate methods</dt><dd>${receipt.candidateMethods.toLocaleString()}</dd></div>
      <div><dt>Libraries</dt><dd>${receipt.admittedLibraries.toLocaleString()} admitted · ${receipt.excludedLibraries.toLocaleString()} excluded</dd></div>
      <div><dt>Ranked pairs</dt><dd>${receipt.rankedPairs.toLocaleString()}</dd></div>
      <div><dt>Returned pairs</dt><dd>${receipt.returnedPairs.toLocaleString()}</dd></div>
      <div><dt>Result limit</dt><dd>${document.limits.maximumResults.toLocaleString()}${document.resultLimitReached ? ` reached · ${document.resultLimitOmittedPairs.toLocaleString()} omitted` : ""}</dd></div>
    </dl>
    <h3>Seed retrieval</h3>
    <ul>${seedItems}</ul>
    <h3>Library coverage</h3>
    <ul>${libraryItems}</ul>
  </details>`;
}

function coverageStatus(document: BrowserCloneCandidateDocument): string {
  const notes = [
    document.coverageIsComplete ? "Coverage complete" : "Coverage incomplete",
    document.resultLimitReached
      ? `result limit reached, ${document.resultLimitOmittedPairs.toLocaleString()} ranked pairs omitted`
      : "",
    document.scopeChangedDuringSearch ? "scope changed during the search" : "",
  ].filter(Boolean);
  return notes.join("; ") + ".";
}

function score(value: number): string {
  return `${(value / 100).toFixed(value % 100 === 0 ? 0 : 1)}%`;
}

function similarity(value: number): string {
  return `${(value * 100).toFixed(1)}%`;
}

function candidateEndpoint(
  label: string,
  method: BrowserCloneCandidateMethod,
  escapeHtml: (value: unknown) => string,
): string {
  const provenance = method.participant.provenance;
  const source = [
    provenance.packageId,
    provenance.packageVersion,
    provenance.tfm,
    provenance.framework,
  ].filter(Boolean).join(" · ") || String(provenance.kind);
  return `<section class="library-api-diff-endpoint">
    <h2>${escapeHtml(label)}</h2>
    <p><code>${escapeHtml(method.addressDisplay)}</code></p>
    <dl>
      <div><dt>Assembly</dt><dd>${escapeHtml(method.participant.assembly.name)}</dd></div>
      <div><dt>Source</dt><dd>${escapeHtml(source)}</dd></div>
      <div><dt>MVID</dt><dd><code>${escapeHtml(method.moduleVersionId)}</code></dd></div>
      <div><dt>MethodDef</dt><dd><code>0x${method.methodDefinitionToken.toString(16).toUpperCase().padStart(8, "0")}</code></dd></div>
    </dl>
  </section>`;
}

function renderCandidateEvidence(
  row: BrowserCloneCandidateRow,
  escapeHtml: (value: unknown) => string,
): string {
  const scores = [
    ["Overall", row.similarity.score],
    ["Operations", row.similarity.operationScore],
    ["Positions", row.similarity.positionScore],
    ["Blocks", row.similarity.blockScore],
    ["Edges", row.similarity.edgeScore],
    ["Locals", row.similarity.localScore],
  ] as const;
  // Retrieval similarity is the producer's ranking signal. No checked clone
  // relation is issued by the product for this pair, and none is implied.
  return `<div class="compare-clone-evidence" aria-label="Selected candidate pair">
    <div class="library-api-diff-member-detail">
      ${candidateEndpoint("Seed", row.left, escapeHtml)}
      ${candidateEndpoint("Candidate", row.right, escapeHtml)}
    </div>
    <section class="compare-clone-scores">
      <h2>Retrieval similarity</h2>
      <dl>${scores.map(([label, value]) =>
        `<div><dt>${label}</dt><dd>${score(value)}</dd></div>`).join("")}</dl>
      <p>Shape ${row.similarity.seedInstructions.toLocaleString()} → ${row.similarity.candidateInstructions.toLocaleString()} instructions · ${row.similarity.seedBlocks.toLocaleString()} → ${row.similarity.candidateBlocks.toLocaleString()} blocks · ${row.similarity.seedEdges.toLocaleString()} → ${row.similarity.candidateEdges.toLocaleString()} edges · ${row.similarity.seedLocals.toLocaleString()} → ${row.similarity.candidateLocals.toLocaleString()} locals</p>
      ${row.nameQualification
        ? `<p>Name admission: type ${similarity(row.nameQualification.declaringTypeSimilarity)} · member ${similarity(row.nameQualification.memberSimilarity)}</p>`
        : ""}
    </section>
    <section class="compare-clone-relation">
      <h2>Checked relation</h2>
      <p>None issued. Retrieval similarity ranks candidates; it does not establish that this pair is a clone.</p>
    </section>
  </div>`;
}

export interface CompareCloneRenderOptions {
  readonly subject: CompareSubjectKind;
  readonly subjectLabel: string;
  readonly targetText: string;
  readonly join: CompareCloneJoin | null;
  readonly selectedRank: number | null;
}

interface RenderedContent {
  readonly status: string;
  readonly content: string;
}

function renderLibraryRows(
  document: BrowserCloneCandidateDocument,
  join: CompareCloneJoin | null,
  escapeHtml: (value: unknown) => string,
): RenderedContent {
  if (document.seeds.length === 0) {
    return {
      status: `Clone search complete. No seed bodies. ${coverageStatus(document)}`,
      content: renderCompareEmpty(
        "No seed bodies",
        "The selected Library produced no method bodies to search from.",
        escapeHtml,
      ),
    };
  }
  const { groups, unjoined } = join === null
    ? { groups: [], unjoined: [...document.seeds] }
    : groupSeeds(document.seeds, join, method => ({
        key: method.typeIdentifier,
        display: method.typeDisplay,
      }));
  const rows = groups.map(group =>
    `<li class="library-api-diff-type"><button type="button" class="library-api-diff-row" data-compare-type-id="${escapeHtml(group.key)}" aria-label="Open ${escapeHtml(group.display)} Compare">
      <span class="library-api-diff-state">Type</span>
      <span class="library-api-diff-type-copy"><strong>${escapeHtml(group.display)}</strong><span>${escapeHtml(groupSummary(group))}</span></span>
    </button></li>`).join("");
  return {
    status: `Clone search complete. ${groups.length.toLocaleString()} Types with seed coverage. ${coverageStatus(document)}`,
    content: `<div class="library-api-diff-metrics">
        <span>${document.receipt.seedMethods.toLocaleString()} seed methods</span>
        <span>${document.receipt.rankedPairs.toLocaleString()} ranked pairs</span>
        <span>${document.receipt.admittedLibraries.toLocaleString()} libraries admitted</span>
      </div>
      ${rows
        ? `<ol class="library-api-diff-types" aria-label="Types with Clone seed coverage">${rows}</ol>`
        : renderCompareEmpty(
            "No Type rows",
            "Seed bodies could not be joined to a loaded Type; their evidence remains below.",
            escapeHtml,
          )}
      ${renderUnjoinedSeeds(unjoined, escapeHtml)}
      ${renderCoverageEvidence(document, escapeHtml)}`,
  };
}

function renderTypeRows(
  document: BrowserCloneCandidateDocument,
  join: CompareCloneJoin | null,
  escapeHtml: (value: unknown) => string,
): RenderedContent {
  const admitted = document.seeds.filter(seed => seed.rankedPairs > 0);
  if (document.seeds.length === 0) {
    return {
      status: `Clone search complete. No seed bodies. ${coverageStatus(document)}`,
      content: renderCompareEmpty(
        "No seed bodies",
        "The selected Type produced no method bodies to search from.",
        escapeHtml,
      ),
    };
  }
  const { groups, unjoined } = join === null
    ? { groups: [], unjoined: [...admitted] }
    : groupSeeds(admitted, join, method => ({
        key: method.memberFingerprint,
        display: method.memberDisplay,
      }));
  // Type Clone has no whole-Type action: every row is one logical Member.
  const rows = groups.map(group =>
    `<li class="library-api-diff-member"><button type="button" class="library-api-diff-row" data-compare-member-fingerprint="${escapeHtml(group.key)}" aria-label="Open ${escapeHtml(group.display)} Compare">
      <span class="library-api-diff-state">Member</span>
      <span class="library-api-diff-type-copy"><strong>${escapeHtml(group.display)}</strong><span>${escapeHtml(groupSummary(group))}</span></span>
    </button></li>`).join("");
  return {
    status: admitted.length === 0
      ? `Clone search complete. No ranked candidates were returned. ${coverageStatus(document)}`
      : `Clone search complete. ${groups.length.toLocaleString()} Members with ranked candidates. ${coverageStatus(document)}`,
    content: `<div class="library-api-diff-metrics">
        <span>${document.receipt.seedMethods.toLocaleString()} seed methods</span>
        <span>${document.receipt.rankedPairs.toLocaleString()} ranked pairs</span>
        <span>${document.receipt.admittedLibraries.toLocaleString()} libraries admitted</span>
      </div>
      ${rows
        ? `<ol class="library-api-diff-members" aria-label="Members with ranked Clone candidates">${rows}</ol>`
        : renderCompareEmpty(
            "No ranked candidates",
            "No seed body of this Type produced a ranked candidate pair.",
            escapeHtml,
          )}
      ${renderUnjoinedSeeds(unjoined, escapeHtml)}
      ${renderCoverageEvidence(document, escapeHtml)}`,
  };
}

function renderMemberRows(
  document: BrowserCloneCandidateDocument,
  selectedRank: number | null,
  escapeHtml: (value: unknown) => string,
): RenderedContent {
  if (document.rows.length === 0) {
    return {
      status: `Clone search complete. No ranked candidates were returned. ${coverageStatus(document)}`,
      content: renderCompareEmpty(
        "No ranked candidates",
        "No candidate pair was ranked for this Member.",
        escapeHtml,
      ) + renderCoverageEvidence(document, escapeHtml),
    };
  }
  const selected = document.rows.find(row => row.rank === selectedRank)
    ?? document.rows[0];
  const rows = document.rows.map(row =>
    `<li class="compare-clone-row"><button type="button" class="library-api-diff-row" data-compare-clone-rank="${row.rank}"${row === selected ? ' aria-current="true"' : ""}>
      <span class="library-api-diff-state">#${row.rank}</span>
      <span class="library-api-diff-type-copy"><strong>${escapeHtml(row.right.addressDisplay)}</strong><span>${escapeHtml(row.right.participant.assembly.name)} · retrieval ${score(row.similarity.score)}</span></span>
    </button></li>`).join("");
  return {
    status: `Clone search complete. ${document.rows.length.toLocaleString()} ranked candidates. ${coverageStatus(document)}`,
    content: `<div class="compare-clone-workbench">
      <ol class="library-api-diff-members compare-clone-list" aria-label="Ranked Clone candidates">${rows}</ol>
      ${selected ? renderCandidateEvidence(selected, escapeHtml) : ""}
    </div>
    ${renderCoverageEvidence(document, escapeHtml)}`,
  };
}

function renderAvailable(
  document: BrowserCloneCandidateDocument,
  options: CompareCloneRenderOptions,
  escapeHtml: (value: unknown) => string,
): RenderedContent {
  switch (options.subject) {
    case "library":
      return renderLibraryRows(document, options.join, escapeHtml);
    case "type":
      return renderTypeRows(document, options.join, escapeHtml);
    case "member":
      return renderMemberRows(document, options.selectedRank, escapeHtml);
    default: {
      const exhaustive: never = options.subject;
      throw new Error(`Unhandled Compare subject: ${String(exhaustive)}`);
    }
  }
}

function frame(
  rendered: RenderedContent,
  options: CompareCloneRenderOptions,
  escapeHtml: (value: unknown) => string,
): string {
  return renderCompareFrame({
    subjectKind: options.subject,
    subjectLabel: options.subjectLabel,
    mode: "clone",
    targetText: options.targetText,
    status: rendered.status,
    content: rendered.content,
    escapeHtml,
  });
}

export function renderCompareClone(
  state: CompareCloneState,
  escapeHtml: (value: unknown) => string,
  options: CompareCloneRenderOptions,
): string {
  switch (state.status) {
    case "idle":
      return frame(
        { status: "Choose a Package Library to search.", content: "" },
        options,
        escapeHtml,
      );
    case "unavailable":
      return frame(
        {
          status: state.message,
          content: renderCompareEmpty(
            "Clone search unavailable",
            state.message,
            escapeHtml,
          ),
        },
        options,
        escapeHtml,
      );
    case "loading":
      return frame(
        {
          status: "Searching structurally similar method bodies...",
          content: renderCompareLoading(),
        },
        options,
        escapeHtml,
      );
    case "failed":
      return frame(
        { status: state.error, content: renderCompareRetry("Retry search") },
        options,
        escapeHtml,
      );
    case "ready": {
      const result = state.result;
      switch (result.kind) {
        case "Available": {
          const document = result.document;
          if (document === null)
            throw new Error("Clone available result has no document.");
          return frame(
            renderAvailable(document, options, escapeHtml),
            options,
            escapeHtml,
          );
        }
        case "Rejected":
        case "Failed":
        case "Unrepresentable": {
          const detail = result.detail
            ?? result.failure?.detail
            ?? `Clone search ${result.kind.toLowerCase()}.`;
          const heading = result.kind === "Rejected"
            ? "Clone search rejected"
            : result.kind === "Failed"
              ? "Clone search failed"
              : `Clone result unrepresentable${result.presentationRejectionKind ? `: ${String(result.presentationRejectionKind)}` : ""}`;
          return frame(
            {
              status: `${heading}.`,
              content: renderCompareEmpty(heading, detail, escapeHtml)
                + (result.kind === "Failed" ? renderCompareRetry("Retry search") : ""),
            },
            options,
            escapeHtml,
          );
        }
        default:
          throw new Error("Unknown Clone result kind.");
      }
    }
    default: {
      const exhaustive: never = state;
      throw new Error(`Unhandled Clone state: ${String(exhaustive)}`);
    }
  }
}

export function bindCompareCloneRows(
  root: ParentNode,
  actions: { readonly selectRank: (rank: number) => void },
): void {
  for (const button of root.querySelectorAll<HTMLButtonElement>(
    "[data-compare-clone-rank]",
  )) {
    button.addEventListener("click", () => {
      const rank = Number(button.dataset.compareCloneRank);
      if (Number.isInteger(rank) && rank > 0) actions.selectRank(rank);
    });
  }
}
