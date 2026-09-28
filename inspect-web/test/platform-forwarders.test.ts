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
  name: "XmlReader",
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
    namespaceCount: 1, namespaceOptionsHtml: "", kindFilters: ["forwarded"],
    accessibilityControlHtml: "", library: "System.Xml",
    parentSubject: "library", filtersExpanded: false, filterSummary: "",
    escapeHtml, typeDisplayName: row => row.name,
    typeLibraryLabel: () => "", kindIcon: kind => kind,
  });
  assert.match(html, /Forwarded<\/small>/);
  assert.match(html, /aria-selected="true"/);
  assert.doesNotMatch(html, /title="0 members"|class<\/span>/);
  assert.deepEqual(typeLensesFor({ isRuntimePack: true }, true),
    [["overview", "Overview"]]);
  assert.deepEqual(typeLensesFor({ isRuntimePack: true }), [["api", "API"]]);
});

test("forwarder filtering uses declaration text without a defining kind", () => {
  for (const text of ["XmlReader", "System.Xml", "ReaderWriter", "forwarded"]) {
    assert.deepEqual(filterForwardedTypes([xmlReader],
      { text, namespace: "", kind: "" }), [xmlReader]);
  }
  assert.deepEqual(filterForwardedTypes([xmlReader],
    { text: "", namespace: "System.Xml", kind: "forwarded" }), [xmlReader]);
  assert.deepEqual(filterForwardedTypes([xmlReader],
    { text: "", namespace: "", kind: "class" }), []);
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
