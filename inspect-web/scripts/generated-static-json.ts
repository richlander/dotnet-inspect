const bigintMarker = "__dotnet_inspect_generated_bigint__:";

export function parseGeneratedStaticJson(initializer: string): unknown {
  const json = initializer.replace(
    /(^|[[:,]\s*)(-?[0-9]+)n(?=\s*[,}\]])/gu,
    (_match, prefix: string, digits: string) =>
      `${prefix}"${bigintMarker}${digits}"`,
  );
  const parsed: unknown = JSON.parse(json);
  return restoreBigInts(parsed);
}

function restoreBigInts(value: unknown): unknown {
  if (typeof value === "string" && value.startsWith(bigintMarker)) {
    return BigInt(value.slice(bigintMarker.length));
  }
  if (Array.isArray(value)) {
    return value.map(restoreBigInts);
  }
  if (isRecord(value)) {
    return Object.fromEntries(
      Object.entries(value).map(([key, item]) =>
        [key, restoreBigInts(item)]),
    );
  }
  return value;
}

function isRecord(
  value: unknown,
): value is Readonly<Record<string, unknown>> {
  return typeof value === "object"
    && value !== null
    && !Array.isArray(value);
}
