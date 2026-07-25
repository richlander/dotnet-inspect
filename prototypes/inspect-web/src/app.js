import { lenses, rootCommands } from "./data.js";
import { initializeEngine, inspectMemberCallGraph, inspectMemberDocumentation, inspectMemberFacts, inspectMemberSource, inspectPackage, inspectSearchTypes, inspectTypeSource } from "/engine.js";

let spotlightCache = null;

const state = {
  theme: localStorage.getItem("inspect-theme") === "light" ? "light" : "dark",
  packages: [],
  package: null,
  requestedPackage: "System.Text.Json",
  requestedVersion: "10.0.0",
  requestedFramework: "net10.0",
  selectedTypeId: "",
  selectedMemberKey: "",
  selectedOverloadIndex: null,
  memberSection: "overview",
  memberSource: null,
  memberSourceLoading: false,
  memberSourceError: "",
  typeSource: null,
  typeSourceLoading: false,
  typeSourceError: "",
  typeSourceKey: "",
  memberCallGraph: null,
  memberCallGraphLoading: false,
  memberCallGraphError: "",
  memberFacts: null,
  memberFactsLoading: false,
  memberFactsError: "",
  memberDocumentationLoading: false,
  memberDocumentationError: "",
  lens: "api",
  typeFilter: "",
  namespaceFilter: "",
  command: "",
  completionIndex: 0,
  promptOpen: false,
  spotlightOpen: false,
  spotlightQuery: "",
  spotlightIndex: 0,
  typeCursor: 0,
  history: [],
  loading: true,
  loadingMessage: "Starting browser inspection engine…",
  error: "",
  diag: null
};

const nav = { stack: [], index: -1 };

function viewSignature() {
  return JSON.stringify({
    p: state.package?.id ?? "",
    l: state.lens,
    t: state.selectedTypeId,
    m: state.selectedMemberKey,
    o: state.selectedOverloadIndex,
    s: state.memberSection
  });
}

function captureView() {
  return {
    package: state.package?.id ?? "",
    lens: state.lens,
    selectedTypeId: state.selectedTypeId,
    selectedMemberKey: state.selectedMemberKey,
    selectedOverloadIndex: state.selectedOverloadIndex,
    memberSection: state.memberSection
  };
}

function recordNav() {
  if (!state.package) return;
  const sig = viewSignature();
  if (nav.index >= 0 && nav.stack[nav.index]?.sig === sig) return;
  nav.stack = nav.stack.slice(0, nav.index + 1);
  nav.stack.push({ sig, view: captureView() });
  nav.index = nav.stack.length - 1;
}

function applyView(view) {
  const pkg = state.packages.find(item => item.id === view.package);
  if (pkg) state.package = pkg;
  state.lens = view.lens;
  state.selectedTypeId = view.selectedTypeId;
  state.selectedMemberKey = view.selectedMemberKey;
  state.selectedOverloadIndex = view.selectedOverloadIndex;
  state.memberSection = view.memberSection;
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  const type = selectedType();
  if (state.lens === "api" && state.selectedMemberKey && selectedMember(type)) {
    if (state.memberSection === "source") loadSelectedMemberSource();
    else if (state.memberSection === "call-graph") loadSelectedMemberCallGraph();
    else if (state.memberSection === "facts") loadSelectedMemberFacts();
    else loadSelectedMemberDocumentation();
  } else {
    render();
  }
}

function navBack() {
  if (nav.index <= 0) return;
  nav.index -= 1;
  applyView(nav.stack[nav.index].view);
}

function navForward() {
  if (nav.index >= nav.stack.length - 1) return;
  nav.index += 1;
  applyView(nav.stack[nav.index].view);
}

const params = new URLSearchParams(location.search);
const route = location.pathname.split("/").filter(Boolean);
const packageAt = route.findIndex(part => part.toLowerCase() === "packages");
const linkedPackage = packageAt >= 0 ? decodeURIComponent(route[packageAt + 1] || "") : params.get("package");
const linkedVersion = packageAt >= 0 ? decodeURIComponent(route[packageAt + 2] || "") : params.get("version");
const linkedType = params.get("type");
const linkedFramework = params.get("framework");

if (linkedPackage) {
  state.requestedPackage = linkedPackage;
  state.requestedVersion = linkedVersion || "latest";
}
if (linkedFramework) state.requestedFramework = linkedFramework;
if (location.hash && lenses.some(([id]) => id === location.hash.slice(1))) {
  state.lens = location.hash.slice(1);
}

const app = document.querySelector("#app");
let mermaidModule;
document.documentElement.dataset.theme = state.theme;

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function selectedType() {
  if (!state.package) return null;
  return state.package.types.find(item => item.id === state.selectedTypeId) || filteredTypes()[0] || state.package.types[0];
}

function filteredTypes() {
  if (!state.package) return [];
  const needle = state.typeFilter.toLowerCase();
  return state.package.types.filter(item => {
    const matchesText = !needle || `${item.name} ${item.namespace} ${item.kind}`.toLowerCase().includes(needle);
    return matchesText && (!state.namespaceFilter || item.namespace === state.namespaceFilter);
  });
}

function namespaces() {
  if (!state.package) return [];
  return [...new Set(state.package.types.map(item => item.namespace))];
}

function typeGroups() {
  const groups = new Map();
  for (const item of filteredTypes()) {
    if (!groups.has(item.namespace)) groups.set(item.namespace, []);
    groups.get(item.namespace).push(item);
  }
  return groups;
}

function memberGroups(type) {
  const groups = new Map();
  for (const member of type.api ?? []) {
    const key = `${member.kind}:${member.name}`;
    if (!groups.has(key)) groups.set(key, { key, name: member.name, kind: member.kind, overloads: [] });
    groups.get(key).overloads.push(member);
  }
  return [...groups.values()];
}

function selectedMember(type) {
  return memberGroups(type).find(group => group.key === state.selectedMemberKey);
}

function parameterTitle(parameters) {
  if (!parameters.length) return "()";
  return `(${parameters.map(parameter => parameter.type.split(".").at(-1)).join(", ")})`;
}

function completions() {
  const input = state.command.trimStart();
  const tokens = input.split(/\s+/).filter(Boolean);
  let entries;

  if (!tokens.length) {
    entries = rootCommands.map(([value, hint]) => ({ value, hint, kind: "command" }));
  } else if (tokens[0] === "type") {
    entries = state.package.types.map(item => ({
      value: item.name,
      hint: item.namespace,
      kind: item.kind
    }));
  } else if (tokens[0] === "show") {
    entries = lenses.map(([value, label]) => ({ value, hint: `${label} lens`, kind: "lens" }));
  } else if (tokens[0] === "framework") {
    entries = state.package.frameworks.map(value => ({ value, hint: "compile assets", kind: "framework" }));
  } else if (tokens[0] === "types") {
    entries = [
      { value: "public", hint: "public surface (default)", kind: "filter" },
      { value: "namespace", hint: "filter to a namespace", kind: "filter" },
      { value: "kind", hint: "filter by class, struct, interface, or enum", kind: "filter" }
    ];
  } else {
    entries = rootCommands.map(([value, hint]) => ({ value, hint, kind: "command" }));
  }

  if (input.endsWith(" ")) return entries.slice(0, 8);
  const needle = tokens.at(-1)?.toLowerCase() || "";
  return entries.filter(entry => entry.value.toLowerCase().includes(needle)).slice(0, 8);
}

