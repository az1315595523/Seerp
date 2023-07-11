#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Burst.LowLevel;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.Scripting.ScriptCompilation;
using UnityEngine;

namespace Unity.Burst.Editor
{
    /// <summary>
    /// Main entry point for initializing the burst compiler service for both JIT and AOT
    /// </summary>
    [InitializeOnLoad]
    internal class BurstLoader
    {
        private const int BURST_PROTOCOL_VERSION = 1;

        // Cache the delegate to make sure it doesn't get collected.
        private static readonly BurstCompilerService.ExtractCompilerFlags TryGetOptionsFromMemberDelegate = TryGetOptionsFromMember;

        /// <summary>
        /// Gets the location to the runtime path of burst.
        /// </summary>
        public static string RuntimePath { get; private set; }

        public static bool IsDebugging { get; private set; }

        public static int DebuggingLevel { get; private set; }

        public static bool SafeShutdown { get; private set; }

        public static int ProtocolVersion { get; private set; }

        private static void VersionUpdateCheck()
        {
            var seek = "com.unity.burst@";
            var first = RuntimePath.LastIndexOf(seek);
            var last = RuntimePath.LastIndexOf(".Runtime");
            string version;
            if (first == -1 || last == -1 || last <= first)
            {
                version = "Unknown";
            }
            else
            {
                first += seek.Length;
                last -= 1;
                version = RuntimePath.Substring(first, last - first);
            }

            var result = BurstCompiler.VersionNotify(version);
            // result will be empty if we are shutting down, and thus we shouldn't popup a dialog
            if (!String.IsNullOrEmpty(result) && result != version)
            {
                if (IsDebugging)
                {
                    UnityEngine.Debug.LogWarning($"[com.unity.burst] - '{result}' != '{version}'");
                }
                OnVersionChangeDetected();
            }
        }

        private static bool UnityBurstRuntimePathOverwritten(out string path)
        {
            path = Environment.GetEnvironmentVariable("UNITY_BURST_RUNTIME_PATH");
            return Directory.Exists(path);
        }

        private static void OnVersionChangeDetected()
        {
            // Write marker file to tell Burst to delete the cache at next startup.
            try
            {
                File.Create(Path.Combine(BurstCompilerOptions.DefaultCacheFolder, BurstCompilerOptions.DeleteCacheMarkerFileName)).Dispose();
            }
            catch (IOException)
            {
                // In the unlikely scenario that two processes are creating this marker file at the same time,
                // and one of them fails, do nothing because the other one has hopefully succeeded.
            }

            // Skip checking if we are using an explicit runtime path.
            if (!UnityBurstRuntimePathOverwritten(out var _))
            {
                EditorUtility.DisplayDialog("Burst Package Update Detected", "The version of Burst used by your project has changed. Please restart the Editor to continue.", "OK");
                BurstCompiler.Shutdown();
            }
        }

        private static bool _currentBuildIsPlayer;

