import assert from "node:assert/strict";
import test from "node:test";
import {
  createSavedWorkspaces,
  type SavedWorkspace,
  type SavedWorkspaceFocus,
} from "../src/saved-workspaces.ts";
import { renderSavedWorkspaces, renderWorkspaceSaveButton } from "../src/saved-workspaces-view.ts";

function harness(initial: string | null = null) {
  let stored = initial;
  let failRead = false;
  let failWrite = false;
  let failCapture = false;
  let failOpen = false;
  let captures = 0;
  const opened: SavedWorkspace[] = [];
  const focused: (SavedWorkspaceFocus | undefined)[] = [];
  const options = {
    read: () => {
      if (failRead) throw new Error("Storage unavailable");
      return stored;
    },
    write: (value: string) => {
      if (failWrite) throw new Error("Quota exceeded");
      stored = value;
    },
    capture: () => {
      captures++;
      if (failCapture) throw new Error("Workspace is not projectable");
      return "owner-issued-packet";
    },
    open: (entry: SavedWorkspace) => {
      if (failOpen) throw new Error("Packet cannot be restored");
      opened.push(entry);
    },
    render: (focus?: SavedWorkspaceFocus) => { focused.push(focus); },
  };
  const saves = createSavedWorkspaces(options);
  return {
    saves, opened, focused,
    stored: () => stored, captures: () => captures,
    reload: () => createSavedWorkspaces(options),
    storage: (value: string | null) => { stored = value; },
    failRead: (value = true) => { failRead = value; },
    failWrite: (value = true) => { failWrite = value; },
    failCapture: () => { failCapture = true; },
    failOpen: () => { failOpen = true; },
    save(name: string) {
      saves.beginSave();
      saves.setName(name);
      saves.save();
    },
  };
}

test("named saves retain the exact opaque packet and reopen across reload without recapture", () => {
  const h = harness();
  assert.equal(h.saves.state.available, true);
  assert.equal(h.captures(), 0);
  h.save("  Json study  ");
  assert.deepEqual(h.saves.state.entries, [{ name: "Json study", packet: "owner-issued-packet" }]);
  assert.equal(h.saves.state.formOpen, false);
  assert.deepEqual(h.focused.at(-1), { kind: "saved-open", name: "Json study", index: 0 });
  const reloaded = h.reload();
  assert.equal(h.captures(), 1);
  reloaded.open("Json study");
  assert.deepEqual(h.opened, [{ name: "Json study", packet: "owner-issued-packet" }]);
  assert.equal(h.captures(), 1);
});

test("duplicate names and invalid names do not replace or capture another save", () => {
  const h = harness();
  h.save("Study");
  const before = h.stored();
  for (const name of ["STUDY", " study ", "   ", "x".repeat(121)]) {
    h.save(name);
    assert.equal(h.stored(), before);
    assert.equal(h.saves.state.entries.length, 1);
    assert.ok(h.saves.state.error);
    assert.equal(h.saves.state.formOpen, true);
  }
  assert.equal(h.captures(), 1);
});

test("forget removes only its saved identity and never opens or recaptures a Workspace", () => {
  const h = harness();
  h.save("First");
  h.save("Second");
  h.saves.forget("First");
  assert.deepEqual(h.saves.state.entries, [{ name: "Second", packet: "owner-issued-packet" }]);
  assert.deepEqual(h.reload().state.entries, h.saves.state.entries);
  assert.deepEqual(h.opened, []);
  assert.equal(h.captures(), 2);
  assert.deepEqual(h.focused.at(-1), { kind: "saved-remove", name: "First", index: 0 });
});

test("write failure preserves saved entries and draft text on save and forget", () => {
  const h = harness();
  h.save("First");
  const before = h.stored();
  h.failWrite();
  h.save("Second");
  assert.equal(h.stored(), before);
  assert.deepEqual(h.saves.state.entries, [{ name: "First", packet: "owner-issued-packet" }]);
  assert.equal(h.saves.state.name, "Second");
  assert.match(h.saves.state.error, /Quota exceeded/);
  h.saves.forget("First");
  assert.equal(h.stored(), before);
  assert.equal(h.saves.state.entries.length, 1);
  assert.match(h.saves.state.error, /Could not forget/);
});

