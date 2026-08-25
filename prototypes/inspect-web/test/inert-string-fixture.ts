import type { InertString } from "../src/inspect-web-engine.d.ts";

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
