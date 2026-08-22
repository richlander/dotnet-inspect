export function parseNonNegativeInteger(
  value: string | undefined,
): number | null {
  if (!value || !/^(?:0|[1-9]\d*)$/.test(value)) return null;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) ? parsed : null;
}

export function parseMetadataToken(
  value: string | undefined,
): number | null {
  if (!value || !/^(?:0|[1-9]\d*|0x[\da-f]+)$/i.test(value)) return null;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed <= 0xffff_ffff
    ? parsed
    : null;
}
