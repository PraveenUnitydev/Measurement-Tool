using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// A coloured label at the top-left of a Home card saying at a glance whether the vehicle can be opened right away:
    ///   ON THIS PC (green)  ·  DOWNLOAD NEEDED · 285 MB (orange)  ·  UPDATE AVAILABLE · 120 MB (blue)
    ///   DOWNLOADING 45% (blue)  ·  CAN'T REACH FILES (grey)
    /// Built in code, so the card prefab doesn't need to change. Returns what it showed, so the card can also dim its
    /// picture for vehicles that aren't on this PC.
    /// </summary>
    public static class StorageBadge
    {
        public enum Kind { None, OnPc, DownloadNeeded, Update, Downloading, Unknown }

        private const string BadgeName = "StorageBadge";
        private static readonly Vector2 Offset = new Vector2(10f, -10f);      // from the card's top-left corner
        private const float Height = 30f, FontSize = 16f;

        private static readonly Color GreenBg = new Color(0.13f, 0.42f, 0.22f, 0.95f);
        private static readonly Color OrangeBg = new Color(0.80f, 0.42f, 0.06f, 0.97f);
        private static readonly Color BlueBg = new Color(0.12f, 0.36f, 0.66f, 0.97f);
        private static readonly Color GreyBg = new Color(0.30f, 0.32f, 0.38f, 0.95f);

        /// <summary>Show the vehicle's state on the card. Returns what is shown (None while this PC is still being checked).</summary>
        public static Kind Apply(GameObject card, string vehicleId, string addressableKey)
        {
            if (card == null) return Kind.None;
            Kind kind; string text;
            Describe(vehicleId, addressableKey, out kind, out text);

            Transform existing = card.transform.Find(BadgeName);
            if (kind == Kind.None)
            {
                if (existing != null) Object.Destroy(existing.gameObject);
                return kind;
            }
            GameObject pill = existing != null ? existing.gameObject : Create(card.transform);
            pill.transform.SetAsLastSibling();                          // above the picture
            pill.GetComponent<Image>().color = Background(kind);
            var label = pill.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = text;
            return kind;
        }

        /// <summary>The label for a vehicle (also used by tests and other screens).</summary>
        public static void Describe(string vehicleId, string addressableKey, out Kind kind, out string text)
        {
            kind = Kind.None; text = "";
            var storage = VehicleStorageService.Instance;
            if (storage == null || !storage.IsReady) return;
            VehicleStorageState state = storage.GetState(vehicleId, addressableKey);
            if (state == null) return;                                   // not a server vehicle (built in, or unknown)

            BatchDownloads.Job job = BatchDownloads.Find(state.vehicleId);
            if (job != null && (job.state == BatchDownloads.JobState.Downloading || job.state == BatchDownloads.JobState.Waiting))
            {
                kind = Kind.Downloading;
                text = job.state == BatchDownloads.JobState.Waiting ? "QUEUED FOR DOWNLOAD"
                     : job.progress != null && job.progress.total > 0 ? "DOWNLOADING " + Mathf.RoundToInt(job.progress.fraction * 100f) + "%" : "DOWNLOADING";
                return;
            }
            string size = state.downloadBytes > 0 ? "  ·  " + ByteFormat.Format(state.downloadBytes) : "";
            switch (state.status)
            {
                case VehicleStatus.UpToDate:
                    kind = Kind.OnPc; text = "ON THIS PC"; break;
                case VehicleStatus.UpdateAvailable:
                case VehicleStatus.UpdateRequired:
                    kind = Kind.Update; text = "UPDATE AVAILABLE" + size; break;
                case VehicleStatus.NotDownloaded:
                case VehicleStatus.FilesMissing:
                    kind = Kind.DownloadNeeded; text = "DOWNLOAD NEEDED" + size; break;
                default:
                    kind = Kind.Unknown; text = string.IsNullOrEmpty(state.label) ? "CHECKING..." : state.label.ToUpperInvariant(); break;
            }
            if (kind == Kind.OnPc && !state.IsDownloaded) { kind = Kind.Unknown; text = string.IsNullOrEmpty(state.label) ? "NOT AVAILABLE" : state.label.ToUpperInvariant(); }
        }

        private static Color Background(Kind k)
        {
            switch (k)
            {
                case Kind.OnPc: return GreenBg;
                case Kind.DownloadNeeded: return OrangeBg;
                case Kind.Update: case Kind.Downloading: return BlueBg;
                default: return GreyBg;
            }
        }

        private static GameObject Create(Transform card)
        {
            var go = new GameObject(BadgeName, typeof(RectTransform));
            go.transform.SetParent(card, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;        // the card's own layout must not move it

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Offset;
            rt.sizeDelta = new Vector2(10f, Height);

            var bg = go.AddComponent<Image>();
            bg.raycastTarget = false;                                     // never block clicks on the card
            var row = go.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(12, 12, 3, 3);
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true; row.childControlHeight = true;
            row.childForceExpandWidth = false; row.childForceExpandHeight = true;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = FontSize;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return go;
        }

        public static Color ColorFor(LabelTone tone)
        {
            switch (tone)
            {
                case LabelTone.Good: return new Color(0.45f, 0.85f, 0.50f);
                case LabelTone.Warn: return new Color(1.00f, 0.65f, 0.20f);
                case LabelTone.Info: return new Color(0.50f, 0.75f, 1.00f);
                default: return Color.white;
            }
        }
    }
}
