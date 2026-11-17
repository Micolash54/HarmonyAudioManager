/*
 * Harmony - Simple Audio Manager
 * Author: Niccolo Chiodo
 * Copyright 2024 Niccolo Chiodo
 *
 * View License in HarmonyAudio/LICENSE.md
 */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HarmonyAudio.Scripts
{
    [CreateAssetMenu(fileName = "Audio Library", menuName = "HarmonyAudio/New Audio Library")]
    public class AudioLibrary : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        public const string DefaultEnumGenerationPath = "Assets/HarmonyAudio/Generated";

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private string enumGenerationPath = DefaultEnumGenerationPath;
        [SerializeField] private List<AudioChannelDefinition> channels = new List<AudioChannelDefinition>();
        [SerializeField] private List<AudioAsset> assets = new List<AudioAsset>();

        // Retained for idempotent upgrades of libraries created before the channel model.
        [SerializeField, HideInInspector] public List<AudioAsset> musicAssets = new List<AudioAsset>();
        [SerializeField, HideInInspector] public List<AudioAsset> soundAssets = new List<AudioAsset>();
        [SerializeField, HideInInspector] public List<AudioAsset> voiceAssets = new List<AudioAsset>();

        private Dictionary<string, AudioChannelDefinition> _channelsById;
        private Dictionary<string, AudioAsset> _assetsById;
        private Dictionary<string, AudioAsset> _assetsByEnumMember;

        public IReadOnlyList<AudioChannelDefinition> Channels => channels;
        public IReadOnlyList<AudioAsset> Assets => assets;
        public string EnumGenerationPath
        {
            get => enumGenerationPath;
            set => enumGenerationPath = value;
        }

        public bool HasPendingLegacyUpgrade =>
            schemaVersion < CurrentSchemaVersion ||
            (assets.Count == 0 && (musicAssets.Count > 0 || soundAssets.Count > 0 || voiceAssets.Count > 0));

        private void OnEnable()
        {
            EnsureBuiltInChannels();
            RebuildLookups();
        }

        private void OnValidate()
        {
            EnsureBuiltInChannels();
            RebuildLookups();
        }

        public bool UpgradeLegacyData()
        {
            bool upgrading = schemaVersion < CurrentSchemaVersion;
            bool changed = EnsureBuiltInChannels();

            var legacyRoutes = new Dictionary<AudioAsset, string>();
            CollectLegacyRoutes(legacyRoutes, musicAssets, AudioChannelIds.Music);
            CollectLegacyRoutes(legacyRoutes, soundAssets, AudioChannelIds.Sfx);
            CollectLegacyRoutes(legacyRoutes, voiceAssets, AudioChannelIds.Voice);

            if (voiceAssets.Count > 0)
                changed |= EnsureVoicePreset();

            bool addedLegacyAssets = false;
            foreach (KeyValuePair<AudioAsset, string> route in legacyRoutes)
            {
                AudioAsset asset = route.Key;
                bool assetAdded = false;
                if (!assets.Contains(asset))
                {
                    assets.Add(asset);
                    assetAdded = true;
                    addedLegacyAssets = true;
                    changed = true;
                }

                if (upgrading || assetAdded)
                    changed |= asset.SetChannelId(route.Value);
            }

            changed |= EnsureAssetIds(upgrading || addedLegacyAssets);

            if (schemaVersion != CurrentSchemaVersion)
            {
                schemaVersion = CurrentSchemaVersion;
                changed = true;
            }

            RebuildLookups();
            return changed;
        }

        public bool EnsureBuiltInChannels()
        {
            bool changed = false;
            changed |= EnsureChannel(AudioChannelDefinition.Create(
                AudioChannelIds.Master,
                "Master",
                AudioChannelPlaybackMode.SingleSource));
            changed |= EnsureChannel(AudioChannelDefinition.Create(
                AudioChannelIds.Music,
                "Music",
                AudioChannelPlaybackMode.SingleSource));
            changed |= EnsureChannel(AudioChannelDefinition.Create(
                AudioChannelIds.Sfx,
                "SFX",
                AudioChannelPlaybackMode.Pooled,
                5,
                10));
            return changed;
        }

        public bool EnsureVoicePreset()
        {
            return EnsureChannel(AudioChannelDefinition.Create(
                AudioChannelIds.Voice,
                "Voice",
                AudioChannelPlaybackMode.Pooled,
                3,
                10));
        }

        public AudioChannelDefinition AddChannel(string displayName)
        {
            string baseId = AudioIdUtility.NormalizeStableId(displayName, "channel");
            string uniqueId = CreateUniqueChannelId(baseId);
            var channel = AudioChannelDefinition.Create(
                uniqueId,
                string.IsNullOrWhiteSpace(displayName) ? "New Channel" : displayName,
                AudioChannelPlaybackMode.Pooled,
                1,
                10);
            channels.Add(channel);
            RebuildLookups();
            return channel;
        }

        public bool RemoveChannel(string channelId)
        {
            if (AudioChannelIds.IsBuiltIn(channelId))
                return false;

            int removed = channels.RemoveAll(channel => channel != null && channel.Id == channelId);
            if (removed > 0)
                RebuildLookups();

            return removed > 0;
        }

        public bool ApplyLegacyManagerDefaults(
            float masterVolume,
            float musicVolume,
            float soundsVolume,
            int initialSoundPoolSize,
            int maxSoundPoolSize,
            bool enableVoice,
            float voiceVolume,
            int initialVoicePoolSize,
            int maxVoicePoolSize)
        {
            bool changed = EnsureBuiltInChannels();
            changed |= GetChannel(AudioChannelIds.Master).ApplyLegacyDefaults(masterVolume, 0, 0);
            changed |= GetChannel(AudioChannelIds.Music).ApplyLegacyDefaults(musicVolume, 0, 0);
            changed |= GetChannel(AudioChannelIds.Sfx).ApplyLegacyDefaults(
                soundsVolume,
                initialSoundPoolSize,
                maxSoundPoolSize);

            if (enableVoice)
            {
                changed |= EnsureVoicePreset();
                changed |= GetChannel(AudioChannelIds.Voice).ApplyLegacyDefaults(
                    voiceVolume,
                    initialVoicePoolSize,
                    maxVoicePoolSize);
            }

            RebuildLookups();
            return changed;
        }

        public AudioChannelDefinition GetChannel(string channelId)
        {
            EnsureLookups();
            if (_channelsById.TryGetValue(channelId ?? string.Empty, out AudioChannelDefinition channel))
                return channel;

            return null;
        }

        public AudioAsset GetAsset(string assetId)
        {
            EnsureLookups();
            string normalized = AudioIdUtility.NormalizeStableId(assetId);
            if (_assetsById.TryGetValue(normalized, out AudioAsset asset))
                return asset;

            Debug.LogWarning($"Audio asset '{assetId}' was not found in AudioLibrary '{name}'.");
            return null;
        }

        /// <summary>
        /// Resolves a member of the generated <c>AudioClips</c> enum to its asset by member name.
        /// </summary>
        public AudioAsset GetAsset<TEnum>(TEnum clipEnum) where TEnum : struct, Enum
        {
            EnsureLookups();
            string memberName = EnumMemberNames<TEnum>.Get(clipEnum);
            if (_assetsByEnumMember.TryGetValue(memberName, out AudioAsset asset))
                return asset;

            Debug.LogWarning(
                $"Audio asset enum '{memberName}' was not found in AudioLibrary '{name}'. " +
                "Regenerate the AudioClips enum if the library changed.");
            return null;
        }

        public List<string> GetValidationMessages()
        {
            EnsureBuiltInChannels();
            var messages = new List<string>();
            var channelIds = new HashSet<string>();
            var assetIds = new HashSet<string>();

            foreach (AudioChannelDefinition channel in channels)
            {
                if (channel == null)
                {
                    messages.Add("The channel list contains an empty entry.");
                    continue;
                }

                if (!AudioIdUtility.IsValidStableId(channel.Id))
                    messages.Add($"Channel '{channel.DisplayName}' has invalid ID '{channel.Id}'.");
                else if (!channelIds.Add(channel.Id))
                    messages.Add($"Channel ID '{channel.Id}' is duplicated.");
            }

            foreach (AudioAsset asset in assets)
            {
                if (asset == null)
                {
                    messages.Add("The unified asset list contains an empty entry.");
                    continue;
                }

                if (!AudioIdUtility.IsValidStableId(asset.AssetId))
                    messages.Add($"AudioAsset '{asset.name}' has invalid or empty ID '{asset.AssetId}'.");
                else if (!assetIds.Add(asset.AssetId))
                    messages.Add($"Audio asset ID '{asset.AssetId}' is duplicated.");

                if (asset.ChannelId == AudioChannelIds.Master)
                    messages.Add($"AudioAsset '{asset.name}' cannot route directly to the Master bus.");
                else if (!channelIds.Contains(asset.ChannelId))
                    messages.Add($"AudioAsset '{asset.name}' routes to missing channel '{asset.ChannelId}'.");
            }

            CollectLegacyRouteConflicts(messages);
            return messages;
        }

        private bool EnsureChannel(AudioChannelDefinition required)
        {
            foreach (AudioChannelDefinition channel in channels)
            {
                if (channel != null && channel.Id == required.Id)
                    return false;
            }

            channels.Add(required);
            return true;
        }

        private bool EnsureAssetIds(bool repairDuplicates)
        {
            bool changed = false;
            var ids = new HashSet<string>();
            foreach (AudioAsset asset in assets)
            {
                if (asset == null)
                    continue;

                string baseId = AudioIdUtility.NormalizeStableId(asset.AssetId, AudioIdUtility.NormalizeStableId(asset.name, "audio"));
                string nextId = baseId;
                if (string.IsNullOrEmpty(asset.AssetId) || (repairDuplicates && ids.Contains(nextId)))
                {
                    nextId = CreateUniqueAssetId(baseId, ids);
                    changed |= asset.SetStableId(nextId);
                }

                ids.Add(asset.AssetId);
            }

            return changed;
        }

        private string CreateUniqueChannelId(string baseId)
        {
            EnsureLookups();
            string candidate = baseId;
            int suffix = 2;
            while (_channelsById.ContainsKey(candidate))
            {
                candidate = $"{baseId}-{suffix}";
                suffix++;
            }

            return candidate;
        }

        private static string CreateUniqueAssetId(string baseId, HashSet<string> existingIds)
        {
            string candidate = baseId;
            int suffix = 2;
            while (existingIds.Contains(candidate))
            {
                candidate = $"{baseId}-{suffix}";
                suffix++;
            }

            return candidate;
        }

        private static void CollectLegacyRoutes(
            Dictionary<AudioAsset, string> routes,
            IEnumerable<AudioAsset> legacyAssets,
            string channelId)
        {
            foreach (AudioAsset asset in legacyAssets)
            {
                if (asset != null && !routes.ContainsKey(asset))
                    routes.Add(asset, channelId);
            }
        }

        private void CollectLegacyRouteConflicts(List<string> messages)
        {
            var routes = new Dictionary<AudioAsset, string>();
            AddLegacyRouteConflicts(messages, routes, musicAssets, AudioChannelIds.Music);
            AddLegacyRouteConflicts(messages, routes, soundAssets, AudioChannelIds.Sfx);
            AddLegacyRouteConflicts(messages, routes, voiceAssets, AudioChannelIds.Voice);
        }

        private static void AddLegacyRouteConflicts(
            List<string> messages,
            Dictionary<AudioAsset, string> routes,
            IEnumerable<AudioAsset> legacyAssets,
            string channelId)
        {
            foreach (AudioAsset asset in legacyAssets)
            {
                if (asset == null)
                    continue;

                if (routes.TryGetValue(asset, out string previousChannelId) && previousChannelId != channelId)
                {
                    messages.Add(
                        $"Legacy AudioAsset '{asset.name}' appears in both '{previousChannelId}' and '{channelId}' lists. " +
                        $"The first route will be used during migration.");
                    continue;
                }

                routes[asset] = channelId;
            }
        }

        private void EnsureLookups()
        {
            if (_channelsById == null || _assetsById == null || _assetsByEnumMember == null)
                RebuildLookups();
        }

        private void RebuildLookups()
        {
            _channelsById = new Dictionary<string, AudioChannelDefinition>(StringComparer.OrdinalIgnoreCase);
            _assetsById = new Dictionary<string, AudioAsset>(StringComparer.OrdinalIgnoreCase);
            _assetsByEnumMember = new Dictionary<string, AudioAsset>(StringComparer.Ordinal);

            foreach (AudioChannelDefinition channel in channels)
            {
                if (channel != null && !_channelsById.ContainsKey(channel.Id))
                    _channelsById.Add(channel.Id, channel);
            }

            foreach (AudioAsset asset in assets)
            {
                if (asset == null || string.IsNullOrEmpty(asset.AssetId))
                    continue;

                if (!_assetsById.ContainsKey(asset.AssetId))
                    _assetsById.Add(asset.AssetId, asset);

                string enumMember = AudioIdUtility.ToEnumMember(asset.AssetId);
                if (!_assetsByEnumMember.ContainsKey(enumMember))
                    _assetsByEnumMember.Add(enumMember, asset);
            }
        }

        // Caches enum member names per enum type so repeated lookups do not allocate.
        private static class EnumMemberNames<TEnum> where TEnum : struct, Enum
        {
            private static readonly Dictionary<TEnum, string> Names = new Dictionary<TEnum, string>();

            public static string Get(TEnum value)
            {
                if (!Names.TryGetValue(value, out string memberName))
                {
                    memberName = value.ToString();
                    Names[value] = memberName;
                }

                return memberName;
            }
        }
    }
}
