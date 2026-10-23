using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace VehicleMeasurement
{
    /// <summary>
    /// The one place that knows the DAS server address. Scenes keep their own URL fields (Inspector values override
    /// the defaults in code), and several still pointed at retired servers (http://10.204.12.44:8000,
    /// http://mrws180550:8000, http://your-server:8080). A request to a retired server fails with errors such as
    /// "Curl error 52: Empty reply from server". Every component now passes its configured URL through here: a
    /// current URL is used as it is, a retired or empty one is replaced by the real server and a warning says which.
    /// </summary>
    public static class DasServer
    {
        /// <summary>The DAS API on VRSP. Catalog, bundles, thumbnails, sign-in and measurements all live under it.</summary>
        public const string ApiBase = "https://vrc.mahindra.com/api/das";

        // Retired servers and placeholders. localhost / 127.0.0.1 are NOT here, so a developer can still point a scene
        // at a server running on their own PC.
        private static readonly Regex Retired = new Regex(
            @"^\s*https?://(10\.\d{1,3}\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3}|mrws\d+|your-server[^/:]*)(:\d+)?(/|$)",
            RegexOptions.IgnoreCase);

        private static readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary>True for an empty URL, a retired server, or the real server over plain http (it only answers https).</summary>
        public static bool IsStale(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true;
            string u = url.Trim();
            if (Retired.IsMatch(u)) return true;
            if (u.StartsWith("http://vrc.mahindra.com", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>A component's API base URL (e.g. SignInManager.serverBaseUrl): the configured one if current, else <see cref="ApiBase"/>.</summary>
        public static string ResolveApiBase(string configured, string owner)
        {
            string u = (configured ?? "").Trim().TrimEnd('/');
            if (!IsStale(u))
            {
                // The real server but without the /api/das part: add it
                if (u.StartsWith("https://vrc.mahindra.com", StringComparison.OrdinalIgnoreCase) && u.IndexOf("/api/das", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    WarnOnce(owner, configured, ApiBase);
                    return ApiBase;
                }
                return u;
            }
            WarnOnce(owner, configured, ApiBase);
            return ApiBase;
        }

        /// <summary>A full endpoint URL (e.g. NetworkChecker.pingUrl): the configured one if current, else ApiBase + <paramref name="pathOnServer"/>.</summary>
        public static string ResolveEndpoint(string configured, string pathOnServer, string owner)
        {
            if (!IsStale(configured)) return configured.Trim();
            string fixedUrl = ApiBase + pathOnServer;
            WarnOnce(owner, configured, fixedUrl);
            return fixedUrl;
        }

        /// <summary>
        /// A content URL taken from data (a thumbnail in the catalog, say). One on a retired server is moved to the
        /// same path on the DAS API (the old file server's root is the API's root); anything else is left alone.
        /// </summary>
        public static string RewriteContentUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !IsStale(url)) return url;
            var m = Regex.Match(url.Trim(), @"^https?://[^/]+(?<path>/.*)?$");
            string path = m.Success && m.Groups["path"].Success ? m.Groups["path"].Value : "/";
            path = Regex.Replace(path, @"^/Bundles/", "/bundles/", RegexOptions.IgnoreCase);
            path = Regex.Replace(path, @"^/Thumbnails?/", "/thumbnails/", RegexOptions.IgnoreCase);
            return ApiBase + path;
        }

        /// <summary>A URL safe to write to the log: no query string (signed storage links carry their signature there).</summary>
        public static string ForLog(string url)
        {
            if (string.IsNullOrEmpty(url)) return "(no url)";
            int q = url.IndexOf('?');
            return q >= 0 ? url.Substring(0, q) + "?…" : url;
        }

        private static void WarnOnce(string owner, string configured, string used)
        {
            string key = owner + "|" + configured;
            if (!_warned.Add(key)) return;
            Debug.LogWarning("[DasServer] " + owner + " is set to '" + (string.IsNullOrWhiteSpace(configured) ? "(empty)" : configured)
                + "', which is not the DAS server. Using " + used + " instead. Fix the value in the scene (menu: DAS > Project Doctor) to remove this warning.");
        }
    }

    /// <summary>
    /// Sends web requests the robust way. The server closes connections that have been idle for a few seconds; when
    /// UnityWebRequest re-uses such a connection the request fails at once with "Curl error 52: Empty reply from
    /// server" (or "connection reset"), even though the server is fine. A request can't be sent twice, so the caller
    /// passes a function that builds it; on that kind of error a fresh request is sent once more. Every request gets a
    /// timeout, and connection failures are logged with the URL (without its query string).
    /// </summary>
    public static class DasHttp
    {
        public const int DefaultTimeoutSeconds = 15;

        /// <summary>True for the errors a closed, re-used connection produces. Only these are retried.</summary>
        public static bool IsClosedConnection(UnityWebRequest req)
        {
            if (req == null || req.result != UnityWebRequest.Result.ConnectionError) return false;
            string e = (req.error ?? "").ToLowerInvariant();
            return e.Contains("error 52") || e.Contains("empty reply") || e.Contains("reset") || e.Contains("recv failure")
                || e.Contains("failed to receive") || e.Contains("connection was closed") || e.Contains("ssl_error_syscall")
                || e.Contains("error 55") || e.Contains("error 56");
        }

        /// <summary>
        /// Build a request with <paramref name="make"/>, send it, and hand the finished request to <paramref name="done"/>.
        /// The caller owns the request it receives and must Dispose it (use <c>using (req) { ... }</c>).
        /// </summary>
        public static IEnumerator Send(Func<UnityWebRequest> make, Action<UnityWebRequest> done, int attempts = 2)
        {
            UnityWebRequest req = null;
            for (int attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
            {
                req = make();
                if (req.timeout <= 0) req.timeout = DefaultTimeoutSeconds;
                yield return req.SendWebRequest();

                if (attempt < attempts && IsClosedConnection(req))
                {
                    Debug.Log("[Net] " + req.method + " " + DasServer.ForLog(req.url) + ": " + req.error + " - the connection had been closed; sending again.");
                    req.Dispose();
                    yield return new WaitForSecondsRealtime(0.25f);
                    continue;
                }
                break;
            }

            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError)
                Debug.LogWarning("[Net] " + req.method + " " + DasServer.ForLog(req.url) + " failed: " + req.error);

            if (done != null) done(req);
            else req.Dispose();
        }
    }
}
