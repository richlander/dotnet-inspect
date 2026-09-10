import assert from "node:assert/strict";
import test from "node:test";
import {
  KeybindingRegistry,
  type KeybindingConflict,
} from "../src/keybinding-registry.ts";
import { fakeDom } from "./fake-dom.ts";

interface KeyboardEventOptions {
  altKey?: boolean;
  ctrlKey?: boolean;
  defaultPrevented?: boolean;
  key?: string;
  metaKey?: boolean;
  path?: readonly EventTarget[];
  shiftKey?: boolean;
  target?: EventTarget | null;
}

function keyboardEvent(options: KeyboardEventOptions = {}) {
  let prevented = options.defaultPrevented ?? false;
  const target = options.target ?? null;
  const event = fakeDom.keyboardEvent({
    altKey: options.altKey ?? false,
    ctrlKey: options.ctrlKey ?? false,
    get defaultPrevented() {
      return prevented;
    },
    key: options.key ?? "k",
    metaKey: options.metaKey ?? false,
    shiftKey: options.shiftKey ?? false,
    target,
    composedPath: () => options.path ?? (target ? [target] : []),
    preventDefault: () => {
      prevented = true;
    },
  });
  return {
    event,
    prevented: () => prevented,
  };
}

test("a higher-priority active binding is the single winner", () => {
  const calls: string[] = [];
  const registry = new KeybindingRegistry();
  registry.register({
    id: "fallback",
    key: "k",
    priority: 10,
    run: () => {
      calls.push("fallback");
      return true;
    },
  });
  registry.register({
    id: "winner",
    key: "k",
    priority: 20,
    run: () => {
      calls.push("winner");
      return true;
    },
  });
  const input = keyboardEvent();

  assert.deepEqual(registry.dispatch(input.event), {
    handled: true,
    bindingId: "winner",
  });
  assert.deepEqual(calls, ["winner"]);
  assert.equal(input.prevented(), true);
});

test("matched but unhandled bindings fall through", () => {
  const calls: string[] = [];
  const registry = new KeybindingRegistry();
  registry.register({
    id: "conditional",
    key: "Escape",
    priority: 20,
    run: () => {
      calls.push("conditional");
      return false;
    },
  });
  registry.register({
    id: "fallback",
    key: "Escape",
    priority: 10,
    run: () => {
      calls.push("fallback");
      return true;
    },
  });

  assert.equal(
    registry.dispatch(keyboardEvent({ key: "Escape" }).event).bindingId,
    "fallback",
  );
  assert.deepEqual(calls, ["conditional", "fallback"]);
});

test("context predicates remove inactive bindings from arbitration", () => {
  let active = false;
  const registry = new KeybindingRegistry();
  registry.register({
    id: "scoped",
    key: "p",
    when: () => active,
    run: () => true,
  });

  assert.equal(registry.dispatch(keyboardEvent({ key: "p" }).event).handled, false);
  active = true;
  assert.equal(registry.dispatch(keyboardEvent({ key: "p" }).event).handled, true);
});

test("availability controls dispatch and description projection together", () => {
  let available = false;
  const registry = new KeybindingRegistry();
  registry.register({
    id: "contextual",
    key: "p",
    available: () => available,
    run: () => true,
  });

  assert.deepEqual(registry.bindingsFor().map(binding => binding.id), [
    "contextual",
  ]);
  assert.deepEqual(registry.availableBindingsFor(), []);
  assert.equal(
    registry.dispatch(keyboardEvent({ key: "p" }).event).handled,
    false);

  available = true;
  assert.deepEqual(
    registry.availableBindingsFor().map(binding => binding.id),
    ["contextual"]);
  assert.equal(
    registry.dispatch(keyboardEvent({ key: "p" }).event).handled,
    true);
});

test("the closest event-path scope wins at equal priority", () => {
  const parent = fakeDom.eventTarget({});
  const child = fakeDom.eventTarget({});
  const calls: string[] = [];
  const registry = new KeybindingRegistry();
  registry.register({
    id: "parent",
    key: "ArrowDown",
    run: () => {
      calls.push("parent");
      return true;
    },
  }, parent);
  registry.register({
    id: "child",
    key: "ArrowDown",
    run: () => {
      calls.push("child");
      return true;
    },
  }, child);

  const result = registry.dispatch(keyboardEvent({
    key: "ArrowDown",
    target: child,
    path: [child, parent],
  }).event);

  assert.equal(result.bindingId, "child");
  assert.deepEqual(calls, ["child"]);
});

test("a scoped gesture arbitrates the stack-navigation collision", () => {
  const list = fakeDom.eventTarget({});
  const calls: string[] = [];
  const registry = new KeybindingRegistry();
  registry.register({
    id: "history.back",
    key: "ArrowLeft",
    modifiers: { shift: true },
    priority: 100,
    run: () => {
      calls.push("history");
      return true;
    },
  });
  const disposeList = registry.register({
    id: "list.step",
    key: "ArrowLeft",
    modifiers: { shift: true },
    priority: 200,
    run: () => {
      calls.push("list");
      return true;
    },
  }, list);
  const input = () => keyboardEvent({
    key: "ArrowLeft",
    shiftKey: true,
    target: list,
    path: [list],
  }).event;

  assert.equal(registry.dispatch(input()).bindingId, "list.step");
  assert.deepEqual(calls, ["list"]);

  disposeList();
  registry.register({
    id: "list.disabled-step",
    key: "ArrowLeft",
    modifiers: { shift: true },
    priority: 200,
    run: () => false,
  }, list);
  assert.equal(registry.dispatch(input()).bindingId, "history.back");
  assert.deepEqual(calls, ["list", "history"]);
});