function render() {
  if (state.loading || state.error || !state.package) {
    renderLoading();
    return;
  }
  const current = selectedType();
  const visible = filteredTypes();
  state.typeCursor = Math.min(state.typeCursor, Math.max(visible.length - 1, 0));
  const suggestions = completions();
  state.completionIndex = Math.min(state.completionIndex, Math.max(suggestions.length - 1, 0));

  app.innerHTML = `
    <div class="workbench">
      <header class="titlebar">
        <a class="brand" href="/" aria-label="dotnet inspect home"><span class="brand-glyph">◇</span><span>dotnet-inspect</span></a>
        <div class="package-tabs" role="tablist" aria-label="Package scope">
          ${state.packages.map(item => `
            <button class="package-tab ${item.id === state.package.id ? "active" : ""}" data-package="${escapeHtml(item.id)}" role="tab">
              <span class="package-cube">⬡</span>
              <span class="tab-label">${escapeHtml(item.id)}</span>
              <small>${escapeHtml(item.version)}</small>
              ${item.id === state.package.id ? '<span class="tab-close">×</span>' : ""}
            </button>`).join("")}
        </div>
        <form class="package-query" id="package-query">
          <span>+</span>
          <input id="package-query-input" placeholder="Package or Package@version" aria-label="Open NuGet package" autocomplete="off" spellcheck="false" />
          <button>open</button>
        </form>
        <div class="title-actions">
          <button id="demo-call-graph">demo</button>
          <button id="theme-toggle" aria-label="Switch to light theme">${state.theme === "dark" ? "light" : "dark"}</button>
          <button id="share">share</button>
          <button id="help" aria-label="Keyboard help">?</button>
        </div>
      </header>

      <section class="scopebar">
        <div class="package-title">
          <span class="scope-kicker">package</span>
          <strong>${escapeHtml(state.package.id)}</strong>
          <span>${escapeHtml(state.package.version)}</span>
        </div>
        <label class="framework-select">
          <span>framework</span>
          <select id="framework">
            ${state.package.frameworks.map(item => `<option ${item === state.package.activeFramework ? "selected" : ""}>${item}</option>`).join("")}
          </select>
        </label>
        <div class="asset-path">compile / lib/${escapeHtml(state.package.activeFramework)} / ${escapeHtml(state.package.assembly)}</div>
        <div class="scope-stats">
          <span><strong>${state.package.totalTypes}</strong> types</span>
          <span><strong>${state.package.totalMembers.toLocaleString()}</strong> members</span>
        </div>
      </section>

      <nav class="lensbar" aria-label="Inspection lenses">
        <button class="home-lens active-static"><span>⌘</span> Types</button>
        <span class="lens-separator"></span>
        ${lenses.map(([id, label], index) => `
          <button class="lens ${state.lens === id ? "active" : ""}" data-lens="${id}">
            ${escapeHtml(label)}<kbd>${index + 1}</kbd>
          </button>`).join("")}
      </nav>

      <main class="workspace">
        <aside class="type-browser" aria-label="Public types">
          <div class="browser-head">
            <div>
              <span class="pane-label">PUBLIC TYPES</span>
              <span class="result-count">${visible.length} shown</span>
            </div>
            <button class="tiny-button" id="clear-filter" title="Clear filter">×</button>
          </div>
          <label class="type-search">
            <span>/</span>
            <input id="type-filter" value="${escapeHtml(state.typeFilter)}" placeholder="Filter types" autocomplete="off" spellcheck="false" />
            <kbd>⌘F</kbd>
          </label>
          <div class="namespace-chips" aria-label="Namespace filters">
            <button class="${!state.namespaceFilter ? "active" : ""}" data-namespace="">all</button>
            ${namespaces().map(item => `<button class="${state.namespaceFilter === item ? "active" : ""}" data-namespace="${escapeHtml(item)}" title="${escapeHtml(item)}">${escapeHtml(item.split(".").at(-1))}</button>`).join("")}
          </div>
          <div class="type-list" role="listbox" tabindex="0" id="type-list">
            ${[...typeGroups()].map(([namespace, types]) => `
              <section class="type-group">
                <button class="namespace-row" data-namespace="${escapeHtml(namespace)}">
                  <span class="chevron">⌄</span>
                  <span>${escapeHtml(namespace)}</span>
                  <small>${types.length}</small>
                </button>
                ${types.map(item => {
                  const selected = item.id === current.id;
                  return `<button class="type-row ${selected ? "selected" : ""}" data-type="${escapeHtml(item.id)}" role="option" aria-selected="${selected}">
                    <span class="kind-icon">${kindIcon(item.kind)}</span>
                    <span class="type-name">${escapeHtml(item.name)}</span>
                    <small>${escapeHtml(shortKind(item.kind))}</small>
                  </button>`;
                }).join("")}
              </section>`).join("") || '<div class="empty-list">No public types match this filter.</div>'}
          </div>
          <footer class="pane-footer"><span>↑↓ navigate</span><span>enter open</span><span>/ filter</span></footer>
        </aside>

        <section class="detail-pane">
          <header class="detail-head">
            <div class="nav-history">
              <button id="nav-back" ${nav.index > 0 ? "" : "disabled"} title="Back (Alt+←)" aria-label="Back">‹</button>
              <button id="nav-forward" ${nav.index < nav.stack.length - 1 ? "" : "disabled"} title="Forward (Alt+→)" aria-label="Forward">›</button>
            </div>
            <div class="breadcrumbs">
              <span>${escapeHtml(state.package.id)}</span><b>/</b><span>${escapeHtml(current.namespace)}</span><b>/</b><strong>${escapeHtml(current.name)}</strong>
              ${state.selectedMemberKey ? `<b>/</b><strong>${escapeHtml(selectedMember(current)?.name ?? "")}</strong>` : ""}
            </div>
            <div class="detail-actions"><button>copy name</button><button>⋯</button></div>
          </header>
          <article class="detail-scroll">
            ${renderLens(current)}
          </article>
          <footer class="statusbar">
            <span class="ready-dot"></span><span>browser wasm ready</span>
            ${state.diag ? `
            <span class="diag" title="Framework assets fetched over the wire — compressed → uncompressed, across ${state.diag.assets} files">↓ download ${fmtMs(state.diag.downloadMs)} · ${fmtBytes(state.diag.transfer)}${state.diag.decoded ? ` → ${fmtBytes(state.diag.decoded)}` : ""}</span>
            <span class="diag" title="Runtime instantiation after assets arrived: WASM compile + module init + runMain">⚙ startup ${fmtMs(state.diag.startupMs)}</span>
            <span class="diag" title="Initial package query precomputed during load">⚡ precompute ${fmtMs(state.diag.precomputeMs)}</span>
            <span class="diag diag-total" title="Total time from navigation start to interactive">Σ ${fmtMs(state.diag.totalMs)}</span>` : ""}
          <span class="status-spacer"></span>
          <span>${escapeHtml(current.assembly)}</span>
          <span>${escapeHtml(state.package.activeFramework)}</span>
          <span>public API surface</span>
          </footer>
        </section>
      </main>

      <section class="command-area">
        <div class="command-panel ${state.promptOpen ? "open" : ""}">
          <div class="suggestions" role="listbox">
            ${suggestions.map((item, index) => `
              <button class="suggestion ${index === state.completionIndex ? "selected" : ""}" data-completion="${escapeHtml(item.value)}">
                <strong>${escapeHtml(item.value)}</strong><span>${escapeHtml(item.hint)}</span><small>${escapeHtml(item.kind)}</small>
              </button>`).join("")}
            <div class="suggestion-help"><span>↑↓ select</span><span>tab complete</span><span>enter run</span><span>esc dismiss</span></div>
          </div>
          <div class="command-line">
            <span class="command-scope">${escapeHtml(state.package.id)}:${escapeHtml(state.package.activeFramework)}</span>
            <span class="prompt">›</span>
            <input id="command" value="${escapeHtml(state.command)}" placeholder="type a command…  try “type JsonSerializer”" autocomplete="off" spellcheck="false" />
            <kbd>⌘K</kbd>
          </div>
        </div>
      </section>
      ${state.spotlightOpen ? renderSpotlight() : ""}
    </div>`;

  bindEvents();
  recordNav();
  maybeAutoLoadTypeSource();
}

function maybeAutoLoadTypeSource() {
  if (state.lens !== "source") return;
  const type = selectedType();
  if (!type) return;
  const signature = typeSourceSignature(type);
  if (state.typeSourceKey === signature) return;
  loadSelectedTypeSource();
}

function renderLens(item) {
  const member = selectedMember(item);
  if (state.lens === "api" && member) return renderMember(item, member);
  if (state.lens === "source") {
    return `
      ${typeHeading(item)}
      ${renderTypeSource(item)}`;
  }
  if (state.lens === "metadata") {
    return `${typeHeading(item)}
      <section class="document-section">
        <div class="section-title"><h2>Type definition</h2><span>ECMA-335 metadata</span></div>
        ${factRows([
          ["Signature", item.signature],
          ["Kind", item.kind],
          ["Accessibility", item.accessibility],
          ["Namespace", item.namespace],
          ["Assembly", item.assembly],
          ["Declared public members", String(item.members)]
        ])}
      </section>`;
  }
  if (state.lens === "findings") {
    return `${typeHeading(item)}
      <section class="document-section empty-document"><span class="large-glyph">△</span><h2>Findings not queried</h2><p>Analysis remains an explicit facet and has not run for this package session.</p></section>`;
  }
  if (state.lens === "dependencies") {
    return `${typeHeading(item)}
      <section class="document-section empty-document"><span class="large-glyph">⌘</span><h2>Dependencies not queried</h2><p>Type relationship analysis is outside the current public API query.</p></section>`;
  }
  if (state.lens === "il") {
    return `${typeHeading(item)}
      <section class="document-section empty-document"><span class="large-glyph">λ</span><h2>Select a method to inspect IL</h2><p>Choose a member from the API surface or run <code>member Deserialize show il</code>.</p></section>`;
  }
  const groups = memberGroups(item);
  return `
    ${typeHeading(item)}
    <section class="document-section">
      <div class="section-title"><h2>Public API</h2><span>${groups.length} member groups · ${item.members} overloads</span></div>
      <div class="member-filter"><button class="active">all</button><button>methods</button><button>properties</button><button>fields</button><span></span><button>declared only</button></div>
      <div class="api-list">${groups.map(group => `
        <button class="api-row" data-member="${escapeHtml(group.key)}">
          <span class="member-icon">${escapeHtml(group.kind?.slice(0, 1)?.toUpperCase() || "M")}</span>
          <code>${highlight(group.overloads[0].signature)}</code>
          <small>${group.overloads.length === 1 ? escapeHtml(group.kind) : `${group.overloads.length} overloads`}</small>
        </button>`).join("") || '<div class="empty-list">No declared public members.</div>'}</div>
    </section>`;
}

