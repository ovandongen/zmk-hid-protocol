# moergo-layer-viz Cutover Brief

**Intended audience:** A Claude Code instance started in `/Users/user/Desktop/Projects/gitrepos/moergo-layer-viz/`.

**Goal:** Replace moergo-layer-viz's inline Raw-HID transport with the new `zmk-hid-protocol` library, consumed as a git submodule. The library at `/Users/user/Desktop/Projects/gitrepos/zmk-hid-protocol/` is feature-complete (82 tests passing) but its work is not yet pushed to GitHub — that needs to happen first.

This is an atomic cutover: one PR that adds the submodule, deletes the originals, rewrites namespaces, and adds an adapter. Tests + build must pass on macOS (host) and ideally on Linux/Windows via CI.

---

## Prerequisites (one-time, done outside moergo-layer-viz)

In `/Users/user/Desktop/Projects/gitrepos/zmk-hid-protocol`:
1. Commit the current working tree (single commit covering steps 2–11).
2. `git push origin main`.

Submodule URL after push: `https://github.com/ovandongen/zmk-hid-protocol.git`.

---

## Step 1: Add the submodule

```
cd /Users/user/Desktop/Projects/gitrepos/moergo-layer-viz
git submodule add https://github.com/ovandongen/zmk-hid-protocol.git external/zmk-hid-protocol
```

Add ProjectReference in `src/MoergoLayerViz.Core/MoergoLayerViz.Core.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\external\zmk-hid-protocol\src\ZmkHidProtocol\ZmkHidProtocol.csproj" />
</ItemGroup>
```

`MoergoLayerViz.App` does **not** need a direct ProjectReference — it pulls the library transitively through Core.

---

## Step 2: Delete files (do this before adding the adapter so dead `using` lines are easier to spot)

From `src/MoergoLayerViz.Core/Input/`:
- `ILayerSource.cs`
- `RawHidProtocol.cs`
- `LinuxRawHidLayerSource.cs`
- `MacRawHidLayerSource.cs`
- `RawHidLayerSource.cs` (Windows USB)

From `src/MoergoLayerViz.App/Services/`:
- `WindowsBleRawHidLayerSource.cs`
- `WindowsHidCompositeLayerSource.cs`

**Keep:**
- `HotkeyLayerTracker.cs`, `HotkeyLayerTrackerLayerSource.cs` — SharpHook fallback, app-specific
- `IKeyEventSource.cs`, `SharpHookKeyEventSource.cs`, `SharpHookProvider.cs`, `GlobalHotkeyService.cs`, `LayerSourceCoordinator.cs`
- `Layout/IKeyboardProfile.cs`

---

## Step 3: Add the `IKeyboardProfile → IDeviceMatcher` adapter

Create `src/MoergoLayerViz.Core/Input/KeyboardProfileMatcher.cs`:

```csharp
using MoergoLayerViz.Core.Layout;
using ZmkHidProtocol.Transport;

namespace MoergoLayerViz.Core.Input;

/// <summary>
/// Adapts the app's <see cref="IKeyboardProfile"/> to the library's
/// <see cref="IDeviceMatcher"/> so the transport layer doesn't depend on
/// MoErgo-specific abstractions.
/// </summary>
public sealed class KeyboardProfileMatcher(IKeyboardProfile profile) : IDeviceMatcher
{
    public bool Matches(int vendorId, int productId, string? productName)
        => profile.MatchesHidDevice(vendorId, productId, productName);

    // Windows BLE can't read VID/PID — name-only fallback.
    public bool MatchesName(string? productName)
        => profile.MatchesHidDevice(0, 0, productName);
}
```

If `IKeyboardProfile.MatchesHidDevice` short-circuits on vid==0/pid==0 in a way that breaks the BLE path, instead expose the profile's `NamePrefixes` list and forward to `productName?.StartsWith(prefix, OrdinalIgnoreCase)` directly. Inspect the implementation before deciding.

---

## Step 4: Rewrite the `HotkeyLayerTrackerLayerSource` to the new `ILayerSource` shape

The library's `ILayerSource` differs from the old one in three ways:

| Old (deleted) | New (library) |
|---|---|
| `void SetProfile(IKeyboardProfile?)` | `void SetMatcher(IDeviceMatcher?)` |
| no `ReportReceived` event | `event Action<ReadOnlyMemory<byte>>? ReportReceived` |
| no `ICommandSink` pairing | every source also implements `ICommandSink` |