test("exact modifiers prevent shifted arrows from matching plain arrows", () => {
  const registry = new KeybindingRegistry();
  registry.register({
    id: "plain",
    key: "ArrowLeft",
    run: () => true,
  });
  registry.register({
    id: "shifted",
    key: "ArrowLeft",
    modifiers: { shift: true },
    run: () => true,
  });

  assert.equal(
    registry.dispatch(keyboardEvent({ key: "ArrowLeft" }).event).bindingId,
    "plain",
  );
  assert.equal(registry.dispatch(keyboardEvent({
    key: "ArrowLeft",
    shiftKey: true,
  }).event).bindingId, "shifted");
});

test("commandOrControl matches either platform modifier and excludes plain input", () => {
  const registry = new KeybindingRegistry();
  registry.register({
    id: "palette",
    key: "K",
    modifiers: { commandOrControl: true },
    run: () => true,
  });

  assert.equal(
    registry.dispatch(keyboardEvent({ key: "k", ctrlKey: true }).event).handled,
    true,
  );
  assert.equal(
    registry.dispatch(keyboardEvent({ key: "K", metaKey: true }).event).handled,
    true,
  );
  assert.equal(
    registry.dispatch(keyboardEvent({ key: "k" }).event).handled,
    false,
  );
});

test("aliases and explicitly allowed extra modifiers are supported", () => {
  const registry = new KeybindingRegistry();
  registry.register({
    id: "zoom",
    key: ["+", "="],
    allowExtraModifiers: true,
    run: () => true,
  });

  assert.equal(registry.dispatch(keyboardEvent({
    key: "+",
    shiftKey: true,
  }).event).handled, true);
  assert.equal(registry.dispatch(keyboardEvent({
    key: "=",
    altKey: true,
  }).event).handled, true);
});

test("equal-precedence ambiguity is reported and remains deterministic", () => {
  const conflicts: KeybindingConflict[] = [];
  const registry = new KeybindingRegistry({
    onConflict: conflict => conflicts.push(conflict),
  });
  registry.register({ id: "first", key: "x", run: () => true });
  registry.register({ id: "second", key: "x", run: () => true });

  assert.equal(
    registry.dispatch(keyboardEvent({ key: "x" }).event).bindingId,
    "first",
  );
  assert.deepEqual(
    conflicts[0]?.bindings.map(binding => binding.id),
    ["first", "second"],
  );
});

test("disposal, introspection, default behavior, and validation are explicit", () => {
  const scope = fakeDom.eventTarget({});
  const registry = new KeybindingRegistry();
  const dispose = registry.register({
    id: "native",
    key: "Enter",
    preventDefault: false,
    run: () => true,
  }, scope);

  assert.deepEqual(registry.bindingsFor(scope), [{
    id: "native",
    keys: ["Enter"],
    modifiers: {},
    allowExtraModifiers: false,
    priority: 0,
    preventDefault: false,
  }]);
  const input = keyboardEvent({
    key: "Enter",
    target: scope,
    path: [scope],
  });
  assert.equal(registry.dispatch(input.event).handled, true);
  assert.equal(input.prevented(), false);
  dispose();
  assert.deepEqual(registry.bindingsFor(scope), []);
  assert.equal(registry.dispatch(input.event).handled, false);

  assert.throws(
    () => registry.register({ id: "", key: "x", run: () => true }),
    /id cannot be empty/,
  );
  assert.throws(
    () => registry.register({ id: "missing", key: [], run: () => true }),
    /must declare a key/,
  );
  assert.throws(
    () => registry.register({
      id: "mixed",
      key: "x",
      modifiers: { commandOrControl: true, control: true },
      run: () => true,
    }),
    /combines commandOrControl/,
  );
});

test("already-handled events are respected by default", () => {
  const registry = new KeybindingRegistry();
  registry.register({ id: "late", key: "x", run: () => true });

  assert.equal(registry.dispatch(keyboardEvent({
    key: "x",
    defaultPrevented: true,
  }).event).handled, false);
});

test("attach owns one keydown listener and returns its teardown", () => {
  const listeners: EventListener[] = [];
  let removed: EventListener | null = null;
  const target = fakeDom.eventTarget({
    addEventListener(type: string, listener: EventListener) {
      assert.equal(type, "keydown");
      listeners.push(listener);
    },
    removeEventListener(type: string, listener: EventListener) {
      assert.equal(type, "keydown");
      removed = listener;
    },
  });
  let calls = 0;
  const registry = new KeybindingRegistry();
  registry.register({
    id: "attached",
    key: "x",
    run: () => {
      calls++;
      return true;
    },
  });

  const detach = registry.attach(target);
  const listener = listeners[0];
  assert.ok(listener);
  listener(keyboardEvent({ key: "x" }).event);
  assert.equal(calls, 1);
  detach();
  assert.equal(removed, listener);
});
