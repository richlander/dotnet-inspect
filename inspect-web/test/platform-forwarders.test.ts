import assert from "node:assert/strict";
import test from "node:test";
import { typeLensesFor } from "../src/data.ts";
import {
  filterForwardedTypes,
  isForwardedType,
  renderForwardedTypeOverview,
} from "../src/platform-forwarders.ts";
import { renderTypeNav } from "../src/type-panel.ts";

const xmlReader = {
  id: "System.Xml:System.Xml.XmlReader",
  name: "System.Xml.XmlReader",
  namespace: "System.Xml",
  targetAssembly: "System.Xml.ReaderWriter",
  action: "opaque-action",
};
const escapeHtml = (value: unknown) => String(value)
  .replaceAll("&", "&amp;").replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;").replaceAll('"', "&quot;");

// PR-fast: presentation of the real XML forwarding declaration.
test("forwarded Type rows have no invented kind or member cardinality", () => {
  assert.equal(isForwardedType(xmlReader), true);
  const html = renderTypeNav({
    current: xmlReader,
    visible: [xmlReader],
    typeGroups: new Map([["System.Xml", [xmlReader]]]),
    typeFilter: "", namespaceFilter: "", kindFilter: "",
    accessibilityFilter: "", traitFilter: "",
    namespaceCount: 1, namespaceOptionsHtml: "",
    kindOptions: [
      { value: "", label: "all", count: 1 },
      { value: "forwarded", label: "forwarded", count: 1 },
    ],
    accessibilityOptions: [{ value: "", label: "all", count: 1 }],
    traitOptions: [
      { value: "", label: "all", count: 1 },
      { value: "api.type-trait.abstract", label: "abstract", count: 0 },
      { value: "api.type-trait.static", label: "static", count: 0 },
      { value: "api.type-trait.object", label: "object", count: 0 },
    ],
    library: "System.Xml",
    parentSubject: "library", filtersExpanded: false, filterSummary: "",
    escapeHtml, typeDisplayName: row => row.name,
    typeLibraryLabel: () => "", kindIcon: kind => kind,
  });
  assert.match(html, /Forwarded<\/small>/);
  assert.match(html, /aria-selected="true"/);
  assert.doesNotMatch(html, /title="0 members"|class<\/span>/);
  assert.deepEqual(typeLensesFor({ isRuntimePack: true }, true),
    [["overview", "Overview"]]);
  assert.deepEqual(typeLensesFor({ isRuntimePack: true }),
    [["api", "API"], ["source", "Source"]]);
});

test("forwarder filtering uses declaration text without a defining kind", () => {
  for (const text of ["XmlReader", "System.Xml", "ReaderWriter", "forwarded"]) {
    assert.deepEqual(filterForwardedTypes([xmlReader],
      { text, namespace: "", kind: "" }), [xmlReader]);
  }
  assert.deepEqual(filterForwardedTypes([xmlReader],
    { text: "", namespace: "System.Xml", kind: "forwarded" }), [xmlReader]);
  assert.deepEqual(filterForwardedTypes([xmlReader],
    { text: "", namespace: "", kind: "api.type-kind.class" }), []);
});

test("Overview binds declaration identity and escapes destination and failures", () => {
  const html = renderForwardedTypeOverview({
    ...xmlReader, targetAssembly: '<script>"destination"</script>',
  }, "System.Xml", { pending: false, error: "<unavailable>" }, escapeHtml);
  assert.match(html, /data-platform-forwarder="System.Xml:System.Xml.XmlReader"/);
  assert.match(html, /&lt;script&gt;&quot;destination&quot;&lt;\/script&gt;/);
  assert.match(html, /role="alert">&lt;unavailable&gt;/);
  assert.doesNotMatch(html, /opaque-action|<script>|data-lens="metadata"/);
  const unavailable = renderForwardedTypeOverview(
    { ...xmlReader, action: "" }, "System.Xml",
    { pending: false, error: "Unavailable" }, escapeHtml);
  assert.doesNotMatch(unavailable, /data-platform-forwarder=/);
  assert.match(unavailable, /System.Xml.ReaderWriter/);
});

// PR-fast: the user-reported System.Runtime declaration uses metadata facts.
test("SafeHandle forwarder uses metadata rows and quiet exact traversal", () => {
  const row = {
    id: "System.Runtime:Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid",
    name: "Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid",
    namespace: "Microsoft.Win32.SafeHandles",
    targetAssembly: "System.Private.CoreLib",
    action: "opaque-corelib-action",
  };
  const html = renderForwardedTypeOverview(row, "System.Runtime",
    { pending: true, error: "" }, escapeHtml);
  assert.match(html, /class="metadata-surface forwarded-type-overview"/);
  assert.match(html, /<dl class="fact-rows">/);
  assert.match(html, /<dt>Declaring assembly<\/dt><dd><code>System.Runtime/);
  assert.match(html, /<dt>Implementation<\/dt>/);
  assert.match(html, /class="forwarder-destination"/);
  assert.match(html, /aria-label="Open Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid in System.Private.CoreLib"/);
  assert.match(html, /disabled aria-busy="true"/);
  assert.match(html, /role="status"/);
  assert.doesNotMatch(html, /does not define members|type-chip|opaque-corelib-action/);
  const unavailable = renderForwardedTypeOverview(row, "System.Runtime",
    { pending: false, error: "Unavailable", available: false }, escapeHtml);
  assert.doesNotMatch(unavailable, /data-platform-forwarder=/);
  assert.match(unavailable, /<dd><code>System.Private.CoreLib<\/code>/);
});
