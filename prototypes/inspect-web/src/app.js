import { lenses, packageLenses, rootCommands } from "./data.js";
import { initializeEngine, inspectExpandPlatformCallGraph, inspectListStyleOptions, inspectLoadRuntimePack, inspectMemberAnnotatedSource, inspectMemberCallGraph, inspectMemberDocumentation, inspectMemberFacts, inspectMemberSource, inspectPackage, inspectPackageCacheStats, inspectPackageDependencies, inspectPackageIntegrations, inspectPackageOpportunities, inspectPackagePerformance, inspectSearchTypes, inspectTypeMemberSource, inspectTypeProjection, inspectTypeSource } from "/engine.js";

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
  home: false,
  queryNotice: "",
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
  memberCallGraphExpanding: false,
  memberCallGraphSeq: 0,
  platformStack: [],
  platformDrillLoading: false,
  platformDrillError: "",
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
  spotlightScope: "all",
  spotlightFocus: "input",
  spotlightChipIndex: 0,
  spotlightPkgHits: [],
  spotlightPkgLoading: false,
  spotlightPkgQuery: "",
  runtimePackLoading: false,
  runtimePackError: "",
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
  errorTitle: "",
  errorDetail: "",
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

// Member-mode strip: the sections shown for an open member, in display order. Lives at
// module scope because both the scope/lens strip (in render) and the member detail view
// read it. Order here is the visual left-to-right order in the strip.
const memberSectionDefs = [
  ["overview", "Overview"],
  ["call-graph", "Call graph"],
  ["facts", "Facts"],
  ["source", "Source"],
  ["annotated", "Annotated source"]
];

// Members that are not directly callable have no method-body identity, so the body-dependent
// sections (Call graph, Facts, Annotated source) don't apply and the engine rejects them.
// Everything else — methods, constructors, operators, explicit interface method impls — keeps
// the full strip. Properties/fields/events still get Overview and Source (which read the
// declaration, not a body).
const bodilessMemberKinds = new Set(["property", "field", "event", "constant"]);

function memberHasBody(member) {
  return !!member && !bodilessMemberKinds.has(member.kind);
}

function memberSectionsFor(member) {
  return memberHasBody(member)
    ? memberSectionDefs
    : memberSectionDefs.filter(([id]) => id === "overview" || id === "source");
}

