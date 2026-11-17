<p align="center">
  <img src="https://github.com/user-attachments/assets/52153a1d-3363-45b6-a054-58d557aec622" alt="Harmony Banner" />
</p>

# Harmony - A Serializable Audio Channel Manager

Harmony is a data-driven audio manager for Unity. `AudioLibrary` stores channel definitions and a unified collection of `AudioAsset`s. `AudioManager` builds runtime sources from that catalog and routes each asset through its assigned channel.

## Features

- Protected `Master`, `Music`, and `SFX` defaults for a quick first setup.
- Custom channels such as `Ambience`, `UI`, `Combat`, or `Voice`.
- Stable channel and asset IDs independent of editable display names and Unity filenames.
- Single-source and pooled playback modes configured per channel.
- Per-channel volume, mute state, fading, and optional `PlayerPrefs` persistence.
- Random multi-clip playback and per-asset spatial audio settings.
- Direct asset references, stable string playback IDs, and an optional unified `AudioClips` enum.
- One-time upgrade of libraries and manager components created with Harmony 1.x.

Requires Unity 2022.3 or newer.

## Installation

### Package Manager (recommended)

1. In Unity, open **Window > Package Manager**.
2. Click **+** and select **Add package from git URL...**.
3. Enter the URL below. The `#2.0.0` suffix pins the version. Leave it out to follow the latest commit.

```text
https://github.com/MicoNoNico/HarmonyAudioManager.git#2.0.0
```

You can also add the line to `Packages/manifest.json` directly:

```json
"com.micolash.harmonyaudio": "https://github.com/MicoNoNico/HarmonyAudioManager.git#2.0.0"
```

### Unity package

