export const fakeDom = {
  event(values: object = {}): Event {
    // Test fakes implement exactly the DOM subset consumed by each binder.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return values as unknown as Event;
  },
  eventTarget(value: object): EventTarget {
    // Test fakes implement exactly the DOM subset consumed by each binder.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return value as unknown as EventTarget;
  },
  document(value: object): Document {
    // Test fakes implement exactly the DOM subset consumed by each helper.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return value as unknown as Document;
  },
  element(value: object): Element {
    // Test fakes implement exactly the DOM subset consumed by each helper.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return value as unknown as Element;
  },
  htmlElement(value: object): HTMLElement {
    // Test fakes implement exactly the DOM subset consumed by each binder.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return value as unknown as HTMLElement;
  },
  keyboardEvent(values: object): KeyboardEvent {
    // Test fakes implement exactly the DOM subset consumed by each binder.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return values as unknown as KeyboardEvent;
  },
  parentNode(value: object): ParentNode {
    // Test fakes implement exactly the DOM subset consumed by each binder.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return value as unknown as ParentNode;
  },
};