`HotkeyLayerTrackerLayerSource` is a fake/synthetic source that never reads raw HID, so:
- `SetMatcher` can be a no-op (the SharpHook tracker doesn't filter by device).
- `ReportReceived` is declared but never raised.
- It does **not** need to implement `ICommandSink` — only sources that talk to real hardware do. But `LayerSourceCoordinator` may need to handle "this source has no command sink" — see Step 6.

Add `#pragma warning disable CS0067` around the unused `ReportReceived` event declaration.

---

## Step 5: Replace inline OS dispatch with `LayerSourceFactory`

In `src/MoergoLayerViz.App/ViewModels/MainWindowViewModel.cs`, the existing block (roughly):

```csharp
ILayerSource hidSource;
#if WINDOWS
    hidSource = new WindowsHidCompositeLayerSource(_profile);
#else
    hidSource = OperatingSystem.IsMacOS()
        ? new MacRawHidLayerSource(_profile)
        : OperatingSystem.IsLinux()
            ? new LinuxRawHidLayerSource(_profile)
            : new RawHidLayerSource(_profile);
#endif
```

…becomes:

```csharp
using ZmkHidProtocol.Transport;
// …
var matcher = new KeyboardProfileMatcher(_profile);
var (hidSource, hidSink) = LayerSourceFactory.Create(matcher);
```

Stash `hidSink` somewhere the app can reach if/when it starts wanting to send commands (`SetLayerStateAsync`, `QueryDeviceInfoAsync`, etc.). If the app doesn't use commands yet, just discard the sink and let the deconstruction `var (hidSource, _) = …` ignore it.

The `#if WINDOWS` block is no longer needed — the library handles it.

---

## Step 6: Namespace rewrites

Use the IDE's **rename refactor** (Rider: Shift+F6 on the namespace; VS: Ctrl+R, R) — **not** `sed`. ~30–50 `using` lines will change. Common mappings:

| Old | New |
|---|---|
| `MoergoLayerViz.Core.Input.RawHidProtocol` | `ZmkHidProtocol.Protocol.RawHidProtocol` |
| `MoergoLayerViz.Core.Input.ILayerSource` | `ZmkHidProtocol.Transport.ILayerSource` |
| `MoergoLayerViz.Core.Input.RawHidLayerSource` | (deleted; use `LayerSourceFactory.Create()`) |
| `MoergoLayerViz.Core.Input.MacRawHidLayerSource` | (deleted; same) |
| `MoergoLayerViz.Core.Input.LinuxRawHidLayerSource` | (deleted; same) |
| `MoergoLayerViz.App.Services.WindowsBleRawHidLayerSource` | (deleted) |
| `MoergoLayerViz.App.Services.WindowsHidCompositeLayerSource` | (deleted) |

Files that **stay** in the `MoergoLayerViz.Core.Input` namespace: `HotkeyLayerTracker`, `HotkeyLayerTrackerLayerSource`, `IKeyEventSource`, plus the new `KeyboardProfileMatcher`.

After rename, do a global search for the dead old names and confirm they're gone.

---

## Step 7: Tests in moergo-layer-viz

Find moergo-layer-viz's test project. Any test referencing the deleted `RawHidProtocol` parser should either:
- Delete (parser is now exercised by the library's 30+ parser tests), or
- Re-target the `ZmkHidProtocol.Protocol.RawHidProtocol` static class (same API).

Run `dotnet test` and confirm green.

---

## Step 8: Verify

```
dotnet build
dotnet test
```

Then launch the app on macOS (your host) and confirm:
- Status line shows a connected keyboard when one is plugged in.
- Layer-state updates land when the keyboard switches layers (USB).
- App doesn't crash when no keyboard is connected.

Linux and Windows verification can land in CI on the PR.

---

## What this cutover does **not** do

- Doesn't wire up `CommandSender`, `ComboDetector`, `LayerStateTracker`, or `IActiveWindowMonitor` into the app yet. Those are new library capabilities; integration is a separate task (the spec's "Phase 5: Integrate into MoergoLayerViz" — app-layer mapping UI, auto-switch toggle, etc.).
- Doesn't change the SharpHook fallback. `LayerSourceCoordinator` keeps swapping between SharpHook and Raw HID.
- Doesn't add new features. Strictly a refactor.

---

## Rollback plan

If anything goes catastrophically wrong:
```
git submodule deinit -f external/zmk-hid-protocol
git rm -f external/zmk-hid-protocol
rm -rf .git/modules/external/zmk-hid-protocol
git checkout HEAD -- src/
```

Then iterate.
