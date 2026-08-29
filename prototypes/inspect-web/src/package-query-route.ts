export function isPackageQueryPath(pathname: string): boolean {
  return pathname === "/query" || pathname === "/query/";
}

export function validPackageQueryPrefix(value: string): string {
  const prefix = value.trim();
  return prefix.length > 0
    && prefix.length <= 100
    && !Array.from(prefix).some(character =>
      character.codePointAt(0)! < 0x20 || character.codePointAt(0) === 0x7f)
    ? prefix
    : "";
}
