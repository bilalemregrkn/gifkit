# GifKit

Lightweight GIF recorder and encoder for Unity. No third-party dependencies.

## Files

| File | Description |
|---|---|
| `GifRecorder.cs` | MonoBehaviour — attach to any GameObject |
| `GifEncoder.cs` | Pure C# GIF89a encoder (LZW + median-cut quantizer) |
| `GifSettings.cs` | ScriptableObject — configure fps, resolution, extra duration |

## Quick Start

1. Add `GifRecorder` component to a GameObject in your scene
2. Create a `GifSettings` asset (`Create → Create GifSettings`) and assign it
3. Subscribe to `OnEncoded` and call the record methods:

```csharp
recorder.OnEncoded += bytes => File.WriteAllBytes("output.gif", bytes);

recorder.StartRecording();
// ... later ...
recorder.FinishRecording(extraDuration: 1f); // captures 1 more second then encodes
```

## API

```csharp
void StartRecording()             // start capturing frames
void StopRecording()              // stop capturing, keep frames — does NOT encode
void FinishRecording(float extra) // capture extra seconds then encode
void EncodeNow()                  // stop and encode immediately

bool IsRecording
event Action<byte[]> OnEncoded    // fires when encode is complete, null if no frames
```

## GifSettings

| Property | Default | Description |
|---|---|---|
| `maxDimension` | 480 | Output is scaled so longest side ≤ this value |
| `fps` | 10 | Capture frame rate |
| `extraDuration` | 1.0 | Seconds to keep recording after FinishRecording() |
