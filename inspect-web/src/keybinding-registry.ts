export interface KeybindingModifiers {
  alt?: boolean;
  commandOrControl?: boolean;
  control?: boolean;
  meta?: boolean;
  shift?: boolean;
}

export interface Keybinding {
  id: string;
  key: string | readonly string[];
  available?: () => boolean;
  modifiers?: KeybindingModifiers;
  allowExtraModifiers?: boolean;
  priority?: number;
  when?: (event: KeyboardEvent) => boolean;
  run: (event: KeyboardEvent) => boolean;
  preventDefault?: boolean;
}

export interface KeybindingDescription {
  id: string;
  keys: readonly string[];
  modifiers: Readonly<KeybindingModifiers>;
  allowExtraModifiers: boolean;
  priority: number;
  preventDefault: boolean;
}

export interface KeybindingConflict {
  event: KeyboardEvent;
  bindings: readonly KeybindingDescription[];
}

export interface KeybindingRegistryOptions {
  onConflict?: (conflict: KeybindingConflict) => void;
  respectDefaultPrevented?: boolean;
}

export interface KeybindingDispatchResult {
  handled: boolean;
  bindingId?: string;
}

interface RegisteredKeybinding {
  available?: () => boolean;
  description: KeybindingDescription;
  order: number;
  when?: (event: KeyboardEvent) => boolean;
  run: (event: KeyboardEvent) => boolean;
}

interface Candidate {
  binding: RegisteredKeybinding;
  scopeDepth: number;
}

function normalizeKey(key: string): string {
  return key.length === 1 ? key.toLowerCase() : key;
}

function modifierMatches(
  actual: boolean,
  expected: boolean | undefined,
  allowExtraModifiers: boolean,
): boolean {
  return expected === undefined
    ? allowExtraModifiers || !actual
    : actual === expected;
}

function matches(
  binding: RegisteredKeybinding,
  event: KeyboardEvent,
): boolean {
  if (binding.available && !binding.available()) return false;
  const { description } = binding;
  if (!description.keys.includes(normalizeKey(event.key))) return false;

  const modifiers = description.modifiers;
  if (!modifierMatches(
    event.altKey,
    modifiers.alt,
    description.allowExtraModifiers,
  )) return false;
  if (!modifierMatches(
    event.shiftKey,
    modifiers.shift,
    description.allowExtraModifiers,
  )) return false;

  if (modifiers.commandOrControl !== undefined) {
    if (modifiers.control !== undefined || modifiers.meta !== undefined) {
      throw new Error(
        `Keybinding '${description.id}' combines commandOrControl with control or meta.`,
      );
    }
    if ((event.ctrlKey || event.metaKey) !== modifiers.commandOrControl) {
      return false;
    }
  } else {
    if (!modifierMatches(
      event.ctrlKey,
      modifiers.control,
      description.allowExtraModifiers,
    )) return false;
    if (!modifierMatches(
      event.metaKey,
      modifiers.meta,
      description.allowExtraModifiers,
    )) return false;
  }

  return binding.when?.(event) ?? true;
}

function eventPath(event: KeyboardEvent): readonly EventTarget[] {
  const path = event.composedPath();
  return path.length > 0
    ? path
    : event.target
      ? [event.target]
      : [];
}

function isKeyboardEvent(event: Event): event is KeyboardEvent {
  return "key" in event;
}

export class KeybindingRegistry {
  readonly #globalBindings: RegisteredKeybinding[] = [];
  readonly #scopedBindings =
    new WeakMap<EventTarget, RegisteredKeybinding[]>();
  readonly #onConflict: ((conflict: KeybindingConflict) => void) | undefined;
  readonly #respectDefaultPrevented: boolean;
  #nextOrder = 0;

  constructor(options: KeybindingRegistryOptions = {}) {
    this.#onConflict = options.onConflict;
    this.#respectDefaultPrevented = options.respectDefaultPrevented ?? true;
  }

