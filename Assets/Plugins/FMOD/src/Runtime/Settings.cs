﻿#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FMOD;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;
#if UNITY_2018_1_OR_NEWER
using UnityEditor.Build.Reporting;
#endif

#endif

namespace FMODUnity
{
    [Serializable]
    public enum ImportType
    {
        StreamingAssets,
        AssetBundle
    }

    [Serializable]
    public enum BankLoadType
    {
        All,
        Specified,
        None
    }

    [Serializable]
    public enum MeterChannelOrderingType
    {
        Standard,
        SeparateLFE,
        Positional
    }

    public enum TriStateBool
    {
        Disabled,
        Enabled,
        Development
    }

    // This class stores all of the FMOD for Unity cross-platform settings, as well as a collection
    // of Platform objects that hold the platform-specific settings. The Platform objects are stored
    // in the same asset as the Settings object using AssetDatabase.AddObjectToAsset.
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public class Settings : ScriptableObject
    {
#if UNITY_EDITOR
        [SerializeField] private bool SwitchSettingsMigration;
#endif

        private const string SettingsAssetName = "FMODStudioSettings";

        private static Settings instance;
        private static bool isInitializing;

        public static Settings Instance
        {
            get
            {
                if (isInitializing) return null;

                if (instance == null)
                {
                    isInitializing = true;
                    instance = Resources.Load(SettingsAssetName) as Settings;
                    if (instance == null)
                    {
                        Debug.Log("[FMOD] Cannot find integration settings, creating default settings");
                        instance = CreateInstance<Settings>();
                        instance.name = "FMOD Studio Integration Settings";

#if UNITY_EDITOR
                        if (!Directory.Exists("Assets/Plugins/FMOD/Resources"))
                            AssetDatabase.CreateFolder("Assets/Plugins/FMOD", "Resources");
                        AssetDatabase.CreateAsset(instance,
                            "Assets/Plugins/FMOD/Resources/" + SettingsAssetName + ".asset");

                        instance.AddPlatformsToAsset();
#endif
                    }

                    isInitializing = false;
                }

                return instance;
            }
        }

#if UNITY_EDITOR
        [MenuItem("FMOD/Edit Settings", priority = 0)]
        public static void EditSettings()
        {
            Selection.activeObject = Instance;
#if UNITY_2018_2_OR_NEWER
            EditorApplication.ExecuteMenuItem("Window/General/Inspector");
#else
            EditorApplication.ExecuteMenuItem("Window/Inspector");
#endif
        }
#endif

        [SerializeField] public bool HasSourceProject = true;

        [SerializeField] public bool HasPlatforms = true;

        [SerializeField] private string sourceProjectPath;

        public string SourceProjectPath
        {
            get => sourceProjectPath;
            set => sourceProjectPath = value;
        }

        [SerializeField] private string sourceBankPath;

        public string SourceBankPath
        {
            get => sourceBankPath;
            set => sourceBankPath = value;
        }

        [SerializeField] public string SourceBankPathUnformatted; // Kept as to not break existing projects

        [SerializeField] public int BankRefreshCooldown = 5;

        [SerializeField] public bool ShowBankRefreshWindow = true;

        public const int BankRefreshPrompt = -1;
        public const int BankRefreshManual = -2;

        [SerializeField] public bool AutomaticEventLoading;

        [SerializeField] public BankLoadType BankLoadType;

        [SerializeField] public bool AutomaticSampleLoading;

        [SerializeField] public string EncryptionKey;

        [SerializeField] public ImportType ImportType;

        public string TargetPath
        {
            get
            {
                if (ImportType == ImportType.AssetBundle)
                {
                    if (string.IsNullOrEmpty(TargetAssetPath))
                        return Application.dataPath;
                    return Application.dataPath + "/" + TargetAssetPath;
                }

                if (string.IsNullOrEmpty(TargetBankFolder))
                    return Application.streamingAssetsPath;
                return Application.streamingAssetsPath + "/" + TargetBankFolder;
            }
        }

        public string TargetSubFolder
        {
            get
            {
                if (ImportType == ImportType.AssetBundle)
                    return TargetAssetPath;
                return TargetBankFolder;
            }
            set
            {
                if (ImportType == ImportType.AssetBundle)
                {
                    TargetAssetPath = value;
                    ;
                }
                else
                {
                    TargetBankFolder = value;
                }
            }
        }

        [SerializeField] public string TargetAssetPath = "FMODBanks";

        [SerializeField] public string TargetBankFolder = "";

        [SerializeField] public DEBUG_FLAGS LoggingLevel = DEBUG_FLAGS.WARNING;

        [SerializeField] public List<Legacy.PlatformIntSetting> SpeakerModeSettings;

        [SerializeField] public List<Legacy.PlatformIntSetting> SampleRateSettings;

        [SerializeField] public List<Legacy.PlatformBoolSetting> LiveUpdateSettings;

        [SerializeField] public List<Legacy.PlatformBoolSetting> OverlaySettings;

        [SerializeField] public List<Legacy.PlatformBoolSetting> LoggingSettings;

        [SerializeField] public List<Legacy.PlatformStringSetting> BankDirectorySettings;

        [SerializeField] public List<Legacy.PlatformIntSetting> VirtualChannelSettings;

        [SerializeField] public List<Legacy.PlatformIntSetting> RealChannelSettings;

        [SerializeField] public List<string> Plugins = new List<string>();

        [SerializeField] public List<string> MasterBanks;

        [SerializeField] public List<string> Banks;

        [SerializeField] public List<string> BanksToLoad;

        [SerializeField] public ushort LiveUpdatePort = 9264;

        [SerializeField] public bool EnableMemoryTracking;

        [SerializeField] public bool AndroidUseOBB;

        [SerializeField] public MeterChannelOrderingType MeterChannelOrdering;

        [SerializeField] public bool StopEventsOutsideMaxDistance;

        [SerializeField] public bool BoltUnitOptionsBuildPending;

        // This holds all known platforms, but only those that have settings are shown in the UI.
        // It is populated at load time from the Platform objects in the settings asset.
        // It is serializable to facilitate undo support.
        [SerializeField] private List<Platform> Platforms = new List<Platform>();

        public Platform FindPlatform(string identifier)
        {
            foreach (var platform in Platforms)
                if (platform.Identifier == identifier)
                    return platform;

            return null;
        }

        public bool PlatformExists(string identifier)
        {
            return FindPlatform(identifier) != null;
        }

        public void ForEachPlatform(Action<Platform> action)
        {
            foreach (var platform in Platforms) action(platform);
        }

        public IEnumerable<Platform> EnumeratePlatforms()
        {
            return Platforms;
        }

        private void AddPlatform(Platform platform)
        {
            if (PlatformExists(platform.Identifier))
                throw new ArgumentException(string.Format("Duplicate platform identifier: {0}", platform.Identifier));

            Platforms.Add(platform);
        }

        public void RemovePlatform(string identifier)
        {
            Platforms.RemoveAll(p => p.Identifier == identifier);
        }

#if UNITY_EDITOR
        // This is used to find the platform that implements the current Unity build target.
        private readonly Dictionary<BuildTarget, Platform> PlatformForBuildTarget =
            new Dictionary<BuildTarget, Platform>();
#endif

        // This is used to find the platform that matches the current Unity runtime platform.
        private readonly Dictionary<RuntimePlatform, List<Platform>> PlatformForRuntimePlatform =
            new Dictionary<RuntimePlatform, List<Platform>>();

        // Default platform settings.

        // Play In Editor platform settings.

        [field: NonSerialized] public Platform DefaultPlatform { get; private set; }

        [field: NonSerialized] public Platform PlayInEditorPlatform { get; private set; }

#if UNITY_EDITOR
        // Adds a new platform group to the set of platforms.
        public PlatformGroup AddPlatformGroup(string displayName, int sortOrder)
        {
            var group = PlatformGroup.Create(displayName, Legacy.Platform.None);
            group.DisplaySortOrder = sortOrder;

            AddPlatform(group);
            AssetDatabase.AddObjectToAsset(group, this);

            LinkPlatform(group);

            return group;
        }
#endif

        // Links the platform to its parent, and to the BuildTargets and RuntimePlatforms it implements.
        private void LinkPlatform(Platform platform)
        {
            LinkPlatformToParent(platform);

            platform.DeclareUnityMappings(this);
        }

#if UNITY_EDITOR
        public void DeclareBuildTarget(BuildTarget buildTarget, Platform platform)
        {
            PlatformForBuildTarget.Add(buildTarget, platform);
        }
#endif

        public void DeclareRuntimePlatform(RuntimePlatform runtimePlatform, Platform platform)
        {
            List<Platform> platforms;

            if (!PlatformForRuntimePlatform.TryGetValue(runtimePlatform, out platforms))
            {
                platforms = new List<Platform>();
                PlatformForRuntimePlatform.Add(runtimePlatform, platforms);
            }

            platforms.Add(platform);

            // Highest priority goes first
            platforms.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

#if UNITY_EDITOR
        private void ClearPlatformSettings()
        {
            RemovePlatformFromAsset(DefaultPlatform);
            RemovePlatformFromAsset(PlayInEditorPlatform);

            ForEachPlatform(RemovePlatformFromAsset);

            foreach (var platform in Resources.LoadAll<Platform>(SettingsAssetName)) RemovePlatformFromAsset(platform);

            DefaultPlatform = null;
            PlayInEditorPlatform = null;

            Platforms.Clear();
            PlatformForBuildTarget.Clear();
            PlatformForRuntimePlatform.Clear();
        }

        // Testing function: Resets all platform settings.
        public void ResetPlatformSettings()
        {
            ClearPlatformSettings();
            OnEnable();
        }

        // Testing function: Reimports legacy platform settings.
        public void ReimportLegacyPlatforms()
        {
            ClearPlatformSettings();
            MigratedPlatforms.Clear();
            OnEnable();
        }

        // We store a persistent list so we don't try to re-migrate platforms if the user deletes them.
        [SerializeField] private List<Legacy.Platform> MigratedPlatforms = new List<Legacy.Platform>();

        private void UpdateMigratedPlatforms()
        {
            ForEachPlatform(platform =>
            {
                if (!MigratedPlatforms.Contains(platform.LegacyIdentifier))
                    MigratedPlatforms.Add(platform.LegacyIdentifier);
            });
        }

        // Adds any missing platforms:
        // * From the template collection
        // * From the legacy settings
        private void AddMissingPlatforms()
        {
            var newPlatforms = new List<Platform>();

            foreach (var template in platformTemplates)
                if (!PlatformExists(template.Identifier))
                    newPlatforms.Add(template.CreateInstance());

            // Ensure that the default platform exists
            if (!DefaultPlatform)
            {
                DefaultPlatform = CreateInstance<PlatformDefault>();
                newPlatforms.Add(DefaultPlatform);
            }

            // Ensure that the Play In Editor platform exists
            if (!PlayInEditorPlatform)
            {
                PlayInEditorPlatform = CreateInstance<PlatformPlayInEditor>();
                newPlatforms.Add(PlayInEditorPlatform);
            }

            // Ensure that the default and Play In Editor platforms have properties
            AffirmPlatformProperties(DefaultPlatform);
            AffirmPlatformProperties(PlayInEditorPlatform);

            // Migrate plugins if necessary
            var PluginsProperty = Platform.PropertyAccessors.Plugins;

            if (!MigratedPlatforms.Contains(DefaultPlatform.LegacyIdentifier))
                PluginsProperty.Set(DefaultPlatform, Plugins);
            else if (!PluginsProperty.HasValue(DefaultPlatform))
                PluginsProperty.Set(DefaultPlatform, new List<string>());

            // Migrate LiveUpdatePort
            if (!Platform.PropertyAccessors.LiveUpdatePort.HasValue(DefaultPlatform))
                Platform.PropertyAccessors.LiveUpdatePort.Set(DefaultPlatform, LiveUpdatePort);

            // Create a map for migrating legacy settings
            var platformMap = new Dictionary<Legacy.Platform, Platform>();

            foreach (var platform in Platforms.Concat(newPlatforms))
                if (platform.LegacyIdentifier != Legacy.Platform.None)
                    platformMap.Add(platform.LegacyIdentifier, platform);

            Func<Legacy.Platform, Platform> AffirmPlatform = null;

            // Ensures that all of the platform's ancestors exist.
            Action<Platform> AffirmAncestors = platform =>
            {
                var legacyParent = Legacy.Parent(platform.LegacyIdentifier);

                if (legacyParent != Legacy.Platform.None)
                    platform.ParentIdentifier = AffirmPlatform(legacyParent).Identifier;
            };

            // Gets the platform corresponding to legacyPlatform (or creates it if it is a group),
            // and ensures that it has properties and all of its ancestors exist.
            // Returns null if legacyPlatform is unknown.
            AffirmPlatform = legacyPlatform =>
            {
                Platform platform;

                if (platformMap.TryGetValue(legacyPlatform, out platform))
                {
                    platform.AffirmProperties();
                }
                else if (Legacy.IsGroup(legacyPlatform))
                {
                    var group = PlatformGroup.Create(Legacy.DisplayName(legacyPlatform), legacyPlatform);
                    platformMap.Add(legacyPlatform, group);
                    newPlatforms.Add(group);

                    platform = group;
                }
                else
                {
                    // This is an unknown platform
                    return null;
                }

                AffirmAncestors(platform);

                return platform;
            };

            // Gets the target plaform to use when migrating settings from legacyPlatform.
            // Returns null if legacyPlatform is unknown or has already been migrated.
            Func<Legacy.Platform, Platform> getMigrationTarget = legacyPlatform =>
            {
                if (MigratedPlatforms.Contains(legacyPlatform))
                    // Already migrated
                    return null;

                return AffirmPlatform(legacyPlatform);
            };

            var speakerModeSettings = SpeakerModeSettings.ConvertAll(
                setting => new Legacy.PlatformSetting<SPEAKERMODE>
                {
                    Value = (SPEAKERMODE) setting.Value,
                    Platform = setting.Platform
                }
            );

            // Migrate all the legacy settings, creating platforms as we need them via AffirmPlatform
            MigrateLegacyPlatforms(speakerModeSettings, Platform.PropertyAccessors.SpeakerMode, getMigrationTarget);
            MigrateLegacyPlatforms(SampleRateSettings, Platform.PropertyAccessors.SampleRate, getMigrationTarget);
            MigrateLegacyPlatforms(LiveUpdateSettings, Platform.PropertyAccessors.LiveUpdate, getMigrationTarget);
            MigrateLegacyPlatforms(OverlaySettings, Platform.PropertyAccessors.Overlay, getMigrationTarget);
            MigrateLegacyPlatforms(BankDirectorySettings, Platform.PropertyAccessors.BuildDirectory,
                getMigrationTarget);
            MigrateLegacyPlatforms(VirtualChannelSettings, Platform.PropertyAccessors.VirtualChannelCount,
                getMigrationTarget);
            MigrateLegacyPlatforms(RealChannelSettings, Platform.PropertyAccessors.RealChannelCount,
                getMigrationTarget);

            // Now we ensure that if a legacy group has settings, all of its descendants exist
            // and inherit from it (even if they have no settings of their own), so that the
            // inheritance structure matches the old system.
            // We look at all groups (not just newly created ones), because a newly created platform
            // may need to inherit from a preexisting group.
            var groupsToProcess = new Queue<Platform>(platformMap.Values.Where(
                platform => platform is PlatformGroup
                            && platform.LegacyIdentifier != Legacy.Platform.None
                            && platform.HasAnyOverriddenProperties));

            while (groupsToProcess.Count > 0)
            {
                var group = groupsToProcess.Dequeue();

                // Ensure that all descendants exist
                foreach (var child in platformMap.Values)
                {
                    if (child.Active)
                        // Don't overwrite existing settings
                        continue;

                    var legacyPlatform = child.LegacyIdentifier;

                    if (legacyPlatform == Legacy.Platform.iOS || legacyPlatform == Legacy.Platform.Android)
                        // These platforms were overridden by MobileHigh and MobileLow in the old system
                        continue;

                    if (MigratedPlatforms.Contains(legacyPlatform))
                        // The user may have deleted this platform since migration, so don't mess with it
                        continue;

                    if (Legacy.Parent(legacyPlatform) == group.LegacyIdentifier)
                    {
                        child.AffirmProperties();
                        child.ParentIdentifier = group.Identifier;

                        if (child is PlatformGroup) groupsToProcess.Enqueue(child as PlatformGroup);
                    }
                }
            }

            // Add all of the new platforms to the set of known platforms
            foreach (var platform in newPlatforms) AddPlatform(platform);

            UpdateMigratedPlatforms();
        }

        private void MigrateLegacyPlatforms<TValue, TSetting>(List<TSetting> settings,
            Platform.PropertyAccessor<TValue> property, Func<Legacy.Platform, Platform> getMigrationTarget)
            where TSetting : Legacy.PlatformSetting<TValue>
        {
            foreach (var setting in settings)
            {
                var platform = getMigrationTarget(setting.Platform);

                if (platform != null) property.Set(platform, setting.Value);
            }
        }

        // The platform that implements the current Unity build target.
        public Platform CurrentEditorPlatform
        {
            get
            {
                if (PlatformForBuildTarget.ContainsKey(EditorUserBuildSettings.activeBuildTarget))
                    return PlatformForBuildTarget[EditorUserBuildSettings.activeBuildTarget];
                return DefaultPlatform;
            }
        }
#endif

        // Links the given platform to its parent, if it has one.
        private void LinkPlatformToParent(Platform platform)
        {
            if (!string.IsNullOrEmpty(platform.ParentIdentifier))
                SetPlatformParent(platform, FindPlatform(platform.ParentIdentifier));
        }

        // The highest-priority platform that matches the current environment.
        public Platform FindCurrentPlatform()
        {
            List<Platform> platforms;

            if (PlatformForRuntimePlatform.TryGetValue(Application.platform, out platforms))
                foreach (var platform in platforms)
                    if (platform.MatchesCurrentEnvironment)
                        return platform;

            return DefaultPlatform;
        }

        public SPEAKERMODE GetEditorSpeakerMode()
        {
            return PlayInEditorPlatform.SpeakerMode;
        }

        private Settings()
        {
            MasterBanks = new List<string>();
            Banks = new List<string>();
            BanksToLoad = new List<string>();
            RealChannelSettings = new List<Legacy.PlatformIntSetting>();
            VirtualChannelSettings = new List<Legacy.PlatformIntSetting>();
            LoggingSettings = new List<Legacy.PlatformBoolSetting>();
            LiveUpdateSettings = new List<Legacy.PlatformBoolSetting>();
            OverlaySettings = new List<Legacy.PlatformBoolSetting>();
            SampleRateSettings = new List<Legacy.PlatformIntSetting>();
            SpeakerModeSettings = new List<Legacy.PlatformIntSetting>();
            BankDirectorySettings = new List<Legacy.PlatformStringSetting>();

            ImportType = ImportType.StreamingAssets;
            AutomaticEventLoading = true;
            AutomaticSampleLoading = false;
            EnableMemoryTracking = false;
        }

        // Adds properties to a platform, thus revealing it in the UI.
        public void AddPlatformProperties(Platform platform)
        {
            platform.AffirmProperties();
            LinkPlatformToParent(platform);
        }

#if UNITY_EDITOR
        public void SetPlatformParent(Platform platform, Platform newParent)
        {
            var oldParent = FindPlatform(platform.ParentIdentifier);

            if (oldParent != null) oldParent.ChildIdentifiers.Remove(platform.Identifier);

            if (newParent != null)
            {
                platform.ParentIdentifier = newParent.Identifier;

                newParent.ChildIdentifiers.Add(platform.Identifier);
                SortPlatformChildren(newParent);
            }
            else
            {
                platform.ParentIdentifier = null;
            }
        }

        public void SetPlatformSortOrder(Platform platform, float sortOrder)
        {
            if (platform.DisplaySortOrder != sortOrder)
            {
                platform.DisplaySortOrder = sortOrder;

                if (platform.Parent != null) SortPlatformChildren(platform.Parent);
            }
        }

        public void SortPlatformChildren(Platform platform)
        {
            platform.ChildIdentifiers.Sort((a, b) =>
            {
                var platformA = FindPlatform(a);
                var platformB = FindPlatform(b);

                return platformA.DisplaySortOrder.CompareTo(platformB.DisplaySortOrder);
            });
        }

        // Ensures that the given platform has valid properties.
        private void AffirmPlatformProperties(Platform platform)
        {
            if (!platform.Active)
            {
                Debug.LogFormat("[FMOD] Cannot find properties for platform {0}, creating default properties",
                    platform.Identifier);
                AddPlatformProperties(platform);
            }
        }
#else
        public void SetPlatformParent(Platform platform, Platform newParent)
        {
            platform.Parent = newParent;
        }
#endif

        // A template for constructing a platform from an identifier.
        private struct PlatformTemplate
        {
            public string Identifier;
            public Func<Platform> CreateInstance;
        }

        // A collection of templates for constructing known platforms.
        private static readonly List<PlatformTemplate> platformTemplates = new List<PlatformTemplate>();

        // Adds a platform to the collection of templates. Platforms register themselves by using
        // [InitializeOnLoad] and calling this function from a static constructor.
        public static void AddPlatformTemplate<T>(string identifier) where T : Platform
        {
            platformTemplates.Add(new PlatformTemplate
            {
                Identifier = identifier,
                CreateInstance = () => CreatePlatformInstance<T>(identifier)
            });
        }

        private static Platform CreatePlatformInstance<T>(string identifier) where T : Platform
        {
            Platform platform = CreateInstance<T>();
            platform.InitializeProperties();
            platform.Identifier = identifier;

            return platform;
        }

        private void OnEnable()
        {
            PopulatePlatformsFromAsset();

            DefaultPlatform = Platforms.FirstOrDefault(platform => platform is PlatformDefault);
            PlayInEditorPlatform = Platforms.FirstOrDefault(platform => platform is PlatformPlayInEditor);

#if UNITY_EDITOR
            if (SwitchSettingsMigration == false)
            {
                // Create Switch settings from the legacy Mobile settings, if they exist
                Legacy.CopySetting(LoggingSettings, Legacy.Platform.Mobile, Legacy.Platform.Switch);
                Legacy.CopySetting(LiveUpdateSettings, Legacy.Platform.Mobile, Legacy.Platform.Switch);
                Legacy.CopySetting(OverlaySettings, Legacy.Platform.Mobile, Legacy.Platform.Switch);

                Legacy.CopySetting(RealChannelSettings, Legacy.Platform.Mobile, Legacy.Platform.Switch);
                Legacy.CopySetting(VirtualChannelSettings, Legacy.Platform.Mobile, Legacy.Platform.Switch);
                Legacy.CopySetting(SampleRateSettings, Legacy.Platform.Mobile, Legacy.Platform.Switch);
                Legacy.CopySetting(SpeakerModeSettings, Legacy.Platform.Mobile, Legacy.Platform.Switch);
                SwitchSettingsMigration = true;
            }

            // Fix up slashes for old settings meta data.
            sourceProjectPath = RuntimeUtils.GetCommonPlatformPath(sourceProjectPath);
            SourceBankPathUnformatted = RuntimeUtils.GetCommonPlatformPath(SourceBankPathUnformatted);

            // Remove the FMODStudioCache if in the old location
            var oldCache = "Assets/Plugins/FMOD/Resources/FMODStudioCache.asset";
            if (File.Exists(oldCache)) AssetDatabase.DeleteAsset(oldCache);

            AddMissingPlatforms();

            // Add all known platforms to the settings asset. We can only do this if the Settings
            // object is already in the asset database, which won't be the case if we're inside the
            // CreateInstance call in the Instance accessor above.
            if (AssetDatabase.Contains(this)) AddPlatformsToAsset();
#endif

            // Link all known platforms
            ForEachPlatform(LinkPlatform);
        }

        private void PopulatePlatformsFromAsset()
        {
            Platforms.Clear();

#if UNITY_EDITOR
            var assetPath = AssetDatabase.GetAssetPath(this);
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var assetPlatforms = assets.OfType<Platform>().ToArray();
#else
            Platform[] assetPlatforms = Resources.LoadAll<Platform>(SettingsAssetName);
#endif

            foreach (var newPlatform in assetPlatforms)
            {
                var existingPlatform = FindPlatform(newPlatform.Identifier);

                if (existingPlatform != null)
                {
                    // Duplicate platform; clean one of them up
                    Platform platformToDestroy;

                    if (newPlatform.Active && !existingPlatform.Active)
                    {
                        RemovePlatform(existingPlatform.Identifier);

                        platformToDestroy = existingPlatform;
                        existingPlatform = null;
                    }
                    else
                    {
                        platformToDestroy = newPlatform;
                    }

                    Debug.LogWarningFormat("FMOD: Cleaning up duplicate platform: ID  = {0}, name = '{1}', type = {2}",
                        platformToDestroy.Identifier, platformToDestroy.DisplayName, platformToDestroy.GetType().Name);

                    DestroyImmediate(platformToDestroy, true);
                }

                if (existingPlatform == null)
                {
                    newPlatform.EnsurePropertiesAreValid();
                    AddPlatform(newPlatform);
                }
            }

#if UNITY_EDITOR
            UpdateMigratedPlatforms();
#endif
        }

#if UNITY_EDITOR
        // Adds all platforms to the settings asset, so they get stored in the same file as the main
        // Settings object.
        private void AddPlatformsToAsset()
        {
            ForEachPlatform(AddPlatformToAsset);
        }

        private void AddPlatformToAsset(Platform platform)
        {
            if (!AssetDatabase.Contains(platform)) AssetDatabase.AddObjectToAsset(platform, this);
        }

        private void RemovePlatformFromAsset(Platform platform)
        {
            if (AssetDatabase.Contains(platform)) DestroyImmediate(platform, true);
        }

        private bool CanBuildTarget(BuildTarget target, Platform.BinaryType binaryType, out string error)
        {
            const string DownloadURL = "https://www.fmod.com/download";

            Platform platform;

            if (!PlatformForBuildTarget.TryGetValue(target, out platform))
            {
                error = string.Format("No FMOD platform found for build target {0}.\n" +
                                      "You may need to install a platform specific integration package from {1}.",
                    target, DownloadURL);
                return false;
            }

            var missingPathsQuery = platform.GetBinaryFilePaths(target, binaryType)
                .Where(path => !File.Exists(path) && !Directory.Exists(path));

            if (missingPathsQuery.Any())
            {
                var missingPaths = missingPathsQuery.Select(path => "- " + path).ToArray();

                string summary;

                if (missingPaths.Length == 1)
                    summary = string.Format("There is an FMOD binary missing for build target {0}", target);
                else
                    summary = string.Format("There are {0} FMOD binaries missing for build target {1}",
                        missingPaths.Length, target);

                if (binaryType == Platform.BinaryType.Logging) summary += " (development build)";

                error = string.Format(
                    "{0}:\n" +
                    "{1}\n" +
                    "You may need to reinstall the relevant integration package from {2}.\n",
                    summary, string.Join("\n", missingPaths), DownloadURL);
                return false;
            }

            error = null;
            return true;
        }

        public const string StaticPluginsSupportHeader = "fmod_static_plugin_support.h";
        public const string RegisterStaticPluginsFile = "fmod_register_static_plugins.cpp";
        public const string RegisterStaticPluginsAssetRelativePath = "/Plugins/FMOD/Cache/" + RegisterStaticPluginsFile;
        public const string RegisterStaticPluginsAssetFullPath = "Assets" + RegisterStaticPluginsAssetRelativePath;

        public static string RegisterStaticPluginsTempFilePath =>
            Application.dataPath + "/../Temp/" + RegisterStaticPluginsFile;


        private void PreprocessBuild(BuildTarget target, Platform.BinaryType binaryType)
        {
            var platform = PlatformForBuildTarget[target];

            PreprocessStaticPlugins(platform, target);
#if UNITY_2018_1_OR_NEWER
            SelectBinaries(platform, target, binaryType);
#endif
        }

        private static void PreprocessStaticPlugins(Platform platform, BuildTarget target)
        {
            // Ensure we don't have leftover temporary changes from a previous build.
            CleanTemporaryChanges();

            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(target);
            var scriptingBackend = PlayerSettings.GetScriptingBackend(buildTargetGroup);

            if (scriptingBackend == ScriptingImplementation.IL2CPP)
            {
                Action<string> reportError = message =>
                {
                    Debug.LogWarningFormat("FMOD: Error processing static plugins for platform {0}: {1}",
                        platform.DisplayName, message);
                };

                if (platform.SupportsAdditionalCPP(target))
                {
                    // Generate code to a file outside the Assets directory and use IL2CPP arguments
                    // to include it in the build.
                    Debug.LogFormat("FMOD: Generating static plugin registration code in {0}",
                        RegisterStaticPluginsTempFilePath);
                    CodeGeneration.GenerateStaticPluginRegistration(RegisterStaticPluginsTempFilePath, platform,
                        reportError);
                    AddIl2CppArgs();
                }
                else
                {
                    // Generate code to a file within the Assets directory and import it so it's
                    // automatically included in the build.
                    Debug.LogFormat("FMOD: Generating static plugin registration code in {0}",
                        RegisterStaticPluginsAssetFullPath);
                    var filePath = Application.dataPath + RegisterStaticPluginsAssetRelativePath;
                    CodeGeneration.GenerateStaticPluginRegistration(filePath, platform, reportError);
                    AssetDatabase.ImportAsset(RegisterStaticPluginsAssetFullPath);
                }
            }
            else if (platform.StaticPlugins.Count > 0)
            {
                Debug.LogWarningFormat(
                    "FMOD: Platform {0} has {1} static plugins specified, " +
                    "but static plugins are only supported on the IL2CPP scripting backend",
                    platform.DisplayName, platform.StaticPlugins.Count);
            }
        }

#if UNITY_2018_1_OR_NEWER
        private static void SelectBinaries(Platform platform, BuildTarget target, Platform.BinaryType binaryType)
        {
            var message = string.Format("FMOD: Selected binaries for platform {0}{1}:", target,
                binaryType == Platform.BinaryType.Logging ? " (development build)" : string.Empty);

            var enabledPaths = new HashSet<string>();

            foreach (var path in platform.GetBinaryAssetPaths(target, binaryType | Platform.BinaryType.Optional))
            {
                var importer = AssetImporter.GetAtPath(path);

                if (importer != null)
                {
                    (importer as PluginImporter).SetCompatibleWithPlatform(target, true);

                    enabledPaths.Add(path);

                    message += string.Format("\n- Enabled {0}", path);
                }
            }

            foreach (var path in platform.GetBinaryAssetPaths(target, Platform.BinaryType.All))
                if (!enabledPaths.Contains(path))
                {
                    var importer = AssetImporter.GetAtPath(path);

                    if (importer != null)
                    {
                        (importer as PluginImporter).SetCompatibleWithPlatform(target, false);

                        message += string.Format("\n- Disabled {0}", path);
                    }
                }

            Debug.Log(message);
        }
#endif

        private const string Il2CppCommand_AdditionalCpp = "--additional-cpp";

        private static IEnumerable<string> AdditionalIl2CppFiles()
        {
            yield return RegisterStaticPluginsTempFilePath;
            yield return Application.dataPath + "/Plugins/FMOD/src/Runtime/" + StaticPluginsSupportHeader;
        }

        private static void AddIl2CppArgs()
        {
            var arguments = PlayerSettings.GetAdditionalIl2CppArgs();

            foreach (var path in AdditionalIl2CppFiles())
            {
                var newArgument = string.Format("{0}=\"{1}\"", Il2CppCommand_AdditionalCpp, path);

                Debug.LogFormat("FMOD: Adding Il2CPP argument '{0}'", newArgument);

                if (string.IsNullOrEmpty(arguments))
                    arguments = newArgument;
                else
                    arguments += " " + newArgument;
            }

            PlayerSettings.SetAdditionalIl2CppArgs(arguments);
        }

        [InitializeOnLoadMethod]
        private static void CleanTemporaryChanges()
        {
            CleanIl2CppArgs();
            CleanTemporaryFiles();
        }

        private static void CleanIl2CppArgs()
        {
            var arguments = PlayerSettings.GetAdditionalIl2CppArgs();
            var newArguments = arguments;

            foreach (var path in AdditionalIl2CppFiles())
            {
                // Match on basename only in case the temp file location has moved
                var basename = Regex.Escape(Path.GetFileName(path));
                var regex = new Regex(Il2CppCommand_AdditionalCpp + "=\"[^\"]*" + basename + "\"");

                for (var startIndex = 0; startIndex < newArguments.Length;)
                {
                    var match = regex.Match(newArguments, startIndex);

                    if (!match.Success) break;

                    Debug.LogFormat("FMOD: Removing Il2CPP argument '{0}'", match.Value);

                    var matchStart = match.Index;
                    var matchEnd = match.Index + match.Length;

                    // Consume an adjacent space if there is one
                    if (matchStart > 0 && newArguments[matchStart - 1] == ' ')
                        --matchStart;
                    else if (matchEnd < newArguments.Length && newArguments[matchEnd] == ' ') ++matchEnd;

                    newArguments = newArguments.Substring(0, matchStart) + newArguments.Substring(matchEnd);
                    startIndex = matchStart;
                }
            }

            if (newArguments != arguments) PlayerSettings.SetAdditionalIl2CppArgs(newArguments);
        }

        private static void CleanTemporaryFiles()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                // Messing with the asset database while entering play mode causes a NullReferenceException
                return;

            if (AssetDatabase.DeleteAsset(RegisterStaticPluginsAssetFullPath))
                Debug.LogFormat("FMOD: Removed temporary file {0}", RegisterStaticPluginsAssetFullPath);
        }

#if UNITY_2018_1_OR_NEWER
        public class BuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
        {
            public void OnPostprocessBuild(BuildReport report)
            {
                CleanTemporaryChanges();
            }

            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                Platform.BinaryType binaryType;

                if ((report.summary.options & BuildOptions.Development) == BuildOptions.Development)
                    binaryType = Platform.BinaryType.Logging;
                else
                    binaryType = Platform.BinaryType.Release;

                string error;
                if (!Instance.CanBuildTarget(report.summary.platform, binaryType, out error))
                    throw new BuildFailedException(error);

                Instance.PreprocessBuild(report.summary.platform, binaryType);
            }
        }
#else
        public class BuildProcessor : IPreprocessBuild, IPostprocessBuild
        {
            public int callbackOrder { get { return 0; } }

