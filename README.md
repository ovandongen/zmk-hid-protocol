# zmk-hid-protocol

A shared .NET 10 library for talking to ZMK keyboards that run the [`zmk-hid-viz`](https://github.com/ovandongen/zmk-hid-viz) firmware module. It provides:

- Raw HID transport on macOS, Linux, and Windows (USB + BLE).
- A protocol parser and command sender (query device info, query config ID, set layer state).
- Active-window detection on macOS, Linux (X11), and Windows.
- Building blocks for app-layer switching: combo detection, layer-state tracking.

The library contains no UI code. It is consumed as a git submodule by:

- [`moergo-layer-viz`](https://github.com/ovandongen/moergo-layer-viz) — MoErgo keyboard visualizer.
- `zmk-key-viz` — universal ZMK keyboard visualizer (planned).

## Building

```bash
dotnet build
dotnet test
```

On non-Windows hosts only the `net10.0` TFM is built. On Windows the `net10.0-windows10.0.19041.0` TFM is also built, which compiles the WinRT BLE GATT path and the `user32` active-window monitor.

## Design

See [docs/zmk-hid-protocol-spec.md](docs/zmk-hid-protocol-spec.md) for the architecture overview and [docs/implementation-plan.md](docs/implementation-plan.md) for the implementation plan and progress tracker.

## License

The library depends on [HidSharp](https://www.nuget.org/packages/HidSharp) (MIT). The library itself is released under the same terms — see `LICENSE` (TBD).
