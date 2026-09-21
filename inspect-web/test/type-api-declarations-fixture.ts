import type {
  ApiDeclarations,
  TypeApiDeclarationScope,
} from "../src/facades/inspect-web-source.d.ts";
import { inertStringFixture } from "./inert-string-fixture.ts";

export function apiDeclarationsFixture(
  scope: TypeApiDeclarationScope = "ApiVisible",
  unavailable = false,
): ApiDeclarations {
  return {
    kind: "apiDeclarations",
    inspection: {
      content: {
        outcome: unavailable ? "Unavailable" : "Available",
        typeIdentity: { namespace: "System.Text.Json", segments: ["JsonNamingPolicy"] },
        scope,
        text: unavailable ? null
          : "public abstract class JsonNamingPolicy\n{\n"
            + "    protected JsonNamingPolicy();\n"
            + "    public abstract string ConvertName(string name);\n"
            + (scope === "All" ? "    static JsonNamingPolicy();\n" : "")
            + "}",
        failures: unavailable ? [{
          kind: "ProjectionTruncated",
          detail: "The <metadata> bound was exceeded.",
          operation: null,
          subjectToken: null,
        }] : [],
      },
      share: {
        kind: "nonProjectable",
        fullUrl: null,
        packet: null,
        path: "type-api-declarations/share",
        reason: inertStringFixture("No portable Workspace Share representation."),
      },
      diagnostics: unavailable ? [{
        code: "type-api-declaration.projection-truncated",
        severity: 2,
        summary: inertStringFixture("The <metadata> bound was exceeded."),
        correspondence: null,
      }] : [],
    },
  };
}
