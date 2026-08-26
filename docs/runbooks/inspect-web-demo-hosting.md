# Hosting an inspect-web demo

This runbook describes how to publish a worktree's inspect-web site so a user
on another network-connected machine can validate in-progress work.

Use one of two private hosting patterns:

1. **Viewer-side SSH forwarding (preferred):** the viewer forwards a local
   loopback port over SSH to the build host's loopback static server.
2. **Tailscale Serve:** a stable gateway receives the static server through an
   SSH reverse tunnel and exposes it to authenticated tailnet clients over
   HTTPS.

Both patterns keep the static server bound to loopback. The preferred pattern
opens no application port on either network; it reuses the build host's
existing SSH access. Use Tailscale Serve when a stable shared URL is more
important than the additional gateway and tailnet dependencies.

This is a review demo, not a production deployment. The production site follows
the release workflow instead.

## Browser security and network scope

The .NET browser runtime requires secure-context browser APIs. Browsers treat
HTTP loopback origins such as `http://127.0.0.1` as potentially trustworthy,
but do not extend that exception to an ordinary LAN address. Proxying a
loopback server to `http://<lan-address>` therefore does not solve the
secure-context requirement: the browser judges the URL it loaded, not the
proxy's upstream.

The two supported patterns satisfy that requirement differently:

- A viewer-side SSH forward gives the browser a loopback URL. SSH protects the
  connection to the build host, so no browser certificate is needed.
- Tailscale Serve obtains and renews the certificate for the gateway's tailnet
  DNS name, terminates HTTPS, and proxies to a gateway loopback origin.

**Tailscale Serve is private to the tailnet.** The viewer must be authenticated
to the tailnet, and tailnet policy must permit access. **Tailscale Funnel is
public Internet exposure and must not be used for review demos.** Do not run
`tailscale funnel`, enable Funnel for a Serve route, or substitute another
public ingress without explicit user approval.

Serve is the repository-standard shared HTTPS terminator, not the only way to
obtain HTTPS. A gateway operator could instead use `tailscale cert` with a
custom TLS server, or a LAN operator could provide another certificate trusted
by the viewer. Those alternatives add private-key handling, certificate
renewal, and server configuration that Serve owns in this workflow.

## Common prerequisites

The build host needs:

- the exact branch in its own clean development worktree;
- the repository-selected .NET SDK and WebAssembly workload;
- Node.js at the version required by `prototypes/inspect-web/package.json`;
- SSH access from the viewer or gateway, depending on the selected pattern.

Use these build-host variables:

```bash
export DEMO_BUILD_ORIGIN_PORT=5198
```

Choose a different port only when `5198` is already owned. Inspect the current
listener instead of killing an unknown process.

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
dotnet_loader="$(
  grep -oE '_framework/dotnet\.[a-z0-9]+\.js' "$site_root/index.html" |
    head -n 1
)"

test -f "$site_root/index.html"
test -f "$site_root/manifest.json"
test -f "$site_root/inspect-web-engine.js"
test -n "$dotnet_loader"
test -f "$site_root/$dotnet_loader"
```

Publish before starting the static server. For a later update, stop the server,
publish the replacement completely, and restart it; do not serve a directory
while `dotnet publish` is rewriting it.

Do not use:

- the Vite development server - it does not serve the published .NET engine;
- the default `npm run preview` command - it targets the frontend `dist/`
  directory rather than the complete published WebAssembly site;
- `dotnet run` as the remote proof - the WebAssembly development host has
  previously served fingerprinted assets incorrectly in this workflow; or
- the frontend `dist/` directory - it does not contain the complete published
  WebAssembly site.

## 3. Serve the published site on build-host loopback

Keep this process in a dedicated persistent terminal or tmux window:

```bash
cd "$repo_root/prototypes/inspect-web"

if [ -n "${DEMO_PUBLIC_HOST:-}" ]; then
  export __VITE_ADDITIONAL_SERVER_ALLOWED_HOSTS="$DEMO_PUBLIC_HOST"
fi

npm run preview -- \
  --host 127.0.0.1 \
  --port "$DEMO_BUILD_ORIGIN_PORT" \
  --strictPort \
  --outDir "$site_root"
