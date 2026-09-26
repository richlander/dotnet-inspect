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

function synchronizeElement(
  current: HTMLElement,
  replacement: HTMLElement,
): void {
  for (const attribute of Array.from(current.attributes)) {
    if (!replacement.hasAttribute(attribute.name)) {
      current.removeAttribute(attribute.name);
    }
  }
  for (const attribute of replacement.attributes) {
    current.setAttribute(attribute.name, attribute.value);
  }
  if (current.innerHTML !== replacement.innerHTML) {
    current.innerHTML = replacement.innerHTML;
  }
}

export interface RenderedInteractionCapture {
  restore(root: ParentNode): void;
}

export function captureRenderedInteractions(
  root: ParentNode,
): RenderedInteractionCapture {
  const existing = keyedElementMap(root);
  return {
    restore(nextRoot) {
      for (const replacement of keyedElements(nextRoot)) {
        const key = replacement.dataset.renderedInteractionKey;
        if (!key) continue;
        const current = existing.get(key);
        existing.delete(key);
        if (!current || current.tagName !== replacement.tagName) continue;
        synchronizeElement(current, replacement);
        replacement.replaceWith(current);
      }
    },
  };
}

export function replaceChildrenPreservingRenderedInteractions(
  root: HTMLElement,
  html: string,
): void {
  const capture = captureRenderedInteractions(root);
  root.innerHTML = html;
  capture.restore(root);
}
