using UnityEngine;

namespace HarmonyAudio.Scripts
{
    public class AudioSourceMetadata : MonoBehaviour
    {
        public bool isInitial;
        public bool isSpatial;
        public string channelId;
        public float clipVolume = 1f;
        public float sourceGain = 1f;
        public int playbackVersion;
    }
}
