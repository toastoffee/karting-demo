#if UNITY_EDITOR_OSX
using System;
using System.Reflection;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

namespace KartGame.Editor
{
    [InitializeOnLoad]
    static class BurstEditorFallback
    {
        const string SessionLogKey = "KartGame.Editor.BurstEditorFallback.Logged";

        static BurstEditorFallback()
        {
            DisableBrokenEditorBurst();
            EditorApplication.delayCall += DisableBrokenEditorBurst;
        }

        static void DisableBrokenEditorBurst()
        {
            try
            {
                JobsUtility.JobCompilerEnabled = false;
                DisableBurstCompilerOptions();
                DisableBurstEditorMenuFlag();

                if (!SessionState.GetBool(SessionLogKey, false))
                {
                    SessionState.SetBool(SessionLogKey, true);
                    Debug.Log("KartGame: Burst has been disabled in the macOS Editor to avoid function-pointer compilation failures. Player builds keep their normal Burst settings.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"KartGame: Failed to disable Burst in the Editor. {exception.Message}");
            }
        }

        static void DisableBurstCompilerOptions()
        {
            Type burstCompilerType = Type.GetType("Unity.Burst.BurstCompiler, Unity.Burst");
            if (burstCompilerType == null)
                return;

            PropertyInfo optionsProperty = burstCompilerType.GetProperty(
                "Options",
                BindingFlags.Static | BindingFlags.Public);

            object options = optionsProperty?.GetValue(null);
            if (options == null)
                return;

            Type optionsType = options.GetType();
            optionsType.GetProperty("EnableBurstCompilation", BindingFlags.Instance | BindingFlags.Public)?.SetValue(options, false);
            optionsType.GetProperty("EnableBurstCompileSynchronously", BindingFlags.Instance | BindingFlags.Public)?.SetValue(options, false);
        }

        static void DisableBurstEditorMenuFlag()
        {
            Type burstEditorOptionsType = Type.GetType("Unity.Burst.Editor.BurstEditorOptions, Unity.Burst.Editor");
            if (burstEditorOptionsType == null)
                return;

            PropertyInfo enableBurstCompilationProperty = burstEditorOptionsType.GetProperty(
                "EnableBurstCompilation",
                BindingFlags.Static | BindingFlags.Public);

            enableBurstCompilationProperty?.SetValue(null, false);
        }
    }
}
#endif
