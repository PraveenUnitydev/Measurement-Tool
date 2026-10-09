using System;
using System.Collections.Generic;

namespace VehicleMeasurement
{
    /// <summary>
    /// The key the app knows a vehicle by, and the Addressables address it is loaded with.
    ///
    /// A vehicle published from Unity is loaded by the address "das/&lt;id&gt;" (its own catalog), while the app has always
    /// known it by its original key ("3XO"): saved measurements, thumbnails, the downloaded list and the picker all use
    /// that key. Switching the app to the new address made published vehicles look like different vehicles (no
    /// thumbnail, old measurements no longer opening it). So the app keeps the original key everywhere, and only the
    /// calls into Addressables translate it with <see cref="Real"/>.
    /// Plain C# (no Unity types) so it is tested outside Unity.
    /// </summary>
    public static class DasKeys
    {
        public const string PublishedPrefix = "das/";
        private static readonly Dictionary<string, string> _toAddress = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static bool IsPublishedAddress(string key)
        {
            return !string.IsNullOrEmpty(key) && key.StartsWith(PublishedPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The key the app uses for a catalog entry: unchanged for vehicles from the original build; for published ones the
        /// key it had before it was first published (server "legacyKey"), else its vehicle id.
        /// </summary>
        public static string AppKeyFor(string vehicleId, string addressableKey, string legacyKey)
        {
            if (!IsPublishedAddress(addressableKey)) return addressableKey;
            if (!string.IsNullOrEmpty(legacyKey) && !IsPublishedAddress(legacyKey)) return legacyKey;
            return string.IsNullOrEmpty(vehicleId) ? addressableKey : vehicleId;
        }

        /// <summary>The Addressables address for an app key (unknown keys and labels are returned unchanged).</summary>
        public static string Real(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            string address;
            lock (_toAddress) return _toAddress.TryGetValue(key, out address) ? address : key;
        }

        public static void Set(string appKey, string address)
        {
            if (string.IsNullOrEmpty(appKey) || string.IsNullOrEmpty(address)) return;
            lock (_toAddress)
            {
                if (string.Equals(appKey, address, StringComparison.Ordinal)) _toAddress.Remove(appKey);
                else _toAddress[appKey] = address;
            }
        }

        public static void Clear() { lock (_toAddress) _toAddress.Clear(); }
    }
}