  register(binding: Keybinding, scope?: EventTarget): () => void {
    if (binding.id.trim() === "") {
      throw new Error("A keybinding id cannot be empty.");
    }
    const keys = [...new Set((typeof binding.key === "string"
      ? [binding.key]
      : [...binding.key])
      .map(normalizeKey))];
    if (keys.length === 0 || keys.some(key => key === "")) {
      throw new Error(`Keybinding '${binding.id}' must declare a key.`);
    }
    const priority = binding.priority ?? 0;
    if (!Number.isFinite(priority)) {
      throw new Error(`Keybinding '${binding.id}' has a non-finite priority.`);
    }
    if (binding.modifiers?.commandOrControl !== undefined
      && (binding.modifiers.control !== undefined
        || binding.modifiers.meta !== undefined)) {
      throw new Error(
        `Keybinding '${binding.id}' combines commandOrControl with control or meta.`,
      );
    }

    const description = Object.freeze({
      id: binding.id,
      keys: Object.freeze(keys),
      modifiers: Object.freeze({ ...binding.modifiers }),
      allowExtraModifiers: binding.allowExtraModifiers ?? false,
      priority,
      preventDefault: binding.preventDefault ?? true,
    });
    const registered: RegisteredKeybinding = {
      description,
      order: this.#nextOrder++,
      run: binding.run,
      ...(binding.available ? { available: binding.available } : {}),
      ...(binding.when ? { when: binding.when } : {}),
    };
    const bindings = scope === undefined
      ? this.#globalBindings
      : this.#scopedBindings.get(scope) ?? [];
    if (scope !== undefined && !this.#scopedBindings.has(scope)) {
      this.#scopedBindings.set(scope, bindings);
    }
    bindings.push(registered);

    return () => {
      const index = bindings.indexOf(registered);
      if (index >= 0) bindings.splice(index, 1);
    };
  }

  bindingsFor(scope?: EventTarget): readonly KeybindingDescription[] {
    const bindings = scope === undefined
      ? this.#globalBindings
      : this.#scopedBindings.get(scope) ?? [];
    return bindings.map(binding => binding.description);
  }

  availableBindingsFor(
    scope?: EventTarget,
  ): readonly KeybindingDescription[] {
    const bindings = scope === undefined
      ? this.#globalBindings
      : this.#scopedBindings.get(scope) ?? [];
    return bindings
      .filter(binding => binding.available?.() ?? true)
      .map(binding => binding.description);
  }

  dispatch(event: KeyboardEvent): KeybindingDispatchResult {
    if (this.#respectDefaultPrevented && event.defaultPrevented) {
      return { handled: false };
    }

    const candidates: Candidate[] = [];
    const path = eventPath(event);
    for (let scopeDepth = 0; scopeDepth < path.length; scopeDepth++) {
      const scope = path[scopeDepth];
      if (!scope) continue;
      const bindings = this.#scopedBindings.get(scope);
      if (!bindings) continue;
      for (const binding of bindings) {
        if (matches(binding, event)) {
          candidates.push({ binding, scopeDepth });
        }
      }
    }
    for (const binding of this.#globalBindings) {
      if (matches(binding, event)) {
        candidates.push({ binding, scopeDepth: Number.MAX_SAFE_INTEGER });
      }
    }
    candidates.sort((left, right) =>
      right.binding.description.priority
        - left.binding.description.priority
      || left.scopeDepth - right.scopeDepth
      || left.binding.order - right.binding.order);

    for (let index = 0; index < candidates.length;) {
      const first = candidates[index];
      if (!first) break;
      let end = index + 1;
      while (end < candidates.length) {
        const candidate = candidates[end];
        if (!candidate
          || candidate.binding.description.priority
            !== first.binding.description.priority
          || candidate.scopeDepth !== first.scopeDepth) {
          break;
        }
        end++;
      }
      const group = candidates.slice(index, end);
      if (group.length > 1) {
        this.#onConflict?.({
          event,
          bindings: group.map(candidate => candidate.binding.description),
        });
      }
      for (const candidate of group) {
        if (!candidate.binding.run(event)) continue;
        if (candidate.binding.description.preventDefault) {
          event.preventDefault();
        }
        return {
          handled: true,
          bindingId: candidate.binding.description.id,
        };
      }
      index = end;
    }

    return { handled: false };
  }

  attach(target: EventTarget): () => void {
    const listener: EventListener = event => {
      if (isKeyboardEvent(event)) this.dispatch(event);
    };
    target.addEventListener("keydown", listener);
    return () => target.removeEventListener("keydown", listener);
  }
}
