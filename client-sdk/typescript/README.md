# @jonpepler/kerbcam

TypeScript bindings for the kerbcam sidecar's WebRTC control
protocol. Used by browsers and other clients that consume the
sidecar's H.264 streams.

The Rust types in [`sidecar/src/protocol/`](https://github.com/jonpepler/kerbcam/tree/main/sidecar/src/protocol)
are the source of truth. [typeshare](https://github.com/1Password/typeshare)
generates `src/index.ts` from them, and CI fails publishes if the
generated file is out of sync. Don't hand-edit it.

The Rust crate and this package ship the same SemVer version,
bumped together by `./scripts/bump-version.sh`.

## Install

Hosted on GitHub Packages. In `.npmrc`:

```ini
@jonpepler:registry=https://npm.pkg.github.com
//npm.pkg.github.com/:_authToken=${GITHUB_TOKEN}
```

`GITHUB_TOKEN` needs `read:packages`. CI's auto-injected
`secrets.GITHUB_TOKEN` already has it.

```sh
pnpm add @jonpepler/kerbcam
```

## Use

The protocol is JSON-per-message over an `RTCDataChannel` named
`kerbcam-control`. The browser opens the channel after the SDP
exchange, then exchanges tagged-union messages with the sidecar.

```ts
import type { ClientMessage, ServerMessage, CameraState } from "@jonpepler/kerbcam";
import { Layer } from "@jonpepler/kerbcam";

// `controlChannel` is the RTCDataChannel returned by
// RTCPeerConnection.createDataChannel("kerbcam-control") on the
// client side, opened after the SDP offer/answer exchange.
declare const controlChannel: RTCDataChannel;

// Client to server
const setLayers: ClientMessage = {
  type: "set-layers",
  content: { flightId: 2592004302, layers: [Layer.Near, Layer.Scaled] },
};
controlChannel.send(JSON.stringify(setLayers));

// Unit variants carry no `content`
controlChannel.send(JSON.stringify({ type: "hello" } satisfies ClientMessage));

// Server to client
controlChannel.onmessage = (ev) => {
  const msg: ServerMessage = JSON.parse(ev.data);
  switch (msg.type) {
    case "camera-snapshot":
      msg.content.cameras.forEach((c: CameraState) => renderCamera(c));
      break;
    case "adaptive-shed":
      console.log(`shed level=${msg.content.level} (KSP ${msg.content.kspFps} fps)`);
      break;
    // ...
  }
};
```

## Per-camera capabilities

`CameraState` includes capability flags so consumers render only
the controls each part actually supports.

- `supportsZoom`, `fov`, `fovMin`, `fovMax`. 19 of 21 stock Hullcam
  VDS parts support runtime FoV via `MuMechModuleHullCameraZoom`.
- `supportsPan`, `panYawMin/Max`, `panPitchMin/Max`. Reserved for the
  planned mod extension that adds steerable mounts. Always `false`
  on shipping parts. Hide pan UI until it flips.
- `encoderBitrateBps`, `targetBitrateBps`. Current encoder bitrate
  and the REMB-driven target. They diverge briefly when receivers'
  bandwidth estimates move.

## Multi-language

`typeshare` also generates Kotlin, Swift, Scala, and Go. Those
land alongside `client-sdk/typescript/` as consumers appear. A C#
binding is pending an upstream `typeshare` C# generator or a
hand-written shim.

## License

[CC BY-NC-SA 4.0](https://github.com/jonpepler/kerbcam/blob/main/LICENSE).

## Versioning

[SemVer](https://semver.org/) against the wire format. See
[CHANGELOG.md](https://github.com/jonpepler/kerbcam/blob/main/client-sdk/typescript/CHANGELOG.md).
While the protocol is at `0.x`, any minor bump may require consumer
updates. Strict SemVer applies at `1.0.0`.
