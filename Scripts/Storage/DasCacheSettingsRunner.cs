using System.Collections;
using UnityEngine;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// Tries to apply <see cref="DasCacheSettings"/> once a second (for up to 30 seconds) until Unity's cache is
    /// ready and the new value has been read back. Lives in its own file because Unity requires a MonoBehaviour's
    /// class name to match its file name.
    /// </summary>
    internal class DasCacheSettingsRunner : MonoBehaviour
    {
        private IEnumerator Start()
        {
            for (int i = 0; i < 30; i++)
            {
                if (DasCacheSettings.TryApply()) break;
                yield return new WaitForSecondsRealtime(1f);
            }
            Destroy(gameObject);
        }
    }
}
