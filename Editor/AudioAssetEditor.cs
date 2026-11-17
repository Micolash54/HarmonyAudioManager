using System.Collections.Generic;
using HarmonyAudio.Scripts;
using UnityEditor;
using UnityEngine;

namespace HarmonyAudio.Editor
{
    [CustomEditor(typeof(AudioAsset))]
    public class AudioAssetEditor : UnityEditor.Editor
    {
        private SerializedProperty _assetIdProp;
        private SerializedProperty _channelIdProp;
        private SerializedProperty _allowMultipleClipsProp;
        private SerializedProperty _singleClipProp;
        private SerializedProperty _multipleClipsProp;
        private SerializedProperty _useSpatialAudioProp;
        private SerializedProperty _spatialBlendProp;
        private SerializedProperty _rolloffModeProp;
        private SerializedProperty _minDistanceProp;
        private SerializedProperty _maxDistanceProp;

        private void OnEnable()
        {
            _assetIdProp = serializedObject.FindProperty("assetId");
            _channelIdProp = serializedObject.FindProperty("channelId");
            _allowMultipleClipsProp = serializedObject.FindProperty("allowMultipleClips");
            _singleClipProp = serializedObject.FindProperty("singleClip");
            _multipleClipsProp = serializedObject.FindProperty("multipleClips");
            _useSpatialAudioProp = serializedObject.FindProperty("useSpatialAudio");
            _spatialBlendProp = serializedObject.FindProperty("spatialBlend");
            _rolloffModeProp = serializedObject.FindProperty("rolloffMode");
            _minDistanceProp = serializedObject.FindProperty("minDistance");
            _maxDistanceProp = serializedObject.FindProperty("maxDistance");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawStableId();
            DrawChannelAssignment();

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_allowMultipleClipsProp);
            if (_allowMultipleClipsProp.boolValue)
                EditorGUILayout.PropertyField(_multipleClipsProp, true);
            else
                EditorGUILayout.PropertyField(_singleClipProp);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_useSpatialAudioProp);
            if (_useSpatialAudioProp.boolValue)
            {
                EditorGUILayout.PropertyField(_spatialBlendProp, new GUIContent("Spatial Blend"));
                EditorGUILayout.PropertyField(_rolloffModeProp, new GUIContent("Rolloff Mode"));
                EditorGUILayout.PropertyField(_minDistanceProp, new GUIContent("Min Distance"));
                EditorGUILayout.PropertyField(_maxDistanceProp, new GUIContent("Max Distance"));
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStableId()
        {
            EditorGUILayout.LabelField("Routing", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string nextId = EditorGUILayout.DelayedTextField("Asset ID", _assetIdProp.stringValue);
            if (EditorGUI.EndChangeCheck())
                _assetIdProp.stringValue = AudioIdUtility.NormalizeStableId(nextId);

            if (!AudioIdUtility.IsValidStableId(_assetIdProp.stringValue))
            {
                EditorGUILayout.HelpBox(
                    "Asset ID is required. Use lowercase letters, numbers, dots, underscores, or hyphens.",
                    MessageType.Error);
            }
        }

        private void DrawChannelAssignment()
        {
            AudioLibrary library = FindOwningLibrary();
            if (library == null)
            {
                EditorGUILayout.PropertyField(_channelIdProp, new GUIContent("Channel ID"));
                EditorGUILayout.HelpBox(
                    "Add this asset to an Audio Library to select its channel from a dropdown.",
                    MessageType.Info);
                return;
            }

            var labels = new List<string>();
            var ids = new List<string>();
            foreach (AudioChannelDefinition channel in library.Channels)
            {
                if (channel == null || !AudioChannelIds.IsRoutable(channel.Id))
                    continue;

                labels.Add($"{channel.DisplayName} ({channel.Id})");
                ids.Add(channel.Id);
            }

            if (ids.Count == 0)
            {
                EditorGUILayout.HelpBox("The owning library does not contain a routable channel.", MessageType.Error);
                return;
            }

            int selectedIndex = ids.IndexOf(_channelIdProp.stringValue);
            if (selectedIndex < 0)
            {
                labels.Insert(0, $"Missing channel ({_channelIdProp.stringValue})");
                ids.Insert(0, _channelIdProp.stringValue);
                selectedIndex = 0;
                EditorGUILayout.HelpBox(
                    "This asset routes to a missing channel. Choose a valid replacement.",
                    MessageType.Error);
            }

            int nextIndex = EditorGUILayout.Popup("Channel", selectedIndex, labels.ToArray());
            _channelIdProp.stringValue = ids[nextIndex];
        }

        private AudioLibrary FindOwningLibrary()
        {
            string[] libraryGuids = AssetDatabase.FindAssets("t:AudioLibrary");
            var audioAsset = (AudioAsset)target;
            foreach (string guid in libraryGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(path);
                if (library != null && Contains(library.Assets, audioAsset))
                    return library;
            }

            return null;
        }

        private static bool Contains(IReadOnlyList<AudioAsset> assets, AudioAsset targetAsset)
        {
            foreach (AudioAsset asset in assets)
            {
                if (asset == targetAsset)
                    return true;
            }

            return false;
        }
    }
}
