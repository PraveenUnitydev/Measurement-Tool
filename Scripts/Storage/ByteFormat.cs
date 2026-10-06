using System.Globalization;

namespace VehicleMeasurement.Storage
{
    public static class ByteFormat
    {
        /// <summary>Human-readable size: "512 B", "340 KB", "8.4 MB", "412 MB", "1.3 GB".</summary>
        public static string Format(long bytes)
        {
            if (bytes < 0) bytes = 0;
            var c = CultureInfo.InvariantCulture;
            if (bytes < 1024) return bytes.ToString(c) + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0", c) + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return (mb < 10 ? mb.ToString("0.0", c) : mb.ToString("0", c)) + " MB";
            double gb = mb / 1024.0;
            return gb.ToString("0.0", c) + " GB";
        }
    }
}
