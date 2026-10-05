using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace FreeBodySolver.Sections
{
    public class SectionGenerator
    {
        public List<PointF> Nodes { get; private set; }

        // Hesaplanan Kesit Özellikleri
        public float Area { get; private set; }
        public float Cx { get; private set; }
        public float Cy { get; private set; }
        public float Ixx { get; private set; }
        public float Iyy { get; private set; }

        public SectionGenerator()
        {
            Nodes = new List<PointF>();
        }

        // Lipped C (Dudaklı C) Kesiti Oluşturma
        public void GenerateLippedC(float A, float B, float C, float t, float r_inner,
                                    int nLip, int nRadius, int nWeb, int nFlange)
        {
            List<PointF> rawNodes = new List<PointF>();

            float r_outer = r_inner + t;
            float r_mid = r_inner + (t / 2f);
            float H = A;
            float D = C; // Lip Uzunluğu

            // 1. Üst Dudak (Top Lip)
            PointF p1 = new PointF(B - t / 2f, H - D);
            PointF p2 = new PointF(B - t / 2f, H - r_outer);
            AddLine(rawNodes, p1, p2, nLip);

            // 2. Üst Sağ Radyus (Top Right Radius)
            PointF cTR = new PointF(B - r_outer, H - r_outer);
            AddArc(rawNodes, cTR, r_mid, 0, 90, nRadius);

            // 3. Üst Başlık (Top Flange)
            PointF p3 = new PointF(B - r_outer, H - t / 2f);
            PointF p4 = new PointF(r_outer, H - t / 2f);
            AddLine(rawNodes, p3, p4, nFlange);

            // 4. Üst Sol Radyus (Top Left Radius)
            PointF cTL = new PointF(r_outer, H - r_outer);
            AddArc(rawNodes, cTL, r_mid, 90, 180, nRadius);

            // 5. Gövde (Web)
            PointF p5 = new PointF(t / 2f, H - r_outer);
            PointF p6 = new PointF(t / 2f, r_outer);
            AddLine(rawNodes, p5, p6, nWeb);

            // 6. Alt Sol Radyus (Bottom Left Radius)
            PointF cBL = new PointF(r_outer, r_outer);
            AddArc(rawNodes, cBL, r_mid, 180, 270, nRadius);

            // 7. Alt Başlık (Bottom Flange)
            PointF p7 = new PointF(r_outer, t / 2f);
            PointF p8 = new PointF(B - r_outer, t / 2f);
            AddLine(rawNodes, p7, p8, nFlange);

            // 8. Alt Sağ Radyus (Bottom Right Radius)
            PointF cBR = new PointF(B - r_outer, r_outer);
            AddArc(rawNodes, cBR, r_mid, 270, 360, nRadius);

            // 9. Alt Dudak (Bottom Lip)
            PointF p9 = new PointF(B - t / 2f, r_outer);
            PointF p10 = new PointF(B - t / 2f, D);
            AddLine(rawNodes, p9, p10, nLip);

            FinalizeNodes(rawNodes, t);
        }

        // U Kesiti Oluşturma (Dudaksız)
        public void GenerateUShape(float A, float B, float t, float r_inner,
                                   int nRadius, int nWeb, int nFlange)
        {
            List<PointF> rawNodes = new List<PointF>();

            float r_outer = r_inner + t;
            float r_mid = r_inner + (t / 2f);
            float H = A;

            // 1. Üst Başlık (Top Flange) - Dış Uçtan Sola Doğru
            PointF p1 = new PointF(B, H - t / 2f);
            PointF p2 = new PointF(r_outer, H - t / 2f);
            AddLine(rawNodes, p1, p2, nFlange);

            // 2. Üst Sol Radyus (Top Left Radius)
            PointF cTL = new PointF(r_outer, H - r_outer);
            AddArc(rawNodes, cTL, r_mid, 90, 180, nRadius);

            // 3. Gövde (Web)
            PointF p3 = new PointF(t / 2f, H - r_outer);
            PointF p4 = new PointF(t / 2f, r_outer);
            AddLine(rawNodes, p3, p4, nWeb);

            // 4. Alt Sol Radyus (Bottom Left Radius)
            PointF cBL = new PointF(r_outer, r_outer);
            AddArc(rawNodes, cBL, r_mid, 180, 270, nRadius);

            // 5. Alt Başlık (Bottom Flange) - Soldan Dış Uca Doğru
            PointF p5 = new PointF(r_outer, t / 2f);
            PointF p6 = new PointF(B, t / 2f);
            AddLine(rawNodes, p5, p6, nFlange);

            FinalizeNodes(rawNodes, t);
        }

        private void FinalizeNodes(List<PointF> rawNodes, float t)
        {
            Nodes.Clear();
            foreach (var p in rawNodes)
            {
                // Çakışan ardışık noktaları (Duplicate nodes) temizle
                if (Nodes.Count == 0 || Distance(Nodes.Last(), p) > 1e-3f)
                    Nodes.Add(p);
            }
            CalculateProperties(t);
        }

        private void AddLine(List<PointF> pts, PointF p1, PointF p2, int segments)
        {
            for (int i = 0; i <= segments; i++)
            {
                float factor = (float)i / segments;
                float x = p1.X + factor * (p2.X - p1.X);
                float y = p1.Y + factor * (p2.Y - p1.Y);
                pts.Add(new PointF(x, y));
            }
        }

        private void AddArc(List<PointF> pts, PointF center, float r, float startAngle, float endAngle, int segments)
        {
            for (int i = 0; i <= segments; i++)
            {
                float factor = (float)i / segments;
                float angleDeg = startAngle + factor * (endAngle - startAngle);
                float angleRad = angleDeg * (float)Math.PI / 180f;
                float x = center.X + r * (float)Math.Cos(angleRad);
                float y = center.Y + r * (float)Math.Sin(angleRad);
                pts.Add(new PointF(x, y));
            }
        }

        private float Distance(PointF p1, PointF p2)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private void CalculateProperties(float t)
        {
            Area = 0;
            float Sx = 0, Sy = 0;
            float Ixx_global = 0, Iyy_global = 0;

            for (int i = 0; i < Nodes.Count - 1; i++)
            {
                PointF p1 = Nodes[i];
                PointF p2 = Nodes[i + 1];
                float dx = p2.X - p1.X;
                float dy = p2.Y - p1.Y;
                float L = (float)Math.Sqrt(dx * dx + dy * dy);
                float a = L * t;

                float cx = (p1.X + p2.X) / 2f;
                float cy = (p1.Y + p2.Y) / 2f;

                Area += a;
                Sx += a * cy;
                Sy += a * cx;

                float ix = a * dy * dy / 12f;
                float iy = a * dx * dx / 12f;

                Ixx_global += ix + a * cy * cy;
                Iyy_global += iy + a * cx * cx;
            }

            Cy = Sx / Area;
            Cx = Sy / Area;

            Ixx = Ixx_global - Area * Cy * Cy;
            Iyy = Iyy_global - Area * Cx * Cx;
        }
    }
}