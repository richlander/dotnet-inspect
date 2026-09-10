import type {
  OperationCancelReason,
  OperationProducerSink,
} from "./operation-authority.ts";

function cancellationReason(reason: string | null): OperationCancelReason {
  switch (reason) {
    case "user":
    case "superseded":
    case "disposed":
    case "feature-observer-failed":
    case "timeout":
    case "worker-restarted":
      return reason;
    default:
      throw new Error("Unknown comparison cancellation reason.");
  }
}

// Transport success preserves the query's verdict, including unavailable evidence.
export function reportComparisonEnvelope<TValue>(
  sink: OperationProducerSink<TValue, unknown, never>,
  subject: string,
  version: number,
  kind: string | number,
  value: TValue | null,
  failureKind: string | number | null,
  error: string | null,
  diagnostic: string | null,
  reason: string | null,
): undefined {
  try {
    if (version !== 1)
      throw new Error(`Unsupported ${subject} result version.`);
    switch (kind) {
      case "Succeeded":
        if (value === null || typeof value !== "object")
          throw new Error(`A ${subject} success carries no value.`);
        sink.reportTerminal({ kind: "succeeded", value });
        break;
      case "Failed": {
        if (typeof error !== "string" || typeof diagnostic !== "string")
          throw new Error(`A ${subject} failure has no error or diagnostic.`);
        const failure = new Error(error);
        if (failureKind === "Expected")
          sink.reportTerminal({ kind: "failed", error: failure });
        else if (failureKind === "Unexpected")
          sink.reportUnexpectedTerminal(failure, diagnostic);
        else throw new Error(`Unknown ${subject} failure kind.`);
        break;
      }
      case "Canceled":
        sink.reportTerminal({
          kind: "canceled",
          reason: cancellationReason(reason),
        });
        break;
      default:
        throw new Error(`Unknown ${subject} result kind.`);
    }
  } catch (failure: unknown) {
    sink.reportUnexpectedTerminal(failure, failure);
  }
  return undefined;
}
