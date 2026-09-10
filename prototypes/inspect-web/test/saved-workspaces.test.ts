import assert from "node:assert/strict";
import test from "node:test";
import {
  createSavedWorkspaces,
  type SavedWorkspace,
  type SavedWorkspaceFocus,
} from "../src/saved-workspaces.ts";
import { renderSavedWorkspaces, renderWorkspaceSaveButton } from "../src/saved-workspaces-view.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((accept, deny) => {
    resolve = accept;
    reject = deny;
  });
  return { promise, resolve, reject };
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
    capture: async () => {
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
    async save(name: string) {
      saves.beginSave();
      saves.setName(name);
      await saves.save();
    },
  };
}

test("named saves retain the exact opaque packet and reopen across reload without recapture", async () => {
  const h = harness();
  assert.equal(h.saves.state.available, true);
  assert.equal(h.captures(), 0);
  await h.save("  Json study  ");
  assert.deepEqual(h.saves.state.entries, [{ name: "Json study", packet: "owner-issued-packet" }]);
  assert.equal(h.saves.state.formOpen, false);
  assert.deepEqual(h.focused.at(-1), { kind: "saved-open", name: "Json study", index: 0 });
  const reloaded = h.reload();
  assert.equal(h.captures(), 1);
  reloaded.open("Json study");
  assert.deepEqual(h.opened, [{ name: "Json study", packet: "owner-issued-packet" }]);
  assert.equal(h.captures(), 1);
});

test("duplicate names and invalid names do not replace or capture another save", async () => {
  const h = harness();
  await h.save("Study");
  const before = h.stored();
  for (const name of ["STUDY", " study ", "   ", "x".repeat(121)]) {
    await h.save(name);
    assert.equal(h.stored(), before);
    assert.equal(h.saves.state.entries.length, 1);
    assert.ok(h.saves.state.error);
    assert.equal(h.saves.state.formOpen, true);
  }
  assert.equal(h.captures(), 1);
});

test("forget removes only its saved identity and never opens or recaptures a Workspace", async () => {
  const h = harness();
  await h.save("First");
  await h.save("Second");
  h.saves.forget("First");
  assert.deepEqual(h.saves.state.entries, [{ name: "Second", packet: "owner-issued-packet" }]);
  assert.deepEqual(h.reload().state.entries, h.saves.state.entries);
  assert.deepEqual(h.opened, []);
  assert.equal(h.captures(), 2);
  assert.deepEqual(h.focused.at(-1), { kind: "saved-remove", name: "First", index: 0 });
});

test("write failure preserves saved entries and draft text on save and forget", async () => {
  const h = harness();
  await h.save("First");
  const before = h.stored();
  h.failWrite();
  await h.save("Second");
  assert.equal(h.stored(), before);
  assert.deepEqual(h.saves.state.entries, [{ name: "First", packet: "owner-issued-packet" }]);
  assert.equal(h.saves.state.name, "Second");
  assert.match(h.saves.state.error, /Quota exceeded/);
  h.saves.forget("First");
  assert.equal(h.stored(), before);
  assert.equal(h.saves.state.entries.length, 1);
  assert.match(h.saves.state.error, /Could not forget/);
});

test("failed projection cannot persist a partial or empty save", async () => {
  const h = harness();
  h.failCapture();
  await h.save("Study");
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
  test(`unreadable saved data is reported and not overwritten: ${raw}`, async () => {
    const h = harness(raw);
    assert.equal(h.saves.state.available, false);
    assert.match(h.saves.state.error, /Could not read/);
    await h.save("New");
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

test("saved entry rendering keeps names escaped and Open separate from Forget", async () => {
  const h = harness();
  await h.save('Study <"A">');
  const view = { state: h.saves.state, canSave: false, canOpen: true };
  assert.match(renderWorkspaceSaveButton(view), / disabled/);
  const html = renderSavedWorkspaces(view, escapeHtml);
  assert.match(html, /data-saved-workspace-open="Study &lt;&quot;A&quot;&gt;"/);
  assert.match(html, /aria-label="Forget saved Workspace Study &lt;&quot;A&quot;&gt;"/);
  assert.equal((html.match(/<button\b/g) ?? []).length, 2);
  assert.doesNotMatch(html, /owner-issued-packet/);
});

test("an in-flight capture permits cancel but not duplicate or stale publication", async () => {
  let resolveCapture!: (packet: string) => void;
  const capture = new Promise<string>(resolve => { resolveCapture = resolve; });
  let captures = 0;
  let stored: string | null = null;
  const saves = createSavedWorkspaces({
    read: () => stored,
    write: value => { stored = value; },
    capture: () => {
      captures++;
      return capture;
    },
    open: () => {},
    render: () => {},
  });
  saves.beginSave();
  saves.setName("Deferred");
  const pending = saves.save();
  assert.equal(saves.state.saving, true);
  assert.equal(captures, 1);
  await saves.save();
  assert.equal(captures, 1);
  saves.cancelSave();
  resolveCapture("stale-packet");
  await pending;
  assert.equal(stored, null);
  assert.deepEqual(saves.state.entries, []);
  assert.equal(saves.state.formOpen, false);
  assert.equal(saves.state.saving, false);
});

test("navigation supersedes both successful and rejected capture completion", async () => {
  for (const reject of [false, true]) {
    const capture = deferred<string>();
    let generation = 1;
    let stored: string | null = null;
    let renders = 0;
    const saves = createSavedWorkspaces({
      read: () => stored,
      write: value => { stored = value; },
      capture: () => capture.promise,
      captureGeneration: () => generation,
      open: () => {},
      render: () => { renders++; },
    });
    saves.beginSave();
    saves.setName("Superseded");
    const pending = saves.save();
    const rendersBeforeNavigation = renders;
    generation++;
    if (reject) capture.reject(new Error("stale capture"));
    else capture.resolve("stale-packet");
    await pending;
    assert.equal(stored, null);
    assert.deepEqual(saves.state.entries, []);
    assert.equal(saves.state.formOpen, false);
    assert.equal(saves.state.saving, false);
    assert.equal(saves.state.error, "");
    assert.equal(renders, rendersBeforeNavigation);
  }
});

test("a replacement save is the only capture allowed to publish", async () => {
  const first = deferred<string>();
  const second = deferred<string>();
  let captures = 0;
  let stored: string | null = null;
  const saves = createSavedWorkspaces({
    read: () => stored,
    write: value => { stored = value; },
    capture: () => ++captures === 1 ? first.promise : second.promise,
    open: () => {},
    render: () => {},
  });
  saves.beginSave();
  saves.setName("First");
  const stale = saves.save();
  saves.beginSave();
  saves.setName("Second");
  const current = saves.save();
  second.resolve("second-packet");
  await current;
  first.resolve("first-packet");
  await stale;
  assert.deepEqual(saves.state.entries, [
    { name: "Second", packet: "second-packet" },
  ]);
  assert.match(stored ?? "", /second-packet/);
  assert.doesNotMatch(stored ?? "", /first-packet/);
});

test("the save form disables capture inputs while preserving Cancel", () => {
  const h = harness();
  h.saves.beginSave();
  h.saves.state.saving = true;
  const html = renderSavedWorkspaces(
    { state: h.saves.state, canSave: true, canOpen: true },
    escapeHtml);
  assert.match(html, /id="workspace-save-name"[^>]* disabled/);
  assert.match(html, /data-workspace-save-submit disabled/);
  assert.match(html, /data-workspace-save-cancel>Cancel/);
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
