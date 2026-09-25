import type {
  BrowserPackageChangesAdvisoryEvidence,
  BrowserPackageChangesAdvisoryReference,
  BrowserPackageChangesFailure,
  BrowserPackageChangesPackageSetDescriptor,
  BrowserPackageChangesProgress,
  BrowserPackageChangesRow,
} from "./facades/inspect-web-package.d.ts";
import { renderBrand } from "./brand.ts";
import type { PackageChangesState } from "./package-changes.ts";
import {
  capturePackageChangesViewport,
  resolvePackageChangesRowWindow,
  restorePackageChangesViewport,
  type PackageChangesViewportSnapshot,
} from "./package-changes-window.ts";

const MAXIMUM_INTERVAL_MILLISECONDS = 42 * 24 * 60 * 60 * 1_000;

export interface PackageChangesBindingActions {
  readonly onBack: () => void;
  readonly onCancel: () => void;
  readonly onResultViewportChange: () => void;
  readonly onRun: (
    packageSetId: string,
    fromExclusive: string | null,
    throughInclusive: string | null,
    securityOnly: boolean,
    maximumRows: number,
  ) => void;
}

export interface RenderPackageChangesOptions {
  readonly state: PackageChangesState;
  readonly packageSets: readonly BrowserPackageChangesPackageSetDescriptor[];
  readonly catalogError?: string;
  readonly viewport?: PackageChangesViewportSnapshot | null;
  readonly escapeHtml: (value: unknown) => string;
}

export function bindPackageChangesView(
  root: ParentNode,
  actions: PackageChangesBindingActions,
): void {
  root.querySelector("#package-changes-back")
    ?.addEventListener("click", actions.onBack);
  root.querySelector("#package-changes-cancel")
    ?.addEventListener("click", actions.onCancel);
  root.querySelector(".query-main")
    ?.addEventListener("scroll", actions.onResultViewportChange, {
      passive: true,
    });
  const custom = root.querySelector<HTMLInputElement>(
    "#package-changes-custom-interval");
  const intervalFields = () => [
    root.querySelector<HTMLInputElement>("#package-changes-from"),
    root.querySelector<HTMLInputElement>("#package-changes-through"),
  ];
  const clearIntervalValidity = () => {
    for (const input of intervalFields()) input?.setCustomValidity("");
  };
  const synchronizeInterval = () => {
    for (const input of intervalFields()) {
      if (input) input.disabled = custom?.checked !== true;
    }
  };
  custom?.addEventListener("change", synchronizeInterval);
  for (const input of intervalFields()) {
    input?.addEventListener("input", clearIntervalValidity);
  }
  synchronizeInterval();
  const packageSet =
    root.querySelector<HTMLSelectElement>("#package-changes-package-set");
  const packageSetSummary =
    root.querySelector<HTMLElement>(".package-changes-package-set-summary");
  packageSet?.addEventListener("change", () => {
    const option = packageSet.selectedOptions[0];
    if (packageSetSummary && option) {
      packageSetSummary.textContent = option.dataset.packageSetSummary ?? "";
    }
  });

  root.querySelector<HTMLFormElement>("#package-changes-form")
    ?.addEventListener("submit", event => {
      event.preventDefault();
      const form = event.currentTarget;
      if (!(form instanceof HTMLFormElement)) return;
      const maximumRows =
        root.querySelector<HTMLInputElement>("#package-changes-limit");
      const securityOnly =
        root.querySelector<HTMLInputElement>("#package-changes-security-only");
      const from = intervalFields()[0];
      const through = intervalFields()[1];
      from?.setCustomValidity("");
      through?.setCustomValidity("");
      if (!packageSet || !maximumRows || !securityOnly || !form.reportValidity()) {
        return;
      }
      const interval = custom?.checked === true
        ? validatePackageChangesInterval(
            from?.value ?? "",
            through?.value ?? "")
        : { fromExclusive: null, throughInclusive: null };
      if (typeof interval === "string") {
        from?.setCustomValidity(interval);
        through?.setCustomValidity(interval);
        form.reportValidity();
        return;
      }
      from?.setCustomValidity("");
      through?.setCustomValidity("");
      actions.onRun(
        packageSet.value,
        interval.fromExclusive,
        interval.throughInclusive,
        securityOnly.checked,
        Number(maximumRows.value));
    });
}

