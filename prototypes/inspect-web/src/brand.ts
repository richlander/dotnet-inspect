function escapeAttribute(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

export function renderBrand(options: {
  href?: string;
  ariaLabel?: string;
  id?: string;
} = {}): string {
  const href = escapeAttribute(options.href ?? "/");
  const ariaLabel = escapeAttribute(
    options.ariaLabel ?? "dotnet inspect home");
  const id = options.id
    ? ` id="${escapeAttribute(options.id)}"`
    : "";
  return `<a${id} class="brand" href="${href}" aria-label="${ariaLabel}"><span class="brand-icon"><img src="/assets/dotnet-inspect-bot.png" width="28" height="28" alt="" /></span><span>dotnet-inspect</span></a>`;
}
