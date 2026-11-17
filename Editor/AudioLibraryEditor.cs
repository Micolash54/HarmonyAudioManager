using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyAudio.Scripts;
using UnityEditor;
using UnityEngine;

namespace HarmonyAudio.Editor
{
    [CustomEditor(typeof(AudioLibrary))]
    public class AudioLibraryEditor : UnityEditor.Editor
    {
        private SerializedProperty _channelsProp;
        private SerializedProperty _assetsProp;
        private SerializedProperty _enumGenerationPathProp;
        private string _newChannelName = "Ambience";
        private bool _channelsFoldout = true;
        private bool _assetsFoldout = true;

        private void OnEnable()
        {
            _channelsProp = serializedObject.FindProperty("channels");
            _assetsProp = serializedObject.FindProperty("assets");
            _enumGenerationPathProp = serializedObject.FindProperty("enumGenerationPath");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var library = (AudioLibrary)target;

            DrawMigration(library);
            DrawValidation(library);
            DrawChannels(library);
            DrawAssets();
            DrawEnumGeneration(library);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawMigration(AudioLibrary library)
        {
            if (!library.HasPendingLegacyUpgrade)
                return;

            EditorGUILayout.HelpBox(
                "This library contains legacy Music, Sound, or Voice lists. Upgrade them into the unified channel catalog.",
                MessageType.Warning);
            if (GUILayout.Button("Upgrade Legacy Library"))
            {
                Undo.RecordObject(library, "Upgrade HarmonyAudio Library");
                if (library.UpgradeLegacyData())
                {
                    MarkLibraryAndAssetsDirty(library);
                    AssetDatabase.SaveAssets();
                }
            }

            EditorGUILayout.Space();
        }

        private static void DrawValidation(AudioLibrary library)
        {
            foreach (string message in library.GetValidationMessages())
                EditorGUILayout.HelpBox(message, MessageType.Error);
        }

        private void DrawChannels(AudioLibrary library)
        {
            _channelsFoldout = EditorGUILayout.Foldout(_channelsFoldout, "Channels", true);
            if (!_channelsFoldout)
                return;

            EditorGUI.indentLevel++;
            for (int index = 0; index < _channelsProp.arraySize; index++)
                DrawChannel(library, index);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            _newChannelName = EditorGUILayout.TextField("New Channel", _newChannelName);
            if (GUILayout.Button("Add", GUILayout.Width(60f)))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(library, "Add HarmonyAudio Channel");
                library.AddChannel(_newChannelName);
                EditorUtility.SetDirty(library);
                serializedObject.Update();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
        }

        private void DrawChannel(AudioLibrary library, int index)
        {
            SerializedProperty channel = _channelsProp.GetArrayElementAtIndex(index);
            SerializedProperty id = channel.FindPropertyRelative("id");
            SerializedProperty displayName = channel.FindPropertyRelative("displayName");
            bool isBuiltIn = AudioChannelIds.IsBuiltIn(id.stringValue);
            bool isMaster = id.stringValue == AudioChannelIds.Master;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(displayName.stringValue) ? "Unnamed Channel" : displayName.stringValue,
                EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Stable ID", id.stringValue);
            }

            EditorGUILayout.PropertyField(displayName, new GUIContent("Display Name"));
            EditorGUILayout.PropertyField(channel.FindPropertyRelative("defaultVolume"));
            EditorGUILayout.PropertyField(channel.FindPropertyRelative("enabledByDefault"));
            EditorGUILayout.PropertyField(channel.FindPropertyRelative("persistSettings"));

            if (!isMaster)
            {
                SerializedProperty playbackMode = channel.FindPropertyRelative("playbackMode");
                EditorGUILayout.PropertyField(playbackMode);
                if ((AudioChannelPlaybackMode)playbackMode.enumValueIndex == AudioChannelPlaybackMode.Pooled)
                {
                    EditorGUILayout.PropertyField(channel.FindPropertyRelative("initialPoolSize"));
                    EditorGUILayout.PropertyField(channel.FindPropertyRelative("maxPoolSize"));
                }
            }

            if (!isBuiltIn && GUILayout.Button("Remove Channel"))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(library, "Remove HarmonyAudio Channel");
                library.RemoveChannel(id.stringValue);
                EditorUtility.SetDirty(library);
                serializedObject.Update();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawAssets()
        {
            _assetsFoldout = EditorGUILayout.Foldout(_assetsFoldout, "Unified Audio Assets", true);
            if (_assetsFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_assetsProp, true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
        }

        private void DrawEnumGeneration(AudioLibrary library)
        {
            EditorGUILayout.LabelField("Optional Enum Generation", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enumGenerationPathProp, new GUIContent("Output Folder"));
            EditorGUILayout.HelpBox(
                "Direct AudioAsset references and stable string IDs are the primary APIs. This generates one optional " +
                "AudioClips enum into your project. The output folder must be inside Assets.",
                MessageType.Info);

            if (GUILayout.Button("Regenerate Unified AudioClips Enum", GUILayout.Height(25f)))
            {
                serializedObject.ApplyModifiedProperties();
                GenerateUnifiedEnum(library);
            }
        }

        /// <summary>
        /// Writes AudioClips.cs into the library's output folder. Returns false when generation was aborted.
        /// Public so build scripts can call it.
        /// </summary>
        public static bool GenerateUnifiedEnum(AudioLibrary library)
        {
            List<string> validationMessages = library.GetValidationMessages();
            if (validationMessages.Count > 0)
            {
                Debug.LogError("HarmonyAudio enum generation aborted. Fix library validation errors first.", library);
                return false;
            }

            // The enum belongs to the consuming project. A package installed through the Package Manager is read-only.
            string directory = (library.EnumGenerationPath ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
            if (directory != "Assets" && !directory.StartsWith("Assets/"))
            {
                Debug.LogError(
                    $"HarmonyAudio enum generation aborted. Output folder '{library.EnumGenerationPath}' must be " +
                    $"inside Assets, for example '{AudioLibrary.DefaultEnumGenerationPath}'.",
                    library);
                return false;
            }

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var usedMembers = new HashSet<string>();
            var builder = new StringBuilder();
            builder.AppendLine("// Generated by Harmony Audio Manager from the Audio Library. Do not edit by hand.");
            builder.AppendLine("namespace HarmonyAudio.Scripts.Library");
            builder.AppendLine("{");
            builder.AppendLine("\tpublic enum AudioClips");
            builder.AppendLine("\t{");

            foreach (AudioAsset asset in library.Assets)
            {
                if (asset == null)
                    continue;

                string member = AudioIdUtility.ToEnumMember(asset.AssetId);
                if (!usedMembers.Add(member))
                {
                    Debug.LogError(
                        $"HarmonyAudio enum generation aborted. Asset ID '{asset.AssetId}' collides with another generated member '{member}'.",
                        asset);
                    return false;
                }

                builder.AppendLine($"\t\t{member},");
            }

            builder.AppendLine("\t}");
            builder.AppendLine("}");

            string filePath = directory + "/AudioClips.cs";
            File.WriteAllText(filePath, builder.ToString());
            AssetDatabase.Refresh();
            Debug.Log($"HarmonyAudio unified AudioClips enum regenerated at '{filePath}'.", library);
            return true;
        }

        private static void MarkLibraryAndAssetsDirty(AudioLibrary library)
        {
            EditorUtility.SetDirty(library);
            foreach (AudioAsset asset in library.Assets)
            {
                if (asset != null)
                    EditorUtility.SetDirty(asset);
            }
        }
    }
}
