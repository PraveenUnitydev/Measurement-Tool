using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// Downloading or updating several vehicles in one go: a queue that downloads one vehicle at a time (full speed for
    /// each, and a failure doesn't stop the rest), checks the disk space for the whole selection first, and keeps going
    /// when the screen that started it is closed or the user opens another screen. Updates use the same download: the
    /// new files come down and the older version is removed once the new one is on disk (VehicleStorageService).
    /// </summary>
    public static class BatchDownloads
    {
        public enum JobState { Waiting, Downloading, Done, Failed, Cancelled }

        public class Job
        {
            public string vehicleId, name, addressableKey;
            public JobState state = JobState.Waiting;
            public long expectedBytes;
            public VehicleDownloads.Progress progress;
            public string message = "";
        }

        private static readonly List<Job> _jobs = new List<Job>();
        private static bool _running;
        private static bool _cancelRequested;

        /// <summary>Something in the queue changed (state, progress every few frames).</summary>
        public static event Action Changed;
        /// <summary>One vehicle's download finished (state Done or Failed). Used for notifications.</summary>
        public static event Action<Job> JobFinished;

        public static IReadOnlyList<Job> Jobs { get { return _jobs; } }
        public static bool Running { get { return _running; } }
        public static Job Current { get { return _jobs.FirstOrDefault(j => j.state == JobState.Downloading); } }
        public static int WaitingCount { get { return _jobs.Count(j => j.state == JobState.Waiting); } }
        public static int DoneCount { get { return _jobs.Count(j => j.state == JobState.Done); } }
        public static int FailedCount { get { return _jobs.Count(j => j.state == JobState.Failed); } }

        /// <summary>Waiting or downloading (so a screen can show it as "queued").</summary>
        public static bool IsQueued(string vehicleId)
        {
            return _jobs.Any(j => string.Equals(j.vehicleId, vehicleId, StringComparison.OrdinalIgnoreCase)
                                  && (j.state == JobState.Waiting || j.state == JobState.Downloading));
        }

        public static Job Find(string vehicleId)
        {
            for (int i = _jobs.Count - 1; i >= 0; i--)
                if (string.Equals(_jobs[i].vehicleId, vehicleId, StringComparison.OrdinalIgnoreCase)) return _jobs[i];
            return null;
        }

        /// <summary>
        /// Check the disk for a selection before queueing it. Returns null when it fits, otherwise the message to show.
        /// </summary>
        public static string SpaceProblem(long bytesNeeded)
        {
            long waiting = _jobs.Where(j => j.state == JobState.Waiting || j.state == JobState.Downloading).Sum(j => Math.Max(0, j.expectedBytes));
            return DiskSpace.ProblemFor(bytesNeeded + waiting);
        }

        /// <summary>Add vehicles to the queue (ones already queued are skipped). Returns how many were added.</summary>
        public static int Enqueue(IEnumerable<LibraryItem> items)
        {
            int added = 0;
            foreach (LibraryItem i in items ?? Enumerable.Empty<LibraryItem>())
            {
                if (i == null || string.IsNullOrEmpty(i.addressableKey) || IsQueued(i.vehicleId)) continue;
                _jobs.Add(new Job
                {
                    vehicleId = i.vehicleId, name = i.name, addressableKey = i.addressableKey,
                    expectedBytes = i.downloadBytes > 0 ? i.downloadBytes : i.totalBytes,
                });
                added++;
            }
            if (added > 0)
            {
                _cancelRequested = false;
                Raise();
                if (!_running) CoroutineHost.Run(RunQueue());
            }
            return added;
        }

        /// <summary>Stop after the current vehicle (it finishes and is kept); the rest are taken off the queue.</summary>
        public static void CancelRemaining()
        {
            _cancelRequested = true;
            foreach (Job j in _jobs.Where(j => j.state == JobState.Waiting))
            {
                j.state = JobState.Cancelled;
                j.message = "Cancelled";
            }
            Raise();
        }

        /// <summary>Queue the failed ones again.</summary>
        /// <summary>Try one failed vehicle again (from its notification).</summary>
        public static void Retry(string vehicleId)
        {
            Job j = _jobs.FirstOrDefault(x => string.Equals(x.vehicleId, vehicleId, StringComparison.OrdinalIgnoreCase)
                                              && (x.state == JobState.Failed || x.state == JobState.Cancelled));
            if (j == null) return;
            j.state = JobState.Waiting; j.message = ""; j.progress = null;
            _cancelRequested = false;
            Raise();
            if (!_running) CoroutineHost.Run(RunQueue());
        }

        public static void RetryFailed()
        {
            int n = 0;
            foreach (Job j in _jobs.Where(j => j.state == JobState.Failed || j.state == JobState.Cancelled))
            {
                j.state = JobState.Waiting; j.message = ""; j.progress = null; n++;
            }
            if (n > 0)
            {
                _cancelRequested = false;
                Raise();
                if (!_running) CoroutineHost.Run(RunQueue());
            }
        }

        /// <summary>Forget finished, failed and cancelled entries (the list in the screen gets shorter).</summary>
        public static void ClearFinished()
        {
            _jobs.RemoveAll(j => j.state == JobState.Done || j.state == JobState.Failed || j.state == JobState.Cancelled);
            Raise();
        }

        /// <summary>"Downloading 2 of 5: Creta - 120 MB of 400 MB · 8 MB/s" / "3 downloaded, 1 failed" / "".</summary>
        public static string Describe()
        {
            int active = _jobs.Count(j => j.state != JobState.Cancelled);
            Job cur = Current;
            if (cur != null)
            {
                int index = _jobs.Where(j => j.state != JobState.Cancelled).ToList().IndexOf(cur) + 1;
                string p = cur.progress != null && cur.progress.total > 0 ? " - " + VehicleDownloads.Describe(cur.progress) : " - starting";
                return "Downloading " + index + " of " + active + ": " + cur.name + p;
            }
            if (_jobs.Count == 0) return "";
            var parts = new List<string>();
            if (DoneCount > 0) parts.Add(DoneCount + " downloaded");
            if (FailedCount > 0) parts.Add(FailedCount + " failed");
            int cancelled = _jobs.Count(j => j.state == JobState.Cancelled);
            if (cancelled > 0) parts.Add(cancelled + " cancelled");
            return string.Join(", ", parts);
        }

        /// <summary>Overall progress 0..1 for the queue (finished vehicles count fully).</summary>
        public static float OverallFraction()
        {
            var active = _jobs.Where(j => j.state != JobState.Cancelled).ToList();
            if (active.Count == 0) return 0f;
            float sum = 0f;
            foreach (Job j in active)
            {
                if (j.state == JobState.Done || j.state == JobState.Failed) sum += 1f;
                else if (j.state == JobState.Downloading && j.progress != null) sum += Mathf.Clamp01(j.progress.fraction);
            }
            return sum / active.Count;
        }

        private static IEnumerator RunQueue()
        {
            _running = true;
            try
            {
                while (!_cancelRequested)
                {
                    Job job = _jobs.FirstOrDefault(j => j.state == JobState.Waiting);
                    if (job == null) break;
                    job.state = JobState.Downloading;
                    job.message = "";
                    Raise();

                    bool finished = false, ok = false; string message = "";
                    int frame = 0;
                    yield return VehicleDownloads.Download(job.addressableKey,
                        p => { job.progress = p; if (++frame % 10 == 0) Raise(); },
                        (success, msg) => { finished = true; ok = success; message = msg ?? ""; });
                    if (!finished) { ok = false; message = "The download stopped."; }

                    job.state = ok ? JobState.Done : JobState.Failed;
                    job.message = ok ? "Done" : message;
                    if (!ok) Debug.LogWarning("[BatchDownloads] " + job.name + ": " + message);
                    Raise();
                    var finished2 = JobFinished;
                    if (finished2 != null) { try { finished2(job); } catch (Exception e) { Debug.LogWarning("[BatchDownloads] A listener failed: " + e.Message); } }
                    yield return null;
                }
            }
            finally
            {
                _running = false;
                Raise();
            }
        }

        private static void Raise()
        {
            var h = Changed;
            if (h == null) return;
            try { h(); }
            catch (Exception e) { Debug.LogWarning("[BatchDownloads] A screen failed to update: " + e.Message); }
        }

        /// <summary>For tests: empty the queue.</summary>
        public static void ResetForTests() { _jobs.Clear(); _running = false; _cancelRequested = false; }
    }
}
