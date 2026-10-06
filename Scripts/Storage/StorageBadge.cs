using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// Adds (or updates) a small line of text in the corner of a Home card saying whether the vehicle is on this PC:
    /// "On this PC · 211 MB", "Not downloaded · 285 MB", "Update needed · 120 MB".
    /// Built in code, so the card prefab doesn't need to change. Nothing is shown until the storage service is ready.
    /// </summary>
    public static class StorageBadge
    {
        private const string BadgeName = "StorageBadge";

        // Where the line sits on the card (bottom-right corner); change these to move it.
        private static readonly Vector2 Offset = new Vector2(-12f, 8f);
        private static readonly Vector2 Size = new Vector2(320f, 26f);
        private const float FontSize = 16f;

        public static void Apply(GameObject card, string vehicleId, string addressableKey)
        {
            if (card == null) return;
            var storage = VehicleStorageService.Instance;
            if (storage == null || !storage.IsReady) return;

            VehicleStorageState state = storage.GetState(vehicleId, addressableKey);
            Transform existing = card.transform.Find(BadgeName);

            if (state == null || string.IsNullOrEmpty(state.label))
            {
                if (existing != null) Object.Destroy(existing.gameObject);
                return;
            }

            TextMeshProUGUI text = existing != null ? existing.GetComponent<TextMeshProUGUI>() : Create(card.transform);
            if (text == null) return;
            text.text = state.label;
            text.color = ColorFor(state.tone);
        }

        private static TextMeshProUGUI Create(Transform card)
        {
            var go = new GameObject(BadgeName, typeof(RectTransform));
            go.transform.SetParent(card, false);

            // Don't let the card's own layout move or resize it
            var layout = go.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = Offset;
            rt.sizeDelta = Size;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = FontSize;
            text.alignment = TextAlignmentOptions.BottomRight;
            text.raycastTarget = false;          // never block clicks on the card
            return text;
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