function renderMember(type, member) {
  if (member.overloads.length > 1 && state.selectedOverloadIndex == null) {
    return `
      <button class="member-back" id="member-back">← ${escapeHtml(type.name)}</button>
      <section class="overload-picker">
        <p class="eyebrow">${escapeHtml(member.kind)} group</p>
        <h1>${escapeHtml(member.name)}</h1>
        <p>Choose a specific overload to inspect.</p>
        <div class="api-list">
          ${member.overloads.map((overload, index) => `
            <button class="api-row overload-row" data-overload="${index}">
              <span class="member-icon">${index + 1}</span>
              <code>${highlight(overload.signature)}</code>
              <small>open →</small>
            </button>`).join("")}
        </div>
      </section>`;
  }
  const sections = [
    ["overview", "Overview"],
    ["call-graph", "Call graph"],
    ["facts", "Facts"],
    ["source", "Source"]
  ];
  const overloadIndex = state.selectedOverloadIndex ?? 0;
  const overload = member.overloads[overloadIndex];
  let content;
  if (state.memberSection === "overview") {
    const pageKind = member.kind === "constructor" ? "Constructor" : `${member.kind.slice(0, 1).toUpperCase()}${member.kind.slice(1)}`;
    const parameters = overload.parameters ?? [];
    content = `
      <article class="learn-overview">
        <header class="learn-title">
          <p>${escapeHtml(type.namespace)}</p>
          <h1>${escapeHtml(type.name)}.${escapeHtml(member.name)}${parameterTitle(parameters)} ${escapeHtml(pageKind)}</h1>
          <span>${escapeHtml(state.package.id)} · ${escapeHtml(state.package.activeFramework)}</span>
        </header>
        <section class="learn-section definition-section">
          <dl class="definition-list">
            <div><dt>Namespace:</dt><dd>${escapeHtml(type.namespace || "global")}</dd></div>
            <div><dt>Assembly:</dt><dd>${escapeHtml(type.assembly)}</dd></div>
            <div><dt>Package:</dt><dd>${escapeHtml(state.package.id)} v${escapeHtml(state.package.version)}</dd></div>
          </dl>
          ${state.memberDocumentationLoading
            ? '<p class="docs-loading">Loading package documentation…</p>'
            : state.memberDocumentationError
              ? `<p class="docs-unavailable">Documentation query failed: ${escapeHtml(state.memberDocumentationError)}</p>`
            : overload.summary
              ? `<p class="api-summary">${escapeHtml(overload.summary)}</p>`
              : '<p class="docs-unavailable">No summary was found in the package XML documentation.</p>'}
          <div class="signature-panel">
            <div class="signature-language"><span>C#</span><small>declaration</small><button id="copy-signature" type="button">copy</button></div>
            <pre class="language-csharp signature-code"><code class="language-csharp">${highlightCSharp(overload.signature)}</code></pre>
          </div>
          <section class="member-identity" aria-labelledby="member-identity-title">
            <div class="identity-heading"><h2 id="member-identity-title">Identity</h2><span>stable across builds</span></div>
            <dl>
              <div><dt>Stable selector</dt><dd><code>${escapeHtml(overload.stableSelector)}</code><button type="button" data-copy-anchor="selector">copy</button></dd></div>
              <div><dt>Digest</dt><dd><code>${escapeHtml(overload.anchorDigest)}</code><button type="button" data-copy-anchor="digest">copy</button></dd></div>
              <div class="canonical-identity"><dt>Canonical signature</dt><dd><code>${escapeHtml(overload.canonicalSignature)}</code><button type="button" data-copy-anchor="canonical">copy</button></dd></div>
            </dl>
            <p>Derived from the canonical signature; suitable for selecting this overload across builds.</p>
          </section>
        </section>
        ${parameters.length ? `<section class="learn-section">
          <h2>Parameters</h2>
          <dl class="parameter-docs">${parameters.map(parameter => `
            <div>
              <dt><code>${escapeHtml(parameter.name)}</code></dt>
              <dd><a>${escapeHtml([parameter.modifier, parameter.type].filter(Boolean).join(" "))}</a>${parameter.hasDefault ? `<span>Default: <code>${escapeHtml(parameter.defaultValue ?? "default")}</code></span>` : ""}<p>${escapeHtml(state.memberDocumentationLoading ? "Loading documentation…" : parameter.description || "No parameter documentation was found in the package XML documentation.")}</p></dd>
            </div>`).join("")}</dl>
        </section>` : ""}
        ${overload.returns ? `<section class="learn-section"><h2>Returns</h2><p class="api-summary">${escapeHtml(overload.returns)}</p></section>` : ""}
        <section class="learn-section">
          <h2>Exceptions</h2>
          ${state.memberDocumentationLoading
            ? '<p class="docs-loading">Loading documented exceptions…</p>'
            : (overload.exceptions ?? []).length
            ? `<dl class="exception-docs">${overload.exceptions.map(exception => `<div><dt>${escapeHtml(exception.type)}</dt><dd>${escapeHtml(exception.description)}</dd></div>`).join("")}</dl>`
            : '<p class="docs-unavailable">No exceptions are documented for this overload.</p>'}
        </section>
        <section class="learn-section applies-to">
          <h2>Applies to</h2>
          <span>${escapeHtml(state.package.activeFramework)}</span>
        </section>
      </article>
    `;
  } else if (state.memberSection === "call-graph") {
    const callers = state.memberCallGraph?.callers?.children ?? [];
    const callees = state.memberCallGraph?.callees?.children ?? [];
    const scope = state.memberCallGraph?.scope;
    content = state.memberCallGraphLoading
      ? `<section class="document-section source-progress"><span class="loader"></span><h2>Building workspace call graph…</h2><p>Scanning implementation IL across ${state.packages.length} loaded package${state.packages.length === 1 ? "" : "s"}.</p></section>`
      : state.memberCallGraph
        ? `<section class="document-section call-graph-section">
            <div class="section-title"><h2>Call graph</h2><span>${callers.length} callers · ${callees.length} callees</span></div>
            <div class="graph-scope"><strong>Workspace callers</strong><span>${scope.packages} loaded packages · ${scope.callerAssemblies} scanned assemblies</span><strong>Callees</strong><span>${escapeHtml(scope.calleeScope)} · depth 2</span></div>
            <div id="call-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph…</p></div>
            <div class="graph-legend" aria-label="Graph legend">
              <span><i class="legend-swatch target"></i>target member</span>
              <span><i class="legend-swatch same-type"></i>same declaring type</span>
              <span><i class="legend-swatch different-type"></i>different type, same assembly</span>
              <span><i class="legend-swatch different-assembly"></i>different assembly</span>
            </div>
            <details class="graph-source"><summary>Mermaid source</summary><pre><code>${escapeHtml(state.memberCallGraph.mermaid)}</code></pre></details>
          </section>`
        : `<section class="document-section empty-member-section"><h2>Call graph query failed</h2><p>${escapeHtml(state.memberCallGraphError || "No call graph result was returned.")}</p></section>`;
  } else if (state.memberSection === "facts") {
    content = renderMemberFacts(type, member, overload, overloadIndex);
  } else {
    content = state.memberSourceLoading
      ? `<section class="document-section source-progress"><span class="loader"></span><h2>Resolving source…</h2><p>Trying checksum-verified SourceLink source, then dotnet-inspect decompilation.</p></section>`
      : state.memberSource
        ? `<section class="document-section source-result">
            <div class="source-provenance"><strong>${state.memberSource.provider === "original" ? "Original source" : "Decompiled source"}</strong><span>${escapeHtml(state.memberSource.provenance)}</span>${state.memberSource.url ? `<a href="${escapeHtml(state.memberSource.url)}" target="_blank" rel="noreferrer">open source ↗</a>` : ""}<button id="copy-source" type="button">copy</button></div>
            <pre class="language-csharp"><code class="language-csharp">${highlightCSharp(state.memberSource.text)}</code></pre>
          </section>`
        : `<section class="document-section empty-member-section"><h2>Source query failed</h2><p>${escapeHtml(state.memberSourceError || "No source result was returned.")}</p></section>`;
  }
  return `
    <button class="member-back" id="member-back">← ${member.overloads.length > 1 ? `${escapeHtml(member.name)} overloads` : escapeHtml(type.name)}</button>
    <nav class="member-sections" aria-label="Member details">
      ${sections.map(([id, label]) => `<button class="${state.memberSection === id ? "active" : ""}" data-member-section="${id}">${label}</button>`).join("")}
    </nav>
    ${content}`;
}

