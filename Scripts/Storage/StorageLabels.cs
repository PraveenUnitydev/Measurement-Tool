namespace VehicleMeasurement.Storage
{
    public enum LabelTone { None, Good, Warn, Info }

    /// <summary>What to tell the user about a vehicle's files, in plain words.</summary>
    public class StorageLabel
    {
        public string text = "";
        public LabelTone tone = LabelTone.None;
    }

    public static class StorageLabels
    {
        /// <param name="canKeepOldVersions">False until vehicles can be pinned to the version they were downloaded with:
        /// until then an out-of-date vehicle has to be updated before it can be opened, and the label says so.</param>
        public static StorageLabel For(VehicleStatusInfo info, long totalBytes, bool canKeepOldVersions)
        {
            var label = new StorageLabel();
            if (info == null) return label;

            switch (info.status)
            {
                case VehicleStatus.NotDownloaded:
                    label.text = "Not downloaded" + Size(info.downloadBytes > 0 ? info.downloadBytes : totalBytes);
                    label.tone = LabelTone.Warn;
                    break;
                case VehicleStatus.UpToDate:
                    label.text = "On this PC" + Size(totalBytes) + (info.removedFromServer ? " (no longer on the server)" : "");
                    label.tone = LabelTone.Good;
                    break;
                case VehicleStatus.UpdateAvailable:
                    label.text = (canKeepOldVersions ? "Update available" : "Update needed") + Size(info.downloadBytes);
                    label.tone = canKeepOldVersions ? LabelTone.Info : LabelTone.Warn;
                    break;
                case VehicleStatus.UpdateRequired:
                    label.text = "Update needed" + Size(info.downloadBytes);
                    label.tone = LabelTone.Warn;
                    break;
                case VehicleStatus.FilesMissing:
                    label.text = "Files missing" + Size(info.downloadBytes);
                    label.tone = LabelTone.Warn;
                    break;
                default:
                    break;      // Unknown: say nothing rather than something wrong
            }
            return label;
        }

        private static string Size(long bytes) { return bytes > 0 ? " · " + ByteFormat.Format(bytes) : ""; }
    }
}
