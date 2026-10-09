using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace VehicleMeasurement.EditorTools.Publish
{
    /// <summary>
    /// Talks to VRSP for publishing vehicles from Unity: publishing sign-in (Microsoft account, its own session - it
    /// never signs anyone out of the DAS app), my vehicles, start / upload / commit, history and rollback.
    /// Requests run on the editor's main thread (await is fine in EditorWindow code).
    /// </summary>
    public class DasPublishClient
    {
        private const string TokenKey = "DAS.Publish.Token";
        private const string NameKey = "DAS.Publish.Name";
        private const string ServerKey = "DAS.Publish.Server";

        public static string ServerBase
        {
            get { return EditorPrefs.GetString(ServerKey, DasServer.ApiBase); }
            set { EditorPrefs.SetString(ServerKey, string.IsNullOrWhiteSpace(value) ? DasServer.ApiBase : value.Trim().TrimEnd('/')); }
        }

        public string Token { get { return EditorPrefs.GetString(TokenKey, ""); } private set { EditorPrefs.SetString(TokenKey, value ?? ""); } }
        public string SignedInAs { get { return EditorPrefs.GetString(NameKey, ""); } private set { EditorPrefs.SetString(NameKey, value ?? ""); } }
        public bool IsSignedIn { get { return !string.IsNullOrEmpty(Token); } }

        public void SignOut() { Token = ""; SignedInAs = ""; }

        // ── JSON shapes (JsonUtility) ────────────────────────────────────
        [Serializable] public class StartSignIn { public string pairingId, pollToken, userCode, loginUrl; public int pollIntervalSeconds = 4; public string error; }
        [Serializable] private class PollBody { public string pairingId, pollToken; }
        [Serializable] public class PollResult { public string status, message, token, email, displayName, role, error; }
        [Serializable] private class SignInBody { public string purpose = "publish"; public string deviceName; public string deviceId; }
        [Serializable] public class MyVehicle { public string vehicleId, vehicleName, manufacturer, modelYear, category, version, publishedBy, publishedAt; public bool published; }
        [Serializable] public class Me { public string email, name, role, error, code; public bool canCreate; public List<MyVehicle> vehicles = new List<MyVehicle>(); }
        [Serializable] public class StartBody { public string vehicleId, vehicleName, manufacturer, modelYear, category, description, notes; }
        [Serializable] public class StartResult { public string publishId, version, folder, loadPath, addressableKey, error, code; }
        [Serializable] public class FileEntry { public string path; public long size; }
        [Serializable] private class FilesBody { public List<FileEntry> files = new List<FileEntry>(); }
        [Serializable] public class Upload { public string path, url, contentType; }
        [Serializable] public class UploadsResult { public List<Upload> uploads = new List<Upload>(); public string error, code; }
        [Serializable] private class CommitBody { public string catalogFile, thumbnailFile; }
        [Serializable] public class SimpleResult { public bool ok; public string error, code, version, vehicleId; }
        [Serializable] public class HistoryRow { public string version, by, at, size, notes; }
        [Serializable] public class History { public string current, error; public List<string> kept = new List<string>(); public List<HistoryRow> history = new List<HistoryRow>(); }
        [Serializable] private class RollbackBody { public string vehicleId, version; }
        [Serializable] private class ThumbRequest { public string vehicleId; public long size; }
        [Serializable] public class ThumbUpload { public string path, url, contentType, error, code; }
        [Serializable] public class DetailsBody { public string vehicleId, vehicleName, manufacturer, modelYear, category, description, thumbnailPath; }

        // ── sign-in ──────────────────────────────────────────────────────

        public async Task<StartSignIn> BeginSignIn()
        {
            var body = new SignInBody { deviceName = ("Unity Editor - " + SystemInfo.deviceName).Substring(0, Math.Min(90, ("Unity Editor - " + SystemInfo.deviceName).Length)), deviceId = SystemInfo.deviceUniqueIdentifier };
            var r = await Send("POST", "/auth/start", JsonUtility.ToJson(body), false);
            var res = Parse<StartSignIn>(r);
            if (res != null && !string.IsNullOrEmpty(res.loginUrl)) Application.OpenURL(res.loginUrl);
            return res;
        }

        /// <summary>One poll. Returns "pending", "approved", "denied" or "expired" (and stores the token when approved).</summary>
        public async Task<PollResult> Poll(StartSignIn s)
        {
            var r = await Send("POST", "/auth/poll", JsonUtility.ToJson(new PollBody { pairingId = s.pairingId, pollToken = s.pollToken }), false);
            var res = Parse<PollResult>(r) ?? new PollResult { status = "pending" };
            if (res.status == "approved" && !string.IsNullOrEmpty(res.token))
            {
                Token = res.token;
                SignedInAs = (res.displayName ?? "") + " (" + (res.email ?? "") + ")";
            }
            return res;
        }

        // ── publishing ───────────────────────────────────────────────────

        public async Task<Me> GetMe() { return Parse<Me>(await Send("GET", "/publish/me", null, true)); }

        public async Task<StartResult> Start(StartBody b) { return Parse<StartResult>(await Send("POST", "/publish/start", JsonUtility.ToJson(b), true)); }

        public async Task<UploadsResult> RequestUploads(string publishId, List<FileEntry> files)
        {
            var body = new FilesBody { files = files };
            return Parse<UploadsResult>(await Send("POST", "/publish/" + publishId + "/uploads", JsonUtility.ToJson(body), true));
        }

        public async Task<SimpleResult> Commit(string publishId, string catalogFile, string thumbnailFile)
        {
            var body = new CommitBody { catalogFile = catalogFile, thumbnailFile = thumbnailFile ?? "" };
            return Parse<SimpleResult>(await Send("POST", "/publish/" + publishId + "/commit", JsonUtility.ToJson(body), true));
        }

        public async Task<History> GetHistory(string vehicleId) { return Parse<History>(await Send("GET", "/publish/history/" + Uri.EscapeDataString(vehicleId), null, true)); }

        public async Task<SimpleResult> Rollback(string vehicleId, string version)
        {
            return Parse<SimpleResult>(await Send("POST", "/publish/rollback", JsonUtility.ToJson(new RollbackBody { vehicleId = vehicleId, version = version }), true));
        }

        /// <summary>Signed upload link for a new thumbnail of an already published vehicle (no build).</summary>
        public async Task<ThumbUpload> RequestDetailsThumbnail(string vehicleId, long size)
        {
            return Parse<ThumbUpload>(await Send("POST", "/publish/details/thumbnail", JsonUtility.ToJson(new ThumbRequest { vehicleId = vehicleId, size = size }), true));
        }

        /// <summary>Change name / maker / year / category / description and optionally the thumbnail, without a new version.</summary>
        public async Task<SimpleResult> SaveDetails(DetailsBody b) { return Parse<SimpleResult>(await Send("POST", "/publish/details", JsonUtility.ToJson(b), true)); }

        /// <summary>Upload one file to a signed link (streamed from disk). Retries twice. Returns null or the error.</summary>
        public static async Task<string> UploadFile(string localPath, Upload u, Action<float> progress)
        {
            string last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                using (var req = new UnityWebRequest(u.url, "PUT"))
                {
                    req.uploadHandler = new UploadHandlerFile(localPath);
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", string.IsNullOrEmpty(u.contentType) ? "application/octet-stream" : u.contentType);
                    req.timeout = 0;                                            // big files: no overall timeout
                    var op = req.SendWebRequest();
                    while (!op.isDone)
                    {
                        if (progress != null) progress(req.uploadProgress);
                        await Task.Delay(100);
                    }
                    if (req.result == UnityWebRequest.Result.Success) { if (progress != null) progress(1f); return null; }
                    last = req.responseCode > 0 ? "HTTP " + req.responseCode + " " + Short(req.downloadHandler.text) : req.error;
                }
                await Task.Delay(1000 * attempt);
            }
            return last;
        }

        // ── plumbing ─────────────────────────────────────────────────────

        public class Response { public long code; public string text; public string error; }

        private async Task<Response> Send(string method, string path, string json, bool auth)
        {
            using (var req = new UnityWebRequest(ServerBase + path, method))
            {
                if (json != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = 60;
                if (auth) req.SetRequestHeader("Authorization", "Bearer " + Token);
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Delay(50);
                var res = new Response { code = req.responseCode, text = req.downloadHandler != null ? req.downloadHandler.text : "" };
                if (req.result == UnityWebRequest.Result.ConnectionError) res.error = "Can't reach " + ServerBase + ": " + req.error;
                if (auth && req.responseCode == 401) { Token = ""; }               // session ended: sign in again
                return res;
            }
        }

        private static T Parse<T>(Response r) where T : class, new()
        {
            if (r == null) return null;
            T obj = null;
            try { if (!string.IsNullOrEmpty(r.text) && r.text.TrimStart().StartsWith("{")) obj = JsonUtility.FromJson<T>(r.text); } catch (Exception) { }
            if (obj == null) obj = new T();
            var f = typeof(T).GetField("error");
            if (f != null && string.IsNullOrEmpty((string)f.GetValue(obj)))
            {
                if (r.error != null) f.SetValue(obj, r.error);
                else if (r.code >= 400) f.SetValue(obj, "Server said HTTP " + r.code + (r.code == 401 ? " - sign in again." : ""));
            }
            return obj;
        }

        private static string Short(string s) { return string.IsNullOrEmpty(s) ? "" : (s.Length > 200 ? s.Substring(0, 200) + "..." : s); }
    }
}
