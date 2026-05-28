import type { AdaptiveShedPayload, CameraState, ClientMessage, ServerMessage } from "../__generated__/types";
import { CameraLifecycle, Layer } from "../__generated__/types";
import type {
  KerbcamConnectionState,
  KerbcamDataChannel,
  KerbcamPeer,
  KerbcamTransport,
} from "../client";

export interface MockCameraInit {
  flightId: number;
  lifecycle?: CameraLifecycle;
  partName?: string;
  partTitle?: string;
  cameraName?: string;
  vesselName?: string;
  layers?: Layer[];
  operatorLayers?: Layer[];
  renderWidth?: number;
  renderHeight?: number;
  operatorWidth?: number;
  operatorHeight?: number;
  supportsZoom?: boolean;
  fov?: number;
  fovMin?: number;
  fovMax?: number;
  supportsPan?: boolean;
  panYaw?: number;
  panPitch?: number;
  panYawMin?: number;
  panYawMax?: number;
  panPitchMin?: number;
  panPitchMax?: number;
  encoderBitrateBps?: number;
  targetBitrateBps?: number;
  degradeLevel?: number;
}

function buildCamera(init: MockCameraInit): CameraState {
  return {
    flightId: init.flightId,
    lifecycle: init.lifecycle ?? CameraLifecycle.Active,
    partName: init.partName ?? `part-${init.flightId}`,
    partTitle: init.partTitle ?? `Part ${init.flightId}`,
    cameraName: init.cameraName ?? `camera-${init.flightId}`,
    vesselName: init.vesselName ?? "Test Vessel",
    layers: init.layers ?? [Layer.Near],
    operatorLayers: init.operatorLayers ?? [Layer.Near],
    renderWidth: init.renderWidth ?? 1280,
    renderHeight: init.renderHeight ?? 720,
    operatorWidth: init.operatorWidth ?? 1280,
    operatorHeight: init.operatorHeight ?? 720,
    supportsZoom: init.supportsZoom ?? true,
    fov: init.fov ?? 60,
    fovMin: init.fovMin ?? 10,
    fovMax: init.fovMax ?? 120,
    supportsPan: init.supportsPan ?? false,
    panYaw: init.panYaw ?? 0,
    panPitch: init.panPitch ?? 0,
    panYawMin: init.panYawMin ?? -90,
    panYawMax: init.panYawMax ?? 90,
    panPitchMin: init.panPitchMin ?? -90,
    panPitchMax: init.panPitchMax ?? 90,
    encoderBitrateBps: init.encoderBitrateBps ?? 0,
    targetBitrateBps: init.targetBitrateBps ?? 0,
    degradeLevel: init.degradeLevel ?? 0,
  };
}

/**
 * In-process protocol-level fake for the kerbcam sidecar.
 *
 * Owns a camera registry and speaks the full kerbcam wire protocol.
 * Use it in tests to exercise `KerbcamClient` behaviour without a real
 * sidecar or WebRTC stack.
 *
 * ```ts
 * const sidecar = new MockSidecar();
 * sidecar.addCamera({ flightId: 42 });
 *
 * vi.spyOn(globalThis, "fetch").mockImplementation(() =>
 *   Promise.resolve(MockSidecar.makeOfferResponse([42]))
 * );
 *
 * const client = new KerbcamClient({ host: "localhost", port: 8088 }, sidecar.createTransport());
 * await client.connect([42]);
 * sidecar.open();   // fires hello + camera-snapshot
 *
 * expect(client.cameras[0].flightId).toBe(42);
 * ```
 */
export class MockSidecar {
  private readonly _cameras = new Map<number, CameraState>();
  private readonly _commands: ClientMessage[] = [];

  private _openHandler: (() => void) | undefined;
  private _clientMsgHandler: ((raw: string) => void) | undefined;
  private _stateHandler: ((s: KerbcamConnectionState) => void) | undefined;

  /** Register a camera that will appear in the `camera-snapshot` sent on `open()`. */
  addCamera(init: MockCameraInit): void {
    this._cameras.set(init.flightId, buildCamera(init));
  }

  /**
   * Returns a `KerbcamTransport` backed by this mock. Pass it as the
   * second argument to `KerbcamClient`.
   */
  createTransport(): KerbcamTransport {
    const self = this;
    return {
      createPeer(): KerbcamPeer {
        const channel: KerbcamDataChannel = {
          send(payload) {
            const msg = JSON.parse(payload) as ClientMessage;
            self._commands.push(msg);
            self._handleClientMessage(msg);
          },
          onOpen(h) {
            self._openHandler = h;
          },
          onMessage(h) {
            self._clientMsgHandler = h;
          },
          onClose() {},
        };
        return {
          addRecvOnlyTransceiver() {},
          createDataChannel: () => channel,
          onTrack() {},
          onStateChange(h) {
            self._stateHandler = h;
          },
          createOffer: async () => "v=0\r\n",
          setLocalDescription: async () => {},
          setRemoteAnswer: async () => {},
          waitForIceComplete: async () => {},
          localSdp: () => "v=0\r\n",
          close() {},
        };
      },
    };
  }

