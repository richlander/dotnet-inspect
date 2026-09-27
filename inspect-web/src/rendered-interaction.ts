const renderedInteractionSelector = "[data-rendered-interaction-key]";

function keyedElements(root: ParentNode): HTMLElement[] {
  return [...root.querySelectorAll<HTMLElement>(renderedInteractionSelector)];
}

function keyedElementMap(root: ParentNode): Map<string, HTMLElement> {
  const elements = new Map<string, HTMLElement>();
  const duplicates = new Set<string>();
  for (const element of keyedElements(root)) {
    const key = element.dataset.renderedInteractionKey;
    if (!key || duplicates.has(key)) continue;
    if (elements.has(key)) {
      elements.delete(key);
      duplicates.add(key);
      continue;
    }
    elements.set(key, element);
  }
  return elements;
}

function synchronizeAttributes(
  current: Element,
  replacement: Element,
): void {
  for (const attribute of Array.from(current.attributes)) {
    if (!replacement.hasAttribute(attribute.name)) {
      current.removeAttribute(attribute.name);
    }
  }
  for (const attribute of replacement.attributes) {
    current.setAttribute(attribute.name, attribute.value);
  }
}

function synchronizeNode(
  current: ChildNode,
  replacement: ChildNode,
): void {
  if (current.nodeType !== replacement.nodeType) {
    current.replaceWith(replacement);
    return;
  }
  if (current instanceof Element && replacement instanceof Element) {
    if (current.namespaceURI !== replacement.namespaceURI
      || current.tagName !== replacement.tagName) {
      current.replaceWith(replacement);
      return;
    }
    synchronizeAttributes(current, replacement);
    synchronizeChildren(current, replacement);
    return;
  }
  if (current.nodeValue !== replacement.nodeValue) {
    current.nodeValue = replacement.nodeValue;
  }
}

function synchronizeChildren(
  current: Element,
  replacement: Element,
): void {
  const currentChildren = Array.from(current.childNodes);
  const replacementChildren = Array.from(replacement.childNodes);
  const sharedCount = Math.min(
    currentChildren.length,
    replacementChildren.length,
  );
  for (let index = 0; index < sharedCount; index++) {
    synchronizeNode(currentChildren[index]!, replacementChildren[index]!);
  }
  for (const child of currentChildren.slice(sharedCount)) child.remove();
  for (const child of replacementChildren.slice(sharedCount)) {
    current.append(child);
  }
}

function synchronizeElement(
  current: HTMLElement,
  replacement: HTMLElement,
): void {
  synchronizeAttributes(current, replacement);
  synchronizeChildren(current, replacement);
}

function pathFromRoot(root: Node, descendant: Node): HTMLElement[] | null {
  const path: HTMLElement[] = [];
  let current: Node | null = descendant;
  while (current !== root) {
    if (!(current instanceof HTMLElement)) return null;
    path.unshift(current);
    current = current.parentNode;
    if (!current) return null;
  }
  return path;
}

function replaceChildrenAround(
  currentParent: Node,
  currentChild: ChildNode,
  replacementParent: Node,
  replacementChild: ChildNode,
): void {
  while (currentChild.previousSibling) currentChild.previousSibling.remove();
  while (currentChild.nextSibling) currentChild.nextSibling.remove();

  const replacementChildren = Array.from(replacementParent.childNodes);
  const replacementIndex = replacementChildren.indexOf(replacementChild);
  if (replacementIndex < 0) {
    throw new Error("Rendered interaction replacement path is invalid.");
  }
  for (const child of replacementChildren.slice(0, replacementIndex)) {
    currentParent.insertBefore(child, currentChild);
  }
  for (const child of replacementChildren.slice(replacementIndex + 1)) {
    currentParent.appendChild(child);
  }
}

function replaceAroundPinnedInteraction(
  root: HTMLElement,
  replacementRoot: DocumentFragment,
  current: HTMLElement,
  replacement: HTMLElement,
): void {
  const currentPath = pathFromRoot(root, current);
  const replacementPath = pathFromRoot(replacementRoot, replacement);
  if (!currentPath || !replacementPath
    || currentPath.length !== replacementPath.length
    || currentPath.some((element, index) =>
      element.tagName !== replacementPath[index]?.tagName)) {
    throw new Error(
      "A retained rendered interaction changed its ancestor structure.",
    );
  }

  let currentParent: Node = root;
  let replacementParent: Node = replacementRoot;
  for (let index = 0; index < currentPath.length; index++) {
    const currentChild = currentPath[index];
    const replacementChild = replacementPath[index];
    if (!currentChild || !replacementChild) {
      throw new Error("Rendered interaction path reconciliation failed.");
    }
    replaceChildrenAround(
      currentParent,
      currentChild,
      replacementParent,
      replacementChild,
    );
    if (index === currentPath.length - 1) {
      synchronizeElement(currentChild, replacementChild);
      continue;
    }
    synchronizeAttributes(currentChild, replacementChild);
    currentParent = currentChild;
    replacementParent = replacementChild;
  }
}

function restoreRenderedInteractions(
  existing: Map<string, HTMLElement>,
  nextRoot: ParentNode,
): void {
  for (const replacement of keyedElements(nextRoot)) {
    const key = replacement.dataset.renderedInteractionKey;
    if (!key) continue;
    const current = existing.get(key);
    existing.delete(key);
    if (!current || current.tagName !== replacement.tagName) continue;
    synchronizeElement(current, replacement);
    replacement.replaceWith(current);
  }
}

export function replaceChildrenPreservingRenderedInteractions(
  root: HTMLElement,
  html: string,
): void {
  const existing = keyedElementMap(root);
  if (existing.size === 0) {
    root.innerHTML = html;
    return;
  }

  const parent = root.parentNode;
  if (!parent || !root.isConnected) {
    throw new Error(
      "Rendered interaction continuity requires a connected replacement root.",
    );
  }

  const template = root.ownerDocument.createElement("template");
  template.innerHTML = html;
  const active = root.ownerDocument.activeElement;
  const activeKey = active instanceof HTMLElement
    ? active.dataset.renderedInteractionKey
    : undefined;
  const pinned = activeKey ? existing.get(activeKey) : undefined;
  const pinnedReplacement = activeKey
    ? keyedElementMap(template.content).get(activeKey)
    : undefined;
  const preservesPinnedInteraction =
    pinned !== undefined
    && pinnedReplacement !== undefined
    && pinned.tagName === pinnedReplacement.tagName;

  const parking = root.ownerDocument.createElement("div");
  parent.insertBefore(parking, root.nextSibling);
  for (const element of existing.values()) {
    if (preservesPinnedInteraction && element === pinned) continue;
    parking.append(element);
  }

  try {
    if (preservesPinnedInteraction) {
      replaceAroundPinnedInteraction(
        root,
        template.content,
        pinned,
        pinnedReplacement,
      );
      const pinnedKey = pinned.dataset.renderedInteractionKey;
      if (!pinnedKey) {
        throw new Error("Pinned rendered interaction has no identity.");
      }
      existing.delete(pinnedKey);
    } else {
      root.replaceChildren(template.content);
    }
    restoreRenderedInteractions(existing, root);
  } finally {
    parking.remove();
  }
}
