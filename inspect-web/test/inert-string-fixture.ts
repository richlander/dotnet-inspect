import type { InertString } from "../src/facades/inspect-web-source.d.ts";
import type {
  InertString as MetadataInertString,
} from "../src/facades/inspect-web-metadata.d.ts";

function isInertStringWireValue(value: unknown): value is InertString {
  return typeof value === "string";
}

export function inertStringFixture(value: string): InertString {
  const parsed: unknown = JSON.parse(JSON.stringify(value));
  if (!isInertStringWireValue(parsed)) {
    throw new TypeError("The inert-string fixture must remain a JSON string.");
  }
  return parsed;
}

function isMetadataInertStringWireValue(
  value: unknown,
): value is MetadataInertString {
  return typeof value === "string";
}

export function metadataInertStringFixture(
  value: string,
): MetadataInertString {
  const parsed: unknown = JSON.parse(JSON.stringify(value));
  if (!isMetadataInertStringWireValue(parsed)) {
    throw new TypeError("The inert-string fixture must remain a JSON string.");
  }
  return parsed;
}
