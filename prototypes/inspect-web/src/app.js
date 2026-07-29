import { lenses, packageLenses, rootCommands } from "./data.js";
import { loadPlatformIndex } from "/src/platform-index.js";
import { initializeEngine, inspectExpandPlatformCallGraph, inspectListStyleOptions, inspectLoadRuntimePack, inspectLoadRuntimePackAssembly, inspectMemberAnnotatedSource, inspectMemberCallGraph, inspectMemberDocumentation, inspectMemberFacts, inspectMemberSource, inspectPackage, inspectPackageCacheStats, inspectPackageDependencies, inspectPackageDocument, inspectPackageHeapEntries, inspectPackageIntegrations, inspectPackageMetadata, inspectPackageMetadataTable, inspectPackageOpportunities, inspectPackagePerformance, inspectPlatformHeapEntries, inspectPlatformIntegrations, inspectPlatformMetadata, inspectPlatformMetadataTable, inspectPlatformOpportunities, inspectPlatformPerformance, inspectSearchTypes, inspectTypeMemberSource, inspectTypeProjection, inspectTypeSource } from "/engine.js";

function loadStoredTaste() {
  try {
    const value = JSON.parse(localStorage.getItem("inspect-taste") || "[]");
    return Array.isArray(value) ? value.filter(item => typeof item === "string") : [];
  } catch {
    return [];
  }
}

const PLATFORM_RECENT_MAX = 8;
const RECENT_PACKAGES_MAX = 12;

// Recently-opened NuGet packages, most-recent first, persisted across sessions so the
// Home listing survives a refresh (the in-memory workspace does not). Written only from
// actual opens (a successful loadPackage), never from search hits or prefetches. Each
// entry is { id, version, framework }; re-opening refetches the nupkg (fast from the
// browser HTTP cache when still present).
function loadRecentPackages() {
  try {
    const value = JSON.parse(localStorage.getItem("inspect-recent-packages") || "[]");
    if (!Array.isArray(value)) return [];
    return value
      .filter(entry => entry && typeof entry.id === "string" && entry.id)
      .map(entry => ({
        id: entry.id,
        version: typeof entry.version === "string" && entry.version ? entry.version : "latest",
        framework: typeof entry.framework === "string" ? entry.framework : "",
      }))
      .slice(0, RECENT_PACKAGES_MAX);
  } catch {
    return [];
  }
}

// Recently-opened platform libraries, most-recent first, persisted across sessions.
// Backs the selector's "Recent" group and the "start on the library you were last
// looking at instead of the aggregate overview" behaviour. Each entry is
// { assembly, pack }; the pack (netcore.app | aspnetcore.app) rides along so a
// remembered ASP.NET Core library re-materialises from the right shared framework.
function loadPlatformRecent() {
  try {
    const value = JSON.parse(localStorage.getItem("inspect-platform-recent") || "[]");
    if (!Array.isArray(value)) return [];
    return value
      .filter(entry => entry && typeof entry.assembly === "string")
      .map(entry => ({
        assembly: entry.assembly.replace(/\.dll$/i, ""),
        pack: entry.pack === "aspnetcore.app" ? "aspnetcore.app" : "netcore.app",
      }))
      .slice(0, PLATFORM_RECENT_MAX);
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
  platformIndex: null,
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
  packageMetadata: null,
  packageMetadataLoading: false,
  packageMetadataError: "",
  packageMetadataKey: "",
  explorer: null,
  memberCallGraph: null,
  memberCallGraphLoading: false,
  memberCallGraphError: "",
  memberCallGraphExpanding: false,
  memberCallGraphSeq: 0,
  platformStack: [],
  platformDrillLoading: false,
  platformDrillError: "",
  dotnetReleases: null,
  dotnetReleasesLoading: false,
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
  libraryScope: null,
  platformRecent: loadPlatformRecent(),
  recentPackages: loadRecentPackages(),
  accessibilityFilter: new Set(["public"]),
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
  packageVersions: {},
  packageVersionsLoading: {},
  runtimePackLoading: false,
  runtimePackError: "",
  graphSourceOpen: false,
  graphSource: null,
  graphSourceLoading: false,
  graphSourceError: "",
  docViewerOpen: false,
  docViewer: null,
  docViewerLoading: false,
  docViewerError: "",
  docViewerHtml: "",
  docViewerMeta: null,
  graphSourceTitle: "",
  graphSourceRequest: null,
  styleOptions: null,
  taste: loadStoredTaste(),
  tasteOpen: false,
  settings: false,
  settingsReturn: "home",
  typeCursor: 0,
  history: [],
  loading: true,
  loadingMessage: "Starting browser inspection engine…",
  loadingSubtitle: "",
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
  // Which platform library the runtime pack is scoped to is part of the view's provenance —
  // capture it so refresh/back/share land on that library instead of the aggregate platform.
  if (isRuntimePackId(state.package.id) && state.libraryScope && state.libraryScope.size === 1) {
    packet.l = [...state.libraryScope][0];
  }
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
      return { tabs: tabsFromTuples(raw), active: 0, view: "", rich: false, type: null, member: null, overload: null, section: null, library: null };
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
        section: raw.c != null ? String(raw.c) : null,
        library: raw.l != null ? String(raw.l) : null
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
  let library = null;

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
      library = share.library;
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
    active,
    library
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
let markdownModule;
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
    return typeMatchesFilterText(item, needle)
      && (!state.namespaceFilter || item.namespace === state.namespaceFilter)
      && (!state.kindFilter || typeKind(item.kind) === state.kindFilter)
      && (!state.libraryScope || state.libraryScope.has(libraryKey(item)))
      && state.accessibilityFilter.has(accessBucket(item.accessibility));
  });
}

// The "Filter types" box matches, within the active scope, on the type's own identity
// (name/namespace/kind), the owning library (assembly) name, and — so a member you
// remember surfaces its declaring type — any member name on the type. The member scan
// runs only when the cheaper identity/library match misses, so keystroke filtering stays
// responsive on large packs like the runtime pseudo-package.
function typeMatchesFilterText(item, needle) {
  if (!needle) return true;
  if (`${item.name} ${item.namespace} ${item.kind} ${libraryKey(item)}`.toLowerCase().includes(needle)) return true;
  const members = item.api;
  if (!members || !members.length) return false;
  for (const member of members) {
    if ((member.name || "").toLowerCase().includes(needle)) return true;
  }
  return false;
}

// Owning-library key for a type: the assembly file name without a .dll suffix,
// falling back to the package's primary assembly. Used to scope the type list to
// one or more libraries within a multi-assembly package.
function libraryKey(item) {
  const asm = (item && item.assembly) || (state.package && state.package.assembly) || "";
  return asm.replace(/\.dll$/i, "");
}

// Libraries present among the loaded types, each with its type count, sorted by
// size then name. The unit the Library selector and per-library overview use.
function packageLibraries() {
  if (!state.package) return [];
  const counts = new Map();
  for (const item of state.package.types) {
    if (!state.accessibilityFilter.has(accessBucket(item.accessibility))) continue;
    const key = libraryKey(item);
    counts.set(key, (counts.get(key) || 0) + 1);
  }
  return [...counts.entries()]
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
}

const LIBRARY_CHIP_MAX = 6;

// How the Library selector presents itself: hidden for a single-library package,
// multi-select chips (all on by default) for a handful, single-select dropdown
// once there are too many to fit as chips.
function libraryMode() {
  const count = packageLibraries().length;
  if (count <= 1) return "none";
  return count <= LIBRARY_CHIP_MAX ? "chips" : "dropdown";
}

// Effective set of in-scope library keys (a null scope means every library).
function activeLibrarySet() {
  if (state.libraryScope) return state.libraryScope;
  return new Set(packageLibraries().map(lib => lib.name));
}

// Multi-select chip toggle. "" resets to all libraries (null scope). Toggling a
// single chip flips it in the active set; a set that ends up full or empty
// collapses back to the "all libraries" default.
function toggleLibraryChip(name) {
  if (!name) { state.libraryScope = null; return; }
  const next = new Set(activeLibrarySet());
  if (next.has(name)) next.delete(name); else next.add(name);
  const all = packageLibraries();
  if (next.size === 0 || next.size === all.length) state.libraryScope = null;
  else state.libraryScope = next;
}

// Reset the type cursor/selection to the first in-scope type after the library
// scope changes, keeping the current namespace/kind filters.
function afterLibraryScopeChange() {
  state.typeCursor = 0;
  const first = filteredTypes()[0];
  if (first) state.selectedTypeId = first.id;
  state.selectedMemberKey = "";
  render();
}

// The Library selector for the type nav pane. Mirrors the framework controls:
// chips (multi-select, all on by default — the inverse of the single-select
// framework chips) for a handful of libraries, a single-select dropdown once a
// package (e.g. the runtime pack) carries too many.
function libraryControl() {
  if (state.package?.isRuntimePack) {
    const select = platformLibrarySelectHtml();
    return select ? `<div class="library-picker platform-library-picker">${select}</div>` : "";
  }
  const mode = libraryMode();
  if (mode === "none") return "";
  const libs = packageLibraries();
  if (mode === "dropdown") {
    const only = state.libraryScope && state.libraryScope.size === 1
      ? [...state.libraryScope][0] : "";
    const total = libs.reduce((sum, lib) => sum + lib.count, 0);
    return `<div class="library-picker">
      <select id="library-jump" class="scope-select" aria-label="Scope to a library">
        <option value="" ${!only ? "selected" : ""}>All libraries · ${total}</option>
        ${libs.map(lib => `<option value="${escapeHtml(lib.name)}" ${only === lib.name ? "selected" : ""}>${escapeHtml(lib.name)} · ${lib.count}</option>`).join("")}
      </select>
    </div>`;
  }
  const active = activeLibrarySet();
  const allOn = !state.libraryScope;
  const chips = libs
    .map(lib => `<button class="${active.has(lib.name) ? "active" : ""}" data-library-chip="${escapeHtml(lib.name)}" title="${escapeHtml(lib.name)}"><span class="ns-count">${lib.count}</span>${escapeHtml(lib.name)}</button>`)
    .join("");
  return `<div class="namespace-chips library-chips" aria-label="Library filters">
    <button class="${allOn ? "active" : ""}" data-library-chip="">all libraries</button>
    ${chips}
  </div>`;
}

function namespaces() {
  if (!state.package) return [];
  return [...new Set(state.package.types
    .filter(item => state.accessibilityFilter.has(accessBucket(item.accessibility)))
    .map(item => item.namespace))];
}

const ACCESS_ORDER = ["public", "protected", "internal", "private"];

// Collapse a raw accessibility string ("public", "protected internal",
// "private protected", "internal", "private", …) to one of four buckets. The
// protected-family variants bucket under "protected" (they are visible to
// subclassers); a missing value is treated as public (top-level public types
// carry no explicit accessibility in metadata).
function accessBucket(access) {
  const value = (access || "public").toLowerCase();
  if (value.includes("protected")) return "protected";
  if (value.includes("internal")) return "internal";
  if (value.includes("private")) return "private";
  return "public";
}

// Accessibility buckets present in the package, in canonical order. "public" is
// always offered; the others appear only when the package actually carries a
// type in that bucket, so a wholly-public package shows just the one chip.
function accessibilityBuckets() {
  if (!state.package) return ["public"];
  const present = new Set(state.package.types.map(item => accessBucket(item.accessibility)));
  present.add("public");
  return ACCESS_ORDER.filter(bucket => present.has(bucket));
}

// Multi-select chip toggle for the accessibility filter. Flips a bucket in the
// active set; an empty result falls back to the "public" default so the type
// list is never blanked out.
function toggleAccessibilityChip(bucket) {
  const next = new Set(state.accessibilityFilter);
  if (next.has(bucket)) next.delete(bucket); else next.add(bucket);
  if (next.size === 0) next.add("public");
  state.accessibilityFilter = next;
}

// The accessibility selector for the type nav pane: a multi-select chip row
// (public on by default) that surfaces the package's non-public types on demand.
// Rendered only when the package carries more than the public bucket.
function accessibilityControl() {
  const buckets = accessibilityBuckets();
  if (buckets.length <= 1) return "";
  const chips = buckets
    .map(bucket => `<button class="${state.accessibilityFilter.has(bucket) ? "active" : ""}" data-access-chip="${bucket}">${bucket}</button>`)
    .join("");
  return `<div class="namespace-chips access-chips" aria-label="Accessibility filters">${chips}</div>`;
}

