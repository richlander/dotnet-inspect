import { assertNever } from "./data.ts";

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

export interface PublicationCoordinate {
  readonly id: string;
  readonly version: string;
}

export type PublicationDate =
  | { readonly status: "loading" }
  | { readonly status: "available"; readonly date: string }
  | { readonly status: "unavailable"; readonly reason: string };

// Preserve the feed's calendar day, independent of the viewer's timezone.
// NuGet's year-1900 unlisted sentinel is not a publication date.
export function publicationCalendarDate(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const match = /^(\d{4}-\d{2}-\d{2})T/.exec(value);
  if (!match?.[1] || !Number.isFinite(Date.parse(value))) return null;
  const date = match[1];
  if (date.startsWith("1900-")) return null;
  if (new Date(`${date}T00:00:00Z`).toISOString().slice(0, 10) !== date)
    return null;
  return date;
}

export function publicationDateText(value: PublicationDate): string {
  switch (value.status) {
    case "available": return `Published ${value.date}`;
    case "loading": return "Published …";
    case "unavailable": return "Published unavailable";
    default: return assertNever(value, "Publication date");
  }
}

export function platformPublicationCoordinate(
  pack: string, version: string | undefined,
): PublicationCoordinate | null {
  if (!version) return null;
  switch (pack) {
    case "netcore.app": return { id: "Microsoft.NETCore.App.Ref", version };
    case "aspnetcore.app": return { id: "Microsoft.AspNetCore.App.Ref", version };
    // The selected .NET patch does not identify NETStandard.Library.Ref's version.
    default: return null;
  }
}

// Decorate existing nuget.org coordinates, without delaying search/activation
// or fetching package payloads. Keep at most four metadata requests active.
export function createPackagePublicationDates(
  fetchMetadata: typeof fetch,
  changed: (coordinate: PublicationCoordinate) => void,
) {
  const dates = new Map<string, PublicationDate>();
  let registrationBase: Promise<string> | null = null;
  let active = 0;
  const queue: Array<() => void> = [];
  const key = (coordinate: PublicationCoordinate) =>
    `${coordinate.id.toLowerCase()}@${coordinate.version.toLowerCase()}`;

  async function json(url: string): Promise<unknown> {
    const response = await fetchMetadata(url, {
      signal: AbortSignal.timeout(8000), credentials: "omit",
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    return response.json();
  }

  async function base(): Promise<string> {
    registrationBase ??= (async () => {
      const index = await json("https://api.nuget.org/v3/index.json");
      const resource: unknown = isRecord(index) && Array.isArray(index.resources)
        ? index.resources.find((item: unknown) => isRecord(item)
          && item["@type"] === "RegistrationsBaseUrl/3.6.0")
        : undefined;
      if (!isRecord(resource) || typeof resource["@id"] !== "string")
        throw new Error("NuGet registration metadata is unavailable.");
      const url = new URL(resource["@id"]);
      if (url.protocol !== "https:" || url.hostname !== "api.nuget.org"
        || url.port || url.username || url.password || url.search || url.hash)
        throw new Error("NuGet returned an unsupported registration endpoint.");
      return url.href.replace(/\/?$/, "/");
    })();
    return registrationBase;
  }

  async function load(coordinate: PublicationCoordinate) {
    try {
      const url = `${await base()}${encodeURIComponent(coordinate.id.toLowerCase())}/${encodeURIComponent(coordinate.version.toLowerCase())}.json`;
      const leaf = await json(url);
      if (!isRecord(leaf) || leaf["@id"] !== url)
        throw new Error("NuGet returned metadata for an unexpected coordinate.");
      const date = publicationCalendarDate(leaf.published);
      dates.set(key(coordinate), date
        ? { status: "available", date }
        : { status: "unavailable", reason: "NuGet does not expose a publication date for this version." });
    } catch (error) {
      dates.set(key(coordinate), {
        status: "unavailable",
        reason: error instanceof Error ? error.message : String(error),
      });
    } finally {
      active--;
      queue.shift()?.();
      changed(coordinate);
    }
  }

  return {
    get(coordinate: PublicationCoordinate): PublicationDate {
      const identity = key(coordinate);
      const existing = dates.get(identity);
      if (existing) return existing;
      // Bound retained observations, preserving all in-flight request identities.
      if (dates.size >= 256) {
        const settled = [...dates].find(([, value]) => value.status !== "loading");
        if (settled) dates.delete(settled[0]);
        else return { status: "unavailable", reason: "Publication metadata request limit reached." };
      }
      const pending = { status: "loading" } as const;
      dates.set(identity, pending);
      const start = () => { active++; void load(coordinate); };
      if (active < 4) start();
      else queue.push(start);
      return pending;
    },
  };
}