1. Download the latest `.unitypackage` from [Releases](https://github.com/MicoNoNico/HarmonyAudioManager/releases).
2. In Unity, go to **Assets > Import Package > Custom Package...**.
3. Select the package and import its files into `Assets/HarmonyAudio`.

Use one installation method per project. If you are replacing a 1.x copy, read [Upgrading from 1.x](#upgrading-from-1x) first.

## Getting Started

### 1. Create a Library

1. Right-click in the Project window.
2. Select **Create > HarmonyAudio > New Audio Library**.
3. Name the asset `AudioLibrary`.

The library creates three protected defaults:

- `master`: global mix bus. Audio assets cannot route directly to it.
- `music`: single-source playback.
- `sfx`: pooled playback.

Use **Add** in the library inspector to create a custom channel. A channel receives an immutable stable ID and an editable display name. Add `Voice` the same way when your project needs a voice channel.

### 2. Create the Manager

1. Create an empty GameObject named `AudioManager`.
2. Add the `AudioManager` component.
3. Assign your `AudioLibrary`.

Harmony creates the required `AudioSource` objects at runtime from the serialized channel definitions.

The manager is a singleton that persists across scene loads. Place it in your first scene.

The `AudioManager` inspector includes a read-only catalog dashboard with channel summaries, assigned asset counts, and validation results.

### 3. Create Audio Assets

1. Right-click in the Project window.
2. Select **Create > HarmonyAudio > New Audio Asset**.
3. Set its stable **Asset ID** and assign its **Channel**.
4. Assign one clip or enable **Allow Multiple Clips** for randomized variants.
5. Enable **Use Spatial Audio** when the asset needs 3D settings.
6. Add the asset to the library's **Unified Audio Assets** list.

The stable asset ID is the recommended string identifier for playback. Renaming the Unity asset file does not change routing or string-based calls.

## Playback API

Play by direct asset reference:

```csharp
using HarmonyAudio.Scripts;
using UnityEngine;

public class MenuAudio : MonoBehaviour
{
    [SerializeField] private AudioAsset click;

    public void PlayClick()
    {
        AudioManager.Play(click);
    }
}
```

Play by stable ID, request a randomized variant, or attach spatial playback to a transform:

```csharp
AudioManager.Play("main-theme", new PlaybackOptions(loop: true));
AudioManager.Play("footsteps", new PlaybackOptions(randomClip: true));
AudioManager.Play("engine", new PlaybackOptions(loop: true, parentTransform: transform));
```

Control any channel through the same API:

```csharp
AudioManager.SetChannelVolume(AudioChannelIds.Sfx, 0.8f);
AudioManager.SetChannelEnabled("ambience", false);
AudioManager.FadeChannelVolume(AudioChannelIds.Music, 0f, 1.5f);
AudioManager.StopChannel("combat");
AudioManager.SaveVolumeSettings();
```

`Play` returns the `AudioSource` that plays the clip, or `null` when nothing was played. Keep the reference when you need to stop or adjust one specific sound, such as a looping engine.

`FadeChannelVolume` takes a gain between `0` and `1` that is multiplied with the channel volume. A fade to `0` silences the channel and a fade to `1` restores the volume the user has set. It does not change the value returned by `GetChannelVolume`.

Passing `AudioChannelIds.Master` to `StopChannel`, `PauseChannel`, or `ResumeChannel` applies the call to every channel.

## Optional Unified Enum

Direct references and stable string IDs are the primary APIs. For a typed workflow:

1. Select the `AudioLibrary`.
2. Check **Output Folder**. The default is `Assets/HarmonyAudio/Generated`. The folder must be inside `Assets`.
3. Click **Regenerate Unified AudioClips Enum**.
4. Wait for Unity to recompile.

Harmony generates one `AudioClips` enum into your project:

```csharp
using HarmonyAudio.Scripts;
using HarmonyAudio.Scripts.Library;

AudioManager.Play(AudioClips.MainTheme, new PlaybackOptions(loop: true));
```

Member names come from the asset IDs. `main-theme` becomes `MainTheme`. Regenerate the enum after you add, remove, or rename assets. Members are matched to assets by name at runtime.

## Upgrading from 1.x

Version 2.0 is a breaking release. Back up or commit your project first.

### 1. Replace the package

1. Move your own files out of `Assets/HarmonyAudio` if you stored any there, such as audio clips, `AudioAsset`s, or your `AudioLibrary`. Move them inside the Unity Editor so references are kept.
2. Delete the old `Assets/HarmonyAudio` folder, including `Scripts/Library` with the generated `MusicClips`, `SoundClips`, and `VoiceClips` enums.
3. Install 2.0 with one of the methods above.

Script references in scenes and assets are kept because the script GUIDs did not change. Your project will show compile errors until step 3 is done.

### 2. Upgrade the data

1. Select your `AudioLibrary` and click **Upgrade Legacy Library** if the button is shown.
2. Select your `AudioManager` scene component and click **Upgrade Legacy Manager Settings** if you want to copy its old volume and pool settings into the library.
3. On the library, set the enum **Output Folder** and click **Regenerate Unified AudioClips Enum**.

The upgrade maps `musicAssets` to the `music` channel, `soundAssets` to `sfx`, and `voiceAssets` to a regular `voice` channel. Each asset receives a stable ID derived from its file name.

An `AudioSource` left on a 1.x `AudioManager` object is still used as the Music source. You can remove it when it does not carry mixer output or other custom settings that your project needs.

### 3. Update your code

| 1.x | 2.0 |
| --- | --- |
| `PlayMusic(MusicClips.X)` | `Play(AudioClips.X, new PlaybackOptions(loop: true))` |
| `PlayRandomMusic(MusicClips.X)` | `Play(AudioClips.X, new PlaybackOptions(loop: true, randomClip: true))` |
| `PlaySound(SoundClips.X)` | `Play(AudioClips.X)` |
| `PlaySound(SoundClips.X, transform)` | `Play(AudioClips.X, new PlaybackOptions(parentTransform: transform))` |
| `PlayRandomSound(SoundClips.X)` | `Play(AudioClips.X, new PlaybackOptions(randomClip: true))` |
| `PlayVoice(VoiceClips.X)` | `Play(AudioClips.X)` |
| `PlayLoopingSpatialSound(SoundClips.X, transform, startMuted)` | `Play(AudioClips.X, new PlaybackOptions(loop: true, parentTransform: transform, startMuted: startMuted))` |
| `StopMusic()`, `StopVoice()` | `StopChannel(AudioChannelIds.Music)`, `StopChannel(AudioChannelIds.Voice)` |
| `PauseMusic()`, `ResumeMusic()` | `PauseChannel(AudioChannelIds.Music)`, `ResumeChannel(AudioChannelIds.Music)` |
| `SetMusicVolume(v)`, `GetMusicVolume()` | `SetChannelVolume(AudioChannelIds.Music, v)`, `GetChannelVolume(AudioChannelIds.Music)` |
| `SetSoundVolume(v)`, `GetSoundVolume()` | `SetChannelVolume(AudioChannelIds.Sfx, v)`, `GetChannelVolume(AudioChannelIds.Sfx)` |
| `SetVoiceVolume(v)`, `GetVoiceVolume()` | `SetChannelVolume(AudioChannelIds.Voice, v)`, `GetChannelVolume(AudioChannelIds.Voice)` |
| `FadeMusicVolume(target, duration)` | `FadeChannelVolume(AudioChannelIds.Music, gain, duration)` |

Two behaviors changed:

- `PlayMusic` looped by default. `Play` does not loop unless you pass `loop: true`.
- Fades used to change the channel volume itself. They now change a separate gain. Code that fades out and later fades back to a saved volume should fade back to `1` instead.

## Channel Model

Each serialized channel defines:

- Stable ID and editable display name
- Default volume and enabled state
- Optional settings persistence
- Playback mode: `SingleSource` or `Pooled`
- Initial and maximum pool size for pooled channels

Runtime source volume is composed as:

```text
master volume * channel volume * fade gain * clip volume * source gain
```

This keeps clip-specific balancing intact when users change channel sliders or run fades.