        static BurstLoader()
        {
            if (BurstCompilerOptions.ForceDisableBurstCompilation)
            {
                if (!BurstCompilerOptions.IsSecondaryUnityProcess)
                {
                    UnityEngine.Debug.LogWarning("[com.unity.burst] Burst is disabled entirely from the command line");
                }
                return;
            }

            // This can be setup to get more diagnostics
            var debuggingStr = Environment.GetEnvironmentVariable("UNITY_BURST_DEBUG");
            IsDebugging = debuggingStr != null;
            if (IsDebugging)
            {
                UnityEngine.Debug.LogWarning("[com.unity.burst] Extra debugging is turned on.");
                int debuggingLevel;
                int.TryParse(debuggingStr, out debuggingLevel);
                if (debuggingLevel <= 0) debuggingLevel = 1;
                DebuggingLevel = debuggingLevel;
            }

            // Try to load the runtime through an environment variable
            if (!UnityBurstRuntimePathOverwritten(out var path))
            {
                // Otherwise try to load it from the package itself
                path = Path.GetFullPath("Packages/com.unity.burst/.Runtime");
            }

            RuntimePath = path;

            if (IsDebugging)
            {
                UnityEngine.Debug.LogWarning($"[com.unity.burst] Runtime directory set to {RuntimePath}");
            }

            BurstCompilerService.Initialize(RuntimePath, TryGetOptionsFromMemberDelegate);

            ProtocolVersion = BurstCompiler.RequestSetProtocolVersion(BURST_PROTOCOL_VERSION);

            BurstCompiler.Initialize(GetAssemblyFolders());

            // It's important that this call comes *after* BurstCompilerService.Initialize,
            // otherwise any calls from within EnsureSynchronized to BurstCompilerService,
            // such as BurstCompiler.Disable(), will silently fail.
            BurstEditorOptions.EnsureSynchronized();

            EditorApplication.quitting += OnEditorApplicationQuitting;

            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;

#if UNITY_2022_2_OR_NEWER
            CompilationPipeline.assemblyCompilationNotRequired += OnAssemblyCompilationNotRequired;
#endif

            EditorApplication.playModeStateChanged += EditorApplicationOnPlayModeStateChanged;
            AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;

            SafeShutdown = false;
            UnityEditor.PackageManager.Events.registeringPackages += PackageRegistrationEvent;
            SafeShutdown = BurstCompiler.IsApiAvailable("SafeShutdown");

            if (!SafeShutdown)
            {
                VersionUpdateCheck();
            }

            // Notify the compiler about a domain reload
            if (IsDebugging)
            {
                UnityEngine.Debug.Log("Burst - Domain Reload");
            }

            BurstCompiler.OnProgress += OnProgress;

            BurstCompiler.EagerCompilationLoggingEnabled = true;

            // Make sure BurstRuntime is initialized. This needs to happen before BurstCompiler.DomainReload,
            // because that can cause calls to BurstRuntime.Log.
            BurstRuntime.Initialize();

            // Notify the JitCompilerService about a domain reload
            BurstCompiler.SetDefaultOptions();
            BurstCompiler.DomainReload();

            BurstCompiler.OnProfileBegin += OnProfileBegin;
            BurstCompiler.OnProfileEnd += OnProfileEnd;
            BurstCompiler.SetProfilerCallbacks();

            BurstCompiler.InitialiseDebuggerHooks();
        }

        private static bool _isQuitting;
        private static void OnEditorApplicationQuitting()
        {
            _isQuitting = true;
        }

        public static Action OnBurstShutdown;

        private static void PackageRegistrationEvent(UnityEditor.PackageManager.PackageRegistrationEventArgs obj)
        {
            bool requireCleanup = false;
            if (SafeShutdown)
            {
                foreach (var changed in obj.changedFrom)
                {
                    if (changed.name.Contains("com.unity.burst"))
                    {
                        requireCleanup = true;
                        break;
                    }
                }
            }

            foreach (var removed in obj.removed)
            {
                if (removed.name.Contains("com.unity.burst"))
                {
                    requireCleanup = true;
                }
            }

            if (requireCleanup)
            {
                OnBurstShutdown?.Invoke();
                if (!SafeShutdown)
                {
                    EditorUtility.DisplayDialog("Burst Package Has Been Removed", "Please restart the Editor to continue.", "OK");
                }
                BurstCompiler.Shutdown();
            }
        }

        // Don't initialize to 0 because that could be a valid progress ID.
        private static int BurstProgressId = -1;

        // If this enum changes, update the benchmarks tool accordingly as we rely on integer value related to this enum
        internal enum BurstEagerCompilationStatus
        {
            NotScheduled,
            Scheduled,
            Completed
        }

        // For the time being, this field is only read through reflection
        internal static BurstEagerCompilationStatus EagerCompilationStatus;

