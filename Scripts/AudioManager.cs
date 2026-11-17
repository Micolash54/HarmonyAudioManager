/*
 * Harmony - Simple Audio Manager
 * Author: Niccolo Chiodo
 * Copyright 2024 Niccolo Chiodo
 *
 * View License in HarmonyAudio/LICENSE.md
 */

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HarmonyAudio.Scripts
{
    public struct PlaybackOptions
    {
        public bool Loop;
        public bool RandomClip;
        public bool StartMuted;
        public Transform ParentTransform;

        public PlaybackOptions(
            bool loop = false,
            bool randomClip = false,
            Transform parentTransform = null,
            bool startMuted = false)
        {
            Loop = loop;
            RandomClip = randomClip;
            ParentTransform = parentTransform;
            StartMuted = startMuted;
        }
    }

    public class AudioManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AudioLibrary audioLibrary;

        // Settings from 1.x scene components. They are only read by UpgradeLegacyConfiguration,
        // which copies them into the AudioLibrary once.
        [SerializeField, HideInInspector] private AudioSource musicSource;
        [SerializeField, HideInInspector] private int initialSoundPoolSize = 5;
        [SerializeField, HideInInspector] private int maxSoundPoolSize = 10;
        [SerializeField, HideInInspector] private int initialVoicePoolSize = 3;
        [SerializeField, HideInInspector] private int maxVoicePoolSize = 10;
        [SerializeField, HideInInspector] private float masterVolume = 1f;
        [SerializeField, HideInInspector] private float musicVolume = 1f;
        [SerializeField, HideInInspector] private float soundsVolume = 1f;
        [SerializeField, HideInInspector] private float voiceVolume = 1f;
        [SerializeField, HideInInspector] public bool enableVoice;
        [SerializeField, HideInInspector] private bool legacyManagerSettingsMigrated;

        private static AudioManager _instance;
        private readonly Dictionary<string, AudioChannelRuntime> _channels =
            new Dictionary<string, AudioChannelRuntime>(StringComparer.OrdinalIgnoreCase);

        public AudioLibrary Catalog => audioLibrary;

        /// <summary>
        /// True while this component still carries 1.x settings that have not been copied into the library.
        /// </summary>
        public bool HasPendingLegacySettings => !legacyManagerSettingsMigrated;

        // Called by the editor when the component is first added. A new component has no 1.x settings to import.
        private void Reset()
        {
            legacyManagerSettingsMigrated = true;
        }

        protected void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        public bool UpgradeLegacyConfiguration()
        {
            if (audioLibrary == null || legacyManagerSettingsMigrated)
                return false;

            audioLibrary.UpgradeLegacyData();
            audioLibrary.ApplyLegacyManagerDefaults(
                masterVolume,
                musicVolume,
                soundsVolume,
                initialSoundPoolSize,
                maxSoundPoolSize,
                enableVoice,
                voiceVolume,
                initialVoicePoolSize,
                maxVoicePoolSize);
            legacyManagerSettingsMigrated = true;
            return true;
        }

        private void Initialize()
        {
            if (audioLibrary == null)
            {
                Debug.LogError("AudioManager requires an AudioLibrary.");
                return;
            }

            audioLibrary.UpgradeLegacyData();
            BuildChannelRuntimes();
            LoadVolumeSettingsInternal();
            InitializeRuntimeSources();
            UpdateAllSourceVolumes();

            foreach (string message in audioLibrary.GetValidationMessages())
                Debug.LogWarning($"HarmonyAudio validation: {message}", audioLibrary);
        }

        private void BuildChannelRuntimes()
        {
            _channels.Clear();
            foreach (AudioChannelDefinition definition in audioLibrary.Channels)
            {
                if (definition == null || string.IsNullOrEmpty(definition.Id))
                    continue;

                if (_channels.ContainsKey(definition.Id))
                {
                    Debug.LogWarning($"Duplicate audio channel ID '{definition.Id}' was ignored.", audioLibrary);
                    continue;
                }

                var runtime = new AudioChannelRuntime(definition);
                if (!definition.IsMaster)
                {
                    runtime.Root = new GameObject($"{definition.DisplayName} Sources");
                    runtime.Root.transform.SetParent(transform);
                }

                _channels.Add(definition.Id, runtime);
            }
        }

        private void InitializeRuntimeSources()
        {
            foreach (AudioChannelRuntime runtime in _channels.Values)
            {
                if (runtime.Definition.IsMaster)
                    continue;

                if (runtime.Definition.PlaybackMode == AudioChannelPlaybackMode.SingleSource)
                {
                    GetOrCreateSingleSource(runtime);
                    continue;
                }

                for (int index = 0; index < runtime.Definition.InitialPoolSize; index++)
                    CreateRuntimeSource(runtime, true);
            }
        }

        public static AudioSource Play(AudioAsset asset, PlaybackOptions options = default(PlaybackOptions))
        {
            if (!TryGetInstance(out AudioManager manager))
                return null;

            return manager.PlayInternal(asset, options);
        }

        public static AudioSource Play(string assetId, PlaybackOptions options = default(PlaybackOptions))
        {
            if (!TryGetInstance(out AudioManager manager))
                return null;

            AudioAsset asset = manager.audioLibrary != null ? manager.audioLibrary.GetAsset(assetId) : null;
            return manager.PlayInternal(asset, options);
        }

        /// <summary>
        /// Plays the asset that matches a member of the generated <c>AudioClips</c> enum.
        /// The enum is generated into the consuming project, so any enum type is accepted
        /// and resolved by member name.
        /// </summary>
        public static AudioSource Play<TEnum>(TEnum clip, PlaybackOptions options = default(PlaybackOptions))
            where TEnum : struct, Enum
        {
            if (!TryGetInstance(out AudioManager manager))
                return null;

            return manager.PlayInternal(manager.audioLibrary.GetAsset(clip), options);
        }

        public static void StopChannel(string channelId)
        {
            if (TryGetInstance(out AudioManager manager))
                manager.StopChannelInternal(channelId);
        }

        public static void PauseChannel(string channelId)
        {
            if (TryGetInstance(out AudioManager manager))
                manager.ForEachChannelSource(channelId, source => source.Pause());
        }

        public static void ResumeChannel(string channelId)
        {
            if (TryGetInstance(out AudioManager manager))
                manager.ForEachChannelSource(channelId, source => source.UnPause());
        }

        public static void SetChannelVolume(string channelId, float volume)
        {
            if (!TryGetInstance(out AudioManager manager))
                return;

            if (!manager.TryGetRuntime(channelId, out AudioChannelRuntime runtime))
                return;

            runtime.Volume = Mathf.Clamp01(volume);
            manager.UpdateRuntimeSourceVolumes(runtime);
        }

        public static float GetChannelVolume(string channelId)
        {
            if (!TryGetInstance(out AudioManager manager))
                return 0f;

            return manager.TryGetRuntime(channelId, out AudioChannelRuntime runtime) ? runtime.Volume : 0f;
        }

        public static void SetChannelEnabled(string channelId, bool enabled)
        {
            if (!TryGetInstance(out AudioManager manager))
                return;

            if (!manager.TryGetRuntime(channelId, out AudioChannelRuntime runtime))
                return;

            runtime.Enabled = enabled;
            if (!enabled)
                manager.StopChannelInternal(channelId);

            manager.UpdateRuntimeSourceVolumes(runtime);
        }

        public static bool GetChannelEnabled(string channelId)
        {
            if (!TryGetInstance(out AudioManager manager))
                return false;

            return manager.TryGetRuntime(channelId, out AudioChannelRuntime runtime) && runtime.Enabled;
        }

        public static AudioSource GetActiveChannelSource(string channelId)
        {
            if (!TryGetInstance(out AudioManager manager))
                return null;

            if (!manager.TryGetRuntime(channelId, out AudioChannelRuntime runtime))
                return null;

            foreach (AudioSource source in runtime.Sources)
            {
                if (source != null && source.isPlaying)
                    return source;
            }

            return null;
        }

        public static void FadeChannelVolume(string channelId, float targetGain, float duration)
        {
            if (!TryGetInstance(out AudioManager manager))
                return;

            if (!manager.TryGetRuntime(channelId, out AudioChannelRuntime runtime))
                return;

            if (runtime.FadeCoroutine != null)
                manager.StopCoroutine(runtime.FadeCoroutine);

            runtime.FadeCoroutine = manager.StartCoroutine(
                manager.FadeChannelGainCoroutine(runtime, Mathf.Clamp01(targetGain), duration));
        }

        public static void SaveVolumeSettings()
        {
            if (TryGetInstance(out AudioManager manager))
                manager.SaveVolumeSettingsInternal();
        }

        public static void LoadVolumeSettings()
        {
            if (!TryGetInstance(out AudioManager manager))
                return;

            manager.LoadVolumeSettingsInternal();
            manager.UpdateAllSourceVolumes();
        }

        public static void SetMasterVolume(float volume)
        {
            SetChannelVolume(AudioChannelIds.Master, volume);
        }

        public static float GetMasterVolume()
        {
            return GetChannelVolume(AudioChannelIds.Master);
        }

        private AudioSource PlayInternal(AudioAsset asset, PlaybackOptions options)
        {
            if (asset == null)
                return null;

            if (!TryGetRuntime(asset.ChannelId, out AudioChannelRuntime runtime))
                return null;

            if (runtime.Definition.IsMaster)
            {
                Debug.LogWarning($"AudioAsset '{asset.name}' cannot route directly to the Master bus.", asset);
                return null;
            }

            if (!runtime.Enabled || !GetMasterRuntime().Enabled)
                return null;

            // Spatial sources are destroyed together with their parent object. Drop those entries.
            runtime.Sources.RemoveAll(IsDestroyedSource);

            AudioClipWithVolume clip = options.RandomClip
                ? GetRandomClipFromAsset(asset)
                : GetClipFromAsset(asset);
            if (clip == null || clip.Clip == null)
            {
                Debug.LogWarning($"AudioAsset '{asset.name}' does not contain a playable clip.", asset);
                return null;
            }

            AudioSource source;
            if (options.ParentTransform != null)
            {
                source = CreateSpatialSource(runtime, options.ParentTransform, asset);
            }
            else if (runtime.Definition.PlaybackMode == AudioChannelPlaybackMode.SingleSource)
            {
                source = GetOrCreateSingleSource(runtime);
            }
            else
            {
                source = AcquirePooledSource(runtime);
            }

            if (source == null)
            {
                Debug.LogWarning($"All sources for channel '{runtime.Definition.DisplayName}' are busy.");
                return null;
            }

            return ConfigureAndPlay(runtime, source, clip, options);
        }

        private AudioSource ConfigureAndPlay(
            AudioChannelRuntime runtime,
            AudioSource source,
            AudioClipWithVolume clip,
            PlaybackOptions options)
        {
            AudioSourceMetadata metadata = GetOrAddMetadata(source);
            metadata.playbackVersion++;
            metadata.channelId = runtime.Definition.Id;
            metadata.clipVolume = clip.Volume;
            metadata.sourceGain = options.StartMuted ? 0f : 1f;

            source.clip = clip.Clip;
            source.loop = options.Loop;
            ApplySourceVolume(runtime, source);
            source.Play();

            if (!options.Loop && (metadata.isSpatial || !metadata.isInitial))
            {
                StartCoroutine(ReleaseSourceAfterPlayback(runtime, source, metadata.playbackVersion));
            }

            return source;
        }

        private AudioSource GetOrCreateSingleSource(AudioChannelRuntime runtime)
        {
            foreach (AudioSource source in runtime.Sources)
            {
                if (source == null)
                    continue;

                AudioSourceMetadata metadata = source.GetComponent<AudioSourceMetadata>();
                if (metadata != null && !metadata.isSpatial)
                    return source;
            }

            if (runtime.Definition.Id == AudioChannelIds.Music && musicSource != null)
            {
                RegisterSource(runtime, musicSource, true, false);
                return musicSource;
            }

            return CreateRuntimeSource(runtime, true);
        }

        private AudioSource AcquirePooledSource(AudioChannelRuntime runtime)
        {
            foreach (AudioSource source in runtime.Sources)
            {
                if (source == null || source.isPlaying)
                    continue;

                AudioSourceMetadata metadata = source.GetComponent<AudioSourceMetadata>();
                if (metadata != null && !metadata.isSpatial)
                    return source;
            }

            int maxPoolSize = runtime.Definition.MaxPoolSize;
            if (maxPoolSize != 0 && CountPoolSources(runtime) >= maxPoolSize)
                return null;

            return CreateRuntimeSource(runtime, false);
        }

        private AudioSource CreateRuntimeSource(AudioChannelRuntime runtime, bool isInitial)
        {
            var sourceObject = new GameObject($"{runtime.Definition.DisplayName} Source");
            sourceObject.transform.SetParent(runtime.Root.transform);
            var source = sourceObject.AddComponent<AudioSource>();
            RegisterSource(runtime, source, isInitial, false);
            return source;
        }

        private AudioSource CreateSpatialSource(
            AudioChannelRuntime runtime,
            Transform parentTransform,
            AudioAsset asset)
        {
            var sourceObject = new GameObject($"{runtime.Definition.DisplayName} Spatial Source");
            sourceObject.transform.SetParent(parentTransform);
            sourceObject.transform.localPosition = Vector3.zero;

            var source = sourceObject.AddComponent<AudioSource>();
            RegisterSource(runtime, source, false, true);
            source.spatialBlend = asset.useSpatialAudio ? asset.spatialBlend : 0f;
            source.rolloffMode = asset.rolloffMode;
            source.minDistance = asset.minDistance;
            source.maxDistance = asset.maxDistance;
            return source;
        }

        private static void RegisterSource(
            AudioChannelRuntime runtime,
            AudioSource source,
            bool isInitial,
            bool isSpatial)
        {
            source.playOnAwake = false;
            AudioSourceMetadata metadata = GetOrAddMetadata(source);
            metadata.isInitial = isInitial;
            metadata.isSpatial = isSpatial;
            metadata.channelId = runtime.Definition.Id;
            if (!runtime.Sources.Contains(source))
                runtime.Sources.Add(source);
        }

        private static AudioSourceMetadata GetOrAddMetadata(AudioSource source)
        {
            AudioSourceMetadata metadata = source.GetComponent<AudioSourceMetadata>();
            return metadata != null ? metadata : source.gameObject.AddComponent<AudioSourceMetadata>();
        }

        private IEnumerator ReleaseSourceAfterPlayback(
            AudioChannelRuntime runtime,
            AudioSource source,
            int playbackVersion)
        {
            while (source != null && source.isPlaying)
                yield return null;

            if (source == null)
                yield break;

            AudioSourceMetadata metadata = source.GetComponent<AudioSourceMetadata>();
            if (metadata == null || metadata.playbackVersion != playbackVersion)
                yield break;

            source.clip = null;
            source.loop = false;
            metadata.clipVolume = 1f;
            metadata.sourceGain = 1f;

            if (metadata.isSpatial || !metadata.isInitial)
                DestroyRuntimeSource(runtime, source);
        }

        private void DestroyRuntimeSource(AudioChannelRuntime runtime, AudioSource source)
        {
            runtime.Sources.Remove(source);
            if (source != null)
                Destroy(source.gameObject);
        }

        private void StopChannelInternal(string channelId)
        {
            if (channelId == AudioChannelIds.Master)
            {
                foreach (AudioChannelRuntime channel in _channels.Values)
                {
                    if (!channel.Definition.IsMaster)
                        StopRuntimeSources(channel);
                }

                return;
            }

            if (TryGetRuntime(channelId, out AudioChannelRuntime runtime))
                StopRuntimeSources(runtime);
        }

        private void StopRuntimeSources(AudioChannelRuntime runtime)
        {
            var sources = new List<AudioSource>(runtime.Sources);
            foreach (AudioSource source in sources)
            {
                if (source == null)
                    continue;

                source.Stop();
                AudioSourceMetadata metadata = source.GetComponent<AudioSourceMetadata>();
                if (metadata != null && (metadata.isSpatial || !metadata.isInitial))
                {
                    DestroyRuntimeSource(runtime, source);
                    continue;
                }

                source.clip = null;
                source.loop = false;
                if (metadata != null)
                {
                    metadata.clipVolume = 1f;
                    metadata.sourceGain = 1f;
                }
            }
        }

        private void ForEachChannelSource(string channelId, Action<AudioSource> action)
        {
            if (channelId == AudioChannelIds.Master)
            {
                foreach (AudioChannelRuntime runtime in _channels.Values)
                {
                    if (!runtime.Definition.IsMaster)
                        ForEachRuntimeSource(runtime, action);
                }

                return;
            }

            if (TryGetRuntime(channelId, out AudioChannelRuntime channel))
                ForEachRuntimeSource(channel, action);
        }

        private static void ForEachRuntimeSource(AudioChannelRuntime runtime, Action<AudioSource> action)
        {
            foreach (AudioSource source in runtime.Sources)
            {
                if (source != null)
                    action(source);
            }
        }

        private IEnumerator FadeChannelGainCoroutine(AudioChannelRuntime runtime, float targetGain, float duration)
        {
            float startGain = runtime.FadeGain;
            if (duration <= 0f)
            {
                runtime.FadeGain = targetGain;
                UpdateRuntimeSourceVolumes(runtime);
                runtime.FadeCoroutine = null;
                yield break;
            }

            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.deltaTime;
                runtime.FadeGain = Mathf.Lerp(startGain, targetGain, timer / duration);
                UpdateRuntimeSourceVolumes(runtime);
                yield return null;
            }

            runtime.FadeGain = targetGain;
            runtime.FadeCoroutine = null;
            UpdateRuntimeSourceVolumes(runtime);
        }

        private void UpdateAllSourceVolumes()
        {
            foreach (AudioChannelRuntime runtime in _channels.Values)
                UpdateRuntimeSourceVolumes(runtime);
        }

        private void UpdateRuntimeSourceVolumes(AudioChannelRuntime runtime)
        {
            if (runtime.Definition.IsMaster)
            {
                UpdateAllNonMasterSourceVolumes();
                return;
            }

            foreach (AudioSource source in runtime.Sources)
            {
                if (source != null)
                    ApplySourceVolume(runtime, source);
            }
        }

        private void UpdateAllNonMasterSourceVolumes()
        {
            foreach (AudioChannelRuntime runtime in _channels.Values)
            {
                if (!runtime.Definition.IsMaster)
                    UpdateRuntimeSourceVolumes(runtime);
            }
        }

        private void ApplySourceVolume(AudioChannelRuntime runtime, AudioSource source)
        {
            AudioSourceMetadata metadata = GetOrAddMetadata(source);
            AudioChannelRuntime master = GetMasterRuntime();
            float masterGain = master.Enabled ? master.Volume * master.FadeGain : 0f;
            float channelGain = runtime.Enabled ? runtime.Volume * runtime.FadeGain : 0f;
            source.volume = masterGain * channelGain * metadata.clipVolume * metadata.sourceGain;
        }

        private void LoadVolumeSettingsInternal()
        {
            foreach (AudioChannelRuntime runtime in _channels.Values)
            {
                AudioChannelDefinition definition = runtime.Definition;
                runtime.Volume = definition.PersistSettings
                    ? Mathf.Clamp01(PlayerPrefs.GetFloat(
                        GetVolumePrefsKey(definition.Id),
                        GetLegacyVolumeDefault(definition)))
                    : definition.DefaultVolume;
                runtime.Enabled = definition.PersistSettings
                    ? PlayerPrefs.GetInt(
                        GetEnabledPrefsKey(definition.Id),
                        definition.EnabledByDefault ? 1 : 0) != 0
                    : definition.EnabledByDefault;
                runtime.FadeGain = 1f;
            }
        }

        private void SaveVolumeSettingsInternal()
        {
            foreach (AudioChannelRuntime runtime in _channels.Values)
            {
                if (!runtime.Definition.PersistSettings)
                    continue;

                PlayerPrefs.SetFloat(GetVolumePrefsKey(runtime.Definition.Id), runtime.Volume);
                PlayerPrefs.SetInt(GetEnabledPrefsKey(runtime.Definition.Id), runtime.Enabled ? 1 : 0);
            }

            PlayerPrefs.Save();
        }

        // Volumes saved by 1.x are read once as a fallback. New values are only written to the per-channel keys.
        private float GetLegacyVolumeDefault(AudioChannelDefinition definition)
        {
            string legacyKey = GetLegacyVolumePrefsKey(definition.Id);
            return string.IsNullOrEmpty(legacyKey)
                ? definition.DefaultVolume
                : PlayerPrefs.GetFloat(legacyKey, definition.DefaultVolume);
        }

        private bool TryGetRuntime(string channelId, out AudioChannelRuntime runtime)
        {
            if (_channels.TryGetValue(channelId ?? string.Empty, out runtime))
                return true;

            Debug.LogWarning($"Audio channel '{channelId}' is not configured.", audioLibrary);
            return false;
        }

        private AudioChannelRuntime GetMasterRuntime()
        {
            return _channels[AudioChannelIds.Master];
        }

        private static readonly Predicate<AudioSource> IsDestroyedSource = source => source == null;

        private static int CountPoolSources(AudioChannelRuntime runtime)
        {
            int count = 0;
            foreach (AudioSource source in runtime.Sources)
            {
                if (source == null)
                    continue;

                AudioSourceMetadata metadata = source.GetComponent<AudioSourceMetadata>();
                if (metadata != null && !metadata.isSpatial)
                    count++;
            }

            return count;
        }

        private static bool TryGetInstance(out AudioManager manager)
        {
            manager = _instance;
            if (manager != null && manager.audioLibrary != null)
                return true;

            Debug.LogError("AudioManager instance not found or its AudioLibrary is missing.");
            return false;
        }

        private static string GetVolumePrefsKey(string channelId)
        {
            return $"HarmonyAudio.Channel.{channelId}.Volume";
        }

        private static string GetEnabledPrefsKey(string channelId)
        {
            return $"HarmonyAudio.Channel.{channelId}.Enabled";
        }

        private static string GetLegacyVolumePrefsKey(string channelId)
        {
            if (channelId == AudioChannelIds.Master)
                return "MasterVolume";
            if (channelId == AudioChannelIds.Music)
                return "MusicVolume";
            if (channelId == AudioChannelIds.Sfx)
                return "SoundsVolume";
            if (channelId == AudioChannelIds.Voice)
                return "VoiceVolume";
            return null;
        }

        private static AudioClipWithVolume GetClipFromAsset(AudioAsset asset)
        {
            if (asset == null)
                return null;

            if (asset.allowMultipleClips)
            {
                if (asset.multipleClips.Count > 0)
                {
                    ClipWithVolume clip = asset.multipleClips[0];
                    return new AudioClipWithVolume(clip.clip, clip.volume);
                }
            }
            else if (asset.singleClip != null && asset.singleClip.clip != null)
            {
                return new AudioClipWithVolume(asset.singleClip.clip, asset.singleClip.volume);
            }

            return null;
        }

        private static AudioClipWithVolume GetRandomClipFromAsset(AudioAsset asset)
        {
            if (asset == null)
                return null;

            if (asset.allowMultipleClips && asset.multipleClips.Count > 0)
            {
                ClipWithVolume clip = asset.multipleClips[UnityEngine.Random.Range(0, asset.multipleClips.Count)];
                return new AudioClipWithVolume(clip.clip, clip.volume);
            }

            return GetClipFromAsset(asset);
        }

        private sealed class AudioChannelRuntime
        {
            public readonly AudioChannelDefinition Definition;
            public readonly List<AudioSource> Sources = new List<AudioSource>();
            public GameObject Root;
            public float Volume;
            public float FadeGain = 1f;
            public bool Enabled;
            public Coroutine FadeCoroutine;

            public AudioChannelRuntime(AudioChannelDefinition definition)
            {
                Definition = definition;
                Volume = definition.DefaultVolume;
                Enabled = definition.EnabledByDefault;
            }
        }
    }

    public class AudioClipWithVolume
    {
        public readonly AudioClip Clip;
        public readonly float Volume;

        public AudioClipWithVolume(AudioClip clip, float volume)
        {
            Clip = clip;
            Volume = volume;
        }
    }
}