// URL-safe base64 over UTF-8 bytes. Used for the opaque share packet so a shared or
// duplicated link can carry the full session state without bloating the visible query.
function base64UrlEncode(text) {
  const bytes = new TextEncoder().encode(text);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function base64UrlDecode(value) {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return new TextDecoder().decode(bytes);
}

// Compact, opaque share packet. Carries everything needed to fully restore a session — the
// open-tab set, which tab is active, the current view, and (only in type view) the selected
// type/member — so the visible query stays down to a human-readable ?package=<id>. Keys are
// terse to keep the encoded string short:
//   t = tabs [[id, version, framework], …]   a = active tab index   v = view token
//   y/m/o/c = selected type / member / overload / member section (type view only)
function encodeShareState() {
  const packet = {
    t: state.packages.map(item => [item.id, item.version, item.activeFramework || ""]),
    a: Math.max(0, state.packages.indexOf(state.package))
  };
  if (state.atPackageRoot) {
    packet.v = state.packageLens && state.packageLens !== "overview" ? `pkg:${state.packageLens}` : "pkg";
  } else {
    // Package identity/view is enough for a package-root link; a selected type only belongs
    // to type view, so it is captured here and nowhere else.
    if (state.lens && state.lens !== "api") packet.v = state.lens;
    if (state.selectedTypeId) packet.y = state.selectedTypeId;
    if (state.selectedMemberKey) packet.m = state.selectedMemberKey;
    if (state.selectedOverloadIndex != null) packet.o = state.selectedOverloadIndex;
    if (state.memberSection && state.memberSection !== "overview") packet.c = state.memberSection;
  }
  return base64UrlEncode(JSON.stringify(packet));
}

function tabsFromTuples(list) {
  return (Array.isArray(list) ? list : [])
    .filter(Array.isArray)
    .map(tuple => ({
      id: String(tuple[0] || ""),
      version: String(tuple[1] || "latest"),
      framework: String(tuple[2] || "")
    }))
    .filter(tab => tab.id);
}

function decodeShareState(value) {
  if (!value) return null;
  try {
    const raw = JSON.parse(base64UrlDecode(value));
    // Legacy form: a bare tuple array of tabs, carrying no view or selection.
    if (Array.isArray(raw)) {
      return { tabs: tabsFromTuples(raw), active: 0, view: "", rich: false, type: null, member: null, overload: null, section: null };
    }
    if (raw && Array.isArray(raw.t)) {
      return {
        tabs: tabsFromTuples(raw.t),
        active: Number.isInteger(raw.a) ? raw.a : 0,
        view: typeof raw.v === "string" ? raw.v : "",
        rich: true,
        type: raw.y != null ? String(raw.y) : null,
        member: raw.m != null ? String(raw.m) : null,
        overload: raw.o != null ? String(raw.o) : null,
        section: raw.c != null ? String(raw.c) : null
      };
    }
    return null;
  } catch {
    return null;
  }
}

// Maps a view token ("pkg", "pkg:dependencies", "source", "call-graph", …) to the lens fields.
function resolveView(token) {
  const atPackageRoot = token === "pkg" || token.startsWith("pkg:");
  return {
    lens: lenses.some(([id]) => id === token) ? token : null,
    atPackageRoot,
    packageLens: atPackageRoot
      ? (packageLenses.some(([id]) => id === token.split(":")[1]) ? token.split(":")[1] : "overview")
      : null
  };
}

function parseLocation() {
  const params = new URLSearchParams(location.search);
  const route = location.pathname.split("/").filter(Boolean);
  const packageAt = route.findIndex(part => part.toLowerCase() === "packages");
  const share = decodeShareState(params.get("w"));

  // Visible fallbacks for legacy/hand-typed links (?package=…, path form, bare params).
  let pkg = packageAt >= 0 ? decodeURIComponent(route[packageAt + 1] || "") : params.get("package");
  let version = packageAt >= 0 ? decodeURIComponent(route[packageAt + 2] || "") : params.get("version");
  let framework = params.get("framework");
  let type = params.get("type");
  let member = params.get("member");
  let overload = params.get("overload");
  let section = params.get("section");
  let viewToken = location.hash.slice(1);
  let tabs = [];
  let active = 0;

  if (share) {
    tabs = share.tabs;
    if (share.rich) {
      // Rich packet is fully authoritative: identity, view, and selection all come from it.
      active = Math.min(Math.max(0, share.active), Math.max(0, tabs.length - 1));
      const target = tabs[active];
      if (target) { pkg = target.id; version = target.version; framework = target.framework; }
      if (share.view) viewToken = share.view;
      type = share.type;
      member = share.member;
      overload = share.overload;
      section = share.section;
    } else {
      // Legacy array packet carries only the extra tab set; the visible params stay the
      // target. Point the active index at the visible package so it opens focused.
      const idx = tabs.findIndex(tab => pkg && tab.id.toLowerCase() === pkg.toLowerCase());
      active = idx >= 0 ? idx : 0;
    }
  }

  const view = resolveView(viewToken);
  return {
    package: pkg,
    version,
    framework,
    type,
    member,
    overload,
    section,
    lens: view.lens,
    atPackageRoot: view.atPackageRoot,
    packageLens: view.packageLens,
    tabs,
    active
  };
}

const initialLocation = parseLocation();
// A bare visit (no package, no shared workspace packet) lands on the intro/home page
// instead of auto-loading a package. Any deep link or shared link skips home and restores
// its workspace directly.
state.home = !initialLocation.package && !(initialLocation.tabs && initialLocation.tabs.length);
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
let depGraphRenderSeq = 0;
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

// The resident runtime pseudo-package (Microsoft.NETCore.App) has no NuGet nupkg, so the
// package lenses that fetch one (Dependencies/Integrations/Opportunities/Analysis) would
// 404. Only Overview renders purely from the already-loaded type surface. Restrict the
// strip so navigating up to package scope in the runtime pack can't reach a broken lens.
function packageLensesFor(pkg) {
  return pkg?.isRuntimePack ? packageLenses.filter(([id]) => id === "overview") : packageLenses;
}

function activeLenses() {
  const sc = scope();
  if (sc === "package") return packageLensesFor(state.package);
  if (sc === "member") return memberSectionsFor(selectedMember(selectedType()));
  return lenses;
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
  state.memberCallGraphExpanding = false;
  // Invalidate any in-flight progressive call-graph load so a late cross-library
  // result can't repopulate the graph after the selection has moved on.
  state.memberCallGraphSeq++;
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

// Switch the open member's section (Overview / Call graph / Facts / Source / Annotated) and
// kick off its lazy load. Shared by the scope-bar strip click and the 1—5 shortcut. If a
// multi-overload member is still on its picker, resolve the first overload so the section
// has content to show.
function applyMemberSection(id) {
  const member = selectedMember(selectedType());
  if (member && member.overloads.length > 1 && state.selectedOverloadIndex == null) {
    state.selectedOverloadIndex = 0;
  }
  state.memberSection = id;
  if (id === "source") loadSelectedMemberSource();
  else if (id === "annotated") loadSelectedMemberAnnotatedSource();
  else if (id === "call-graph") loadSelectedMemberCallGraph();
  else if (id === "facts") loadSelectedMemberFacts();
  else if (id === "overview") loadSelectedMemberDocumentation();
  else render();
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
    const strip = packageLensesFor(state.package);
    const index = strip.findIndex(([id]) => id === state.packageLens);
    state.packageLens = strip[(index + delta + strip.length) % strip.length][0];
    render();
    return;
  }
  const type = selectedType();
  const member = state.lens === "api" ? selectedMember(type) : null;
  const overloadOpen = member && !(member.overloads.length > 1 && state.selectedOverloadIndex == null);
  if (overloadOpen) {
    const order = memberSectionsFor(member).map(([id]) => id);
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

function commandSuggestionsHtml(items) {
  return `${items.map((item, index) => `
      <button class="suggestion ${index === state.completionIndex ? "selected" : ""}" data-completion="${escapeHtml(item.value)}">
        <strong>${escapeHtml(item.value)}</strong><span>${escapeHtml(item.hint)}</span><small>${escapeHtml(item.kind)}</small>
      </button>`).join("")}
      <div class="suggestion-help"><span>↑↓ select</span><span>tab complete</span><span>enter run</span><span>esc dismiss</span></div>`;
}

function bindCommandCompletionClicks(root) {
  root.querySelectorAll("[data-completion]").forEach(button => button.addEventListener("mousedown", event => {
    event.preventDefault();
    applyCompletion(button.dataset.completion);
  }));
}

// Repaint just the completion list. The command <input> is left untouched so the caret
// and native editing state survive (a full render() forced the caret to the end every
// keystroke), and nothing outside the command panel is rebuilt.
function updateCommandSuggestions() {
  const container = document.querySelector("#command-suggestions");
  if (!container) return;
  state.completionIndex = Math.min(state.completionIndex, Math.max(completions().length - 1, 0));
  container.innerHTML = commandSuggestionsHtml(completions());
  bindCommandCompletionClicks(container);
  container.querySelector(".suggestion.selected")?.scrollIntoView({ block: "nearest" });
}

function render() {
  if (state.loading || state.error) {
    renderLoading();
    return;
  }
  if (state.home) {
    renderHomeView();
    return;
  }
  if (!state.package) {
    renderLoading();
    return;
  }
  const current = selectedType();
  const visible = filteredTypes();
  // Keep the package lens on something the active package actually supports, so a restored
  // URL or stale selection can neither render nor auto-load a lens that fetches a missing nupkg.
  if (state.atPackageRoot && !packageLensesFor(state.package).some(([id]) => id === state.packageLens)) {
    state.packageLens = "overview";
  }
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
          <button id="go-home" title="Back to the home page">home</button>
          <button id="theme-toggle" aria-label="Switch to light theme">${state.theme === "dark" ? "light" : "dark"}</button>
          <button id="share">share</button>
          <button id="help" aria-label="Keyboard help">?</button>
        </div>
      </header>

      ${state.queryNotice
        ? `<div class="query-notice" role="alert">
            <span class="query-notice-glyph">⚠</span>
            <span class="query-notice-text">${escapeHtml(state.queryNotice)}</span>
            <button id="dismiss-notice" type="button" aria-label="Dismiss">×</button>
          </div>`
        : ""}

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

      ${renderScopeBar()}

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
            ${state.packageCacheStats && state.packageCacheStats.packages > 0 ? `
            <span class="diag" title="${state.packageCacheStats.packages} distinct NuGet package${state.packageCacheStats.packages === 1 ? "" : "s"} acquired this session; ${state.packageCacheStats.resident} currently resident in the in-memory cache${state.packageCacheStats.packages > state.packageCacheStats.resident ? ` (${state.packageCacheStats.packages - state.packageCacheStats.resident} evicted under the LRU limit of 6 packages / 64 MB)` : ""}">◇ ${state.packageCacheStats.packages} package${state.packageCacheStats.packages === 1 ? "" : "s"} · ${state.packageCacheStats.resident} resident in cache</span>` : ""}
          <span class="status-spacer"></span>
          <span>${escapeHtml(current.assembly)}</span>
          <span>${escapeHtml(state.package.activeFramework)}</span>
          <span>public API surface</span>
          </footer>
        </section>
      </main>

      <section class="command-area">
        <div class="command-panel ${state.promptOpen ? "open" : ""}">
          <div class="suggestions" id="command-suggestions" role="listbox">
            ${commandSuggestionsHtml(suggestions)}
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

// The scope switcher + lens strip. The leading segmented control is the scope ladder —
// Package (whole package), Types (one public type), and Member (a member of that type,
// shown only once you drill in). Each segment is selectable and swaps the strip beside it:
//   package → package lenses   type → type lenses   member → member sections
// Keeping all three families of buttons on one strip means the member modes (Overview,
// Call graph, …) live here too instead of inside the detail pane.
function renderScopeBar() {
  const sc = scope();
  const lensButton = (id, label, active, attr, index) =>
    `<button class="lens ${active ? "active" : ""}" ${attr}="${id}">${escapeHtml(label)}<kbd>${index + 1}</kbd></button>`;
  let strip;
  if (sc === "package") {
    strip = packageLensesFor(state.package).map(([id, label], i) => lensButton(id, label, state.packageLens === id, "data-package-lens", i)).join("");
  } else if (sc === "member") {
    const sections = memberSectionsFor(selectedMember(selectedType()));
    strip = sections.map(([id, label], i) => lensButton(id, label, state.memberSection === id, "data-member-section", i)).join("");
  } else {
    strip = lenses.map(([id, label], i) => lensButton(id, label, state.lens === id, "data-lens", i)).join("");
  }
  const seg = (id, label, active) =>
    `<button class="scope-seg ${active ? "active" : ""}" data-scope="${id}" role="tab" aria-selected="${active}">${label}</button>`;
  return `
    <nav class="lensbar" aria-label="Scope and lenses">
      <div class="scope-switch" role="tablist" aria-label="Scope">
        ${seg("package", "Package", sc === "package")}
        ${seg("type", "Types", sc === "type")}
        ${sc === "member" ? seg("member", "Member", true) : ""}
      </div>
      <span class="lens-separator"></span>
      ${strip}
    </nav>`;
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
  const orderedGroups = [...groups].sort((a, b) => compareFrameworks(a.framework, b.framework));
  const selectorChips = orderedGroups
    .map(group => `<button class="type-chip ${group.framework === selectedTfm ? "active" : ""}" data-dep-framework="${escapeHtml(group.framework)}">${escapeHtml(group.framework)}</button>`)
    .join("");
  const selector = `
    <section class="document-section">
      <div class="section-title"><h2>Target frameworks</h2><span>one framework at a time</span></div>
      <div class="type-chip-list" id="dep-tfm-chips">${selectorChips}</div>
    </section>`;

  const group = groups.find(candidate => candidate.framework === selectedTfm) || groups[0];
  const depList = dependencyListSectionHtml(groups, selectedTfm);

  const graphSection = `
    <section class="document-section">
      <div class="section-title"><h2>Dependency graph</h2><span>callers above · dependencies below · click a package to open</span></div>
      <div id="dependency-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph…</p></div>
    </section>`;

  return `${selector}${graphSection}${depList}`;
}

// The NuGet dependency list for the selected TFM. Extracted so a framework switch can
// replace just this section in place instead of re-rendering the whole page (which would
// reset the dependency graph container to its loader and flash the diagram).
function dependencyListSectionHtml(groups, selectedTfm) {
  const group = groups.find(candidate => candidate.framework === selectedTfm) || groups[0];
  const deps = group.dependencies || [];
  const openIds = new Set(state.packages.map(item => item.id.toLowerCase()));
  return `
    <section class="document-section" id="dep-list-section">
      <div class="section-title"><h2>NuGet dependencies</h2><span>${escapeHtml(group.framework)} · ${deps.length} package${deps.length === 1 ? "" : "s"}</span></div>
      ${deps.length
        ? `<ul class="dep-list">${deps.map(dependency => {
            const isOpen = openIds.has(dependency.id.toLowerCase());
            const attrs = isOpen
              ? `data-dep-open="${escapeHtml(dependency.id)}" title="Switch to ${escapeHtml(dependency.id)}"`
              : `data-dep-load="${escapeHtml(dependency.id)}" data-dep-version="${escapeHtml(dependency.versionRange || "")}" title="Open ${escapeHtml(dependency.id)} in a new tab"`;
            return `<li><button class="dep-name as-link${isOpen ? " is-open" : ""}" ${attrs}>${escapeHtml(dependency.id)}</button><code class="dep-version">${escapeHtml(dependency.versionRange || "*")}</code></li>`;
          }).join("")}</ul>`
        : `<div class="empty-list">No package dependencies declared for ${escapeHtml(group.framework)}.</div>`}
    </section>`;
}

// Switch the dependency lens to a different target framework without a full page render:
// toggle the active chip, swap the dependency list in place, and let renderDependencyGraph
// swap the diagram (it keeps the old SVG until the new one is ready, so no loader flash).
function patchDependenciesFramework() {
  const groups = state.packageDependencies?.dependencyGroups || [];
  const listSection = document.querySelector("#dep-list-section");
  if (!groups.length || !listSection) { render(); return; }
  const selectedTfm = resolveDependenciesFramework(groups);
  document.querySelectorAll("#dep-tfm-chips [data-dep-framework]").forEach(button =>
    button.classList.toggle("active", button.dataset.depFramework === selectedTfm));
  listSection.outerHTML = dependencyListSectionHtml(groups, selectedTfm);
  bindDependencyListHandlers();
  renderDependencyGraph();
}

function bindDependencyListHandlers() {
  document.querySelectorAll("[data-dep-open]").forEach(button => {
    button.onclick = () => switchToPackageForDependencies(button.dataset.depOpen);
  });
  document.querySelectorAll("[data-dep-load]").forEach(button => {
    button.onclick = () => openDependencyPackage(button.dataset.depLoad, button.dataset.depVersion || "");
  });
}

// Orders target-framework monikers: modern .NET (net with a dotted version) first,
// then .NET Framework (net without a dot), then netstandard, each descending by version,
// with anything else sorted alphabetically last.
function frameworkTier(moniker) {
  const m = String(moniker).toLowerCase();
  if (m.startsWith("netstandard")) return 2;
  if (m.startsWith("net")) return m.slice(3).includes(".") ? 0 : 1;
  return 3;
}

function frameworkVersionParts(moniker) {
  const match = String(moniker).toLowerCase().match(/(\d+(?:\.\d+)*)$/);
  const version = match ? match[1] : "";
  if (!version) return [];
  return version.includes(".") ? version.split(".").map(Number) : version.split("").map(Number);
}

function compareFrameworks(a, b) {
  const tierA = frameworkTier(a);
  const tierB = frameworkTier(b);
  if (tierA !== tierB) return tierA - tierB;
  if (tierA === 3) return String(a).localeCompare(String(b));
  const versionA = frameworkVersionParts(a);
  const versionB = frameworkVersionParts(b);
  const length = Math.max(versionA.length, versionB.length);
  for (let i = 0; i < length; i++) {
    const partA = versionA[i] ?? 0;
    const partB = versionB[i] ?? 0;
    if (partA !== partB) return partB - partA;
  }
  return String(a).localeCompare(String(b));
}

function resolveDependenciesFramework(groups) {
  const available = groups.map(group => group.framework);
  if (state.dependenciesFramework && available.includes(state.dependenciesFramework)) {
    return state.dependenciesFramework;
  }
  const active = groups.find(group => group.isActive);
  return active ? active.framework : [...available].sort(compareFrameworks)[0];
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
    refreshPackageStats();
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
  refreshPackageStats();
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

  const blocks = categories.map(category => {
    const signals = [...category.signals].sort((a, b) => {
      const rank = shape => /type/i.test(shape) ? 0 : 1;
      return rank(a.shape) - rank(b.shape) || a.kind.localeCompare(b.kind) || a.name.localeCompare(b.name);
    });
    const rows = signals.map(signal => {
      const isType = /type/i.test(signal.shape);
      const { short, qualifier } = splitSignalName(signal.name);
      return `
        <div class="signal-row" title="${escapeHtml(signal.name)} · ${escapeHtml(signal.shape)} · ${escapeHtml(signal.kind)}">
          <span class="signal-badge signal-${isType ? "type" : "api"}">${isType ? "T" : "ƒ"}</span>
          <span class="signal-body"><span class="signal-name">${escapeHtml(short)}</span>${qualifier ? `<span class="signal-ns">${escapeHtml(qualifier)}</span>` : ""}</span>
          <span class="signal-kind">${escapeHtml(signal.kind)}</span>
        </div>`;
    }).join("");
    return `
    <section class="document-section">
      <div class="section-title"><h2>${escapeHtml(category.integration)}</h2><span>${category.typeCount} type${category.typeCount === 1 ? "" : "s"} · ${category.apiCount} API${category.apiCount === 1 ? "" : "s"}</span></div>
      <div class="signal-list">${rows}</div>
    </section>`;
  }).join("");

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
      <p class="lens-note">Ecosystem areas this package's surface suggests but does not yet integrate with. Chips are live: the type opens in this package, a suggested package loads on demand, and each "look for" API opens a search.</p>
      <div class="type-chip-list">${categories.map(category => `<span class="type-chip">${escapeHtml(category.integration)} <span class="ns-count">${category.items.length}</span></span>`).join("")}</div>
    </section>`;

  const blocks = categories.map(category => {
    const rows = category.items.map(renderOpportunityRow).join("");
    return `
    <section class="document-section">
      <div class="section-title"><h2>${escapeHtml(category.integration)}</h2><span>${category.items.length} suggestion${category.items.length === 1 ? "" : "s"}</span></div>
      <div class="opp-list">${rows}</div>
    </section>`;
  }).join("");

  return `${warning}${summary}${blocks}`;
}

// Renders a single integration-opportunity as a signal-style row with live chips: the API
// (a type in this package) navigates in place; a suggested package (a dotted namespace parsed
// from the integration kind) loads on demand; each concrete "look for" API opens the spotlight
// search. Naming patterns (wildcards) stay as muted, non-clickable hints.
function renderOpportunityRow(item) {
  const api = splitSignalName(item.api);
  const kind = splitOpportunityKind(item.integrationType);
  const kindHtml = kind.package
    ? `<button class="opp-package-chip" data-opp-package="${escapeHtml(kind.package)}" title="Load ${escapeHtml(kind.package)} into the workspace">${escapeHtml(kind.package)}</button>${kind.text ? `<span class="opp-kind-text">${escapeHtml(kind.text)}</span>` : ""}`
    : `<span class="opp-kind-text">${escapeHtml(item.integrationType)}</span>`;
  return `
    <div class="opp-row">
      <span class="signal-badge signal-type">T</span>
      <div class="opp-body">
        <div class="opp-head">
          <button class="opp-type-chip" data-opp-type="${escapeHtml(item.api)}" title="Open ${escapeHtml(item.api)} in this package">
            <span class="opp-type-name">${escapeHtml(api.short)}</span>${api.qualifier ? `<span class="opp-type-ns">${escapeHtml(api.qualifier)}</span>` : ""}
          </button>
          <span class="opp-kind">${kindHtml}</span>
        </div>
        <div class="opp-lookfor"><span class="opp-lookfor-label">look for</span>${renderLookForChips(item.lookFor)}</div>
      </div>
    </div>`;
}

// Pulls a leading dotted namespace (a candidate package like "Microsoft.Extensions.AI") off the
// front of an integration-kind phrase so it can render as a load-on-demand package chip. Kinds
// with no dotted prefix (e.g. "IServiceCollection registration") stay as plain muted text.
function splitOpportunityKind(integrationType) {
  const match = String(integrationType || "").match(/^([A-Z][A-Za-z0-9]+(?:\.[A-Z][A-Za-z0-9]+)+)\b\s*(.*)$/);
  return match ? { package: match[1], text: match[2].trim() } : { package: null, text: String(integrationType || "") };
}

// Turns the comma-separated "look for" hint into chips. Concrete identifiers open a spotlight
// search (seeded on the base name, generics stripped); wildcard patterns like "Add*" render as
// muted, non-interactive hints because they are naming shapes rather than resolvable types.
function renderLookForChips(lookFor) {
  const tokens = String(lookFor || "").split(",").map(token => token.trim()).filter(Boolean);
  if (!tokens.length) return `<span class="opp-pattern">any registration surface</span>`;
  return tokens.map(token => {
    if (token.includes("*")) return `<span class="opp-pattern" title="Naming pattern">${escapeHtml(token)}</span>`;
    const seed = token.replace(/<.*$/, "");
    return `<button class="opp-chip" data-opp-lookfor="${escapeHtml(seed)}" title="Search the workspace for ${escapeHtml(token)}">${escapeHtml(token)}</button>`;
  }).join("");
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
    const active = currentCallGraph();
    const drilled = state.platformStack.length > 0;
    // A resident runtime-pack member's base graph is itself a platform (callee-only) graph:
    // there is no workspace to scan for callers, so present it as a platform view rather than
    // a "Workspace callers · 0 loaded packages" line that reads as an empty/failed scan.
    const platformView = drilled || Boolean(state.package?.isRuntimePack);
    const callers = active?.callers?.children ?? [];
    const callees = active?.callees?.children ?? [];
    const scope = active?.scope;
    const breadcrumb = drilled
      ? `<div class="graph-breadcrumb">
          <button type="button" data-graph-back title="Back one level">‹ Back</button>
          <span class="graph-crumbs">${escapeHtml(platformCrumbTrail())}</span>
        </div>`
      : "";
    const scopeLine = !scope
      ? ""
      : platformView
      ? `<div class="graph-scope"><strong>Platform${drilled ? " descent" : ""}</strong><span>${escapeHtml(scope.calleeScope)} · CoreCLR runtime pack</span><strong>Callees</strong><span>depth 2</span></div>`
      : `<div class="graph-scope"><strong>Workspace callers</strong><span>${scope.packages} loaded packages · ${scope.callerAssemblies} scanned assemblies</span><strong>Callees</strong><span>${escapeHtml(scope.calleeScope)} · depth 2</span></div>`;
    content = state.memberCallGraphLoading
      ? `<section class="document-section source-progress"><span class="loader"></span><h2>Building workspace call graph…</h2><p>Scanning implementation IL across ${state.packages.length} loaded package${state.packages.length === 1 ? "" : "s"}.</p></section>`
      : active
        ? `<section class="document-section call-graph-section">
            <div class="section-title"><h2>Call graph</h2><span>${callers.length} caller${callers.length === 1 ? "" : "s"} · ${callees.length} callee${callees.length === 1 ? "" : "s"}</span></div>
            ${breadcrumb}
            ${state.platformDrillLoading
              ? `<div class="graph-expanding"><span class="loader"></span> Range-fetching the implementation assembly from the CoreCLR runtime pack…</div>`
              : ""}
            ${state.platformDrillError
              ? `<div class="graph-drill-error">${escapeHtml(state.platformDrillError)}</div>`
              : ""}
            ${state.memberCallGraphExpanding
              ? `<div class="graph-expanding"><span class="loader"></span> Scanning ${state.packages.length - 1} other librar${state.packages.length - 1 === 1 ? "y" : "ies"} for callers…</div>`
              : ""}
            ${scopeLine}
            <div id="call-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph…</p></div>
            <div class="graph-legend" aria-label="Graph legend">
              <span><i class="legend-swatch target"></i>target member</span>
              <span><i class="legend-swatch same-type"></i>same declaring type</span>
              <span><i class="legend-swatch different-type"></i>different type, same assembly</span>
              <span><i class="legend-swatch different-assembly"></i>different assembly (click to descend)</span>
            </div>
            <details class="graph-mermaid"><summary>Mermaid source</summary><pre><code>${escapeHtml(active.mermaid)}</code></pre></details>
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
  // The member-mode strip (Overview / Call graph / Facts / Source / Annotated) now lives in
  // the top scope+lens bar, so the detail view renders only the section content itself.
  return content;
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
  const allocOffsets = facts.allocations.map(a => a.offset);
  const callOffsets = facts.calls.map(c => c.offset);
  const safetyOffsets = facts.safety.map(s => s.offset);
  const loopAllocOffsets = facts.allocations.filter(a => a.inLoop).map(a => a.offset);
  return `
    <section class="document-section facts-section">
      <div class="section-title"><h2>Method facts</h2><span>selected overload</span></div>
      ${factRows([
        ["Overload", `${overloadIndex + 1} of ${member.overloads.length}`],
        ["Kind", overload.kind],
        ["Metadata token", overload.metadataToken == null ? "not exposed" : `0x${overload.metadataToken.toString(16).padStart(8, "0")}`],
        ["Declaring type", type.id],
        ["Allocations", String(signals.allocations), allocOffsets],
        ["Calls", String(facts.calls.length), callOffsets],
        ["Copies", String(signals.copies)],
        ["Reflection calls", String(signals.reflection)],
        ["Throws / catches / finally", `${signals.throws} / ${signals.catches} / ${signals.finallys}`],
        ["Unsafe", signals.unsafe ? "yes" : "no", signals.unsafe ? safetyOffsets : []],
        ["Allocates in loop", signals.allocatesInLoop ? "yes" : "no", signals.allocatesInLoop ? loopAllocOffsets : []]
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
  return `<dl class="fact-rows">${rows.map(([key, value, evidence]) => `<div><dt>${escapeHtml(key)}</dt><dd><code>${escapeHtml(value)}</code>${factEvidence(evidence)}</dd></div>`).join("")}</dl>`;
}

// Inline, muted IL-offset evidence riding along a fact's value (e.g. "1  IL_000C").
// Deduplicated and capped so a hot method never restores the long-line problem; the
// overflow count carries the full list in a tooltip and the detail table below holds
// every occurrence. Sourced from the detail collections so the summary and the tables agree.
function factEvidence(offsets) {
  const unique = [...new Set((offsets ?? []).filter(Boolean))];
  if (!unique.length) return "";
  const CAP = 2;
  const shown = unique.slice(0, CAP);
  const extra = unique.length - shown.length;
  const label = shown.join(", ") + (extra > 0 ? ` +${extra}` : "");
  return `<span class="fact-evidence" title="${escapeHtml(unique.join(", "))}">${escapeHtml(label)}</span>`;
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

// Split an integration signal's fully-qualified name into its short member/type name and a
// declaring qualifier. Cuts off a method parameter list or generic argument list before the
// last-dot split so a dot inside "(...)" or "<...>" never gets mistaken for the name boundary.
function splitSignalName(fullName) {
  const paren = fullName.indexOf("(");
  const angle = fullName.indexOf("<");
  const bounds = [paren, angle].filter(i => i >= 0);
  const cut = bounds.length ? Math.min(...bounds) : -1;
  const head = cut < 0 ? fullName : fullName.slice(0, cut);
  const suffix = cut < 0 ? "" : fullName.slice(cut);
  const dot = head.lastIndexOf(".");
  return {
    short: (dot < 0 ? head : head.slice(dot + 1)) + suffix,
    qualifier: dot < 0 ? "" : head.slice(0, dot),
  };
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
  document.querySelectorAll("[data-scope]").forEach(button => button.addEventListener("click", () => {
    const target = button.dataset.scope;
    if (target === "package") {
      state.atPackageRoot = true;
    } else if (target === "type") {
      // Pop out to the type level: leave the package root and drop any open member so the
      // type lenses (API / Metadata / Source) take the strip. Ensure a type is selected.
      state.atPackageRoot = false;
      if (!state.selectedTypeId) {
        const first = filteredTypes()[0];
        if (first) state.selectedTypeId = first.id;
      }
      state.selectedMemberKey = "";
      state.selectedOverloadIndex = null;
    }
    // "member" is only shown while it is already the active scope, so it is a no-op.
    render();
  }));
  document.querySelectorAll("[data-package-lens]").forEach(button => button.addEventListener("click", () => {
    state.packageLens = button.dataset.packageLens;
    render();
  }));
  document.querySelectorAll("[data-framework-chip]").forEach(button => button.addEventListener("click", () => {
    loadPackage(state.package.id, state.package.version, button.dataset.frameworkChip);
  }));
  document.querySelectorAll("[data-dep-framework]").forEach(button => button.addEventListener("click", () => {
    if (state.dependenciesFramework === button.dataset.depFramework) return;
    state.dependenciesFramework = button.dataset.depFramework;
    patchDependenciesFramework();
  }));
  bindDependencyListHandlers();
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
  document.querySelectorAll("[data-opp-type]").forEach(button => button.addEventListener("click", () => {
    const id = button.dataset.oppType;
    const target = state.package.types.find(item => item.id === id);
    if (!target) { openSpotlight(shortTypeName(id)); return; }
    state.atPackageRoot = false;
    navigateToTypeByName(id);
  }));
  document.querySelectorAll("[data-opp-package]").forEach(button => button.addEventListener("click", () => {
    openDependencyPackage(button.dataset.oppPackage, "");
  }));
  document.querySelectorAll("[data-opp-lookfor]").forEach(button => button.addEventListener("click", () => {
    openSpotlight(button.dataset.oppLookfor);
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
    applyMemberSection(button.dataset.memberSection);
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
  bindCommandCompletionClicks(document);

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
      if (state.spotlightFocus === "chips") {
        state.spotlightFocus = "input";
        updateSpotlightChips();
      }
      scheduleSpotlightPackageFetch();
      updateSpotlightResults();
    });
    spotlightInput.addEventListener("keydown", handleSpotlightKeys);
  }
  bindSpotlightChipClicks(document);
  bindSpotlightResultClicks(document);
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
    updateCommandSuggestions();
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
  document.querySelector("[data-graph-back]")?.addEventListener("click", popPlatformDrill);
  document.querySelector("#dismiss-notice")?.addEventListener("click", () => {
    state.queryNotice = "";
    render();
  });
  document.querySelector("#nav-back")?.addEventListener("click", navBack);
  document.querySelector("#nav-forward")?.addEventListener("click", navForward);
  document.querySelector("#go-home").addEventListener("click", goHome);
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

// Ranked type matches across all loaded packages (engine-owned SearchTypes, with a
// client-side fallback). This is one target among several the scoped Spotlight blends.
function spotlightTypeMatches(query) {
  const cache = spotlightCandidates();
  if (!query) {
    return cache.pool
      .filter(item => item.pkg === state.package)
      .sort((a, b) => a.type.name.localeCompare(b.type.name))
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

// Flat member index across every loaded type, deduped by (package, type, member group).
// Cached against the same workspace signature as the type pool so it rebuilds only when
// packages or their type counts change.
let spotlightMemberCache = null;
function spotlightMemberCandidates() {
  const signature = `${state.package?.id ?? ""}#${state.packages
    .map(pkg => `${pkg.id}:${pkg.types?.length ?? 0}`)
    .join("|")}`;
  if (spotlightMemberCache && spotlightMemberCache.signature === signature) return spotlightMemberCache.pool;
  const pool = [];
  for (const pkg of [state.package, ...state.packages.filter(item => item !== state.package)]) {
    if (!pkg?.types) continue;
    for (const type of pkg.types) {
      for (const group of memberGroups(type)) {
        pool.push({ pkg, type, memberKey: group.key, name: group.name, kind: group.kind });
      }
    }
  }
  spotlightMemberCache = { signature, pool };
  return pool;
}

function spotlightMemberMatches(query) {
  const pool = spotlightMemberCandidates();
  if (!query) return [];
  const lowerQuery = query.toLowerCase();
  const scored = [];
  for (const item of pool) {
    const lower = item.name.toLowerCase();
    let rank;
    if (lower === lowerQuery) rank = 0;
    else if (lower.startsWith(lowerQuery)) rank = 1;
    else if (lower.includes(lowerQuery)) rank = 2;
    else {
      const sub = subsequenceRanges(lower, lowerQuery);
      if (!sub) continue;
      rank = 3;
    }
    scored.push({ item, rank });
  }
  scored.sort((a, b) =>
    a.rank - b.rank
    || a.item.name.length - b.item.name.length
    || a.item.name.localeCompare(b.item.name));
  return scored.map(entry => ({ ...entry.item, ranges: computeHighlightRanges(entry.item.name, lowerQuery) }));
}

// Already-open packages whose id matches the query (or all of them when the query is empty).
function spotlightLoadedPackageMatches(query) {
  const lowerQuery = query.toLowerCase();
  return state.packages
    .filter(pkg => !lowerQuery || pkg.id.toLowerCase().includes(lowerQuery))
    .map(pkg => ({ pkg, ranges: computeHighlightRanges(pkg.id, lowerQuery) }));
}

const SPOTLIGHT_SCOPES = [
  { id: "all", label: "All" },
  { id: "packages", label: "Packages" },
  { id: "types", label: "Types" },
  { id: "members", label: "Members" },
  { id: "runtime", label: "Runtime" },
];

// Runtime-pack types as spotlight match items (empty-query listing for the Runtime scope).
function runtimePackTypeList() {
  const rt = runtimePackPackage();
  if (!rt?.types) return [];
  return rt.types
    .slice()
    .sort((a, b) => a.name.localeCompare(b.name))
    .map(type => ({ pkg: rt, type, ranges: [] }));
}

// Blends the four targets into one ordered result list, honouring the active scope chip.
// In "all" each group is capped so every target stays visible; a focused scope shows a
// deeper single-target list. Loaded packages rank ahead of NuGet discovery hits, which
// exclude anything already open.
function spotlightResults() {
  const query = state.spotlightQuery.trim();
  const scope = state.spotlightScope;
  const all = scope === "all";
  const results = [];

  if (scope === "runtime") {
    if (state.runtimePackLoading) {
      results.push({ kind: "rtpack-status", loading: true });
      return results;
    }
    if (!runtimePackLoaded()) {
      if (state.runtimePackError) results.push({ kind: "rtpack-status", error: state.runtimePackError });
      results.push({ kind: "rtpack-suggest" });
      return results;
    }
    const typeSource = query ? spotlightTypeMatches(query) : runtimePackTypeList();
    for (const match of typeSource.filter(item => item.pkg?.isRuntimePack).slice(0, 50)) {
      results.push({ ...match, kind: "type" });
    }
    if (query) {
      for (const match of spotlightMemberMatches(query).filter(item => item.pkg?.isRuntimePack).slice(0, 50)) {
        results.push({ ...match, kind: "member" });
      }
    }
    return results;
  }

  if (all || scope === "packages") {
    const loaded = spotlightLoadedPackageMatches(query).slice(0, all ? 3 : 20);
    for (const match of loaded) results.push({ kind: "pkg-loaded", pkg: match.pkg, ranges: match.ranges });
    const openIds = new Set(state.packages.map(pkg => pkg.id.toLowerCase()));
    let added = 0;
    for (const hit of state.spotlightPkgHits) {
      if (openIds.has(hit.id.toLowerCase())) continue;
      results.push({ kind: "pkg-nuget", hit, ranges: computeHighlightRanges(hit.id, query.toLowerCase()) });
      if (all && ++added >= 4) break;
    }
  }
  if ((all || scope === "types") && query) {
    for (const match of spotlightTypeMatches(query).slice(0, all ? 6 : 50)) results.push({ ...match, kind: "type" });
  } else if (scope === "types" && !query) {
    for (const match of spotlightTypeMatches("").slice(0, 40)) results.push({ ...match, kind: "type" });
  }
  if ((all || scope === "members") && query) {
    for (const match of spotlightMemberMatches(query).slice(0, all ? 6 : 50)) results.push({ ...match, kind: "member" });
  }
  // Offer the runtime pack when the user is clearly hunting a platform type but it isn't
  // loaded yet — one gesture makes BCL types (TextWriter, String…) searchable session-wide.
  if ((all || scope === "types") && query.length >= 2 && !runtimePackLoaded() && !state.runtimePackLoading) {
    results.push({ kind: "rtpack-suggest" });
  }
  return results;
}

function spotlightRowHtml(result, index) {
  const selected = index === state.spotlightIndex ? "selected" : "";
  const multiPkg = state.packages.length > 1;
  const base = `class="spotlight-item ${selected}" role="option" aria-selected="${index === state.spotlightIndex}" data-sl-index="${index}"`;
  if (result.kind === "pkg-loaded") {
    return `<button ${base} data-sl-pkg-open="${escapeHtml(result.pkg.id)}">
      <span class="kind-icon sl-pkg">▣</span>
      <span class="spotlight-item-name">${highlightRanges(result.pkg.id, result.ranges)}</span>
      <span class="spotlight-item-ns">${escapeHtml(result.pkg.version)} · open</span>
    </button>`;
  }
  if (result.kind === "pkg-nuget") {
    return `<button ${base} data-sl-pkg-load="${escapeHtml(result.hit.id)}" data-sl-pkg-version="${escapeHtml(result.hit.version || "")}">
      <span class="kind-icon sl-pkg-new">↓</span>
      <span class="spotlight-item-name">${highlightRanges(result.hit.id, result.ranges)}</span>
      <span class="spotlight-item-ns">${escapeHtml(result.hit.version || "")} · nuget.org</span>
    </button>`;
  }
  if (result.kind === "rtpack-suggest") {
    const fw = state.package?.activeFramework || "runtime";
    return `<button ${base} data-sl-load-runtime="1">
      <span class="kind-icon sl-pkg-new">↓</span>
      <span class="spotlight-item-name">Load .NET runtime pack</span>
      <span class="spotlight-item-ns">Search platform types (TextWriter, String…) · ${escapeHtml(fw)}</span>
    </button>`;
  }
  if (result.kind === "rtpack-status") {
    const text = result.loading
      ? "Loading .NET runtime pack — this can take a while…"
      : `Runtime pack failed: ${result.error || "unknown error"}`;
    return `<div class="spotlight-item spotlight-status ${selected}" data-sl-index="${index}">
      <span class="kind-icon">${result.loading ? "◔" : "⚠"}</span>
      <span class="spotlight-item-name">${escapeHtml(text)}</span>
    </div>`;
  }
  if (result.kind === "member") {
    return `<button ${base} data-sl-member="${escapeHtml(result.memberKey)}" data-sl-pkg="${escapeHtml(result.pkg.id)}" data-sl-type="${escapeHtml(result.type.id)}">
      <span class="kind-icon sl-member">ƒ</span>
      <span class="spotlight-item-name">${highlightRanges(result.name, result.ranges)}</span>
      <span class="spotlight-item-ns">${escapeHtml(result.type.name)}${multiPkg ? ` · ${escapeHtml(result.pkg.id)}` : ""}</span>
    </button>`;
  }
  return `<button ${base} data-sl-type="${escapeHtml(result.type.id)}" data-sl-pkg="${escapeHtml(result.pkg.id)}">
    <span class="kind-icon">${kindIcon(result.type.kind)}</span>
    <span class="spotlight-item-name">${highlightRanges(result.type.name, result.ranges)}</span>
    <span class="spotlight-item-ns">${escapeHtml(result.type.namespace || "")}${multiPkg ? ` · ${escapeHtml(result.pkg.id)}` : ""}</span>
  </button>`;
}

const SPOTLIGHT_GROUP_LABELS = { "pkg-loaded": "Packages", "pkg-nuget": "Packages", type: "Types", member: "Members", "rtpack-suggest": "Runtime", "rtpack-status": "Runtime" };

function spotlightResultsHtml(results) {
  if (!results.length) {
    const q = state.spotlightQuery.trim();
    if (!q) return `<div class="spotlight-empty">Search packages, types, and members — pick a target below.</div>`;
    if (state.spotlightPkgLoading) return `<div class="spotlight-empty">Searching…</div>`;
    return `<div class="spotlight-empty">Nothing matches “${escapeHtml(q)}”.</div>`;
  }
  const grouped = state.spotlightScope === "all";
  let html = "";
  let lastGroup = null;
  results.forEach((result, index) => {
    if (grouped) {
      const group = SPOTLIGHT_GROUP_LABELS[result.kind];
      if (group !== lastGroup) {
        html += `<div class="spotlight-group">${group}</div>`;
        lastGroup = group;
      }
    }
    html += spotlightRowHtml(result, index);
  });
  if (state.spotlightPkgLoading && (state.spotlightScope === "all" || state.spotlightScope === "packages")) {
    html += `<div class="spotlight-hint">Searching nuget.org…</div>`;
  }
  return html;
}

function spotlightChipsHtml() {
  return SPOTLIGHT_SCOPES.map((scope, index) => {
    const active = state.spotlightScope === scope.id ? "active" : "";
    const focused = state.spotlightFocus === "chips" && state.spotlightChipIndex === index ? "focused" : "";
    return `<button class="spotlight-chip ${active} ${focused}" data-sl-scope="${scope.id}" data-sl-chip="${index}">${scope.label}</button>`;
  }).join("");
}

function renderSpotlight() {
  const results = spotlightResults();
  state.spotlightIndex = Math.min(state.spotlightIndex, Math.max(results.length - 1, 0));
  return `
    <div class="spotlight-backdrop" id="spotlight-backdrop">
      <div class="spotlight" role="dialog" aria-modal="true" aria-label="Go to anything">
        <div class="spotlight-search">
          <span class="spotlight-glyph">⌕</span>
          <input id="spotlight-input" value="${escapeHtml(state.spotlightQuery)}" placeholder="Go to anything…  package, type, or member" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="spotlight-results" />
          <kbd>esc</kbd>
        </div>
        <div class="spotlight-chips" id="spotlight-chips">${spotlightChipsHtml()}</div>
        <div class="spotlight-results" id="spotlight-results" role="listbox">${spotlightResultsHtml(results)}</div>
        <div class="spotlight-foot"><span>↑↓ select</span><span>→ target</span><span>↵ open</span><span>esc close</span></div>
      </div>
    </div>`;
}

function bindSpotlightResultClicks(root) {
  const dispatch = index => {
    const results = spotlightResults();
    const result = results[Number(index)];
    if (result) pickSpotlightResult(result);
  };
  root.querySelectorAll("[data-sl-index]").forEach(button =>
    button.addEventListener("click", () => dispatch(button.dataset.slIndex)));
}

function bindSpotlightChipClicks(root) {
  root.querySelectorAll("[data-sl-scope]").forEach(button =>
    button.addEventListener("click", () => setSpotlightScope(button.dataset.slScope)));
}

// Repaints just the scope-chip row so arrow-key focus movement doesn't rebuild the
// input (and lose its caret) on every keystroke.
function updateSpotlightChips() {
  const container = document.querySelector("#spotlight-chips");
  if (!container) return;
  container.innerHTML = spotlightChipsHtml();
  bindSpotlightChipClicks(container);
}

function updateSpotlightResults() {
  const container = document.querySelector("#spotlight-results");
  if (!container) return;
  const results = spotlightResults();
  state.spotlightIndex = Math.min(state.spotlightIndex, Math.max(results.length - 1, 0));
  container.innerHTML = spotlightResultsHtml(results);
  bindSpotlightResultClicks(container);
  container.querySelector(".spotlight-item.selected")?.scrollIntoView({ block: "nearest" });
}

function setSpotlightScope(scope) {
  if (!SPOTLIGHT_SCOPES.some(item => item.id === scope)) return;
  state.spotlightScope = scope;
  state.spotlightIndex = 0;
  if (scope === "runtime" && !runtimePackLoaded() && !state.runtimePackLoading) {
    activateRuntimePack();
    focusSpotlight();
    return;
  }
  if (scope === "packages" || scope === "all") scheduleSpotlightPackageFetch();
  // Scope only affects the chip row and the results list, so repaint those in place
  // instead of re-rendering the whole app (which flashed the screen on every chip move).
  updateSpotlightChips();
  updateSpotlightResults();
  focusSpotlight();
}

// Debounced client-side NuGet discovery. Guards against stale queries via spotlightPkgQuery
// and refreshes results only when the resolved query still matches the input.
let spotlightPkgTimer = null;
function scheduleSpotlightPackageFetch() {
  const query = state.spotlightQuery.trim();
  if (spotlightPkgTimer) clearTimeout(spotlightPkgTimer);
  if (state.spotlightScope !== "all" && state.spotlightScope !== "packages") return;
  if (query.length < 2) {
    state.spotlightPkgHits = [];
    state.spotlightPkgQuery = "";
    state.spotlightPkgLoading = false;
    return;
  }
  if (query === state.spotlightPkgQuery) return;
  state.spotlightPkgLoading = true;
  spotlightPkgTimer = setTimeout(() => fetchSpotlightPackages(query), 220);
}

async function fetchSpotlightPackages(query) {
  const url = `https://azuresearch-usnc.nuget.org/query?q=${encodeURIComponent(query)}&take=8&prerelease=true&semVerLevel=2.0.0`;
  try {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const payload = await response.json();
    if (state.spotlightQuery.trim() !== query) return; // stale
    state.spotlightPkgHits = (payload.data || []).map(item => ({
      id: item.id,
      version: item.version,
      description: item.description || "",
    }));
    state.spotlightPkgQuery = query;
  } catch (error) {
    if (state.spotlightQuery.trim() !== query) return;
    state.spotlightPkgHits = [];
    state.spotlightPkgQuery = query;
  } finally {
    if (state.spotlightQuery.trim() === query) {
      state.spotlightPkgLoading = false;
      updateSpotlightResults();
    }
  }
}

function openSpotlight(seed = "") {
  state.spotlightOpen = true;
  state.spotlightQuery = seed;
  state.spotlightScope = "all";
  state.spotlightFocus = "input";
  state.spotlightChipIndex = 0;
  state.spotlightIndex = 0;
  state.spotlightPkgHits = [];
  state.spotlightPkgQuery = "";
  state.spotlightPkgLoading = false;
  if (seed.trim()) scheduleSpotlightPackageFetch();
  render();
  focusSpotlight();
}

function closeSpotlight() {
  state.spotlightOpen = false;
  state.spotlightQuery = "";
  state.spotlightFocus = "input";
  state.spotlightChipIndex = 0;
  state.spotlightIndex = 0;
  state.spotlightPkgHits = [];
  state.spotlightPkgQuery = "";
  state.spotlightPkgLoading = false;
  if (spotlightPkgTimer) clearTimeout(spotlightPkgTimer);
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

// Routes a blended result to the right navigation path per its kind.
function pickSpotlightResult(result) {
  if (!result) { closeSpotlight(); return; }
  switch (result.kind) {
    case "pkg-loaded": pickSpotlightLoadedPackage(result.pkg); break;
    case "pkg-nuget": closeSpotlight(); loadPackage(result.hit.id, result.hit.version); break;
    case "member": pickSpotlightMember(result); break;
    case "rtpack-suggest": state.spotlightScope = "runtime"; state.spotlightIndex = 0; activateRuntimePack(); break;
    case "rtpack-status": break;
    default: pickSpotlight(result.pkg.id, result.type.id); break;
  }
}

// Kicks off the runtime-pack load (if not already loaded/loading) and repaints the
// spotlight in place so the loading row and, once resolved, the platform types appear
// without tearing down the dialog.
function activateRuntimePack() {
  if (runtimePackLoaded() || state.runtimePackLoading) {
    updateSpotlightChips();
    updateSpotlightResults();
    return;
  }
  const framework = state.package?.activeFramework || "";
  const pending = loadRuntimePack(framework); // sets runtimePackLoading synchronously
  updateSpotlightChips();
  updateSpotlightResults();
  pending.then(() => {
    if (!state.spotlightOpen) return;
    updateSpotlightChips();
    updateSpotlightResults();
  });
}

function pickSpotlightLoadedPackage(pkg) {
  const target = state.packages.find(item => item.id === pkg.id) || pkg;
  state.package = target;
  state.atPackageRoot = true;
  state.selectedTypeId = null;
  state.selectedMemberKey = "";
  state.selectedOverloadIndex = null;
  resetMemberSectionState();
  state.spotlightOpen = false;
  state.spotlightQuery = "";
  state.spotlightIndex = 0;
  render();
}

function pickSpotlightMember(result) {
  const pkg = state.packages.find(item => item.id === result.pkg.id) || result.pkg;
  const type = pkg?.types?.find(item => item.id === result.type.id);
  if (!type) { closeSpotlight(); return; }
  state.package = pkg;
  state.atPackageRoot = false;
  state.selectedTypeId = type.id;
  state.lens = "api";
  state.selectedMemberKey = result.memberKey;
  state.selectedOverloadIndex = null;
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  state.spotlightOpen = false;
  state.spotlightQuery = "";
  state.spotlightIndex = 0;
  resetMemberSectionState();
  state.typeCursor = filteredTypes().findIndex(item => item.id === state.selectedTypeId);
  render();
  loadSelectedMemberDocumentation();
}

function pickSpotlight(pkgId, typeId) {
  const pkg = state.packages.find(item => item.id === pkgId) || state.package;
  const type = pkg?.types?.find(item => item.id === typeId);
  if (!type) {
    closeSpotlight();
    return;
  }
  state.package = pkg;
  state.atPackageRoot = false;
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

function spotlightScopeIndex() {
  return Math.max(0, SPOTLIGHT_SCOPES.findIndex(scope => scope.id === state.spotlightScope));
}

// Move the virtual chip cursor and live-apply the scope it lands on. Keeps DOM focus on
// the input so typing still routes here; only the chip row repaints.
function moveSpotlightChip(index) {
  state.spotlightChipIndex = index;
  setSpotlightScope(SPOTLIGHT_SCOPES[index].id);
}

function spotlightFocusInput() {
  state.spotlightFocus = "input";
  updateSpotlightChips();
  focusSpotlight();
}

// Repaint only the selected-row highlight over the already-rendered result rows. Crucially
// this does NOT recompute spotlightResults() (which runs a synchronous WASM type search and
// a full member scan), so holding an arrow key no longer floods the single-threaded main
// loop and stays smooth.
function highlightSpotlightSelection() {
  const container = document.querySelector("#spotlight-results");
  if (!container) return 0;
  const items = container.querySelectorAll(".spotlight-item");
  items.forEach((el, i) => {
    const selected = i === state.spotlightIndex;
    el.classList.toggle("selected", selected);
    el.setAttribute("aria-selected", selected ? "true" : "false");
  });
  items[state.spotlightIndex]?.scrollIntoView({ block: "nearest" });
  return items.length;
}

// Move the result selection by delta without wrapping. Returns false when the move would
// step above the first row (so the caller can hand focus back up to the chip row).
function moveSpotlightSelection(delta) {
  const container = document.querySelector("#spotlight-results");
  const count = container ? container.querySelectorAll(".spotlight-item").length : 0;
  if (!count) return false;
  const next = state.spotlightIndex + delta;
  if (next < 0) return false;
  state.spotlightIndex = Math.min(count - 1, next);
  highlightSpotlightSelection();
  return true;
}

function handleSpotlightKeys(event) {
  if (event.key === "Escape") {
    event.preventDefault();
    closeSpotlight();
    return;
  }
  if (event.key === "Tab") {
    event.preventDefault();
    const order = SPOTLIGHT_SCOPES.map(scope => scope.id);
    const current = order.indexOf(state.spotlightScope);
    const next = event.shiftKey ? (current - 1 + order.length) % order.length : (current + 1) % order.length;
    state.spotlightChipIndex = next;
    setSpotlightScope(order[next]);
    return;
  }

  // Chip-focus zone: arrows traverse the scope chips (live-applying scope), and step
  // back out to the text field or down into the results.
  if (state.spotlightFocus === "chips") {
    if (event.key === "ArrowRight") {
      event.preventDefault();
      if (state.spotlightChipIndex < SPOTLIGHT_SCOPES.length - 1) moveSpotlightChip(state.spotlightChipIndex + 1);
    } else if (event.key === "ArrowLeft") {
      event.preventDefault();
      if (state.spotlightChipIndex === 0) spotlightFocusInput();
      else moveSpotlightChip(state.spotlightChipIndex - 1);
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      spotlightFocusInput();
    } else if (event.key === "ArrowDown" || event.key === "Enter") {
      event.preventDefault();
      state.spotlightIndex = 0;
      spotlightFocusInput();
      highlightSpotlightSelection();
    }
    return;
  }

  // Text zone: Right at the caret's right edge hands focus to the chips; otherwise the
  // usual result navigation applies. Result computation is deferred to Enter so arrow
  // navigation never triggers the WASM type search.
  if (event.key === "ArrowRight") {
    const input = event.target;
    const atEnd = input.selectionStart === input.selectionEnd && input.selectionStart === input.value.length;
    if (atEnd) {
      event.preventDefault();
      state.spotlightFocus = "chips";
      state.spotlightChipIndex = spotlightScopeIndex();
      updateSpotlightChips();
    }
  } else if (event.key === "ArrowDown") {
    event.preventDefault();
    moveSpotlightSelection(1);
  } else if (event.key === "ArrowUp") {
    event.preventDefault();
    // At the top of the list, step back up to the chip row (which can then step up to
    // the search text) instead of wrapping around within the results.
    if (!moveSpotlightSelection(-1)) {
      state.spotlightFocus = "chips";
      state.spotlightChipIndex = spotlightScopeIndex();
      updateSpotlightChips();
    }
  } else if (event.key === "Enter") {
    event.preventDefault();
    pickSpotlightResult(spotlightResults()[state.spotlightIndex]);
  }
}

function handleCommandKeys(event) {
  const items = completions();
  if (event.key === "ArrowDown") {
    event.preventDefault();
    state.completionIndex = (state.completionIndex + 1) % Math.max(1, items.length);
    updateCommandSuggestions();
  } else if (event.key === "ArrowUp") {
    event.preventDefault();
    state.completionIndex = (state.completionIndex - 1 + Math.max(1, items.length)) % Math.max(1, items.length);
    updateCommandSuggestions();
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
  // We rewrote the command programmatically, so push it into the live input and
  // repaint only the suggestion list (no full render / caret reset needed elsewhere).
  const input = document.querySelector("#command");
  if (input) input.value = state.command;
  updateCommandSuggestions();
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
  // Only the target package id is clear text — it makes the link human-readable at a glance.
  // Everything else (version, framework, view, selection, and the full open-tab set) rides in
  // the opaque share packet, so the visible query stays ?package=<id>&w=<packet>.
  params.set("package", state.package.id);
  params.set("w", encodeShareState());
  url.search = params.toString();
  url.hash = "";
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

function showToast(message, duration = 2200) {
  document.querySelector(".toast")?.remove();
  const toast = document.createElement("div");
  toast.className = "toast";
  toast.textContent = message;
  document.body.append(toast);
  setTimeout(() => toast.remove(), duration);
}

// Turns a raw inspection failure into a friendly, actionable message. A mistyped package
// name surfaces as a NuGet 404; call that out plainly instead of showing a stack trace.
function friendlyLoadError(error, packageId, version) {
  const raw = String(error?.message || error || "");
  if (/\b404\b|not\s*found/i.test(raw)) {
    const suffix = version && version !== "latest" ? `@${version}` : "";
    return {
      notFound: true,
      title: "Package not found",
      message: `Package “${packageId}${suffix}” wasn’t found on NuGet. Check the spelling — names are case-insensitive — and try again.`
    };
  }
  return {
    notFound: false,
    title: "Inspection query failed",
    message: `Couldn’t load “${packageId}”: ${raw || "unknown error"}`
  };
}

async function copyText(value, confirmation) {
  try {
    await navigator.clipboard.writeText(value);
    showToast(confirmation);
  } catch {
    showToast("clipboard access was denied");
  }
}

// The intro/home page shown on a bare visit: what the tool is, a persistent Spotlight-style
// search, and a few demo entry points. The search reuses the Spotlight machinery in place
// (shared #spotlight-input / #spotlight-chips / #spotlight-results ids), so results, scope
// chips, NuGet discovery, and result picking all behave exactly like the modal Spotlight.
function renderHomeView() {
  const results = spotlightResults();
  state.spotlightIndex = Math.min(state.spotlightIndex, Math.max(results.length - 1, 0));
  app.innerHTML = `
    <div class="home">
      <header class="home-bar">
        <a class="brand" href="/" aria-label="dotnet inspect home"><span class="brand-glyph">◇</span><span>dotnet-inspect</span></a>
        <div class="home-bar-actions">
          <a class="home-link" href="https://github.com/richlander/dotnet-inspect" target="_blank" rel="noreferrer">GitHub</a>
          <button id="home-theme" aria-label="Switch theme">${state.theme === "dark" ? "light" : "dark"}</button>
        </div>
      </header>
      <main class="home-hero">
        <div class="home-copy">
          <p class="home-kicker">Browser-native · WebAssembly · zero install</p>
          <h1 class="home-title">Inspect any .NET package, right in the browser.</h1>
          <p class="home-lede">Explore NuGet packages and the .NET platform — types, members, public API surface, dependencies, call graphs, and decompiled C# — all computed locally in your browser. Nothing to install, nothing uploaded.</p>
          <div class="home-search" role="search">
            <div class="home-search-box">
              <span class="spotlight-glyph">⌕</span>
              <input id="spotlight-input" value="${escapeHtml(state.spotlightQuery)}" placeholder="Search NuGet — a package, type, or member…" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="spotlight-results" />
            </div>
            <div class="spotlight-chips" id="spotlight-chips">${spotlightChipsHtml()}</div>
            <div class="spotlight-results home-results" id="spotlight-results" role="listbox">${spotlightResultsHtml(results)}</div>
          </div>
          <div class="home-demos">
            <span class="home-demos-label">Or jump straight into a demo</span>
            <div class="home-demo-row">
              <button class="home-demo" data-home-demo="stj"><strong>System.Text.Json</strong><small>Browse a real package API</small></button>
              <button class="home-demo" data-home-demo="callgraph"><strong>Cross-package call graph</strong><small>Trace calls across four packages</small></button>
              <button class="home-demo" data-home-demo="runtime"><strong>The .NET runtime</strong><small>Inspect platform BCL types</small></button>
            </div>
          </div>
        </div>
        <aside class="home-art">${homeArtSvg()}</aside>
      </main>
      <footer class="home-foot">
        <span class="ready-dot"></span><span>browser wasm ready</span>
        ${state.diag ? `<span class="diag">⚙ ready in ${fmtMs(state.diag.totalMs)}</span>` : ""}
      </footer>
    </div>`;
  bindHomeEvents();
}

// The hero mascot: dotnet-bot inspecting through a magnifying glass (official dotnet/brand
// character, CC0). Rendered as a plain <img> so it scales crisply and keeps its transparent
// background on either theme; the .home-art frame reserves and centers the slot.
function homeArtSvg() {
  return `<img class="home-art-img" src="/assets/dotnet-inspect-bot.png" width="680" height="680" alt="dotnet-bot inspecting through a magnifying glass" />`;
}

function bindHomeEvents() {
  document.querySelector("#home-theme")?.addEventListener("click", toggleTheme);
  const input = document.querySelector("#spotlight-input");
  if (input) {
    input.addEventListener("input", event => {
      state.spotlightQuery = event.target.value;
      state.spotlightIndex = 0;
      scheduleSpotlightPackageFetch();
      updateSpotlightResults();
    });
    input.addEventListener("keydown", event => {
      const results = spotlightResults();
      if (event.key === "ArrowDown") {
        event.preventDefault();
        state.spotlightIndex = Math.min(state.spotlightIndex + 1, Math.max(results.length - 1, 0));
        updateSpotlightResults();
      } else if (event.key === "ArrowUp") {
        event.preventDefault();
        state.spotlightIndex = Math.max(state.spotlightIndex - 1, 0);
        updateSpotlightResults();
      } else if (event.key === "Enter") {
        event.preventDefault();
        pickSpotlightResult(results[state.spotlightIndex]);
      }
    });
  }
  bindSpotlightChipClicks(document.querySelector("#spotlight-chips"));
  bindSpotlightResultClicks(document.querySelector("#spotlight-results"));
  document.querySelectorAll("[data-home-demo]").forEach(button =>
    button.addEventListener("click", () => runHomeDemo(button.dataset.homeDemo)));
  requestAnimationFrame(() => document.querySelector("#spotlight-input")?.focus());
}

function runHomeDemo(kind) {
  state.home = false;
  if (kind === "stj") loadPackage("System.Text.Json", "10.0.0", "net10.0");
  else if (kind === "callgraph") runCallGraphDemo();
  else if (kind === "runtime") openRuntimePackFromHome();
}

// Return to the intro/home page without tearing down the warm engine or the loaded packages.
// Soft in-app navigation (pushState "/") so a refresh stays on home and Back returns to the
// workbench; the home search reuses the still-resident package list.
function goHome() {
  state.home = true;
  state.spotlightOpen = false;
  state.spotlightQuery = "";
  state.spotlightIndex = 0;
  state.spotlightScope = "all";
  state.spotlightFocus = "input";
  state.spotlightChipIndex = 0;
  state.spotlightPkgHits = [];
  state.spotlightPkgLoading = false;
  state.spotlightPkgQuery = "";
  try { history.pushState(null, "", "/"); } catch {}
  render();
}

// Loads the resident runtime pack and lands on its package Overview (the runtime pack has no
// nupkg, so this goes through loadRuntimePack rather than loadPackage).
async function openRuntimePackFromHome() {
  state.home = false;
  state.loading = true;
  state.error = "";
  state.loadingMessage = "Loading the .NET runtime pack…";
  render();
  const pack = await loadRuntimePack("net10.0");
  if (!pack) {
    state.loading = false;
    state.error = "Couldn’t load the .NET runtime pack. Retry, or open a different package.";
    state.errorTitle = "Runtime pack failed";
    render();
    return;
  }
  state.package = pack;
  state.atPackageRoot = true;
  state.packageLens = "overview";
  state.selectedTypeId = pack.types[0]?.id || "";
  state.selectedMemberKey = "";
  state.selectedOverloadIndex = null;
  state.loading = false;
  render();
  loadSelectionData();
}

function renderLoading() {
  app.innerHTML = `
    <div class="loading-screen">
      <div class="loading-brand"><span>◇</span> dotnet-inspect</div>
      ${state.error
        ? `<div class="load-error">
             <strong>${escapeHtml(state.errorTitle || "Inspection query failed")}</strong>
             <p class="load-error-message">${escapeHtml(state.error)}</p>
             <form class="load-error-query" id="error-package-query">
               <input id="error-package-input" placeholder="Package or Package@version" aria-label="Open a different NuGet package" autocomplete="off" spellcheck="false" value="${escapeHtml(state.requestedPackage || "")}" />
               <button type="submit">open</button>
             </form>
             <div class="load-error-actions">
               <button id="retry-load" type="button">retry</button>
               ${state.errorDetail ? `<button id="toggle-error-detail" type="button">details</button>` : ""}
             </div>
             ${state.errorDetail ? `<pre class="load-error-detail" hidden>${escapeHtml(state.errorDetail)}</pre>` : ""}
           </div>`
        : `<div class="load-progress"><span class="loader"></span><strong>${escapeHtml(state.loadingMessage)}</strong><small>${escapeHtml(state.requestedPackage)}@${escapeHtml(state.requestedVersion)} · ${escapeHtml(state.requestedFramework || "best framework")}</small></div>`}
    </div>`;
  document.querySelector("#retry-load")?.addEventListener("click", bootstrap);
  document.querySelector("#error-package-query")?.addEventListener("submit", event => {
    event.preventDefault();
    const value = document.querySelector("#error-package-input").value.trim();
    const separator = value.lastIndexOf("@");
    if (!value || separator === value.length - 1) return;
    const packageId = separator > 0 ? value.slice(0, separator) : value;
    const version = separator > 0 ? value.slice(separator + 1) : "latest";
    loadPackage(packageId, version, "");
  });
  document.querySelector("#toggle-error-detail")?.addEventListener("click", () => {
    const pre = document.querySelector(".load-error-detail");
    if (pre) pre.hidden = !pre.hidden;
  });
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

  // The runtime pseudo-package has no companion XML-documentation nupkg on nuget.org, so a
  // doc fetch would 404. Skip it (rendering once) rather than firing a late async render()
  // that would wipe an in-progress call-graph diagram back to its placeholder.
  if (state.package?.isRuntimePack) {
    overload.documentationLoaded = true;
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
// Projects the current package and its transitive dependency neighbourhood into a
// call-graph-style Mermaid flowchart. Walks up to three levels of callees (from cached
// dependency manifests) and three levels of callers (open packages that transitively
// depend on the centre). Because only opened packages have cached manifests, the graph
// grows as the user clicks around and opens more of the neighbourhood.
function buildDependencyGraphMermaid(selectedTfm) {
  const MAX_DEPTH = 3;
  const MAX_NODES = 80;
  const centerId = state.package.id;
  const centerKey = centerId.toLowerCase();
  const openById = new Map(state.packages.map(item => [item.id.toLowerCase(), item]));

  const nodeInfo = new Map();
  const ensureNode = (id, versionRange) => {
    const key = id.toLowerCase();
    if (!nodeInfo.has(key)) {
      const open = openById.get(key);
      const kind = key === centerKey ? "self" : (open ? "open" : "external");
      nodeInfo.set(key, { id, kind, versionRange: versionRange || "" });
    }
    return nodeInfo.get(key);
  };
  ensureNode(centerId);

  const edgeSet = new Set();
  const edges = [];
  const addEdge = (fromId, toId) => {
    const key = `${fromId.toLowerCase()}\u0000${toId.toLowerCase()}`;
    if (edgeSet.has(key)) return;
    edgeSet.add(key);
    edges.push({ from: fromId.toLowerCase(), to: toId.toLowerCase() });
  };

  const groupFor = (id, version) => {
    let groups = state.workspaceDependencies[`${id.toLowerCase()}@${String(version).toLowerCase()}`];
    if (!groups && id.toLowerCase() === centerKey) groups = state.packageDependencies?.dependencyGroups;
    if (!groups) return null;
    return groups.find(group => group.framework === selectedTfm)
      || groups.find(group => group.isActive)
      || groups[0];
  };

  // Callees: walk the centre's dependencies, expanding any dependency that is itself an
  // open package (only open packages have cached manifests to walk further).
  let downFrontier = [{ id: centerId, version: state.package.version }];
  const downVisited = new Set([centerKey]);
  for (let depth = 0; depth < MAX_DEPTH && downFrontier.length && nodeInfo.size < MAX_NODES; depth++) {
    const next = [];
    for (const node of downFrontier) {
      const group = groupFor(node.id, node.version);
      if (!group) continue;
      for (const dependency of group.dependencies || []) {
        ensureNode(dependency.id, dependency.versionRange);
        addEdge(node.id, dependency.id);
        const depKey = dependency.id.toLowerCase();
        if (!downVisited.has(depKey)) {
          downVisited.add(depKey);
          const open = openById.get(depKey);
          if (open) next.push({ id: open.id, version: open.version });
        }
      }
    }
    downFrontier = next;
  }

  // Callers: open packages that (transitively) declare a dependency on a frontier node.
  let upFrontier = [centerId];
  const upVisited = new Set([centerKey]);
  for (let depth = 0; depth < MAX_DEPTH && upFrontier.length && nodeInfo.size < MAX_NODES; depth++) {
    const next = [];
    for (const targetId of upFrontier) {
      const targetKey = targetId.toLowerCase();
      for (const pkg of state.packages) {
        const pkgKey = pkg.id.toLowerCase();
        if (pkgKey === targetKey) continue;
        const group = groupFor(pkg.id, pkg.version);
        if (!group) continue;
        if ((group.dependencies || []).some(dependency => dependency.id.toLowerCase() === targetKey)) {
          ensureNode(pkg.id);
          addEdge(pkg.id, targetId);
          if (!upVisited.has(pkgKey)) {
            upVisited.add(pkgKey);
            next.push(pkg.id);
          }
        }
      }
    }
    upFrontier = next;
  }

  if (!edges.length) return null;

  const keys = [...nodeInfo.keys()];
  const idOf = new Map();
  keys.forEach((key, index) => idOf.set(key, `d${index}`));
  const lines = ["flowchart TD"];
  for (const key of keys) {
    const info = nodeInfo.get(key);
    const label = info.id.replace(/"/g, "&quot;");
    lines.push(`  ${idOf.get(key)}["${label}"]:::${info.kind}`);
  }
  for (const edge of edges) {
    lines.push(`  ${idOf.get(edge.from)} --> ${idOf.get(edge.to)}`);
  }
  lines.push("classDef self fill:var(--accent-soft),stroke:var(--accent),color:var(--text),stroke-width:2px;");
  lines.push("classDef open fill:var(--panel-active),stroke:var(--blue),color:var(--text);");
  lines.push("classDef external fill:transparent,stroke:var(--line-strong),color:var(--dim);");
  const nodeInfoByLabel = new Map([...nodeInfo.values()].map(info => [info.id, info]));
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
    delete container.dataset.graphPending;
    container.innerHTML = '<p class="graph-empty">No connected packages for this framework. Open a package that depends on this one to see caller edges.</p>';
    return;
  }
  // Already showing exactly this graph — nothing to do.
  if (container.dataset.graphDef === built.definition && container.querySelector(".graph-viewport")) return;
  // A render for this exact definition is already in flight on this container; let it finish.
  // (renderDependencyGraph is invoked repeatedly per render cycle — from both
  // maybeAutoLoadPackageDependencies and ensureWorkspaceDependencies — so without this guard
  // two concurrent mermaid.render calls race and one's catch can clobber the other's graph.)
  if (container.dataset.graphPending === built.definition) return;
  container.dataset.graphPending = built.definition;
  const seq = ++depGraphRenderSeq;
  try {
    mermaidModule ??= import("https://cdn.jsdelivr.net/npm/mermaid@11.15.0/dist/mermaid.esm.min.mjs");
    const { default: mermaid } = await mermaidModule;
    if (seq !== depGraphRenderSeq) return;
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: "strict",
      theme: state.theme === "light" ? "default" : "dark",
      themeVariables: { fontSize: "16px" },
      flowchart: { htmlLabels: false, curve: "basis" }
    });
    const id = `dep-graph-${seq.toString(36)}-${Date.now().toString(36)}`;
    const rootStyle = getComputedStyle(document.documentElement);
    const resolved = built.definition.replace(
      /var\((--[\w-]+)\)/g,
      (whole, name) => rootStyle.getPropertyValue(name).trim() || whole
    );
    const { svg } = await mermaid.render(id, resolved);
    // A newer render superseded this one, or the container was swapped out — bail without touching the DOM.
    if (seq !== depGraphRenderSeq) return;
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
    container.dataset.graphDef = built.definition;
    attachGraphPanZoom(container, viewport);
    viewport.querySelectorAll("g.node").forEach(node => {
      const label = (node.textContent || "").replace(/\s+/g, " ").trim();
      const info = built.nodeInfoByLabel.get(label);
      if (!info || info.kind === "self") return;
      node.classList.add("nav-node");
      node.style.cursor = "pointer";
      node.addEventListener("click", () => {
        if (info.kind === "open") switchToPackageForDependencies(info.id);
        else openDependencyPackage(info.id, info.versionRange);
      });
    });
  } catch (error) {
    // Only surface the error if this is still the latest render and nothing else has drawn a graph.
    if (seq === depGraphRenderSeq
      && document.querySelector("#dependency-graph-diagram") === container
      && !container.querySelector(".graph-viewport")) {
      container.dataset.graphDef = "";
      container.innerHTML = `<div class="graph-render-error"><strong>Diagram rendering failed</strong><p>${escapeHtml(String(error?.message || error))}</p></div>`;
    }
  } finally {
    if (container.dataset.graphPending === built.definition) delete container.dataset.graphPending;
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

// Extracts a concrete version to load from a NuGet dependency range. Ranges are usually a
// bare minimum ("10.0.10", meaning >=), sometimes bracketed ("[10.0.0, )"); pull the first
// version token and fall back to "latest" when it can't be parsed.
function dependencyVersion(range) {
  if (!range) return "latest";
  const match = String(range).match(/\d+(?:\.\d+)+(?:-[0-9A-Za-z.-]+)?/);
  return match ? match[0] : "latest";
}

async function openDependencyPackage(packageId, versionRange) {
  const existing = state.packages.find(item => item.id.toLowerCase() === packageId.toLowerCase());
  if (existing) {
    switchToPackageForDependencies(existing.id);
    return;
  }
  const model = await loadPackage(packageId, dependencyVersion(versionRange), "");
  if (!model) return;
  state.atPackageRoot = true;
  state.packageLens = "dependencies";
  state.dependenciesFramework = "";
  render();
}

function nextPaint() {
  // Resolve after the browser has had a chance to lay out and paint the current DOM.
  return new Promise(resolve =>
    requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(resolve, 0))));
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

  // A resident runtime pack has no NuGet workspace to scan for callers; its members'
  // implementation lives in the range-fetched platform assembly. Route them through the
  // same platform-descent path the BCL call-graph nodes use so the graph resolves.
  if (state.package?.isRuntimePack) {
    await loadRuntimeMemberCallGraph(type, overload);
    return;
  }

  // Progressive, two-stage load so live data prints quickly even with many libraries open.
  // Stage 1 (fast) scopes the query to the target assembly only — that yields the callees and
  // the intra-library callers without downloading/opening any other package. Stage 2 (slow)
  // re-runs across the full workspace to add cross-library callers, then re-renders (the
  // "flash"). A sequence token drops results once the member/overload selection has moved on.
  const seq = ++state.memberCallGraphSeq;
  // A fresh workspace graph invalidates any in-progress platform descent.
  state.platformStack = [];
  state.platformDrillLoading = false;
  state.platformDrillError = "";
  const base = {
    packageId: state.package.id,
    version: state.package.version,
    framework: state.package.activeFramework,
    assembly: type.assembly,
    type: type.id,
    member: overload.name,
    signature: overload.signature
  };
  const hasOtherLibraries = state.packages.length > 1;

  state.memberCallGraphLoading = true;
  state.memberCallGraphExpanding = false;
  state.memberCallGraphError = "";
  render();
  try {
    const local = await inspectMemberCallGraph({ ...base, workspace: [] });
    if (seq !== state.memberCallGraphSeq) return;
    state.memberCallGraph = local;
    state.memberCallGraphLoading = false;
    state.memberCallGraphExpanding = hasOtherLibraries;
    render();
    await renderMermaidCallGraph();

    if (hasOtherLibraries) {
      // The engine runs synchronously on the main thread, so yield a paint frame first —
      // otherwise the stage-1 graph never appears before the blocking cross-library pass.
      await nextPaint();
      if (seq !== state.memberCallGraphSeq) return;
      const full = await inspectMemberCallGraph({
        ...base,
        workspace: state.packages.map(packageItem => ({
          package: packageItem.id,
          version: packageItem.version,
          framework: packageItem.activeFramework
        }))
      });
      if (seq !== state.memberCallGraphSeq) return;
      const previousMermaid = state.memberCallGraph?.mermaid;
      state.memberCallGraph = full;
      state.memberCallGraphExpanding = false;
      refreshPackageStats();
      patchCallGraphSection(previousMermaid);
    }
  } catch (error) {
    if (seq !== state.memberCallGraphSeq) return;
    state.memberCallGraphLoading = false;
    state.memberCallGraphExpanding = false;
    if (state.memberCallGraph) {
      // Stage 1 already produced a graph; drop the banner in place rather than
      // clobbering the page with an error or a full re-render.
      patchCallGraphSection(state.memberCallGraph.mermaid);
    } else {
      state.memberCallGraphError = String(error?.message || error);
      render();
    }
  }
}

// Builds a runtime-pack member's call graph via the platform-descent engine export (which
// range-fetches the owning platform assembly) rather than the workspace call-graph path.
// The result is itself a platform graph, so its callees descend further (see the
// isRuntimePack branch in the node-binding block).
async function loadRuntimeMemberCallGraph(type, overload) {
  const seq = ++state.memberCallGraphSeq;
  state.platformStack = [];
  state.platformDrillLoading = false;
  state.platformDrillError = "";
  state.memberCallGraphLoading = true;
  state.memberCallGraphExpanding = false;
  state.memberCallGraphError = "";
  render();
  try {
    const paramSig = (overload.parameters ?? []).map(parameter => parameter.type).join(",");
    const graph = await inspectExpandPlatformCallGraph({
      framework: state.package.activeFramework,
      assembly: type.assembly,
      type: type.id,
      member: overload.name,
      paramSig
    });
    if (seq !== state.memberCallGraphSeq) return;
    state.memberCallGraph = graph;
    state.memberCallGraphLoading = false;
    state.memberCallGraphExpanding = false;
    render();
    await renderMermaidCallGraph();
  } catch (error) {
    if (seq !== state.memberCallGraphSeq) return;
    state.memberCallGraphLoading = false;
    state.memberCallGraphExpanding = false;
    state.memberCallGraphError = String(error?.message || error);
    render();
  }
}

// Update just the call-graph section in place so the stage-2 result doesn't flash
// the whole page. Leaves the stage-1 diagram untouched unless the graph changed.
function patchCallGraphSection(previousMermaid) {
  const section = document.querySelector(".call-graph-section");
  if (!section) return; // not on the call-graph view; state is cached for re-entry.
  const graph = state.memberCallGraph;
  const callers = graph?.callers?.children ?? [];
  const callees = graph?.callees?.children ?? [];
  const scope = graph?.scope;
  const countSpan = section.querySelector(".section-title span");
  if (countSpan) {
    countSpan.textContent =
      `${callers.length} caller${callers.length === 1 ? "" : "s"} · ${callees.length} callee${callees.length === 1 ? "" : "s"}`;
  }
  section.querySelector(".graph-expanding")?.remove();
  const scopeEl = section.querySelector(".graph-scope");
  if (scopeEl && scope) {
    scopeEl.innerHTML =
      `<strong>Workspace callers</strong><span>${scope.packages} loaded packages · ${scope.callerAssemblies} scanned assemblies</span><strong>Callees</strong><span>${escapeHtml(scope.calleeScope)} · depth 2</span>`;
  }
  const sourceCode = section.querySelector(".graph-mermaid pre code");
  if (sourceCode) sourceCode.textContent = graph?.mermaid ?? "";
  if (graph?.mermaid && graph.mermaid !== previousMermaid) renderMermaidCallGraph();
}

async function renderMermaidCallGraph() {
  const container = document.querySelector("#call-graph-diagram");
  const active = currentCallGraph();
  if (!container || !active?.mermaid) return;
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
    const definition = active.mermaid.replace(
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
    // Inside a platform descent the whole graph lives in the runtime pack, not the
    // workspace, so a clicked callee must resolve against the active platform graph
    // and descend further — routing it through the workspace resolvers would look the
    // type up in the loaded package and fail (e.g. "Type 'TextWriter' is not in Markout.dll").
    // A resident runtime pack's base member graph is itself a platform graph, so its
    // callees descend the same way from the start.
    const drilled = state.platformStack.length > 0 || Boolean(state.package?.isRuntimePack);
    svg.querySelectorAll("g.node").forEach(node => {
      const label = (node.textContent || "").replace(/\s+/g, " ").trim();
      if (drilled) {
        const deeper = resolvePlatformNode(label, { requireExternal: false });
        if (!deeper) return;
        node.classList.add("nav-node", "platform-node");
        node.style.cursor = "pointer";
        node.addEventListener("click", () => {
          if (moved) return;
          navigateOrDrillPlatform(deeper);
        });
        return;
      }
      const target = resolveNodeLabel(label);
      const source = target ? null : resolveNodeForSource(label, node.classList.contains("differentAssembly"));
      // A node with no workspace target and no source is a platform (BCL / cross-library)
      // callee: resolvable identity lives on the active graph's tree, so we can descend
      // into its implementation IL by range-fetching the owning assembly on demand.
      const platform = (target || source) ? null : resolvePlatformNode(label);
      if (!target && !source && !platform) return;
      node.classList.add("nav-node");
      if (platform) node.classList.add("platform-node");
      node.style.cursor = "pointer";
      node.addEventListener("click", () => {
        if (moved) return;
        if (target) navigateToMember(target.pkg, target.type, target.group);
        else if (source) openGraphSource(source.request, source.title);
        else navigateOrDrillPlatform(platform);
      });
    });
  }

  fit();
}

function currentCallGraph() {
  return state.platformStack.length > 0
    ? state.platformStack[state.platformStack.length - 1].graph
    : state.memberCallGraph;
}

function platformCrumbTrail() {
  const root = state.memberCallGraph?.callees?.label
    ? state.memberCallGraph.callees.label.replace(/\(.*$/, "")
    : "member";
  return [root, ...state.platformStack.map(entry => entry.title)].join(" › ");
}

// Walk the active graph's caller + callee trees into a flat node list so a clicked
// SVG node (matched by its compact "Type.Member" label) can recover the structured
// identity the engine attached (assembly, typeFullName, memberName, paramSig).
function flattenGraphNodes(graph) {
  const out = [];
  const visit = node => {
    if (!node) return;
    out.push(node);
    (node.children ?? []).forEach(visit);
  };
  visit(graph?.callers);
  visit(graph?.callees);
  return out;
}

function resolvePlatformNode(label, { requireExternal = true } = {}) {
  const dot = label.lastIndexOf(".");
  if (dot < 0) return null;
  let typeName = label.slice(0, dot);
  const memberName = label.slice(dot + 1);
  if (typeName.endsWith(".")) typeName = typeName.slice(0, -1);
  if (!typeName || !memberName) return null;
  const wantType = stripArity(typeName);
  const graph = currentCallGraph();
  // When descending inside an already-platform graph, skip the graph's own root nodes so
  // clicking a callee moves deeper rather than re-pushing the current member.
  const roots = new Set([graph?.callers, graph?.callees]);
  for (const node of flattenGraphNodes(graph)) {
    if (requireExternal) {
      if (node.status !== "External") continue;
    } else if (roots.has(node)) {
      continue;
    }
    if (!node.typeFullName || !node.assembly) continue;
    if (node.memberName !== memberName) continue;
    const simple = stripArity(node.typeFullName.split(".").pop() ?? "");
    if (simple === wantType) return node;
  }
  return null;
}

async function drillPlatformNode(node) {
  if (state.platformDrillLoading) return;
  state.platformDrillLoading = true;
  state.platformDrillError = "";
  render();
  try {
    const graph = await inspectExpandPlatformCallGraph({
      framework: state.package.activeFramework,
      assembly: node.assembly,
      type: node.typeFullName,
      member: node.memberName,
      paramSig: node.paramSig ?? ""
    });
    state.platformStack.push({
      graph,
      title: `${stripArity(node.typeFullName.split(".").pop() ?? "")}.${node.memberName}`
    });
    state.platformDrillLoading = false;
    render();
    await renderMermaidCallGraph();
  } catch (error) {
    state.platformDrillLoading = false;
    state.platformDrillError =
      `Could not descend into ${node.typeFullName}.${node.memberName}: ${String(error?.message || error)}`;
    render();
    await renderMermaidCallGraph();
  }
}

function popPlatformDrill() {
  if (state.platformStack.length === 0) return;
  state.platformStack.pop();
  state.platformDrillError = "";
  render();
  renderMermaidCallGraph();
}

// A clicked platform (BCL) call-graph node should land the user *inside* the resident
// runtime pack at that member — a first-class, refreshable location with its own header,
// member list, breadcrumb, and URL — rather than an in-place descent that stays pinned to
// the workspace package. The runtime pack is loaded on demand (its System.Private.CoreLib
// types are resident); if the clicked type lives in a not-yet-resident sibling assembly we
// fall back to the lightweight in-place descent so the callees still appear.
async function navigateOrDrillPlatform(node) {
  if (state.platformDrillLoading) return;
  const framework = state.package?.activeFramework || "";
  let pack = runtimePackPackage();
  if (!pack) {
    state.platformDrillLoading = true;
    state.platformDrillError = "";
    render();
    pack = await loadRuntimePack(framework);
    state.platformDrillLoading = false;
    if (!pack) {
      state.platformDrillError = state.runtimePackError || "Could not load the .NET runtime pack.";
      render();
      await renderMermaidCallGraph();
      return;
    }
  }
  const selection = findRuntimeMemberSelection(pack, node);
  if (!selection) {
    await drillPlatformNode(node);
    return;
  }
  navigateToRuntimeMember(pack, selection.type, selection.group, selection.overloadIndex);
}

// Enter the resident runtime pack focused on one member's call graph. Mirrors
// navigateToMember but targets the call-graph section (the reason the user clicked a graph
// node) and clears any active platform descent so the new member's graph loads fresh.
function navigateToRuntimeMember(pack, type, group, overloadIndex) {
  state.package = pack;
  state.atPackageRoot = false;
  state.lens = "api";
  state.selectedTypeId = type.id;
  state.selectedMemberKey = group.key;
  state.selectedOverloadIndex = overloadIndex ?? 0;
  state.memberSection = "call-graph";
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  state.platformStack = [];
  state.platformDrillLoading = false;
  state.platformDrillError = "";
  state.memberSource = null;
  state.memberSourceError = "";
  state.memberCallGraph = null;
  state.memberCallGraphError = "";
  state.memberCallGraphExpanding = false;
  state.memberFacts = null;
  state.memberFactsError = "";
  state.memberAnnotated = null;
  state.memberAnnotatedError = "";
  state.typeCursor = Math.max(0, filteredTypes().findIndex(item => item.id === type.id));
  loadSelectedMemberCallGraph();
}

// Resolve a platform call-graph node's structured identity (typeFullName / memberName /
// paramSig) to a concrete type + member group + overload in the resident runtime pack.
// Overload disambiguation mirrors the engine's SelectPlatformMember: match by name, then by
// parameter arity, preferring an exact simplified-type-name match as a tie-breaker. Returns
// null when the type isn't resident so the caller can fall back to an in-place descent.
function findRuntimeMemberSelection(pack, node) {
  if (!pack || !node?.typeFullName) return null;
  const type = pack.types.find(item => item.id === node.typeFullName);
  if (!type) return null;
  const named = memberGroups(type).filter(group => group.name === node.memberName);
  if (!named.length) return null;
  const want = paramNamesFromSig(node.paramSig);
  let arityMatch = null;
  for (const group of named) {
    for (let i = 0; i < group.overloads.length; i++) {
      const params = group.overloads[i].parameters ?? [];
      if (params.length !== want.length) continue;
      if (!arityMatch) arityMatch = { type, group, overloadIndex: i };
      if (params.every((parameter, idx) => simpleTypeName(parameter.type) === want[idx])) {
        return { type, group, overloadIndex: i };
      }
    }
  }
  return arityMatch ?? { type, group: named[0], overloadIndex: 0 };
}

function paramNamesFromSig(sig) {
  return sig
    ? String(sig).split(",").map(part => part.trim()).filter(Boolean).map(simpleTypeName)
    : [];
}

// Client-side mirror of the engine's SimpleTypeName: strip namespace, generic arguments,
// arity, and the nullable-reference annotation so overload matching survives the display-
// vs metadata-name gap (e.g. a callee's "string" against an overload's "string?").
function simpleTypeName(type) {
  if (!type) return "";
  let name = String(type).trim();
  const generic = name.indexOf("<");
  if (generic >= 0) name = name.slice(0, generic);
  const array = name.indexOf("[");
  const suffix = array >= 0 ? name.slice(array) : "";
  if (array >= 0) name = name.slice(0, array);
  const tick = name.indexOf("`");
  if (tick >= 0) name = name.slice(0, tick);
  const dot = name.lastIndexOf(".");
  if (dot >= 0) name = name.slice(dot + 1);
  return (name.replace(/\?+$/, "") + suffix.replace(/\?+$/, "")).toLowerCase();
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
  const prevPackage = state.package;
  const prevRequested = {
    package: state.requestedPackage,
    version: state.requestedVersion,
    framework: state.requestedFramework
  };
  state.loading = true;
  state.error = "";
  state.home = false;
  state.queryNotice = "";
  state.requestedPackage = packageId;
  state.requestedVersion = version;
  state.requestedFramework = framework;
  state.loadingMessage = `Querying ${packageId}@${version}…`;
  render();

  try {
    const result = await inspectPackage(packageId, version, framework);
    refreshPackageStats();
    const types = (result.types ?? []).map(type => ({
      ...type,
      api: type.api ?? []
    }));
    const packageModel = {
      id: result.package,
      version: result.version,
      frameworks: (result.frameworks ?? []).slice().sort(compareFrameworks),
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
    const friendly = friendlyLoadError(error, packageId, version);
    if (prevPackage) {
      // A failed *new* query must not blow away an already-open workbench and trap the user
      // on a full-screen error. Keep them in their current package and restore the requested
      // identity (so URL/retry stay pinned to the good package); surface a persistent,
      // dismissible notice banner so the failure is clearly explained, not silent.
      state.package = prevPackage;
      state.requestedPackage = prevRequested.package;
      state.requestedVersion = prevRequested.version;
      state.requestedFramework = prevRequested.framework;
      state.error = "";
      state.queryNotice = friendly.message;
      render();
    } else {
      state.error = friendly.message;
      state.errorTitle = friendly.title;
      state.errorDetail = String(error?.stack || error);
      render();
    }
    return null;
  }
}

function runtimePackLoaded() {
  return state.packages.some(item => item.isRuntimePack);
}

function runtimePackPackage() {
  return state.packages.find(item => item.isRuntimePack) || null;
}

// The resident runtime pseudo-package rides in the shared workspace/URL packet under the
// display id "Microsoft.NETCore.App", but it has no NuGet nupkg — restoring it means
// re-running LoadRuntimePack (per TFM), not GetPackageBytesAsync. This id test lets the
// restore path route it correctly instead of 404-ing on a nupkg fetch.
function isRuntimePackId(id) {
  return String(id || "").toLowerCase() === "microsoft.netcore.app";
}

// Loads the platform runtime pack (System.Private.CoreLib for the given TFM) and adds it as
// a resident pseudo-package flagged isRuntimePack, so its BCL types become searchable in
// Spotlight and browsable/navigable like any package. SPC is fetched eagerly; sibling pack
// assemblies load lazily as navigation reaches them. Does not switch the active package.
async function loadRuntimePack(framework) {
  if (state.runtimePackLoading) return runtimePackPackage();
  const existing = runtimePackPackage();
  if (existing) return existing;
  state.runtimePackLoading = true;
  state.runtimePackError = "";
  try {
    const result = await inspectLoadRuntimePack(framework || "");
    refreshPackageStats();
    const types = (result.types ?? []).map(type => ({ ...type, api: type.api ?? [] }));
    const packageModel = {
      id: result.package,
      version: result.version,
      frameworks: (result.frameworks ?? []).slice().sort(compareFrameworks),
      activeFramework: result.activeFramework,
      assembly: (result.assemblies ?? []).map(item => item.name).join(", "),
      assemblies: result.assemblies ?? [],
      types,
      totalTypes: types.length,
      totalMembers: result.totalMembers,
      isRuntimePack: true
    };
    const at = state.packages.findIndex(item =>
      item.id.toLowerCase() === packageModel.id.toLowerCase()
      && item.version.toLowerCase() === packageModel.version.toLowerCase());
    if (at >= 0) state.packages[at] = packageModel;
    else state.packages.push(packageModel);
    state.runtimePackLoading = false;
    return packageModel;
  } catch (error) {
    state.runtimePackLoading = false;
    state.runtimePackError = String(error?.message || error);
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

// Restores the full open-tab set from the opaque workspace bucket (or just the visible
// target for a lone/legacy link), loading each tab in order so the tab bar and any
// cross-package dependency edges come back. Only the focused target restores its deep-link.
async function restoreInitialWorkspace() {
  const target = {
    id: state.requestedPackage,
    version: state.requestedVersion,
    framework: state.requestedFramework
  };
  const tabs = (initialLocation.tabs && initialLocation.tabs.length)
    ? initialLocation.tabs.slice()
    : [target];
  const matchesTarget = tab =>
    isRuntimePackId(tab.id)
      ? isRuntimePackId(target.id)
      : (tab.id.toLowerCase() === target.id.toLowerCase()
        && String(tab.version).toLowerCase() === String(target.version).toLowerCase());
  if (!tabs.some(matchesTarget)) tabs.push(target);

  const savedDeep = pendingDeepLink;
  pendingDeepLink = null;
  for (const tab of tabs) {
    // The runtime pack has no nupkg; rebuild it from its TFM so a refreshed/shared link that
    // landed inside a platform member restores instead of 404-ing on a NuGet fetch.
    if (isRuntimePackId(tab.id)) await loadRuntimePack(tab.framework);
    else await loadPackage(tab.id, tab.version, tab.framework);
  }
  pendingDeepLink = savedDeep;

  const targetModel = state.packages.find(matchesTarget);
  if (targetModel) {
    state.package = targetModel;
    applyDeepLink(savedDeep);
  }
  state.loading = false;
  render();
  loadSelectionData();
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
    if (state.home) {
      // Engine is warm and search is ready; show the intro/home page without loading a package.
      state.loading = false;
      state.diag = computeDiagnostics(tStart, tEngine, performance.now());
      render();
      return;
    }
    await restoreInitialWorkspace();
    const tReady = performance.now();
    state.diag = computeDiagnostics(tStart, tEngine, tReady);
    render();
  } catch (error) {
    state.loading = false;
    state.error = "Couldn’t start the inspection engine. Retry, or open a different package.";
    state.errorTitle = "Startup failed";
    state.errorDetail = String(error?.stack || error);
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

function refreshPackageStats() {
  try {
    const stats = inspectPackageCacheStats();
    if (stats) state.packageCacheStats = stats;
  } catch {
    // Keep the last known counts; a stats read failure must not disrupt inspection.
  }
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
  // The home page has its own scoped input handling (search box); global workbench
  // shortcuts assume a loaded package, so stay out of the way here.
  if (state.home) return;
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
      const sc = scope();
      if (sc === "package") { state.packageLens = set[index][0]; render(); }
      else if (sc === "member") applyMemberSection(set[index][0]);
      else { state.lens = set[index][0]; render(); }
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
  const bareHome = !loc.package && !(loc.tabs && loc.tabs.length);
  if (bareHome) {
    // Navigated back to the bare root — show the intro/home page (engine stays warm).
    state.home = true;
    render();
    return;
  }
  state.home = false;
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