        private static void OnProgress(int current, int total)
        {
            if (current == total)
            {
                EagerCompilationStatus = BurstEagerCompilationStatus.Completed;
            }

            // OnProgress is called from a background thread,
            // but we need to update the progress UI on the main thread.
            EditorApplication.CallDelayed(() =>
            {
                if (current == total)
                {
                    // We've finished - remove progress bar.
                    if (Progress.Exists(BurstProgressId))
                    {
                        Progress.Remove(BurstProgressId);
                        BurstProgressId = -1;
                    }
                }
                else
                {
                    // Do we need to create the progress bar?
                    if (!Progress.Exists(BurstProgressId))
                    {
                        BurstProgressId = Progress.Start(
                            "Burst",
                            "Compiling...",
                            Progress.Options.Unmanaged);
                    }

                    Progress.Report(
                        BurstProgressId,
                        current / (float)total,
                        $"Compiled {current} / {total} libraries");
                }
            });
        }

        [ThreadStatic]
        private static Dictionary<string, IntPtr> ProfilerMarkers;

        private static unsafe void OnProfileBegin(string markerName, string metadataName, string metadataValue)
        {
            if (ProfilerMarkers == null)
            {
                // Initialize thread-static dictionary.
                ProfilerMarkers = new Dictionary<string, IntPtr>();
            }

            if (!ProfilerMarkers.TryGetValue(markerName, out var markerPtr))
            {
                ProfilerMarkers.Add(markerName, markerPtr = ProfilerUnsafeUtility.CreateMarker(
                    markerName,
                    ProfilerUnsafeUtility.CategoryScripts,
                    MarkerFlags.Script,
                    metadataName != null ? 1 : 0));

                // metadataName is assumed to be consistent for a given markerName.
                if (metadataName != null)
                {
                    ProfilerUnsafeUtility.SetMarkerMetadata(
                        markerPtr,
                        0,
                        metadataName,
                        (byte)ProfilerMarkerDataType.String16,
                        (byte)ProfilerMarkerDataUnit.Undefined);
                }
            }

            if (metadataName != null && metadataValue != null)
            {
                fixed (char* methodNamePtr = metadataValue)
                {
                    var metadata = new ProfilerMarkerData
                    {
                        Type = (byte)ProfilerMarkerDataType.String16,
                        Size = ((uint)metadataValue.Length + 1) * 2,
                        Ptr = methodNamePtr
                    };
                    ProfilerUnsafeUtility.BeginSampleWithMetadata(markerPtr, 1, &metadata);
                }
            }
            else
            {
                ProfilerUnsafeUtility.BeginSample(markerPtr);
            }
        }

        private static void OnProfileEnd(string markerName)
        {
            if (ProfilerMarkers == null)
            {
                // If we got here it means we had a domain reload between when we called profile begin and
                // now profile end, and so we need to bail out.
                return;
            }

            if (!ProfilerMarkers.TryGetValue(markerName, out var markerPtr))
            {
                return;
            }

            ProfilerUnsafeUtility.EndSample(markerPtr);
        }

        private static void EditorApplicationOnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (DebuggingLevel > 2)
            {
                UnityEngine.Debug.Log($"Burst - Change of Editor State: {state}");
            }

            switch (state)
            {
                case PlayModeStateChange.ExitingPlayMode:
                    // Cleanup any loaded burst natives so users have a clean point to update the libraries.
                    BurstCompiler.UnloadAdditionalLibraries();
                    break;
            }
        }