function renderMemberFacts(type, member, overload, overloadIndex) {
  if (state.memberFactsLoading) {
    return `<section class="document-section source-progress"><span class="loader"></span><h2>Analyzing method…</h2><p>Decoding the selected overload and deriving method evidence and performance opportunities.</p></section>`;
  }
  if (!state.memberFacts) {
    return `<section class="document-section empty-member-section"><h2>Facts query failed</h2><p>${escapeHtml(state.memberFactsError || "No facts result was returned.")}</p></section>`;
  }

  const facts = state.memberFacts;
  const signals = facts.signals;
  return `
    <section class="document-section facts-section">
      <div class="section-title"><h2>Method facts</h2><span>selected overload</span></div>
      ${factRows([
        ["Overload", `${overloadIndex + 1} of ${member.overloads.length}`],
        ["Kind", overload.kind],
        ["Metadata token", overload.metadataToken == null ? "not exposed" : `0x${overload.metadataToken.toString(16).padStart(8, "0")}`],
        ["Declaring type", type.id],
        ["Allocations", String(signals.allocations)],
        ["Calls", String(facts.calls.length)],
        ["Copies", String(signals.copies)],
        ["Reflection calls", String(signals.reflection)],
        ["Throws / catches / finally", `${signals.throws} / ${signals.catches} / ${signals.finallys}`],
        ["Unsafe", signals.unsafe ? "yes" : "no"],
        ["Allocates in loop", signals.allocatesInLoop ? "yes" : "no"],
        ["Evidence", signals.evidenceOffsets.length ? signals.evidenceOffsets.join(", ") : "none"]
      ])}
    </section>
    ${renderFactTable("Allocation facts", facts.allocations, [
      ["IL", "offset"], ["Kind", "kind"], ["Type", "type"], ["Multiplicity", "multiplicity"],
      ["Path", "path"], ["Escape", "escape"], ["Loop", row => row.inLoop ? "yes" : ""],
      ["Size", row => row.estimatedSizeBytes == null ? "" : `${row.estimatedSizeBytes} B`]
    ], "No heap-allocation occurrences were found in this method.")}
    ${renderFactTable("Calls", facts.calls, [
      ["IL", "offset"], ["Opcode", "opcode"], ["Callee", "callee"],
      ["Multiplicity", "multiplicity"], ["Loop", row => row.inLoop ? "yes" : ""],
      ["Target", row => row.exactTarget ? "exact" : "open"]
    ], "No direct call sites were found in this method.")}
    ${renderFactTable("Safety facts", facts.safety, [
      ["IL", row => row.offset || ""], ["Kind", "kind"], ["Evidence", "detail"]
    ], "No unsafe operations or declaration evidence were found.")}
    ${renderFactTable("Exception regions", facts.exceptionRegions, [
      ["Region", "region"], ["Clause", "clause"], ["Try", "tryRange"],
      ["Handler", "handlerRange"], ["Filter", row => row.filterRange || ""],
      ["Caught type", row => row.caughtType || ""]
    ], "No exception regions were found in this method.")}
    <section class="document-section performance-facts">
      <div class="section-title"><h2>Performance opportunities</h2><span>ranked judgments · ${facts.performanceOpportunities.length}</span></div>
      ${facts.performanceOpportunities.length
        ? facts.performanceOpportunities.map(opportunity => `
          <article class="performance-opportunity">
            <div><strong>${escapeHtml(opportunity.shape)}</strong><span class="confidence ${escapeHtml(opportunity.confidence)}">${escapeHtml(opportunity.confidence)}</span>${opportunity.offset ? `<code>${escapeHtml(opportunity.offset)}</code>` : ""}</div>
            <p>${escapeHtml(opportunity.evidence)}</p>
            <dl><dt>Possible direction</dt><dd>${escapeHtml(opportunity.fix)}</dd>${opportunity.caveat ? `<dt>Caveat</dt><dd>${escapeHtml(opportunity.caveat)}</dd>` : ""}<dt>Provenance</dt><dd>${escapeHtml([opportunity.provenance, opportunity.finding].filter(Boolean).join(" · "))}</dd></dl>
          </article>`).join("")
        : '<div class="empty-fact-group">No curated performance opportunities were found for this method.</div>'}
    </section>`;
}

function renderFactTable(title, rows, columns, emptyText) {
  return `<section class="document-section fact-group">
    <div class="section-title"><h2>${escapeHtml(title)}</h2><span>${rows.length}</span></div>
    ${rows.length
      ? `<div class="fact-table" style="--fact-columns:${columns.length}">${columns.map(([label]) => `<strong>${escapeHtml(label)}</strong>`).join("")}${rows.map(row => columns.map(([, field]) => {
          const value = typeof field === "function" ? field(row) : row[field];
          return `<code>${escapeHtml(value ?? "")}</code>`;
        }).join("")).join("")}</div>`
      : `<div class="empty-fact-group">${escapeHtml(emptyText)}</div>`}
  </section>`;
}

function typeHeading(item) {
  return `<header class="type-heading">
    <div class="type-badge">${kindIcon(item.kind)}</div>
    <div>
      <div class="type-namespace">${escapeHtml(item.namespace)}</div>
      <h1>${escapeHtml(item.name)}</h1>
      <code class="type-signature">${highlight(item.signature)}</code>
    </div>
    <div class="type-metrics"><span><strong>${item.members}</strong> members</span><span><strong>public</strong> accessibility</span></div>
    <dl class="definition-list">
      <div><dt>TFM:</dt><dd>${escapeHtml(state.package.activeFramework)}</dd></div>
      <div><dt>Library:</dt><dd>${escapeHtml(item.assembly)}</dd></div>
      <div><dt>Package:</dt><dd>${escapeHtml(state.package.id)}@${escapeHtml(state.package.version)}</dd></div>
    </dl>
  </header>`;
}

function factRows(rows) {
  return `<dl class="fact-rows">${rows.map(([key, value]) => `<div><dt>${escapeHtml(key)}</dt><dd><code>${escapeHtml(value)}</code></dd></div>`).join("")}</dl>`;
}

function typeSourceSignature(item) {
  return `${state.package.id}@${state.package.version}/${state.package.activeFramework}/${item.assembly}/${item.id}`;
}

function renderTypeSource(item) {
  const current = typeSourceSignature(item);
  const fresh = state.typeSourceKey === current;
  if (state.typeSourceLoading && fresh) {
    return `<section class="document-section source-progress"><span class="loader"></span><h2>Decompiling type…</h2><p>Reconstructing the whole type as C# with dotnet-inspect.</p></section>`;
  }
  if (fresh && state.typeSource) {
    return `<section class="document-section source-result">
        <div class="source-provenance"><strong>Decompiled source</strong><span>${escapeHtml(state.typeSource.provenance)}</span><button id="copy-type-source" type="button">copy</button></div>
        <pre class="language-csharp"><code class="language-csharp">${highlightCSharp(state.typeSource.text)}</code></pre>
      </section>`;
  }
  if (fresh && state.typeSourceError) {
    return `<section class="document-section empty-document"><span class="large-glyph">⌁</span><h2>Type source failed</h2><p>${escapeHtml(state.typeSourceError)}</p></section>`;
  }
  return `<section class="document-section source-progress"><span class="loader"></span><h2>Decompiling type…</h2><p>Reconstructing the whole type as C# with dotnet-inspect.</p></section>`;
}

function kindIcon(kind) {
  if (kind.includes("struct")) return "S";
  if (kind === "enum") return "E";
  if (kind.includes("interface")) return "I";
  return "C";
}

function shortKind(kind) {
  return kind.replace("sealed ", "").replace("abstract ", "").replace("static ", "").replace("readonly ", "");
}

function highlight(value) {
  return escapeHtml(value)
    .replace(/\b(public|static|class|abstract|sealed|readonly|struct|return|if|is|new|default)\b/g, '<span class="kw">$1</span>')
    .replace(/\b(string|object|void|Type|Stream|Task|ValueTask|CancellationToken|TValue)\b/g, '<span class="primitive">$1</span>');
}

function highlightCSharp(value) {
  if (globalThis.Prism?.languages?.csharp) {
    return globalThis.Prism.highlight(String(value), globalThis.Prism.languages.csharp, "csharp");
  }
  return escapeHtml(value);
}