```

This uses the already-pinned Vite dependency as a static server while
overriding its default output directory with the published `wwwroot`.
`--strictPort` keeps an occupied port from silently selecting a different one.
For the Serve pattern, allow only the exact tailnet DNS hostname; do not disable
Vite's host check globally.

Record the window or exact PID. Do not use a name-based process kill during
cleanup.

From another terminal on the build host:

```bash
curl -fsSI "http://127.0.0.1:$DEMO_BUILD_ORIGIN_PORT/"
curl -fsSI "http://127.0.0.1:$DEMO_BUILD_ORIGIN_PORT/inspect-web-engine.js"
curl -fsSI "http://127.0.0.1:$DEMO_BUILD_ORIGIN_PORT/$dotnet_loader"
```

The simple static host supports the root and query-string workspace links used
for demos. It does not implement Azure Static Web Apps route fallback; do not
claim direct-path routes such as `/credits` as validated through this host.

## Pattern A: viewer-side SSH forwarding

Use this pattern by default. It requires the viewer to have SSH access to the
build host but requires no gateway, tailnet, certificate, or network-facing
application listener.

On the viewer machine, choose an unused loopback port and start the forward:

```bash
export DEMO_BUILD_HOST="<build-host SSH name>"
export DEMO_BUILD_ORIGIN_PORT=5198
export DEMO_VIEWER_PORT=5198

ssh -N -T \
  -o ExitOnForwardFailure=yes \
  -o ServerAliveInterval=30 \
  -o ServerAliveCountMax=3 \
  -L "127.0.0.1:${DEMO_VIEWER_PORT}:127.0.0.1:${DEMO_BUILD_ORIGIN_PORT}" \
  "$DEMO_BUILD_HOST"
```

`ExitOnForwardFailure` is required. Without it, SSH can remain alive after
failing to claim the viewer port, leaving a success-shaped process that serves
nothing.

Keep the tunnel in a dedicated terminal. From another viewer terminal, verify
the complete path:

```bash
curl -fsSI "http://127.0.0.1:$DEMO_VIEWER_PORT/"
dotnet_loader="$(
  curl -fsS "http://127.0.0.1:$DEMO_VIEWER_PORT/" |
    grep -oE '_framework/dotnet\.[a-z0-9]+\.js' |
    head -n 1
)"
curl -fsSI "http://127.0.0.1:$DEMO_VIEWER_PORT/$dotnet_loader"
```

The browser URL is:

```text
http://127.0.0.1:<DEMO_VIEWER_PORT>/?package=...&w=...&build=<short-head>
```

The loopback URL belongs to the viewer machine. Do not replace it with the
build host's LAN address or tailnet name; doing so loses the browser's loopback
secure-context treatment.

## Pattern B: Tailscale Serve through a stable gateway

Use this pattern when the user has indicated that a Tailscale Serve session is
available. It has three hops:

```text
viewer browser
  -> HTTPS over the private tailnet
  -> Tailscale Serve on the gateway
  -> gateway loopback port
  -> SSH reverse tunnel
  -> build-host loopback static server
```

One Serve session can contain multiple path mappings and can be reconfigured
after it starts. This runbook deliberately treats the user-provided mapping as
fixed: one tailnet HTTPS URL and path proxies to one assigned gateway loopback
port. Keep that mapping configured between web-development demos, but attach
the reverse tunnel and origin only while a demo is active.

The agent does not inspect or change the Serve session and needs no Tailscale
operator access. Never reset, replace, or add mappings to the session. When the
user has not indicated that a session is available, use viewer-side SSH
forwarding instead.

On the build host, set the environment-specific gateway values:

```bash
export DEMO_GATEWAY_HOST="<gateway SSH host>"
export DEMO_PUBLIC_HOST="<gateway tailnet DNS name>"
export DEMO_HTTPS_PORT="<assigned HTTPS port>"
export DEMO_GATEWAY_ORIGIN_PORT="<assigned gateway loopback port>"
export DEMO_BUILD_ORIGIN_PORT=5198
```

Use the values supplied with the available session. Do not discover alternative
gateways or routes, and do not commit private network configuration to the
repository.

Before attaching the tunnel, confirm that no process already owns the assigned
gateway loopback port:

```bash
ssh -o BatchMode=yes "$DEMO_GATEWAY_HOST" \
  "ss -ltn | grep ':${DEMO_GATEWAY_ORIGIN_PORT} ' || true"
```

An occupied gateway origin may belong to another demo. Coordinate its teardown;
do not kill an unknown process or run `tailscale serve reset`.

Keep the reverse tunnel in a dedicated persistent terminal or tmux window:

```bash
ssh -N -T \
  -o BatchMode=yes \
  -o ExitOnForwardFailure=yes \
  -o ServerAliveInterval=30 \
  -o ServerAliveCountMax=3 \
  -R "127.0.0.1:${DEMO_GATEWAY_ORIGIN_PORT}:127.0.0.1:${DEMO_BUILD_ORIGIN_PORT}" \
  "$DEMO_GATEWAY_HOST"