        static bool CurrentCompilationTaskIsForEditor()
        {
            try
            {
                var inst = EditorCompilationInterface.Instance;
#if UNITY_2021_1_OR_NEWER
                var editorCompilationType = inst.GetType();
                var activeBeeBuildField = editorCompilationType.GetField("_currentBeeScriptCompilationState", BindingFlags.Instance | BindingFlags.NonPublic);
                if (activeBeeBuildField == null)
                {
                    activeBeeBuildField = editorCompilationType.GetField("activeBeeBuild", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                var activeBeeBuild = activeBeeBuildField.GetValue(inst);

                // If a user is doing an `AssemblyBuilder` compilation, we do not support that in Burst.
                // This seems to manifest as a null `activeBeeBuild`, so we bail here if that happens.
                if (activeBeeBuild == null)
                {
                    return false;
                }

                var settings = activeBeeBuild.GetType().GetProperty("settings", BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance).GetValue(activeBeeBuild);
                var opt = (EditorScriptCompilationOptions)settings.GetType().GetProperty("CompilationOptions").GetValue(settings);
#else
                var task = inst.GetType()
                    .GetField("compilationTask", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(inst);

                // If a user is doing an `AssemblyBuilder` compilation, we do not support that in Burst.
                // This seems to manifest as a null `task`, so we bail here if that happens.
                if (task == null)
                {
                    return false;
                }

                var opt = (EditorScriptCompilationOptions)task.GetType()
                    .GetField("options", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(task);
#endif

#if UNITY_2022_2_OR_NEWER
                if ((opt & EditorScriptCompilationOptions.BuildingSkipCompile) != 0)
                {
                    return false;
                }
#endif

                return (opt & EditorScriptCompilationOptions.BuildingForEditor) != 0;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("Burst - Unknown private compilation pipeline API\nAssuming editor build\n" + ex.ToString());

                return true;
            }
        }

        private static void OnCompilationStarted(object value)
        {
            if (!CurrentCompilationTaskIsForEditor())
            {
                if (DebuggingLevel > 2)
                {
                    UnityEngine.Debug.Log($"{DateTime.UtcNow} Burst - not handling '{value}' because it's a player build");
                }

                _currentBuildIsPlayer = true;

                return;
            }

            if (DebuggingLevel > 2)
            {
                UnityEngine.Debug.Log($"{DateTime.UtcNow} Burst - compilation started for '{value}'");
            }

            BurstCompiler.NotifyCompilationStarted(GetAssemblyFolders());
        }

        private static string[] GetAssemblyFolders()
        {
            var assemblyFolders = new HashSet<string>();

            // First, we get the path to Mono system libraries. This will be something like
            // <EditorPath>/Data/MonoBleedingEdge/lib/mono/unityjit-win32
            //
            // You might think we could use MonoLibraryHelpers.GetSystemReferenceDirectories
            // here, but we can't, because that returns the _reference assembly_ directories,
            // not the actual implementation assembly directory.
            assemblyFolders.Add(Path.GetDirectoryName(typeof(object).Assembly.Location));

            // Now add the default assembly search paths.
            // This will include
            // - Unity dlls in <EditorPath>/Data/Managed and <EditorPath>/Data/Managed/UnityEngine
            // - Platform support dlls e.g. <EditorPath>/Data/PlaybackEngines/WindowsStandaloneSupport
            // - Package paths. These are interesting because they are "virtual" paths, of the form
            //   Packages/<MyPackageName>. They need to be resolved to physical paths.
            // - Library/ScriptAssemblies. This needs to be resolved to the full path.
            var defaultAssemblySearchPaths = AssemblyHelper.GetDefaultAssemblySearchPaths();
            var packagesLookup = GetPackagesLookup();
            foreach (var searchPath in defaultAssemblySearchPaths)
            {
                if (TryResolvePath(searchPath, packagesLookup, out var resolvedPath))
                {
                    assemblyFolders.Add(resolvedPath);
                }
            }

            if (IsDebugging)
            {
                UnityEngine.Debug.Log($"{DateTime.UtcNow} Burst - AssemblyFolders : \n{string.Join("\n", assemblyFolders)}");
            }

            return assemblyFolders.ToArray();
        }

        private static Dictionary<string, string> GetPackagesLookup()
        {
            var packages = new Dictionary<string, string>();

            // Fetch list of packages
#if UNITY_2021_1_OR_NEWER
            var allPackages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
#else
            var allPackages = UnityEditor.PackageManager.PackageInfo.GetAll();
#endif
            foreach (var p in allPackages)
            {
                packages.Add(p.name, p.resolvedPath);
            }

            return packages;
        }

        private const string PackagesPath = "Packages/";
        private static readonly int PackagesPathLength = PackagesPath.Length;

        private static bool TryResolvePath(string path, Dictionary<string, string> packagesLookup, out string resolvedPath)
        {
            if (path.StartsWith("Packages/", StringComparison.InvariantCulture))
            {
                var secondSlashIndex = path.IndexOf('/', PackagesPathLength);
                var packageName = secondSlashIndex > -1
                    ? path.Substring(PackagesPathLength, secondSlashIndex - PackagesPathLength)
                    : path.Substring(PackagesPathLength);

                if (packagesLookup.TryGetValue(packageName, out var resolvedPathTemp))
                {
                    path = secondSlashIndex > -1
                        ? Path.Combine(resolvedPathTemp, path.Substring(secondSlashIndex + 1))
                        : resolvedPathTemp;
                }
                else
                {
                    if (IsDebugging)
                    {
                        UnityEngine.Debug.Log($"{DateTime.UtcNow} Burst - unknown package path '{path}'");
                    }
                    resolvedPath = null;
                    return false;
                }
            }

            resolvedPath = Path.GetFullPath(path);
            return true;
        }

        private static void OnCompilationFinished(object value)
        {
            if (_currentBuildIsPlayer)
            {
                if (DebuggingLevel > 2)
                {
                    UnityEngine.Debug.Log($"{DateTime.UtcNow} Burst - ignoring finished compilation '{value}' because it's a player build");
                }

                _currentBuildIsPlayer = false;
                return;
            }

            if (DebuggingLevel > 2)
            {
                UnityEngine.Debug.Log($"{DateTime.UtcNow} Burst - compilation finished for '{value}'");
            }

            BurstCompiler.NotifyCompilationFinished();
        }

        private static void OnAssemblyCompilationFinished(string arg1, CompilerMessage[] arg2)
        {
            if (_currentBuildIsPlayer)
            {
                if (DebuggingLevel > 2)
                {
                    UnityEngine.Debug.Log($"{DateTime.UtcNow} Burst - ignoring '{arg1}' because it's a player build");
                }

                return;
            }

            if (DebuggingLevel > 2)
            {
                UnityEngine.Debug.Log($"{DateTime.UtcNow} Burst - Assembly compilation finished for '{arg1}'");
            }

            BurstCompiler.NotifyAssemblyCompilationFinished(Path.GetFileNameWithoutExtension(arg1));
        }

        private static void OnAssemblyCompilationNotRequired(string arg1)
        {
            if (_currentBuildIsPlayer)
            {
                if (DebuggingLevel > 2)
                {
                    UnityEngine.Debug.Log($"{DateTime.UtcNow} Burst - ignoring '{arg1}' because it's a player build");
                }

                return;
            }

            if (DebuggingLevel > 2)
            {
                UnityEngine.Debug.Log($"{DateTime.UtcNow} Burst - Assembly compilation not required for '{arg1}'");
            }

            BurstCompiler.NotifyAssemblyCompilationNotRequired(Path.GetFileNameWithoutExtension(arg1));
        }

        private static bool TryGetOptionsFromMember(MemberInfo member, out string flagsOut)
        {
            return BurstCompiler.Options.TryGetOptions(member, true, out flagsOut);
        }

        private static void OnDomainUnload(object sender, EventArgs e)
        {
            if (DebuggingLevel > 2)
            {
                UnityEngine.Debug.Log($"Burst - OnDomainUnload");
            }

            BurstCompiler.Cancel();

            // This check here is to execute shutdown after all OnDisable's. EditorApplication.quitting event is called before OnDisable's, so we need to shutdown in here.
            if (_isQuitting)
            {
                BurstCompiler.Shutdown();
            }

            // Because of a check in Unity (specifically SCRIPTINGAPI_THREAD_AND_SERIALIZATION_CHECK),
            // we are not allowed to call thread-unsafe methods (like Progress.Exists) after the
            // kApplicationTerminating bit has been set. And because the domain is unloaded
            // (thus triggering AppDomain.DomainUnload) *after* that bit is set, we can't call Progress.Exists
            // during shutdown. So we check _isQuitting here. When quitting, it's fine for the progress item
            // not to be removed since it's all being torn down anyway.
            if (!_isQuitting && Progress.Exists(BurstProgressId))
            {
                Progress.Remove(BurstProgressId);
                BurstProgressId = -1;
            }
        }
    }
}
#endif