test("failed projection cannot persist a partial or empty save", () => {
  const h = harness();
  h.failCapture();
  h.save("Study");
  assert.equal(h.stored(), null);
  assert.deepEqual(h.saves.state.entries, []);
  assert.equal(h.saves.state.name, "Study");
  assert.match(h.saves.state.error, /not projectable/);
});

for (const raw of [
  "{",
  '{"version":2,"entries":[]}',
  '{"version":1,"entries":[{"name":"A","packet":"p"},{"name":"a","packet":"q"}]}',
  '{"version":1,"entries":[{"name":"A","packet":null}]}',
]) {
  test(`unreadable saved data is reported and not overwritten: ${raw}`, () => {
    const h = harness(raw);
    assert.equal(h.saves.state.available, false);
    assert.match(h.saves.state.error, /Could not read/);
    h.save("New");
    assert.equal(h.stored(), raw);
    h.storage(null);
    h.saves.retry();
    assert.equal(h.saves.state.available, true);
    assert.equal(h.saves.state.error, "");
  });
}

test("storage read failures remain visible until a successful retry", () => {
  const h = harness();
  h.failRead();
  const reloaded = h.reload();
  assert.equal(reloaded.state.available, false);
  assert.match(reloaded.state.error, /Storage unavailable/);
  h.failRead(false);
  reloaded.retry();
  assert.equal(reloaded.state.available, true);
  assert.equal(reloaded.state.error, "");
});

test("a saved packet can fail to open and still be forgotten without decoding", () => {
  const h = harness('{"version":1,"entries":[{"name":"Old","packet":""}]}');
  assert.equal(h.saves.state.available, true);
  h.failOpen();
  h.saves.open("Old");
  assert.match(h.saves.state.error, /cannot be restored/);
  assert.equal(h.saves.state.entries.length, 1);
  h.saves.forget("Old");
  assert.deepEqual(h.reload().state.entries, []);
  assert.equal(h.captures(), 0);
});

test("canceling a save changes only the transient form", () => {
  const h = harness();
  h.saves.beginSave();
  h.saves.setName("Draft");
  h.saves.cancelSave();
  assert.equal(h.stored(), null);
  assert.equal(h.captures(), 0);
  assert.equal(h.saves.state.formOpen, false);
  assert.deepEqual(h.focused.at(-1), { kind: "save" });
});

const escapeHtml = (value: unknown) => String(value)
  .replaceAll("&", "&amp;").replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;").replaceAll('"', "&quot;");

test("saved entry rendering keeps names escaped and Open separate from Forget", () => {
  const h = harness();
  h.save('Study <"A">');
  const view = { state: h.saves.state, canSave: false, canOpen: true };
  assert.match(renderWorkspaceSaveButton(view), / disabled/);
  const html = renderSavedWorkspaces(view, escapeHtml);
  assert.match(html, /data-saved-workspace-open="Study &lt;&quot;A&quot;&gt;"/);
  assert.match(html, /aria-label="Forget saved Workspace Study &lt;&quot;A&quot;&gt;"/);
  assert.equal((html.match(/<button\b/g) ?? []).length, 2);
  assert.doesNotMatch(html, /owner-issued-packet/);
});

test("an empty saved shelf adds no persistent section and read failure offers Retry", () => {
  const h = harness();
  const view = { state: h.saves.state, canSave: true, canOpen: true };
  assert.equal(renderSavedWorkspaces(view, escapeHtml), "");
  h.failRead();
  h.saves.retry();
  assert.match(renderSavedWorkspaces(view, escapeHtml), /role="alert"/);
  assert.match(renderSavedWorkspaces(view, escapeHtml), /data-saved-workspaces-retry/);
  assert.match(renderWorkspaceSaveButton(view), / disabled/);
});
