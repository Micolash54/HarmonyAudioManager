/*
 * Harmony - Simple Audio Manager
 * Author: Niccolò Chiodo
 * Copyright © 2024 Niccolò Chiodo
 * 
 * View License in HarmonyAudio/LICENSE.md
 * 
 * Description:
 * A scriptable object that holds a single or multiple audio clips for assignment in the library.
 */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HarmonyAudio.Scripts
{
    [CreateAssetMenu(fileName = "Audio Asset", menuName = "HarmonyAudio/New Audio Asset")]
    public class AudioAsset : ScriptableObject
    {
        [SerializeField] private string assetId;
        [SerializeField] private string channelId = AudioChannelIds.Sfx;

        public bool allowMultipleClips = false;

        [Tooltip("Single Audio Clip with Volume")]
        public ClipWithVolume singleClip;

        [Tooltip("List of Audio Clips with individual volumes")]
        public List<ClipWithVolume> multipleClips = new List<ClipWithVolume>();
        
        // Spatial audio settings
        public bool useSpatialAudio = false;
        [Range(0f, 1f)]
        public float spatialBlend = 1f; // 0 = 2D, 1 = 3D
        public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
        public float minDistance = 1f;
        public float maxDistance = 100f;

        public string AssetId => assetId;
        public string ChannelId => channelId;

        private void OnValidate()
        {
            assetId = AudioIdUtility.NormalizeStableId(assetId, AudioIdUtility.NormalizeStableId(name, "audio"));
            channelId = AudioIdUtility.NormalizeStableId(channelId, AudioChannelIds.Sfx);
        }

        public bool EnsureStableId(string suggestedId)
        {
            if (!string.IsNullOrEmpty(assetId))
                return false;

            assetId = AudioIdUtility.NormalizeStableId(suggestedId, "audio");
            return true;
        }

        public bool SetStableId(string value)
        {
            string normalized = AudioIdUtility.NormalizeStableId(value);
            if (assetId == normalized)
                return false;

            assetId = normalized;
            return true;
        }

        public bool SetChannelId(string value)
        {
            string normalized = AudioIdUtility.NormalizeStableId(value, AudioChannelIds.Sfx);
            if (channelId == normalized)
                return false;

            channelId = normalized;
            return true;
        }
    }
    
    [Serializable]
    public class ClipWithVolume
    {
        public AudioClip clip;
        [Range(0f, 1f)]
        public float volume = 1f;
    }
}