// Options for the namespace picker dropdown: every namespace in the active
// package (honoring the library + accessibility filters), sorted, with its type
// count.
function namespaceOptions() {
  if (!state.package) return "";
  const counts = new Map();
  for (const item of state.package.types) {
    if (state.libraryScope && !state.libraryScope.has(libraryKey(item))) continue;
    if (!state.accessibilityFilter.has(accessBucket(item.accessibility))) continue;
    counts.set(item.namespace, (counts.get(item.namespace) || 0) + 1);
  }
  return [...counts.keys()]
    .sort((a, b) => a.localeCompare(b))
    .map(ns => `<option value="${escapeHtml(ns)}" ${state.namespaceFilter === ns ? "selected" : ""}>${escapeHtml(ns || "(global namespace)")} · ${counts.get(ns)}</option>`)
    .join("");
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
    .filter(item => !state.libraryScope || state.libraryScope.has(libraryKey(item)))
    .filter(item => state.accessibilityFilter.has(accessBucket(item.accessibility)))
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
// package lenses that fetch one would 404 — except Integrations, which scans a single
// platform library the engine acquires directly from the runtime pack (see
// QueryPlatformIntegrations). Dependencies/Opportunities/Analysis stay package-only.
function packageLensesFor(pkg) {
  if (!pkg?.isRuntimePack) return packageLenses;
  return packageLenses.filter(([id]) =>
    id === "overview" || id === "integrations" || id === "opportunities" || id === "analysis" || id === "metadata");
}

// The single platform library the Integrations/Opportunities/Analysis lenses scan: whatever
// is currently scoped (one library), else none — the lens then prompts the user to pick one.
// Identity is the bare assembly key (no .dll), matching libraryScope and the platform roster.
function scopedPlatformLibrary() {
  if (!state.package?.isRuntimePack) return null;
  if (state.libraryScope && state.libraryScope.size === 1) return [...state.libraryScope][0];
  return null;
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

// The C#-spelled type name for display (List<T>, Dictionary<TKey, TValue>). Identity —
// item.id / item.name — stays the metadata form for selection, search, and deep-links.
function typeDisplayName(item) {
  return item?.displayName || item?.name || "";
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
  // The Settings page is a modal-style full view layered over whatever the user came from
  // (home or a package). It owns no URL — it's a preferences panel, not shareable content —
  // so it renders first and returns; closeSettings restores the underlying view.
  if (state.settings) {
    loadingBotSrc = null;
    renderSettingsView();
    return;
  }
  // The Metadata Explorer is a full-bleed "browse the database" view layered over the
  // package workbench. Like Settings it owns no URL and renders first, returning to the
  // Metadata lens on close.
  if (state.explorer?.open) {
    loadingBotSrc = null;
    renderMetadataExplorer();
    return;
  }
  // A loading/interstitial view holds one random bot for its whole appearance; any non-loading
  // view resets it so the next interstitial picks a fresh random bot (see interstitialBotSrc).
  const showingInterstitial = state.loading || state.error || (!state.home && !state.package);
  if (!showingInterstitial) loadingBotSrc = null;
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
          ${platformTabHtml()}
          ${state.packages.filter(item => !item.isRuntimePack).map(item => `
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
          <button id="open-settings" title="Settings" aria-label="Open settings">⚙</button>
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
          <span class="scope-kicker">${state.package.isRuntimePack ? "platform" : "package"}</span>
          <strong>${escapeHtml(packageDisplayName(state.package))}</strong>
          <span>${escapeHtml(state.package.version)}</span>
        </div>
        <label class="version-select">
          <span>version</span>
          <select id="package-version">
            ${versionOptionsHtml(state.package)}
          </select>
        </label>
        <label class="framework-select">
          <span>framework</span>
          <select id="framework"${state.package.frameworks.length <= 1 ? " disabled" : ""}>
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
                ? `<strong>${escapeHtml(packageDisplayName(state.package))}</strong><b>/</b><span>${escapeHtml(packageLenses.find(([id]) => id === state.packageLens)?.[1] || "Overview")}</span>`
                : `<span>${escapeHtml(packageDisplayName(state.package))}</span><b>/</b><span>${escapeHtml(current.namespace)}</span><b>/</b><strong>${escapeHtml(typeDisplayName(current))}</strong>
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
            <span class="diag" title="${state.packageCacheStats.packages} distinct NuGet package${state.packageCacheStats.packages === 1 ? "" : "s"} acquired this session; ${state.packageCacheStats.resident} currently resident in the in-memory cache${state.packageCacheStats.packages > state.packageCacheStats.resident ? ` (${state.packageCacheStats.packages - state.packageCacheStats.resident} evicted under the LRU limit of 12 packages / 128 MB)` : ""}">◇ ${state.packageCacheStats.packages} package${state.packageCacheStats.packages === 1 ? "" : "s"} · ${state.packageCacheStats.resident} resident in cache</span>` : ""}
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
      ${state.docViewerOpen ? renderDocViewer() : ""}
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
  maybeAutoLoadPackageMetadata();
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
        <input id="type-filter" value="${escapeHtml(state.typeFilter)}" placeholder="Filter types, members, libraries" autocomplete="off" spellcheck="false" />
        <kbd>⌘F</kbd>
      </label>
      <div class="namespace-picker">
        <select id="namespace-jump" class="scope-select" aria-label="Filter by namespace">
          <option value="" ${!state.namespaceFilter ? "selected" : ""}>All namespaces · ${namespaces().length}</option>
          ${namespaceOptions()}
        </select>
      </div>
      <div class="chip-stack">
        <div class="namespace-chips kind-chips" aria-label="Type kind filters">
          <button class="${!state.kindFilter ? "active" : ""}" data-kind-filter="">all kinds</button>
          ${typeKinds().map(kind => `<button class="${state.kindFilter === kind ? "active" : ""}" data-kind-filter="${kind}">${kind}</button>`).join("")}
        </div>
        ${accessibilityControl()}
        ${libraryControl()}
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
                <span class="type-name">${escapeHtml(typeDisplayName(item))}</span>
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
    <aside class="type-browser member-nav" aria-label="Members of ${escapeHtml(typeDisplayName(type))}">
      <div class="browser-head">
        <div>
          <span class="pane-label">MEMBERS</span>
          <span class="result-count">${memberGroups(type).length} members</span>
        </div>
      </div>
      <button class="nav-back-row" id="nav-to-types" title="Back to types (Esc)">
        <span class="chevron">‹</span>
        <span class="type-name">${escapeHtml(typeDisplayName(type))}</span>
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
    <div class="type-badge">${pkg.isRuntimePack ? "◎" : "⬡"}</div>
    <div>
      <div class="type-namespace">${pkg.isRuntimePack ? "Shared framework" : "NuGet package"}</div>
      <h1>${escapeHtml(packageDisplayName(pkg))}</h1>
      <code class="type-signature">${pkg.isRuntimePack ? `${escapeHtml(packageDisplayName(pkg))} · ${escapeHtml(pkg.version)}` : `${escapeHtml(pkg.id)}@${escapeHtml(pkg.version)}`}</code>
    </div>
    <div class="type-metrics"><span><strong>${pkg.totalTypes}</strong> types</span><span><strong>${pkg.totalMembers.toLocaleString()}</strong> members</span></div>
    <dl class="definition-list">
      <div><dt>Active TFM:</dt><dd>${escapeHtml(pkg.activeFramework)}</dd></div>
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
  if (state.packageLens === "metadata") return `${packageHeading()}${renderPackageMetadata()}`;
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
  const lib = scopedPlatformLibrary();
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}${lib ? `#${lib}` : ""}`;
}

function renderPackageIntegrations() {
  const isPlatform = Boolean(state.package?.isRuntimePack);
  const scopedLib = scopedPlatformLibrary();
  // On the Platform the scan targets one library at a time (the whole shared framework is
  // ~160 assemblies). Offer a picker to switch libraries; when nothing is scoped yet, prompt
  // for a choice instead of scanning.
  const platformPicker = isPlatform
    ? `<section class="document-section"><div class="library-picker platform-library-picker overview-library-picker">${platformLibrarySelectHtml({ dataAttr: "data-platform-integrations-library", selected: scopedLib || "" })}</div></section>`
    : "";
  if (isPlatform && !scopedLib) {
    return `${platformPicker}<section class="document-section empty-document"><span class="large-glyph">◈</span><h2>Pick a library to scan</h2><p>Choose a .NET platform library above to scan its public surface for DI, logging, OpenTelemetry, ASP.NET Core, AI, or hosting integration signals.</p></section>`;
  }
  const scanScope = isPlatform ? `${scopedLib} · ${escapeHtml(state.package.activeFramework)}` : escapeHtml(state.package.activeFramework);
  const current = packageIntegrationsSignature();
  const fresh = state.packageIntegrationsKey === current;
  if (state.packageIntegrationsLoading && fresh) {
    return `${platformPicker}<section class="document-section source-progress"><span class="loader"></span><h2>Scanning integrations…</h2><p>Reading the public surface of ${isPlatform ? escapeHtml(scopedLib) : "each assembly"} for ecosystem signals.</p></section>`;
  }
  if (fresh && state.packageIntegrationsError) {
    return `${platformPicker}<section class="document-section empty-document"><span class="large-glyph">◈</span><h2>Integration scan failed</h2><p>${escapeHtml(state.packageIntegrationsError)}</p></section>`;
  }
  const data = fresh ? state.packageIntegrations : null;
  if (!data) {
    return `${platformPicker}<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const categories = data.categories || [];
  const warning = data.inspectionError
    ? `<section class="document-section metadata-warning"><strong>⚠ Some assemblies could not be scanned</strong><ul><li><code>${escapeHtml(data.inspectionError)}</code></li></ul></section>`
    : "";

  if (!categories.length) {
    return `${platformPicker}${warning}<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No ecosystem integrations detected</h2><p>The public surface of ${isPlatform ? `${escapeHtml(scopedLib)}` : escapeHtml(state.package.activeFramework)} shows no known DI, logging, OpenTelemetry, ASP.NET Core, AI, or hosting signals.</p></section>`;
  }

  const summary = `
    <section class="document-section">
      <div class="section-title"><h2>Ecosystem integrations</h2><span>${categories.length} categor${categories.length === 1 ? "y" : "ies"} · ${data.totalSignals} signal${data.totalSignals === 1 ? "" : "s"} · ${scanScope}</span></div>
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

  return `${platformPicker}${warning}${summary}${blocks}`;
}

async function loadPackageIntegrations() {
  const isPlatform = Boolean(state.package?.isRuntimePack);
  const scopedLib = scopedPlatformLibrary();
  // The Platform lens needs a chosen library to scan; without one the render prompts for a
  // selection, so there is nothing to fetch yet.
  if (isPlatform && !scopedLib) return;
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
    const result = isPlatform
      ? await inspectPlatformIntegrations({
          targetFramework: state.package.activeFramework,
          assemblyFileName: `${scopedLib}.dll`,
          pack: platformPackForAssembly(scopedLib)
        })
      : await inspectPackageIntegrations({
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
  const lib = scopedPlatformLibrary();
  return `${pkg.id}@${pkg.version}/${pkg.activeFramework}${lib ? `#${lib}` : ""}`;
}

// The Opportunities and Analysis lenses run over one platform library at a time, so on the
// Platform they render the same inline library picker as Integrations and prompt for a choice
// when nothing is scoped. This mirrors renderPackageIntegrations' platform handling.
function platformLensPicker(dataAttr) {
  const scopedLib = scopedPlatformLibrary();
  return `<section class="document-section"><div class="library-picker platform-library-picker overview-library-picker">${platformLibrarySelectHtml({ dataAttr, selected: scopedLib || "" })}</div></section>`;
}

function renderPackageOpportunities() {
  const isPlatform = Boolean(state.package?.isRuntimePack);
  const scopedLib = scopedPlatformLibrary();
  const picker = isPlatform ? platformLensPicker("data-platform-opportunities-library") : "";
  if (isPlatform && !scopedLib) {
    return `${picker}<section class="document-section empty-document"><span class="large-glyph">△</span><h2>Pick a library to scan</h2><p>Choose a .NET platform library above to compare its public surface against ecosystem integration patterns.</p></section>`;
  }
  const scanScope = isPlatform ? `${escapeHtml(scopedLib)} · ${escapeHtml(state.package.activeFramework)}` : escapeHtml(state.package.activeFramework);
  const current = packageScopeSignature();
  const fresh = state.packageOpportunitiesKey === current;
  if (state.packageOpportunitiesLoading && fresh) {
    return `${picker}<section class="document-section source-progress"><span class="loader"></span><h2>Scanning opportunities…</h2><p>Comparing the public surface against ecosystem integration patterns.</p></section>`;
  }
  if (fresh && state.packageOpportunitiesError) {
    return `${picker}<section class="document-section empty-document"><span class="large-glyph">△</span><h2>Opportunity scan failed</h2><p>${escapeHtml(state.packageOpportunitiesError)}</p></section>`;
  }
  const data = fresh ? state.packageOpportunities : null;
  if (!data) {
    return `${picker}<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const categories = data.categories || [];
  const warning = data.inspectionError
    ? `<section class="document-section metadata-warning"><strong>⚠ Some assemblies could not be scanned</strong><ul><li><code>${escapeHtml(data.inspectionError)}</code></li></ul></section>`
    : "";

  if (!categories.length) {
    return `${picker}${warning}<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No integration opportunities</h2><p>The public surface of ${scanScope} shows no obvious auth, cloud-client, configuration, database, or AI-client patterns that suggest a missing ecosystem integration.</p></section>`;
  }

  const summary = `
    <section class="document-section">
      <div class="section-title"><h2>Integration opportunities</h2><span>${categories.length} area${categories.length === 1 ? "" : "s"} · ${data.totalOpportunities} suggestion${data.totalOpportunities === 1 ? "" : "s"} · ${scanScope}</span></div>
      <p class="lens-note">Ecosystem areas this ${isPlatform ? "library" : "package"}'s surface suggests but does not yet integrate with. Chips are live: the type opens in this package, a suggested package loads on demand, and each "look for" API opens a search.</p>
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

  return `${picker}${warning}${summary}${blocks}`;
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
  const isPlatform = Boolean(state.package?.isRuntimePack);
  const scopedLib = scopedPlatformLibrary();
  if (isPlatform && !scopedLib) return;
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
    const result = isPlatform
      ? await inspectPlatformOpportunities({
          targetFramework: state.package.activeFramework,
          assemblyFileName: `${scopedLib}.dll`,
          pack: platformPackForAssembly(scopedLib)
        })
      : await inspectPackageOpportunities({
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
  if (Boolean(state.package?.isRuntimePack) && !scopedPlatformLibrary()) return;
  if (state.packageOpportunitiesKey === packageScopeSignature()) return;
  loadPackageOpportunities();
}

function renderPackagePerformance() {
  const isPlatform = Boolean(state.package?.isRuntimePack);
  const scopedLib = scopedPlatformLibrary();
  const picker = isPlatform ? platformLensPicker("data-platform-analysis-library") : "";
  if (isPlatform && !scopedLib) {
    return `${picker}<section class="document-section empty-document"><span class="large-glyph">△</span><h2>Pick a library to analyze</h2><p>Choose a .NET platform library above to classify allocation and performance opportunities across its method bodies.</p></section>`;
  }
  const scanScope = isPlatform ? `${escapeHtml(scopedLib)} · ${escapeHtml(state.package.activeFramework)}` : escapeHtml(state.package.activeFramework);
  const current = packageScopeSignature();
  const fresh = state.packagePerformanceKey === current;
  if (state.packagePerformanceLoading && fresh) {
    return `${picker}<section class="document-section source-progress"><span class="loader"></span><h2>Analyzing allocations…</h2><p>Classifying allocation and performance opportunities across every method body.</p></section>`;
  }
  if (fresh && state.packagePerformanceError) {
    return `${picker}<section class="document-section empty-document"><span class="large-glyph">△</span><h2>Analysis failed</h2><p>${escapeHtml(state.packagePerformanceError)}</p></section>`;
  }
  const data = fresh ? state.packagePerformance : null;
  if (!data) {
    return `${picker}<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const members = data.members || [];
  const warning = data.inspectionError
    ? `<section class="document-section metadata-warning"><strong>⚠ Some assemblies could not be analyzed</strong><ul><li><code>${escapeHtml(data.inspectionError)}</code></li></ul></section>`
    : "";
  const nonPublicNote = data.nonPublicOpportunities > 0
    ? ` · ${data.nonPublicOpportunities} in non-public members`
    : "";

  if (!members.length) {
    return `${picker}${warning}<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No public allocation hot spots</h2><p>${data.totalOpportunities} allocation/performance opportunit${data.totalOpportunities === 1 ? "y was" : "ies were"} classified, but none surface on a public member of ${scanScope}${nonPublicNote}. Open a member's Facts lens to inspect its body directly.</p></section>`;
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
      <div class="section-title"><h2>Allocation &amp; performance triage</h2><span>${members.length} public member${members.length === 1 ? "" : "s"} · ${data.totalOpportunities} opportunit${data.totalOpportunities === 1 ? "y" : "ies"}${nonPublicNote} · ${scanScope}</span></div>
      <p class="lens-note">Ranked by in-loop opportunities, then count. Static IL classification — confirm impact with a benchmark or profiler. Select a member to open its Facts lens.</p>
    </section>`;

  return `${picker}${warning}${summary}<section class="document-section"><div class="perf-list">${rows}</div></section>`;
}

async function loadPackagePerformance() {
  const isPlatform = Boolean(state.package?.isRuntimePack);
  const scopedLib = scopedPlatformLibrary();
  if (isPlatform && !scopedLib) return;
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
    const result = isPlatform
      ? await inspectPlatformPerformance({
          targetFramework: state.package.activeFramework,
          assemblyFileName: `${scopedLib}.dll`,
          pack: platformPackForAssembly(scopedLib)
        })
      : await inspectPackagePerformance({
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
  if (Boolean(state.package?.isRuntimePack) && !scopedPlatformLibrary()) return;
  if (state.packagePerformanceKey === packageScopeSignature()) return;
  loadPackagePerformance();
}

// The Metadata lens: the image-level "container" view of each assembly — metadata format
// version, heap sizes, ECMA-335 table row counts, and PE/CLI header facts. This is the shape
// of the metadata itself, distinct from the API surface (the types within). For the platform
// it scopes to one runtime-pack assembly (the shared framework is ~160 assemblies); for a
// NuGet package it describes every active-framework lib/ assembly.
function renderPackageMetadata() {
  const isPlatform = Boolean(state.package?.isRuntimePack);
  const scopedLib = scopedPlatformLibrary();
  const picker = isPlatform ? platformLensPicker("data-platform-metadata-library") : "";
  if (isPlatform && !scopedLib) {
    return `${picker}<section class="document-section empty-document"><span class="large-glyph">△</span><h2>Pick a library to inspect</h2><p>Choose a .NET platform library above to read its metadata image — format version, heaps, tables, and PE/CLI headers.</p></section>`;
  }
  const scanScope = isPlatform ? `${escapeHtml(scopedLib)} · ${escapeHtml(state.package.activeFramework)}` : escapeHtml(state.package.activeFramework);
  const current = packageScopeSignature();
  const fresh = state.packageMetadataKey === current;
  if (state.packageMetadataLoading && fresh) {
    return `${picker}<section class="document-section source-progress"><span class="loader"></span><h2>Reading metadata…</h2><p>Describing the metadata image — heaps, tables, and headers.</p></section>`;
  }
  if (fresh && state.packageMetadataError) {
    return `${picker}<section class="document-section empty-document"><span class="large-glyph">△</span><h2>Metadata read failed</h2><p>${escapeHtml(state.packageMetadataError)}</p></section>`;
  }
  const data = fresh ? state.packageMetadata : null;
  if (!data) {
    return `${picker}<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const assemblies = data.assemblies || [];
  const warning = data.inspectionError
    ? `<section class="document-section metadata-warning"><strong>⚠ Some assemblies could not be read</strong><ul><li><code>${escapeHtml(data.inspectionError)}</code></li></ul></section>`
    : "";

  if (!assemblies.length) {
    return `${picker}${warning}<section class="document-section empty-document"><span class="large-glyph">◇</span><h2>No metadata images</h2><p>None of the assemblies in ${scanScope} carry ECMA-335 metadata (they may be native or resource-only).</p></section>`;
  }

  const blocks = assemblies.map(renderAssemblyMetadataBlock).join("");
  const summary = `
    <section class="document-section">
      <div class="section-title"><h2>Metadata image</h2><span>${assemblies.length} assembl${assemblies.length === 1 ? "y" : "ies"} · ${scanScope}</span></div>
      <p class="lens-note">The physical shape of each assembly's metadata — format stamp, heap sizes, populated ECMA-335 tables, and PE/CLI headers. This describes the container, not the API surface.</p>
    </section>`;

  return `${picker}${warning}${summary}${blocks}`;
}

function renderAssemblyMetadataBlock(asm) {
  const heapRows = (asm.heaps || [])
    .filter(heap => heap.sizeInBytes > 0)
    .map(heap => `
      <button type="button" class="meta-heap" data-mde-open-heap="${escapeHtml(asm.assembly)}|${escapeHtml(heap.name)}" title="Browse ${escapeHtml(heapStreamName(heap.name))} in the metadata explorer">
        <span class="meta-heap-name">${escapeHtml(heapStreamName(heap.name))}</span>
        <span class="meta-heap-size">${fmtBytes(heap.sizeInBytes)}</span>
        <span class="meta-heap-addr">${escapeHtml(heap.addressing === "Index" ? "index" : "byte offset")} · max ${heap.maxAddress}</span>
      </button>`).join("");

  const tables = (asm.tables || []).slice().sort((a, b) => b.rowCount - a.rowCount);
  const tableRows = tables.map(table => `
    <button type="button" class="meta-table-row ${table.isProjected ? "" : "meta-table-unprojected"}" data-mde-open="${escapeHtml(asm.assembly)}|${table.index}" title="${table.isProjected ? "Open in the metadata explorer" : "Present in the image but not modeled by the projection"}">
      <span class="meta-table-name">${escapeHtml(table.name)}</span>
      <span class="meta-table-count">${table.rowCount.toLocaleString()}</span>
      <span class="meta-table-go">→</span>
    </button>`).join("");

  const h = asm.headers || {};
  const corLine = h.corFlags
    ? `<span class="meta-fact"><span class="meta-fact-k">CLI</span><span class="meta-fact-v">v${h.majorRuntimeVersion}.${h.minorRuntimeVersion} · ${escapeHtml(h.corFlags)}${h.entryPointToken ? ` · entry 0x${(h.entryPointToken >>> 0).toString(16)}` : ""}</span></span>`
    : "";

  return `
    <section class="document-section meta-assembly">
      <div class="section-title"><h2>${escapeHtml(asm.assembly)}</h2><span>${escapeHtml(asm.kind)}${asm.isAssembly ? " · assembly manifest" : " · module"} · metadata ${fmtBytes(asm.metadataSize)}</span></div>
      <div class="meta-facts">
        <span class="meta-fact"><span class="meta-fact-k">Format</span><span class="meta-fact-v">${escapeHtml(asm.metadataVersion)}</span></span>
        <span class="meta-fact"><span class="meta-fact-k">Machine</span><span class="meta-fact-v">${escapeHtml(h.machine || "—")}${h.isPE32Plus ? " · PE32+" : " · PE32"}</span></span>
        <span class="meta-fact"><span class="meta-fact-k">Subsystem</span><span class="meta-fact-v">${escapeHtml(h.subsystem || "—")}</span></span>
        <span class="meta-fact"><span class="meta-fact-k">Tables</span><span class="meta-fact-v">${asm.projectedTableTotal}/${tables.length} populated</span></span>
        ${corLine}
      </div>
      <div class="meta-grid">
        <div class="meta-col">
          <h3 class="meta-col-title">Heaps</h3>
          <div class="meta-heaps">${heapRows || '<div class="meta-empty">No non-empty heaps</div>'}</div>
        </div>
        <div class="meta-col">
          <h3 class="meta-col-title">Tables <span class="meta-col-note">by row count</span></h3>
          <div class="meta-tables">${tableRows || '<div class="meta-empty">No populated tables</div>'}</div>
        </div>
      </div>
    </section>`;
}

async function loadPackageMetadata() {
  const isPlatform = Boolean(state.package?.isRuntimePack);
  const scopedLib = scopedPlatformLibrary();
  if (isPlatform && !scopedLib) return;
  const signature = packageScopeSignature();
  if (state.packageMetadataKey === signature && (state.packageMetadata || state.packageMetadataError)) {
    render();
    return;
  }
  state.packageMetadataKey = signature;
  state.packageMetadata = null;
  state.packageMetadataError = "";
  state.packageMetadataLoading = true;
  render();
  try {
    const result = isPlatform
      ? await inspectPlatformMetadata({
          targetFramework: state.package.activeFramework,
          assemblyFileName: `${scopedLib}.dll`,
          pack: platformPackForAssembly(scopedLib)
        })
      : await inspectPackageMetadata({
          packageId: state.package.id,
          version: state.package.version,
          framework: state.package.activeFramework
        });
    if (state.packageMetadataKey === signature) state.packageMetadata = result;
  } catch (error) {
    if (state.packageMetadataKey === signature) state.packageMetadataError = String(error?.message || error);
  } finally {
    if (state.packageMetadataKey === signature) state.packageMetadataLoading = false;
    render();
  }
}

function maybeAutoLoadPackageMetadata() {
  if (!state.atPackageRoot || state.packageLens !== "metadata") return;
  if (Boolean(state.package?.isRuntimePack) && !scopedPlatformLibrary()) return;
  if (state.packageMetadataKey === packageScopeSignature()) return;
  loadPackageMetadata();
}

// ─── Metadata Explorer ─────────────────────────────────────────────────────────
// A spatial "browse the metadata like a database" view. The overview lens hands off an
// assembly + a starting table; the explorer lays every populated table out as a card,
// lazy-loads each table's row window on demand, renders cells with their typed values, and
// turns handle/range cells into ref->def jumps that transport you to the target table+row.

const EXPLORER_PAGE = 50;

// Opens the explorer over one assembly, focused on a table (and optionally a row). The table
// directory comes from the already-loaded overview so the canvas can render immediately; each
// card fetches its own row window.
function openExplorer(assemblyFileName, tableIndex, rowId = 0) {
  const ex = buildBaseExplorer(assemblyFileName);
  if (!ex) return;
  ex.history = [{ index: Number(tableIndex), rowId: Number(rowId) || 0 }];
  ex.historyPos = 0;
  state.explorer = ex;
  applyExplorerFocus();
}

// Opens the explorer focused on a heap card (#Strings / #Blob / #GUID / #US) rather than a table.
function openExplorerHeap(assemblyFileName, heapName) {
  const ex = buildBaseExplorer(assemblyFileName);
  if (!ex) return;
  ex.history = [{ heap: heapName }];
  ex.historyPos = 0;
  state.explorer = ex;
  applyExplorerFocus();
}

// The common explorer state: the table + heap directories drawn from the loaded overview, plus
// empty window caches. Focus is set by the caller (openExplorer / openExplorerHeap).
function buildBaseExplorer(assemblyFileName) {
  const data = state.packageMetadata;
  const asm = (data?.assemblies || []).find(a => a.assembly === assemblyFileName)
    || (data?.assemblies || [])[0];
  if (!asm) return null;
  const isPlatform = Boolean(state.package?.isRuntimePack);
  const directory = (asm.tables || [])
    .slice()
    .sort((a, b) => a.index - b.index)
    .map(t => ({ index: t.index, name: t.name, rowCount: t.rowCount, isProjected: t.isProjected }));
  const heaps = (asm.heaps || [])
    .filter(h => h.sizeInBytes > 0)
    .map(h => ({ name: h.name, streamName: heapStreamName(h.name), sizeInBytes: h.sizeInBytes, addressing: h.addressing }));
  return {
    open: true,
    isPlatform,
    assemblyFileName: asm.assembly,
    pack: isPlatform ? platformPackForAssembly(asm.assembly.replace(/\.dll$/i, "")) : null,
    packageId: state.package.id,
    version: state.package.version,
    framework: state.package.activeFramework,
    directory,
    heaps,
    windows: {},
    heapWindows: {},
    focusIndex: directory[0]?.index ?? 0,
    focusHeap: null,
    highlight: null,
    detail: null,
    history: [],
    historyPos: -1,
  };
}

// ECMA-335 stream name for a HeapKind name, matching the product's spelling.
function heapStreamName(name) {
  switch (name) {
    case "String": return "#Strings";
    case "Blob": return "#Blob";
    case "Guid": return "#GUID";
    case "UserString": return "#US";
    default: return `#${name}`;
  }
}

function closeExplorer() {
  state.explorer = null;
  render();
}

function explorerTableName(index) {
  const hit = state.explorer?.directory.find(t => t.index === index);
  return hit ? hit.name : `#${index}`;
}

async function loadExplorerWindow(index, startRowId = 1) {
  const ex = state.explorer;
  if (!ex) return;
  const existing = ex.windows[index];
  if (existing && (existing.loading || (existing.data && existing.data.startRowId === startRowId))) return;
  ex.windows[index] = { loading: true, error: "", data: existing?.data || null, startRowId };
  render();
  try {
    const request = {
      assemblyFileName: ex.assemblyFileName,
      tableIndex: index,
      startRowId,
      maxRows: EXPLORER_PAGE,
    };
    const result = ex.isPlatform
      ? await inspectPlatformMetadataTable({ ...request, targetFramework: ex.framework, pack: ex.pack })
      : await inspectPackageMetadataTable({ ...request, packageId: ex.packageId, version: ex.version, framework: ex.framework });
    if (state.explorer !== ex) return;
    ex.windows[index] = { loading: false, error: result.error || "", data: result, startRowId };
  } catch (error) {
    if (state.explorer !== ex) return;
    ex.windows[index] = { loading: false, error: String(error?.message || error), data: null, startRowId };
  } finally {
    if (state.explorer === ex) {
      render();
      if (index === ex.focusIndex && !ex.focusHeap) explorerScrollToFocus();
    }
  }
}

// Lists one heap's entries via the engine (referenced-only for #Strings/#Blob, complete for
// #GUID, nothing for #US). Cached per heap name; coverage/truncation travel with the result.
async function loadExplorerHeap(heapName) {
  const ex = state.explorer;
  if (!ex) return;
  const existing = ex.heapWindows[heapName];
  if (existing && (existing.loading || existing.data)) return;
  ex.heapWindows[heapName] = { loading: true, error: "", data: null };
  render();
  try {
    const request = { assemblyFileName: ex.assemblyFileName, heap: heapName };
    const result = ex.isPlatform
      ? await inspectPlatformHeapEntries({ ...request, targetFramework: ex.framework, pack: ex.pack })
      : await inspectPackageHeapEntries({ ...request, packageId: ex.packageId, version: ex.version, framework: ex.framework });
    if (state.explorer !== ex) return;
    ex.heapWindows[heapName] = { loading: false, error: result.error || "", data: result };
  } catch (error) {
    if (state.explorer !== ex) return;
    ex.heapWindows[heapName] = { loading: false, error: String(error?.message || error), data: null };
  } finally {
    if (state.explorer === ex) {
      render();
      if (ex.focusHeap === heapName) explorerScrollToFocus();
    }
  }
}
// ref->def: transport to the target table+row. Every jump pushes a focus entry onto the
// history stack so Back/Forward can walk the journey — essential once the focus panel hides
// the table you came from (including intra-table hops like TypeDef.Extends -> another TypeDef,
// which otherwise look like "you didn't move").
function explorerJump(index, rowId) {
  pushExplorerFocus({ index, rowId: rowId || 0 });
}

// A focus entry is either { index, rowId } (rowId 0 = table, no highlighted row) or { heap }.
function sameFocus(a, b) {
  if (!a || !b) return false;
  if (a.heap != null || b.heap != null) return a.heap === b.heap;
  return a.index === b.index;
}

// Move focus to a new entry, truncating any forward history (a fresh branch). Re-selecting the
// current table just updates its row in place rather than stacking a duplicate.
function pushExplorerFocus(entry) {
  const ex = state.explorer;
  if (!ex) return;
  const cur = ex.history[ex.historyPos];
  if (sameFocus(cur, entry)) {
    ex.history[ex.historyPos] = entry;
  } else {
    ex.history = ex.history.slice(0, ex.historyPos + 1);
    ex.history.push(entry);
    ex.historyPos = ex.history.length - 1;
  }
  applyExplorerFocus();
}

function explorerHistoryBack() {
  const ex = state.explorer;
  if (!ex || ex.historyPos <= 0) return;
  ex.historyPos--;
  applyExplorerFocus();
}

function explorerHistoryForward() {
  const ex = state.explorer;
  if (!ex || ex.historyPos >= ex.history.length - 1) return;
  ex.historyPos++;
  applyExplorerFocus();
}

// Realize the current history entry: set focus + highlight + detail, load the window/heap that
// backs it, render, and scroll it into place. The single source of truth for "where am I".
function applyExplorerFocus() {
  const ex = state.explorer;
  const entry = ex?.history[ex.historyPos];
  if (!entry) return;
  if (entry.heap != null) {
    ex.focusHeap = entry.heap;
    ex.highlight = null;
    ex.detail = null;
    if (!ex.heapWindows[entry.heap]) loadExplorerHeap(entry.heap);
    else render();
  } else {
    ex.focusHeap = null;
    ex.focusIndex = entry.index;
    ex.highlight = entry.rowId ? { index: entry.index, rowId: entry.rowId } : null;
    ex.detail = entry.rowId ? { index: entry.index, rowId: entry.rowId } : null;
    const start = entry.rowId ? Math.max(1, Math.floor((entry.rowId - 1) / EXPLORER_PAGE) * EXPLORER_PAGE + 1) : 1;
    const win = ex.windows[entry.index];
    const onScreen = win?.data && (!entry.rowId
      || (entry.rowId >= win.data.startRowId && entry.rowId < win.data.startRowId + (win.data.rows?.length || 0)));
    if (onScreen) render();
    else loadExplorerWindow(entry.index, start);
  }
  explorerScrollToFocus();
}

// Center the active card in the dim wall behind the lightbox (spatial context peeking around the
// edges), and scroll the highlighted row into view inside the focus panel's own grid.
function explorerScrollToFocus() {
  requestAnimationFrame(() => {
    const ex = state.explorer;
    if (!ex) return;
    const wallCard = ex.focusHeap
      ? document.querySelector(`.mde-wall .mde-heap-card[data-mde-heap="${cssEscape(ex.focusHeap)}"]`)
      : document.querySelector(`.mde-wall .mde-card[data-mde-index="${ex.focusIndex}"]`);
    if (wallCard) wallCard.scrollIntoView({ behavior: "smooth", block: "center" });
    const focusGrid = document.querySelector(".mde-focus .mde-grid-scroll");
    if (focusGrid && !ex.highlight) focusGrid.scrollTop = 0;
    const row = ex.highlight && document.querySelector(`.mde-focus .mde-row[data-mde-row="${ex.highlight.index}:${ex.highlight.rowId}"]`);
    if (row) row.scrollIntoView({ behavior: "smooth", block: "center" });
  });
}

// Attribute-selector-safe heap name (heap names are simple identifiers, but be defensive).
function cssEscape(value) {
  return String(value).replace(/["\\]/g, "\\$&");
}

function renderMetadataExplorer() {
  const ex = state.explorer;
  const chips = ex.directory.map(t => `
    <button type="button" class="mde-chip ${t.index === ex.focusIndex && !ex.focusHeap ? "active" : ""} ${t.isProjected ? "" : "mde-chip-unprojected"}" data-mde-chip="${t.index}" title="${t.rowCount.toLocaleString()} rows${t.isProjected ? "" : " · not modeled"}">
      ${escapeHtml(t.name)}<span class="mde-chip-count">${t.rowCount.toLocaleString()}</span>
    </button>`).join("");
  const heapChips = (ex.heaps || []).map(h => `
    <button type="button" class="mde-chip mde-chip-heap ${ex.focusHeap === h.name ? "active" : ""}" data-mde-heap-chip="${escapeHtml(h.name)}" title="${escapeHtml(h.streamName)} · ${fmtBytes(h.sizeInBytes)}">
      ${escapeHtml(h.streamName)}<span class="mde-chip-count">${fmtBytes(h.sizeInBytes)}</span>
    </button>`).join("");

  const cards = ex.directory.map(t => renderExplorerCard(t)).join("");
  const heapCards = (ex.heaps || []).length
    ? `<div class="mde-heap-divider"><span>heaps</span></div>` + ex.heaps.map(renderHeapCard).join("")
    : "";

  const canBack = ex.historyPos > 0;
  const canForward = ex.historyPos < ex.history.length - 1;
  const focusPanel = renderExplorerFocusPanel();

  app.innerHTML = `
    <div class="metadata-explorer">
      <header class="mde-bar">
        <div class="mde-nav" role="group" aria-label="Explorer navigation">
          <button id="mde-exit" class="mde-navbtn mde-nav-exit" title="Exit the explorer (Esc at the start)">✕ Exit</button>
          <button id="mde-hist-back" class="mde-navbtn" ${canBack ? "" : "disabled"} title="Back (Backspace)">← Back</button>
          <button id="mde-hist-fwd" class="mde-navbtn" ${canForward ? "" : "disabled"} title="Forward (Shift+Backspace)">Forward →</button>
        </div>
        <div class="mde-title">
          <span class="mde-title-asm">${escapeHtml(ex.assemblyFileName)}</span>
          <span class="mde-title-note">metadata tables · ${ex.directory.length} populated · click a ref to jump</span>
        </div>
      </header>
      <nav class="mde-chips">${chips}${heapChips ? `<span class="mde-chip-sep"></span>${heapChips}` : ""}</nav>
      <div class="mde-body">
        <div class="mde-canvas mde-wall" id="mde-canvas">${cards}${heapCards}</div>
        ${focusPanel}
      </div>
    </div>`;
  bindMetadataExplorerEvents();
}

// The focus lightbox: the current table (or heap) blown up front-and-center over the dim wall,
// with the row inspector docked on its right. Auto-focus (every ref->def jump lands here) means
// this is the primary reading surface — the wall behind is spatial context you can click into.
function renderExplorerFocusPanel() {
  const ex = state.explorer;
  const card = ex.focusHeap
    ? renderHeapCard(ex.heaps.find(h => h.name === ex.focusHeap) || {})
    : renderExplorerCard(ex.directory.find(t => t.index === ex.focusIndex) || {});
  const detail = renderExplorerDetail();
  return `
    <div class="mde-focus">
      <div class="mde-focus-card">${card}</div>
      ${detail}
    </div>`;
}

// A heap card: header (stream name, size, coverage badge), a coverage caveat banner, and the
// listed entries (address · refs · value). The value reuses the same cell renderer as the grid,
// so a listed #Strings entry and a Name cell pointing at it render identically.
function renderHeapCard(h) {
  const ex = state.explorer;
  const win = ex.heapWindows[h.name];
  const focused = ex.focusHeap === h.name;
  let body;
  if (win?.loading && !win.data) {
    body = `<div class="mde-card-empty"><span class="loader"></span> Reading ${escapeHtml(h.streamName)}…</div>`;
  } else if (win?.error) {
    body = `<div class="mde-card-empty mde-card-error">△ ${escapeHtml(win.error)}</div>`;
  } else if (win?.data) {
    body = renderHeapListing(win.data);
  } else {
    body = `<div class="mde-card-empty mde-card-lazy" data-mde-heap-needs-load="${escapeHtml(h.name)}"><span class="loader"></span> Loading ${escapeHtml(h.streamName)}…</div>`;
  }
  const coverage = win?.data?.coverage;
  const badge = coverage
    ? `<span class="mde-cov-badge mde-cov-${coverage.toLowerCase()}">${escapeHtml(coverageLabel(coverage))}</span>`
    : "";
  return `
    <section class="mde-heap-card ${focused ? "mde-card-focus" : ""}" data-mde-heap="${escapeHtml(h.name)}">
      <div class="mde-card-head">
        <h3>${escapeHtml(h.streamName)}</h3>
        <span class="mde-card-meta">heap · ${fmtBytes(h.sizeInBytes)}${badge ? " · " : ""}</span>${badge}
      </div>
      ${body}
    </section>`;
}

function coverageLabel(coverage) {
  switch (coverage) {
    case "Complete": return "every entry";
    case "ReferencedOnly": return "referenced only";
    case "NotEnumerable": return "not enumerable";
    default: return coverage;
  }
}

// The listing body: a coverage caveat line, then the entry rows. Coverage is stated as part of
// the answer so a referenced-only or truncated list is never read as the whole heap.
function renderHeapListing(data) {
  const note = heapCoverageNote(data);
  if (data.coverage === "NotEnumerable" || !(data.entries || []).length) {
    return `<div class="mde-heap-note">${note}</div>`;
  }
  const isIndex = data.heap === "Guid";
  const sel = state.explorer?.detail;
  const rows = data.entries.map(entry => {
    const addr = isIndex ? `#${entry.offset}` : `0x${(entry.offset >>> 0).toString(16)}`;
    const isSel = sel && sel.heap === data.heap && sel.offset === entry.offset;
    return `<tr class="mde-heap-row ${isSel ? "mde-heap-row-sel" : ""}" data-mde-heap-row="${escapeHtml(data.heap)}:${entry.offset}">
      <td class="mde-heap-addr" title="${isIndex ? "GUID index" : "heap byte offset"}">${addr}</td>
      <td class="mde-heap-val">${renderHeapValueCell(entry.value)}</td>
      <td class="mde-heap-refs" title="referenced by ${entry.referenceCount} projected cell${entry.referenceCount === 1 ? "" : "s"}">${entry.referenceCount.toLocaleString()}×</td>
    </tr>`;
  }).join("");
  return `
    <div class="mde-heap-note">${note}</div>
    <div class="mde-grid-scroll"><table class="mde-grid mde-heap-grid">
      <thead><tr><th class="mde-heap-addr">addr</th><th>value</th><th class="mde-heap-refs" title="reference count">refs</th></tr></thead>
      <tbody>${rows}</tbody>
    </table></div>`;
}

function heapCoverageNote(data) {
  const parts = [];
  switch (data.coverage) {
    case "Complete":
      parts.push(`Every entry in this heap is listed — the GUID heap is fixed-size records at consecutive indices, so it enumerates exactly.`);
      break;
    case "ReferencedOnly":
      parts.push(`Only entries a projected table row points at are listed — the heap may hold values nothing references, still readable by address.`);
      break;
    case "NotEnumerable":
      parts.push(`No entry can be listed: no ECMA-335 table column points into ${escapeHtml(data.streamName)} — its references are <code>ldstr</code> operands inside method bodies. An empty list here is a blind spot, not an empty heap.`);
      break;
    default:
      break;
  }
  if (data.rowsTruncated) parts.push(`Reference scan did not cover every row of every table, so some references are uncounted.`);
  if (data.entriesTruncated) parts.push(`The entry budget cut the listing short.`);
  return parts.join(" ");
}

// A heap entry's value renders exactly like the same heap cell in a grid, minus the jump (a heap
// value has no ref->def target). Falls back through the flat cell union defensively.
function renderHeapValueCell(cell) {
  if (!cell) return `<span class="mde-nil">·</span>`;
  if (cell.kind === "heap") {
    const val = cell.text != null ? cell.text : cell.preview;
    const cls = `mde-cell-heap mde-heap-${(cell.heap || "").toLowerCase()}`;
    return `<span class="${cls}" title="${cell.length} byte${cell.length === 1 ? "" : "s"}${cell.truncated ? " · truncated" : ""}">${escapeHtml(val ?? "")}${cell.truncated ? "…" : ""}</span>`;
  }
  return renderExplorerCell(cell, null);
}

function renderExplorerCard(t) {
  const ex = state.explorer;
  const win = ex.windows[t.index];
  const focused = t.index === ex.focusIndex;
  let body;
  if (!t.isProjected) {
    body = `<div class="mde-card-empty">This table has ${t.rowCount.toLocaleString()} rows but is not modeled by the projection yet.</div>`;
  } else if (win?.loading && !win.data) {
    body = `<div class="mde-card-empty"><span class="loader"></span> Reading rows…</div>`;
  } else if (win?.error) {
    body = `<div class="mde-card-empty mde-card-error">△ ${escapeHtml(win.error)}</div>`;
  } else if (win?.data) {
    body = renderExplorerGrid(win.data);
  } else {
    body = `<div class="mde-card-empty mde-card-lazy" data-mde-needs-load="${t.index}"><span class="loader"></span> Loading ${t.name}…</div>`;
  }

  const win2 = win?.data;
  const pager = win2 && win2.rows?.length
    ? (() => {
        const from = win2.startRowId;
        const to = win2.startRowId + win2.rows.length - 1;
        const hasPrev = from > 1;
        const hasNext = to < win2.rowCount;
        return `<div class="mde-pager">
          <span>rows ${from.toLocaleString()}–${to.toLocaleString()} of ${win2.rowCount.toLocaleString()}</span>
          <span class="mde-pager-btns">
            <button type="button" data-mde-page="${t.index}:${Math.max(1, from - EXPLORER_PAGE)}" ${hasPrev ? "" : "disabled"}>‹ prev</button>
            <button type="button" data-mde-page="${t.index}:${to + 1}" ${hasNext ? "" : "disabled"}>next ›</button>
          </span>
        </div>`;
      })()
    : "";

  return `
    <section class="mde-card ${focused ? "mde-card-focus" : ""} ${t.isProjected ? "" : "mde-card-dim"}" data-mde-index="${t.index}">
      <div class="mde-card-head">
        <h3>${escapeHtml(t.name)}</h3>
        <span class="mde-card-meta">table ${t.index} · ${t.rowCount.toLocaleString()} row${t.rowCount === 1 ? "" : "s"}</span>
      </div>
      ${body}
      ${pager}
    </section>`;
}

function renderExplorerGrid(data) {
  const ex = state.explorer;
  const cols = data.columns || [];
  const header = `<tr><th class="mde-gutter">#</th>${cols.map(c => `<th title="${escapeHtml(c.kind)}${c.candidateTargets?.length ? " → " + c.candidateTargets.map(explorerTableName).join(", ") : ""}">${escapeHtml(c.name)}</th>`).join("")}</tr>`;
  const rows = (data.rows || []).map(row => {
    const hot = ex.highlight && ex.highlight.index === data.index && ex.highlight.rowId === row.rowId;
    const sel = ex.detail && ex.detail.index === data.index && ex.detail.rowId === row.rowId;
    const cells = row.cells.map((cell, i) => `<td>${renderExplorerCell(cell, cols[i])}</td>`).join("");
    return `<tr class="mde-row ${hot ? "mde-row-hot" : ""} ${sel ? "mde-row-sel" : ""}" data-mde-row="${data.index}:${row.rowId}"><td class="mde-gutter" title="token 0x${(row.token >>> 0).toString(16)}">${row.rowId}</td>${cells}</tr>`;
  }).join("");
  return `<div class="mde-grid-scroll"><table class="mde-grid"><thead>${header}</thead><tbody>${rows}</tbody></table></div>`;
}

function renderExplorerCell(cell, column) {
  if (!cell) return "";
  switch (cell.kind) {
    case "nil":
      return `<span class="mde-nil">·</span>`;
    case "scalar":
      return `<span class="mde-cell-scalar">${escapeHtml(cell.display ?? String(cell.raw ?? ""))}</span>`;
    case "flags":
      return `<span class="mde-cell-flags" title="0x${((cell.raw ?? 0) >>> 0).toString(16)}">${escapeHtml(cell.decoded || String(cell.raw ?? 0))}</span>`;
    case "heap": {
      const val = cell.text != null ? cell.text : cell.preview;
      const cls = `mde-cell-heap mde-heap-${(cell.heap || "").toLowerCase()}`;
      return `<span class="${cls}" title="#${escapeHtml(cell.heap || "")} @${cell.offset} · ${cell.length} byte${cell.length === 1 ? "" : "s"}">${escapeHtml(val ?? "")}${cell.truncated ? "…" : ""}</span>`;
    }
    case "handle": {
      if (!cell.targetRowId) return `<span class="mde-nil">nil</span>`;
      const label = cell.display || `${explorerTableName(cell.targetTable)} #${cell.targetRowId}`;
      return `<button type="button" class="mde-ref" data-mde-jump="${cell.targetTable}:${cell.targetRowId}" title="→ ${escapeHtml(explorerTableName(cell.targetTable))} #${cell.targetRowId}">${escapeHtml(label)}${cell.truncated ? "…" : ""} <span class="mde-ref-arrow">↗</span></button>`;
    }
    case "range": {
      if (!cell.count) return `<span class="mde-nil">empty</span>`;
      return `<button type="button" class="mde-ref mde-ref-range" data-mde-jump="${cell.targetTable}:${cell.startRowId}" title="→ ${escapeHtml(explorerTableName(cell.targetTable))} rows ${cell.startRowId}‥${cell.endRowId}">${escapeHtml(explorerTableName(cell.targetTable))} #${cell.startRowId}‥${cell.endRowId} <span class="mde-ref-count">${cell.count}</span></button>`;
    }
    case "malformed":
      return `<span class="mde-cell-malformed" title="${escapeHtml(cell.detail || "")}">malformed</span>`;
    default:
      return "";
  }
}

// The row inspector: the selected row's cells laid out vertically, labeled by column, with
// handle/range cells still jumpable. A focused "read this one row" companion to the grid.
function renderExplorerDetail() {
  const ex = state.explorer;
  if (!ex.detail) return "";
  const win = ex.windows[ex.detail.index];
  const row = win?.data?.rows?.find(r => r.rowId === ex.detail.rowId);
  if (!row) return "";
  const cols = win.data.columns || [];
  const fields = row.cells.map((cell, i) => `
    <div class="mde-detail-field">
      <span class="mde-detail-k">${escapeHtml(cols[i]?.name || `col ${i}`)}</span>
      <span class="mde-detail-v">${renderExplorerCell(cell, cols[i])}</span>
    </div>`).join("");
  return `
    <aside class="mde-detail">
      <div class="mde-detail-head">
        <span class="mde-detail-title">${escapeHtml(win.data.name)} #${row.rowId}</span>
        <button type="button" class="mde-detail-close" data-mde-detail-close="1" title="Close">✕</button>
      </div>
      <div class="mde-detail-token">token 0x${(row.token >>> 0).toString(16)}</div>
      <div class="mde-detail-fields">${fields}</div>
    </aside>`;
}

let explorerObserver = null;
function bindMetadataExplorerEvents() {
  document.querySelector("#mde-exit")?.addEventListener("click", closeExplorer);
  document.querySelector("#mde-hist-back")?.addEventListener("click", explorerHistoryBack);
  document.querySelector("#mde-hist-fwd")?.addEventListener("click", explorerHistoryForward);
  document.querySelectorAll("[data-mde-chip]").forEach(chip =>
    chip.addEventListener("click", () => pushExplorerFocus({ index: Number(chip.dataset.mdeChip), rowId: 0 })));
  document.querySelectorAll("[data-mde-jump]").forEach(btn =>
    btn.addEventListener("click", event => {
      event.stopPropagation();
      const [index, rowId] = btn.dataset.mdeJump.split(":").map(Number);
      explorerJump(index, rowId);
    }));
  // Clicking a card in the dim wall pulls that table into the focus panel (a spatial jump).
  document.querySelectorAll(".mde-wall .mde-card[data-mde-index] .mde-card-head").forEach(head =>
    head.addEventListener("click", () => {
      const card = head.closest(".mde-card");
      if (card) pushExplorerFocus({ index: Number(card.dataset.mdeIndex), rowId: 0 });
    }));
  document.querySelectorAll(".mde-wall .mde-heap-card[data-mde-heap] .mde-card-head").forEach(head =>
    head.addEventListener("click", () => {
      const card = head.closest(".mde-heap-card");
      if (card) pushExplorerFocus({ heap: card.dataset.mdeHeap });
    }));
  // Selecting a row in the focus panel updates the inspector in place — it refines the current
  // position (remembered for Back/Forward) rather than stacking a new history entry.
  document.querySelectorAll(".mde-focus .mde-row[data-mde-row]").forEach(tr =>
    tr.addEventListener("click", () => {
      const [index, rowId] = tr.dataset.mdeRow.split(":").map(Number);
      const ex = state.explorer;
      ex.detail = { index, rowId };
      ex.highlight = { index, rowId };
      const cur = ex.history[ex.historyPos];
      if (cur && cur.index === index) cur.rowId = rowId;
      render();
    }));
  document.querySelectorAll("[data-mde-page]").forEach(btn =>
    btn.addEventListener("click", () => {
      const [index, start] = btn.dataset.mdePage.split(":").map(Number);
      loadExplorerWindow(index, start);
    }));
  document.querySelectorAll("[data-mde-heap-chip]").forEach(chip =>
    chip.addEventListener("click", () => pushExplorerFocus({ heap: chip.dataset.mdeHeapChip })));
  document.querySelector("[data-mde-detail-close]")?.addEventListener("click", () => {
    const ex = state.explorer;
    ex.detail = null;
    const cur = ex.history[ex.historyPos];
    if (cur && cur.index != null) cur.rowId = 0;
    render();
  });

  // Hydrate cards as they scroll into view (the "wall of tables filling in as you pan" feel).
  explorerObserver?.disconnect();
  explorerObserver = new IntersectionObserver(entries => {
    for (const entry of entries) {
      if (entry.isIntersecting) {
        if (entry.target.dataset.mdeHeapNeedsLoad != null) {
          loadExplorerHeap(entry.target.dataset.mdeHeapNeedsLoad);
        } else {
          loadExplorerWindow(Number(entry.target.dataset.mdeNeedsLoad));
        }
      }
    }
  }, { root: document.querySelector("#mde-canvas"), rootMargin: "200px" });
  document.querySelectorAll("[data-mde-needs-load], [data-mde-heap-needs-load]").forEach(el => explorerObserver.observe(el));

  // Always ensure the focused table or heap is loaded and in view.
  if (state.explorer) {
    if (state.explorer.focusHeap && !state.explorer.heapWindows[state.explorer.focusHeap]) {
      loadExplorerHeap(state.explorer.focusHeap);
    } else if (!state.explorer.focusHeap && !state.explorer.windows[state.explorer.focusIndex]) {
      loadExplorerWindow(state.explorer.focusIndex);
    }
  }
  explorerScrollToFocus();
}


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

  const kindPlural = { class: "classes", struct: "structs", interface: "interfaces", enum: "enums", delegate: "delegates" };

  // Per-library breakdown: group the loaded types by their owning assembly, each
  // with its own types-by-kind. The library is the meaningful unit for
  // measurement — a merged "classes per package" number is noise. The overview
  // reports the public surface (matching the package's headline type count);
  // non-public types are reached via the type nav pane's accessibility filter.
  const libStats = new Map();
  for (const type of pkg.types) {
    if (accessBucket(type.accessibility) !== "public") continue;
    const asm = type.assembly || pkg.assembly || "(unknown)";
    let stat = libStats.get(asm);
    if (!stat) libStats.set(asm, (stat = { types: 0, kinds: new Map() }));
    stat.types++;
    const kind = typeKind(type.kind);
    stat.kinds.set(kind, (stat.kinds.get(kind) || 0) + 1);
  }
  const memberFor = asm => {
    const bare = asm.endsWith(".dll") ? asm.slice(0, -4) : asm;
    const hit = (pkg.assemblies || []).find(a => a.name === asm || a.name === bare || a.name === `${bare}.dll`);
    return hit ? hit.publicMembers : null;
  };
  const libraryRows = [...libStats.entries()]
    .sort((a, b) => b[1].types - a[1].types)
    .map(([asm, stat]) => {
      const name = asm.endsWith(".dll") ? asm.slice(0, -4) : asm;
      const members = memberFor(asm);
      const multi = libStats.size > 1;
      const kinds = KIND_ORDER
        .filter(kind => stat.kinds.has(kind))
        .map(kind => multi
          ? `<button class="lib-kind as-button" data-lib-scope="${escapeHtml(name)}" data-lib-kind="${kind}" title="Show ${kindPlural[kind] || kind} in ${escapeHtml(name)}"><strong>${stat.kinds.get(kind)}</strong> ${kindPlural[kind] || kind}</button>`
          : `<span class="lib-kind"><strong>${stat.kinds.get(kind)}</strong> ${kindPlural[kind] || kind}</span>`)
        .join("");
      const nameCell = multi
        ? `<button class="library-name as-button" data-lib-scope="${escapeHtml(name)}" title="Show all ${escapeHtml(name)} types">${escapeHtml(name)}</button>`
        : `<span class="library-name" title="${escapeHtml(asm)}">${escapeHtml(name)}</span>`;
      return `<div class="library-row">
        <div class="library-row-head">
          ${nameCell}
          <span class="library-metric">${stat.types} type${stat.types === 1 ? "" : "s"}${members != null ? ` · ${members.toLocaleString()} members` : ""}</span>
        </div>
        <div class="library-kinds">${kinds}</div>
      </div>`;
    })
    .join("");

  // For the runtime pack, the loaded set is one library; the static index knows
  // the full roster, so surface how many more libraries this framework carries.
  // Scope the count to the resident pack — the index now spans both the CoreCLR
  // and ASP.NET Core shared frameworks, and conflating them would overcount.
  let librariesSubtitle = `${libStats.size} loaded`;
  if (pkg.isRuntimePack && state.platformIndex) {
    const indexPack = /aspnetcore/i.test(pkg.name || "") ? "aspnetcore.app" : "netcore.app";
    const total = state.platformIndex.assembliesFor(pkg.activeFramework, indexPack).filter(a => a.kind === "impl").length;
    if (total > 0) librariesSubtitle = `${libStats.size} loaded · ${total} in ${escapeHtml(pkg.activeFramework)}`;
  }

  const nsCounts = new Map();
  for (const type of pkg.types) {
    if (accessBucket(type.accessibility) !== "public") continue;
    const ns = type.namespace || "global";
    nsCounts.set(ns, (nsCounts.get(ns) || 0) + 1);
  }
  const namespaces = [...nsCounts.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, 12)
    .map(([ns, count]) => `<button class="type-chip" data-namespace-jump="${escapeHtml(ns)}"><span class="ns-count">${count}</span>${escapeHtml(ns)}</button>`)
    .join("");
  const nsOverflow = nsCounts.size > 12 ? `<span class="ns-overflow">+${nsCounts.size - 12} more</span>` : "";

  // Package-shipped Markdown: README/PACKAGE at the root and skill files under skills/.
  // Presence comes from the surface manifest; the body is fetched on demand when opened.
  const docKindLabel = { readme: "Readme", package: "Package", skill: "Skill" };
  const docKindGlyph = { readme: "▤", package: "▤", skill: "◆" };
  const documents = (pkg.documents || [])
    .map(doc => `<button class="doc-chip doc-${escapeHtml(doc.kind)}" data-doc-path="${escapeHtml(doc.path)}" title="${escapeHtml(doc.path)} · ${doc.size.toLocaleString()} bytes">
        <span class="doc-glyph">${docKindGlyph[doc.kind] || "▤"}</span>
        <span class="doc-name">${escapeHtml(doc.name)}</span>
        <span class="doc-kind">${docKindLabel[doc.kind] || doc.kind}</span>
      </button>`)
    .join("");
  const documentsSection = (pkg.documents || []).length
    ? `<section class="document-section">
      <div class="section-title"><h2>Documentation</h2><span>${pkg.documents.length} file${pkg.documents.length === 1 ? "" : "s"} — click to read</span></div>
      <div class="doc-chip-list">${documents}</div>
    </section>`
    : "";

  return `
    <section class="document-section">
      <div class="section-title"><h2>Target frameworks</h2><span>${pkg.frameworks.length} · active highlighted</span></div>
      <div class="type-chip-list">${frameworks}</div>
    </section>
    <section class="document-section">
      <div class="section-title"><h2>Libraries</h2><span>${librariesSubtitle}</span></div>
      ${pkg.isRuntimePack ? `<div class="library-picker platform-library-picker overview-library-picker">${platformLibrarySelectHtml()}</div>` : ""}
      <div class="library-list">${libraryRows}</div>
    </section>
    <section class="document-section">
      <div class="section-title"><h2>Namespaces</h2><span>${nsCounts.size} — click to filter</span></div>
      <div class="type-chip-list">${namespaces}${nsOverflow}</div>
    </section>${documentsSection}`;
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
      <button class="member-back" id="member-back">← ${escapeHtml(typeDisplayName(type))}</button>
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
          <h1>${escapeHtml(typeDisplayName(type))}.${escapeHtml(member.name)}${parameterTitle(parameters)} ${escapeHtml(pageKind)}</h1>
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
      ? `<div class="graph-scope"><strong>Platform${drilled ? " descent" : ""}</strong><span>${escapeHtml(scope.calleeScope)} · runtime pack</span><strong>Callees</strong><span>depth 2</span></div>`
      : `<div class="graph-scope"><strong>Workspace callers</strong><span>${scope.packages} loaded packages · ${scope.callerAssemblies} scanned assemblies</span><strong>Callees</strong><span>${escapeHtml(scope.calleeScope)} · depth 2</span></div>`;
    content = state.memberCallGraphLoading
      ? `<section class="document-section source-progress"><span class="loader"></span><h2>Building workspace call graph…</h2><p>Scanning implementation IL across ${state.packages.length} loaded package${state.packages.length === 1 ? "" : "s"}.</p></section>`
      : active && active.noBody
        ? `<section class="document-section empty-member-section"><h2>No call graph</h2><p>${escapeHtml(active.callees?.memberName || "This member")} is an abstract or interface method — it declares no IL body, so it has no in-assembly callers or callees to graph.</p></section>`
        : active
        ? `<section class="document-section call-graph-section">
            <div class="section-title"><h2>Call graph</h2><span>${callers.length} caller${callers.length === 1 ? "" : "s"} · ${callees.length} callee${callees.length === 1 ? "" : "s"}</span></div>
            ${breadcrumb}
            ${state.platformDrillLoading
              ? `<div class="graph-expanding"><span class="loader"></span> Range-fetching the implementation assembly from the runtime pack…</div>`
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
      <h1>${escapeHtml(typeDisplayName(item))}</h1>
      <code class="type-signature">${highlight(item.signature)}</code>
    </div>
    <div class="type-metrics"><span><strong>${item.members}</strong> members</span><span><strong>${escapeHtml(item.accessibility || "public")}</strong> accessibility</span></div>
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
        <div class="type-chip-list">${meta.interfaces.map(name => relatedTypeChip(name)).join("")}</div>
      </section>`
    : "";

  const derived = (meta.derivedTypes || []).length
    ? `<section class="document-section">
        <div class="section-title"><h2>Known derived types</h2><span>${meta.derivedTypes.length} in ${escapeHtml(meta.assembly || item.assembly)}</span></div>
        <div class="type-chip-list">${meta.derivedTypes.map(name => relatedTypeChip(name)).join("")}</div>
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
        <div class="section-title"><h2>Type relationships</h2><span>base · interfaces · derived — click a highlighted node to open</span></div>
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
  document.querySelector("[data-platform-open]")?.addEventListener("click", () => openRuntimePackFromHome());
  // Browser-tab behavior for a crowded strip: keep the active tab in view, and let a
  // vertical wheel scroll the horizontal strip so hidden tabs stay reachable.
  const tabStrip = document.querySelector(".package-tabs");
  if (tabStrip) {
    requestAnimationFrame(() =>
      tabStrip.querySelector(".package-tab.active")?.scrollIntoView({ block: "nearest", inline: "nearest" }));
    tabStrip.addEventListener("wheel", event => {
      if (event.deltaY === 0) return;
      event.preventDefault();
      tabStrip.scrollLeft += event.deltaY;
    }, { passive: false });
  }
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
  document.querySelectorAll("[data-lib-scope]").forEach(button => button.addEventListener("click", () => {
    state.atPackageRoot = false;
    state.libraryScope = new Set([button.dataset.libScope]);
    if (state.package?.isRuntimePack) recordPlatformRecent(button.dataset.libScope);
    state.kindFilter = button.dataset.libKind || "";
    state.namespaceFilter = "";
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
  const namespaceJump = document.getElementById("namespace-jump");
  if (namespaceJump) namespaceJump.addEventListener("change", () => {
    state.namespaceFilter = namespaceJump.value;
    state.typeCursor = 0;
    const first = filteredTypes()[0];
    if (first) state.selectedTypeId = first.id;
    state.selectedMemberKey = "";
    render();
  });
  document.querySelectorAll("[data-kind-filter]").forEach(button => button.addEventListener("click", () => {
    state.kindFilter = button.dataset.kindFilter;
    state.typeCursor = 0;
    const first = filteredTypes()[0];
    if (first) state.selectedTypeId = first.id;
    state.selectedMemberKey = "";
    render();
  }));
  document.querySelectorAll("[data-library-chip]").forEach(button => button.addEventListener("click", () => {
    toggleLibraryChip(button.dataset.libraryChip);
    afterLibraryScopeChange();
  }));
  document.querySelectorAll("[data-access-chip]").forEach(button => button.addEventListener("click", () => {
    toggleAccessibilityChip(button.dataset.accessChip);
    afterLibraryScopeChange();
  }));
  const libraryJump = document.getElementById("library-jump");
  if (libraryJump) libraryJump.addEventListener("change", () => {
    state.libraryScope = libraryJump.value ? new Set([libraryJump.value]) : null;
    afterLibraryScopeChange();
  });
  document.querySelectorAll("[data-platform-library-select]").forEach(select => select.addEventListener("change", () => {
    const name = select.value;
    if (!name) return;
    const pack = select.selectedOptions[0]?.dataset.pack || "netcore.app";
    openPlatformLibrary(name, pack);
  }));
  // The lens-scoped library pickers (Integrations, Opportunities, Analysis) scope the scan
  // without leaving the lens: unlike the main platform selector they keep package (platform)
  // root + the active lens, then rescan. Types are loaded too so switching to Types/Overview
  // afterward isn't empty.
  const bindPlatformLensPicker = (dataAttr, lens, loader) => {
    document.querySelectorAll(`[${dataAttr}]`).forEach(select => select.addEventListener("change", async () => {
      const name = select.value;
      if (!name) return;
      const key = name.replace(/\.dll$/i, "");
      const pack = select.selectedOptions[0]?.dataset.pack || platformPackForAssembly(key);
      const resident = (runtimePackPackage()?.types || []).some(type => libraryKey(type) === key);
      if (!resident) await loadRuntimePackAssembly(platformScopeTfm(), `${key}.dll`, pack);
      state.libraryScope = new Set([key]);
      recordPlatformRecent(key, pack);
      state.atPackageRoot = true;
      state.packageLens = lens;
      loader();
    }));
  };
  bindPlatformLensPicker("data-platform-integrations-library", "integrations", loadPackageIntegrations);
  bindPlatformLensPicker("data-platform-opportunities-library", "opportunities", loadPackageOpportunities);
  bindPlatformLensPicker("data-platform-analysis-library", "analysis", loadPackagePerformance);
  bindPlatformLensPicker("data-platform-metadata-library", "metadata", loadPackageMetadata);
  document.querySelectorAll("[data-mde-open]").forEach(btn =>
    btn.addEventListener("click", () => {
      const [assembly, tableIndex] = btn.dataset.mdeOpen.split("|");
      openExplorer(assembly, Number(tableIndex));
    }));
  document.querySelectorAll("[data-mde-open-heap]").forEach(btn =>
    btn.addEventListener("click", () => {
      const [assembly, heapName] = btn.dataset.mdeOpenHeap.split("|");
      openExplorerHeap(assembly, heapName);
    }));
  bindCommandCompletionClicks(document);

  document.querySelector("#framework").addEventListener("change", event => {
    loadPackage(state.package.id, state.package.version, event.target.value);
  });
  document.querySelector("#package-version")?.addEventListener("change", event => {
    if (state.package?.isRuntimePack) switchPlatformVersion(event.target.value);
    else switchPackageVersion(event.target.value);
  });
  ensurePackageVersions(state.package);
  if (state.package?.isRuntimePack) ensureDotnetReleases();
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
  document.querySelectorAll("[data-doc-path]").forEach(button =>
    button.addEventListener("click", () => openPackageDocument(button.dataset.docPath)));
  document.querySelector("#doc-viewer-backdrop")?.addEventListener("mousedown", event => {
    if (event.target.id === "doc-viewer-backdrop") closeDocViewer();
  });
  document.querySelector("#doc-viewer-close")?.addEventListener("click", closeDocViewer);
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
    state.libraryScope = null;
    state.accessibilityFilter = new Set(["public"]);
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
  document.querySelector("#open-settings")?.addEventListener("click", () => openSettings("workbench"));
  document.querySelector("#help").addEventListener("click", () => showToast("⌘K command · ⌘P / type to find a type · ⌘F filter · 1—5 lenses · ↑↓ types · Alt+←/→ back/forward · graph: wheel zoom, click node to open, +/− zoom, 0 fit, arrows pan"));
}

function toggleTheme() {
  setTheme(state.theme === "dark" ? "light" : "dark");
}

// Apply and persist a specific theme, refreshing any live graphs whose colors are theme-bound.
function setTheme(theme) {
  state.theme = theme === "light" ? "light" : "dark";
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
  { id: "runtime", label: "Platform" },
];

const PLATFORM_PACK_LABEL = { "netcore.app": ".NET", "aspnetcore.app": "ASP.NET Core" };

// The target framework the Platform scope resolves libraries against. A resident Platform
// pack's own framework is authoritative — even a preview TFM (e.g. net11.0) the static index
// does not carry yet, whose roster is then honestly empty rather than silently another
// major's libraries. With no resident pack (home Platform scope), prefer the focused
// package's framework, then net10.0 — always clamped to a TFM the static index carries.
function platformScopeTfm() {
  const idx = state.platformIndex;
  const known = idx ? idx.tfms() : [];
  const resident = runtimePackPackage()?.activeFramework;
  if (resident) return resident;
  const inIndex = tfm => tfm && (!idx || known.includes(tfm));
  for (const candidate of [state.package?.activeFramework, "net10.0"]) {
    if (inIndex(candidate)) return candidate;
  }
  return known.includes("net10.0") ? "net10.0" : (known[known.length - 1] || "net10.0");
}

// Index-first library roster for the Platform scope: every implementation
// assembly the static platform index knows for the active TFM, across the
// CoreCLR (netcore.app) and ASP.NET Core (aspnetcore.app) shared frameworks —
// with NO pack download. Each library drills in by fetching just that one
// assembly from its shared-framework pack. Matched on assembly name; sorted
// CoreCLR first, then by public-type count so the biggest libraries surface
// first.
function platformLibraryRoster(query) {
  const idx = state.platformIndex;
  if (!idx) return [];
  const tfm = platformScopeTfm();
  const lower = query.trim().toLowerCase();
  const rt = runtimePackPackage();
  const loadedKeys = new Set((rt?.assemblies || []).map(a => (a.name || "").replace(/\.dll$/i, "")));
  const rows = [];
  for (const pack of ["netcore.app", "aspnetcore.app"]) {
    for (const row of idx.assembliesFor(tfm, pack)) {
      if (row.kind !== "impl") continue;
      if (lower && !row.assembly.toLowerCase().includes(lower)) continue;
      rows.push({
        assembly: row.assembly,
        pack,
        publicTypes: row.publicTypes,
        loaded: loadedKeys.has(row.assembly),
        ranges: computeHighlightRanges(row.assembly, lower),
      });
    }
  }
  rows.sort((a, b) =>
    (a.pack === b.pack ? 0 : a.pack === "netcore.app" ? -1 : 1)
    || b.publicTypes - a.publicTypes
    || a.assembly.localeCompare(b.assembly));
  return rows;
}

// Which shared framework an assembly ships in, resolved from the static index
// roster (defaulting to CoreCLR). Used when recording a recent library from a
// context that does not already carry the pack token.
function platformPackForAssembly(key) {
  const hit = platformLibraryRoster("").find(lib => lib.assembly === key);
  return hit ? hit.pack : "netcore.app";
}

// Remember an opened platform library at the front of the recent list (most-recent
// first, deduped, capped) and persist it. Recent duplicates the .NET / ASP.NET Core
// catalog groups by design — no cross-group de-dupe.
function recordPlatformRecent(assembly, pack) {
  const key = (assembly || "").replace(/\.dll$/i, "");
  if (!key) return;
  const normPack = pack === "aspnetcore.app" ? "aspnetcore.app"
    : pack === "netcore.app" ? "netcore.app"
    : platformPackForAssembly(key);
  const rest = (state.platformRecent || []).filter(entry => entry.assembly !== key);
  state.platformRecent = [{ assembly: key, pack: normPack }, ...rest].slice(0, PLATFORM_RECENT_MAX);
  try {
    localStorage.setItem("inspect-platform-recent", JSON.stringify(state.platformRecent));
  } catch {
    // Persistence is best-effort; an in-memory recent list still works this session.
  }
}

// Remember an opened NuGet package at the front of the recent list (most-recent first,
// deduped by id, capped) and persist it, so the Home listing survives a refresh. Called
// only from a successful open, never from search hits or prefetches. The resident runtime
// pseudo-package has no nupkg and is excluded.
function recordRecentPackage(id, version, framework) {
  if (!id || isRuntimePackId(id)) return;
  const rest = (state.recentPackages || []).filter(entry => entry.id.toLowerCase() !== id.toLowerCase());
  state.recentPackages = [
    { id, version: version || "latest", framework: framework || "" },
    ...rest,
  ].slice(0, RECENT_PACKAGES_MAX);
  try {
    localStorage.setItem("inspect-recent-packages", JSON.stringify(state.recentPackages));
  } catch {
    // Persistence is best-effort; the in-memory list still works this session.
  }
}

// The most-recently-opened library that is actually available in the active
// platform framework's roster, or null. Lets the Platform land on the library you
// were last looking at instead of the aggregate overview.
function mostRecentAvailableLibrary() {
  const roster = platformLibraryRoster("");
  if (!roster.length) return null;
  const byAssembly = new Map(roster.map(lib => [lib.assembly, lib]));
  for (const entry of state.platformRecent || []) {
    const hit = byAssembly.get(entry.assembly);
    if (hit) return { assembly: hit.assembly, pack: hit.pack };
  }
  return null;
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
    if (state.runtimePackError && !runtimePackLoaded()) {
      results.push({ kind: "rtpack-status", error: state.runtimePackError });
    }
    // Index-first: the platform library roster needs no pack download. Selecting
    // "Platform" instantly lists the CoreCLR + ASP.NET Core libraries the static
    // index knows for the active framework, filterable by name.
    const roster = platformLibraryRoster(query);
    for (const lib of roster.slice(0, 200)) {
      results.push({ ...lib, kind: "platform-lib" });
    }
    // Once a pack is resident, blend its type/member matches so drilled-in
    // platform content stays searchable alongside the library roster.
    if (runtimePackLoaded()) {
      const typeSource = query ? spotlightTypeMatches(query) : [];
      for (const match of typeSource.filter(item => item.pkg?.isRuntimePack).slice(0, 50)) {
        results.push({ ...match, kind: "type" });
      }
      if (query) {
        for (const match of spotlightMemberMatches(query).filter(item => item.pkg?.isRuntimePack).slice(0, 50)) {
          results.push({ ...match, kind: "member" });
        }
      }
    }
    // Only when the static index is unavailable do we fall back to the old
    // download-first prompt, so the scope is never empty and inert.
    if (!roster.length && !runtimePackLoaded()) {
      results.push({ kind: "rtpack-suggest" });
    }
    return results;
  }

  if (all || scope === "packages") {
    const loaded = spotlightLoadedPackageMatches(query).slice(0, all ? 3 : 20);
    for (const match of loaded) results.push({ kind: "pkg-loaded", pkg: match.pkg, ranges: match.ranges });
    const openIds = new Set(state.packages.map(pkg => pkg.id.toLowerCase()));
    // Persisted recently-opened packages that are not currently open. These carry the
    // Home listing across a refresh (the in-memory workspace is gone); re-opening one
    // refetches its nupkg (fast from the browser HTTP cache).
    const lowerQuery = query.toLowerCase();
    const recentShown = new Set();
    for (const entry of state.recentPackages || []) {
      const key = entry.id.toLowerCase();
      if (openIds.has(key) || recentShown.has(key)) continue;
      if (lowerQuery && !key.includes(lowerQuery)) continue;
      recentShown.add(key);
      results.push({ kind: "pkg-recent", entry, ranges: computeHighlightRanges(entry.id, lowerQuery) });
      if (all && recentShown.size >= 6) break;
    }
    let added = 0;
    for (const hit of state.spotlightPkgHits) {
      if (openIds.has(hit.id.toLowerCase()) || recentShown.has(hit.id.toLowerCase())) continue;
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
  if (result.kind === "pkg-recent") {
    const ver = result.entry.version && result.entry.version !== "latest" ? result.entry.version : "";
    return `<button ${base} data-sl-pkg-recent="${escapeHtml(result.entry.id)}">
      <span class="kind-icon sl-pkg">▣</span>
      <span class="spotlight-item-name">${highlightRanges(result.entry.id, result.ranges)}</span>
      <span class="spotlight-item-ns">${ver ? `${escapeHtml(ver)} · ` : ""}recent</span>
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
  if (result.kind === "platform-lib") {
    const label = PLATFORM_PACK_LABEL[result.pack] || result.pack;
    const types = `${result.publicTypes} type${result.publicTypes === 1 ? "" : "s"}`;
    const meta = `${label} · ${types}${result.loaded ? " · loaded" : ""}`;
    return `<button ${base} data-sl-platform-lib="${escapeHtml(result.assembly)}" data-sl-platform-pack="${escapeHtml(result.pack)}">
      <span class="kind-icon sl-lib">▤</span>
      <span class="spotlight-item-name">${highlightRanges(result.assembly, result.ranges)}</span>
      <span class="spotlight-item-ns">${escapeHtml(meta)}</span>
    </button>`;
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

const SPOTLIGHT_GROUP_LABELS = { "pkg-recent": "Recent", "pkg-loaded": "Packages", "pkg-nuget": "Packages", type: "Types", member: "Members", "platform-lib": "Libraries", "rtpack-suggest": "Runtime", "rtpack-status": "Runtime" };

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
      if (group && group !== lastGroup) {
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
  // The Platform scope is now index-first: selecting it lists the platform
  // library roster from the static index with no download. A pack loads lazily
  // only when the user drills into a specific library.
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

// Compare two NuGet SemVer-ish versions descending (newest first). Falls back to string
// comparison for non-numeric pre-release tails so the list stays deterministic.
function compareVersionsDesc(a, b) {
  const parse = v => String(v).split(/[.\-+]/).map(part => (/^\d+$/.test(part) ? Number(part) : part));
  const pa = parse(a);
  const pb = parse(b);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const x = pa[i];
    const y = pb[i];
    if (x === y) continue;
    if (x === undefined) return 1;   // shorter (release) sorts before its prerelease
    if (y === undefined) return -1;
    if (typeof x === "number" && typeof y === "number") return y - x;
    return String(y).localeCompare(String(x));
  }
  return 0;
}

// Build the <option> list for the version selector. Always includes the currently loaded
// version (even before the flatcontainer index has been fetched) so the control is never empty.
function versionOptionsHtml(pkg) {
  if (pkg.isRuntimePack) return platformVersionOptionsHtml(pkg);
  const idLower = pkg.id.toLowerCase();
  const fetched = state.packageVersions[idLower] ?? [];
  const versions = fetched.length ? fetched.slice() : [pkg.version];
  if (!versions.some(v => v.toLowerCase() === pkg.version.toLowerCase())) {
    versions.unshift(pkg.version);
    versions.sort(compareVersionsDesc);
  }
  return versions
    .map(v => `<option value="${escapeHtml(v)}" ${v.toLowerCase() === pkg.version.toLowerCase() ? "selected" : ""}>${escapeHtml(v)}</option>`)
    .join("");
}

// The Platform version selector's options: one entry per in-support .NET major (8+) from the
// dotnet/core releases index, each labelled with that channel's latest release — the latest
// stable patch for stable majors, the latest preview for a preview major (e.g. .NET 11). The
// option value is the TFM (net8.0 …) so a change reloads the whole Platform at that major. A
// preview major whose TFM the bundled library index doesn't carry loads (CoreLib browsing)
// but offers no library roster yet — honest, not hidden. The active TFM is always present so
// the control is never empty before the index loads.
function platformVersionOptionsHtml(pkg) {
  const releases = state.dotnetReleases || [];
  const list = releases.map(r => ({ tfm: r.tfm, version: r.version }));
  if (!list.some(r => r.tfm === pkg.activeFramework)) {
    list.unshift({ tfm: pkg.activeFramework, version: pkg.version });
  }
  return list
    .map(r => `<option value="${escapeHtml(r.tfm)}" ${r.tfm === pkg.activeFramework ? "selected" : ""}>${escapeHtml(r.version)}</option>`)
    .join("");
}

// Lazily fetch the .NET release channels (latest patch per major) from the dotnet/core
// release index (CORS-enabled), keep only in-support majors (8+), cache them, and repaint the
// Platform version selector in place. Powers the Platform version dropdown; a transient
// failure leaves the selector on the single current-version option.
async function ensureDotnetReleases() {
  if (state.dotnetReleases || state.dotnetReleasesLoading) return;
  state.dotnetReleasesLoading = true;
  try {
    const url = "https://raw.githubusercontent.com/dotnet/core/refs/heads/main/release-notes/releases-index.json";
    const response = await fetch(url);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const payload = await response.json();
    const rows = (payload["releases-index"] || [])
      .map(entry => {
        const major = parseInt(entry["channel-version"], 10);
        return { major, tfm: `net${entry["channel-version"]}`, version: entry["latest-release"] };
      })
      .filter(row => Number.isFinite(row.major) && row.major >= 8 && row.version)
      .sort((a, b) => b.major - a.major);
    state.dotnetReleases = rows;
    if (state.package?.isRuntimePack) {
      const select = document.querySelector("#package-version");
      if (select) select.innerHTML = versionOptionsHtml(state.package);
    }
  } catch {
    // Leave the selector on the single current-version option; a transient index failure
    // must not break the workbench.
  } finally {
    state.dotnetReleasesLoading = false;
  }
}

// Switch the resident Platform to a different .NET major (by TFM). Drops the current
// pseudo-package and its accumulated drilled libraries, then loads a fresh Platform for the
// chosen TFM and lands on its overview — mirroring the in-place version switch for ordinary
// packages. The engine resolves the exact latest patch for that major.
async function switchPlatformVersion(tfm) {
  const pkg = runtimePackPackage();
  if (!pkg || !tfm || tfm === pkg.activeFramework) return;
  state.packages = state.packages.filter(item => !item.isRuntimePack);
  state.libraryScope = null;
  state.platformStack = [];
  state.home = false;
  state.loading = true;
  state.error = "";
  state.loadingMessage = "Loading the .NET Platform…";
  state.loadingSubtitle = `.NET Platform · ${tfm}`;
  render();
  const loaded = await loadRuntimePack(tfm);
  if (!loaded) {
    state.loading = false;
    state.error = state.runtimePackError || "Couldn’t load the .NET Platform.";
    state.errorTitle = "Platform failed";
    render();
    return;
  }
  state.package = loaded;
  state.loading = false;
  state.atPackageRoot = true;
  state.packageLens = "overview";
  state.selectedTypeId = loaded.types[0]?.id || "";
  state.selectedMemberKey = "";
  state.selectedOverloadIndex = null;
  state.typeFilter = "";
  state.namespaceFilter = "";
  state.kindFilter = "";
  render();
  loadSelectionData();
}

// Lazily fetch the full published-version list for a package straight from the NuGet
// flatcontainer index (CORS-enabled), cache it, and repaint the version selector in place.
async function ensurePackageVersions(pkg) {
  if (!pkg || pkg.isRuntimePack) return;
  const idLower = pkg.id.toLowerCase();
  if (state.packageVersions[idLower] || state.packageVersionsLoading[idLower]) return;
  state.packageVersionsLoading[idLower] = true;
  try {
    const url = `https://api.nuget.org/v3-flatcontainer/${encodeURIComponent(idLower)}/index.json`;
    const response = await fetch(url);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const payload = await response.json();
    const versions = (payload.versions || []).slice().sort(compareVersionsDesc);
    state.packageVersions[idLower] = versions;
    updateVersionSelect(idLower);
  } catch {
    // Leave the selector on the single current-version option; a transient index failure
    // must not break the workbench.
  } finally {
    state.packageVersionsLoading[idLower] = false;
  }
}

// Repaint just the version <select> options without a full re-render, so an async index
// fetch never disturbs focus, scroll, or the rest of the workbench.
function updateVersionSelect(idLower) {
  if (!state.package || state.package.id.toLowerCase() !== idLower) return;
  const select = document.querySelector("#package-version");
  if (select) select.innerHTML = versionOptionsHtml(state.package);
}

// Switch the current package to a different published version. Replaces the current tab in
// place (drops the previous version's entry) so the selector mutates this package rather than
// spawning a second tab, mirroring a browser's version picker.
async function switchPackageVersion(newVersion) {
  const pkg = state.package;
  if (!pkg || pkg.isRuntimePack) return;
  const id = pkg.id;
  const oldVersion = pkg.version;
  if (!newVersion || newVersion.toLowerCase() === oldVersion.toLowerCase()) return;
  const framework = pkg.activeFramework;
  const loaded = await loadPackage(id, newVersion, framework);
  if (loaded && loaded.version.toLowerCase() !== oldVersion.toLowerCase()) {
    const before = state.packages.length;
    state.packages = state.packages.filter(item =>
      !(item.id.toLowerCase() === id.toLowerCase() && item.version.toLowerCase() === oldVersion.toLowerCase()));
    if (state.packages.length !== before) render();
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
    case "pkg-recent": closeSpotlight(); loadPackage(result.entry.id, result.entry.version, result.entry.framework); break;
    case "member": pickSpotlightMember(result); break;
    case "rtpack-suggest": state.spotlightScope = "runtime"; state.spotlightIndex = 0; activateRuntimePack(); break;
    case "platform-lib": openPlatformLibrary(result.assembly, result.pack); break;
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

// Drill into one platform library from the index-first Platform scope: lazily fetch just
// that assembly from its shared-framework pack (CoreCLR or ASP.NET Core), creating or
// extending the resident runtime pseudo-package, then scope the workbench to it and land in
// its type list. The download happens only here, on demand — selecting the Platform scope
// itself never downloads.
async function openPlatformLibrary(assembly, pack) {
  closeSpotlight();
  const key = (assembly || "").replace(/\.dll$/i, "");
  const fileName = key ? `${key}.dll` : "";
  const tfm = platformScopeTfm();
  const alreadyLoaded = (runtimePackPackage()?.types || []).some(type => libraryKey(type) === key);
  if (!alreadyLoaded) {
    state.home = false;
    state.loading = true;
    state.error = "";
    state.loadingMessage = "Loading the platform library…";
    state.loadingSubtitle = `${key} · ${tfm}`;
    render();
    const loaded = await loadRuntimePackAssembly(tfm, fileName, pack);
    if (!loaded) {
      state.loading = false;
      state.error = state.runtimePackError
        ? `Couldn’t load ${key}: ${state.runtimePackError}`
        : `Couldn’t load ${key} from the .NET runtime pack.`;
      state.errorTitle = "Platform library failed";
      render();
      return;
    }
  }
  const pkg = runtimePackPackage();
  if (!pkg) { render(); return; }
  state.package = pkg;
  state.home = false;
  state.loading = false;
  const hasLib = pkg.types.some(type => libraryKey(type) === key);
  state.libraryScope = hasLib ? new Set([key]) : null;
  if (hasLib) recordPlatformRecent(key, pack);
  state.atPackageRoot = !hasLib; // scoped → jump straight to the type list; otherwise the overview
  state.packageLens = "overview";
  state.namespaceFilter = "";
  state.typeFilter = "";
  state.kindFilter = "";
  const scoped = filteredTypes();
  state.selectedTypeId = scoped[0]?.id || pkg.types[0]?.id || "";
  state.selectedMemberKey = "";
  state.selectedOverloadIndex = null;
  render();
  loadSelectionData();
}

function pickSpotlightLoadedPackage(pkg) {
  const target = state.packages.find(item => item.id === pkg.id) || pkg;
  state.home = false;
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
  state.home = false;
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
  state.home = false;
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
          <button id="home-settings" aria-label="Open settings" title="Settings">⚙</button>
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
              <button class="home-demo" data-home-demo="runtime"><strong>.NET Platform</strong><small>Inspect platform BCL types</small></button>
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
  document.querySelector("#home-settings")?.addEventListener("click", () => openSettings("home"));
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

// The two package demos jump to a rich, curated deep link (open tabs + selected type +,
// for the platform, a scoped library) so the buttons showcase the workbench, not a bare
// package root. pushState keeps them shareable/refreshable; the workspace restore reuses
// the same path as a shared link. The call-graph demo stays a bespoke multi-package load.
const HOME_DEMO_LINKS = {
  stj: "?package=System.Text.Json&w=eyJ0IjpbWyJTeXN0ZW0uVGV4dC5Kc29uIiwiMTAuMC4wIiwibmV0MTAuMCJdXSwiYSI6MCwieSI6IlN5c3RlbS5UZXh0Lkpzb24uSnNvblNlcmlhbGl6ZXIifQ",
  runtime: "?package=Microsoft.NETCore.App&w=eyJ0IjpbWyJTeXN0ZW0uVGV4dC5Kc29uIiwiMTAuMC4wIiwibmV0MTAuMCJdLFsiTWljcm9zb2Z0Lk5FVENvcmUuQXBwIiwiMTAuMC4xMCIsIm5ldDEwLjAiXV0sImEiOjEsImwiOiJTeXN0ZW0uUHJpdmF0ZS5Db3JlTGliIiwieSI6IlN5c3RlbS5Db2xsZWN0aW9ucy5HZW5lcmljLkxpc3RgMSJ9"
};

function runHomeDemo(kind) {
  state.home = false;
  if (kind === "callgraph") { runCallGraphDemo(); return; }
  const link = HOME_DEMO_LINKS[kind];
  if (!link) return;
  try { history.pushState(null, "", link); } catch {}
  const loc = parseLocation();
  restoreWorkspaceFromLocation(loc, {
    type: loc.type, member: loc.member, overload: loc.overload, section: loc.section
  });
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
  state.loadingMessage = "Loading the .NET Platform…";
  state.loadingSubtitle = ".NET Platform · net10.0";
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
  state.home = false;
  state.loading = false;
  // Start on the library you were last looking at, not the aggregate overview,
  // when one is available in this framework's roster.
  const recent = mostRecentAvailableLibrary();
  if (recent) {
    await openPlatformLibrary(recent.assembly, recent.pack);
    return;
  }
  state.atPackageRoot = true;
  state.packageLens = "overview";
  state.selectedTypeId = pack.types[0]?.id || "";
  state.selectedMemberKey = "";
  state.selectedOverloadIndex = null;
  render();
  loadSelectionData();
}

// The inspector-bot mascot series shown on interstitial (loading) screens. Each entry is a
// color variant of the same dotnet-bot-inspector character living in /assets/bots/. To grow
// the series, drop a new PNG in that folder and add its basename here — nothing else needed.
const BOT_ART = [
  "dotnet-inspect-bot-violet",
  "dotnet-inspect-bot-teal",
  "dotnet-inspect-bot-azure",
  "dotnet-inspect-bot-magenta",
  "dotnet-inspect-bot-crimson",
  "dotnet-inspect-bot-amber"
];

// One random bot is chosen per interstitial *appearance* and held for the life of that
// appearance (the loading message ticks re-render, but the bot must not flicker). It is reset
// to null whenever a non-loading view renders (see render()), so the NEXT loading screen picks
// a fresh random bot.
let loadingBotSrc = null;
function interstitialBotSrc() {
  if (!loadingBotSrc) {
    loadingBotSrc = `/assets/bots/${BOT_ART[Math.floor(Math.random() * BOT_ART.length)]}.png`;
  }
  return loadingBotSrc;
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
        : `<div class="load-progress"><img class="loading-bot" src="${interstitialBotSrc()}" width="200" height="200" alt="dotnet-bot inspector mascot" /><span class="loader"></span><strong>${escapeHtml(state.loadingMessage)}</strong><small>${state.loadingSubtitle ? escapeHtml(state.loadingSubtitle) : `${escapeHtml(state.requestedPackage)}@${escapeHtml(state.requestedVersion)} · ${escapeHtml(state.requestedFramework || "best framework")}`}</small></div>`}
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
      if (target) {
        node.classList.add("nav-node");
        node.style.cursor = "pointer";
        node.addEventListener("click", () => navigateToTypeByName(fullName));
        return;
      }
      // Reported by metadata but not in the browsable surface (internal type or a
      // type in another assembly). Mark it non-navigable with a native tooltip so
      // the dead node reads as informational rather than broken.
      node.classList.add("non-nav");
      const title = document.createElementNS("http://www.w3.org/2000/svg", "title");
      title.textContent = `${fullName} — not in the browsable public surface`;
      node.insertBefore(title, node.firstChild);
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
  // Clicking a non-public related type (e.g. an internal derived implementer)
  // enables its accessibility bucket so it appears in the nav list rather than
  // being filtered out by the public-by-default view.
  const bucket = accessBucket(target.accessibility);
  if (!state.accessibilityFilter.has(bucket)) {
    const next = new Set(state.accessibilityFilter);
    next.add(bucket);
    state.accessibilityFilter = next;
  }
  state.selectedTypeId = target.id;
  state.selectedMemberKey = "";
  state.memberKindFilter = "all";
  state.typeCursor = filteredTypes().findIndex(candidate => candidate.id === target.id);
  render();
}

// A related type (interface / base / derived) is only openable if it is part of
// the loaded surface. Non-public implementers in the loaded assemblies are now
// included (with an accessibility filter), so only types in OTHER assemblies
// remain unbrowsable.
function typeIsNavigable(fullName) {
  return !!state.package && state.package.types.some(candidate => candidate.id === fullName);
}

// Render a related-type chip: an active button when it resolves to a browsable
// type in the loaded surface, otherwise a static chip that explains why it can't
// be opened (it lives in another assembly).
function relatedTypeChip(name) {
  const short = escapeHtml(shortTypeName(name));
  if (typeIsNavigable(name)) {
    return `<button class="type-chip" data-graph-type="${escapeHtml(name)}" title="${escapeHtml(name)}">${short}</button>`;
  }
  return `<span class="type-chip is-static" title="${escapeHtml(name)} — not in the loaded surface (in another assembly)">${short}</span>`;
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
    // Cap at 1:1 so a tiny graph (e.g. two nodes) renders at its natural size,
    // centered, instead of being upscaled to fill the tall viewport.
    const fitScale = Math.min(rect.width / naturalWidth, rect.height / naturalHeight) * 0.92;
    view.scale = clampScale(Math.min(fitScale, 1));
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

// Lazily load marked + DOMPurify (mirrors the mermaid CDN-ESM pattern). marked renders GFM
// (tables, fenced code); DOMPurify strips any embedded HTML/script so third-party package
// Markdown can never inject active content into the app.
async function markdownLibs() {
  markdownModule ??= Promise.all([
    import("https://cdn.jsdelivr.net/npm/marked@15.0.7/lib/marked.esm.js"),
    import("https://cdn.jsdelivr.net/npm/dompurify@3.2.4/dist/purify.es.mjs")
  ]);
  const [{ marked }, { default: DOMPurify }] = await markdownModule;
  return { marked, DOMPurify };
}

async function renderMarkdown(text) {
  const { marked, DOMPurify } = await markdownLibs();
  const html = marked.parse(String(text ?? ""), { gfm: true, breaks: false });
  return DOMPurify.sanitize(html, { USE_PROFILES: { html: true } });
}

async function renderMarkdownInline(text) {
  const { marked, DOMPurify } = await markdownLibs();
  const html = marked.parseInline(String(text ?? ""), { gfm: true });
  return DOMPurify.sanitize(html, { USE_PROFILES: { html: true } });
}

// Skill files carry a leading YAML frontmatter block (---\n…\n---). Rendered as Markdown it turns
// into a mangled setext heading, so split it out: parse name/version/description (handling folded
// >-/> and literal |/|- block scalars) and hand back the remaining body for normal rendering.
function splitFrontmatter(text) {
  const source = String(text ?? "");
  const match = /^\uFEFF?---\r?\n([\s\S]*?)\r?\n---\r?\n?/.exec(source);
  if (!match) return { meta: null, body: source };
  const meta = {};
  const lines = match[1].split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const kv = /^([A-Za-z0-9_-]+):\s?(.*)$/.exec(lines[i]);
    if (!kv) continue;
    let value = kv[2];
    if (value === ">" || value === ">-" || value === "|" || value === "|-") {
      const folded = value.startsWith(">");
      const buffer = [];
      while (i + 1 < lines.length && (/^\s+\S/.test(lines[i + 1]) || lines[i + 1].trim() === "")) {
        buffer.push(lines[++i].trim());
      }
      value = buffer.join(folded ? " " : "\n").trim();
    }
    meta[kv[1]] = value.trim();
  }
  return { meta, body: source.slice(match[0].length) };
}

async function openPackageDocument(path) {
  const pkg = state.package;
  const doc = (pkg?.documents || []).find(candidate => candidate.path === path);
  if (!pkg || !doc) return;
  state.docViewerOpen = true;
  state.docViewer = doc;
  state.docViewerHtml = "";
  state.docViewerMeta = null;
  state.docViewerError = "";
  state.docViewerLoading = true;
  render();
  try {
    const content = await inspectPackageDocument({ packageId: pkg.id, version: pkg.version, path });
    const { meta, body } = splitFrontmatter(content.text);
    state.docViewerHtml = await renderMarkdown(body);
    state.docViewerMeta = meta && (meta.name || meta.description)
      ? {
          name: meta.name || doc.name,
          version: meta.version || "",
          descriptionHtml: meta.description ? await renderMarkdownInline(meta.description) : ""
        }
      : null;
  } catch (error) {
    state.docViewerError = String(error?.message || error);
  } finally {
    state.docViewerLoading = false;
    render();
  }
}

function closeDocViewer() {
  state.docViewerOpen = false;
  state.docViewer = null;
  state.docViewerHtml = "";
  state.docViewerMeta = null;
  state.docViewerError = "";
  state.docViewerLoading = false;
  render();
}

function renderDocViewer() {
  const doc = state.docViewer;
  const title = doc ? `${doc.name}` : "Document";
  const subtitle = doc ? doc.path : "";
  const meta = state.docViewerMeta;
  const metaCard = meta
    ? `<div class="doc-frontmatter">
        <div class="doc-fm-head"><strong>${escapeHtml(meta.name)}</strong>${meta.version ? `<span class="doc-fm-version">v${escapeHtml(meta.version)}</span>` : ""}</div>
        ${meta.descriptionHtml ? `<p class="doc-fm-desc">${meta.descriptionHtml}</p>` : ""}
      </div>`
    : "";
  const body = state.docViewerLoading
    ? `<div class="doc-viewer-status">Loading ${escapeHtml(title)}…</div>`
    : state.docViewerError
      ? `<div class="doc-viewer-status error">${escapeHtml(state.docViewerError)}</div>`
      : `${metaCard}<article class="markdown-body">${state.docViewerHtml}</article>`;
  return `
    <div class="doc-viewer-backdrop" id="doc-viewer-backdrop">
      <div class="doc-viewer" role="dialog" aria-modal="true" aria-label="Package document">
        <div class="doc-viewer-head">
          <span class="doc-viewer-title">${escapeHtml(title)}<small>${escapeHtml(subtitle)}</small></span>
          <button id="doc-viewer-close" type="button" aria-label="Close">esc</button>
        </div>
        <div class="doc-viewer-body">${body}</div>
      </div>
    </div>`;
}

const TASTE_TIERS = [
  ["Formatting", "Formatting"],
  ["Spelling", "Spelling (this.)"],
  ["Lens", "Lenses · byte-divergent"],
  ["Synthesis", "Name synthesis"]
];

// The decompiler style ("taste") catalog, grouped by tier, as checkbox rows. Shared by the
// detail-view taste popover and the Settings page so both stay in lockstep with the engine's
// StyleOptionCatalog (fetched once into state.styleOptions).
function styleCatalogGroupsHtml() {
  const options = state.styleOptions || [];
  if (!options.length) return "";
  return TASTE_TIERS
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
      </div>`).join("");
}

function renderTastePopover() {
  const groups = styleCatalogGroupsHtml();
  const body = groups || '<div class="taste-empty">Style catalog unavailable.</div>';
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

// Open the Settings page, remembering where to return (the home page vs. the workbench) so
// closing restores that view without touching the URL.
function openSettings(from) {
  state.settingsReturn = from === "workbench" ? "workbench" : "home";
  state.settings = true;
  state.tasteOpen = false;
  render();
}

function closeSettings() {
  state.settings = false;
  render();
}

// The Settings page: a persistent preferences panel. Every control here writes straight to
// localStorage (theme → inspect-theme, taste → inspect-taste) so choices survive a reload and
// future sessions. Grouped into Appearance and Decompiler style; the latter reuses the same
// style-option catalog the detail-view taste popover shows.
function renderSettingsView() {
  const catalog = styleCatalogGroupsHtml();
  const styleBody = catalog
    || '<div class="taste-empty">Style catalog is still loading — reopen Settings in a moment.</div>';
  const activeCount = state.taste.length;
  app.innerHTML = `
    <div class="settings-page">
      <header class="settings-bar">
        <a class="brand" href="/" aria-label="dotnet inspect home"><span class="brand-glyph">◇</span><span>dotnet-inspect</span></a>
        <button id="settings-close" class="settings-close">${state.settingsReturn === "workbench" ? "back to workbench" : "back to home"} ✕</button>
      </header>
      <main class="settings-main">
        <div class="settings-head">
          <h1>Settings</h1>
          <p class="settings-lede">Preferences are stored locally in your browser and persist across sessions. Nothing is uploaded.</p>
        </div>

        <section class="settings-section">
          <div class="settings-section-head">
            <h2>Appearance</h2>
            <p>Choose the color theme for the whole app.</p>
          </div>
          <div class="settings-control">
            <div class="settings-segment" role="group" aria-label="Theme">
              <button type="button" class="settings-seg ${state.theme === "dark" ? "active" : ""}" data-theme="dark" aria-pressed="${state.theme === "dark"}">Dark</button>
              <button type="button" class="settings-seg ${state.theme === "light" ? "active" : ""}" data-theme="light" aria-pressed="${state.theme === "light"}">Light</button>
            </div>
          </div>
        </section>

        <section class="settings-section">
          <div class="settings-section-head">
            <h2>Decompiler style <span class="settings-badge">${activeCount ? `${activeCount} on` : "default"}</span></h2>
            <p>Tune how decompiled C# is spelled and synthesized — including <strong>readable local names</strong>. These apply to every source and call-graph view. The default is opcode-faithful.</p>
          </div>
          <div class="settings-taste">${styleBody}</div>
          <div class="settings-taste-foot">
            ${activeCount
              ? '<button id="settings-taste-clear" type="button" class="settings-reset">Reset to default</button>'
              : '<span class="settings-muted">Default · opcode-faithful</span>'}
          </div>
        </section>
      </main>
    </div>`;
  bindSettingsEvents();
}

function bindSettingsEvents() {
  document.querySelector("#settings-close")?.addEventListener("click", closeSettings);
  document.querySelectorAll(".settings-seg[data-theme]").forEach(button =>
    button.addEventListener("click", () => setTheme(button.dataset.theme)));
  document.querySelectorAll(".settings-taste [data-taste]").forEach(checkbox =>
    checkbox.addEventListener("change", () => toggleTaste(checkbox.dataset.taste)));
  document.querySelector("#settings-taste-clear")?.addEventListener("click", clearTaste);
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

async function loadPackage(packageId, version, framework, options = {}) {
  // Background restores load a tab's data into state.packages (for the tab bar and
  // cross-package edges) WITHOUT stealing the main view: no focus switch, no selection
  // reset, no loading toggle, no render. The caller (workspace restore) keeps the loading
  // overlay up and focuses the real target once, so non-target tabs never flash into view.
  const background = options.background === true;
  const prevPackage = state.package;
  const prevRequested = {
    package: state.requestedPackage,
    version: state.requestedVersion,
    framework: state.requestedFramework
  };
  if (!background) {
    state.loading = true;
    state.error = "";
    state.home = false;
    state.queryNotice = "";
    state.requestedPackage = packageId;
    state.requestedVersion = version;
    state.requestedFramework = framework;
    state.loadingSubtitle = "";
    state.loadingMessage = `Querying ${packageId}@${version}…`;
    render();
  }

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
      totalTypes: types.filter(type => accessBucket(type.accessibility) === "public").length,
      totalMembers: result.totalMembers,
      documents: result.documents ?? []
    };
    const existing = state.packages.findIndex(item =>
      item.id.toLowerCase() === packageModel.id.toLowerCase()
      && item.version.toLowerCase() === packageModel.version.toLowerCase());
    if (existing >= 0) state.packages[existing] = packageModel;
    else state.packages.push(packageModel);
    recordRecentPackage(packageModel.id, packageModel.version, packageModel.activeFramework);
    if (background) return packageModel;
    state.package = packageModel;
    state.typeFilter = "";
    state.namespaceFilter = "";
    state.kindFilter = "";
    state.libraryScope = null;
    state.accessibilityFilter = new Set(["public"]);
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
    // A failed background restore of a non-target tab must not disrupt the workbench or the
    // real target; drop it silently (the tab simply won't appear).
    if (background) return null;
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

// Display name for a package. The resident runtime pseudo-package is presented as
// ".NET Platform"; its stable identity stays "Microsoft.NETCore.App" for the wire
// protocol, tab matching, and deep-link restore (see isRuntimePackId). Every other
// package shows its own id. Presentation only — never feed this back as an identity.
function packageDisplayName(pkg) {
  return pkg && pkg.isRuntimePack ? ".NET Platform" : (pkg ? pkg.id : "");
}

// Large-n library selector for the resident Platform pseudo-package: a single
// dropdown over the full static-index roster across both shared frameworks — the
// natural expansion of the small-n library chips. Picking a resident library scopes
// the workbench to it; picking one that is not yet loaded drills in (fetching just
// that assembly). Rendered both in the nav pane (where the small-n chips live) and on
// the overview page. Returns "" until the index is available.
function platformLibrarySelectHtml(options = {}) {
  const dataAttr = options.dataAttr || "data-platform-library-select";
  const selectedKey = options.selected;
  const roster = platformLibraryRoster("");
  if (!roster.length) return "";
  const byAssembly = new Map(roster.map(lib => [lib.assembly, lib]));
  const scoped = selectedKey !== undefined
    ? String(selectedKey || "")
    : (state.libraryScope && state.libraryScope.size === 1 ? [...state.libraryScope][0] : "");
  // Recent = the loaded/most-recently-accessed libraries: the explicit MRU first
  // (persisted across sessions), then any other currently-loaded libraries such as
  // System.Private.CoreLib, which is always resident but never explicitly "opened".
  // Resolved against the active framework's roster so counts stay honest. Duplicates
  // the .NET / ASP.NET Core catalog groups by design.
  const recentKeys = [];
  const recent = [];
  const pushRecent = lib => {
    if (!lib || recentKeys.includes(lib.assembly)) return;
    recentKeys.push(lib.assembly);
    recent.push(lib);
  };
  for (const entry of state.platformRecent || []) pushRecent(byAssembly.get(entry.assembly));
  for (const lib of roster) if (lib.loaded) pushRecent(lib);
  // The selector always shows a single "current" library: whatever is scoped,
  // else the most-recent, else the largest library — never a useless reset row.
  const current = scoped || recent[0]?.assembly || roster[0]?.assembly || "";
  let selectedMarked = false;
  const option = lib => {
    const isSel = !selectedMarked && lib.assembly === current;
    if (isSel) selectedMarked = true;
    return `<option value="${escapeHtml(lib.assembly)}" data-pack="${escapeHtml(lib.pack)}" ${isSel ? "selected" : ""}>${escapeHtml(lib.assembly)} · ${lib.publicTypes} types</option>`;
  };
  const recentGroup = recent.length
    ? `<optgroup label="Recent">${recent.map(option).join("")}</optgroup>`
    : "";
  const group = (pack, label) => {
    const rows = roster.filter(lib => lib.pack === pack).map(option).join("");
    return rows ? `<optgroup label="${escapeHtml(label)}">${rows}</optgroup>` : "";
  };
  return `<select class="scope-select platform-library-select" ${dataAttr} aria-label="Select a platform library" title="Pick a library to scope the type list to it. Recent lists the libraries currently loaded (most-recently accessed first); .NET and ASP.NET Core are the full catalog.">
      ${recentGroup}
      ${group("netcore.app", ".NET")}
      ${group("aspnetcore.app", "ASP.NET Core")}
    </select>`;
}

// The always-present, non-closable, left-most "Platform" tab. It abstracts the .NET runtime
// packs (netcore.app, aspnetcore.app, …) behind a single surface: when a pack is resident it
// activates it; otherwise clicking loads it lazily. Rendered separately from the normal tab
// map so it is always first and never carries a close affordance.
function platformTabHtml() {
  const rt = runtimePackPackage();
  const active = rt && state.package && state.package.id === rt.id ? "active" : "";
  const framework = rt?.activeFramework || state.package?.activeFramework || "";
  const attr = rt ? `data-package="${escapeHtml(rt.id)}"` : `data-platform-open="1"`;
  return `<button class="package-tab platform ${active}" ${attr} role="tab" title="Platform · .NET runtime libraries">
      <span class="package-cube">◎</span>
      <span class="tab-label">Platform</span>
      <small>${escapeHtml(framework || "load")}</small>
    </button>`;
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
      documents: result.documents ?? [],
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

// Loads ONE named runtime-pack assembly (e.g. System.Text.Json.dll from CoreCLR, or
// Microsoft.AspNetCore.Routing.dll from the ASP.NET Core shared framework) and folds its
// type surface into the resident runtime pseudo-package, creating that package if it is not
// resident yet. `pack` names the shared framework (netcore.app | aspnetcore.app), threaded
// through so per-type/member queries later route to the right pack. This backs index-first
// Platform drill-in: the Platform scope roster comes from the static index with no download,
// and picking a library fetches just that assembly here. Types/assemblies are merged (deduped
// by id/name) so the runtime pack accumulates the libraries the user visits.
async function loadRuntimePackAssembly(framework, assemblyFileName, pack) {
  if (state.runtimePackLoading) return runtimePackPackage();
  state.runtimePackLoading = true;
  state.runtimePackError = "";
  try {
    const result = await inspectLoadRuntimePackAssembly(framework || "", assemblyFileName, pack || "");
    refreshPackageStats();
    const newTypes = (result.types ?? []).map(type => ({ ...type, api: type.api ?? [] }));
    const existing = runtimePackPackage();
    if (existing) {
      const seenTypes = new Set(existing.types.map(type => type.id));
      for (const type of newTypes) if (!seenTypes.has(type.id)) existing.types.push(type);
      const seenAsm = new Set((existing.assemblies || []).map(item => item.name));
      for (const asm of (result.assemblies ?? [])) if (!seenAsm.has(asm.name)) existing.assemblies.push(asm);
      existing.assembly = (existing.assemblies || []).map(item => item.name).join(", ");
      existing.totalTypes = existing.types.length;
      existing.totalMembers = (existing.totalMembers || 0) + (result.totalMembers || 0);
      state.runtimePackLoading = false;
      return existing;
    }
    const packageModel = {
      id: result.package,
      version: result.version,
      frameworks: (result.frameworks ?? []).slice().sort(compareFrameworks),
      activeFramework: result.activeFramework,
      assembly: (result.assemblies ?? []).map(item => item.name).join(", "),
      assemblies: result.assemblies ?? [],
      types: newTypes,
      totalTypes: newTypes.length,
      totalMembers: result.totalMembers,
      documents: result.documents ?? [],
      isRuntimePack: true
    };
    state.packages.push(packageModel);
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
  state.loadingSubtitle = "";
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

// Loads the full open-tab set described by a parsed location (opaque workspace bucket, or a
// lone target), then restores the active tab's platform library scope and deep-link
// selection. Shared by boot restore, refreshed/shared links, and the in-app demo buttons.
async function restoreWorkspaceFromLocation(loc, deep) {
  state.home = false;
  state.loading = true;
  state.error = "";
  render();
  const target = {
    id: loc.package,
    version: loc.version || "latest",
    framework: loc.framework || ""
  };
  const tabs = (loc.tabs && loc.tabs.length) ? loc.tabs.slice() : [target];
  const matchesTarget = tab =>
    isRuntimePackId(tab.id)
      ? isRuntimePackId(target.id)
      : (tab.id.toLowerCase() === target.id.toLowerCase()
        && String(tab.version).toLowerCase() === String(target.version).toLowerCase());
  if (!tabs.some(matchesTarget)) tabs.push(target);

  // Tab loads must not consume a stale deep link; the target's selection is applied below.
  pendingDeepLink = null;
  // Load every tab's data so the tab bar and cross-package edges come back, but keep the
  // main view under the loading overlay throughout: NuGet tabs load in the background (no
  // focus steal) and loadRuntimePack already never steals focus. The real target is focused
  // once, below — so a non-target tab (e.g. an STJ tab on a platform-library link) never
  // flashes into view before the target resolves.
  for (const tab of tabs) {
    if (isRuntimePackId(tab.id)) await loadRuntimePack(tab.framework);
    else await loadPackage(tab.id, tab.version, tab.framework, { background: true });
  }

  const targetModel = state.packages.find(matchesTarget);
  if (targetModel) {
    state.package = targetModel;
    // Restore the platform library scope captured in the share packet before applying the
    // deep link, so a refreshed/shared platform-library link lands on that library.
    if (isRuntimePackId(targetModel.id) && loc.library) {
      await applyPlatformLibraryScope(loc.library);
    }
    applyDeepLink(deep);
    state.loading = false;
    render();
    loadSelectionData();
  } else if (!isRuntimePackId(target.id)) {
    // The focused NuGet target failed to load during the silent background pass; re-run it in
    // the foreground so its error (e.g. a 404) surfaces properly instead of a blank workbench.
    pendingDeepLink = deep;
    await loadPackage(target.id, target.version, target.framework);
  } else {
    state.loading = false;
    render();
  }
}

// Restores the full open-tab set from the opaque workspace bucket (or just the visible
// target for a lone/legacy link), loading each tab in order so the tab bar and any
// cross-package dependency edges come back. Only the focused target restores its deep-link.
async function restoreInitialWorkspace() {
  const loc = {
    ...initialLocation,
    package: state.requestedPackage,
    version: state.requestedVersion,
    framework: state.requestedFramework
  };
  await restoreWorkspaceFromLocation(loc, pendingDeepLink);
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
  // The Metadata Explorer is a full-screen modal-style view. Backspace walks the ref->def
  // history (Shift+Backspace forward); Escape steps back through it and finally exits.
  if (state.explorer?.open) {
    if (event.key === "Escape") {
      event.preventDefault();
      if (state.explorer.historyPos > 0) explorerHistoryBack();
      else closeExplorer();
    } else if (event.key === "Backspace") {
      event.preventDefault();
      if (event.shiftKey) explorerHistoryForward();
      else explorerHistoryBack();
    }
    return;
  }
  // Settings is a modal-style page reachable from home too, so handle its Escape before the
  // home bail below (which otherwise swallows the keystroke on the home page).
  if (state.settings) {
    if (event.key === "Escape") {
      event.preventDefault();
      closeSettings();
    }
    return;
  }
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
  } else if (event.key === "Escape" && state.docViewerOpen) {
    event.preventDefault();
    closeDocViewer();
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
    && (isRuntimePackId(loc.package)
      ? isRuntimePackId(state.package.id)
      : (loc.package.toLowerCase() === state.package.id.toLowerCase()
        && (!loc.version || loc.version.toLowerCase() === state.package.version.toLowerCase())));
  if (samePackage || !loc.package) {
    if (isRuntimePackId(state.package.id)) {
      // Back/forward within the platform: re-scope to the target library (or the
      // aggregate) before restoring selection, since scope is part of the view.
      restorePlatformScopeThenDeepLink(loc);
    } else {
      applyDeepLink(loc);
      render();
      loadSelectionData();
    }
  } else if (isRuntimePackId(loc.package)) {
    // The runtime pack has no nupkg; rebuild it from its TFM instead of 404-ing
    // on a NuGet fetch when back/forward lands on a platform state.
    pendingDeepLink = { type: loc.type, member: loc.member, overload: loc.overload, section: loc.section };
    restoreRuntimePackFromHistory(loc);
  } else {
    pendingDeepLink = { type: loc.type, member: loc.member, overload: loc.overload, section: loc.section };
    loadPackage(loc.package, loc.version || "latest", loc.framework || "");
  }
});

// Re-scope the active runtime pack to the platform library named in a share/history packet
// (lazily loading that assembly if needed via the same drill-in path as clicking it), or
// clear the scope for the aggregate platform, then restore the deep-linked selection.
async function restorePlatformScopeThenDeepLink(loc) {
  await applyPlatformLibraryScope(loc.library);
  applyDeepLink(loc);
  render();
  loadSelectionData();
}

// Load and scope to a single platform library key (or clear the scope when null). Reuses
// openPlatformLibrary so a restored view matches clicking the library in the selector.
async function applyPlatformLibraryScope(libraryKey) {
  const key = String(libraryKey || "").replace(/\.dll$/i, "");
  if (!key) { state.libraryScope = null; return; }
  // The pack (CoreCLR vs ASP.NET Core) is resolved from the static index roster; ensure it
  // is loaded on a cold shared/refreshed link so the right assembly is fetched.
  if (!state.platformIndex) {
    try { state.platformIndex = await loadPlatformIndex(); } catch { /* best effort; defaults to CoreCLR */ }
  }
  await openPlatformLibrary(key, platformPackForAssembly(key));
}

// History (back/forward) landed on a .NET Platform state. Its resident pseudo-package
// has no nupkg, so restore it via loadRuntimePack (usually already resident, so instant),
// re-scope to the captured library, and re-apply the deep link, mirroring
// restoreInitialWorkspace's runtime-pack path.
async function restoreRuntimePackFromHistory(loc) {
  const pack = await loadRuntimePack(loc.framework || "");
  const deep = pendingDeepLink;
  pendingDeepLink = null;
  if (pack) {
    state.package = pack;
    if (loc.library) await applyPlatformLibraryScope(loc.library);
    applyDeepLink(deep || loc);
  }
  render();
  loadSelectionData();
}

bootstrap();

// Warm the static platform-assembly/facade index in the background. It is a
// hint layer (facade badges, per-library overview roster, library-scope
// selector) built on top of the app; prefetching keeps it ready without
// blocking boot. Cached on state once resolved; exposed for verification.
window.__platformIndex = loadPlatformIndex();
window.__platformIndex.then(index => { if (index) state.platformIndex = index; });
