# Hosting an inspect-web demo

This runbook describes how to publish a worktree's inspect-web site so a user
on another tailnet-connected machine can validate in-progress work.

The repository-standard demo topology has two roles:

1. The **build host** publishes the exact worktree and serves the resulting
   static site on loopback.
2. A stable **HTTPS gateway** receives that loopback service through an SSH
   reverse tunnel and exposes it with Tailscale Serve.

```text
user browser
  -> HTTPS over the tailnet
  -> Tailscale Serve on the gateway
  -> gateway loopback port
  -> SSH reverse tunnel
  -> build-host loopback static server
  -> published wwwroot for one exact Git head
```

This is a review demo, not a production deployment. The production site follows
the release workflow instead.

## Why this topology

- Remote .NET WebAssembly requires HTTPS because its loader uses secure-context
  browser APIs.
- The published `wwwroot` is the artifact users will eventually receive. Vite
  and `dotnet run` are development hosts and are not substitutes for it.
- Both origin ports bind to loopback. The tailnet gateway is the only
  network-facing listener.
- The gateway URL remains stable while the implementation worktree and build
  host can change.
- Tailscale Serve is tailnet-only. Do not use Tailscale Funnel for a review
  demo.

The gateway route is shared infrastructure. It can present only one demo on a
given origin port at a time. Inspect its current owner before taking it over,
and never reset or replace an unknown route.

## Prerequisites

The build host needs:

- the exact branch in its own clean development worktree;
- the repository-selected .NET SDK and WebAssembly workload;
- Node.js at the version required by `prototypes/inspect-web/package.json`;
- Python 3 for the static server;
- SSH access to the gateway; and
- Tailscale connectivity.

The gateway needs Tailscale Serve configured once by its operator. The examples
below use these variables so hostnames remain environment configuration rather
than repository policy:

```bash
export DEMO_GATEWAY_HOST="<gateway SSH host>"
export DEMO_PUBLIC_HOST="<gateway tailnet DNS name>"
export DEMO_HTTPS_PORT=8443
export DEMO_GATEWAY_ORIGIN_PORT=5199
export DEMO_BUILD_ORIGIN_PORT=5198
```

Use the gateway values supplied for the current environment. Do not infer a
gateway from an arbitrary machine name or commit private network configuration
to the repository.

## 1. Establish the exact candidate

Run from the implementation worktree:

```bash
repo_root="$(git rev-parse --show-toplevel)"
head="$(git rev-parse HEAD)"
short_head="$(git rev-parse --short=12 HEAD)"
git status --short --branch
```

Do not demo an uncommitted tree unless the user explicitly asks to inspect
uncommitted work. In that exceptional case, say so beside the URL; a Git SHA
cannot identify the content they are seeing.

Before replacing the shared demo, inspect the gateway:

```bash
ssh -o BatchMode=yes "$DEMO_GATEWAY_HOST" \
  "tailscale serve status; ss -ltn | grep ':${DEMO_GATEWAY_ORIGIN_PORT} ' || true"
```

An occupied gateway origin may belong to another demo. Coordinate its teardown;
do not kill an unknown process or run `tailscale serve reset`.

## 2. Build the production artifact

Build the frontend and publish the WebAssembly engine from the same worktree:

```bash
cd "$repo_root/prototypes/inspect-web"
npm ci
npm run build

cd "$repo_root"
dotnet publish prototypes/inspect-web/engine/InspectWeb.Engine.csproj \
  -c Release \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -m:1 \
  -nr:false
```

Derive the target framework from the project rather than embedding today's
framework in scripts:

```bash
target_framework="$(
  dotnet msbuild prototypes/inspect-web/engine/InspectWeb.Engine.csproj \
    -nologo -getProperty:TargetFramework
)"
site_root="$repo_root/prototypes/inspect-web/engine/bin/Release/$target_framework/publish/wwwroot"

test -f "$site_root/index.html"
test -f "$site_root/manifest.json"
test -f "$site_root/inspect-web-engine.js"
test -f "$site_root/_framework/dotnet.js"
```

Publish before starting the static server. For a later update, stop the server,
publish the replacement completely, and restart it; do not serve a directory
while `dotnet publish` is rewriting it.

Do not use:

- `vite` or `vite preview` — the demo must include the published .NET engine,
  and Vite also applies host allow-list behavior;
- `dotnet run` as the remote proof — the WebAssembly development host has
  previously served fingerprinted assets incorrectly in this workflow; or
- the frontend `dist/` directory — it does not contain the complete published
  WebAssembly site.

## 3. Serve the published site on loopback

Keep this process in a dedicated persistent terminal or tmux window:

```bash
python3 -m http.server "$DEMO_BUILD_ORIGIN_PORT" \
  --bind 127.0.0.1 \
  --directory "$site_root"
```

Record the window or exact PID. Do not use a name-based process kill during
cleanup.

From another terminal on the build host:

```bash
curl -fsSI "http://127.0.0.1:$DEMO_BUILD_ORIGIN_PORT/"
curl -fsSI "http://127.0.0.1:$DEMO_BUILD_ORIGIN_PORT/inspect-web-engine.js"
curl -fsSI "http://127.0.0.1:$DEMO_BUILD_ORIGIN_PORT/_framework/dotnet.js"
```

