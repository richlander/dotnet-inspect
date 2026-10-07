import assert from "node:assert/strict";
import test from "node:test";
import {
  createPackagePublicationDates,
  platformPublicationCoordinate,
  publicationCalendarDate,
} from "../src/package-publication.ts";

const base = "https://api.nuget.org/v3/registration5-gz-semver2/";
const index = { resources: [{ "@type": "RegistrationsBaseUrl/3.6.0", "@id": base }] };
const json = (body: unknown) => new Response(JSON.stringify(body));
const requestUrl = (input: RequestInfo | URL) => typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
const tick = () => new Promise<void>(resolve => setImmediate(resolve));

test("publication dates preserve the feed day and reject missing, invalid and unlisted dates", () => {
  assert.equal(publicationCalendarDate("2024-11-12T23:45:00-08:00"), "2024-11-12");
  assert.equal(publicationCalendarDate("2024-11-12T00:15:00+14:00"), "2024-11-12");
  assert.equal(publicationCalendarDate("2024-02-29T12:00:00Z"), "2024-02-29");
  for (const value of [null, undefined, 4, "", "2024-02-30T12:00:00Z", "1900-01-01T00:00:00Z", "invalid", "2024-11-12"])
    assert.equal(publicationCalendarDate(value), null);
});

test("platform dates use the containing reference pack, not an assembly name or runtime patch for netstandard", () => {
  assert.deepEqual(platformPublicationCoordinate("netcore.app", "10.0.12", true), { id: "Microsoft.NETCore.App.Ref", version: "10.0.12" });
  assert.deepEqual(platformPublicationCoordinate("aspnetcore.app", "10.0.12", true), { id: "Microsoft.AspNetCore.App.Ref", version: "10.0.12" });
  assert.equal(platformPublicationCoordinate("netstandard", "10.0.12", true), null);
  assert.equal(platformPublicationCoordinate("other", "10.0.12", true), null);
  assert.equal(platformPublicationCoordinate("netcore.app", "10.0.12", false), null);
  assert.equal(platformPublicationCoordinate("netcore.app", undefined, true), null);
});

test("dates return pending immediately, deduplicate coordinates, and isolate exact versions", async () => {
  const requests: string[] = [];
  const changes: string[] = [];
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const dates = createPackagePublicationDates(async input => {
    const url = requestUrl(input); requests.push(url);
    if (url.endsWith("/index.json")) return json(index);
    await gate;
    return json({ "@id": url, published: url.includes("/1.0.0.json") ? "2020-01-02T23:00:00Z" : "2024-03-04T01:00:00Z" });
  }, coordinate => { changes.push(coordinate.version); });
  const first = { id: "Example", version: "1.0.0" };
  const second = { id: "Example", version: "2.0.0" };
  assert.equal(dates.get(first).status, "loading");
  assert.equal(dates.get({ id: "EXAMPLE", version: "1.0.0" }), dates.get(first));
  dates.get(second);
  await tick();
  assert.equal(requests.length, 3);
  assert.equal(changes.length, 0);
  release(); await tick();
  assert.deepEqual(dates.get(first), { status: "available", date: "2020-01-02" });
  assert.deepEqual(dates.get(second), { status: "available", date: "2024-03-04" });
  assert.deepEqual(changes.sort((a, b) => a.localeCompare(b)), ["1.0.0", "2.0.0"]);
  assert.equal(requests.length, 3);
});

test("metadata failures and unlisted sentinels are visible unavailable outcomes", async () => {
  for (const outcome of ["http", "wrong-coordinate", "sentinel", "missing"]) {
    const dates = createPackagePublicationDates(async input => {
      const url = requestUrl(input);
      if (url.endsWith("/index.json")) return json(index);
      if (outcome === "http") return new Response(null, { status: 503 });
      return json({ "@id": outcome === "wrong-coordinate" ? `${base}other/1.0.0.json` : url,
        published: outcome === "sentinel" ? "1900-01-01T00:00:00Z" : undefined });
    }, () => {});
    const coordinate = { id: "Example", version: "1.0.0" };
    dates.get(coordinate); await tick();
    const result = dates.get(coordinate);
    assert.equal(result.status, "unavailable");
    if (result.status === "unavailable") assert.ok(result.reason);
  }
});

test("registration discovery refuses credentials and other origins", async () => {
  const requests: string[] = [];
  const dates = createPackagePublicationDates(async input => {
    requests.push(requestUrl(input));
    return json({ resources: [{ "@type": "RegistrationsBaseUrl/3.6.0", "@id": "https://other.example/registration/" }] });
  }, () => {});
  const coordinate = { id: "Example", version: "1.0.0" };
  dates.get(coordinate); await tick();
  assert.equal(dates.get(coordinate).status, "unavailable");
  assert.equal(requests.length, 1);
});

test("leaf requests have bounded concurrency and share service discovery", async () => {
  let active = 0; let maximum = 0; let indexes = 0;
  const releases: Array<() => void> = [];
  const dates = createPackagePublicationDates(async input => {
    const url = requestUrl(input);
    if (url.endsWith("/index.json")) { indexes++; return json(index); }
    active++; maximum = Math.max(maximum, active);
    await new Promise<void>(resolve => { releases.push(resolve); });
    active--;
    return json({ "@id": url, published: "2024-01-02T00:00:00Z" });
  }, () => {});
  const coordinates = Array.from({ length: 10 }, (_, i) => ({ id: `Example${i}`, version: "1.0.0" }));
  coordinates.forEach(coordinate => dates.get(coordinate));
  await tick(); assert.equal(active, 4);
  while (releases.length) { releases.splice(0).forEach(release => release()); await tick(); }
  assert.equal(maximum, 4); assert.equal(indexes, 1);
  assert.ok(coordinates.every(coordinate => dates.get(coordinate).status === "available"));
});
