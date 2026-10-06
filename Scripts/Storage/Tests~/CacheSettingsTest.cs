using System; using System.Collections.Generic; using UnityEngine; using VehicleMeasurement.Storage;
class CacheSettingsTest {
  static int pass, fail;
  static void Check(string n, bool ok, string x = "") { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + n + (x != "" ? "  [" + x + "]" : "")); }
  static void Reset(bool valid, int delay) { Caching.Caches.Clear(); Caching.Delays.Clear(); Caching.SetterIgnored = false; Caching.ThrowOnDefault = false; Caching.IsReady = true;
    Caching.Caches.Add(new Cache { valid = valid, path = "C:/cache" }); if (delay > 0) Caching.Delays["C:/cache"] = delay; }
  static void Main() {
    int target = DasCacheSettings.KeepUnusedBundlesSeconds;
    Check("Target is five years (1825 days)", target / 86400 == 1825);

    Reset(true, 0); Caching.IsReady = false;
    Check("Cache not ready yet -> waits, changes nothing", !DasCacheSettings.TryApply() && DasCacheSettings.LastResult.StartsWith("waiting: the cache isn't ready") && Caching.defaultCache.expirationDelay == 12960000, DasCacheSettings.LastResult);

    Reset(false, 0);
    Check("Cache not valid yet -> waits", !DasCacheSettings.TryApply() && DasCacheSettings.LastResult.StartsWith("waiting: the default cache isn't valid"), DasCacheSettings.LastResult);

    Reset(true, 0);
    Check("Unity default (150 days) -> raised to 1825 and READ BACK", DasCacheSettings.TryApply() && Caching.defaultCache.expirationDelay == target && DasCacheSettings.LastResult.Contains("kept for 1825 days"), DasCacheSettings.LastResult);
    Check("  ...raw numbers recorded: target, before (150 days), after", DasCacheSettings.LastDetail == "target=" + target + "s, before=12960000s, after=" + target + "s", DasCacheSettings.LastDetail);

    Reset(true, target * 2 > 0 ? target + 1000 : target);
    int before = Caching.defaultCache.expirationDelay;
    Check("Already longer than the target -> left alone, never lowered", DasCacheSettings.TryApply() && Caching.defaultCache.expirationDelay == before);

    Reset(true, 0); Caching.SetterIgnored = true;
    bool ok = DasCacheSettings.TryApply();
    Check("Unity ignores the setter (what the real machine showed) -> NOT reported as success, says so", !ok && DasCacheSettings.LastResult.StartsWith("the setting was ignored: still 150 days"), DasCacheSettings.LastResult);
    Check("  ...raw numbers show before == after", DasCacheSettings.LastDetail.EndsWith("before=12960000s, after=12960000s"), DasCacheSettings.LastDetail);

    Reset(true, 0); Caching.ThrowOnDefault = true;
    ok = DasCacheSettings.TryApply();
    Check("Unity throws -> caught, reported, never crashes the app", !ok && DasCacheSettings.LastResult.StartsWith("error on attempt") && DasCacheSettings.LastResult.Contains("boom"), DasCacheSettings.LastResult);

    int attempts = DasCacheSettings.Attempts; Reset(true, 0); DasCacheSettings.TryApply();
    Check("Attempts are counted", DasCacheSettings.Attempts == attempts + 1);
    Console.WriteLine("\nPassed " + pass + " | Failed " + fail); Environment.Exit(fail == 0 ? 0 : 1);
  }
}