export function renderPackageChangesView(
  options: RenderPackageChangesOptions,
): string {
  const {
    state,
    packageSets,
    catalogError = "",
    viewport = null,
    escapeHtml,
  } = options;
  const selectedId = state.request?.packageSetId ?? packageSets[0]?.id ?? "";
  const explicitInterval = state.request?.fromExclusive !== null
    && state.request?.fromExclusive !== undefined;
  const selected = packageSets.find(packageSet => packageSet.id === selectedId);
  return `
    <div class="query-page package-changes-page">
      <header class="query-page-bar">
        ${renderBrand({ id: "package-changes-product" })}
        <div class="query-page-navigation">
          <button id="package-changes-back" type="button">Back</button>
        </div>
      </header>
      <main class="query-main">
        <div class="query-heading">
          <p class="query-kicker">Product package sets · NuGet catalog and advisory evidence</p>
          <h1 id="package-changes-heading" tabindex="-1">Package Activity</h1>
          <p>Inspect a bounded package-set interval. Results preserve producer order, evidence availability, source coverage, and typed completion.</p>
        </div>
        ${catalogError
          ? `<div class="query-navigation-error" role="alert">${escapeHtml(catalogError)}</div>`
          : ""}
        <form id="package-changes-form" class="package-changes-form">
          <label for="package-changes-package-set">Package set</label>
          <select id="package-changes-package-set" required${packageSets.length ? "" : " disabled"}>
            ${packageSets.map(packageSet =>
              `<option value="${escapeHtml(packageSet.id)}" data-package-set-summary="${escapeHtml(packageSet.summary)}"${packageSet.id === selectedId ? " selected" : ""}>${escapeHtml(packageSet.title)}</option>`).join("")}
          </select>
          <p class="package-changes-package-set-summary">${escapeHtml(selected?.summary ?? "The product package-set catalog is unavailable.")}</p>
          <label class="package-changes-check">
            <input id="package-changes-custom-interval" type="checkbox"${explicitInterval ? " checked" : ""} />
            Use a custom UTC interval
          </label>
          <div class="package-changes-interval">
            <label for="package-changes-from">After (exclusive, UTC)</label>
            <input id="package-changes-from" type="datetime-local" step="1" value="${escapeHtml(toDateTimeLocal(state.request?.fromExclusive))}" />
            <label for="package-changes-through">Through (inclusive, UTC)</label>
            <input id="package-changes-through" type="datetime-local" step="1" value="${escapeHtml(toDateTimeLocal(state.request?.throughInclusive))}" />
          </div>
          <label class="package-changes-check">
            <input id="package-changes-security-only" type="checkbox"${state.request?.securityOnly ? " checked" : ""} />
            Security-relevant activity only
          </label>
          <label for="package-changes-limit">Maximum results</label>
          <input id="package-changes-limit" type="number" min="1" max="1000" step="1" value="${state.request?.maximumRows ?? 100}" required />
          <div class="package-changes-actions">
            <button id="package-changes-run" class="primary-action" type="submit"${packageSets.length ? "" : " disabled"}>Run report</button>
            <button id="package-changes-cancel" type="button"${state.settlement.kind === "running" ? "" : " disabled"}>Cancel</button>
          </div>
          <p class="query-facet-disclosure">The default interval is the product-owned previous 42 days. Custom endpoints must be paired, increasing UTC values no more than 42 days apart.</p>
        </form>
        <section id="package-changes-results" class="query-results package-changes-results" aria-label="Package Activity results" tabindex="-1">
          ${renderPackageChangesStream(state, escapeHtml, viewport)}
        </section>
      </main>
    </div>`;
}