            public void OnPreprocessBuild(BuildTarget target, string path)
            {
                Platform.BinaryType binaryType = Platform.BinaryType.Release | Platform.BinaryType.Logging;

                string error;
                if (!Settings.Instance.CanBuildTarget(target, binaryType, out error))
                {
                    throw new BuildFailedException(error);
                }

                Settings.Instance.PreprocessBuild(target, binaryType);
            }

            public void OnPostprocessBuild(BuildTarget target, string path)
            {
                Settings.CleanTemporaryChanges();
            }
        }
#endif

        public class BuildTargetChecker : IActiveBuildTargetChanged
        {
            public int callbackOrder => 0;

            public void OnActiveBuildTargetChanged(BuildTarget previous, BuildTarget current)
            {
                var binaryType = EditorUserBuildSettings.development
                    ? Platform.BinaryType.Logging
                    : Platform.BinaryType.Release;

                string error;
                if (!Instance.CanBuildTarget(current, binaryType, out error))
                {
                    Debug.LogWarning(error);

#if UNITY_2019_3_OR_NEWER
                    if (EditorWindow.HasOpenInstances<BuildPlayerWindow>())
                    {
                        var message =
                            new GUIContent("FMOD detected issues with this platform!\nSee the Console for details.");
                        EditorWindow.GetWindow<BuildPlayerWindow>().ShowNotification(message, 10);
                    }
#endif
                }
            }
        }
#endif
    }

    // This class stores data types and code used for migrating old settings.
    public static class Legacy
    {
        [Serializable]
        public enum Platform
        {
            None,
            PlayInEditor,
            Default,
            Desktop,
            Mobile,
            MobileHigh,
            MobileLow,
            Console,
            Windows,
            Mac,
            Linux,
            iOS,
            Android,
            Deprecated_1,
            XboxOne,
            PS4,
            Deprecated_2,
            Deprecated_3,
            AppleTV,
            UWP,
            Switch,
            WebGL,
            Stadia,
            Reserved_1,
            Reserved_2,
            Reserved_3,
            Count
        }

        // Copies a setting from one platform to another.
        public static void CopySetting<T, U>(List<T> list, Platform fromPlatform, Platform toPlatform)
            where T : PlatformSetting<U>, new()
        {
            var fromSetting = list.Find(x => x.Platform == fromPlatform);
            var toSetting = list.Find(x => x.Platform == toPlatform);

            if (fromSetting != null)
            {
                if (toSetting == null)
                {
                    toSetting = new T {Platform = toPlatform};
                    list.Add(toSetting);
                }

                toSetting.Value = fromSetting.Value;
            }
            else if (toSetting != null)
            {
                list.Remove(toSetting);
            }
        }

        public static void CopySetting(List<PlatformBoolSetting> list, Platform fromPlatform, Platform toPlatform)
        {
            CopySetting<PlatformBoolSetting, TriStateBool>(list, fromPlatform, toPlatform);
        }

        public static void CopySetting(List<PlatformIntSetting> list, Platform fromPlatform, Platform toPlatform)
        {
            CopySetting<PlatformIntSetting, int>(list, fromPlatform, toPlatform);
        }

        // Returns the UI display name for the given platform.
        public static string DisplayName(Platform platform)
        {
            switch (platform)
            {
                case Platform.Linux:
                    return "Linux";
                case Platform.Desktop:
                    return "Desktop";
                case Platform.Console:
                    return "Console";
                case Platform.iOS:
                    return "iOS";
                case Platform.Mac:
                    return "OSX";
                case Platform.Mobile:
                    return "Mobile";
                case Platform.PS4:
                    return "PS4";
                case Platform.Windows:
                    return "Windows";
                case Platform.UWP:
                    return "UWP";
                case Platform.XboxOne:
                    return "XBox One";
                case Platform.Android:
                    return "Android";
                case Platform.AppleTV:
                    return "Apple TV";
                case Platform.MobileHigh:
                    return "High-End Mobile";
                case Platform.MobileLow:
                    return "Low-End Mobile";
                case Platform.Stadia:
                    return "Stadia";
                case Platform.Switch:
                    return "Switch";
                case Platform.WebGL:
                    return "WebGL";
            }

            return "Unknown";
        }

        // Returns the UI sort order for the given platform.
        public static float SortOrder(Platform legacyPlatform)
        {
            switch (legacyPlatform)
            {
                case Platform.Desktop:
                    return 1;
                case Platform.Windows:
                    return 1.1f;
                case Platform.Mac:
                    return 1.2f;
                case Platform.Linux:
                    return 1.3f;
                case Platform.Mobile:
                    return 2;
                case Platform.MobileHigh:
                    return 2.1f;
                case Platform.MobileLow:
                    return 2.2f;
                case Platform.AppleTV:
                    return 2.3f;
                case Platform.Console:
                    return 3;
                case Platform.XboxOne:
                    return 3.1f;
                case Platform.PS4:
                    return 3.2f;
                case Platform.Switch:
                    return 3.3f;
                case Platform.Stadia:
                    return 3.4f;
                default:
                    return 0;
            }
        }

        // Returns the parent for the given platform.
        public static Platform Parent(Platform platform)
        {
            switch (platform)
            {
                case Platform.Windows:
                case Platform.Linux:
                case Platform.Mac:
                case Platform.UWP:
                case Platform.WebGL:
                    return Platform.Desktop;
                case Platform.MobileHigh:
                case Platform.MobileLow:
                case Platform.iOS:
                case Platform.Android:
                case Platform.AppleTV:
                    return Platform.Mobile;
                case Platform.Switch:
                case Platform.XboxOne:
                case Platform.PS4:
                case Platform.Stadia:
                case Platform.Reserved_1:
                case Platform.Reserved_2:
                case Platform.Reserved_3:
                    return Platform.Console;
                case Platform.Desktop:
                case Platform.Console:
                case Platform.Mobile:
                    return Platform.Default;
                case Platform.PlayInEditor:
                case Platform.Default:
                default:
                    return Platform.None;
            }
        }

        // Determines whether the given platform is a group
        public static bool IsGroup(Platform platform)
        {
            switch (platform)
            {
                case Platform.Desktop:
                case Platform.Mobile:
                case Platform.Console:
                    return true;
                default:
                    return false;
            }
        }

        public class PlatformSettingBase
        {
            public Platform Platform;
        }

        public class PlatformSetting<T> : PlatformSettingBase
        {
            public T Value;
        }

        [Serializable]
        public class PlatformIntSetting : PlatformSetting<int>
        {
        }

        [Serializable]
        public class PlatformStringSetting : PlatformSetting<string>
        {
        }

        [Serializable]
        public class PlatformBoolSetting : PlatformSetting<TriStateBool>
        {
        }
    }
}