function bindEvents() {
  document.querySelectorAll("[data-package]").forEach(button => button.addEventListener("click", () => {
    state.package = state.packages.find(item => item.id === button.dataset.package);
    state.selectedTypeId = state.package.types[0].id;
    state.selectedMemberKey = "";
    state.typeFilter = "";
    state.namespaceFilter = "";
    render();
  }));
  document.querySelectorAll("[data-lens]").forEach(button => button.addEventListener("click", () => {
    state.lens = button.dataset.lens;
    state.selectedMemberKey = "";
    render();
  }));
  document.querySelectorAll("[data-type]").forEach(button => button.addEventListener("click", () => {
    state.selectedTypeId = button.dataset.type;
    state.selectedMemberKey = "";
    state.typeCursor = filteredTypes().findIndex(item => item.id === state.selectedTypeId);
    render();
  }));
  document.querySelectorAll("[data-member]").forEach(button => button.addEventListener("click", () => {
    state.selectedMemberKey = button.dataset.member;
    state.selectedOverloadIndex = null;
    state.memberSection = "overview";
    state.memberSource = null;
    state.memberSourceError = "";
    state.memberCallGraph = null;
    state.memberCallGraphError = "";
    state.memberFacts = null;
    state.memberFactsError = "";
    loadSelectedMemberDocumentation();
  }));
  document.querySelectorAll("[data-overload]").forEach(button => button.addEventListener("click", () => {
    state.selectedOverloadIndex = Number(button.dataset.overload);
    state.memberSection = "overview";
    state.memberSource = null;
    state.memberSourceError = "";
    state.memberCallGraph = null;
    state.memberCallGraphError = "";
    state.memberFacts = null;
    state.memberFactsError = "";
    loadSelectedMemberDocumentation();
  }));
  document.querySelectorAll("[data-member-section]").forEach(button => button.addEventListener("click", () => {
    state.memberSection = button.dataset.memberSection;
    if (state.memberSection === "source") loadSelectedMemberSource();
    else if (state.memberSection === "call-graph") loadSelectedMemberCallGraph();
    else if (state.memberSection === "facts") loadSelectedMemberFacts();
    else if (state.memberSection === "overview") loadSelectedMemberDocumentation();
    else render();
  }));
  document.querySelector("#member-back")?.addEventListener("click", () => {
    const member = selectedMember(selectedType());
    if (member?.overloads.length > 1 && state.selectedOverloadIndex != null) {
      state.selectedOverloadIndex = null;
    } else {
      state.selectedMemberKey = "";
    }
    render();
  });
  document.querySelector("#copy-signature")?.addEventListener("click", async () => {
    const type = selectedType();
    const member = selectedMember(type);
    const overload = member?.overloads[state.selectedOverloadIndex ?? 0];
    if (overload) await copyText(overload.signature, "signature copied");
  });
  document.querySelectorAll("[data-copy-anchor]").forEach(button => button.addEventListener("click", async () => {
    const type = selectedType();
    const member = selectedMember(type);
    const overload = member?.overloads[state.selectedOverloadIndex ?? 0];
    const values = {
      selector: overload?.stableSelector,
      digest: overload?.anchorDigest,
      canonical: overload?.canonicalSignature
    };
    const value = values[button.dataset.copyAnchor];
    if (value) await copyText(value, `${button.dataset.copyAnchor} copied`);
  }));
  document.querySelector("#copy-source")?.addEventListener("click", async () => {
    if (state.memberSource) await copyText(state.memberSource.text, "source copied");
  });
  document.querySelector("#copy-type-source")?.addEventListener("click", async () => {
    if (state.typeSource) await copyText(state.typeSource.text, "source copied");
  });
  document.querySelectorAll("[data-namespace]").forEach(button => button.addEventListener("click", () => {
    state.namespaceFilter = button.dataset.namespace;
    state.typeCursor = 0;
    const first = filteredTypes()[0];
    if (first) state.selectedTypeId = first.id;
    state.selectedMemberKey = "";
    render();
  }));
  document.querySelectorAll("[data-completion]").forEach(button => button.addEventListener("mousedown", event => {
    event.preventDefault();
    applyCompletion(button.dataset.completion);
  }));

  document.querySelector("#framework").addEventListener("change", event => {
    loadPackage(state.package.id, state.package.version, event.target.value);
  });
  const filter = document.querySelector("#type-filter");
  filter.addEventListener("input", event => {
    state.typeFilter = event.target.value;
    state.typeCursor = 0;
    const first = filteredTypes()[0];
    if (first) state.selectedTypeId = first.id;
    state.selectedMemberKey = "";
    render();
    focusFilter();
  });
  filter.addEventListener("keydown", event => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      document.querySelector("#type-list").focus();
    } else if (event.key === "Escape") {
      state.typeFilter = "";
      render();
    }
  });
  document.querySelector("#type-list").addEventListener("keydown", handleTypeKeys);
  const spotlightInput = document.querySelector("#spotlight-input");
  if (spotlightInput) {
    spotlightInput.addEventListener("input", event => {
      state.spotlightQuery = event.target.value;
      state.spotlightIndex = 0;
      updateSpotlightResults();
    });
    spotlightInput.addEventListener("keydown", handleSpotlightKeys);
  }
  document.querySelectorAll("[data-sl-type]").forEach(button => button.addEventListener("click", () => {
    pickSpotlight(button.dataset.slPkg, button.dataset.slType);
  }));
  document.querySelector("#spotlight-backdrop")?.addEventListener("mousedown", event => {
    if (event.target.id === "spotlight-backdrop") closeSpotlight();
  });
  document.querySelector("#clear-filter").addEventListener("click", () => {
    state.typeFilter = "";
    state.namespaceFilter = "";
    render();
    focusFilter();
  });

  const command = document.querySelector("#command");
  command.addEventListener("focus", () => {
    state.promptOpen = true;
    document.querySelector(".command-panel").classList.add("open");
  });
  command.addEventListener("input", event => {
    state.command = event.target.value;
    state.promptOpen = true;
    state.completionIndex = 0;
    render();
    focusCommand();
  });
  command.addEventListener("keydown", handleCommandKeys);
  document.querySelector("#package-query").addEventListener("submit", event => {
    event.preventDefault();
    const value = document.querySelector("#package-query-input").value.trim();
    const separator = value.lastIndexOf("@");
    if (!value || separator === value.length - 1) {
      showToast("enter a package, optionally followed by @version");
      return;
    }
    const packageId = separator > 0 ? value.slice(0, separator) : value;
    const version = separator > 0 ? value.slice(separator + 1) : "latest";
    loadPackage(packageId, version, "");
  });
  document.querySelector("#share").addEventListener("click", share);
  document.querySelector("#nav-back")?.addEventListener("click", navBack);
  document.querySelector("#nav-forward")?.addEventListener("click", navForward);
  document.querySelector("#demo-call-graph").addEventListener("click", runCallGraphDemo);
  document.querySelector("#theme-toggle").addEventListener("click", toggleTheme);
  document.querySelector("#help").addEventListener("click", () => showToast("⌘K command · ⌘P / type to find a type · ⌘F filter · 1—6 lenses · ↑↓ types · Alt+←/→ back/forward · graph: wheel zoom, click node to open, +/− zoom, 0 fit, arrows pan"));
}

function toggleTheme() {
  state.theme = state.theme === "dark" ? "light" : "dark";
  localStorage.setItem("inspect-theme", state.theme);
  document.documentElement.dataset.theme = state.theme;
  render();
  if (state.memberCallGraph) renderMermaidCallGraph();
}

function handleTypeKeys(event) {
  const items = filteredTypes();
  if (!items.length) return;
  let cursor = items.findIndex(item => item.id === state.selectedTypeId);
  if (cursor < 0) cursor = Math.min(state.typeCursor, items.length - 1);
  if (event.key === "ArrowDown" || event.key === "j") {
    event.preventDefault();
    cursor = Math.min(items.length - 1, cursor + 1);
  } else if (event.key === "ArrowUp" || event.key === "k") {
    event.preventDefault();
    cursor = Math.max(0, cursor - 1);
  } else if (event.key === "Home") {
    cursor = 0;
  } else if (event.key === "End") {
    cursor = items.length - 1;
  } else if (event.key === "/") {
    event.preventDefault();
    focusFilter();
    return;
  } else {
    return;
  }
  selectTypeByCursor(cursor, items, true);
}

function selectTypeByCursor(cursor, items, focusList) {
  state.typeCursor = cursor;
  state.selectedTypeId = items[cursor].id;
  state.selectedMemberKey = "";
  render();
  requestAnimationFrame(() => {
    if (focusList) document.querySelector("#type-list")?.focus();
    document.querySelector(`[data-type="${CSS.escape(state.selectedTypeId)}"]`)?.scrollIntoView({ block: "nearest" });
  });
}

function stepTypeSelection(delta) {
  const items = filteredTypes();
  if (!items.length) return;
  let cursor = items.findIndex(item => item.id === state.selectedTypeId);
  if (cursor < 0) cursor = Math.min(state.typeCursor, items.length - 1);
  cursor = Math.max(0, Math.min(items.length - 1, cursor + delta));
  selectTypeByCursor(cursor, items, false);
}

function spotlightPool() {
  const pool = [];
  const seen = new Set();
  const pkgs = [state.package, ...state.packages.filter(item => item !== state.package)];
  for (const pkg of pkgs) {
    if (!pkg?.types) continue;
    for (const type of pkg.types) {
      const key = `${pkg.id}\u0000${type.id}`;
      if (seen.has(key)) continue;
      seen.add(key);
      pool.push({ pkg, type });
    }
  }
  return pool;
}

function spotlightCandidates() {
  const signature = `${state.package?.id ?? ""}#${state.packages
    .map(pkg => `${pkg.id}:${pkg.types?.length ?? 0}`)
    .join("|")}`;
  if (spotlightCache && spotlightCache.signature === signature) return spotlightCache;

  const pool = spotlightPool();
  const keyMap = new Map();
  const candidates = pool.map(item => {
    const key = `${item.pkg.id}\u0000${item.type.id}`;
    keyMap.set(key, item);
    const full = `${item.type.namespace ? `${item.type.namespace}.` : ""}${item.type.name}`;
    return { key, name: item.type.name, full };
  });
  spotlightCache = {
    signature,
    pool,
    keyMap,
    candidatesJson: JSON.stringify(candidates),
  };
  return spotlightCache;
}

// Highlight is presentation only; ranking is owned by the engine's SearchTypes.
// Recompute visible spans against the simple type name (exact → prefix → substring → subsequence).
function computeHighlightRanges(name, lowerQuery) {
  if (!lowerQuery) return [];
  const lower = name.toLowerCase();
  if (lower === lowerQuery) return [[0, name.length]];
  if (lower.startsWith(lowerQuery)) return [[0, lowerQuery.length]];
  const index = lower.indexOf(lowerQuery);
  if (index >= 0) return [[index, index + lowerQuery.length]];
  const sub = subsequenceRanges(lower, lowerQuery);
  return sub ? sub.ranges : [];
}

function subsequenceRanges(text, query) {
  let ti = 0;
  let qi = 0;
  let contig = 0;
  let last = -2;
  const ranges = [];
  while (ti < text.length && qi < query.length) {
    if (text[ti] === query[qi]) {
      if (ti === last + 1) contig++;
      const tail = ranges[ranges.length - 1];
      if (tail && tail[1] === ti) tail[1] = ti + 1;
      else ranges.push([ti, ti + 1]);
      last = ti;
      qi++;
    }
    ti++;
  }
  return qi === query.length ? { ranges, contig } : null;
}

function highlightRanges(name, ranges) {
  if (!ranges || !ranges.length) return escapeHtml(name);
  let out = "";
  let pos = 0;
  for (const [start, end] of ranges) {
    out += escapeHtml(name.slice(pos, start));
    out += `<mark>${escapeHtml(name.slice(start, end))}</mark>`;
    pos = end;
  }
  return out + escapeHtml(name.slice(pos));
}

function spotlightFallbackMatches(query, pool) {
  const lowerQuery = query.toLowerCase();
  const scored = [];
  for (const item of pool) {
    const lower = item.type.name.toLowerCase();
    let rank;
    if (lower === lowerQuery) rank = 0;
    else if (lower.startsWith(lowerQuery)) rank = 1;
    else if (lower.includes(lowerQuery)) rank = 2;
    else continue;
    scored.push({ item, rank });
  }
  scored.sort((a, b) =>
    a.rank - b.rank
    || a.item.type.name.length - b.item.type.name.length
    || a.item.type.name.localeCompare(b.item.type.name));
  return scored
    .slice(0, 30)
    .map(entry => ({ ...entry.item, ranges: computeHighlightRanges(entry.item.type.name, lowerQuery) }));
}

