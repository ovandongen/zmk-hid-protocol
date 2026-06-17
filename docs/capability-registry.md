# Capability registry (generated)

> Generated from `CapabilityRegistry` — **do not edit by hand**. The
> `CapabilityRegistryDocTests` staleness test regenerates this and fails if it
> drifts. This is the host half of the capability contract; firmware mirrors
> `(id ↔ byte ↔ payload)`. The wire stays open: an id absent here is still
> discovered and inventoried by a host, just inert until defined.

## Known capabilities

| Id | Roles | Tier | Byte | Payload | Value | Confirm | Description |
|---|---|---|---|---|---|---|---|
| `core.pointing.dpi.set` | Handles, Triggers | Core | 0xE1 | Uint32LE | Count | no | Set pointer CPI/DPI sensitivity (absolute count). |
| `core.pointing.dpi.setIndex` | Handles, Triggers | Core | 0xE2 | Uint32LE | Index | no | Select a DPI preset slot by zero-based index. |
| `core.pointing.dragScroll.set` | Handles, Triggers | FwSpecific | 0xE9 | Uint32LE | Toggle | no | Enable or disable drag-scroll mode (0/1). |
| `core.pointing.snipe.set` | Handles, Triggers | FwSpecific | 0xEB | Uint32LE | Toggle | no | Enable or disable snipe (precision) mode (0/1). |
| `core.layer.set` | Handles | Core | 0xFC | LayerSetBitmask | — | no | Set the active-layer bitmask (the app's auto-switch / mouse-layer push). |
| `core.layer.setBase` | Handles | Core | 0xF4 | LayerIndex | — | no | Set the base (default) layer. |
| `core.layer.activate` | Handles | Optional | 0xF3 | LayerRefIndex | — | yes | Activate a layer (momentary/stacked); confirm-acked by reference. |
| `core.layer.deactivate` | Handles | Optional | 0xF2 | LayerRefIndex | — | yes | Deactivate a previously activated layer; confirm-acked by reference. |
| `core.rgb.set` | Handles, Triggers | Optional | 0xD1 | RgbSetMask | — | no | Set RGB underglow to an absolute state (mask-driven HSV + effect). |
| `core.rgb.setKey` | Handles, Triggers | Optional | 0xD2 | RgbSetKey | — | no | Set a single key's RGB color (QMK per-key). |
| `core.layer.changed` | Notifies | Core | 0xFF | LayerStateBitmask | — | no | Active-layer bitmask changed (the core layer-viz telemetry). |
| `core.keyboard.key.event` | Notifies | Optional | 0xF1 | KeyEvent | — | no | Per-key press/release by matrix position (live-highlighting telemetry). |
| `core.rgb.changed` | Notifies | Optional | 0xD0 | RgbState | — | no | RGB state changed (device→host receipt for core.rgb.set). |
| `signal.fire` | Triggers | Core | 0xC0 | SignalId | — | no | Opaque host-defined trigger id; meaning is host config, not on the wire. |

## Payload shapes

Byte layout after `[0] = action`:

| Shape | Layout |
|---|---|
| None | (no payload) |
| Uint32LE | `[1..4]` uint32 LE |
| LayerIndex | `[1]` layer index |
| LayerRefIndex | `[1..2]` ref uint16 LE, `[3]` layer index |
| LayerSetBitmask | `[1..4]` active-layer bitmask uint32 LE |
| RgbSetMask | `[1]` mask, `[2]` on, `[3..4]` hue uint16 LE, `[5]` sat, `[6]` val, `[7]` effect |
| RgbSetKey | `[1]` key index, `[2..3]` hue uint16 LE, `[4]` sat, `[5]` val |
| LayerStateBitmask | `[1]` format marker = 0x04, `[2..5]` default-layer bitmask uint32 LE (single bit), `[6..9]` active-layer bitmask uint32 LE |
| KeyEvent | `[2]` matrix position, `[3]` pressed (0/1) |
| RgbState | `[1]` on, `[2..3]` hue uint16 LE, `[4]` sat, `[5]` val, `[6]` effect |
| SignalId | `[1]` opaque id |
