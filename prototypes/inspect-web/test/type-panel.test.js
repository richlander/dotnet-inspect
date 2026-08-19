import assert from "node:assert/strict";
import test from "node:test";
import {
  renderMemberNav,
  renderTypeMetadata,
  renderTypeNav,
  renderTypeSource,
  typeHeading,
  typeMetadataSignature,
  typeSourceSignature,
} from "../src/type-panel.ts";

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function typeDisplayName(item) {
  return item?.displayName || item?.name || "";
}

function kindIcon(kind) {
  if (kind.includes("struct")) return "S";
  if (kind === "enum") return "E";
  if (kind.includes("interface")) return "I";
  return "C";
}

function shortKind(kind) {
  return kind.replace("sealed ", "").replace("abstract ", "");
}

function highlight(value) {
  return escapeHtml(value).replace(/\b(public|class)\b/g, '<span class="kw">$1</span>');
}

function highlightCSharp(value) {
  return escapeHtml(value);
}

function factRows(rows) {
  return `<dl>${rows.map(([key, value]) => `<div><dt>${escapeHtml(key)}</dt><dd>${escapeHtml(value)}</dd></div>`).join("")}</dl>`;
}

const jsonSerializer = {
  id: "System.Text.Json.JsonSerializer",
  name: "JsonSerializer",
  namespace: "System.Text.Json",
  kind: "sealed class",
  signature: "public sealed class JsonSerializer",
  members: 12,
  accessibility: "public",
  assembly: "System.Text.Json.dll",
  definitionId: "T:System.Text.Json.JsonSerializer",
};

const jsonDocument = {
  id: "System.Text.Json.JsonDocument",
  name: "JsonDocument",
  namespace: "System.Text.Json",
  kind: "class",
  signature: "public class JsonDocument",
  members: 8,
  accessibility: "public",
  assembly: "System.Text.Json.dll",
};

test("the type nav lists namespace groups with the current type selected", () => {
  const html = renderTypeNav({
    current: jsonSerializer,
    visible: [jsonSerializer, jsonDocument],
    typeGroups: new Map([["System.Text.Json", [jsonSerializer, jsonDocument]]]),
    typeFilter: "",
    namespaceFilter: "",
    kindFilter: "",
    namespaceCount: 1,
    namespaceOptionsHtml: '<option value="System.Text.Json">System.Text.Json · 2</option>',
    kindFilters: ["class"],
    accessibilityControlHtml: "",
    libraryControlHtml: "",
    escapeHtml,
    typeDisplayName,
    kindIcon,
    shortKind,
  });

  assert.match(html, /2 shown/);
  assert.match(
    html,
    /data-type="System\.Text\.Json\.JsonSerializer" role="option" aria-selected="true"/);
  assert.match(
    html,
    /data-type="System\.Text\.Json\.JsonDocument" role="option" aria-selected="false"/);
  assert.match(html, /data-namespace="System\.Text\.Json"/);
});

test("the type nav reports no matches for an empty filtered group", () => {
  const html = renderTypeNav({
    current: jsonSerializer,
    visible: [],
    typeGroups: new Map(),
    typeFilter: "nothing-matches",
    namespaceFilter: "",
    kindFilter: "",
    namespaceCount: 0,
    namespaceOptionsHtml: "",
    kindFilters: [],
    accessibilityControlHtml: "",
    libraryControlHtml: "",
    escapeHtml,
    typeDisplayName,
    kindIcon,
    shortKind,
  });

  assert.match(html, /No public types match this filter\./);
});

test("the member nav marks the active group and its selected overload", () => {
  const group = {
    key: "method:Serialize",
    name: "Serialize",
    kind: "method",
    overloads: [
      { signature: "string Serialize(object value)" },
      { signature: "string Serialize<T>(T value)" },
    ],
  };
  const entries = [
    { kind: "member", group },
    { kind: "overload", group, index: 0 },
    { kind: "overload", group, index: 1 },
  ];

  const html = renderMemberNav({
    type: jsonSerializer,
    entries,
    memberCount: 1,
    selectedMemberKey: "method:Serialize",
    selectedOverloadIndex: 1,
    escapeHtml,
    typeDisplayName,
    shortKind,
    highlight,
  });

  assert.match(html, /class="type-row member-row active-group [^"]*" data-nav-member="method:Serialize"/);
  assert.match(
    html,
    /data-nav-overload="1" role="option" aria-selected="true"/);
  assert.match(
    html,
    /data-nav-overload="0" role="option" aria-selected="false"/);
});

