using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// VEHICLE PDF EXPORTER (v2 - With Save Dialog)
    /// 
    /// Features:
    /// - Opens Windows Save Dialog for user to choose location and filename
    /// - Company logo in header and footer
    /// - CONFIDENTIAL watermark
    /// - Print-friendly white theme
    /// 
    /// USAGE:
    /// VehiclePDFExporter.ExportMeasurement(vehicleData, camera, (success, path) => {
    ///     if (success) Debug.Log("Saved to: " + path);
    /// });
    /// </summary>
    public static class VehiclePDFExporter
    {
        private const float PAGE_WIDTH = 595f;
        private const float PAGE_HEIGHT = 842f;
        private const float MARGIN = 40f;
        private const float CONTENT_WIDTH = PAGE_WIDTH - (MARGIN * 2);
        private const float LOGO_MAX_WIDTH = 120f;
        private const float LOGO_MAX_HEIGHT = 50f;
        private const string LOGO_RESOURCE_PATH = "Logo/LogoIcon";
        private const string CONFIDENTIAL_TEXT = "CONFIDENTIAL - FOR INTERNAL USE ONLY";

        private static readonly Color32 COLOR_PRIMARY = new Color32(30, 60, 114, 255);
        private static readonly Color32 COLOR_SECONDARY = new Color32(100, 100, 100, 255);
        private static readonly Color32 COLOR_POSITIVE = new Color32(34, 139, 34, 255);
        private static readonly Color32 COLOR_NEGATIVE = new Color32(200, 50, 50, 255);
        private static readonly Color32 COLOR_BORDER = new Color32(200, 200, 200, 255);
        private static readonly Color32 COLOR_CONFIDENTIAL = new Color32(180, 0, 0, 255);

        private static Texture2D _cachedLogo = null;
        private static bool _logoLoadAttempted = false;

        public static void SetLogo(Texture2D logo) { _cachedLogo = logo; _logoLoadAttempted = true; }

        private static Texture2D GetLogo()
        {
            if (!_logoLoadAttempted)
            {
                _cachedLogo = Resources.Load<Texture2D>(LOGO_RESOURCE_PATH);
                _logoLoadAttempted = true;
            }
            return _cachedLogo;
        }

        #region Public API

        public static void ExportMeasurement(SavedVehicleMeasurement data, Camera camera, Action<bool, string> onComplete = null)
        {
            if (data == null) { onComplete?.Invoke(false, null); return; }
            PDFExporterHelper.Instance.StartCoroutine(ExportMeasurementCoroutine(data, camera, onComplete));
        }

        // VehiclePDFExporter.cs (inside VehiclePDFExporter class)

        public static void ExportComparison(
            SavedVehicleMeasurement a,
            SavedVehicleMeasurement b,
            VehiclePreviewRenderer previewA,
            VehiclePreviewRenderer previewB,
            Action<bool, string> onComplete = null)
        {
            if (a == null || b == null)
            {
                onComplete?.Invoke(false, null);
                return;
            }

            PDFExporterHelper.Instance.StartCoroutine(
                ExportComparisonCoroutine(a, b, previewA, previewB, onComplete)
            );
        }

        #endregion

        #region Coroutines

        private static IEnumerator ExportMeasurementCoroutine(SavedVehicleMeasurement data, Camera camera, Action<bool, string> onComplete)
        {
            Texture2D screenshot = null;
            if (camera != null) yield return CaptureScreenshot(camera, tex => screenshot = tex);

            string defaultName = $"{SanitizeFileName(data.vehicleName)}_Measurement_{DateTime.Now:yyyyMMdd}";
            string filePath = WindowsFileDialog.SaveFileDialog("Save Measurement Report", FileDialogFilter.PDF, "pdf", defaultName);

            if (string.IsNullOrEmpty(filePath))
            {
                if (screenshot != null) UnityEngine.Object.Destroy(screenshot);
                onComplete?.Invoke(false, null);
                yield break;
            }

            bool success = GenerateMeasurementPDF(data, screenshot, filePath);
            if (screenshot != null) UnityEngine.Object.Destroy(screenshot);
            onComplete?.Invoke(success, filePath);
        }

        // VehiclePDFExporter.cs (inside VehiclePDFExporter class)

        private static IEnumerator ExportComparisonCoroutine(
            SavedVehicleMeasurement a,
            SavedVehicleMeasurement b,
            VehiclePreviewRenderer previewA,
            VehiclePreviewRenderer previewB,
            Action<bool, string> onComplete)
        {
            Texture2D shotA = null;
            Texture2D shotB = null;

            // Capture preview A
            if (previewA != null)
                yield return previewA.CapturePreviewTexture(tex => shotA = tex);

            // Capture preview B
            if (previewB != null)
                yield return previewB.CapturePreviewTexture(tex => shotB = tex);

            string defaultName =
                $"Comparison_{SanitizeFileName(a.vehicleName)}_vs_{SanitizeFileName(b.vehicleName)}_{DateTime.Now:yyyyMMdd}";

            string filePath = WindowsFileDialog.SaveFileDialog(
                "Save Comparison Report", FileDialogFilter.PDF, "pdf", defaultName);

            if (string.IsNullOrEmpty(filePath))
            {
                if (shotA != null) UnityEngine.Object.Destroy(shotA);
                if (shotB != null) UnityEngine.Object.Destroy(shotB);
                onComplete?.Invoke(false, null);
                yield break;
            }

            bool success = GenerateComparisonPDF(a, b, shotA, shotB, filePath);

            if (shotA != null) UnityEngine.Object.Destroy(shotA);
            if (shotB != null) UnityEngine.Object.Destroy(shotB);

            onComplete?.Invoke(success, filePath);
        }

        private static IEnumerator CaptureScreenshot(Camera camera, Action<Texture2D> onCaptured)
        {
            yield return new WaitForEndOfFrame();

            RenderTexture rt = new RenderTexture(Screen.width, Screen.height, 24);
            camera.targetTexture = rt;

            Texture2D screenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            camera.Render();

            RenderTexture.active = rt;
            screenshot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            screenshot.Apply();

            camera.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.Destroy(rt);

            onCaptured?.Invoke(screenshot);
        }

        #endregion

        #region PDF Generation

        private static bool GenerateMeasurementPDF(SavedVehicleMeasurement data, Texture2D screenshot, string filePath)
        {
            try
            {
                var pdf = new SimplePDFWriter();
                float y = PAGE_HEIGHT - MARGIN;

                y = DrawHeaderWithLogo(pdf, "VEHICLE MEASUREMENT REPORT", y);
                y = DrawVehicleInfo(pdf, data, y);

                if (screenshot != null) { y -= 15; y = DrawScreenshot(pdf, screenshot, y, 260); }

                y -= 20;
                y = DrawMeasurementsTable(pdf, data, y);
                DrawFooterWithConfidential(pdf, data.savedDate ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                pdf.Save(filePath);
                return true;
            }
            catch (Exception e) { Debug.LogError($"[PDFExporter] {e.Message}"); return false; }
        }

        // VehiclePDFExporter.cs (inside VehiclePDFExporter class)

        private static bool GenerateComparisonPDF(
            SavedVehicleMeasurement a,
            SavedVehicleMeasurement b,
            Texture2D screenshotA,
            Texture2D screenshotB,
            string filePath)
        {
            try
            {
                var pdf = new SimplePDFWriter();
                float y = PAGE_HEIGHT - MARGIN;

                y = DrawHeaderWithLogo(pdf, "VEHICLE COMPARISON REPORT", y);
                y -= 5;

                pdf.AddText($"Vehicle A: {a.vehicleName}", MARGIN, y, 11, COLOR_PRIMARY, true);
                pdf.AddText($"Vehicle B: {b.vehicleName}", PAGE_WIDTH / 2, y, 11, COLOR_PRIMARY, true);
                y -= 18;

                // NEW: side-by-side preview block
                if (screenshotA != null || screenshotB != null)
                {
                    y = DrawSideBySideScreenshots(pdf, a.vehicleName, b.vehicleName, screenshotA, screenshotB, y, 200f);
                }

                y -= 15;
                y = DrawComparisonTable(pdf, a, b, y);

                DrawFooterWithConfidential(pdf, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                pdf.Save(filePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PDFExporter] {e.Message}");
                return false;
            }
        }

        #endregion

        #region Drawing

        private static float DrawHeaderWithLogo(SimplePDFWriter pdf, string title, float y)
        {
            Texture2D logo = GetLogo();
            float titleX = MARGIN;

            if (logo != null)
            {
                float ar = (float)logo.width / logo.height;
                float w = LOGO_MAX_WIDTH, h = w / ar;
                if (h > LOGO_MAX_HEIGHT) { h = LOGO_MAX_HEIGHT; w = h * ar; }
                pdf.AddImage(logo, MARGIN, y - h + 12, w, h);
                titleX = MARGIN + w + 15;
            }

            pdf.AddText(title, titleX, y, 18, COLOR_PRIMARY, true);
            y -= 22;
            pdf.AddText("SAE J1100 Dimensional Analysis", titleX, y, 10, COLOR_SECONDARY);
            y -= 15;
            pdf.AddLine(MARGIN, y, PAGE_WIDTH - MARGIN, y, COLOR_BORDER, 1);
            return y - 15;
        }

        private static float DrawVehicleInfo(SimplePDFWriter pdf, SavedVehicleMeasurement data, float y)
        {
            pdf.AddText("VEHICLE INFORMATION", MARGIN, y, 11, COLOR_PRIMARY, true);
            y -= 18;
            pdf.AddText("Name:", MARGIN, y, 9, COLOR_SECONDARY);
            pdf.AddText(data.vehicleName ?? "N/A", MARGIN + 80, y, 9, COLOR_PRIMARY);
            pdf.AddText("Date:", PAGE_WIDTH / 2, y, 9, COLOR_SECONDARY);
            pdf.AddText(data.savedDate ?? "N/A", PAGE_WIDTH / 2 + 60, y, 9, COLOR_PRIMARY);
            return y - 14;
        }

        private static float DrawScreenshot(SimplePDFWriter pdf, Texture2D img, float y, float maxH)
        {
            float ar = (float)img.width / img.height;
            float w = CONTENT_WIDTH, h = w / ar;
            if (h > maxH) { h = maxH; w = h * ar; }
            float x = MARGIN + (CONTENT_WIDTH - w) / 2;
            float iy = y - h;
            pdf.AddImage(img, x, iy, w, h);
            pdf.AddRect(x, iy, w, h, COLOR_BORDER, 0.5f);
            return iy - 10;
        }

        private static float DrawMeasurementsTable(SimplePDFWriter pdf, SavedVehicleMeasurement d, float y)
        {
            pdf.AddText("MEASUREMENTS (SAE J1100)", MARGIN, y, 11, COLOR_PRIMARY, true);
            y -= 18;

            float[] c = { 90, 170, 130 };
            y = DrawTableHeader(pdf, new[] { "Parameter", "Description", "Value (mm)" }, y, c);

            y -= 3; y = DrawSection(pdf, "LENGTH", y);
            y = DrawRow(pdf, "L103", "Overall Length", d.L103_OverallLength, y, c);
            y = DrawRow(pdf, "L101", "Wheelbase", d.L101_Wheelbase, y, c);
            y = DrawRow(pdf, "L104", "Front Overhang", d.L104_FrontOverhang, y, c);
            y = DrawRow(pdf, "L105", "Rear Overhang", d.L105_RearOverhang, y, c);

            y -= 6; y = DrawSection(pdf, "WIDTH", y);
            y = DrawRow(pdf, "W103", "Overall Width", d.W103_OverallWidth, y, c);
            y = DrawRow(pdf, "W144", "Front Track", d.W144_FrontTrack, y, c);
            y = DrawRow(pdf, "W145", "Rear Track", d.W145_RearTrack, y, c);

            y -= 6; y = DrawSection(pdf, "HEIGHT", y);
            y = DrawRow(pdf, "H100", "Overall Height", d.H100_OverallHeight, y, c);
            y = DrawRow(pdf, "H101", "Ground Clearance", d.H101_GroundClearance, y, c);

            y -= 6; y = DrawSection(pdf, "WHEELS", y);
            y = DrawRow(pdf, "TD-F", "Front Tyre Diameter", d.TD_F_FrontDiameter, y, c);
            y = DrawRow(pdf, "TD-R", "Rear Tyre Diameter", d.TD_R_RearDiameter, y, c);

            return y;
        }

        private static float DrawComparisonTable(SimplePDFWriter pdf, SavedVehicleMeasurement a, SavedVehicleMeasurement b, float y)
        {
            pdf.AddText("MEASUREMENT COMPARISON", MARGIN, y, 11, COLOR_PRIMARY, true);
            y -= 18;

            float[] c = { 65, 80, 80, 75, 55, 35 };
            y = DrawTableHeader(pdf, new[] { "Param", Truncate(a.vehicleName, 10), Truncate(b.vehicleName, 10), "Diff", "% Diff", "" }, y, c);

            y -= 3; y = DrawCompSection(pdf, "LENGTH", y);
            y = DrawCompRow(pdf, "L103", a.L103_OverallLength, b.L103_OverallLength, y, c);
            y = DrawCompRow(pdf, "L101", a.L101_Wheelbase, b.L101_Wheelbase, y, c);
            y = DrawCompRow(pdf, "L104", a.L104_FrontOverhang, b.L104_FrontOverhang, y, c);
            y = DrawCompRow(pdf, "L105", a.L105_RearOverhang, b.L105_RearOverhang, y, c);

            y -= 5; y = DrawCompSection(pdf, "WIDTH", y);
            y = DrawCompRow(pdf, "W103", a.W103_OverallWidth, b.W103_OverallWidth, y, c);
            y = DrawCompRow(pdf, "W144", a.W144_FrontTrack, b.W144_FrontTrack, y, c);
            y = DrawCompRow(pdf, "W145", a.W145_RearTrack, b.W145_RearTrack, y, c);

            y -= 5; y = DrawCompSection(pdf, "HEIGHT", y);
            y = DrawCompRow(pdf, "H100", a.H100_OverallHeight, b.H100_OverallHeight, y, c);
            y = DrawCompRow(pdf, "H101", a.H101_GroundClearance, b.H101_GroundClearance, y, c);

            y -= 5; y = DrawCompSection(pdf, "WHEELS", y);
            y = DrawCompRow(pdf, "TD-F", a.TD_F_FrontDiameter, b.TD_F_FrontDiameter, y, c);
            y = DrawCompRow(pdf, "TD-R", a.TD_R_RearDiameter, b.TD_R_RearDiameter, y, c);

            return y;
        }

        private static float DrawTableHeader(SimplePDFWriter pdf, string[] cells, float y, float[] cols)
        {
            float x = MARGIN;
            for (int i = 0; i < cells.Length && i < cols.Length; i++) { pdf.AddText(cells[i], x, y, 9, COLOR_PRIMARY, true); x += cols[i]; }
            y -= 14;
            float totalW = 0; foreach (var w in cols) totalW += w;
            pdf.AddLine(MARGIN, y + 3, MARGIN + totalW, y + 3, COLOR_BORDER, 0.5f);
            return y;
        }

        private static float DrawSection(SimplePDFWriter pdf, string name, float y) { pdf.AddText(name, MARGIN, y, 9, COLOR_SECONDARY, true); return y - 12; }
        private static float DrawCompSection(SimplePDFWriter pdf, string name, float y) { pdf.AddText(name, MARGIN, y, 8, COLOR_SECONDARY, true); return y - 11; }

        private static float DrawRow(SimplePDFWriter pdf, string code, string desc, float val, float y, float[] c)
        {
            float x = MARGIN;
            pdf.AddText(code, x, y, 9, COLOR_PRIMARY); x += c[0];
            pdf.AddText(desc, x, y, 9, COLOR_SECONDARY); x += c[1];
            pdf.AddText($"{val:F1}", x, y, 9, COLOR_PRIMARY);
            return y - 11;
        }

        private static float DrawCompRow(SimplePDFWriter pdf, string code, float va, float vb, float y, float[] c)
        {
            float x = MARGIN;
            float diff = vb - va;
            float pct = va > 0 ? (diff / va) * 100f : 0f;
            Color32 col = diff > 0.1f ? COLOR_POSITIVE : (diff < -0.1f ? COLOR_NEGATIVE : COLOR_SECONDARY);
            string sign = diff > 0 ? "+" : "";

            pdf.AddText(code, x, y, 8, COLOR_PRIMARY); x += c[0];
            pdf.AddText($"{va:F1}", x, y, 8, COLOR_SECONDARY); x += c[1];
            pdf.AddText($"{vb:F1}", x, y, 8, COLOR_SECONDARY); x += c[2];
            pdf.AddText($"{sign}{diff:F1}", x, y, 8, col); x += c[3];
            pdf.AddText($"{sign}{pct:F1}%", x, y, 8, col); x += c[4];
           // pdf.AddText(diff > 0.1f ? "▲" : (diff < -0.1f ? "▼" : "―"), x, y, 8, col);
            return y - 10;
        }

        private static void DrawFooterWithConfidential(SimplePDFWriter pdf, string date)
        {
            float top = MARGIN + 45;
            pdf.AddLine(MARGIN, top + 5, PAGE_WIDTH - MARGIN, top + 5, COLOR_BORDER, 0.5f);

            Texture2D logo = GetLogo();
            if (logo != null)
            {
                float h = 25f, w = h * ((float)logo.width / logo.height);
                if (w > 80) { w = 80; h = w / ((float)logo.width / logo.height); }
                pdf.AddImage(logo, MARGIN, top - h - 5, w, h);
            }

            pdf.AddText(CONFIDENTIAL_TEXT, PAGE_WIDTH / 2 - 90, top - 8, 9, COLOR_CONFIDENTIAL, true);
            pdf.AddText("Vehicle Dimension Analysis System", MARGIN + 90, top - 22, 7, COLOR_SECONDARY);
            pdf.AddText($"Generated: {date}", PAGE_WIDTH - MARGIN - 95, top - 22, 7, COLOR_SECONDARY);
            pdf.AddText("Page 1 of 1", PAGE_WIDTH / 2 - 20, top - 32, 7, COLOR_SECONDARY);
        }

        private static string SanitizeFileName(string n)
        {
            if (string.IsNullOrEmpty(n)) return "Vehicle";
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Replace(" ", "_");
        }
        // VehiclePDFExporter.cs (inside VehiclePDFExporter class)

        private static float DrawSideBySideScreenshots(
            SimplePDFWriter pdf,
            string nameA,
            string nameB,
            Texture2D imgA,
            Texture2D imgB,
            float y,
            float rowHeight)
        {
            float gap = 12f;
            float boxW = (CONTENT_WIDTH - gap) / 2f;

            float leftX = MARGIN;
            float rightX = MARGIN + boxW + gap;

            // captions
            pdf.AddText(Truncate(nameA, 18), leftX, y, 9, COLOR_SECONDARY, true);
            pdf.AddText(Truncate(nameB, 18), rightX, y, 9, COLOR_SECONDARY, true);

            y -= 12;

            float bottomY = y - rowHeight;

            DrawImageInBox(pdf, imgA, leftX, bottomY, boxW, rowHeight);
            DrawImageInBox(pdf, imgB, rightX, bottomY, boxW, rowHeight);

            return bottomY - 10;
        }

        private static void DrawImageInBox(SimplePDFWriter pdf, Texture2D img, float x, float y, float boxW, float boxH)
        {
            // Border box
            pdf.AddRect(x, y, boxW, boxH, COLOR_BORDER, 0.5f);

            if (img == null)
            {
                pdf.AddText("No Preview", x + 8, y + boxH / 2 - 4, 8, COLOR_SECONDARY);
                return;
            }

            float ar = (float)img.width / img.height;

            float w = boxW;
            float h = w / ar;

            if (h > boxH)
            {
                h = boxH;
                w = h * ar;
            }

            // center inside the box
            float ix = x + (boxW - w) / 2f;
            float iy = y + (boxH - h) / 2f;

            pdf.AddImage(img, ix, iy, w, h);
        }
        private static string Truncate(string s, int m) => string.IsNullOrEmpty(s) ? "N/A" : (s.Length <= m ? s : s.Substring(0, m - 2) + "..");

        #endregion
    }

    #region PDF Writer

    internal class SimplePDFWriter
    {
        private StringBuilder _page = new StringBuilder();
        private List<byte[]> _images = new List<byte[]>();

        public void AddText(string text, float x, float y, float size, Color32 c, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            _page.AppendLine($"BT {c.r / 255f:F3} {c.g / 255f:F3} {c.b / 255f:F3} rg {(bold ? "/F2" : "/F1")} {size} Tf {x:F2} {y:F2} Td ({text}) Tj ET");
        }

        public void AddLine(float x1, float y1, float x2, float y2, Color32 c, float w)
        {
            _page.AppendLine($"{c.r / 255f:F3} {c.g / 255f:F3} {c.b / 255f:F3} RG {w:F2} w {x1:F2} {y1:F2} m {x2:F2} {y2:F2} l S");
        }

        public void AddRect(float x, float y, float w, float h, Color32 c, float lw)
        {
            _page.AppendLine($"{c.r / 255f:F3} {c.g / 255f:F3} {c.b / 255f:F3} RG {lw:F2} w {x:F2} {y:F2} {w:F2} {h:F2} re S");
        }

        public void AddImage(Texture2D tex, float x, float y, float w, float h)
        {
            int idx = _images.Count;
            _images.Add(tex.EncodeToJPG(85));
            _page.AppendLine($"q {w:F2} 0 0 {h:F2} {x:F2} {y:F2} cm /Img{idx} Do Q");
        }

        public void Save(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs, Encoding.ASCII))
            {
                var pos = new List<long>();
                w.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n%âãÏÓ\n"));

                pos.Add(fs.Position);
                w.Write(Encoding.ASCII.GetBytes("1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n"));

                pos.Add(fs.Position);
                w.Write(Encoding.ASCII.GetBytes("2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n"));

                pos.Add(fs.Position);
                var sb = new StringBuilder("3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Resources<</Font<</F1 4 0 R/F2 5 0 R>>");
                if (_images.Count > 0) { sb.Append("/XObject<<"); for (int i = 0; i < _images.Count; i++) sb.Append($"/Img{i} {7 + i} 0 R "); sb.Append(">>"); }
                sb.Append(">>/Contents 6 0 R>>\nendobj\n");
                w.Write(Encoding.ASCII.GetBytes(sb.ToString()));

                pos.Add(fs.Position);
                w.Write(Encoding.ASCII.GetBytes("4 0 obj\n<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>\nendobj\n"));

                pos.Add(fs.Position);
                w.Write(Encoding.ASCII.GetBytes("5 0 obj\n<</Type/Font/Subtype/Type1/BaseFont/Helvetica-Bold>>\nendobj\n"));

                pos.Add(fs.Position);
                byte[] content = Encoding.ASCII.GetBytes(_page.ToString());
                w.Write(Encoding.ASCII.GetBytes($"6 0 obj\n<</Length {content.Length}>>\nstream\n"));
                w.Write(content);
                w.Write(Encoding.ASCII.GetBytes("\nendstream\nendobj\n"));

                for (int i = 0; i < _images.Count; i++)
                {
                    pos.Add(fs.Position);
                    byte[] img = _images[i];
                    int iw = 800, ih = 600;
                    for (int j = 0; j < img.Length - 10; j++)
                        if (img[j] == 0xFF && (img[j + 1] == 0xC0 || img[j + 1] == 0xC2))
                        { ih = (img[j + 5] << 8) | img[j + 6]; iw = (img[j + 7] << 8) | img[j + 8]; break; }
                    w.Write(Encoding.ASCII.GetBytes($"{7 + i} 0 obj\n<</Type/XObject/Subtype/Image/Width {iw}/Height {ih}/ColorSpace/DeviceRGB/BitsPerComponent 8/Filter/DCTDecode/Length {img.Length}>>\nstream\n"));
                    w.Write(img);
                    w.Write(Encoding.ASCII.GetBytes("\nendstream\nendobj\n"));
                }

                long xref = fs.Position;
                int total = 7 + _images.Count;
                w.Write(Encoding.ASCII.GetBytes($"xref\n0 {total}\n0000000000 65535 f \n"));
                foreach (long p in pos) w.Write(Encoding.ASCII.GetBytes($"{p:D10} 00000 n \n"));
                w.Write(Encoding.ASCII.GetBytes($"trailer\n<</Size {total}/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF"));
            }
        }
    }

    #endregion

    #region Helpers

    internal class PDFExporterHelper : MonoBehaviour
    {
        private static PDFExporterHelper _instance;
        public static PDFExporterHelper Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GameObject("PDFExporterHelper").AddComponent<PDFExporterHelper>();
                    DontDestroyOnLoad(_instance.gameObject);
                }
                return _instance;
            }
        }
    }

    public class PDFExporterSettings : MonoBehaviour
    {
        public Texture2D companyLogo;
        void Awake() { if (companyLogo != null) VehiclePDFExporter.SetLogo(companyLogo); }
    }

    #endregion
}