function spotlightMatches() {
  const query = state.spotlightQuery.trim();
  const cache = spotlightCandidates();
  if (!query) {
    return cache.pool
      .filter(item => item.pkg === state.package)
      .sort((a, b) => a.type.name.localeCompare(b.type.name))
      .slice(0, 20)
      .map(item => ({ ...item, ranges: [] }));
  }

  const hits = inspectSearchTypes(query, cache.candidatesJson);
  if (!hits) return spotlightFallbackMatches(query, cache.pool);

  const lowerQuery = query.toLowerCase();
  const matches = [];
  for (const hit of hits) {
    const item = cache.keyMap.get(hit.key);
    if (!item) continue;
    matches.push({ ...item, ranges: computeHighlightRanges(item.type.name, lowerQuery) });
  }
  return matches;
}

function spotlightResultsHtml(matches, multiPkg) {
  if (!matches.length) {
    return `<div class="spotlight-empty">No types match “${escapeHtml(state.spotlightQuery.trim())}”.</div>`;
  }
  return matches.map((match, index) => `
    <button class="spotlight-item ${index === state.spotlightIndex ? "selected" : ""}" role="option" aria-selected="${index === state.spotlightIndex}" data-sl-pkg="${escapeHtml(match.pkg.id)}" data-sl-type="${escapeHtml(match.type.id)}">
      <span class="kind-icon">${kindIcon(match.type.kind)}</span>
      <span class="spotlight-item-name">${highlightRanges(match.type.name, match.ranges)}</span>
      <span class="spotlight-item-ns">${escapeHtml(match.type.namespace || "")}</span>
      ${multiPkg ? `<small class="spotlight-item-pkg">${escapeHtml(match.pkg.id)}</small>` : ""}
    </button>`).join("");
}

function renderSpotlight() {
  const matches = spotlightMatches();
  state.spotlightIndex = Math.min(state.spotlightIndex, Math.max(matches.length - 1, 0));
  const multiPkg = state.packages.length > 1;
  return `
    <div class="spotlight-backdrop" id="spotlight-backdrop">
      <div class="spotlight" role="dialog" aria-modal="true" aria-label="Go to type">
        <div class="spotlight-search">
          <span class="spotlight-glyph">⌕</span>
          <input id="spotlight-input" value="${escapeHtml(state.spotlightQuery)}" placeholder="Go to type…  start typing a name" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="spotlight-results" />
          <kbd>esc</kbd>
        </div>
        <div class="spotlight-results" id="spotlight-results" role="listbox">${spotlightResultsHtml(matches, multiPkg)}</div>
        <div class="spotlight-foot"><span>↑↓ select</span><span>↵ open</span><span>esc close</span>${multiPkg ? "<span>all loaded packages</span>" : ""}</div>
      </div>
    </div>`;
}

function updateSpotlightResults() {
  const container = document.querySelector("#spotlight-results");
  if (!container) return;
  const matches = spotlightMatches();
  state.spotlightIndex = Math.min(state.spotlightIndex, Math.max(matches.length - 1, 0));
  container.innerHTML = spotlightResultsHtml(matches, state.packages.length > 1);
  container.querySelectorAll("[data-sl-type]").forEach(button => button.addEventListener("click", () => {
    pickSpotlight(button.dataset.slPkg, button.dataset.slType);
  }));
  container.querySelector(".spotlight-item.selected")?.scrollIntoView({ block: "nearest" });
}

function openSpotlight(seed = "") {
  state.spotlightOpen = true;
  state.spotlightQuery = seed;
  state.spotlightIndex = 0;
  render();
  focusSpotlight();
}

function closeSpotlight() {
  state.spotlightOpen = false;
  state.spotlightQuery = "";
  state.spotlightIndex = 0;
  render();
}

function focusSpotlight() {
  requestAnimationFrame(() => {
    const input = document.querySelector("#spotlight-input");
    if (!input) return;
    input.focus();
    input.setSelectionRange(input.value.length, input.value.length);
  });
}

function pickSpotlight(pkgId, typeId) {
  const pkg = state.packages.find(item => item.id === pkgId) || state.package;
  const type = pkg?.types?.find(item => item.id === typeId);
  if (!type) {
    closeSpotlight();
    return;
  }
  state.package = pkg;
  state.selectedTypeId = type.id;
  state.selectedMemberKey = "";
  state.selectedOverloadIndex = null;
  state.memberSection = "overview";
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.spotlightOpen = false;
  state.spotlightQuery = "";
  state.spotlightIndex = 0;
  state.typeCursor = filteredTypes().findIndex(item => item.id === state.selectedTypeId);
  render();
  requestAnimationFrame(() => {
    document.querySelector(`[data-type="${CSS.escape(state.selectedTypeId)}"]`)?.scrollIntoView({ block: "nearest" });
  });
}

function handleSpotlightKeys(event) {
  const matches = spotlightMatches();
  if (event.key === "ArrowDown") {
    event.preventDefault();
    state.spotlightIndex = matches.length ? (state.spotlightIndex + 1) % matches.length : 0;
    updateSpotlightResults();
  } else if (event.key === "ArrowUp") {
    event.preventDefault();
    state.spotlightIndex = matches.length ? (state.spotlightIndex - 1 + matches.length) % matches.length : 0;
    updateSpotlightResults();
  } else if (event.key === "Enter") {
    event.preventDefault();
    const match = matches[state.spotlightIndex];
    if (match) pickSpotlight(match.pkg.id, match.type.id);
  } else if (event.key === "Escape") {
    event.preventDefault();
    closeSpotlight();
  }
}

function handleCommandKeys(event) {
  const items = completions();
  if (event.key === "ArrowDown") {
    event.preventDefault();
    state.completionIndex = (state.completionIndex + 1) % Math.max(1, items.length);
    render();
    focusCommand();
  } else if (event.key === "ArrowUp") {
    event.preventDefault();
    state.completionIndex = (state.completionIndex - 1 + Math.max(1, items.length)) % Math.max(1, items.length);
    render();
    focusCommand();
  } else if (event.key === "Tab" && items.length) {
    event.preventDefault();
    applyCompletion(items[state.completionIndex].value);
  } else if (event.key === "Enter") {
    event.preventDefault();
    executeCommand();
  } else if (event.key === "Escape") {
    state.promptOpen = false;
    state.command = "";
    render();
    document.querySelector("#type-list").focus();
  }
}

function applyCompletion(value) {
  const tokens = state.command.trim().split(/\s+/).filter(Boolean);
  if (!tokens.length) state.command = `${value} `;
  else if (state.command.endsWith(" ")) state.command += `${value} `;
  else {
    tokens[tokens.length - 1] = value;
    state.command = `${tokens.join(" ")} `;
  }
  state.completionIndex = 0;
  state.promptOpen = true;
  render();
  focusCommand();
}

function executeCommand() {
  const value = state.command.trim();
  if (!value) return;
  const [verb, ...rest] = value.split(/\s+/);
  const argument = rest.join(" ");
  if (verb === "type") {
    const match = state.package.types.find(item => item.name.toLowerCase() === argument.toLowerCase())
      || state.package.types.find(item => item.name.toLowerCase().includes(argument.toLowerCase()));
    if (match) {
      state.selectedTypeId = match.id;
      state.selectedMemberKey = "";
    }
  } else if (verb === "show") {
    const match = lenses.find(([id, label]) => id === argument.toLowerCase() || label.toLowerCase() === argument.toLowerCase());
    if (match) state.lens = match[0];
  } else if (verb === "framework" && state.package.frameworks.includes(argument)) {
    loadPackage(state.package.id, state.package.version, argument);
  } else if (verb === "package") {
    const [id, version = "latest"] = argument.split("@");
    if (id) loadPackage(id, version, "");
  } else if (verb === "clear") {
    state.typeFilter = "";
    state.namespaceFilter = "";
  } else if (verb === "find" || verb === "types") {
    state.typeFilter = argument.replace(/^public\s*/, "");
  } else if (verb === "share") {
    share();
  }
  state.history = [value, ...state.history.filter(item => item !== value)].slice(0, 5);
  state.command = "";
  state.promptOpen = false;
  render();
}

function openCommand(value = "") {
  state.command = value;
  state.promptOpen = true;
  render();
  focusCommand();
}

function focusCommand() {
  requestAnimationFrame(() => {
    const input = document.querySelector("#command");
    input.focus();
    input.setSelectionRange(input.value.length, input.value.length);
  });
}

function focusFilter() {
  requestAnimationFrame(() => {
    const input = document.querySelector("#type-filter");
    input.focus();
    input.setSelectionRange(input.value.length, input.value.length);
  });
}

async function share() {
  const url = new URL(location.href);
  url.pathname = `/packages/${encodeURIComponent(state.package.id)}/${encodeURIComponent(state.package.version)}`;
  url.search = new URLSearchParams({
    framework: state.package.activeFramework,
    type: selectedType().id
  });
  url.hash = state.lens;
  await navigator.clipboard?.writeText(url.toString());
  showToast("selection link copied");
}

function showToast(message) {
  document.querySelector(".toast")?.remove();
  const toast = document.createElement("div");
  toast.className = "toast";
  toast.textContent = message;
  document.body.append(toast);
  setTimeout(() => toast.remove(), 2200);
}