test("the type heading reports the owning package and library", () => {
  const html = typeHeading({
    item: jsonSerializer,
    packageContext: { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" },
    escapeHtml,
    typeDisplayName,
    kindIcon,
    highlight,
  });

  assert.match(html, /<h1>JsonSerializer<\/h1>/);
  assert.match(html, /System\.Text\.Json\.dll/);
  assert.match(html, /System\.Text\.Json@9\.0\.0/);
});

test("type metadata signature keys on the exact package, framework, and type coordinate", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  assert.equal(
    typeMetadataSignature(jsonSerializer, packageContext),
    "System.Text.Json@9.0.0/net9.0/System.Text.Json.dll/System.Text.Json.JsonSerializer");
});

test("type source signature routes through the shared decompiler-taste-aware key", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  const calls = [];
  const memberRequestKey = (parts, taste) => {
    calls.push({ parts, taste });
    return "computed-key";
  };

  const signature = typeSourceSignature(jsonSerializer, packageContext, ["identifier-casing"], memberRequestKey);

  assert.equal(signature, "computed-key");
  assert.deepEqual(calls, [{
    parts: [
      "System.Text.Json",
      "9.0.0",
      "net9.0",
      "System.Text.Json.dll",
      "T:System.Text.Json.JsonSerializer",
    ],
    taste: ["identifier-casing"],
  }]);
});

test("type metadata renders a loading state while the projection is in flight", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  const key = typeMetadataSignature(jsonSerializer, packageContext);
  const html = renderTypeMetadata({
    item: jsonSerializer,
    packageContext,
    metadataState: {
      typeMetadataKey: key,
      typeMetadataLoading: true,
      typeMetadataError: null,
      typeMetadata: null,
    },
    escapeHtml,
    relatedTypeChip: name => `<button>${escapeHtml(name)}</button>`,
    factRows,
  });

  assert.match(html, /Projecting type metadata…/);
});

test("type metadata renders composition, interfaces, and derived types once loaded", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  const key = typeMetadataSignature(jsonSerializer, packageContext);
  const html = renderTypeMetadata({
    item: jsonSerializer,
    packageContext,
    metadataState: {
      typeMetadataKey: key,
      typeMetadataLoading: false,
      typeMetadataError: null,
      typeMetadata: {
        kind: "class",
        accessibility: "public",
        namespace: "System.Text.Json",
        assembly: "System.Text.Json.dll",
        interfaces: ["System.IDisposable"],
        derivedTypes: ["System.Text.Json.MyJsonSerializer"],
        composition: { total: 3, methods: 3 },
      },
    },
    escapeHtml,
    relatedTypeChip: name => `<button data-graph-type="${escapeHtml(name)}">${escapeHtml(name)}</button>`,
    factRows,
  });

  assert.match(html, /Implements/);
  assert.match(html, /data-graph-type="System\.IDisposable"/);
  assert.match(html, /Known derived types/);
  assert.match(html, /Composition/);
  assert.match(html, /3<\/strong><span>Methods/);
});

test("type source renders the provenance and copy action once loaded", () => {
  const html = renderTypeSource({
    item: jsonSerializer,
    currentSignature: "sig",
    sourceState: {
      typeSourceKey: "sig",
      typeSourceLoading: false,
      typeSource: { provider: "original", provenance: "SourceLink", url: "https://example.test", text: "class JsonSerializer {}" },
      typeSourceError: null,
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /Original source/);
  assert.match(html, /SourceLink/);
  assert.match(html, /id="copy-type-source"/);
  assert.match(html, /open source ↗/);
});

test("type source reports a failure without a stale result", () => {
  const html = renderTypeSource({
    item: jsonSerializer,
    currentSignature: "sig",
    sourceState: {
      typeSourceKey: "sig",
      typeSourceLoading: false,
      typeSource: null,
      typeSourceError: "The decompiler query failed.",
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /Type source failed/);
  assert.match(html, /The decompiler query failed\./);
});
