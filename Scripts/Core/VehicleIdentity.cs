using System;
using System.IO;

namespace VehicleMeasurement
{
    /// <summary>
    /// One vehicle can be referred to in several forms: its id ("3008"), the key the app uses ("Peugeot3008"), its
    /// published Addressables address ("das/3008"), the key it had before it was published (legacyKey), and the file
    /// name a measurement was saved under (the key with unsafe characters replaced: "das_3008", "Thar_Roxx").
    /// <see cref="Matches"/> treats all of those as the same vehicle, so every screen and store finds it the same way.
    /// Plain C# (no Unity types), tested outside Unity.
    /// </summary>
    public static class VehicleIdentity
    {
        /// <summary>The file id a measurement is saved under - the same rule MeasurementController has always used.</summary>
        public static string FileId(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            string name = key;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Replace('/', '_').Replace('\\', '_').Replace(':', '_');      // also on non-Windows builds and tests
            name = name.Replace(' ', '_');
            name = name.Replace("(Clone)", "").Trim('_');
            return name;
        }

        /// <summary>Is <paramref name="any"/> one of this vehicle's names (any casing, or as a saved file id)?</summary>
        public static bool Matches(string any, string vehicleId, string appKey, string address, string legacyKey)
        {
            if (string.IsNullOrWhiteSpace(any)) return false;
            any = any.Trim();
            string file = FileId(any);
            foreach (string n in new[] { vehicleId, appKey, address, legacyKey })
            {
                if (string.IsNullOrEmpty(n)) continue;
                if (string.Equals(n, any, StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(file) && string.Equals(FileId(n), file, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
