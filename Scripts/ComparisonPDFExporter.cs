using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// COMPARISON PDF EXPORTER
    /// 
    /// Exports comparison data to PDF with:
    /// - Side-by-side vehicle screenshots (from both preview cameras)
    /// - Measurement comparison table
    /// - Company logo and watermark
    /// 
    /// PDF Layout:
    /// ┌─────────────────────────────────────────────────────────────┐
    /// │  [LOGO]              VEHICLE COMPARISON REPORT              │
    /// │                      Date: 2024-01-15                       │
    /// ├─────────────────────────────────────────────────────────────┤
    /// │  ┌─────────────────┐    ┌─────────────────┐                │
    /// │  │   VEHICLE A     │    │   VEHICLE B     │                │
    /// │  │   [Screenshot]  │    │   [Screenshot]  │                │
    /// │  │   XUV700        │    │   Kushaq        │                │
    /// │  └─────────────────┘    └─────────────────┘                │
    /// ├─────────────────────────────────────────────────────────────┤
    /// │  MEASUREMENT COMPARISON                                     │
    /// │  ┌──────────┬──────────┬──────────┬──────────┬────────┐    │
    /// │  │ Parameter│ Vehicle A│ Vehicle B│   Diff   │   %    │    │
    /// │  ├──────────┼──────────┼──────────┼──────────┼────────┤    │
    /// │  │ L103     │ 4500 mm  │ 4200 mm  │ +300 mm  │ +7.1%  │    │
    /// │  │ ...      │          │          │          │        │    │
    /// │  └──────────┴──────────┴──────────┴──────────┴────────┘    │
    /// ├─────────────────────────────────────────────────────────────┤
    /// │                      CONFIDENTIAL                           │
    /// └─────────────────────────────────────────────────────────────┘
    /// </summary>
    public class ComparisonPDFExporter : MonoBehaviour
    {
        [Header("═══ PREVIEW RENDERERS ═══")]
        public VehiclePreviewRenderer vehicleAPreview;
        public VehiclePreviewRenderer vehicleBPreview;
        
        [Header("═══ SCREENSHOT SETTINGS ═══")]
        public int screenshotWidth = 800;
        public int screenshotHeight = 600;
        public Color backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        
        [Header("═══ PDF SETTINGS ═══")]
        public string companyName = "Your Company";
        public Texture2D companyLogo;
        public bool addWatermark = true;
        public string watermarkText = "CONFIDENTIAL";
        
        [Header("═══ COLORS ═══")]
        public Color headerColor = new Color(0.2f, 0.4f, 0.8f);
        public Color positiveColor = new Color(0.2f, 0.7f, 0.3f);
        public Color negativeColor = new Color(0.9f, 0.4f, 0.2f);
        public Color neutralColor = new Color(0.5f, 0.5f, 0.5f);
        
        // Measurement definitions
        private readonly MeasurementDef[] _measurements = new MeasurementDef[]
        {
            new MeasurementDef("L103", "Overall Length", "mm"),
            new MeasurementDef("L101", "Wheelbase", "mm"),
            new MeasurementDef("L104", "Front Overhang", "mm"),
            new MeasurementDef("L105", "Rear Overhang", "mm"),
            new MeasurementDef("W103", "Overall Width", "mm"),
            new MeasurementDef("W144", "Front Track", "mm"),
            new MeasurementDef("W145", "Rear Track", "mm"),
            new MeasurementDef("H100", "Overall Height", "mm"),
            new MeasurementDef("H101", "Ground Clearance", "mm"),
            new MeasurementDef("TD_F", "Front Wheel Ø", "mm"),
            new MeasurementDef("TD_R", "Rear Wheel Ø", "mm"),
        };
        
        #region Public API
        
        /// <summary>
        /// Export comparison to PDF
        /// </summary>
        public void ExportPDF(SavedVehicleMeasurement vehicleA, SavedVehicleMeasurement vehicleB, Action<bool, string> callback = null)
        {
            StartCoroutine(ExportPDFCoroutine(vehicleA, vehicleB, callback));
        }
        
        #endregion
        
        #region Export Coroutine
        
        private IEnumerator ExportPDFCoroutine(SavedVehicleMeasurement vehicleA, SavedVehicleMeasurement vehicleB, Action<bool, string> callback)
        {
            if (vehicleA == null || vehicleB == null)
            {
                callback?.Invoke(false, "Both vehicles required for comparison");
                yield break;
            }
            
            Debug.Log("[ComparisonPDF] Starting export...");
            
            // 1. Capture screenshots from both previews
            Texture2D screenshotA = null;
            Texture2D screenshotB = null;
            
            if (vehicleAPreview != null)
            {
                screenshotA = CaptureFromPreview(vehicleAPreview);
                Debug.Log($"[ComparisonPDF] Captured Vehicle A screenshot: {screenshotA != null}");
            }
            
            yield return null;
            
            if (vehicleBPreview != null)
            {
                screenshotB = CaptureFromPreview(vehicleBPreview);
                Debug.Log($"[ComparisonPDF] Captured Vehicle B screenshot: {screenshotB != null}");
            }
            
            yield return null;
            
            // 2. Generate PDF
            try
            {
                string filePath = GeneratePDF(vehicleA, vehicleB, screenshotA, screenshotB);
                
                // Cleanup textures
                if (screenshotA != null) Destroy(screenshotA);
                if (screenshotB != null) Destroy(screenshotB);
                
                Debug.Log($"[ComparisonPDF] Exported to: {filePath}");
                callback?.Invoke(true, filePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ComparisonPDF] Export failed: {e.Message}");
                callback?.Invoke(false, e.Message);
            }
        }
        
        #endregion
        
        #region Screenshot Capture
        
        private Texture2D CaptureFromPreview(VehiclePreviewRenderer preview)
        {
            if (preview == null || preview.previewCamera == null)
                return CreatePlaceholderTexture();
            
            Camera cam = preview.previewCamera;
            
            // Create render texture
            RenderTexture rt = new RenderTexture(screenshotWidth, screenshotHeight, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            
            // Store original
            RenderTexture originalTarget = cam.targetTexture;
            CameraClearFlags originalClear = cam.clearFlags;
            Color originalBg = cam.backgroundColor;
            
            // Setup for capture
            cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            
            // Render
            cam.Render();
            
            // Read pixels
            RenderTexture.active = rt;
            Texture2D screenshot = new Texture2D(screenshotWidth, screenshotHeight, TextureFormat.RGB24, false);
            screenshot.ReadPixels(new Rect(0, 0, screenshotWidth, screenshotHeight), 0, 0);
            screenshot.Apply();
            
            // Restore
            RenderTexture.active = null;
            cam.targetTexture = originalTarget;
            cam.clearFlags = originalClear;
            cam.backgroundColor = originalBg;
            
            // Cleanup
            rt.Release();
            Destroy(rt);
            
            return screenshot;
        }
        
        private Texture2D CreatePlaceholderTexture()
        {
            Texture2D tex = new Texture2D(screenshotWidth, screenshotHeight, TextureFormat.RGB24, false);
            Color[] pixels = new Color[screenshotWidth * screenshotHeight];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = backgroundColor;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
        
        #endregion
        
        #region PDF Generation
        
        private string GeneratePDF(SavedVehicleMeasurement vehicleA, SavedVehicleMeasurement vehicleB, 
                                    Texture2D screenshotA, Texture2D screenshotB)
        {
            // Create filename
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = $"Comparison_{vehicleA.vehicleName}_vs_{vehicleB.vehicleName}_{timestamp}.pdf";
            string filePath = Path.Combine(GetExportFolder(), SanitizeFilename(filename));
            
            // PDF dimensions (A4 landscape in points: 841.89 x 595.28)
            float pageWidth = 841.89f;
            float pageHeight = 595.28f;
            float margin = 40f;
            
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                List<long> objectOffsets = new List<long>();
                int objectCount = 0;
                
                // PDF Header
                writer.Write(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n"));
                writer.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }); // Binary marker
                
                // Object 1: Catalog
                objectOffsets.Add(fs.Position);
                objectCount++;
                WriteString(writer, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                
                // Object 2: Pages
                objectOffsets.Add(fs.Position);
                objectCount++;
                WriteString(writer, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                
                // Object 3: Page
                objectOffsets.Add(fs.Position);
                objectCount++;
                WriteString(writer, $"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth:F2} {pageHeight:F2}] " +
                    $"/Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R >> /XObject << /ImgA 7 0 R /ImgB 8 0 R >> >> >>\nendobj\n");
                
                // Object 4: Page Content
                string content = GeneratePageContent(vehicleA, vehicleB, pageWidth, pageHeight, margin);
                byte[] contentBytes = System.Text.Encoding.ASCII.GetBytes(content);
                
                objectOffsets.Add(fs.Position);
                objectCount++;
                WriteString(writer, $"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
                writer.Write(contentBytes);
                WriteString(writer, "\nendstream\nendobj\n");
                
                // Object 5: Font (Helvetica)
                objectOffsets.Add(fs.Position);
                objectCount++;
                WriteString(writer, "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");
                
                // Object 6: Font Bold (Helvetica-Bold)
                objectOffsets.Add(fs.Position);
                objectCount++;
                WriteString(writer, "6 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>\nendobj\n");
                
                // Object 7: Image A
                objectOffsets.Add(fs.Position);
                objectCount++;
                WriteImageObject(writer, screenshotA, 7);
                
                // Object 8: Image B
                objectOffsets.Add(fs.Position);
                objectCount++;
                WriteImageObject(writer, screenshotB, 8);
                
                // XRef
                long xrefOffset = fs.Position;
                WriteString(writer, $"xref\n0 {objectCount + 1}\n");
                WriteString(writer, "0000000000 65535 f \n");
                foreach (long offset in objectOffsets)
                {
                    WriteString(writer, $"{offset:D10} 00000 n \n");
                }
                
                // Trailer
                WriteString(writer, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\n");
                WriteString(writer, $"startxref\n{xrefOffset}\n%%EOF\n");
            }
            
            return filePath;
        }
        
        private string GeneratePageContent(SavedVehicleMeasurement vehicleA, SavedVehicleMeasurement vehicleB,
                                           float pageWidth, float pageHeight, float margin)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            
            float contentWidth = pageWidth - (margin * 2);
            float y = pageHeight - margin;
            
            // Title
            sb.AppendLine("BT");
            sb.AppendLine("/F2 18 Tf");
            sb.AppendLine($"{margin} {y - 20} Td");
            sb.AppendLine("(VEHICLE COMPARISON REPORT) Tj");
            sb.AppendLine("ET");
            
            // Date
            sb.AppendLine("BT");
            sb.AppendLine("/F1 10 Tf");
            sb.AppendLine($"{pageWidth - margin - 150} {y - 20} Td");
            sb.AppendLine($"(Date: {DateTime.Now:yyyy-MM-dd HH:mm}) Tj");
            sb.AppendLine("ET");
            
            y -= 50;
            
            // Vehicle names above images
            float imageWidth = (contentWidth - 40) / 2;
            float imageHeight = imageWidth * 0.75f;
            float imageY = y - imageHeight;
            
            // Vehicle A label
            sb.AppendLine("BT");
            sb.AppendLine("/F2 14 Tf");
            sb.AppendLine($"{margin} {y - 5} Td");
            sb.AppendLine($"({EscapePdfString(vehicleA.vehicleName)}) Tj");
            sb.AppendLine("ET");
            
            // Vehicle B label
            sb.AppendLine("BT");
            sb.AppendLine("/F2 14 Tf");
            sb.AppendLine($"{margin + imageWidth + 40} {y - 5} Td");
            sb.AppendLine($"({EscapePdfString(vehicleB.vehicleName)}) Tj");
            sb.AppendLine("ET");
            
            y -= 20;
            
            // Image A
            sb.AppendLine("q");
            sb.AppendLine($"{imageWidth} 0 0 {imageHeight} {margin} {y - imageHeight} cm");
            sb.AppendLine("/ImgA Do");
            sb.AppendLine("Q");
            
            // Image B
            sb.AppendLine("q");
            sb.AppendLine($"{imageWidth} 0 0 {imageHeight} {margin + imageWidth + 40} {y - imageHeight} cm");
            sb.AppendLine("/ImgB Do");
            sb.AppendLine("Q");
            
            y -= imageHeight + 30;
            
            // Comparison Table Header
            sb.AppendLine("BT");
            sb.AppendLine("/F2 12 Tf");
            sb.AppendLine($"{margin} {y} Td");
            sb.AppendLine("(MEASUREMENT COMPARISON) Tj");
            sb.AppendLine("ET");
            
            y -= 25;
            
            // Table
            float[] colWidths = { 120, 100, 100, 100, 80, 80 };
            float tableX = margin;
            
            // Table header row
            string[] headers = { "Parameter", vehicleA.vehicleName, vehicleB.vehicleName, "Difference", "% Diff", "Status" };
            DrawTableRow(sb, tableX, y, colWidths, headers, true);
            y -= 18;
            
            // Separator line
            sb.AppendLine($"q 0.8 0.8 0.8 RG 1 w {margin} {y + 15} m {pageWidth - margin} {y + 15} l S Q");
            
            // Data rows
            foreach (var m in _measurements)
            {
                float valueA = vehicleA.GetValue(m.Code);
                float valueB = vehicleB.GetValue(m.Code);
                float diff = valueA - valueB;
                float percentDiff = valueB > 0 ? (diff / valueB) * 100f : 0f;
                
                string diffStr = diff == 0 ? "—" : $"{(diff > 0 ? "+" : "")}{diff:F1} {m.Unit}";
                string percentStr = diff == 0 ? "—" : $"{(percentDiff > 0 ? "+" : "")}{percentDiff:F1}%";
                string status = diff == 0 ? "=" : (diff > 0 ? "A > B" : "B > A");
                
                string[] rowData = {
                    $"{m.Code} - {m.Name}",
                    $"{valueA:F1} {m.Unit}",
                    $"{valueB:F1} {m.Unit}",
                    diffStr,
                    percentStr,
                    status
                };
                
                DrawTableRow(sb, tableX, y, colWidths, rowData, false);
                y -= 16;
                
                if (y < margin + 50) break; // Prevent overflow
            }
            
            // Watermark
            if (addWatermark)
            {
                sb.AppendLine("q");
                sb.AppendLine("0.9 0.9 0.9 rg");
                sb.AppendLine("BT");
                sb.AppendLine("/F2 60 Tf");
                sb.AppendLine($"0.95 0 0 0.95 {pageWidth / 2 - 150} {pageHeight / 2} Tm");
                sb.AppendLine($"({watermarkText}) Tj");
                sb.AppendLine("ET");
                sb.AppendLine("Q");
            }
            
            // Footer
            sb.AppendLine("BT");
            sb.AppendLine("/F1 8 Tf");
            sb.AppendLine($"{margin} 20 Td");
            sb.AppendLine($"(Generated by {companyName} - Vehicle Measurement System) Tj");
            sb.AppendLine("ET");
            
            return sb.ToString();
        }
        
        private void DrawTableRow(System.Text.StringBuilder sb, float x, float y, float[] colWidths, string[] values, bool isHeader)
        {
            string font = isHeader ? "/F2" : "/F1";
            int fontSize = isHeader ? 10 : 9;
            
            float currentX = x;
            for (int i = 0; i < values.Length && i < colWidths.Length; i++)
            {
                sb.AppendLine("BT");
                sb.AppendLine($"{font} {fontSize} Tf");
                sb.AppendLine($"{currentX} {y} Td");
                sb.AppendLine($"({EscapePdfString(TruncateString(values[i], (int)(colWidths[i] / 6)))}) Tj");
                sb.AppendLine("ET");
                currentX += colWidths[i];
            }
        }
        
        private void WriteImageObject(BinaryWriter writer, Texture2D texture, int objectNum)
        {
            if (texture == null)
            {
                // Empty image placeholder
                WriteString(writer, $"{objectNum} 0 obj\n<< /Type /XObject /Subtype /Image /Width 1 /Height 1 " +
                    $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 3 >>\nstream\n");
                writer.Write(new byte[] { 128, 128, 128 });
                WriteString(writer, "\nendstream\nendobj\n");
                return;
            }
            
            // Get raw RGB data
            Color32[] pixels = texture.GetPixels32();
            byte[] rgbData = new byte[pixels.Length * 3];
            
            // Flip vertically (PDF coordinate system)
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    int srcIdx = (texture.height - 1 - y) * texture.width + x;
                    int dstIdx = (y * texture.width + x) * 3;
                    rgbData[dstIdx] = pixels[srcIdx].r;
                    rgbData[dstIdx + 1] = pixels[srcIdx].g;
                    rgbData[dstIdx + 2] = pixels[srcIdx].b;
                }
            }
            
            WriteString(writer, $"{objectNum} 0 obj\n<< /Type /XObject /Subtype /Image /Width {texture.width} /Height {texture.height} " +
                $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {rgbData.Length} >>\nstream\n");
            writer.Write(rgbData);
            WriteString(writer, "\nendstream\nendobj\n");
        }
        
        #endregion
        
        #region Helpers
        
        private void WriteString(BinaryWriter writer, string text)
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes(text));
        }
        
        private string EscapePdfString(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }
        
        private string TruncateString(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;
            return text.Substring(0, maxLength - 2) + "..";
        }
        
        private string SanitizeFilename(string filename)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
                filename = filename.Replace(c, '_');
            return filename;
        }
        
        private string GetExportFolder()
        {
            string folder = Path.Combine(Application.persistentDataPath, "Exports");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            return folder;
        }
        
        #endregion
    }
}
