using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// Downloading a vehicle's files explicitly (not as a side effect of opening it): how much is missing, enough disk
    /// space, real progress, recorded when done. The same vehicle is never downloaded twice at once - a second request
    /// joins the first. A screen can stop watching a download at any time; the download itself finishes in the
    /// background and is kept.
    /// </summary>
    public static class VehicleDownloads
    {
        public class Progress { public long downloaded, total; public float fraction; public float bytesPerSecond; }

        private class Running
        {
            public AsyncOperationHandle handle;
            public long total;
            public bool done, ok;
            public string error;
        }

        private static readonly Dictionary<string, Running> _running = new Dictionary<string, Running>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True while this vehicle is downloading.</summary>
        public static bool IsDownloading(string key) { Running r; return key != null && _running.TryGetValue(key, out r) && !r.done; }

        /// <summary>Bytes still to download for this vehicle (0 = on disk). -1 if it isn't in the catalog.</summary>
        public static IEnumerator MissingBytes(string key, Action<long> done)
        {
            if (string.IsNullOrEmpty(key)) { done(-1); yield break; }
            var h = Addressables.GetDownloadSizeAsync(key);
            yield return h;
            long size = h.Status == AsyncOperationStatus.Succeeded ? h.Result : -1;
            Addressables.Release(h);
            done(size);
        }

        /// <summary>
        /// Download the vehicle's files. <paramref name="onProgress"/> is called every frame while watching;
        /// <paramref name="done"/> gets (success, message). <paramref name="stopWatching"/> returning true ends the
        /// coroutine early (the download continues and is recorded when it completes).
        /// </summary>
        public static IEnumerator Download(string key, Action<Progress> onProgress, Action<bool, string> done, Func<bool> stopWatching = null)
        {
            long missing = 0;
            yield return MissingBytes(key, m => missing = m);
            if (missing < 0) { done(false, "This vehicle is not in the server catalog."); yield break; }
            if (missing == 0) { Record(key); done(true, "Already on this PC."); yield break; }

            Running run;
            if (!_running.TryGetValue(key, out run) || run.done)
            {
                string space = DiskSpace.ProblemFor(missing);
                if (space != null) { done(false, space); yield break; }
                run = new Running { handle = Addressables.DownloadDependenciesAsync(key), total = missing };
                _running[key] = run;
                CoroutineHost.Run(Watch(key, run));
            }

            var p = new Progress { total = run.total };
            float lastT = Time.realtimeSinceStartup; long lastBytes = 0;
            while (!run.done)
            {
                if (stopWatching != null && stopWatching()) yield break;
                if (run.handle.IsValid())
                {
                    DownloadStatus s = run.handle.GetDownloadStatus();
                    p.downloaded = s.DownloadedBytes;
                    p.total = s.TotalBytes > 0 ? s.TotalBytes : run.total;
                    p.fraction = p.total > 0 ? (float)p.downloaded / p.total : 0f;
                    float now = Time.realtimeSinceStartup;
                    if (now - lastT >= 0.5f) { p.bytesPerSecond = (p.downloaded - lastBytes) / (now - lastT); lastT = now; lastBytes = p.downloaded; }
                }
                if (onProgress != null) onProgress(p);
                yield return null;
            }
            done(run.ok, run.ok ? "Downloaded." : run.error);
        }

        // Owns the handle: waits for the end, records the vehicle, releases the handle - whether or not a screen watches
        private static IEnumerator Watch(string key, Running run)
        {
            while (!run.handle.IsDone) yield return null;
            run.ok = run.handle.Status == AsyncOperationStatus.Succeeded;
            run.error = run.ok ? null : RemoteAddressableVehicleLoader.FriendlyDownloadError(run.handle.OperationException);
            Addressables.Release(run.handle);
            if (run.ok) Record(key);
            run.done = true;
            _running.Remove(key);
        }

        private static void Record(string key)
        {
            var loader = RemoteAddressableVehicleLoader.Instance;
            var info = loader != null ? loader.GetVehicleInfo(key) : null;
            if (info != null) DownloadedVehiclesTracker.MarkAsDownloaded(info);
        }

        /// <summary>"120.5 MB of 402 MB · 8.2 MB/s"</summary>
        public static string Describe(Progress p)
        {
            string s = ByteFormat.Format(p.downloaded) + " of " + ByteFormat.Format(p.total);
            if (p.bytesPerSecond > 0) s += "  -  " + ByteFormat.Format((long)p.bytesPerSecond) + "/s";
            return s;
        }
    }

    /// <summary>A hidden object that runs coroutines which must outlive any screen.</summary>
    public class CoroutineHost : MonoBehaviour
    {
        private static CoroutineHost _instance;
        public static void Run(IEnumerator routine)
        {
            if (_instance == null)
            {
                var go = new GameObject("DAS CoroutineHost");
                go.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<CoroutineHost>();
            }
            _instance.StartCoroutine(routine);
        }
    }
}