```

Verify the forwarded origin from the gateway:

```bash
ssh -o BatchMode=yes "$DEMO_GATEWAY_HOST" \
  "curl -fsSI http://127.0.0.1:${DEMO_GATEWAY_ORIGIN_PORT}/"
```

Confirm the existing HTTPS mapping from the gateway without changing it:

```bash
ssh -o BatchMode=yes "$DEMO_GATEWAY_HOST" \
  "curl -fsSI https://${DEMO_PUBLIC_HOST}:${DEMO_HTTPS_PORT}/"
```

The build host may be unable to reach the gateway's tailnet listener because of
tailnet policy even while the gateway and viewer can. A timeout from the build
host does not justify falling back to an insecure network URL. Check each hop
separately and obtain the viewer-browser proof below.

The browser URL is:

```text
https://<DEMO_PUBLIC_HOST>:<DEMO_HTTPS_PORT>/?package=...&w=...&build=<short-head>
```

## Give the user an identifiable URL

Start with the application-generated workspace link for the selected pattern,
then append the candidate head as a cache-busting `build` parameter.

Use `?build=` for a bare root URL and `&build=` when the URL already has a
query. The build value identifies the intended candidate and forces a distinct
document URL; it is not a substitute for confirming the running artifact.

State all three:

- the complete clickable URL;
- the exact full Git head; and
- what interaction or output the user should validate.

## Validate from the viewer's browser

An origin returning HTTP 200 is necessary but insufficient. The demo is ready
only after the viewer's browser proves that:

1. The page loads without a certificate or secure-context error.
2. The .NET engine reaches its ready state.
3. The exact changed scenario works through real pointer and keyboard input.
4. A hard refresh of the complete workspace URL restores the same view.
5. Browser back and forward work when navigation is part of the change.
6. The browser console has no startup or module-loading error.

Prefer the user's machine for final acceptance. An automated browser exercising
the same URL shape is useful preflight evidence, but it does not replace the
user's validation when the purpose of the demo is UX feedback.

## Updating and teardown

For a new head:

1. Stop the build-host static server.
2. Build and publish the new head completely.
3. Restart the static server against the new `wwwroot`.
4. Re-establish the selected tunnel.
5. Re-run the origin, end-to-end HTTP or HTTPS, and browser checks.
6. Send a new URL with the new short head.

For viewer-side SSH forwarding:

1. Stop the viewer-side tunnel by its terminal or exact PID.
2. Stop the build-host static server by its terminal or exact PID.

For Tailscale Serve:

1. Stop the reverse tunnel by its terminal or exact PID.
2. Stop the build-host static server by its terminal or exact PID.
3. Verify the gateway origin no longer answers.
4. Leave the shared Serve mapping in place unless its operator explicitly asks
   for removal.

Never leave a URL advertised after its tunnel or exact artifact is gone. Never
silently repoint an existing build-tagged URL to unrelated work.

## Failure guide

| Symptom | Likely boundary | Response |
| --- | --- | --- |
| Build-host origin fails | Publish or static server | Check `site_root`, process, and the required published files |
| SSH exits immediately | Port ownership or authentication | Keep `ExitOnForwardFailure`; inspect the named port and SSH access |
| Viewer HTTP works but browser reports insecure context | Wrong URL | Use viewer loopback, not the build host's LAN or tailnet address |
| Gateway origin fails | Reverse tunnel | Check the tunnel terminal and both loopback ports |
| Serve returns 502/503 | Serve cannot reach its origin | Restore the reverse tunnel; do not replace the Serve route |
| Serve times out only on the build host | Tailnet policy | Validate on the gateway and an authorized viewer |
| Vite reports a disallowed host | Missing exact Serve hostname | Restart with `__VITE_ADDITIONAL_SERVER_ALLOWED_HOSTS` set to the tailnet DNS name |
| Fingerprinted loader or bundled assets are empty or missing | Wrong artifact or host | Re-publish and serve the complete `wwwroot` |
| Page paints but controls remain inert | Wasm engine did not initialize | Inspect framework requests and browser console; HTTP 200 alone is not success |
| User sees an older build | Stale URL or wrong served worktree | Confirm full head, restart from its `wwwroot`, and issue a new `build=<short-head>` URL |