export function patchPackageChangesStream(
  root: ParentNode,
  options: Omit<RenderPackageChangesOptions, "packageSets" | "catalogError">,
): boolean {
  const stream = root.querySelector<HTMLElement>("#package-changes-results");
  if (!stream) return false;
  const viewport = options.viewport ?? capturePackageChangesViewport(root);
  const focusKey =
    root instanceof Document
      && root.activeElement instanceof HTMLElement
      ? root.activeElement.dataset.packageChangesFocusKey ?? null
      : null;
  stream.innerHTML = renderPackageChangesStream(
    options.state,
    options.escapeHtml,
    viewport);
  const cancel =
    root.querySelector<HTMLButtonElement>("#package-changes-cancel");
  if (cancel) cancel.disabled = options.state.settlement.kind !== "running";
  restorePackageChangesViewport(root, viewport);
  if (focusKey !== null) {
    root.querySelector<HTMLElement>(
      `[data-package-changes-focus-key="${CSS.escape(focusKey)}"]`)
      ?.focus({ preventScroll: true });
  }
  return true;
}

export function validatePackageChangesInterval(
  fromValue: string,
  throughValue: string,
):
  | string
  | { fromExclusive: string; throughInclusive: string } {
  fromValue = fromValue.trim();
  throughValue = throughValue.trim();
  if (!fromValue || !throughValue) {
    return "Enter both custom UTC interval endpoints.";
  }
  const fromUtc = parseUtcInput(fromValue);
  const throughUtc = parseUtcInput(throughValue);
  if (fromUtc === null || throughUtc === null) {
    return "Enter valid UTC dates and times.";
  }
  if (throughUtc.milliseconds <= fromUtc.milliseconds) {
    return "The through time must be later than the after time.";
  }
  if (throughUtc.milliseconds - fromUtc.milliseconds
      > MAXIMUM_INTERVAL_MILLISECONDS) {
    return "The custom interval cannot exceed 42 days.";
  }
  return {
    fromExclusive: fromUtc.roundTrip,
    throughInclusive: throughUtc.roundTrip,
  };
}

function parseUtcInput(
  value: string,
): { milliseconds: number; roundTrip: string } | null {
  const match =
    /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(value);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const hour = Number(match[4]);
  const minute = Number(match[5]);
  const second = Number(match[6] ?? 0);
  const milliseconds = Date.UTC(
    year, month - 1, day, hour, minute, second);
  const date = new Date(milliseconds);
  if (date.getUTCFullYear() !== year
      || date.getUTCMonth() + 1 !== month
      || date.getUTCDate() !== day
      || date.getUTCHours() !== hour
      || date.getUTCMinutes() !== minute
      || date.getUTCSeconds() !== second) {
    return null;
  }
  return {
    milliseconds,
    roundTrip:
      `${value.length === 16 ? `${value}:00` : value}.0000000Z`,
  };
}

function toDateTimeLocal(value: string | null | undefined): string {
  if (!value) return "";
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})/.exec(value);
  return match?.[1] ?? "";
}

function renderPackageChangesStream(
  state: PackageChangesState,
  escapeHtml: (value: unknown) => string,
  viewport: PackageChangesViewportSnapshot | null,
): string {
  const status = renderStatus(state, escapeHtml);
  const progress = renderProgress(state.progress, escapeHtml);
  const failures = renderFailures(state.failures, escapeHtml);
  const coverage = state.settlement.kind === "succeeded"
    ? renderCoverage(state.settlement.inspection, escapeHtml)
    : "";
  const rowWindow = resolvePackageChangesRowWindow(state.rows.length, viewport);
  const rows = state.rows.slice(rowWindow.start, rowWindow.end)
    .map((row, relativeIndex) =>
      renderRow(row, rowWindow.start + relativeIndex, state.rows.length, escapeHtml))
    .join("");
  const list = state.rows.length
    ? `<div id="package-changes-row-window" class="query-list package-changes-list" role="list" aria-label="Activity events ${rowWindow.start + 1} through ${rowWindow.end} of ${state.rows.length}" data-changes-row-count="${state.rows.length}" data-changes-row-extent="${rowWindow.rowExtent}">
        ${rowWindow.beforeHeight
          ? `<div class="query-row-spacer" aria-hidden="true" style="height:${rowWindow.beforeHeight.toFixed(2)}px"></div>`
          : ""}
        ${rows}
        ${rowWindow.afterHeight
          ? `<div class="query-row-spacer" aria-hidden="true" style="height:${rowWindow.afterHeight.toFixed(2)}px"></div>`
          : ""}
      </div>`
    : renderEmpty(state);
  return `${status}${progress}${failures}${coverage}${list}`;
}

