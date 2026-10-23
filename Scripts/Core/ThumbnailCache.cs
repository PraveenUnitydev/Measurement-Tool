using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// Vehicle thumbnails for the screens, loaded without freezing the app.
    /// Before: Home read and decoded every card's PNG on the main thread, on every refresh, and never released the
    /// textures - with dozens of vehicles that was the multi-second freeze when Home opened (and leaked memory).
    /// Now: a file is read on a worker thread, decoded on the main thread at most one per frame, and kept until the
    /// file changes on disk (last-write time and size are compared). Screens show cards at once and the pictures
    /// fill in.
    /// </summary>
    public static class ThumbnailCache
    {
        private class Entry { public DateTime written; public long length; public Sprite sprite; }

        private static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<Action<Sprite>>> _waiting = new Dictionary<string, List<Action<Sprite>>>(StringComparer.OrdinalIgnoreCase);
        private static int _lastDecodeFrame = -1;

        /// <summary>Loads in progress (for progress displays).</summary>
        public static int Pending { get { return _waiting.Count; } }

        /// <summary>The cached sprite if this file is already loaded and unchanged; otherwise null (no disk read, no decode).</summary>
        public static Sprite TryGet(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Entry e;
            if (!_cache.TryGetValue(path, out e) || e.sprite == null) return null;
            FileInfo info;
            try { info = new FileInfo(path); } catch (Exception) { return null; }
            if (!info.Exists) { Forget(path); return null; }
            return info.LastWriteTimeUtc == e.written && info.Length == e.length ? e.sprite : null;
        }

        /// <summary>
        /// Load a picture file and call <paramref name="done"/> with the sprite (null if missing or unreadable).
        /// Calls for the same file while it is loading share one load. Run it with StartCoroutine.
        /// </summary>
        public static IEnumerator Load(string path, Action<Sprite> done)
        {
            Sprite hit = TryGet(path);
            if (hit != null || string.IsNullOrEmpty(path)) { if (done != null) done(hit); yield break; }

            List<Action<Sprite>> listeners;
            if (_waiting.TryGetValue(path, out listeners)) { if (done != null) listeners.Add(done); yield break; }
            listeners = new List<Action<Sprite>>();
            if (done != null) listeners.Add(done);
            _waiting[path] = listeners;

            Sprite sprite = null;
            byte[] bytes = null;
            DateTime written = default(DateTime);
            long length = 0;

            // read on a worker thread
            Task<byte[]> read = Task.Run(() =>
            {
                var info = new FileInfo(path);
                if (!info.Exists) return null;
                written = info.LastWriteTimeUtc;
                length = info.Length;
                return File.ReadAllBytes(path);
            });
            while (!read.IsCompleted) yield return null;
            if (read.Status == TaskStatus.RanToCompletion) bytes = read.Result;

            if (bytes != null && bytes.Length > 0)
            {
                // decode on the main thread, at most one picture per frame
                while (_lastDecodeFrame == Time.frameCount) yield return null;
                _lastDecodeFrame = Time.frameCount;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(bytes, true))
                {
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    _cache[path] = new Entry { written = written, length = length, sprite = sprite };
                }
                else
                {
                    UnityEngine.Object.Destroy(texture);
                    Debug.LogWarning("[Thumbnails] Not a readable picture: " + path);
                }
            }

            _waiting.Remove(path);
            foreach (var l in listeners)
            {
                try { l(sprite); }
                catch (Exception e) { Debug.LogWarning("[Thumbnails] A screen failed to show a picture: " + e.Message); }
            }
        }

        /// <summary>Drop a file from the cache and free its texture (e.g. after the vehicle was deleted).</summary>
        public static void Forget(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            // Only dropped from the cache: a card may still show this picture, and destroying it would empty the card
            _cache.Remove(path);
        }
    }
}
