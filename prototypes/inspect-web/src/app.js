import { lenses, packageLenses, rootCommands } from "./data.js";
import { initializeEngine, inspectListStyleOptions, inspectMemberAnnotatedSource, inspectMemberCallGraph, inspectMemberDocumentation, inspectMemberFacts, inspectMemberSource, inspectPackage, inspectPackageDependencies, inspectPackageIntegrations, inspectPackageOpportunities, inspectPackagePerformance, inspectSearchTypes, inspectTypeMemberSource, inspectTypeProjection, inspectTypeSource } from "/engine.js";

function loadStoredTaste() {
  try {
    const value = JSON.parse(localStorage.getItem("inspect-taste") || "[]");
    return Array.isArray(value) ? value.filter(item => typeof item === "string") : [];
  } catch {
    return [];
  }
}

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
  memberKindFilter: "all",
  memberSource: null,
  memberSourceLoading: false,
  memberSourceError: "",
  memberAnnotated: null,
  memberAnnotatedLoading: false,
  memberAnnotatedError: "",
  typeSource: null,
  typeSourceLoading: false,
  typeSourceError: "",
  typeSourceKey: "",
  typeMetadata: null,
  typeMetadataLoading: false,
  typeMetadataError: "",
  typeMetadataKey: "",
  packageDependencies: null,
  packageDependenciesLoading: false,
  packageDependenciesError: "",
  packageDependenciesKey: "",
  dependenciesFramework: "",
  workspaceDependencies: {},
  packageIntegrations: null,
  packageIntegrationsLoading: false,
  packageIntegrationsError: "",
  packageIntegrationsKey: "",
  packageOpportunities: null,
  packageOpportunitiesLoading: false,
  packageOpportunitiesError: "",
  packageOpportunitiesKey: "",
  packagePerformance: null,
  packagePerformanceLoading: false,
  packagePerformanceError: "",
  packagePerformanceKey: "",
  memberCallGraph: null,
  memberCallGraphLoading: false,
  memberCallGraphError: "",
  memberFacts: null,
  memberFactsLoading: false,
  memberFactsError: "",
  memberDocumentationLoading: false,
  memberDocumentationError: "",
  lens: "api",
  packageLens: "overview",
  atPackageRoot: false,
  typeFilter: "",
  namespaceFilter: "",
  kindFilter: "",
  command: "",
  completionIndex: 0,
  promptOpen: false,
  spotlightOpen: false,
  spotlightQuery: "",
  spotlightIndex: 0,
  graphSourceOpen: false,
  graphSource: null,
  graphSourceLoading: false,
  graphSourceError: "",
  graphSourceTitle: "",
  graphSourceRequest: null,
  styleOptions: null,
  taste: loadStoredTaste(),
  tasteOpen: false,
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
    s: state.memberSection,
    pr: state.atPackageRoot,
    pl: state.packageLens
  });
}

function captureView() {
  return {
    package: state.package?.id ?? "",
    lens: state.lens,
    selectedTypeId: state.selectedTypeId,
    selectedMemberKey: state.selectedMemberKey,
    selectedOverloadIndex: state.selectedOverloadIndex,
    memberSection: state.memberSection,
    atPackageRoot: state.atPackageRoot,
    packageLens: state.packageLens
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
  state.atPackageRoot = view.atPackageRoot ?? false;
  state.packageLens = view.packageLens ?? "overview";
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  const type = selectedType();
  if (!state.atPackageRoot && state.lens === "api" && state.selectedMemberKey && selectedMember(type)) {
    if (state.memberSection === "source") loadSelectedMemberSource();
    else if (state.memberSection === "annotated") loadSelectedMemberAnnotatedSource();
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

const memberSections = ["overview", "source", "annotated", "call-graph", "facts"];

function parseLocation() {
  const params = new URLSearchParams(location.search);
  const route = location.pathname.split("/").filter(Boolean);
  const packageAt = route.findIndex(part => part.toLowerCase() === "packages");
  const hashLens = location.hash.slice(1);
  return {
    package: packageAt >= 0 ? decodeURIComponent(route[packageAt + 1] || "") : params.get("package"),
    version: packageAt >= 0 ? decodeURIComponent(route[packageAt + 2] || "") : params.get("version"),
    framework: params.get("framework"),
    type: params.get("type"),
    member: params.get("member"),
    overload: params.get("overload"),
    section: params.get("section"),
    lens: lenses.some(([id]) => id === hashLens) ? hashLens : null,
    atPackageRoot: hashLens === "pkg" || hashLens.startsWith("pkg:"),
    packageLens: (hashLens === "pkg" || hashLens.startsWith("pkg:"))
      ? (packageLenses.some(([id]) => id === hashLens.split(":")[1]) ? hashLens.split(":")[1] : "overview")
      : null
  };
}

const initialLocation = parseLocation();
if (initialLocation.package) {
  state.requestedPackage = initialLocation.package;
  state.requestedVersion = initialLocation.version || "latest";
}
if (initialLocation.framework) state.requestedFramework = initialLocation.framework;
if (initialLocation.lens) state.lens = initialLocation.lens;
if (initialLocation.atPackageRoot) {
  state.atPackageRoot = true;
  state.packageLens = initialLocation.packageLens || "overview";
}

// Deep-link selection to restore once the first package model is available. Consumed
// (and cleared) by the first loadPackage so later package switches start fresh.
let pendingDeepLink = {
  type: initialLocation.type,
  member: initialLocation.member,
  overload: initialLocation.overload,
  section: initialLocation.section
};

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
    return matchesText
      && (!state.namespaceFilter || item.namespace === state.namespaceFilter)
      && (!state.kindFilter || typeKind(item.kind) === state.kindFilter);
  });
}

function namespaces() {
  if (!state.package) return [];
  return [...new Set(state.package.types.map(item => item.namespace))];
}

// Collapse a raw kind string ("sealed class", "readonly struct", "enum", …) to a
// primary bucket used by the kind filter chips.
function typeKind(kind) {
  const value = (kind || "").toLowerCase();
  if (value.includes("interface")) return "interface";
  if (value.includes("delegate")) return "delegate";
  if (value.includes("enum")) return "enum";
  if (value.includes("struct")) return "struct";
  return "class";
}

const KIND_ORDER = ["class", "struct", "interface", "enum", "delegate"];

