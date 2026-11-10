import type {
  BrowserMemberGroupDocumentInspection,
  BrowserMemberGroupDocumentRow,
} from "./facades/inspect-web-metadata.d.ts";

type MemberDocumentationStatus = "loaded" | "loading" | "error";

interface MemberContractParameter {
  readonly name: string;
  readonly type: string;
  readonly modifier: string | null;
  readonly hasDefault: boolean;
  readonly defaultValue: string | null;
  readonly description: string | null;
}

interface MemberContractException {
  readonly type: string;
  readonly description: string;
}

export interface MemberContractModel {
  readonly parameters: readonly MemberContractParameter[];
  readonly returnType: string | null;
  readonly returns: string | null;
  readonly exceptions: readonly MemberContractException[];
  readonly activeFramework: string;
  readonly documentationStatus: MemberDocumentationStatus;
}

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function parameterDocumentation(
  parameter: MemberContractParameter,
  status: MemberDocumentationStatus,
) {
  if (status === "loading") {
    return {
      className: "docs-loading",
      text: "Loading parameter documentation…",
    };
  }
  if (status === "error") {
    return {
      className: "docs-unavailable",
      text: "Parameter documentation is unavailable.",
    };
  }
  return parameter.description
    ? {
        className: "member-contract-description",
        text: parameter.description,
      }
    : {
        className: "docs-unavailable",
        text:
          "No parameter documentation was found in the package XML documentation.",
      };
}

function renderParameters(model: MemberContractModel) {
  if (model.parameters.length === 0) return "";
  const count =
    `${model.parameters.length} parameter${model.parameters.length === 1 ? "" : "s"}`;
  return `
    <section class="learn-section member-contract-section member-parameters" aria-labelledby="member-parameters-title">
      <div class="member-contract-heading">
        <h2 id="member-parameters-title">Parameters</h2>
        <span>${count}</span>
      </div>
      <dl class="member-contract-list parameter-docs">${model.parameters.map(parameter => {
        const documentation = parameterDocumentation(
          parameter,
          model.documentationStatus);
        return `
          <div>
            <dt>
              <code class="member-contract-name">${escapeHtml(parameter.name)}</code>
              <code class="member-contract-type">${escapeHtml([parameter.modifier, parameter.type].filter(Boolean).join(" "))}</code>
              ${parameter.hasDefault ? `<span class="member-contract-default">Default <code>${escapeHtml(parameter.defaultValue ?? "default")}</code></span>` : ""}
            </dt>
            <dd><p class="${documentation.className}">${escapeHtml(documentation.text)}</p></dd>
          </div>`;
      }).join("")}</dl>
    </section>`;
}

function renderReturns(model: MemberContractModel) {
  if (!model.returns) return "";
  const returnIdentity = model.returnType
    ? `<code class="member-contract-type">${escapeHtml(model.returnType)}</code>`
    : '<span class="member-contract-identity-unavailable">Type unavailable</span>';
  return `
    <section class="learn-section member-contract-section member-returns" aria-labelledby="member-returns-title">
      <div class="member-contract-heading"><h2 id="member-returns-title">Returns</h2></div>
      <dl class="member-contract-list">
        <div>
          <dt>${returnIdentity}</dt>
          <dd><p class="member-contract-description">${escapeHtml(model.returns)}</p></dd>
        </div>
      </dl>
    </section>`;
}

function renderExceptions(model: MemberContractModel) {
  const status = model.documentationStatus === "loading"
    ? "compiled documentation"
    : model.documentationStatus === "error"
      ? "unavailable"
      : `${model.exceptions.length} documented`;
  const content = model.documentationStatus === "loading"
    ? '<p class="docs-loading">Loading documented exceptions…</p>'
    : model.documentationStatus === "error"
      ? '<p class="docs-unavailable">Exception documentation is unavailable.</p>'
      : model.exceptions.length > 0
        ? `<dl class="member-contract-list exception-docs">${model.exceptions.map(exception => `
            <div>
              <dt><code class="member-contract-type">${escapeHtml(exception.type)}</code></dt>
              <dd><p class="member-contract-description">${escapeHtml(exception.description)}</p></dd>
            </div>`).join("")}</dl>`
        : '<p class="docs-unavailable">No exceptions are documented for this overload.</p>';
  return `
    <section class="learn-section member-contract-section member-exceptions" aria-labelledby="member-exceptions-title">
      <div class="member-contract-heading">
        <h2 id="member-exceptions-title">Exceptions</h2>
        <span>${status}</span>
      </div>
      ${content}
    </section>`;
}

export function renderMemberContractSections(model: MemberContractModel) {
  return `
    ${renderParameters(model)}
    ${renderReturns(model)}
    ${renderExceptions(model)}
    <footer class="member-applicability" aria-label="Applies to">
      <span>Applies to</span>
      <code>${escapeHtml(model.activeFramework)}</code>
    </footer>`;
}

export function renderMemberDeclarations(options: {
  name: string;
  kind: string;
  typeName: string;
  inspection: BrowserMemberGroupDocumentInspection | null;
  loading: boolean;
  error: string;
  renderSignature: (row: BrowserMemberGroupDocumentRow) => string;
}) {
  const document = options.inspection?.outcome === "Available"
    ? options.inspection.document : null;
  const error = options.error || (options.inspection && !document
    ? options.inspection.detail ?? "Exact declarations are unavailable." : "");
  const status = error
    ? `<p class="inspection-error" role="alert">${escapeHtml(error)}</p>`
    : options.loading
      ? `<p class="docs-loading" role="status">${document
        ? "Loading Member details…" : "Loading exact declarations…"}</p>`
      : document ? "" : '<p class="docs-unavailable">Exact declarations are unavailable.</p>';
  const content = document
    ? document.rows.map(row => `
        <section class="signature-panel">
          <div class="signature-language"><h2><span>C#</span><small>declaration ${row.baselineOrdinal}</small></h2></div>
          <pre class="language-csharp signature-code"><code class="language-csharp">${options.renderSignature(row)}</code></pre>
        </section>`).join("")
    : "";
  return `<section class="member-surface member-overload-surface" aria-labelledby="member-surface-title">
    <header class="api-surface-head member-surface-head">
      <h1 id="member-surface-title">${escapeHtml(options.name)}</h1>
      <p>${document ? `${document.count} exact declaration${document.count === 1 ? "" : "s"}` : "Exact declarations"}
        <span>· ${escapeHtml(options.kind)}</span></p>
      <button class="member-back" id="member-back" title="Back to ${escapeHtml(options.typeName)}">← Type</button>
    </header>
    <div class="member-surface-scroll">${status}${content}</div>
  </section>`;
}
