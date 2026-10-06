using System;
using UnityEngine;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// Unity quietly deletes any cached bundle that hasn't been opened for a set time (150 days by default).
    /// For a lab tool with dozens of vehicles, one that isn't opened for a few months would silently vanish
    /// and be re-downloaded without warning. This keeps downloaded vehicles until someone removes them on purpose.
    ///
    /// The first version set this once, at the very first moment the app starts, and a real-machine report
    /// showed it had no effect (still 150 days) - most likely because the cache isn't ready that early. It now
    /// waits until the cache is ready, applies the value, READS IT BACK to prove it stuck, retries for up to
    /// 30 seconds, and records what happened in <see cref="LastResult"/> (shown in the storage report).
    /// </summary>
    public static class DasCacheSettings
    {
        /// <summary>Five years, in seconds.</summary>
        public const int KeepUnusedBundlesSeconds = 5 * 365 * 24 * 60 * 60;

        public static string LastResult { get; private set; }
        public static int Attempts { get; private set; }

        static DasCacheSettings() { LastResult = "has not run yet"; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartRunner()
        {
            var go = new GameObject("DasCacheSettings");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<DasCacheSettingsRunner>();
        }

        /// <summary>One attempt. True only when the new value has been read back and is in effect.</summary>
        public static bool TryApply()
        {
            Attempts++;
            try
            {
                if (!Caching.ready) { LastResult = "waiting: the cache isn't ready yet (attempt " + Attempts + ")"; return false; }
                Cache cache = Caching.defaultCache;
                if (!cache.valid) { LastResult = "waiting: the default cache isn't valid yet (attempt " + Attempts + ")"; return false; }

                if (cache.expirationDelay < KeepUnusedBundlesSeconds)
                    cache.expirationDelay = KeepUnusedBundlesSeconds;

                int now = Caching.defaultCache.expirationDelay;   // read it back from the cache itself
                if (now >= KeepUnusedBundlesSeconds)
                {
                    LastResult = "applied on attempt " + Attempts + ": unused bundles are kept for " + Days(now) + " days";
                    return true;
                }
                LastResult = "the setting was ignored: still " + Days(now) + " days after attempt " + Attempts;
                return false;
            }
            catch (Exception e)
            {
                LastResult = "error on attempt " + Attempts + ": " + e.Message;
                return false;
            }
        }

        private static string Days(int seconds) { return (seconds / 86400).ToString(); }
    }
}
