import type {
  DateTimeOffsetString,
} from "../src/facades/inspect-web-package.d.ts";
import {
  isDateTimeOffsetJsonString,
} from "../src/engine-worker-package-changes.ts";

export function dateTimeOffsetString(value: string): DateTimeOffsetString {
  if (!isDateTimeOffsetJsonString(value))
    throw new TypeError(`Invalid DateTimeOffset fixture: ${value}`);
  return value;
}
