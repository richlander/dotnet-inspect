// `dom-payload-boundary.test.ts` compares this product-owned contract with every
// canonical decoder call, so deleting or substituting an attribute's parser is red.
export const numericDomAttributes = {
  annotatedFact: ["parseNonNegativeInteger"],
  annotatedOffset: ["parseNonNegativeInteger"],
  depGroup: ["isSelectedGroupChip", "parseNonNegativeInteger"],
  mdeChip: ["parseNonNegativeInteger"],
  mdeIndex: ["parseNonNegativeInteger"],
  mdeJump: ["parseExplorerCoordinates"],
  mdeNeedsLoad: ["parseNonNegativeInteger"],
  mdeOpen: ["parseNonNegativeInteger"],
  mdePage: ["parseExplorerCoordinates"],
  mdeRow: ["parseExplorerCoordinates"],
  navOverload: ["parseNonNegativeInteger"],
  overload: ["parseNonNegativeInteger"],
  perfToken: ["parseMetadataToken"],
  slIndex: ["parseNonNegativeInteger"],
} as const;

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
  // Decimal only. The single producer of a metadata token payload interpolates
  // `member.metadataToken`, a number, so it always renders decimal; the hexadecimal branch
  // this parser used to carry had no producer and only the tests exercised it. It also
  // could not share the decimal branch's canonicality rule, since `"0x06000001"` is the
  // conventional zero-padded spelling of a token while `"0100663297"` is malformed. An
  // unreachable notation with its own rule is surface, not tolerance.
  if (!value || !/^(?:0|[1-9]\d*)$/.test(value)) return null;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed <= 0xffff_ffff
    ? parsed
    : null;
}

// The parser's rejection value is `null`, and `null` is also how the caller spells "no group
// is selected". Comparing the two directly marks every unparsable chip active precisely when
// nothing is selected -- the inverse of the intent, and a shape the older `Number(...)`
// comparison could not produce because `NaN !== NaN`.
export function isSelectedGroupChip(
  value: string | undefined,
  selectedGroupIndex: number | null,
): boolean {
  const group = parseNonNegativeInteger(value);
  return group !== null && group === selectedGroupIndex;
}

export function parseExplorerCoordinates(
  value: string | undefined,
): [number, number] | null {
  const parts = value?.split(":");
  if (!parts || parts.length !== 2) return null;
  const index = parseNonNegativeInteger(parts[0]);
  const rowId = parseNonNegativeInteger(parts[1]);
  return index !== null && rowId !== null ? [index, rowId] : null;
}
