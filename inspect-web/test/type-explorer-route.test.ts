import assert from "node:assert/strict";
import test from "node:test";

import {
  decodeTypeExplorerIntent,
  defaultTypeExplorerIntent,
  encodeTypeExplorerIntent,
  isTypeExplorerPath,
  isTypeExplorerPredecessor,
  readTypeExplorerHistory,
  typeExplorerHistoryState,
  typeExplorerUrl,
  typeSourceUrl,
} from "../src/type-explorer-route.ts";
import { withHistoryEntryId } from "../src/package-query-route.ts";

test("Type Explorer recognizes only its canonical routed path", () => {
  assert.equal(isTypeExplorerPath("/type-explorer"), true);
  assert.equal(isTypeExplorerPath("/type-explorer/"), true);
  assert.equal(isTypeExplorerPath("/type/source"), false);
});

test("Type Explorer intent round-trips bounded exact selection currency", () => {
  const intent = {
    ...defaultTypeExplorerIntent(),
    bodyMode: "SelectedBody" as const,
    placement: "Static" as const,
    accessibilities: ["Public", "ProtectedInternal"] as const,
    selectedDeclarationId: 42,
    documentRevision: "a".repeat(64),
  };

  assert.deepEqual(
    decodeTypeExplorerIntent(encodeTypeExplorerIntent(intent)),
    intent);
});

test("Type Explorer rejects malformed, duplicate, and partial intent", () => {
  const encoded = (value: unknown) => {
    const bytes = new TextEncoder().encode(JSON.stringify(value));
    let binary = "";
    for (const byte of bytes) binary += String.fromCharCode(byte);
    return btoa(binary)
      .replaceAll("+", "-")
      .replaceAll("/", "_")
      .replace(/=+$/u, "");
  };
  assert.equal(decodeTypeExplorerIntent(null), null);
  assert.equal(decodeTypeExplorerIntent("not base64!"), null);
  assert.equal(decodeTypeExplorerIntent(encoded({
    ...defaultTypeExplorerIntent(),
    accessibilities: ["Public", "Public"],
  })), null);
  assert.equal(decodeTypeExplorerIntent(encoded({
    ...defaultTypeExplorerIntent(),
    selectedDeclarationId: 42,
    documentRevision: null,
  })), null);
  assert.equal(decodeTypeExplorerIntent(encoded({
    ...defaultTypeExplorerIntent(),
    selectedDeclarationId: -1,
  })), null);
  assert.equal(decodeTypeExplorerIntent(encoded({
    ...defaultTypeExplorerIntent(),
    documentRevision: "stale",
  })), null);
});

test("Type Explorer URL preserves Workspace authority and only changes route intent", () => {
  const current = new URL(
    "https://example.test/?package=System.Text.Json&w=opaque#type");
  const explorer = typeExplorerUrl(current, defaultTypeExplorerIntent());
  assert.equal(explorer.pathname, "/type-explorer");
  assert.equal(explorer.searchParams.get("package"), "System.Text.Json");
  assert.equal(explorer.searchParams.get("w"), "opaque");
  assert.ok(explorer.searchParams.get("te"));
  assert.equal(explorer.hash, "#type");

  const source = typeSourceUrl(explorer);
  assert.equal(source.pathname, "/");
  assert.equal(source.searchParams.get("w"), "opaque");
  assert.equal(source.searchParams.has("te"), false);
  assert.equal(source.hash, "#type");
});

test("Type Explorer history restores focus only on its exact predecessor", () => {
  const predecessor = withHistoryEntryId(null, "source-entry");
  const explorer = typeExplorerHistoryState(
    null,
    "explorer-entry",
    {
      predecessorEntryId: "source-entry",
      returnFocus: "explore-source",
    });

  assert.deepEqual(readTypeExplorerHistory(explorer), {
    predecessorEntryId: "source-entry",
    returnFocus: "explore-source",
  });
  assert.equal(
    isTypeExplorerPredecessor(predecessor, "source-entry"),
    true);
  assert.equal(
    isTypeExplorerPredecessor(
      withHistoryEntryId(null, "other"),
      "source-entry"),
    false);
});