async function copyText(value, confirmation) {
  try {
    await navigator.clipboard.writeText(value);
    showToast(confirmation);
  } catch {
    showToast("clipboard access was denied");
  }
}

function renderLoading() {
  app.innerHTML = `
    <div class="loading-screen">
      <div class="loading-brand"><span>◇</span> dotnet-inspect</div>
      ${state.error
        ? `<div class="load-error"><strong>Inspection query failed</strong><pre>${escapeHtml(state.error)}</pre><button id="retry-load">retry</button></div>`
        : `<div class="load-progress"><span class="loader"></span><strong>${escapeHtml(state.loadingMessage)}</strong><small>${escapeHtml(state.requestedPackage)}@${escapeHtml(state.requestedVersion)} · ${escapeHtml(state.requestedFramework || "best framework")}</small></div>`}
    </div>`;
  document.querySelector("#retry-load")?.addEventListener("click", bootstrap);
}

async function loadSelectedMemberDocumentation() {
  const type = selectedType();
  const member = selectedMember(type);
  if (!member || (member.overloads.length > 1 && state.selectedOverloadIndex == null)) {
    render();
    return;
  }
  const overload = member.overloads[state.selectedOverloadIndex ?? 0];
  if (!overload?.documentationId || overload.documentationLoaded) {
    render();
    return;
  }

  state.memberDocumentationLoading = true;
  state.memberDocumentationError = "";
  render();
  try {
    const documentation = await inspectMemberDocumentation({
      packageId: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework,
      assembly: type.assembly,
      documentationId: overload.documentationId
    });
    overload.summary = documentation.summary;
    overload.returns = documentation.returns;
    overload.exceptions = documentation.exceptions ?? [];
    overload.parameters = (overload.parameters ?? []).map(parameter => ({
      ...parameter,
      description: documentation.parameters?.[parameter.name] ?? null
    }));
    overload.documentationLoaded = true;
  } catch (error) {
    state.memberDocumentationError = String(error?.message || error);
  } finally {
    state.memberDocumentationLoading = false;
    render();
  }
}

async function loadSelectedMemberSource() {
  if (state.memberSource) {
    render();
    return;
  }
  const type = selectedType();
  const member = selectedMember(type);
  const overload = member?.overloads[state.selectedOverloadIndex ?? 0];
  if (!type || !member || !overload) {
    state.memberSourceError = "Select a concrete overload before opening Source.";
    render();
    return;
  }

  state.memberSourceLoading = true;
  state.memberSourceError = "";
  render();
  try {
    state.memberSource = await inspectMemberSource({
      packageId: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework,
      assembly: type.assembly,
      type: type.id,
      member: overload.name,
      signature: overload.signature
    });
  } catch (error) {
    state.memberSourceError = String(error?.message || error);
  } finally {
    state.memberSourceLoading = false;
    render();
  }
}

async function loadSelectedTypeSource() {
  const type = selectedType();
  if (!type) {
    render();
    return;
  }
  const signature = typeSourceSignature(type);
  if (state.typeSourceKey === signature && (state.typeSource || state.typeSourceError)) {
    render();
    return;
  }
  state.typeSourceKey = signature;
  state.typeSource = null;
  state.typeSourceError = "";
  state.typeSourceLoading = true;
  render();
  try {
    const result = await inspectTypeSource({
      packageId: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework,
      assembly: type.assembly,
      type: type.id
    });
    if (state.typeSourceKey === signature) state.typeSource = result;
  } catch (error) {
    if (state.typeSourceKey === signature) state.typeSourceError = String(error?.message || error);
  } finally {
    if (state.typeSourceKey === signature) state.typeSourceLoading = false;
    render();
  }
}

async function loadSelectedMemberCallGraph() {
  if (state.memberCallGraph) {
    render();
    renderMermaidCallGraph();
    return;
  }
  const type = selectedType();
  const member = selectedMember(type);
  const overload = member?.overloads[state.selectedOverloadIndex ?? 0];
  if (!type || !member || !overload) {
    state.memberCallGraphError = "Select a concrete overload before opening Call graph.";
    render();
    return;
  }

  state.memberCallGraphLoading = true;
  state.memberCallGraphError = "";
  render();
  try {
    state.memberCallGraph = await inspectMemberCallGraph({
      packageId: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework,
      assembly: type.assembly,
      type: type.id,
      member: overload.name,
      signature: overload.signature,
      workspace: state.packages.map(packageItem => ({
        package: packageItem.id,
        version: packageItem.version,
        framework: packageItem.activeFramework
      }))
    });
  } catch (error) {
    state.memberCallGraphError = String(error?.message || error);
  } finally {
    state.memberCallGraphLoading = false;
    render();
    if (state.memberCallGraph) renderMermaidCallGraph();
  }
}

async function renderMermaidCallGraph() {
  const container = document.querySelector("#call-graph-diagram");
  if (!container || !state.memberCallGraph?.mermaid) return;
  try {
    mermaidModule ??= import("https://cdn.jsdelivr.net/npm/mermaid@11.15.0/dist/mermaid.esm.min.mjs");
    const { default: mermaid } = await mermaidModule;
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: "strict",
      theme: state.theme === "light" ? "default" : "dark",
      themeVariables: { fontSize: "17px" },
      flowchart: { htmlLabels: false, curve: "basis" }
    });
    const id = `call-graph-${Date.now().toString(36)}`;
    const rootStyle = getComputedStyle(document.documentElement);
    const definition = state.memberCallGraph.mermaid.replace(
      /var\((--[\w-]+)\)/g,
      (whole, name) => rootStyle.getPropertyValue(name).trim() || whole
    );
    const { svg } = await mermaid.render(id, definition);
    if (document.querySelector("#call-graph-diagram") === container) {
      container.innerHTML =
        '<div class="graph-viewport"></div>'
        + '<div class="graph-controls">'
        + '<button type="button" data-zoom="in" title="Zoom in" aria-label="Zoom in">+</button>'
        + '<button type="button" data-zoom="out" title="Zoom out" aria-label="Zoom out">\u2212</button>'
        + '<button type="button" class="reset" data-zoom="reset" title="Reset view" aria-label="Reset view">fit</button>'
        + '</div>';
      const viewport = container.querySelector(".graph-viewport");
      viewport.innerHTML = svg;
      attachGraphPanZoom(container, viewport);
    }
  } catch (error) {
    if (document.querySelector("#call-graph-diagram") === container) {
      container.innerHTML = `<div class="graph-render-error"><strong>Diagram rendering failed</strong><p>${escapeHtml(String(error?.message || error))}</p></div>`;
    }
  }
}

function attachGraphPanZoom(container, viewport) {
  const svg = viewport.querySelector("svg");
  if (!svg) return;

  const box = svg.viewBox?.baseVal;
  const naturalWidth = box && box.width ? box.width : svg.getBoundingClientRect().width;
  const naturalHeight = box && box.height ? box.height : svg.getBoundingClientRect().height;
  svg.setAttribute("width", naturalWidth);
  svg.setAttribute("height", naturalHeight);

  const minScale = 0.2;
  const maxScale = 8;
  const view = { scale: 1, x: 0, y: 0 };
  const clampScale = value => Math.min(maxScale, Math.max(minScale, value));

  function apply() {
    svg.style.transform = `translate(${view.x}px, ${view.y}px) scale(${view.scale})`;
  }

  function fit() {
    const rect = viewport.getBoundingClientRect();
    if (!naturalWidth || !naturalHeight || !rect.width) return;
    view.scale = clampScale(Math.min(rect.width / naturalWidth, rect.height / naturalHeight) * 0.92);
    view.x = (rect.width - naturalWidth * view.scale) / 2;
    view.y = (rect.height - naturalHeight * view.scale) / 2;
    apply();
  }

  function zoomAt(px, py, factor) {
    const next = clampScale(view.scale * factor);
    const ratio = next / view.scale;
    view.x = px - (px - view.x) * ratio;
    view.y = py - (py - view.y) * ratio;
    view.scale = next;
    apply();
  }

  viewport.addEventListener("wheel", event => {
    event.preventDefault();
    const rect = viewport.getBoundingClientRect();
    zoomAt(event.clientX - rect.left, event.clientY - rect.top, Math.exp(-event.deltaY * 0.0015));
  }, { passive: false });

  let pointerId = null;
  let moved = false;
  let capturing = false;
  const panThreshold = 5;
  const start = { x: 0, y: 0, vx: 0, vy: 0 };
  viewport.addEventListener("pointerdown", event => {
    if (event.button !== 0) return;
    pointerId = event.pointerId;
    moved = false;
    capturing = false;
    start.x = event.clientX;
    start.y = event.clientY;
    start.vx = view.x;
    start.vy = view.y;
  });
  viewport.addEventListener("pointermove", event => {
    if (pointerId !== event.pointerId) return;
    const dx = event.clientX - start.x;
    const dy = event.clientY - start.y;
    if (!capturing) {
      if (Math.abs(dx) + Math.abs(dy) <= panThreshold) return;
      capturing = true;
      moved = true;
      viewport.setPointerCapture(pointerId);
      viewport.classList.add("panning");
    }
    view.x = start.vx + dx;
    view.y = start.vy + dy;
    apply();
  });
  function endPan(event) {
    if (pointerId !== event.pointerId) return;
    if (capturing) {
      viewport.releasePointerCapture(pointerId);
      viewport.classList.remove("panning");
    }
    capturing = false;
    pointerId = null;
  }
  viewport.addEventListener("pointerup", endPan);
  viewport.addEventListener("pointercancel", endPan);

  container.querySelectorAll(".graph-controls button").forEach(button => {
    button.addEventListener("click", () => {
      const rect = viewport.getBoundingClientRect();
      const mode = button.dataset.zoom;
      if (mode === "in") zoomAt(rect.width / 2, rect.height / 2, 1.25);
      else if (mode === "out") zoomAt(rect.width / 2, rect.height / 2, 0.8);
      else fit();
    });
  });

  viewport.tabIndex = 0;
  viewport.addEventListener("keydown", event => {
    const rect = viewport.getBoundingClientRect();
    const step = 45;
    if (event.key === "+" || event.key === "=") zoomAt(rect.width / 2, rect.height / 2, 1.25);
    else if (event.key === "-" || event.key === "_") zoomAt(rect.width / 2, rect.height / 2, 0.8);
    else if (event.key === "0") fit();
    else if (event.key === "ArrowLeft") { view.x += step; apply(); }
    else if (event.key === "ArrowRight") { view.x -= step; apply(); }
    else if (event.key === "ArrowUp") { view.y += step; apply(); }
    else if (event.key === "ArrowDown") { view.y -= step; apply(); }
    else return;
    event.preventDefault();
  });

  svg.querySelectorAll("g.node").forEach(node => {
    const label = (node.textContent || "").replace(/\s+/g, " ").trim();
    const target = resolveNodeLabel(label);
    if (!target) return;
    node.classList.add("nav-node");
    node.style.cursor = "pointer";
    node.addEventListener("click", () => {
      if (moved) return;
      navigateToMember(target.pkg, target.type, target.group);
    });
  });

  fit();
}

