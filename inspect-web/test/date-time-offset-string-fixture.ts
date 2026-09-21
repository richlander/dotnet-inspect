import type {
  DateTimeOffsetString,
} from "../src/facades/inspect-web-package.d.ts";

const roundTripTimestamp =
  /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/;

export function dateTimeOffsetString(value: string): DateTimeOffsetString {
  if (!roundTripTimestamp.test(value) || !Number.isFinite(Date.parse(value)))
    throw new TypeError(`Invalid DateTimeOffset fixture: ${value}`);
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return value as DateTimeOffsetString;
}