function renderStatus(
  state: PackageChangesState,
  escapeHtml: (value: unknown) => string,
): string {
  const settlement = state.settlement;
  let text: string;
  let detail = "";
  switch (settlement.kind) {
    case "idle":
      text = "Ready";
      detail = "Choose a product package set and run a report.";
      break;
    case "running":
      text = `${state.rows.length.toLocaleString()} activity events · streaming…`;
      detail = "Progressive rows remain visible if the operation is canceled.";
      break;
    case "canceling":
      text = `${state.rows.length.toLocaleString()} activity events · canceling…`;
      detail = settlement.reason === "user"
        ? "Cancellation requested; waiting for the Worker to settle."
        : settlement.reason === "superseded"
          ? "A newer operation replaced this report; waiting for the prior Worker operation to settle."
          : "The report was left; waiting for the Worker operation to settle.";
      break;
    case "canceled":
      text = `${state.rows.length.toLocaleString()} activity events · canceled`;
      detail = settlement.reason === "superseded"
        ? "A newer operation or mode replaced this report."
        : settlement.reason === "disposed"
          ? "The report was canceled after leaving Activity mode; admitted rows are retained."
          : "The operation was canceled explicitly; admitted rows are retained.";
      break;
    case "failed":
      text = `${state.rows.length.toLocaleString()} activity events · operation failed`;
      detail = settlement.error;
      if (settlement.diagnostic) detail += ` Diagnostic: ${settlement.diagnostic}`;
      break;
    case "succeeded":
      text = `${state.rows.length.toLocaleString()} activity events · ${completionLabel(settlement.inspection.content.summary.completion)}`;
      detail = completionDescription(
        settlement.inspection.content.summary.completion);
      break;
  }
  return `<section class="query-summary package-changes-summary" aria-live="polite"><strong>${escapeHtml(text)}</strong><span>${escapeHtml(detail)}</span></section>`;
}

function renderProgress(
  progress: readonly BrowserPackageChangesProgress[],
  escapeHtml: (value: unknown) => string,
): string {
  if (!progress.length) return "";
  return `<section class="package-changes-progress" aria-label="Report progress"><h2>Progress</h2><ul>${progress.map(item => {
    const total = item.total === null ? "" : ` of ${item.total}`;
    return `<li><strong>${escapeHtml(progressLabel(item.phase))}</strong>: ${item.completed.toLocaleString()}${total}${item.capturedHorizon ? ` · horizon ${escapeHtml(item.capturedHorizon)}` : ""}</li>`;
  }).join("")}</ul></section>`;
}

function renderFailures(
  failures: readonly BrowserPackageChangesFailure[],
  escapeHtml: (value: unknown) => string,
): string {
  if (!failures.length) return "";
  return `<section class="query-failures package-changes-failures" aria-label="Report failures" aria-live="polite"><h2>Failures (${failures.length})</h2><ul>${failures.map(failure => {
    const receipt = failure.packageReceiptFailure;
    const source = failure.catalogFailure ?? receipt?.failure ?? null;
    const packageId =
      source?.packageId ?? receipt?.catalogActivity.packageId ?? null;
    const version =
      source?.version ?? receipt?.catalogActivity.version ?? null;
    const coordinate = packageId
      ? `${packageId}${version ? `@${version}` : ""}`
      : "Report";
    return `<li><strong>${escapeHtml(failureProviderLabel(failure.provider))}</strong> · ${escapeHtml(coordinate)}${failure.advisoryFailure ? ` · ${escapeHtml(advisoryFailureLabel(failure.advisoryFailure))}` : ""}${source ? ` · package source capability ${source.capability}: ${escapeHtml(source.kind)} — ${escapeHtml(source.detail)}` : ""}</li>`;
  }).join("")}</ul></section>`;
}

