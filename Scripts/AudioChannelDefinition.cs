using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace HarmonyAudio.Scripts
{
    public static class AudioChannelIds
    {
        public const string Master = "master";
        public const string Music = "music";
        public const string Sfx = "sfx";
        public const string Voice = "voice";

        public static bool IsBuiltIn(string channelId)
        {
            return channelId == Master || channelId == Music || channelId == Sfx;
        }

        public static bool IsRoutable(string channelId)
        {
            return !string.IsNullOrEmpty(channelId) && channelId != Master;
        }
    }

    public enum AudioChannelPlaybackMode
    {
        SingleSource,
        Pooled
    }

    [Serializable]
    public class AudioChannelDefinition
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField, Range(0f, 1f)] private float defaultVolume = 1f;
        [SerializeField] private bool enabledByDefault = true;
        [SerializeField] private bool persistSettings = true;
        [SerializeField] private int initialPoolSize;
        [SerializeField] private int maxPoolSize;
        [SerializeField] private AudioChannelPlaybackMode playbackMode;

        public string Id => id;
        public string DisplayName => displayName;
        public float DefaultVolume => defaultVolume;
        public bool EnabledByDefault => enabledByDefault;
        public bool PersistSettings => persistSettings;
        public int InitialPoolSize => Mathf.Max(0, initialPoolSize);
        public int MaxPoolSize => Mathf.Max(0, maxPoolSize);
        public AudioChannelPlaybackMode PlaybackMode => playbackMode;
        public bool IsMaster => id == AudioChannelIds.Master;
        public bool IsBuiltIn => AudioChannelIds.IsBuiltIn(id);

        public static AudioChannelDefinition Create(
            string id,
            string displayName,
            AudioChannelPlaybackMode playbackMode,
            int initialPoolSize = 0,
            int maxPoolSize = 0)
        {
            return new AudioChannelDefinition
            {
                id = id,
                displayName = displayName,
                playbackMode = playbackMode,
                initialPoolSize = Mathf.Max(0, initialPoolSize),
                maxPoolSize = Mathf.Max(0, maxPoolSize)
            };
        }

        internal bool ApplyLegacyDefaults(float volume, int initialSize, int maxSize)
        {
            bool changed = false;
            changed |= SetIfDifferent(ref defaultVolume, Mathf.Clamp01(volume));
            changed |= SetIfDifferent(ref initialPoolSize, Mathf.Max(0, initialSize));
            changed |= SetIfDifferent(ref maxPoolSize, Mathf.Max(0, maxSize));
            return changed;
        }

        private static bool SetIfDifferent(ref float current, float next)
        {
            if (Mathf.Approximately(current, next))
                return false;

            current = next;
            return true;
        }

        private static bool SetIfDifferent(ref int current, int next)
        {
            if (current == next)
                return false;

            current = next;
            return true;
        }
    }

    public static class AudioIdUtility
    {
        private static readonly Regex InvalidStableIdCharacters = new Regex(
            @"[^a-z0-9._-]+",
            RegexOptions.Compiled);

        public static string NormalizeStableId(string value, string fallback = "")
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            string normalized = value.Trim().ToLowerInvariant().Replace(' ', '-');
            normalized = InvalidStableIdCharacters.Replace(normalized, "-");
            normalized = normalized.Trim('-', '.', '_');
            return string.IsNullOrEmpty(normalized) ? fallback : normalized;
        }

        public static bool IsValidStableId(string value)
        {
            return !string.IsNullOrEmpty(value) && NormalizeStableId(value) == value;
        }

        public static string ToEnumMember(string stableId)
        {
            if (string.IsNullOrEmpty(stableId))
                return "Unknown";

            var builder = new StringBuilder();
            bool capitalizeNext = true;
            foreach (char character in stableId)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    capitalizeNext = true;
                    continue;
                }

                builder.Append(capitalizeNext ? char.ToUpperInvariant(character) : character);
                capitalizeNext = false;
            }

            if (builder.Length == 0)
                return "Unknown";

            if (char.IsDigit(builder[0]))
                builder.Insert(0, '_');

            return builder.ToString();
        }
    }
}
