using HarmonyAudio.Scripts;
using UnityEditor;
using UnityEngine;

namespace HarmonyAudio.Editor
{
    [CustomEditor(typeof(AudioManager))]
    public class AudioManagerEditor : UnityEditor.Editor
    {
        // Banner.png is located by GUID so it resolves whether the package lives in Assets or in Packages.
        private const string BannerGuid = "02f8f319adc5dfa4c951264cab0001ce";

        private Texture2D _headerImage;
        private bool _dashboardFoldout = true;
        private bool _channelsFoldout = true;

        private void OnEnable()
        {
            string bannerPath = AssetDatabase.GUIDToAssetPath(BannerGuid);
            _headerImage = string.IsNullOrEmpty(bannerPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(bannerPath);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_headerImage != null)
            {
                float inspectorWidth = EditorGUIUtility.currentViewWidth - 30f;
                GUILayout.Label(_headerImage, GUILayout.Width(inspectorWidth), GUILayout.Height(inspectorWidth / 3f));
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("audioLibrary"));
            serializedObject.ApplyModifiedProperties();

            var manager = (AudioManager)target;
            if (manager.Catalog == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign an Audio Library. Channels, assets, and enum generation settings live in that catalog.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Channel definitions are stored in the assigned Audio Library. Select the library to edit built-ins or add custom channels.",
                MessageType.Info);

            DrawDashboard(manager);

            if (!manager.HasPendingLegacySettings)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "This component was created with Harmony 1.x. Importing copies its old volume and pool settings " +
                "into the Audio Library and overwrites the Master, Music, SFX, and Voice channel defaults there.",
                MessageType.Warning);
            if (GUILayout.Button("Upgrade Legacy Manager Settings"))
            {
                Undo.RecordObject(manager, "Upgrade HarmonyAudio Manager Settings");
                Undo.RecordObject(manager.Catalog, "Upgrade HarmonyAudio Library");
                if (manager.UpgradeLegacyConfiguration())
                {
                    EditorUtility.SetDirty(manager);
                    EditorUtility.SetDirty(manager.Catalog);
                    foreach (AudioAsset asset in manager.Catalog.Assets)
                    {
                        if (asset != null)
                            EditorUtility.SetDirty(asset);
                    }
                    AssetDatabase.SaveAssets();
                }
            }
        }

        private void DrawDashboard(AudioManager manager)
        {
            AudioLibrary catalog = manager.Catalog;
            EditorGUILayout.Space();
            _dashboardFoldout = EditorGUILayout.Foldout(_dashboardFoldout, "Catalog Dashboard", true);
            if (!_dashboardFoldout)
                return;

            EditorGUI.indentLevel++;
            DrawReadOnlyLabel("Library", catalog.name);
            DrawReadOnlyLabel("Channels", catalog.Channels.Count.ToString());
            DrawReadOnlyLabel("Unified Assets", catalog.Assets.Count.ToString());
            DrawReadOnlyLabel("Legacy Library Upgrade", catalog.HasPendingLegacyUpgrade ? "Pending" : "Complete");
            DrawReadOnlyLabel(
                "Legacy Manager Settings",
                manager.HasPendingLegacySettings ? "Not imported" : "Nothing to import");

            SerializedProperty legacyMusicSource = serializedObject.FindProperty("musicSource");
            string legacySourceStatus = legacyMusicSource.objectReferenceValue != null
                ? "Attached (optional compatibility fallback)"
                : "Not attached (runtime source will be created)";
            DrawReadOnlyLabel("Legacy Music Source", legacySourceStatus);

            _channelsFoldout = EditorGUILayout.Foldout(_channelsFoldout, "Configured Channels", true);
            if (_channelsFoldout)
            {
                EditorGUI.indentLevel++;
                foreach (AudioChannelDefinition channel in catalog.Channels)
                {
                    if (channel != null)
                        DrawChannelSummary(catalog, channel);
                }
                EditorGUI.indentLevel--;
            }

            var validationMessages = catalog.GetValidationMessages();
            if (validationMessages.Count == 0)
            {
                EditorGUILayout.HelpBox("Catalog validation passed.", MessageType.Info);
            }
            else
            {
                foreach (string message in validationMessages)
                    EditorGUILayout.HelpBox(message, MessageType.Warning);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawChannelSummary(AudioLibrary catalog, AudioChannelDefinition channel)
        {
            int assignedAssets = 0;
            foreach (AudioAsset asset in catalog.Assets)
            {
                if (asset != null && asset.ChannelId == channel.Id)
                    assignedAssets++;
            }

            string behavior = channel.IsMaster
                ? "Global mix bus"
                : channel.PlaybackMode.ToString();
            string persistence = channel.PersistSettings ? "persistent" : "session only";
            string summary =
                $"{channel.DisplayName} ({channel.Id}): {behavior}, " +
                $"volume {channel.DefaultVolume:0.##}, {persistence}, {assignedAssets} assets";

            if (!channel.IsMaster && channel.PlaybackMode == AudioChannelPlaybackMode.Pooled)
                summary += $", pool {channel.InitialPoolSize}/{FormatMaxPoolSize(channel.MaxPoolSize)}";

            EditorGUILayout.LabelField(summary, EditorStyles.wordWrappedLabel);
        }

        private static string FormatMaxPoolSize(int maxPoolSize)
        {
            return maxPoolSize == 0 ? "unlimited" : maxPoolSize.ToString();
        }

        private static void DrawReadOnlyLabel(string label, string value)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField(label, value);
        }
    }
}