function renderCoverage(
  inspection: Extract<
    PackageChangesState["settlement"],
    { kind: "succeeded" }
  >["inspection"],
  escapeHtml: (value: unknown) => string,
): string {
  const { request, source, summary: completion } = inspection.content;
  return `<section class="package-changes-coverage" aria-label="Report completion and coverage">
    <h2>Completion and coverage</h2>
    <dl>
      <div><dt>Package scope</dt><dd>${escapeHtml(request.packageScope.selectionId ?? request.packageScope.kind)} · ${request.packageScope.packageIds.length.toLocaleString()} package IDs</dd></div>
      <div><dt>Requested interval</dt><dd>${escapeHtml(request.fromExclusive)} (exclusive) through ${escapeHtml(request.throughInclusive)} (inclusive)${request.usedDefaultInterval ? " · default interval" : ""}</dd></div>
      <div><dt>Catalog horizon</dt><dd>${escapeHtml(completion.capturedHorizon ?? "Unavailable")}</dd></div>
      <div><dt>Catalog source</dt><dd>${escapeHtml(source.producer)} · ${escapeHtml(source.transportKind)} · ${escapeHtml(completion.catalogCompletion)}</dd></div>
      <div><dt>Catalog work</dt><dd>${completion.catalogPagesAcquired.toLocaleString()} pages · ${completion.catalogHttpAttempts.toLocaleString()} HTTP attempts · ${completion.catalogDecodedBytes.toLocaleString()} decoded bytes</dd></div>
      <div><dt>Events</dt><dd>${completion.catalogInWindowEventCount.toLocaleString()} in interval · ${completion.matchingEventCount.toLocaleString()} matching · ${completion.retainedEventCount.toLocaleString()} retained${completion.candidateLimitReached ? " · candidate limit reached" : ""}</dd></div>
      <div><dt>Advisory evidence</dt><dd>${escapeHtml(completion.advisoryEvidence.advisoryProducer)} observed ${escapeHtml(completion.advisoryEvidence.observedAt)} · ${completion.advisoryEvidence.complete ? "complete" : "partial"} · ${completion.advisoryEvidence.apiRequests.toLocaleString()} requests</dd></div>
      <div><dt>Receipt evidence</dt><dd>${completion.receiptSuccesses.toLocaleString()} successes · ${completion.receiptFailures.length.toLocaleString()} failures from ${completion.receiptRequests.toLocaleString()} requests${completion.receiptLimitReached ? " · receipt limit reached" : ""}</dd></div>
      <div><dt>Unevaluable populations</dt><dd>${completion.currentContextUnevaluableRows.toLocaleString()} current-context · ${completion.securityReleaseUnevaluableRows.toLocaleString()} security-release</dd></div>
      <div><dt>Returned rows</dt><dd>${completion.returnedRowCount.toLocaleString()} of ${completion.eligibleRowCount.toLocaleString()} eligible${completion.resultLimitReached ? " · result limit reached" : ""}</dd></div>
    </dl>
    ${inspection.share.kind === "NonProjectable"
      ? `<p class="query-facet-disclosure">Sharing is unavailable: ${escapeHtml(inspection.share.reason ?? "this report is not projectable.")}</p>`
      : ""}
  </section>`;
}

