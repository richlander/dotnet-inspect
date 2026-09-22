function documentFocusableElements(
  document: Document,
  menu: HTMLElement,
): HTMLElement[] {
  return [...document.querySelectorAll<HTMLElement>(
    'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), '
      + 'textarea:not([disabled]), [tabindex]',
  )].filter(element =>
    !element.hidden
    && element.tabIndex >= 0
    && element.getClientRects().length > 0
    && !menu.contains(element));
}

export function continueMenuButtonDocumentOrder(
  button: HTMLElement,
  menu: HTMLElement,
  event: KeyboardEvent,
  closeMenu: () => void,
): void {
  const focusable = documentFocusableElements(button.ownerDocument, menu);
  const buttonIndex = focusable.indexOf(button);
  const target = event.shiftKey
    ? focusable[buttonIndex - 1]
    : focusable[buttonIndex + 1];
  if (!target) {
    closeMenu();
    button.focus({ preventScroll: true });
    return;
  }
  event.preventDefault();
  closeMenu();
  target.focus();
}
