# Changelog

## [2.0.0]

Harmony 2.0 replaces the fixed Music, Sound, and Voice categories with configurable channels. It is a breaking release. See "Upgrading from 1.x" in the README.

### Added

- Channel model. An `AudioLibrary` defines channels with a stable ID, display name, default volume, enabled state, optional `PlayerPrefs` persistence, and a playback mode (`SingleSource` or `Pooled`). `Master`, `Music`, and `SFX` are built in. Any number of custom channels can be added.
- Unified playback API: `AudioManager.Play(AudioAsset)`, `AudioManager.Play(string assetId)`, and `AudioManager.Play(AudioClips.Member)`, each with optional `PlaybackOptions` (loop, random clip, parent transform, start muted).
- Channel API: `StopChannel`, `PauseChannel`, `ResumeChannel`, `SetChannelVolume`, `GetChannelVolume`, `SetChannelEnabled`, `GetChannelEnabled`, `FadeChannelVolume`, and `GetActiveChannelSource`.
- Stable asset IDs on `AudioAsset`, independent of the asset file name.
- Library validation in the inspectors and at startup.
- Installation through the Unity Package Manager with a git URL.
- One-time upgrade of 1.x libraries and 1.x `AudioManager` components from the inspector.

### Changed

- One generated `AudioClips` enum replaces `MusicClips`, `SoundClips`, and `VoiceClips`. It is generated into the consuming project, by default in `Assets/HarmonyAudio/Generated`, and no longer into the package folder.
- Fades change a separate fade gain and leave the channel volume untouched. A fade target of `1` means the full channel volume.
- Volume settings are saved under `HarmonyAudio.Channel.<id>.Volume` and `HarmonyAudio.Channel.<id>.Enabled`. Values saved by 1.x are still read as a fallback.
- The inspector banner moved to the `Editor` folder and is no longer included in player builds.

### Removed

- `PlayMusic`, `PlayRandomMusic`, `PlaySound`, `PlayRandomSound`, `PlayVoice`, `PlayRandomVoice`, `PlayLoopingSpatialSound`, and the per-category stop, pause, resume, fade, and volume methods. `SetMasterVolume` and `GetMasterVolume` remain.
- The `MusicClips`, `SoundClips`, and `VoiceClips` enums.
- The `Resources` folder with the bundled font and icon.

## [1.0.5]

- Spatial audio settings on `AudioAsset`.
