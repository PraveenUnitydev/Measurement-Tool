using UnityEngine;

public class Test : MonoBehaviour
{
    public Texture2D _logo;
    private const string LOGO_RESOURCE_PATH = "Logo/LogoIcon";

    private static Texture2D _cachedLogo = null;
    private static bool _logoLoadAttempted = false;
    public static void SetLogo(Texture2D logo) { _cachedLogo = logo; _logoLoadAttempted = true; }

    private void Start()
    {
        _logo = GetLogo();
        if (_logo != null)
        {
            Debug.LogWarning("Not null " + LOGO_RESOURCE_PATH);
        }
        else
        {
            Debug.LogWarning("logo is null"+LOGO_RESOURCE_PATH);
        }
    }

    private static Texture2D GetLogo()
    {
        if (!_logoLoadAttempted)
        {
            _cachedLogo = Resources.Load<Texture2D>(LOGO_RESOURCE_PATH);
            _logoLoadAttempted = true;
        }
        return _cachedLogo;
    }
}
