#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ] || [ ! -x "$1" ]; then
    echo "Usage: $0 <linux-native-executable>" >&2
    exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
binary=$(realpath "$1")

case "$(uname -m)" in
    x86_64) image_arch=amd64 ;;
    aarch64) image_arch=arm64v8 ;;
    *) echo "Unsupported ABI test host architecture: $(uname -m)" >&2; exit 2 ;;
esac

# Run on the agent host, after pack's build container has exited. The canary
# contains only its own Ubuntu 18.04 runtime libraries, never a mounted host
# SDK, sysroot, Docker socket, cache, or writable source checkout.
docker run --rm --pull=always --network none --read-only \
    --cap-drop ALL --security-opt no-new-privileges \
    --tmpfs /tmp:rw,mode=1777 \
    --user "$(id -u):$(id -g)" \
    --mount "type=bind,src=$binary,dst=/opt/dotnet-inspect,readonly" \
    --mount "type=bind,src=$repo_root/eng/assert-linux-aot-runtime.sh,dst=/opt/assert-runtime.sh,readonly" \
    -e HOME=/tmp/abi-home \
    "mcr.microsoft.com/dotnet-buildtools/prereqs:ubuntu-18.04-net11.0-abi-test-$image_arch" \
    sh /opt/assert-runtime.sh