// Kind buckets present in the current package, honoring the active namespace filter
// (but not the kind filter itself, so chips stay stable while one is selected).
function typeKinds() {
  if (!state.package) return [];
  const present = new Set(state.package.types
    .filter(item => !state.namespaceFilter || item.namespace === state.namespaceFilter)
    .map(item => typeKind(item.kind)));
  return KIND_ORDER.filter(kind => present.has(kind));
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

// Selection sits on a scope ladder: package (a whole NuGet package / its assemblies),
// type (one public type), or member (a member + its overloads under the API lens). The
// lens strip, detail pane, and arrow keys all react to the active scope.
function scope() {
  if (state.atPackageRoot) return "package";
  return state.lens === "api" && state.selectedMemberKey ? "member" : "type";
}

function activeLenses() {
  return scope() === "package" ? packageLenses : lenses;
}

// The nav pane reacts to context: types at the top level, or the current type's
// members (with the active member's overloads nested) once a member is open under
// the API lens. Both modes render into #type-list so keyboard/scroll logic is shared.
function navMode() {
  if (state.atPackageRoot) return "type";
  return state.lens === "api" && state.selectedMemberKey ? "member" : "type";
}

function resetMemberSectionState() {
  state.memberSection = "overview";
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
}

function openMemberGroup(key) {
  state.selectedMemberKey = key;
  state.selectedOverloadIndex = null;
  resetMemberSectionState();
  loadSelectedMemberDocumentation();
}

function openOverload(index) {
  state.selectedOverloadIndex = index;
  resetMemberSectionState();
  loadSelectedMemberDocumentation();
}

// Flattened, ordered nav rows for member mode: every member group, with the active
// group's overloads nested immediately beneath it. This is the exact list ↑/↓ walks.
function memberNavEntries(type) {
  const entries = [];
  for (const group of memberGroups(type)) {
    entries.push({ kind: "member", group });
    if (group.key === state.selectedMemberKey && group.overloads.length > 1) {
      group.overloads.forEach((_, index) => entries.push({ kind: "overload", group, index }));
    }
  }
  return entries;
}

function memberNavCursor(entries) {
  const index = entries.findIndex(entry => {
    if (entry.kind === "overload") {
      return entry.group.key === state.selectedMemberKey && state.selectedOverloadIndex === entry.index;
    }
    const isMulti = entry.group.overloads.length > 1;
    return entry.group.key === state.selectedMemberKey && (isMulti ? state.selectedOverloadIndex == null : true);
  });
  return index < 0 ? 0 : index;
}

function selectMemberNavEntry(entry, focusList) {
  if (entry.kind === "member") {
    if (entry.group.key === state.selectedMemberKey && entry.group.overloads.length === 1) {
      render();
    } else {
      openMemberGroup(entry.group.key);
    }
  } else {
    if (entry.group.key !== state.selectedMemberKey) state.selectedMemberKey = entry.group.key;
    openOverload(entry.index);
  }
  requestAnimationFrame(() => {
    if (focusList) document.querySelector("#type-list")?.focus();
    document.querySelector("#type-list .selected")?.scrollIntoView({ block: "nearest" });
  });
}

function stepMemberNav(delta, focusList) {
  const type = selectedType();
  const entries = memberNavEntries(type);
  if (!entries.length) return;
  let cursor = memberNavCursor(entries);
  cursor = Math.max(0, Math.min(entries.length - 1, cursor + delta));
  selectMemberNavEntry(entries[cursor], focusList);
}

// ↑/↓ always act on the visible nav list, whatever depth you are at.
function stepNav(delta) {
  if (navMode() === "member") stepMemberNav(delta, false);
  else stepTypeSelection(delta);
}

// ←/→ act on the horizontal tab strip at your depth: sections when a concrete
// overload is open, otherwise the lens strip.
function stepHorizontal(delta) {
  if (state.atPackageRoot) {
    const index = packageLenses.findIndex(([id]) => id === state.packageLens);
    state.packageLens = packageLenses[(index + delta + packageLenses.length) % packageLenses.length][0];
    render();
    return;
  }
  const type = selectedType();
  const member = state.lens === "api" ? selectedMember(type) : null;
  const overloadOpen = member && !(member.overloads.length > 1 && state.selectedOverloadIndex == null);
  if (overloadOpen) {
    const order = ["overview", "call-graph", "facts", "source", "annotated"];
    let index = order.indexOf(state.memberSection);
    if (index < 0) index = 0;
    state.memberSection = order[(index + delta + order.length) % order.length];
    if (state.memberSection === "source") loadSelectedMemberSource();
    else if (state.memberSection === "annotated") loadSelectedMemberAnnotatedSource();
    else if (state.memberSection === "call-graph") loadSelectedMemberCallGraph();
    else if (state.memberSection === "facts") loadSelectedMemberFacts();
    else loadSelectedMemberDocumentation();
  } else {
    const index = lenses.findIndex(([id]) => id === state.lens);
    state.lens = lenses[(index + delta + lenses.length) % lenses.length][0];
    render();
  }
}

// Enter drills one level deeper; Escape/Backspace pops back out.
function drillIn() {
  if (state.atPackageRoot) {
    state.atPackageRoot = false;
    render();
    return;
  }
  const type = selectedType();
  if (!type) return;
  if (navMode() === "type") {
    if (state.lens !== "api") state.lens = "api";
    const groups = memberGroups(type);
    if (groups.length) openMemberGroup(groups[0].key);
    else render();
  } else {
    const member = selectedMember(type);
    if (member && member.overloads.length > 1 && state.selectedOverloadIndex == null) {
      openOverload(0);
    } else {
      document.querySelector(".detail-scroll")?.focus?.();
    }
  }
}

function drillOut() {
  if (navMode() === "member") {
    const member = selectedMember(selectedType());
    if (member && member.overloads.length > 1 && state.selectedOverloadIndex != null) {
      state.selectedOverloadIndex = null;
    } else {
      state.selectedMemberKey = "";
      state.selectedOverloadIndex = null;
    }
    render();
    return true;
  }
  if (!state.atPackageRoot) {
    state.atPackageRoot = true;
    render();
    return true;
  }
  return false;
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
        <button class="home-lens ${state.atPackageRoot ? "active" : ""}" id="package-root" title="Package scope"><span>⌘</span> Types</button>
        <span class="lens-separator"></span>
        ${state.atPackageRoot
          ? packageLenses.map(([id, label], index) => `
            <button class="lens ${state.packageLens === id ? "active" : ""}" data-package-lens="${id}">
              ${escapeHtml(label)}<kbd>${index + 1}</kbd>
            </button>`).join("")
          : lenses.map(([id, label], index) => `
            <button class="lens ${state.lens === id ? "active" : ""}" data-lens="${id}">
              ${escapeHtml(label)}<kbd>${index + 1}</kbd>
            </button>`).join("")}
      </nav>

      <main class="workspace">
        ${renderNavPane(current, visible)}

        <section class="detail-pane">
          <header class="detail-head">
            <div class="nav-history">
              <button id="nav-back" ${nav.index > 0 ? "" : "disabled"} title="Back (Alt+←)" aria-label="Back">‹</button>
              <button id="nav-forward" ${nav.index < nav.stack.length - 1 ? "" : "disabled"} title="Forward (Alt+→)" aria-label="Forward">›</button>
            </div>
            <div class="breadcrumbs">
              ${state.atPackageRoot
                ? `<strong>${escapeHtml(state.package.id)}</strong><b>/</b><span>${escapeHtml(packageLenses.find(([id]) => id === state.packageLens)?.[1] || "Overview")}</span>`
                : `<span>${escapeHtml(state.package.id)}</span><b>/</b><span>${escapeHtml(current.namespace)}</span><b>/</b><strong>${escapeHtml(current.name)}</strong>
              ${state.selectedMemberKey ? `<b>/</b><strong>${escapeHtml(selectedMember(current)?.name ?? "")}</strong>` : ""}`}
            </div>
            <div class="detail-actions"><button id="copy-name" type="button">copy name</button><button id="taste-btn" class="${state.taste.length ? "active" : ""}" title="Decompiler style (taste)">taste${state.taste.length ? ` · ${state.taste.length}` : ""}</button></div>
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
      ${state.graphSourceOpen ? renderGraphSource() : ""}
      ${state.tasteOpen ? renderTastePopover() : ""}
    </div>`;

  bindEvents();
  recordNav();
  syncUrl();
  maybeAutoLoadTypeSource();
  maybeAutoLoadTypeMetadata();
  maybeAutoLoadPackageDependencies();
  maybeAutoLoadPackageIntegrations();
  maybeAutoLoadPackageOpportunities();
  maybeAutoLoadPackagePerformance();
}

function maybeAutoLoadTypeSource() {
  if (state.lens !== "source") return;
  const type = selectedType();
  if (!type) return;
  const signature = typeSourceSignature(type);
  if (state.typeSourceKey === signature) return;
  loadSelectedTypeSource();
}

function maybeAutoLoadTypeMetadata() {
  if (state.lens !== "metadata") return;
  const type = selectedType();
  if (!type) return;
  const signature = typeMetadataSignature(type);
  if (state.typeMetadataKey === signature) {
    if (state.typeMetadata?.graphNodes?.length > 1) renderTypeGraph();
    return;
  }
  loadSelectedTypeMetadata();
}

function renderNavPane(current, visible) {
  return navMode() === "member" ? renderMemberNav(current) : renderTypeNav(current, visible);
}

function renderTypeNav(current, visible) {
  return `
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
      <div class="namespace-chips kind-chips" aria-label="Type kind filters">
        <button class="${!state.kindFilter ? "active" : ""}" data-kind-filter="">all kinds</button>
        ${typeKinds().map(kind => `<button class="${state.kindFilter === kind ? "active" : ""}" data-kind-filter="${kind}">${kind}</button>`).join("")}
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
      <footer class="pane-footer"><span>↑↓ types</span><span>←→ lens</span><span>↵ open</span></footer>
    </aside>`;
}

function renderMemberNav(type) {
  const entries = memberNavEntries(type);
  return `
    <aside class="type-browser member-nav" aria-label="Members of ${escapeHtml(type.name)}">
      <div class="browser-head">
        <div>
          <span class="pane-label">MEMBERS</span>
          <span class="result-count">${memberGroups(type).length} members</span>
        </div>
      </div>
      <button class="nav-back-row" id="nav-to-types" title="Back to types (Esc)">
        <span class="chevron">‹</span>
        <span class="type-name">${escapeHtml(type.name)}</span>
        <small>types</small>
      </button>
      <div class="type-list member-list" role="listbox" tabindex="0" id="type-list">
        ${entries.map(entry => {
          if (entry.kind === "member") {
            const group = entry.group;
            const isMulti = group.overloads.length > 1;
            const active = group.key === state.selectedMemberKey;
            const selected = active && (isMulti ? state.selectedOverloadIndex == null : true);
            return `<button class="type-row member-row ${active ? "active-group" : ""} ${selected ? "selected" : ""}" data-nav-member="${escapeHtml(group.key)}" role="option" aria-selected="${selected}">
              <span class="member-icon">${escapeHtml(group.kind?.slice(0, 1)?.toUpperCase() || "M")}</span>
              <span class="type-name">${escapeHtml(group.name)}</span>
              <small>${isMulti ? `${group.overloads.length}×` : escapeHtml(shortKind(group.kind))}</small>
            </button>`;
          }
          const selected = entry.group.key === state.selectedMemberKey && state.selectedOverloadIndex === entry.index;
          return `<button class="type-row overload-nav-row ${selected ? "selected" : ""}" data-nav-overload="${entry.index}" role="option" aria-selected="${selected}">
            <span class="overload-branch">↳</span>
            <code>${highlight(entry.group.overloads[entry.index].signature)}</code>
          </button>`;
        }).join("")}
      </div>
      <footer class="pane-footer"><span>↑↓ members</span><span>←→ sections</span><span>esc types</span></footer>
    </aside>`;
}

function packageHeading() {
  const pkg = state.package;
  return `<header class="type-heading">
    <div class="type-badge">⬡</div>
    <div>
      <div class="type-namespace">NuGet package</div>
      <h1>${escapeHtml(pkg.id)}</h1>
      <code class="type-signature">${escapeHtml(pkg.id)}@${escapeHtml(pkg.version)}</code>
    </div>
    <div class="type-metrics"><span><strong>${pkg.totalTypes}</strong> types</span><span><strong>${pkg.totalMembers.toLocaleString()}</strong> members</span></div>
    <dl class="definition-list">
      <div><dt>Active TFM:</dt><dd>${escapeHtml(pkg.activeFramework)}</dd></div>
      <div><dt>Assemblies:</dt><dd>${pkg.assemblies?.length ?? 0}</dd></div>
      <div><dt>Frameworks:</dt><dd>${pkg.frameworks.length}</dd></div>
    </dl>
  </header>`;
}

function packageLensPlaceholder(lensId) {
  const copy = {
    dependencies: ["⌘", "Dependencies", "Package NuGet dependencies and assembly references. Wiring the engine export in a follow-up pass."]
  }[lensId] || ["△", "Not available", "This package lens is not wired yet."];
  return `<section class="document-section empty-document"><span class="large-glyph">${copy[0]}</span><h2>${escapeHtml(copy[1])}</h2><p>${escapeHtml(copy[2])}</p></section>`;
}

function renderPackageView() {
  if (state.packageLens === "overview") return `${packageHeading()}${renderPackageOverview()}`;
  if (state.packageLens === "dependencies") return `${packageHeading()}${renderPackageDependencies()}`;
  if (state.packageLens === "integrations") return `${packageHeading()}${renderPackageIntegrations()}`;
  if (state.packageLens === "opportunities") return `${packageHeading()}${renderPackageOpportunities()}`;
  if (state.packageLens === "analysis") return `${packageHeading()}${renderPackagePerformance()}`;
  return `${packageHeading()}${packageLensPlaceholder(state.packageLens)}`;
}

function packageDependenciesSignature() {
  const pkg = state.package;
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}`;
}

function renderPackageDependencies() {
  const current = packageDependenciesSignature();
  const fresh = state.packageDependenciesKey === current;
  if (state.packageDependenciesLoading && fresh) {
    return `<section class="document-section source-progress"><span class="loader"></span><h2>Reading dependencies…</h2><p>Parsing the package manifest and assembly references.</p></section>`;
  }
  if (fresh && state.packageDependenciesError) {
    return `<section class="document-section empty-document"><span class="large-glyph">⌘</span><h2>Dependency query failed</h2><p>${escapeHtml(state.packageDependenciesError)}</p></section>`;
  }
  const data = fresh ? state.packageDependencies : null;
  if (!data) {
    return `<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const groups = data.dependencyGroups || [];
  if (!groups.length) {
    return `<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No package dependencies</h2><p>The manifest declares no NuGet dependencies — a self-contained package.</p></section>`;
  }

  const selectedTfm = resolveDependenciesFramework(groups);
  const selectorChips = groups
    .map(group => `<button class="type-chip ${group.framework === selectedTfm ? "active" : ""}" data-dep-framework="${escapeHtml(group.framework)}">${escapeHtml(group.framework)}</button>`)
    .join("");
  const selector = `
    <section class="document-section">
      <div class="section-title"><h2>Target frameworks</h2><span>one framework at a time</span></div>
      <div class="type-chip-list">${selectorChips}</div>
    </section>`;

  const group = groups.find(candidate => candidate.framework === selectedTfm) || groups[0];
  const deps = group.dependencies || [];
  const openIds = new Set(state.packages.map(item => item.id.toLowerCase()));
  const depList = `
    <section class="document-section">
      <div class="section-title"><h2>NuGet dependencies</h2><span>${escapeHtml(group.framework)} · ${deps.length} package${deps.length === 1 ? "" : "s"}</span></div>
      ${deps.length
        ? `<ul class="dep-list">${deps.map(dependency => {
            const isOpen = openIds.has(dependency.id.toLowerCase());
            const nameCell = isOpen
              ? `<button class="dep-name as-link" data-dep-open="${escapeHtml(dependency.id)}" title="Open ${escapeHtml(dependency.id)}">${escapeHtml(dependency.id)}</button>`
              : `<span class="dep-name" title="${escapeHtml(dependency.id)}">${escapeHtml(dependency.id)}</span>`;
            return `<li>${nameCell}<code class="dep-version">${escapeHtml(dependency.versionRange || "*")}</code></li>`;
          }).join("")}</ul>`
        : `<div class="empty-list">No package dependencies declared for ${escapeHtml(group.framework)}.</div>`}
    </section>`;

  const graphSection = `
    <section class="document-section">
      <div class="section-title"><h2>Dependency graph</h2><span>callers above · direct dependencies below</span></div>
      <p class="lens-note">Open packages that depend on <strong>${escapeHtml(state.package.id)}</strong> appear as callers; its own direct <code>${escapeHtml(group.framework)}</code> dependencies appear below. Open-package nodes are clickable.</p>
      <div id="dependency-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph…</p></div>
    </section>`;

  return `${selector}${graphSection}${depList}`;
}

function resolveDependenciesFramework(groups) {
  const available = groups.map(group => group.framework);
  if (state.dependenciesFramework && available.includes(state.dependenciesFramework)) {
    return state.dependenciesFramework;
  }
  const active = groups.find(group => group.isActive);
  return active ? active.framework : available[0];
}

async function loadPackageDependencies() {
  const signature = packageDependenciesSignature();
  if (state.packageDependenciesKey === signature && (state.packageDependencies || state.packageDependenciesError)) {
    render();
    return;
  }
  state.packageDependenciesKey = signature;
  state.packageDependencies = null;
  state.packageDependenciesError = "";
  state.packageDependenciesLoading = true;
  render();
  try {
    const result = await inspectPackageDependencies({
      packageId: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework,
      assembly: state.package.assembly
    });
    if (state.packageDependenciesKey === signature) state.packageDependencies = result;
    if (result?.dependencyGroups) {
      state.workspaceDependencies[`${state.package.id.toLowerCase()}@${state.package.version.toLowerCase()}`] = result.dependencyGroups;
    }
  } catch (error) {
    if (state.packageDependenciesKey === signature) state.packageDependenciesError = String(error?.message || error);
  } finally {
    if (state.packageDependenciesKey === signature) state.packageDependenciesLoading = false;
    render();
    ensureWorkspaceDependencies();
  }
}

function maybeAutoLoadPackageDependencies() {
  if (!state.atPackageRoot || state.packageLens !== "dependencies") return;
  if (state.packageDependenciesKey === packageDependenciesSignature()) {
    if (state.packageDependencies) {
      renderDependencyGraph();
      ensureWorkspaceDependencies();
    }
    return;
  }
  loadPackageDependencies();
}

// Fetches dependency manifests for every other open package so the dependency graph can
// draw incoming "caller" edges (open packages that declare a dependency on the current one).
async function ensureWorkspaceDependencies() {
  const missing = state.packages.filter(item =>
    !state.workspaceDependencies[`${item.id.toLowerCase()}@${item.version.toLowerCase()}`]);
  if (!missing.length) {
    renderDependencyGraph();
    return;
  }
  for (const item of missing) {
    const key = `${item.id.toLowerCase()}@${item.version.toLowerCase()}`;
    try {
      const result = await inspectPackageDependencies({
        packageId: item.id,
        version: item.version,
        framework: item.activeFramework,
        assembly: item.assembly
      });
      state.workspaceDependencies[key] = result?.dependencyGroups || [];
    } catch {
      state.workspaceDependencies[key] = [];
    }
  }
  if (state.atPackageRoot && state.packageLens === "dependencies") renderDependencyGraph();
}

function packageIntegrationsSignature() {
  const pkg = state.package;
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}`;
}

function renderPackageIntegrations() {
  const current = packageIntegrationsSignature();
  const fresh = state.packageIntegrationsKey === current;
  if (state.packageIntegrationsLoading && fresh) {
    return `<section class="document-section source-progress"><span class="loader"></span><h2>Scanning integrations…</h2><p>Reading the public surface of each assembly for ecosystem signals.</p></section>`;
  }
  if (fresh && state.packageIntegrationsError) {
    return `<section class="document-section empty-document"><span class="large-glyph">◈</span><h2>Integration scan failed</h2><p>${escapeHtml(state.packageIntegrationsError)}</p></section>`;
  }
  const data = fresh ? state.packageIntegrations : null;
  if (!data) {
    return `<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const categories = data.categories || [];
  const warning = data.inspectionError
    ? `<section class="document-section metadata-warning"><strong>⚠ Some assemblies could not be scanned</strong><ul><li><code>${escapeHtml(data.inspectionError)}</code></li></ul></section>`
    : "";

  if (!categories.length) {
    return `${warning}<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No ecosystem integrations detected</h2><p>The public surface of ${escapeHtml(state.package.activeFramework)} shows no known DI, logging, OpenTelemetry, ASP.NET Core, AI, or hosting signals.</p></section>`;
  }

  const summary = `
    <section class="document-section">
      <div class="section-title"><h2>Ecosystem integrations</h2><span>${categories.length} categor${categories.length === 1 ? "y" : "ies"} · ${data.totalSignals} signal${data.totalSignals === 1 ? "" : "s"} · ${escapeHtml(state.package.activeFramework)}</span></div>
      <div class="type-chip-list">${categories.map(category => `<span class="type-chip">${escapeHtml(category.integration)} <span class="ns-count">${category.signals.length}</span></span>`).join("")}</div>
    </section>`;

  const blocks = categories.map(category => `
    <section class="document-section">
      <div class="section-title"><h2>${escapeHtml(category.integration)}</h2><span>${category.typeCount} type${category.typeCount === 1 ? "" : "s"} · ${category.apiCount} API${category.apiCount === 1 ? "" : "s"}</span></div>
      <div class="type-chip-list">${category.signals.map(signal => `<code class="attr-chip" title="${escapeHtml(signal.shape)} · ${escapeHtml(signal.kind)}">${escapeHtml(signal.name)} <span class="ref-version">${escapeHtml(signal.kind)}</span></code>`).join("")}</div>
    </section>`).join("");

  return `${warning}${summary}${blocks}`;
}

async function loadPackageIntegrations() {
  const signature = packageIntegrationsSignature();
  if (state.packageIntegrationsKey === signature && (state.packageIntegrations || state.packageIntegrationsError)) {
    render();
    return;
  }
  state.packageIntegrationsKey = signature;
  state.packageIntegrations = null;
  state.packageIntegrationsError = "";
  state.packageIntegrationsLoading = true;
  render();
  try {
    const result = await inspectPackageIntegrations({
      packageId: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework
    });
    if (state.packageIntegrationsKey === signature) state.packageIntegrations = result;
  } catch (error) {
    if (state.packageIntegrationsKey === signature) state.packageIntegrationsError = String(error?.message || error);
  } finally {
    if (state.packageIntegrationsKey === signature) state.packageIntegrationsLoading = false;
    render();
  }
}

function maybeAutoLoadPackageIntegrations() {
  if (!state.atPackageRoot || state.packageLens !== "integrations") return;
  if (state.packageIntegrationsKey === packageIntegrationsSignature()) return;
  loadPackageIntegrations();
}

function packageScopeSignature() {
  const pkg = state.package;
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}`;
}

function renderPackageOpportunities() {
  const current = packageScopeSignature();
  const fresh = state.packageOpportunitiesKey === current;
  if (state.packageOpportunitiesLoading && fresh) {
    return `<section class="document-section source-progress"><span class="loader"></span><h2>Scanning opportunities…</h2><p>Comparing the public surface against ecosystem integration patterns.</p></section>`;
  }
  if (fresh && state.packageOpportunitiesError) {
    return `<section class="document-section empty-document"><span class="large-glyph">△</span><h2>Opportunity scan failed</h2><p>${escapeHtml(state.packageOpportunitiesError)}</p></section>`;
  }
  const data = fresh ? state.packageOpportunities : null;
  if (!data) {
    return `<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const categories = data.categories || [];
  const warning = data.inspectionError
    ? `<section class="document-section metadata-warning"><strong>⚠ Some assemblies could not be scanned</strong><ul><li><code>${escapeHtml(data.inspectionError)}</code></li></ul></section>`
    : "";

  if (!categories.length) {
    return `${warning}<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No integration opportunities</h2><p>The public surface of ${escapeHtml(state.package.activeFramework)} shows no obvious auth, cloud-client, configuration, database, or AI-client patterns that suggest a missing ecosystem integration.</p></section>`;
  }

  const summary = `
    <section class="document-section">
      <div class="section-title"><h2>Integration opportunities</h2><span>${categories.length} area${categories.length === 1 ? "" : "s"} · ${data.totalOpportunities} suggestion${data.totalOpportunities === 1 ? "" : "s"} · ${escapeHtml(state.package.activeFramework)}</span></div>
      <p class="lens-note">Ecosystem areas this package's surface suggests but does not yet integrate with. Each row points at what to look for.</p>
    </section>`;

  const blocks = categories.map(category => `
    <section class="document-section">
      <div class="section-title"><h2>${escapeHtml(category.integration)}</h2><span>${category.items.length} suggestion${category.items.length === 1 ? "" : "s"}</span></div>
      <dl class="fact-rows">${category.items.map(item => `<div><dt><code>${escapeHtml(item.api)}</code></dt><dd>${escapeHtml(item.integrationType)} · <span class="dim">look for</span> <code>${escapeHtml(item.lookFor)}</code></dd></div>`).join("")}</dl>
    </section>`).join("");

  return `${warning}${summary}${blocks}`;
}

async function loadPackageOpportunities() {
  const signature = packageScopeSignature();
  if (state.packageOpportunitiesKey === signature && (state.packageOpportunities || state.packageOpportunitiesError)) {
    render();
    return;
  }
  state.packageOpportunitiesKey = signature;
  state.packageOpportunities = null;
  state.packageOpportunitiesError = "";
  state.packageOpportunitiesLoading = true;
  render();
  try {
    const result = await inspectPackageOpportunities({
      packageId: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework
    });
    if (state.packageOpportunitiesKey === signature) state.packageOpportunities = result;
  } catch (error) {
    if (state.packageOpportunitiesKey === signature) state.packageOpportunitiesError = String(error?.message || error);
  } finally {
    if (state.packageOpportunitiesKey === signature) state.packageOpportunitiesLoading = false;
    render();
  }
}

function maybeAutoLoadPackageOpportunities() {
  if (!state.atPackageRoot || state.packageLens !== "opportunities") return;
  if (state.packageOpportunitiesKey === packageScopeSignature()) return;
  loadPackageOpportunities();
}

function renderPackagePerformance() {
  const current = packageScopeSignature();
  const fresh = state.packagePerformanceKey === current;
  if (state.packagePerformanceLoading && fresh) {
    return `<section class="document-section source-progress"><span class="loader"></span><h2>Analyzing allocations…</h2><p>Classifying allocation and performance opportunities across every method body.</p></section>`;
  }
  if (fresh && state.packagePerformanceError) {
    return `<section class="document-section empty-document"><span class="large-glyph">△</span><h2>Analysis failed</h2><p>${escapeHtml(state.packagePerformanceError)}</p></section>`;
  }
  const data = fresh ? state.packagePerformance : null;
  if (!data) {
    return `<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const members = data.members || [];
  const warning = data.inspectionError
    ? `<section class="document-section metadata-warning"><strong>⚠ Some assemblies could not be analyzed</strong><ul><li><code>${escapeHtml(data.inspectionError)}</code></li></ul></section>`
    : "";
  const nonPublicNote = data.nonPublicOpportunities > 0
    ? ` · ${data.nonPublicOpportunities} in non-public members`
    : "";

  if (!members.length) {
    return `${warning}<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No public allocation hot spots</h2><p>${data.totalOpportunities} allocation/performance opportunit${data.totalOpportunities === 1 ? "y was" : "ies were"} classified, but none surface on a public member of ${escapeHtml(state.package.activeFramework)}${nonPublicNote}. Open a member's Facts lens to inspect its body directly.</p></section>`;
  }

  const rows = members.map(member => {
    const display = `${shortTypeName(member.typeId)}.${escapeHtml(member.memberName)}`;
    const shapes = member.shapes.map(shape => `<span class="perf-shape">${escapeHtml(shape)}</span>`).join("");
    const loopBadge = member.inLoopCount > 0 ? `<span class="perf-loop" title="${member.inLoopCount} in a loop">↻ ${member.inLoopCount}</span>` : "";
    return `
      <button class="perf-row" data-perf-token="${member.metadataToken}" title="${escapeHtml(member.typeId)}.${escapeHtml(member.memberName)} — open Facts">
        <span class="perf-count">${member.opportunityCount}</span>
        <span class="perf-member"><span class="perf-name">${display}</span><span class="perf-shapes">${shapes}</span></span>
        <span class="perf-meta">${loopBadge}<span class="perf-confidence perf-${escapeHtml((member.confidence || "").toLowerCase())}">${escapeHtml(member.confidence || "—")}</span></span>
      </button>`;
  }).join("");

  const summary = `
    <section class="document-section">
      <div class="section-title"><h2>Allocation &amp; performance triage</h2><span>${members.length} public member${members.length === 1 ? "" : "s"} · ${data.totalOpportunities} opportunit${data.totalOpportunities === 1 ? "y" : "ies"}${nonPublicNote} · ${escapeHtml(state.package.activeFramework)}</span></div>
      <p class="lens-note">Ranked by in-loop opportunities, then count. Static IL classification — confirm impact with a benchmark or profiler. Select a member to open its Facts lens.</p>
    </section>`;

  return `${warning}${summary}<section class="document-section"><div class="perf-list">${rows}</div></section>`;
}

async function loadPackagePerformance() {
  const signature = packageScopeSignature();
  if (state.packagePerformanceKey === signature && (state.packagePerformance || state.packagePerformanceError)) {
    render();
    return;
  }
  state.packagePerformanceKey = signature;
  state.packagePerformance = null;
  state.packagePerformanceError = "";
  state.packagePerformanceLoading = true;
  render();
  try {
    const result = await inspectPackagePerformance({
      packageId: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework
    });
    if (state.packagePerformanceKey === signature) state.packagePerformance = result;
  } catch (error) {
    if (state.packagePerformanceKey === signature) state.packagePerformanceError = String(error?.message || error);
  } finally {
    if (state.packagePerformanceKey === signature) state.packagePerformanceLoading = false;
    render();
  }
}

function maybeAutoLoadPackagePerformance() {
  if (!state.atPackageRoot || state.packageLens !== "analysis") return;
  if (state.packagePerformanceKey === packageScopeSignature()) return;
  loadPackagePerformance();
}

// Drills from a perf-triage row to the member's Facts lens by metadata token: the token
// is joined against the same public API surface the nav pane renders, so the member,
// its overload, and its declaring type are all resolvable client-side.
function drillToPerfMember(token) {
  const numeric = Number(token);
  const targetType = state.package.types.find(type =>
    (type.api || []).some(member => member.metadataToken === numeric));
  if (!targetType) return;
  const member = targetType.api.find(candidate => candidate.metadataToken === numeric);
  if (!member) return;

  state.atPackageRoot = false;
  state.selectedTypeId = targetType.id;
  state.namespaceFilter = "";
  state.memberKindFilter = "all";
  state.lens = "api";
  const key = `${member.kind}:${member.name}`;
  state.selectedMemberKey = key;
  const group = memberGroups(targetType).find(candidate => candidate.key === key);
  const overloadIndex = group && group.overloads.length > 1
    ? group.overloads.findIndex(overload => overload.metadataToken === numeric)
    : -1;
  state.selectedOverloadIndex = overloadIndex >= 0 ? overloadIndex : null;
  resetMemberSectionState();
  state.memberSection = "facts";
  state.typeCursor = filteredTypes().findIndex(candidate => candidate.id === targetType.id);
  loadSelectedMemberFacts();
}

function renderPackageOverview() {
  const pkg = state.package;

  const frameworks = pkg.frameworks
    .map(framework => `<button class="type-chip ${framework === pkg.activeFramework ? "active" : ""}" data-framework-chip="${escapeHtml(framework)}">${escapeHtml(framework)}</button>`)
    .join("");

  const assemblies = (pkg.assemblies || [])
    .map(assembly => `<div class="assembly-cell"><span class="assembly-name" title="${escapeHtml(assembly.name)}">${escapeHtml(assembly.name)}</span><span class="assembly-stats"><strong>${assembly.publicTypes}</strong> type${assembly.publicTypes === 1 ? "" : "s"} · <strong>${assembly.publicMembers.toLocaleString()}</strong> member${assembly.publicMembers === 1 ? "" : "s"}</span></div>`)
    .join("") || '<div class="assembly-cell"><span class="assembly-name">No assemblies</span></div>';

  const kindCounts = new Map();
  for (const type of pkg.types) {
    const kind = typeKind(type.kind);
    kindCounts.set(kind, (kindCounts.get(kind) || 0) + 1);
  }
  const kindPlural = { class: "classes", struct: "structs", interface: "interfaces", enum: "enums", delegate: "delegates" };
  const kinds = KIND_ORDER
    .filter(kind => kindCounts.has(kind))
    .map(kind => `<button class="count-cell as-button" data-kind-jump="${kind}"><strong>${kindCounts.get(kind)}</strong><span>${kindPlural[kind] || kind}</span></button>`)
    .join("");

  const nsCounts = new Map();
  for (const type of pkg.types) {
    const ns = type.namespace || "global";
    nsCounts.set(ns, (nsCounts.get(ns) || 0) + 1);
  }
  const namespaces = [...nsCounts.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, 12)
    .map(([ns, count]) => `<button class="type-chip" data-namespace-jump="${escapeHtml(ns)}"><span class="ns-count">${count}</span>${escapeHtml(ns)}</button>`)
    .join("");
  const nsOverflow = nsCounts.size > 12 ? `<span class="ns-overflow">+${nsCounts.size - 12} more</span>` : "";

  return `
    <section class="document-section">
      <div class="section-title"><h2>Target frameworks</h2><span>${pkg.frameworks.length} · active highlighted</span></div>
      <div class="type-chip-list">${frameworks}</div>
    </section>
    <section class="document-section">
      <div class="section-title"><h2>Assemblies</h2><span>${(pkg.assemblies || []).length} in lib/${escapeHtml(pkg.activeFramework)}</span></div>
      <div class="composition-grid assembly-grid">${assemblies}</div>
    </section>
    <section class="document-section">
      <div class="section-title"><h2>Types by kind</h2><span>${pkg.totalTypes} public types — click to browse</span></div>
      <div class="composition-grid">${kinds}</div>
    </section>
    <section class="document-section">
      <div class="section-title"><h2>Namespaces</h2><span>${nsCounts.size} — click to filter</span></div>
      <div class="type-chip-list">${namespaces}${nsOverflow}</div>
    </section>`;
}

function renderLens(item) {
  if (state.atPackageRoot) return renderPackageView();
  const member = selectedMember(item);
  if (state.lens === "api" && member) return renderMember(item, member);
  if (state.lens === "source") {
    return `
      ${typeHeading(item)}
      ${renderTypeSource(item)}`;
  }
  if (state.lens === "metadata") {
    return `${typeHeading(item)}${renderTypeMetadata(item)}`;
  }
  const groups = memberGroups(item);
  const kindOrder = ["constructor", "method", "property", "field", "event"];
  const kindLabels = { constructor: "constructors", method: "methods", property: "properties", field: "fields", event: "events" };
  const presentKinds = kindOrder.filter(kind => groups.some(group => group.kind === kind));
  if (state.memberKindFilter !== "all" && !presentKinds.includes(state.memberKindFilter)) state.memberKindFilter = "all";
  const activeKind = state.memberKindFilter;
  const visibleGroups = activeKind === "all" ? groups : groups.filter(group => group.kind === activeKind);
  const filterButtons = [`<button class="member-kind ${activeKind === "all" ? "active" : ""}" data-kind="all">all</button>`]
    .concat(presentKinds.map(kind =>
      `<button class="member-kind ${activeKind === kind ? "active" : ""}" data-kind="${kind}">${kindLabels[kind]}</button>`))
    .join("");
  return `
    ${typeHeading(item)}
    <section class="document-section">
      <div class="section-title"><h2>Public API</h2><span>${groups.length} member groups · ${item.members} overloads</span></div>
      <div class="member-filter">${filterButtons}</div>
      <div class="api-list">${visibleGroups.map(group => `
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
    ["source", "Source"],
    ["annotated", "Annotated source"]
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
  } else if (state.memberSection === "annotated") {
    content = state.memberAnnotatedLoading
      ? `<section class="document-section source-progress"><span class="loader"></span><h2>Annotating member…</h2><p>Raising the selected overload to C# and interleaving the raw IL with hidden-fact comments.</p></section>`
      : state.memberAnnotated
        ? `<section class="document-section source-result annotated-result">
            <div class="source-provenance"><strong>Annotated source</strong><span>${escapeHtml(state.memberAnnotated.provenance)}</span><button id="copy-annotated" type="button">copy</button></div>
            <pre class="language-csharp"><code class="language-csharp">${highlightCSharp(state.memberAnnotated.text)}</code></pre>
          </section>`
        : `<section class="document-section empty-member-section"><h2>Annotated source query failed</h2><p>${escapeHtml(state.memberAnnotatedError || "No annotated source result was returned.")}</p></section>`;
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

function typeMetadataSignature(item) {
  return `${state.package.id}@${state.package.version}/${state.package.activeFramework}/${item.assembly}/${item.id}`;
}

const COMPOSITION_KINDS = [
  ["methods", "Methods"],
  ["properties", "Properties"],
  ["fields", "Fields"],
  ["events", "Events"],
  ["constructors", "Constructors"],
  ["operators", "Operators"],
  ["extensionMethods", "Extension methods"],
  ["explicitInterfaceImplementations", "Explicit impls"]
];

const COMPOSITION_FLAGS = [
  ["static", "static"],
  ["unsafe", "unsafe"],
  ["async", "async"],
  ["virtual", "virtual"],
  ["abstract", "abstract"],
  ["override", "override"],
  ["extension", "extension"],
  ["obsolete", "obsolete"]
];

function renderCompositionGrid(composition) {
  const kinds = COMPOSITION_KINDS
    .filter(([key]) => composition[key] > 0)
    .map(([key, label]) => `<div class="count-cell"><strong>${composition[key]}</strong><span>${label}</span></div>`)
    .join("");
  const flags = COMPOSITION_FLAGS
    .filter(([key]) => composition[key] > 0)
    .map(([key, label]) => `<span class="count-flag flag-${key}">${composition[key]} ${label}</span>`)
    .join("");
  return `
    <div class="composition-grid">${kinds || '<div class="count-cell"><strong>0</strong><span>members</span></div>'}</div>
    ${flags ? `<div class="composition-flags">${flags}</div>` : ""}`;
}

function renderTypeMetadata(item) {
  const current = typeMetadataSignature(item);
  const fresh = state.typeMetadataKey === current;
  if (state.typeMetadataLoading && fresh) {
    return `<section class="document-section source-progress"><span class="loader"></span><h2>Projecting type metadata…</h2><p>Composing type facts through the shared dotnet-inspect projection.</p></section>`;
  }
  if (fresh && state.typeMetadataError) {
    return `<section class="document-section empty-document"><span class="large-glyph">⌁</span><h2>Metadata projection failed</h2><p>${escapeHtml(state.typeMetadataError)}</p></section>`;
  }
  const meta = fresh ? state.typeMetadata : null;
  if (!meta) {
    return `<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const shape = [
    ["Kind", [...(meta.modifiers || []), meta.kind].join(" ")],
    ["Accessibility", meta.accessibility || "public"],
    ["Namespace", meta.namespace || "global"],
    ["Assembly", meta.assembly || item.assembly]
  ];
  if (meta.baseType) shape.push(["Base type", meta.baseType]);
  if (meta.enumUnderlyingType) shape.push(["Enum underlying", meta.enumUnderlyingType]);
  if (meta.typeParameters?.length) {
    shape.push(["Type parameters", meta.typeParameters
      .map(parameter => `${parameter.variance ? parameter.variance + " " : ""}${parameter.name}${parameter.constraints?.length ? ` : ${parameter.constraints.join(", ")}` : ""}`)
      .join(" · ")]);
  }

  const interfaces = (meta.interfaces || []).length
    ? `<section class="document-section">
        <div class="section-title"><h2>Implements</h2><span>${meta.interfaces.length} interface${meta.interfaces.length === 1 ? "" : "s"}</span></div>
        <div class="type-chip-list">${meta.interfaces.map(name => `<button class="type-chip" data-graph-type="${escapeHtml(name)}" title="${escapeHtml(name)}">${escapeHtml(shortTypeName(name))}</button>`).join("")}</div>
      </section>`
    : "";

  const derived = (meta.derivedTypes || []).length
    ? `<section class="document-section">
        <div class="section-title"><h2>Known derived types</h2><span>${meta.derivedTypes.length} in ${escapeHtml(meta.assembly || item.assembly)}</span></div>
        <div class="type-chip-list">${meta.derivedTypes.map(name => `<button class="type-chip" data-graph-type="${escapeHtml(name)}" title="${escapeHtml(name)}">${escapeHtml(shortTypeName(name))}</button>`).join("")}</div>
      </section>`
    : "";

  const attributes = (meta.attributes || []).length
    ? `<section class="document-section">
        <div class="section-title"><h2>Custom attributes</h2><span>${meta.attributes.length}</span></div>
        <div class="type-chip-list">${meta.attributes.map(name => `<code class="attr-chip">[${escapeHtml(name)}]</code>`).join("")}</div>
      </section>`
    : "";

  const composition = meta.composition
    ? `<section class="document-section">
        <div class="section-title"><h2>Composition</h2><span>${meta.composition.total} member${meta.composition.total === 1 ? "" : "s"}</span></div>
        ${renderCompositionGrid(meta.composition)}
      </section>`
    : "";

  const graph = (meta.graphNodes || []).length > 1
    ? `<section class="document-section call-graph-section">
        <div class="section-title"><h2>Type relationships</h2><span>base · interfaces · derived — click a node to open</span></div>
        <div id="type-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph…</p></div>
      </section>`
    : "";

  const failures = (meta.inspectionFailures || []).length
    ? `<section class="document-section metadata-warning"><strong>⚠ Relationship view may be incomplete</strong><ul>${meta.inspectionFailures.map(entry => `<li><code>${escapeHtml(entry)}</code></li>`).join("")}</ul></section>`
    : "";

  return `
    <section class="document-section">
      <div class="section-title"><h2>Type shape</h2><span>ECMA-335 metadata</span></div>
      ${factRows(shape)}
    </section>
    ${composition}
    ${interfaces}
    ${derived}
    ${attributes}
    ${graph}
    ${failures}`;
}

function shortTypeName(fullName) {
  const generic = fullName.indexOf("<");
  const head = generic < 0 ? fullName : fullName.slice(0, generic);
  const tail = generic < 0 ? "" : fullName.slice(generic);
  const dot = head.lastIndexOf(".");
  return (dot < 0 ? head : head.slice(dot + 1)) + tail;
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
    state.kindFilter = "";
    render();
  }));
  document.querySelector("#package-root")?.addEventListener("click", () => {
    state.atPackageRoot = true;
    render();
  });
  document.querySelectorAll("[data-package-lens]").forEach(button => button.addEventListener("click", () => {
    state.packageLens = button.dataset.packageLens;
    render();
  }));
  document.querySelectorAll("[data-framework-chip]").forEach(button => button.addEventListener("click", () => {
    loadPackage(state.package.id, state.package.version, button.dataset.frameworkChip);
  }));
  document.querySelectorAll("[data-dep-framework]").forEach(button => button.addEventListener("click", () => {
    state.dependenciesFramework = button.dataset.depFramework;
    render();
  }));
  document.querySelectorAll("[data-dep-open]").forEach(button => button.addEventListener("click", () => {
    switchToPackageForDependencies(button.dataset.depOpen);
  }));
  document.querySelectorAll("[data-kind-jump]").forEach(button => button.addEventListener("click", () => {
    state.atPackageRoot = false;
    state.kindFilter = button.dataset.kindJump;
    state.namespaceFilter = "";
    state.typeFilter = "";
    state.selectedMemberKey = "";
    state.typeCursor = 0;
    const first = filteredTypes()[0];
    if (first) state.selectedTypeId = first.id;
    render();
  }));
  document.querySelectorAll("[data-namespace-jump]").forEach(button => button.addEventListener("click", () => {
    state.atPackageRoot = false;
    state.namespaceFilter = button.dataset.namespaceJump;
    state.kindFilter = "";
    state.typeFilter = "";
    state.selectedMemberKey = "";
    state.typeCursor = 0;
    const first = filteredTypes()[0];
    if (first) state.selectedTypeId = first.id;
    render();
  }));
  document.querySelectorAll("[data-lens]").forEach(button => button.addEventListener("click", () => {
    state.lens = button.dataset.lens;
    state.selectedMemberKey = "";
    render();
  }));
  document.querySelectorAll("[data-type]").forEach(button => button.addEventListener("click", () => {
    state.atPackageRoot = false;
    state.selectedTypeId = button.dataset.type;
    state.selectedMemberKey = "";
    state.memberKindFilter = "all";
    state.typeCursor = filteredTypes().findIndex(item => item.id === state.selectedTypeId);
    render();
  }));
  document.querySelectorAll("[data-graph-type]").forEach(button => button.addEventListener("click", () => {
    navigateToTypeByName(button.dataset.graphType);
  }));
  document.querySelectorAll("[data-perf-token]").forEach(button => button.addEventListener("click", () => {
    drillToPerfMember(button.dataset.perfToken);
  }));
  document.querySelectorAll(".member-filter .member-kind").forEach(button => button.addEventListener("click", () => {
    state.memberKindFilter = button.dataset.kind;
    render();
  }));
  document.querySelectorAll("[data-member]").forEach(button => button.addEventListener("click", () => {
    openMemberGroup(button.dataset.member);
  }));
  document.querySelectorAll("[data-overload]").forEach(button => button.addEventListener("click", () => {
    openOverload(Number(button.dataset.overload));
  }));
  document.querySelectorAll("[data-nav-member]").forEach(button => button.addEventListener("click", () => {
    const group = memberGroups(selectedType()).find(item => item.key === button.dataset.navMember);
    if (group) selectMemberNavEntry({ kind: "member", group }, false);
  }));
  document.querySelectorAll("[data-nav-overload]").forEach(button => button.addEventListener("click", () => {
    const group = selectedMember(selectedType());
    if (group) selectMemberNavEntry({ kind: "overload", group, index: Number(button.dataset.navOverload) }, false);
  }));
  document.querySelector("#nav-to-types")?.addEventListener("click", () => {
    drillOut();
  });
  document.querySelectorAll("[data-member-section]").forEach(button => button.addEventListener("click", () => {
    state.memberSection = button.dataset.memberSection;
    if (state.memberSection === "source") loadSelectedMemberSource();
    else if (state.memberSection === "annotated") loadSelectedMemberAnnotatedSource();
    else if (state.memberSection === "call-graph") loadSelectedMemberCallGraph();
    else if (state.memberSection === "facts") loadSelectedMemberFacts();
    else if (state.memberSection === "overview") loadSelectedMemberDocumentation();
    else render();
  }));
  document.querySelector("#member-back")?.addEventListener("click", () => {
    drillOut();
  });
  document.querySelector("#copy-name")?.addEventListener("click", async () => {
    const type = selectedType();
    if (!type) return;
    const typeName = `${type.namespace ? `${type.namespace}.` : ""}${type.name}`;
    const member = selectedMember(type);
    const fullName = member ? `${typeName}.${member.name}` : typeName;
    await copyText(fullName, "name copied");
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
  document.querySelector("#copy-annotated")?.addEventListener("click", async () => {
    if (state.memberAnnotated) await copyText(state.memberAnnotated.text, "annotated source copied");
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
  document.querySelectorAll("[data-kind-filter]").forEach(button => button.addEventListener("click", () => {
    state.kindFilter = button.dataset.kindFilter;
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
  filter?.addEventListener("input", event => {
    state.typeFilter = event.target.value;
    state.typeCursor = 0;
    const first = filteredTypes()[0];
    if (first) state.selectedTypeId = first.id;
    state.selectedMemberKey = "";
    render();
    focusFilter();
  });
  filter?.addEventListener("keydown", event => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      document.querySelector("#type-list").focus();
    } else if (event.key === "Escape") {
      state.typeFilter = "";
      render();
    }
  });
  document.querySelector("#type-list")?.addEventListener("keydown", handleTypeKeys);
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
  document.querySelector("#graph-source-backdrop")?.addEventListener("mousedown", event => {
    if (event.target.id === "graph-source-backdrop") closeGraphSource();
  });
  document.querySelector("#graph-source-close")?.addEventListener("click", closeGraphSource);
  document.querySelector("#taste-btn")?.addEventListener("click", event => {
    event.stopPropagation();
    state.tasteOpen = !state.tasteOpen;
    render();
  });
  document.querySelectorAll("#taste-popover [data-taste]").forEach(checkbox =>
    checkbox.addEventListener("change", () => toggleTaste(checkbox.dataset.taste)));
  document.querySelector("#taste-clear")?.addEventListener("click", clearTaste);
  document.querySelector("#clear-filter")?.addEventListener("click", () => {
    state.typeFilter = "";
    state.namespaceFilter = "";
    state.kindFilter = "";
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
  document.querySelector("#help").addEventListener("click", () => showToast("⌘K command · ⌘P / type to find a type · ⌘F filter · 1—5 lenses · ↑↓ types · Alt+←/→ back/forward · graph: wheel zoom, click node to open, +/− zoom, 0 fit, arrows pan"));
}

function toggleTheme() {
  state.theme = state.theme === "dark" ? "light" : "dark";
  localStorage.setItem("inspect-theme", state.theme);
  document.documentElement.dataset.theme = state.theme;
  render();
  if (state.memberCallGraph) renderMermaidCallGraph();
  const depGraph = document.querySelector("#dependency-graph-diagram");
  if (depGraph) { depGraph.dataset.graphDef = ""; renderDependencyGraph(); }
}

function handleTypeKeys(event) {
  if (navMode() === "member") {
    if (event.key === "ArrowDown" || event.key === "j") {
      event.preventDefault();
      stepMemberNav(1, true);
    } else if (event.key === "ArrowUp" || event.key === "k") {
      event.preventDefault();
      stepMemberNav(-1, true);
    } else if (event.key === "ArrowLeft") {
      event.preventDefault();
      stepHorizontal(-1);
    } else if (event.key === "ArrowRight") {
      event.preventDefault();
      stepHorizontal(1);
    }
    return;
  }
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
  state.memberKindFilter = "all";
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
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
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
    state.kindFilter = "";
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

function buildStateUrl(base = location.href) {
  const url = new URL(base);
  if (!state.package) return url;
  // Keep the deep link entirely in the query string on the root path. The end
  // deployment is a pure static file server with no SPA fallback, so a refresh or
  // shared link must resolve to a real file (index.html at "/"); path segments
  // like /packages/{id} would 404. The client restores selection from the query.
  url.pathname = "/";
  const params = new URLSearchParams();
  params.set("package", state.package.id);
  params.set("version", state.package.version);
  if (state.package.activeFramework) params.set("framework", state.package.activeFramework);
  if (state.selectedTypeId) params.set("type", state.selectedTypeId);
  if (state.selectedMemberKey) params.set("member", state.selectedMemberKey);
  if (state.selectedOverloadIndex != null) params.set("overload", String(state.selectedOverloadIndex));
  if (state.memberSection && state.memberSection !== "overview") params.set("section", state.memberSection);
  url.search = params.toString();
  url.hash = state.atPackageRoot
    ? (state.packageLens && state.packageLens !== "overview" ? `pkg:${state.packageLens}` : "pkg")
    : (state.lens && state.lens !== "api" ? state.lens : "");
  return url;
}

// Rewrite the address bar to reflect the current selection so a refresh restores it and
// the URL is always shareable. replaceState (not pushState) keeps the app's own
// back/forward buttons authoritative and avoids flooding browser history on every render.
function syncUrl() {
  if (!state.package || state.loading) return;
  try {
    history.replaceState(null, "", buildStateUrl().toString());
  } catch {
    // Ignore environments that disallow history rewriting (e.g. sandboxed frames).
  }
}

// Apply a parsed URL selection onto the currently loaded package, validating that the
// type/member/overload/section still exist.
function applyDeepLink(deep) {
  const pkg = state.package;
  if (!pkg) return;
  const restoreType = deep?.type && pkg.types.some(item => item.id === deep.type);
  state.selectedTypeId = restoreType ? deep.type : (pkg.types[0]?.id || "");
  state.selectedMemberKey = "";
  state.selectedOverloadIndex = null;
  state.memberSection = "overview";
  if (restoreType && deep) {
    const type = pkg.types.find(item => item.id === deep.type);
    const groups = memberGroups(type);
    const group = deep.member ? groups.find(item => item.key === deep.member) : null;
    if (group) {
      state.selectedMemberKey = deep.member;
      const overloadIndex = Number(deep.overload);
      if (deep.overload != null && deep.overload !== ""
        && Number.isInteger(overloadIndex) && overloadIndex >= 0
        && overloadIndex < group.overloads.length) {
        state.selectedOverloadIndex = overloadIndex;
      }
      if (deep.section && memberSections.includes(deep.section)) state.memberSection = deep.section;
    }
  }
  state.typeCursor = Math.max(0, filteredTypes().findIndex(item => item.id === state.selectedTypeId));
}

// Kick off the async data load implied by the current lens/section so a restored or
// history-navigated view fills in its content.
function loadSelectionData() {
  if (state.atPackageRoot) return;
  if (state.lens === "source") {
    loadSelectedTypeSource();
    return;
  }
  if (state.lens === "metadata") {
    loadSelectedTypeMetadata();
    return;
  }
  if (state.lens !== "api" || !state.selectedMemberKey) return;
  const member = selectedMember(selectedType());
  if (!member) return;
  if (member.overloads.length > 1 && state.selectedOverloadIndex == null) return;
  if (state.memberSection === "source") loadSelectedMemberSource();
  else if (state.memberSection === "annotated") loadSelectedMemberAnnotatedSource();
  else if (state.memberSection === "call-graph") loadSelectedMemberCallGraph();
  else if (state.memberSection === "facts") loadSelectedMemberFacts();
  else loadSelectedMemberDocumentation();
}

async function share() {
  await navigator.clipboard?.writeText(buildStateUrl().toString());
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
      signature: overload.signature,
      styleOptionsJson: JSON.stringify(state.taste)
    });
  } catch (error) {
    state.memberSourceError = String(error?.message || error);
  } finally {
    state.memberSourceLoading = false;
    render();
  }
}

async function loadSelectedMemberAnnotatedSource() {
  if (state.memberAnnotated) {
    render();
    return;
  }
  const type = selectedType();
  const member = selectedMember(type);
  const overload = member?.overloads[state.selectedOverloadIndex ?? 0];
  if (!type || !member || !overload) {
    state.memberAnnotatedError = "Select a concrete overload before opening Annotated source.";
    render();
    return;
  }

  state.memberAnnotatedLoading = true;
  state.memberAnnotatedError = "";
  render();
  try {
    state.memberAnnotated = await inspectMemberAnnotatedSource({
      packageId: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework,
      assembly: type.assembly,
      type: type.id,
      member: overload.name,
      signature: overload.signature,
      styleOptionsJson: JSON.stringify(state.taste)
    });
  } catch (error) {
    state.memberAnnotatedError = String(error?.message || error);
  } finally {
    state.memberAnnotatedLoading = false;
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
      type: type.id,
      styleOptionsJson: JSON.stringify(state.taste)
    });
    if (state.typeSourceKey === signature) state.typeSource = result;
  } catch (error) {
    if (state.typeSourceKey === signature) state.typeSourceError = String(error?.message || error);
  } finally {
    if (state.typeSourceKey === signature) state.typeSourceLoading = false;
    render();
  }
}

async function loadSelectedTypeMetadata() {
  const type = selectedType();
  if (!type) {
    render();
    return;
  }
  const signature = typeMetadataSignature(type);
  if (state.typeMetadataKey === signature && (state.typeMetadata || state.typeMetadataError)) {
    render();
    return;
  }
  state.typeMetadataKey = signature;
  state.typeMetadata = null;
  state.typeMetadataError = "";
  state.typeMetadataLoading = true;
  render();
  try {
    const result = await inspectTypeProjection({
      packageId: state.package.id,
      version: state.package.version,
      framework: state.package.activeFramework,
      assembly: type.assembly,
      type: type.id
    });
    if (state.typeMetadataKey === signature) state.typeMetadata = result;
  } catch (error) {
    if (state.typeMetadataKey === signature) state.typeMetadataError = String(error?.message || error);
  } finally {
    if (state.typeMetadataKey === signature) state.typeMetadataLoading = false;
    render();
    if (state.typeMetadata?.graphNodes?.length > 1) renderTypeGraph();
  }
}

// Projects the neutral type-relationship node/edge model into a Mermaid flowchart so it
// renders with the same pan/zoom/click affordances as the call graph.
function buildTypeGraphMermaid(meta) {
  const nodes = meta.graphNodes || [];
  const edges = meta.graphEdges || [];
  if (nodes.length < 2) return null;
  const idOf = new Map();
  nodes.forEach((node, index) => idOf.set(node.id, `t${index}`));
  const lines = ["flowchart TD"];
  for (const node of nodes) {
    const label = shortTypeName(node.displayName).replace(/"/g, "&quot;");
    lines.push(`  ${idOf.get(node.id)}["${label}"]:::${node.role}`);
  }
  for (const edge of edges) {
    const from = idOf.get(edge.fromId);
    const to = idOf.get(edge.toId);
    if (from && to) lines.push(`  ${from} --> ${to}`);
  }
  lines.push("classDef self fill:var(--accent-soft),stroke:var(--accent),color:var(--text),stroke-width:2px;");
  lines.push("classDef base fill:var(--panel-active),stroke:var(--line-strong),color:var(--text);");
  lines.push("classDef interface fill:transparent,stroke:var(--line-strong),color:var(--dim);");
  lines.push("classDef derived fill:var(--panel),stroke:var(--line),color:var(--text);");
  return lines.join("\n");
}

async function renderTypeGraph() {
  const container = document.querySelector("#type-graph-diagram");
  if (!container || container.querySelector(".graph-viewport")) return;
  const meta = state.typeMetadata;
  const definition = meta ? buildTypeGraphMermaid(meta) : null;
  if (!definition) return;
  const fullNameOf = new Map((meta.graphNodes || []).map(node => [shortTypeName(node.displayName), node.id]));
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
    const id = `type-graph-${Date.now().toString(36)}`;
    const rootStyle = getComputedStyle(document.documentElement);
    const resolved = definition.replace(
      /var\((--[\w-]+)\)/g,
      (whole, name) => rootStyle.getPropertyValue(name).trim() || whole
    );
    const { svg } = await mermaid.render(id, resolved);
    if (document.querySelector("#type-graph-diagram") !== container) return;
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
    viewport.querySelectorAll("g.node").forEach(node => {
      const label = (node.textContent || "").replace(/\s+/g, " ").trim();
      const fullName = fullNameOf.get(label);
      if (!fullName) return;
      const target = state.package.types.find(candidate => candidate.id === fullName);
      if (!target) return;
      node.classList.add("nav-node");
      node.style.cursor = "pointer";
      node.addEventListener("click", () => navigateToTypeByName(fullName));
    });
  } catch (error) {
    if (document.querySelector("#type-graph-diagram") === container) {
      container.innerHTML = `<div class="graph-render-error"><strong>Diagram rendering failed</strong><p>${escapeHtml(String(error?.message || error))}</p></div>`;
    }
  }
}

function navigateToTypeByName(fullName) {
  const target = state.package.types.find(candidate => candidate.id === fullName);
  if (!target) return;
  state.selectedTypeId = target.id;
  state.selectedMemberKey = "";
  state.memberKindFilter = "all";
  state.typeCursor = filteredTypes().findIndex(candidate => candidate.id === target.id);
  render();
}

// Projects the current package, its direct dependencies for the selected framework, and any
// open packages that declare a dependency on it into a call-graph-style Mermaid flowchart:
// caller (open) packages flow down into the current package, which flows down into its deps.
function buildDependencyGraphMermaid(selectedTfm) {
  const centerId = state.package.id;
  const centerKey = centerId.toLowerCase();
  const openById = new Map(state.packages.map(item => [item.id.toLowerCase(), item]));

  const nodes = [];
  const nodeInfoByLabel = new Map();
  const seen = new Set();
  const addNode = (id, role) => {
    const key = id.toLowerCase();
    if (seen.has(key)) return;
    seen.add(key);
    const open = openById.get(key);
    const kind = key === centerKey ? "self" : (open ? "open" : "external");
    nodes.push({ id, kind });
    nodeInfoByLabel.set(id, { id, kind });
  };

  addNode(centerId, "self");

  const centerGroups = state.workspaceDependencies[`${centerKey}@${state.package.version.toLowerCase()}`]
    || state.packageDependencies?.dependencyGroups
    || [];
  const centerGroup = centerGroups.find(group => group.framework === selectedTfm)
    || centerGroups.find(group => group.isActive)
    || centerGroups[0];
  const downstream = (centerGroup?.dependencies || []).map(dependency => dependency.id);
  for (const id of downstream) addNode(id);

  const upstream = [];
  for (const item of state.packages) {
    if (item.id.toLowerCase() === centerKey) continue;
    const groups = state.workspaceDependencies[`${item.id.toLowerCase()}@${item.version.toLowerCase()}`] || [];
    const group = groups.find(candidate => candidate.framework === selectedTfm)
      || groups.find(candidate => candidate.isActive)
      || groups[0];
    const dependsOnCenter = (group?.dependencies || []).some(dependency => dependency.id.toLowerCase() === centerKey);
    if (dependsOnCenter) {
      addNode(item.id);
      upstream.push(item.id);
    }
  }

  if (!downstream.length && !upstream.length) return null;

  const idOf = new Map();
  nodes.forEach((node, index) => idOf.set(node.id.toLowerCase(), `d${index}`));
  const lines = ["flowchart TD"];
  for (const node of nodes) {
    const label = node.id.replace(/"/g, "&quot;");
    lines.push(`  ${idOf.get(node.id.toLowerCase())}["${label}"]:::${node.kind}`);
  }
  for (const id of upstream) {
    lines.push(`  ${idOf.get(id.toLowerCase())} --> ${idOf.get(centerKey)}`);
  }
  for (const id of downstream) {
    lines.push(`  ${idOf.get(centerKey)} --> ${idOf.get(id.toLowerCase())}`);
  }
  lines.push("classDef self fill:var(--accent-soft),stroke:var(--accent),color:var(--text),stroke-width:2px;");
  lines.push("classDef open fill:var(--panel-active),stroke:var(--blue),color:var(--text);");
  lines.push("classDef external fill:transparent,stroke:var(--line-strong),color:var(--dim);");
  return { definition: lines.join("\n"), nodeInfoByLabel };
}

async function renderDependencyGraph() {
  const container = document.querySelector("#dependency-graph-diagram");
  if (!container) return;
  const groups = state.packageDependencies?.dependencyGroups || [];
  if (!groups.length) return;
  const selectedTfm = resolveDependenciesFramework(groups);
  const built = buildDependencyGraphMermaid(selectedTfm);
  if (!built) {
    container.dataset.graphDef = "";
    container.innerHTML = '<p class="graph-empty">No connected packages for this framework. Open a package that depends on this one to see caller edges.</p>';
    return;
  }
  if (container.dataset.graphDef === built.definition && container.querySelector(".graph-viewport")) return;
  container.dataset.graphDef = built.definition;
  try {
    mermaidModule ??= import("https://cdn.jsdelivr.net/npm/mermaid@11.15.0/dist/mermaid.esm.min.mjs");
    const { default: mermaid } = await mermaidModule;
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: "strict",
      theme: state.theme === "light" ? "default" : "dark",
      themeVariables: { fontSize: "16px" },
      flowchart: { htmlLabels: false, curve: "basis" }
    });
    const id = `dep-graph-${Date.now().toString(36)}`;
    const rootStyle = getComputedStyle(document.documentElement);
    const resolved = built.definition.replace(
      /var\((--[\w-]+)\)/g,
      (whole, name) => rootStyle.getPropertyValue(name).trim() || whole
    );
    const { svg } = await mermaid.render(id, resolved);
    if (document.querySelector("#dependency-graph-diagram") !== container) return;
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
    viewport.querySelectorAll("g.node").forEach(node => {
      const label = (node.textContent || "").replace(/\s+/g, " ").trim();
      const info = built.nodeInfoByLabel.get(label);
      if (!info || info.kind !== "open") return;
      node.classList.add("nav-node");
      node.style.cursor = "pointer";
      node.addEventListener("click", () => switchToPackageForDependencies(info.id));
    });
  } catch (error) {
    if (document.querySelector("#dependency-graph-diagram") === container) {
      container.dataset.graphDef = "";
      container.innerHTML = `<div class="graph-render-error"><strong>Diagram rendering failed</strong><p>${escapeHtml(String(error?.message || error))}</p></div>`;
    }
  }
}

function switchToPackageForDependencies(packageId) {
  const target = state.packages.find(item => item.id.toLowerCase() === packageId.toLowerCase());
  if (!target) return;
  state.package = target;
  state.atPackageRoot = true;
  state.packageLens = "dependencies";
  state.dependenciesFramework = "";
  state.selectedTypeId = target.types[0]?.id || "";
  state.selectedMemberKey = "";
  state.selectedOverloadIndex = null;
  render();
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
      attachGraphPanZoom(container, viewport, true);
    }
  } catch (error) {
    if (document.querySelector("#call-graph-diagram") === container) {
      container.innerHTML = `<div class="graph-render-error"><strong>Diagram rendering failed</strong><p>${escapeHtml(String(error?.message || error))}</p></div>`;
    }
  }
}

function attachGraphPanZoom(container, viewport, bindCallGraphNodes = false) {
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

  if (bindCallGraphNodes) {
    svg.querySelectorAll("g.node").forEach(node => {
      const label = (node.textContent || "").replace(/\s+/g, " ").trim();
      const target = resolveNodeLabel(label);
      const source = target ? null : resolveNodeForSource(label, node.classList.contains("differentAssembly"));
      if (!target && !source) return;
      node.classList.add("nav-node");
      node.style.cursor = "pointer";
      node.addEventListener("click", () => {
        if (moved) return;
        if (target) navigateToMember(target.pkg, target.type, target.group);
        else openGraphSource(source.request, source.title);
      });
    });
  }

  fit();
}

function stripArity(name) {
  const tick = name.indexOf("`");
  return tick < 0 ? name : name.slice(0, tick);
}

// The compact call-graph label strips generic arity ("JsonTypeInfo" for JsonTypeInfo`1),
// which also collides with a same-named non-generic type. Match on exact and
// arity-stripped forms of both the simple name and the full id so a generic node can
// still find its declaring type.
function typeMatchesName(type, typeName) {
  return type.name === typeName
    || type.id === typeName
    || type.id.endsWith("." + typeName)
    || stripArity(type.name) === typeName
    || stripArity(type.id) === typeName
    || stripArity(type.id).endsWith("." + typeName);
}

function resolveNodeLabel(label) {
  const dot = label.lastIndexOf(".");
  if (dot < 0) return null;
  let typeName = label.slice(0, dot);
  const memberName = label.slice(dot + 1);
  if (typeName.endsWith(".")) typeName = typeName.slice(0, -1);
  if (!typeName) return null;
  const candidates = [state.package, ...state.packages.filter(item => item !== state.package)];
  // Prefer the candidate type that actually declares the member: an arity-stripped name
  // can match both a generic type and a same-named non-generic type, and only one owns
  // the member.
  for (const pkg of candidates) {
    if (!pkg?.types) continue;
    for (const type of pkg.types.filter(item => typeMatchesName(item, typeName))) {
      const group = findMemberGroup(memberGroups(type), memberName);
      if (group) return { pkg, type, group };
    }
  }
  return null;
}

function findMemberGroup(groups, memberName) {
  let group = groups.find(item => item.name === memberName);
  if (group) return group;

  const accessor = memberName.match(/^(get|set|add|remove)_(.+)$/);
  if (accessor) {
    const backing = accessor[2];
    const kind = accessor[1] === "get" || accessor[1] === "set" ? "property" : "event";
    group = groups.find(item => item.name === backing && item.kind === kind)
      ?? groups.find(item => item.name === backing);
    if (group) return group;
    if ((backing === "Item" || backing === "Chars")) {
      group = groups.find(item => item.kind === "property" && (item.name === "Item" || item.name === "this[]"));
      if (group) return group;
    }
  }

  if (memberName === "ctor" || memberName === ".ctor" || memberName === "#ctor") {
    group = groups.find(item => item.kind === "constructor");
    if (group) return group;
  }
  return null;
}

function resolveNodeForSource(label, external = false) {
  const dot = label.lastIndexOf(".");
  if (dot < 0) return null;
  let typeName = label.slice(0, dot);
  const memberName = label.slice(dot + 1);
  if (typeName.endsWith(".")) typeName = typeName.slice(0, -1);
  if (!typeName || !memberName) return null;
  // Accessors and public members already navigate through resolveNodeLabel; compiler
  // generated helpers (e.g. <DeepEquals>g__...) are not on the metadata surface. The
  // decompile fallback targets ordinary non-public methods of loaded assemblies.
  if (/^(get|set|add|remove)_/.test(memberName)) return null;
  if (memberName.includes("<") || memberName.includes(">")) return null;

  // A declaring type that is a public loaded type routes to its own package/assembly.
  // The engine resolves the exact declaring type (disambiguating generic arity
  // collisions by which type declares the member), so pass the arity-stripped simple
  // name it can match on.
  const candidates = [state.package, ...state.packages.filter(item => item !== state.package)];
  for (const pkg of candidates) {
    if (!pkg?.types) continue;
    const type = pkg.types.find(item => typeMatchesName(item, typeName));
    if (!type) continue;
    return {
      title: `${stripArity(type.name)}.${memberName}`,
      request: {
        packageId: pkg.id,
        version: pkg.version,
        framework: pkg.activeFramework,
        assembly: type.assembly,
        type: stripArity(typeName),
        member: memberName
      }
    };
  }

  // A non-public declaring type is absent from the public type list; assume the graph
  // target's package and assembly, where internal implementation types resolve. Nodes the
  // graph marks as belonging to a different assembly (BCL/runtime) are not in the loaded
  // workspace, so leave them inert rather than offering a click that cannot resolve.
  const current = selectedType();
  if (!external && current && state.package) {
    return {
      title: `${typeName}.${memberName}`,
      request: {
        packageId: state.package.id,
        version: state.package.version,
        framework: state.package.activeFramework,
        assembly: current.assembly,
        type: typeName,
        member: memberName
      }
    };
  }
  return null;
}

async function openGraphSource(request, title) {
  state.graphSourceOpen = true;
  state.graphSourceTitle = title;
  state.graphSourceRequest = { request, title };
  state.graphSource = null;
  state.graphSourceError = "";
  state.graphSourceLoading = true;
  render();
  try {
    state.graphSource = await inspectTypeMemberSource({ ...request, styleOptionsJson: JSON.stringify(state.taste) });
  } catch (error) {
    state.graphSourceError = String(error?.message || error);
  } finally {
    state.graphSourceLoading = false;
    render();
  }
}

function closeGraphSource() {
  state.graphSourceOpen = false;
  state.graphSource = null;
  state.graphSourceError = "";
  state.graphSourceLoading = false;
  state.graphSourceRequest = null;
  render();
}

const TASTE_TIERS = [
  ["Formatting", "Formatting"],
  ["Spelling", "Spelling (this.)"],
  ["Lens", "Lenses · byte-divergent"],
  ["Synthesis", "Name synthesis"]
];

function renderTastePopover() {
  const options = state.styleOptions || [];
  const body = options.length
    ? TASTE_TIERS
        .filter(([tier]) => options.some(option => option.tier === tier))
        .map(([tier, label]) => `
          <div class="taste-group">
            <div class="taste-group-title">${escapeHtml(label)}</div>
            ${options.filter(option => option.tier === tier).map(option => `
              <label class="taste-item">
                <input type="checkbox" data-taste="${escapeHtml(option.id)}" ${state.taste.includes(option.id) ? "checked" : ""} />
                <span class="taste-item-text">
                  <span class="taste-item-title">${escapeHtml(option.title)}${option.byteDivergent ? '<em class="taste-badge divergent">byte-divergent</em>' : ""}${option.oracleEndorsed ? '<em class="taste-badge oracle">oracle</em>' : ""}</span>
                  <span class="taste-item-summary">${escapeHtml(option.summary)}</span>
                </span>
              </label>`).join("")}
          </div>`).join("")
    : '<div class="taste-empty">Style catalog unavailable.</div>';
  return `
    <div class="taste-popover" id="taste-popover" role="dialog" aria-label="Decompiler taste">
      <div class="taste-head"><strong>Taste</strong><span>decompiler style knobs</span></div>
      <div class="taste-body">${body}</div>
      <div class="taste-foot">${state.taste.length ? '<button id="taste-clear" type="button">reset to default</button>' : '<span>default · opcode-faithful</span>'}</div>
    </div>`;
}

function invalidateSourceCaches() {
  state.memberSource = null;
  state.memberSourceError = "";
  state.typeSource = null;
  state.typeSourceKey = "";
  state.typeSourceError = "";
}

function reloadVisibleSource() {
  if (state.graphSourceOpen && state.graphSourceRequest) {
    openGraphSource(state.graphSourceRequest.request, state.graphSourceRequest.title);
  }
  if (state.lens === "source") loadSelectedTypeSource();
  else if (state.selectedMemberKey && state.memberSection === "source") loadSelectedMemberSource();
}

function toggleTaste(id) {
  const option = (state.styleOptions || []).find(item => item.id === id);
  if (state.taste.includes(id)) {
    state.taste = state.taste.filter(item => item !== id);
  } else {
    if (option?.conflictGroup) {
      const groupIds = (state.styleOptions || [])
        .filter(item => item.conflictGroup === option.conflictGroup)
        .map(item => item.id);
      state.taste = state.taste.filter(item => !groupIds.includes(item));
    }
    state.taste = [...state.taste, id];
  }
  localStorage.setItem("inspect-taste", JSON.stringify(state.taste));
  invalidateSourceCaches();
  reloadVisibleSource();
  render();
}

function clearTaste() {
  state.taste = [];
  localStorage.setItem("inspect-taste", "[]");
  invalidateSourceCaches();
  reloadVisibleSource();
  render();
}

function renderGraphSource() {
  const body = state.graphSourceLoading
    ? `<div class="graph-source-status">Decompiling ${escapeHtml(state.graphSourceTitle)}…</div>`
    : state.graphSource
      ? `<div class="source-provenance"><strong>${state.graphSource.provider === "original" ? "Original source" : "Decompiled source"}</strong><span>${escapeHtml(state.graphSource.provenance)}</span></div>
         <pre class="language-csharp"><code class="language-csharp">${highlightCSharp(state.graphSource.text)}</code></pre>`
      : `<div class="graph-source-status error">${escapeHtml(state.graphSourceError || "No source was returned.")}</div>`;
  return `
    <div class="graph-source-backdrop" id="graph-source-backdrop">
      <div class="graph-source" role="dialog" aria-modal="true" aria-label="Decompiled member source">
        <div class="graph-source-head">
          <span class="graph-source-title">${escapeHtml(state.graphSourceTitle)}</span>
          <button id="graph-source-close" type="button" aria-label="Close">esc</button>
        </div>
        <div class="graph-source-body">${body}</div>
      </div>
    </div>`;
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
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
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
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
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
    state.typeFilter = "";
    state.namespaceFilter = "";
    state.kindFilter = "";
    state.dependenciesFramework = "";
    const deep = pendingDeepLink;
    pendingDeepLink = null;
    if (deep && (deep.type || deep.member)) {
      applyDeepLink(deep);
    } else {
      state.selectedTypeId = packageModel.types[0]?.id || "";
      state.selectedMemberKey = "";
      state.selectedOverloadIndex = null;
      state.memberSection = "overview";
    }
    state.loading = false;
    render();
    loadSelectionData();
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
    try {
      state.styleOptions = await inspectListStyleOptions();
    } catch {
      state.styleOptions = [];
    }
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
  if (event.key === "Escape" && state.tasteOpen) {
    event.preventDefault();
    state.tasteOpen = false;
    render();
  } else if (event.key === "Escape" && state.graphSourceOpen) {
    event.preventDefault();
    closeGraphSource();
  } else if (event.key === "Escape" && !typing && (navMode() === "member" || !state.atPackageRoot)) {
    event.preventDefault();
    drillOut();
  } else if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
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
  } else if (!typing && !event.metaKey && !event.ctrlKey && /^[1-9]$/.test(event.key)) {
    const set = activeLenses();
    const index = Number(event.key) - 1;
    if (index < set.length) {
      if (state.atPackageRoot) state.packageLens = set[index][0];
      else state.lens = set[index][0];
      render();
    }
  } else if (!typing && !event.defaultPrevented && !event.metaKey && !event.ctrlKey && !event.altKey
      && (event.key === "ArrowUp" || event.key === "ArrowDown")) {
    event.preventDefault();
    stepNav(event.key === "ArrowDown" ? 1 : -1);
  } else if (!typing && !event.defaultPrevented && !event.metaKey && !event.ctrlKey && !event.altKey
      && (event.key === "ArrowLeft" || event.key === "ArrowRight")) {
    event.preventDefault();
    stepHorizontal(event.key === "ArrowRight" ? 1 : -1);
  } else if (!typing && !event.defaultPrevented && !event.metaKey && !event.ctrlKey && !event.altKey
      && !state.spotlightOpen && !state.promptOpen && event.key === "Enter") {
    event.preventDefault();
    drillIn();
  } else if (!typing && !event.defaultPrevented && !event.metaKey && !event.ctrlKey && !event.altKey
      && event.key === "Backspace" && (navMode() === "member" || !state.atPackageRoot)) {
    event.preventDefault();
    drillOut();
  } else if (!typing && event.key === "/") {
    event.preventDefault();
    focusFilter();
  } else if (!typing && !state.spotlightOpen && !event.metaKey && !event.ctrlKey && !event.altKey
      && !event.defaultPrevented && event.key.length === 1 && /[a-zA-Z]/.test(event.key)) {
    event.preventDefault();
    openSpotlight(event.key);
  }
});

document.addEventListener("mousedown", event => {
  if (!state.tasteOpen) return;
  if (event.target.closest("#taste-popover") || event.target.closest("#taste-btn")) return;
  state.tasteOpen = false;
  render();
});

// Re-apply state when the address bar changes underneath us (browser back/forward, or a
// hand-edited URL). Within the loaded package we mutate selection directly; a different
// package is (re)loaded with the URL selection queued as a deep link.
window.addEventListener("popstate", () => {
  if (!state.package) return;
  const loc = parseLocation();
  state.lens = loc.lens || "api";
  state.atPackageRoot = loc.atPackageRoot || false;
  state.packageLens = loc.packageLens || "overview";
  const samePackage = loc.package
    && loc.package.toLowerCase() === state.package.id.toLowerCase()
    && (!loc.version || loc.version.toLowerCase() === state.package.version.toLowerCase());
  if (samePackage || !loc.package) {
    applyDeepLink(loc);
    render();
    loadSelectionData();
  } else {
    pendingDeepLink = { type: loc.type, member: loc.member, overload: loc.overload, section: loc.section };
    loadPackage(loc.package, loc.version || "latest", loc.framework || "");
  }
});

bootstrap();
