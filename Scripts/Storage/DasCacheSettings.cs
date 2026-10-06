using System;
using UnityEngine;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// Unity quietly deletes any cached bundle that hasn't been opened for a set time (its default is
    /// roughly 150 days). For a lab tool with dozens of vehicles, one that isn't opened for a few months
    /// would silently vanish and be re-downloaded without warning. This keeps downloaded vehicles until
    /// someone removes them on purpose.
    /// </summary>
    public static class DasCacheSettings
    {
        /// <summary>Five years, in seconds.</summary>
        public const int KeepUnusedBundlesSeconds = 5 * 365 * 24 * 60 * 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            try
            {
                Cache cache = Caching.defaultCache;
                if (cache.valid && cache.expirationDelay < KeepUnusedBundlesSeconds)
                    cache.expirationDelay = KeepUnusedBundlesSeconds;
            }
            catch (Exception)
            {
                // Never let a cache setting stop the app from starting
            }
        }
    }
}