function renderRow(
  row: BrowserPackageChangesRow,
  index: number,
  rowCount: number,
  escapeHtml: (value: unknown) => string,
): string {
  const activity = row.catalogActivity;
  const packageHref = packageUrl(activity.packageId, activity.version);
  const catalogLeafHref = safeExternalHref(activity.leafUrl);
  const securityRelease = row.securityReleaseStatus === "EvidencedInInterval"
    && row.securityRelease !== null
    ? `<p class="package-changes-security-positive"><strong>Security release evidenced in interval</strong> · receipt ${escapeHtml(row.securityRelease.receipt.receivedAt)} (${escapeHtml(row.securityRelease.receipt.basis)})</p>`
    : `<p><strong>Security-release evaluation:</strong> ${escapeHtml(securityReleaseLabel(row.securityReleaseStatus))}</p>`;
  return `<article class="query-row package-changes-row" role="listitem" data-changes-row-index="${index}" aria-posinset="${index + 1}" aria-setsize="${rowCount}">
    <header>
      <p class="package-changes-activity">${escapeHtml(activityLabel(activity.activity))} · ${escapeHtml(catalogKindLabel(activity.catalogKind))}</p>
      <h2><a href="${packageHref}" target="_blank" rel="noopener noreferrer" data-package-changes-focus-key="package-${index}">${escapeHtml(activity.packageId)} <span>${escapeHtml(activity.version)}</span></a></h2>
      <p>${escapeHtml(activity.commitTimestamp)}</p>
    </header>
    <p><strong>Catalog event:</strong> ${escapeHtml(activity.commitId)}${catalogLeafHref
      ? ` · <a href="${escapeHtml(catalogLeafHref)}" target="_blank" rel="noopener noreferrer" data-package-changes-focus-key="catalog-${index}">Catalog leaf</a>`
      : ` · ${escapeHtml(activity.leafUrl)}`}</p>
    ${securityRelease}
    <div class="package-changes-evidence-grid">
      ${renderAdvisoryEvidence("Current advisory context", row.currentAdvisoryContext, `current-${index}`, escapeHtml)}
      ${renderAdvisoryEvidence("Exact fixed-version evidence", row.fixedVersionEvidence, `fixed-${index}`, escapeHtml)}
    </div>
    ${row.packageReceipt
      ? `<p><strong>Package receipt:</strong> ${escapeHtml(row.packageReceipt.receivedAt)} · ${escapeHtml(row.packageReceipt.basis)}</p>`
      : `<p><strong>Package receipt:</strong> unavailable</p>`}
  </article>`;
}

function renderAdvisoryEvidence(
  heading: string,
  evidence: BrowserPackageChangesAdvisoryEvidence,
  focusPrefix: string,
  escapeHtml: (value: unknown) => string,
): string {
  let status: string;
  if (evidence.availability === "Complete") {
    status = evidence.advisories.length
      ? `Checked · ${evidence.advisories.length} matching advisory references`
      : "Checked · no matching advisory in acquired reviewed data";
  } else if (evidence.availability === "Partial") {
    status = evidence.advisories.length
      ? `Partial · ${evidence.advisories.length} references acquired`
      : "Partial · no matching reference was acquired";
  } else {
    status = "Unavailable · advisory evidence could not be evaluated";
  }
  return `<section><h3>${heading}</h3><p>${escapeHtml(status)}</p>${renderAdvisories(evidence.advisories, focusPrefix, escapeHtml)}</section>`;
}

function renderAdvisories(
  advisories: readonly BrowserPackageChangesAdvisoryReference[],
  focusPrefix: string,
  escapeHtml: (value: unknown) => string,
): string {
  if (!advisories.length) return "";
  return `<ul>${advisories.map((advisory, advisoryIndex) => {
    const href = safeExternalHref(advisory.advisoryUrl);
    const identity = advisory.cveId
      ? `${advisory.ghsaId} · ${advisory.cveId}`
      : advisory.ghsaId;
    return `<li>${href
      ? `<a href="${escapeHtml(href)}" target="_blank" rel="noopener noreferrer" data-package-changes-focus-key="${focusPrefix}-${advisoryIndex}">${escapeHtml(identity)}</a>`
      : escapeHtml(identity)} · ${escapeHtml(advisory.severity)} · published ${escapeHtml(advisory.publishedAt)} · updated ${escapeHtml(advisory.updatedAt)}</li>`;
  }).join("")}</ul>`;
}

