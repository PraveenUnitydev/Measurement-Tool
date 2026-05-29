using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adjusts a TMP_Text RectTransform width based on character count only.
/// Works best with monospaced fonts or when a rough width is acceptable.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
[RequireComponent(typeof(LayoutElement))]
public class TMPWidthByCharCount : MonoBehaviour
{
    [Header("Sizing")]
    [Tooltip("Pixels per character at the base font size.")]
    public float pixelsPerChar = 10f;

    [Tooltip("Extra pixels added to both sides (left+right total).")]
    public float horizontalPadding = 16f;

    [Tooltip("Minimum preferred width clamp.")]
    public float minWidth = 0f;

    [Tooltip("Maximum preferred width clamp (0 = no cap).")]
    public float maxWidth = 0f;

    [Header("Font Size Scaling")]
    [Tooltip("If enabled, width scales with current TMP fontSize relative to a base size.")]
    public bool scaleWithFontSize = true;

    [Tooltip("The font size at which pixelsPerChar was calibrated.")]
    public float baseFontSize = 36f;

    TMP_Text _tmp;
    LayoutElement _le;

    void Awake()
    {
        _tmp = GetComponent<TMP_Text>();
        _le = GetComponent<LayoutElement>();
    }

    void OnEnable()
    {
        RefreshWidth();
    }

    // Call this after you change text/font size programmatically
    public void RefreshWidth()
    {
        var s = _tmp.text ?? string.Empty;

        // Character-count logic: you can customize what "length" means
        int charCount = VisibleCharCount(s);

        float ppc = pixelsPerChar;
        if (scaleWithFontSize && baseFontSize > 0.01f)
        {
            ppc *= (_tmp.fontSize / baseFontSize);
        }

        float width = charCount * ppc + horizontalPadding;

        if (width < minWidth) width = minWidth;
        if (maxWidth > 0f && width > maxWidth) width = maxWidth;

        _le.preferredWidth = width;

        // If this sits in a HorizontalLayoutGroup, you often want to rebuild the parent
        var parent = transform.parent as RectTransform;
        if (parent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
    }

    public void SetText(string s)
    {
        _tmp.text = s;
        RefreshWidth();
    }

    /// <summary>
    /// Customize which characters count. For example, treat wide CJK chars as 2,
    /// ignore spaces, or collapse multiple spaces.
    /// </summary>
    int VisibleCharCount(string s)
    {
        int count = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            // Example rules:
            // - Ignore spaces:
            // if (c == ' ') continue;

            // - Double width for CJK (very rough):
            // if (IsCJK(c)) { count += 2; continue; }

            count++;
        }
        return count;
    }

    // Rough CJK test if you want to weight differently; not used by default.
    bool IsCJK(char c)
    {
        return (c >= 0x4E00 && c <= 0x9FFF) || // CJK Unified Ideographs
               (c >= 0x3400 && c <= 0x4DBF) || // CJK Extension A
               (c >= 0x3040 && c <= 0x30FF) || // Hiragana + Katakana
               (c >= 0xAC00 && c <= 0xD7AF);   // Hangul
    }
}