  /**
   * Simulate the sidecar completing the WebRTC handshake. Fires the
   * channel `onOpen` handler (which triggers the client's `hello`), then
   * responds with `hello` + `camera-snapshot`.
   */
  open(): void {
    this._openHandler?.();
    this._sendToClient({ type: "hello", content: { sidecarVersion: "0.0.1-mock", encoderBackend: "mock" } });
    this._sendToClient({ type: "camera-snapshot", content: { cameras: Array.from(this._cameras.values()) } });
  }

  /** Drive the underlying peer's connection-state handler. */
  setConnectionState(state: KerbcamConnectionState): void {
    this._stateHandler?.(state);
  }

  /**
   * Mark a camera as destroyed and push a `camera-state-changed` message
   * to the client. The camera stays in the internal registry with
   * `lifecycle: Destroyed`.
   */
  destroyCamera(flightId: number): void {
    const cam = this._cameras.get(flightId);
    if (!cam) return;
    const destroyed: CameraState = { ...cam, lifecycle: CameraLifecycle.Destroyed };
    this._cameras.set(flightId, destroyed);
    this._sendToClient({ type: "camera-state-changed", content: { state: destroyed } });
  }

  /**
   * Apply a partial update to an existing camera and push a
   * `camera-state-changed` message to the client.
   */
  updateCamera(flightId: number, partial: Partial<CameraState>): void {
    const cam = this._cameras.get(flightId);
    if (!cam) return;
    const updated: CameraState = { ...cam, ...partial };
    this._cameras.set(flightId, updated);
    this._sendToClient({ type: "camera-state-changed", content: { state: updated } });
  }

  /** Send a `ping` from the sidecar; the client should respond with `pong`. */
  firePing(): void {
    this._sendToClient({ type: "ping" });
  }

  /** Push an `adaptive-shed` event to the client. */
  fireAdaptiveShed(payload: AdaptiveShedPayload): void {
    this._sendToClient({ type: "adaptive-shed", content: payload });
  }

  /** Every `ClientMessage` received from the client, in order. */
  get commands(): ReadonlyArray<ClientMessage> {
    return this._commands;
  }

  /**
   * Find the most recent client command of the given type. Pass `flightId`
   * to further filter by camera (ignored for message types without a
   * `content.flightId` field).
   */
  lastCommand<T extends ClientMessage["type"]>(
    type: T,
    flightId?: number,
  ): Extract<ClientMessage, { type: T }> | undefined {
    for (let i = this._commands.length - 1; i >= 0; i--) {
      const cmd = this._commands[i];
      if (cmd.type !== type) continue;
      if (flightId !== undefined) {
        const c = cmd as { content?: { flightId?: number } };
        if (c.content?.flightId !== flightId) continue;
      }
      return cmd as Extract<ClientMessage, { type: T }>;
    }
    return undefined;
  }

  /**
   * Build a `Response` that looks like the sidecar's `POST /offer` reply.
   * Pass to `vi.spyOn(globalThis, "fetch").mockImplementation(...)` so the
   * client's handshake can complete without a real HTTP server.
   *
   * ```ts
   * vi.spyOn(globalThis, "fetch").mockImplementation(() =>
   *   Promise.resolve(MockSidecar.makeOfferResponse([42]))
   * );
   * ```
   */
  static makeOfferResponse(cameras: number[]): Response {
    return new Response(
      JSON.stringify({ sdp: "v=0\r\n", cameras }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }

  private _sendToClient(msg: ServerMessage): void {
    this._clientMsgHandler?.(JSON.stringify(msg));
  }

  private _handleClientMessage(msg: ClientMessage): void {
    switch (msg.type) {
      case "set-fov": {
        const cam = this._cameras.get(msg.content.flightId);
        if (cam) this._cameras.set(msg.content.flightId, { ...cam, fov: msg.content.fov });
        break;
      }
      case "set-layers": {
        const cam = this._cameras.get(msg.content.flightId);
        if (cam) {
          this._cameras.set(msg.content.flightId, {
            ...cam,
            layers: msg.content.layers,
            operatorLayers: msg.content.layers,
          });
        }
        break;
      }
      case "set-render-size": {
        const cam = this._cameras.get(msg.content.flightId);
        if (cam) {
          this._cameras.set(msg.content.flightId, {
            ...cam,
            renderWidth: msg.content.width,
            renderHeight: msg.content.height,
            operatorWidth: msg.content.width,
            operatorHeight: msg.content.height,
          });
        }
        break;
      }
      case "set-pan": {
        const cam = this._cameras.get(msg.content.flightId);
        if (cam) {
          this._cameras.set(msg.content.flightId, {
            ...cam,
            panYaw: msg.content.yaw,
            panPitch: msg.content.pitch,
          });
        }
        break;
      }
      case "set-degrade": {
        const cam = this._cameras.get(msg.content.flightId);
        if (cam) {
          this._cameras.set(msg.content.flightId, { ...cam, degradeLevel: msg.content.level });
        }
        break;
      }
      case "hello":
      case "pong":
      case "request-keyframe":
        break;
    }
  }
}