function resolveNodeLabel(label) {
  const dot = label.lastIndexOf(".");
  if (dot < 0) return null;
  const typeName = label.slice(0, dot);
  const memberName = label.slice(dot + 1);
  const candidates = [state.package, ...state.packages.filter(item => item !== state.package)];
  for (const pkg of candidates) {
    if (!pkg?.types) continue;
    const type = pkg.types.find(item =>
      item.name === typeName || item.id === typeName || item.id.endsWith("." + typeName));
    if (!type) continue;
    const group = memberGroups(type).find(item => item.name === memberName);
    if (group) return { pkg, type, group };
  }
  return null;
}

function navigateToMember(pkg, type, group) {
  state.package = pkg;
  state.lens = "api";
  state.selectedTypeId = type.id;
  state.selectedMemberKey = group.key;
  state.selectedOverloadIndex = null;
  state.memberSection = "overview";
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  loadSelectedMemberDocumentation();
}

async function loadSelectedMemberFacts() {
  if (state.memberFacts) {
    render();
    return;
  }
  const type = selectedType();
  const member = selectedMember(type);
  const overload = member?.overloads[state.selectedOverloadIndex ?? 0];
  if (!type || !member || !overload) {
    state.memberFactsError = "Select a concrete overload before opening Facts.";
    render();
    return;
  }

  state.memberFactsLoading = true;
  state.memberFactsError = "";
  render();
  try {
    state.memberFacts = await inspectMemberFacts({
      packageId: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework,
      assembly: type.assembly,
      type: type.id,
      member: overload.name,
      signature: overload.signature
    });
  } catch (error) {
    state.memberFactsError = String(error?.message || error);
  } finally {
    state.memberFactsLoading = false;
    render();
  }
}

async function loadPackage(packageId, version, framework) {
  state.loading = true;
  state.error = "";
  state.requestedPackage = packageId;
  state.requestedVersion = version;
  state.requestedFramework = framework;
  state.loadingMessage = `Querying ${packageId}@${version}…`;
  render();

  try {
    const result = await inspectPackage(packageId, version, framework);
    const types = (result.types ?? []).map(type => ({
      ...type,
      api: type.api ?? []
    }));
    const packageModel = {
      id: result.package,
      version: result.version,
      frameworks: result.frameworks ?? [],
      activeFramework: result.activeFramework,
      assembly: (result.assemblies ?? []).map(item => item.name).join(", "),
      assemblies: result.assemblies ?? [],
      types,
      totalTypes: types.length,
      totalMembers: result.totalMembers
    };
    const existing = state.packages.findIndex(item =>
      item.id.toLowerCase() === packageModel.id.toLowerCase()
      && item.version.toLowerCase() === packageModel.version.toLowerCase());
    if (existing >= 0) state.packages[existing] = packageModel;
    else state.packages.push(packageModel);
    state.package = packageModel;
    state.selectedTypeId = linkedType && packageModel.types.some(item => item.id === linkedType)
      ? linkedType
      : packageModel.types[0]?.id || "";
    state.selectedMemberKey = "";
    state.typeFilter = "";
    state.namespaceFilter = "";
    state.loading = false;
    render();
    return packageModel;
  } catch (error) {
    state.loading = false;
    state.error = String(error?.stack || error);
    render();
    return null;
  }
}

async function runCallGraphDemo() {
  state.loading = true;
  state.error = "";
  state.loadingMessage = "Loading cross-package call graph demo…";
  render();

  const targetPackage = await loadPackage(
    "Microsoft.Extensions.DependencyInjection.Abstractions",
    "10.0.0",
    "net10.0");
  const loggingPackage = await loadPackage("Microsoft.Extensions.Logging", "10.0.0", "net10.0");
  const httpPackage = await loadPackage("Microsoft.Extensions.Http", "10.0.0", "net10.0");
  if (!targetPackage || !loggingPackage || !httpPackage) return;

  state.package = state.packages.find(item =>
    item.id === "Microsoft.Extensions.DependencyInjection.Abstractions"
    && item.version === "10.0.0") || targetPackage;
  const type = state.package.types.find(item =>
    item.id === "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions");
  const member = type && memberGroups(type).find(item =>
    item.name === "TryAddEnumerable" && item.kind === "method");
  const overloadIndex = member?.overloads.findIndex(item => item.anchorDigest === "74b6b4b321") ?? -1;
  if (!type || !member || overloadIndex < 0) {
    state.loading = false;
    state.error = "The call graph demo member was not found in the selected package.";
    render();
    return;
  }

  state.selectedTypeId = type.id;
  state.selectedMemberKey = member.key;
  state.selectedOverloadIndex = overloadIndex;
  state.memberSection = "call-graph";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.loading = false;
  render();
  await loadSelectedMemberCallGraph();
}

async function bootstrap() {
  state.loading = true;
  state.error = "";
  render();
  const tStart = performance.now();
  try {
    await initializeEngine(message => {
      state.loadingMessage = message;
      render();
    });
    const tEngine = performance.now();
    await loadPackage(state.requestedPackage, state.requestedVersion, state.requestedFramework);
    const tReady = performance.now();
    state.diag = computeDiagnostics(tStart, tEngine, tReady);
    render();
  } catch (error) {
    state.loading = false;
    state.error = String(error?.stack || error);
    render();
  }
}

function computeDiagnostics(tStart, tEngine, tReady) {
  const assets = performance.getEntriesByType("resource")
    .filter(entry => entry.name.includes("/_framework/"));
  let firstStart = Infinity;
  let lastEnd = 0;
  let transfer = 0;
  let decoded = 0;
  for (const entry of assets) {
    firstStart = Math.min(firstStart, entry.startTime);
    lastEnd = Math.max(lastEnd, entry.responseEnd);
    transfer += entry.transferSize || 0;
    decoded += entry.decodedBodySize || 0;
  }
  const hasAssets = assets.length > 0 && Number.isFinite(firstStart);
  return {
    downloadMs: hasAssets ? lastEnd - firstStart : 0,
    startupMs: hasAssets ? Math.max(0, tEngine - lastEnd) : tEngine - tStart,
    precomputeMs: tReady - tEngine,
    totalMs: tReady,
    transfer,
    decoded,
    assets: assets.length
  };
}

function fmtMs(ms) {
  if (ms == null) return "—";
  return ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(2)} s`;
}

function fmtBytes(bytes) {
  if (!bytes) return "—";
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(value < 10 && unit > 0 ? 1 : 0)} ${units[unit]}`;
}

document.addEventListener("keydown", event => {
  const typing = ["INPUT", "SELECT", "TEXTAREA"].includes(document.activeElement?.tagName);
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
    event.preventDefault();
    openCommand();
  } else if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "p") {
    event.preventDefault();
    openSpotlight();
  } else if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "f") {
    event.preventDefault();
    focusFilter();
  } else if (event.altKey && event.key === "ArrowLeft") {
    event.preventDefault();
    navBack();
  } else if (event.altKey && event.key === "ArrowRight") {
    event.preventDefault();
    navForward();
  } else if (!typing && !event.metaKey && !event.ctrlKey && /^[1-6]$/.test(event.key)) {
    state.lens = lenses[Number(event.key) - 1][0];
    render();
  } else if (!typing && !event.defaultPrevented && !event.metaKey && !event.ctrlKey && !event.altKey
      && (event.key === "ArrowUp" || event.key === "ArrowDown")) {
    event.preventDefault();
    stepTypeSelection(event.key === "ArrowDown" ? 1 : -1);
  } else if (!typing && event.key === "/") {
    event.preventDefault();
    focusFilter();
  } else if (!typing && !state.spotlightOpen && !event.metaKey && !event.ctrlKey && !event.altKey
      && !event.defaultPrevented && event.key.length === 1 && /[a-zA-Z]/.test(event.key)) {
    event.preventDefault();
    openSpotlight(event.key);
  }
});

bootstrap();