function renderEmpty(state: PackageChangesState): string {
  if (state.settlement.kind === "succeeded") {
    return `<section class="query-empty"><span class="large-glyph">◇</span><h2>No returned activity</h2><p>The completion and coverage evidence above describes whether this is a complete, bounded, partial, or failed semantic result.</p></section>`;
  }
  return `<section class="query-empty"><span class="large-glyph">◇</span><h2>No activity yet</h2><p>Run a report to stream Package Activity evidence.</p></section>`;
}

function packageUrl(packageId: string, version: string): string {
  return `https://www.nuget.org/packages/${encodeURIComponent(packageId)}/${encodeURIComponent(version)}`;
}

export function safeExternalHref(value: string): string | null {
  try {
    const url = new URL(value);
    return url.protocol === "https:" ? url.href : null;
  } catch {
    return null;
  }
}

function completionLabel(value: string): string {
  switch (value) {
    case "Complete": return "complete";
    case "ResultLimitReached": return "result limit reached";
    case "Partial": return "partial";
    case "Failed": return "failed";
    default: return `unknown completion (${value})`;
  }
}

function completionDescription(value: string): string {
  switch (value) {
    case "Complete":
      return "The requested bounded report completed with complete source evidence.";
    case "ResultLimitReached":
      return "The report completed, but the requested result bound omitted eligible rows.";
    case "Partial":
      return "Rows are available, but one or more evidence sources are incomplete or unavailable.";
    case "Failed":
      return "The report reached a typed terminal document whose semantic result failed.";
    default:
      return "The report returned an unrecognized completion value.";
  }
}

function progressLabel(value: string): string {
  switch (value) {
    case "Catalog": return "NuGet catalog";
    case "Advisory": return "Advisory acquisition";
    case "PackageReceipt": return "Package receipts";
    default: return value;
  }
}

function activityLabel(value: string): string {
  switch (value) {
    case "SnapshotObserved": return "Package snapshot observed";
    case "DeletionObserved": return "Package deletion observed";
    default: return value;
  }
}

function catalogKindLabel(value: string): string {
  switch (value) {
    case "Details": return "Catalog details";
    case "Delete": return "Catalog delete";
    default: return value;
  }
}

function securityReleaseLabel(value: string): string {
  switch (value) {
    case "EvidencedInInterval": return "evidenced in interval";
    case "ReceiptOutsideInterval": return "fixed-version receipt is outside the interval";
    case "CheckedNoFixedVersionAssociation": return "checked; no fixed-version association";
    case "FixedVersionEvidenceUnavailable": return "fixed-version evidence unavailable";
    case "DeleteActivityUnevaluable": return "delete activity is unevaluable";
    case "ReceiptFailure": return "package receipt acquisition failed";
    case "ReceiptLimitReached": return "package receipt limit reached";
    default: return value;
  }
}

function failureProviderLabel(value: string): string {
  switch (value) {
    case "Catalog": return "NuGet catalog";
    case "Advisory": return "Advisory provider";
    case "PackageReceipt": return "Package receipt provider";
    default: return value;
  }
}

function advisoryFailureLabel(value: string): string {
  switch (value) {
    case "SourceUnavailable": return "source unavailable";
    case "RequestLimitReached": return "request limit reached";
    case "ResponseByteLimitReached": return "response byte limit reached";
    case "AggregateResponseByteLimitReached":
      return "aggregate response byte limit reached";
    case "DeadlineReached": return "deadline reached";
    case "RateLimitOrForbidden": return "rate limited or forbidden";
    case "InvalidData": return "invalid advisory data";
    case "InvalidContinuation": return "invalid advisory continuation";
    default: return value;
  }
}