The simple static host supports the root and query-string workspace links used
for demos. It does not implement Azure Static Web Apps route fallback; do not
claim direct-path routes such as `/credits` as validated through this host.

## 4. Open the reverse tunnel

Keep the tunnel in a second dedicated persistent terminal or tmux window:

```bash
ssh -N -T \
  -o BatchMode=yes \
  -o ExitOnForwardFailure=yes \
  -o ServerAliveInterval=30 \
  -o ServerAliveCountMax=3 \
  -R "127.0.0.1:${DEMO_GATEWAY_ORIGIN_PORT}:127.0.0.1:${DEMO_BUILD_ORIGIN_PORT}" \
  "$DEMO_GATEWAY_HOST"
```

`ExitOnForwardFailure` is required. Without it, SSH can remain alive after
failing to claim the gateway port, leaving a success-shaped tunnel process that
serves nothing.

Verify the origin from the gateway:

```bash
ssh -o BatchMode=yes "$DEMO_GATEWAY_HOST" \
  "curl -fsSI http://127.0.0.1:${DEMO_GATEWAY_ORIGIN_PORT}/"
```

## 5. Confirm the HTTPS route

The gateway operator configures the route once:

```bash
tailscale serve --bg \
  --https="$DEMO_HTTPS_PORT" \
  "http://127.0.0.1:$DEMO_GATEWAY_ORIGIN_PORT"
```

Normal demo publication must reuse the existing matching route, not rewrite it.
Confirm it from the gateway:

```bash
ssh -o BatchMode=yes "$DEMO_GATEWAY_HOST" \
  "tailscale serve status"

ssh -o BatchMode=yes "$DEMO_GATEWAY_HOST" \
  "curl -fsSI https://${DEMO_PUBLIC_HOST}:${DEMO_HTTPS_PORT}/"
```

The build host may be unable to reach the gateway's public tailnet listener
because of ACL or routing policy even while the gateway and user can. A timeout
from the build host does not justify falling back to insecure HTTP. Check each
hop separately and obtain the external browser proof below.

## 6. Give the user an identifiable URL

Start with the application-generated workspace link, then append the candidate
head as a cache-busting build parameter:

```text
https://<DEMO_PUBLIC_HOST>:<DEMO_HTTPS_PORT>/?package=...&w=...&build=<short-head>
```

Use `?build=` for a bare root URL and `&build=` when the URL already has a
query. The build value identifies the intended candidate and forces a distinct
document URL; it is not a substitute for confirming the running artifact.

State all three:

- the complete clickable URL;
- the exact full Git head; and
- what interaction or output the user should validate.

## 7. Validate from a user-equivalent browser

An origin returning HTTP 200 is necessary but insufficient. The demo is ready
only after a separate tailnet-connected browser proves that:

1. HTTPS loads without a certificate or secure-context error.
2. The .NET engine reaches its ready state.
3. The exact changed scenario works through real pointer and keyboard input.
4. A hard refresh of the complete workspace URL restores the same view.
5. Browser back and forward work when navigation is part of the change.
6. The browser console has no startup or module-loading error.

Prefer the user's machine for final acceptance. An automated browser on another
tailnet peer is useful preflight evidence, but it does not replace the user's
validation when the purpose of the demo is UX feedback.

## Updating and teardown

For a new head:

1. Stop the build-host static server.
2. Build and publish the new head completely.
3. Restart the static server against the new `wwwroot`.
4. Keep or re-establish the reverse tunnel.
5. Re-run the origin, gateway, HTTPS, and browser checks.
6. Send a new URL with the new short head.

When the demo is no longer needed:

1. Stop the reverse tunnel by its terminal or exact PID.
2. Stop the build-host static server by its terminal or exact PID.
3. Verify the gateway origin no longer answers.
4. Leave the shared Tailscale Serve mapping in place unless its operator
   explicitly asks for removal.

Never leave a URL advertised after its tunnel or exact artifact is gone. Never
silently repoint an existing build-tagged URL to unrelated work.

## Failure guide

| Symptom | Likely boundary | Response |
| --- | --- | --- |
| Local origin fails | Publish or static server | Check `site_root`, process, and the four required files |
| SSH exits immediately | Gateway port or authentication | Keep `ExitOnForwardFailure`; inspect ownership instead of choosing random processes to kill |
| Gateway origin fails | Reverse tunnel | Check the tunnel terminal and both loopback ports |
| HTTPS returns 502/503 | Serve mapping reaches no origin | Restore the tunnel; do not replace the HTTPS route |
| HTTPS times out only on build host | Tailnet ACL/routing | Validate on the gateway and a user-equivalent peer |
| Vite reports a disallowed host | Wrong server | Serve the published `wwwroot`, not Vite |
| `dotnet.js` or bundled assets are empty/missing | Wrong artifact or host | Re-publish and use the static `wwwroot` host |
| Page paints but controls remain inert | Wasm engine did not initialize | Inspect framework requests and browser console; HTTP 200 alone is not success |
| User sees an older build | Stale URL or wrong served worktree | Confirm full head, restart from its `wwwroot`, and issue a new `build=<short-head>` URL |
