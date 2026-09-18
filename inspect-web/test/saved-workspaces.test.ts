import assert from "node:assert/strict";
import test from "node:test";
import {
  createSavedWorkspaces,
  type SavedWorkspaceCapture,
  type SavedWorkspace,
  type SavedWorkspaceFocus,
} from "../src/saved-workspaces.ts";
import { renderSavedWorkspaces, renderWorkspaceSaveButton } from "../src/saved-workspaces-view.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

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
      return {
        kind: "complete-format-3",
        packet: "owner-issued-packet",
        canonicalLocation: "/?w=legacy-location#workspace",
        activeTabIndex: 0,
        coordinateCount: 1,
      } satisfies SavedWorkspaceCapture;
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
      void saves.save();
    },
  };
}

test("named saves retain an explicit complete definition and reopen across reload without recapture", () => {
  const h = harness();
  assert.equal(h.saves.state.available, true);
  assert.equal(h.captures(), 0);
  h.save("  Json study  ");
  assert.deepEqual(h.saves.state.entries, [{
    name: "Json study",
    kind: "complete-format-3",
    packet: "owner-issued-packet",
    canonicalLocation: "/?w=legacy-location#workspace",
    activeTabIndex: 0,
    coordinateCount: 1,
  }]);
  assert.equal(h.saves.state.formOpen, false);
  assert.deepEqual(h.focused.at(-1), { kind: "saved-open", name: "Json study", index: 0 });
  const reloaded = h.reload();
  assert.equal(h.captures(), 1);
  reloaded.open("Json study");
  assert.deepEqual(h.opened, [{
    name: "Json study",
    kind: "complete-format-3",
    packet: "owner-issued-packet",
    canonicalLocation: "/?w=legacy-location#workspace",
    activeTabIndex: 0,
    coordinateCount: 1,
  }]);
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
  assert.deepEqual(h.saves.state.entries, [{
    name: "Second",
    kind: "complete-format-3",
    packet: "owner-issued-packet",
    canonicalLocation: "/?w=legacy-location#workspace",
    activeTabIndex: 0,
    coordinateCount: 1,
  }]);
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
  assert.deepEqual(h.saves.state.entries, [{
    name: "First",
    kind: "complete-format-3",
    packet: "owner-issued-packet",
    canonicalLocation: "/?w=legacy-location#workspace",
    activeTabIndex: 0,
    coordinateCount: 1,
  }]);
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

test("overlapping Save submissions share one pending capture", async () => {
  let stored: string | null = null;
  const capture = deferred<SavedWorkspaceCapture>();
  let captures = 0;
  const saves = createSavedWorkspaces({
    read: () => stored,
    write: value => { stored = value; },
    capture: () => {
      captures++;
      return capture.promise;
    },
    open: () => {},
    render: () => {},
  });

  saves.beginSave();
  saves.setName("My Workspace");
  const first = saves.save();
  const second = saves.save();
  assert.equal(captures, 1);
  capture.resolve({
    kind: "complete-format-3",
    packet: "owner-issued-packet",
    canonicalLocation: "/?w=legacy-location#workspace",
    activeTabIndex: 0,
    coordinateCount: 1,
  });
  await Promise.all([first, second]);

  assert.deepEqual(saves.state.entries, [{
    name: "My Workspace",
    kind: "complete-format-3",
    packet: "owner-issued-packet",
    canonicalLocation: "/?w=legacy-location#workspace",
    activeTabIndex: 0,
    coordinateCount: 1,
  }]);
  assert.equal(createSavedWorkspaces({
    read: () => stored,
    write: () => {},
    capture: () => ({
      kind: "complete-format-3",
      packet: "unused",
      canonicalLocation: "/?w=unused#workspace",
      activeTabIndex: 0,
      coordinateCount: 1,
    }),
    open: () => {},
    render: () => {},
  }).state.available, true);
});

test("asynchronous capture reports a following write failure", async () => {
  const capture = deferred<SavedWorkspaceCapture>();
  const saves = createSavedWorkspaces({
    read: () => null,
    write: () => { throw new Error("Quota exceeded"); },
    capture: () => capture.promise,
    open: () => {},
    render: () => {},
  });

  saves.beginSave();
  saves.setName("My Workspace");
  const operation = saves.save();
  capture.resolve({
    kind: "complete-format-3",
    packet: "owner-issued-packet",
    canonicalLocation: "/?w=legacy-location#workspace",
    activeTabIndex: 0,
    coordinateCount: 1,
  });
  await operation;

  assert.deepEqual(saves.state.entries, []);
  assert.equal(saves.state.name, "My Workspace");
  assert.match(saves.state.error, /Could not save Workspace: Error: Quota exceeded/);
});

test("canceling an asynchronous save retires it before a new draft", async () => {
  let stored: string | null = null;
  const oldCapture = deferred<SavedWorkspaceCapture>();
  const focused: (SavedWorkspaceFocus | undefined)[] = [];
  const saves = createSavedWorkspaces({
    read: () => stored,
    write: value => { stored = value; },
    capture: () => oldCapture.promise,
    open: () => {},
    render: focus => { focused.push(focus); },
  });

  saves.beginSave();
  saves.setName("Old Workspace");
  const oldOperation = saves.save();
  saves.cancelSave();
  saves.beginSave();
  saves.setName("New Workspace");
  const focusBeforeSettlement = focused.length;

  oldCapture.resolve({
    kind: "complete-format-3",
    packet: "old-owner-issued-packet",
    canonicalLocation: "/?w=old#workspace",
    activeTabIndex: 0,
    coordinateCount: 1,
  });
  await oldOperation;

  assert.equal(stored, null);
  assert.deepEqual(saves.state.entries, []);
  assert.equal(saves.state.formOpen, true);
  assert.equal(saves.state.name, "New Workspace");
  assert.equal(saves.state.error, "");
  assert.equal(focused.length, focusBeforeSettlement);
});

for (const raw of [
  "{",
  '{"version":3,"entries":[]}',
  '{"version":1,"entries":[{"name":"A","packet":"p"},{"name":"a","packet":"q"}]}',
  '{"version":1,"entries":[{"name":"A","packet":null}]}',
  '{"version":2,"entries":[{"name":"A","kind":"complete-format-3","packet":"p","canonicalLocation":"/","activeTabIndex":0}]}',
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

test("version-1 records remain explicit legacy compatibility entries", () => {
  const h = harness(
    '{"version":1,"entries":[{"name":"Old","packet":"legacy-packet"}]}',
  );

  assert.deepEqual(h.saves.state.entries, [{
    name: "Old",
    kind: "legacy-format-1",
    packet: "legacy-packet",
  }]);

  h.save("New");
  const persisted: unknown = JSON.parse(h.stored() ?? "");
  assert.ok(
    persisted !== null
    && typeof persisted === "object"
    && "version" in persisted
    && "entries" in persisted);
  assert.equal(persisted.version, 2);
  assert.deepEqual(persisted.entries, [
    {
      name: "Old",
      kind: "legacy-format-1",
      packet: "legacy-packet",
    },
    {
      name: "New",
      kind: "complete-format-3",
      packet: "owner-issued-packet",
      canonicalLocation: "/?w=legacy-location#workspace",
      activeTabIndex: 0,
      coordinateCount: 1,
    },
  ]);
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
