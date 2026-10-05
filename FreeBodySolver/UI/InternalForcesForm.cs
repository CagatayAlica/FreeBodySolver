using FreeBodySolver.Core;
using FreeBodySolver.Models;
using FreeBodySolver.Sections;
using FreeBodySolver.Solver;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FreeBodySolver.UI
{
    // : Form ibaresi bu sınıfın bir pencere (Windows Form) olduğunu belirtir.
    public class InternalForcesForm : Form
    {
        private ComboBox cmbGroup, cmbLine, cmbDiagramType;
        private DataGridView dgv;
        private PictureBox diagramCanvas;

        private List<Line> lines;
        private Dictionary<int, PointF> nodes;
        private Dictionary<int, int> nodeMap;
        private Dictionary<string, (double[] U, double[] R)> results;
        private Dictionary<int, List<LoadEntry>> lineLoads;

        // Visual Studio'nun bulamadığı 6 parametreli Constructor (Yapıcı Metot) buradadır:
        public InternalForcesForm(List<Line> lines, Dictionary<int, PointF> nodes, Dictionary<int, int> nodeMap, Dictionary<string, (double[] U, double[] R)> results, Dictionary<int, List<LoadEntry>> lineLoads, string initialGroup)
        {
            this.Text = "Eleman İç Kuvvetleri ve Diyagramlar";

            this.nodes = nodes;
            float minX = 0, maxX = 0;
            if (nodes.Count > 0)
            {
                minX = nodes.Values.Min(v => v.X);
                maxX = nodes.Values.Max(v => v.X);
            }
            float totalLength = maxX - minX;

            int desiredWidth = 900;
            if (totalLength > 0)
            {
                desiredWidth = (int)(totalLength * 150) + 200;
                int maxScreenWidth = Screen.PrimaryScreen.WorkingArea.Width - 100;
                desiredWidth = Math.Max(750, Math.Min(desiredWidth, maxScreenWidth));
            }
            this.Size = new Size(desiredWidth, 750);
            this.StartPosition = FormStartPosition.CenterParent;

            this.lines = lines;
            this.nodeMap = nodeMap;
            this.results = results;
            this.lineLoads = lineLoads;

            Panel pnlTop = new Panel { Dock = DockStyle.Top, Height = 45 };

            cmbGroup = new ComboBox { Location = new Point(10, 12), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var g in results.Keys) cmbGroup.Items.Add(g);
            cmbGroup.SelectedItem = initialGroup;

            cmbLine = new ComboBox { Location = new Point(140, 12), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var ln in lines) cmbLine.Items.Add($"Çizgi {ln.Id} (Node {ln.A} -> {ln.B})");
            if (cmbLine.Items.Count > 0) cmbLine.SelectedIndex = 0;

            cmbDiagramType = new ComboBox { Location = new Point(330, 12), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbDiagramType.Items.AddRange(new object[] { "M (Eğilme Momenti)", "V (Kesme Kuvveti)", "N (Eksenel Kuvvet)", "d (Çökme)" });
            cmbDiagramType.SelectedIndex = 0;

            cmbGroup.SelectedIndexChanged += (s, e) => CalculateAndDisplay();
            cmbLine.SelectedIndexChanged += (s, e) => CalculateAndDisplay();
            cmbDiagramType.SelectedIndexChanged += (s, e) => diagramCanvas.Invalidate();

            pnlTop.Controls.Add(cmbGroup);
            pnlTop.Controls.Add(cmbLine);
            pnlTop.Controls.Add(cmbDiagramType);

            SplitContainer split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 250
            };

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false
            };

            dgv.Columns.Add("x", "Mesafe x (m)");
            dgv.Columns.Add("M", "Eğilme M (kNm)");
            dgv.Columns.Add("V", "Kesme V (kN)");
            dgv.Columns.Add("N", "Eksenel N (kN)");
            dgv.Columns.Add("d", "Çökme d (mm)");

            diagramCanvas = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White };
            diagramCanvas.Paint += DiagramCanvas_Paint;

            diagramCanvas.Resize += (s, e) => diagramCanvas.Invalidate();
            this.Resize += (s, e) => diagramCanvas.Invalidate();

            split.Panel1.Controls.Add(dgv);
            split.Panel2.Controls.Add(diagramCanvas);

            this.Controls.Add(split);
            this.Controls.Add(pnlTop);

            CalculateAndDisplay();
        }

        private void GetLineData(Line ln, string group, int segments, out double[] X, out double[] M, out double[] V, out double[] N, out double[] D)
        {
            X = new double[segments + 1];
            M = new double[segments + 1];
            V = new double[segments + 1];
            N = new double[segments + 1];
            D = new double[segments + 1];

            double[] U = results[group].U;

            var p1 = nodes[ln.A];
            var p2 = nodes[ln.B];
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double L = Math.Sqrt(dx * dx + dy * dy);
            if (L < 1e-6) return;

            double c = dx / L;
            double s = dy / L;

            double[] u_global = new double[6] {
                U[nodeMap[ln.A] * 3], U[nodeMap[ln.A] * 3 + 1], U[nodeMap[ln.A] * 3 + 2],
                U[nodeMap[ln.B] * 3], U[nodeMap[ln.B] * 3 + 1], U[nodeMap[ln.B] * 3 + 2]
            };

            double[,] T = new double[6, 6];
            T[0, 0] = c; T[0, 1] = s; T[1, 0] = -s; T[1, 1] = c; T[2, 2] = 1;
            T[3, 3] = c; T[3, 4] = s; T[4, 3] = -s; T[4, 4] = c; T[5, 5] = 1;

            double[] u_local = FemSolver.Multiply(T, u_global);

            double W_X = 0, W_Y = 0, loc_x = 0, loc_y = 0;
            if (lineLoads.ContainsKey(ln.Id))
            {
                foreach (var ld in lineLoads[ln.Id].Where(l => l.Group == group))
                {
                    if (ld.Dir == LoadDirection.PlusX) W_X += ld.Value;
                    if (ld.Dir == LoadDirection.MinusX) W_X -= ld.Value;
                    if (ld.Dir == LoadDirection.PlusY) W_Y += ld.Value;
                    if (ld.Dir == LoadDirection.MinusY) W_Y -= ld.Value;
                    if (ld.Dir == LoadDirection.LocalPlusX) loc_x += ld.Value;
                    if (ld.Dir == LoadDirection.LocalMinusX) loc_x -= ld.Value;
                    if (ld.Dir == LoadDirection.LocalPlusY) loc_y += ld.Value;
                    if (ld.Dir == LoadDirection.LocalMinusY) loc_y -= ld.Value;
                }
            }

            double w_local_x = loc_x + W_X * c + W_Y * s;
            double w_local_y = loc_y + (-W_X * s + W_Y * c);

            double[] f_eq = new double[6] {
                w_local_x * L / 2.0, w_local_y * L / 2.0, w_local_y * L * L / 12.0,
                w_local_x * L / 2.0, w_local_y * L / 2.0, -w_local_y * L * L / 12.0
            };

            // --- YENİ: Seçilen Çizginin Kesit Özelliklerini Çekiyoruz ---
            double currentA = FemSolver.A;
            double currentI = FemSolver.I;

            if (!string.IsNullOrEmpty(ln.SectionName))
            {
                var profile = SectionDatabase.LoadProfiles().FirstOrDefault(p => p.Name == ln.SectionName);
                if (profile != null)
                {
                    SectionGenerator gen = new SectionGenerator();
                    if (profile.ShapeType == "Lipped C Shape")
                        gen.GenerateLippedC(profile.A, profile.B, profile.C, profile.t, profile.R, 2, 4, 4, 3);
                    else
                        gen.GenerateUShape(profile.A, profile.B, profile.t, profile.R, 4, 4, 3);

                    currentA = gen.Area * 1e-6;
                    currentI = gen.Ixx * 1e-12;
                }
            }
            // ------------------------------------------------------------

            double[,] kLocal = new double[6, 6];
            double ea_l = FemSolver.E * currentA / L;
            double ei_l3 = 12 * FemSolver.E * currentI / (L * L * L);
            double ei_l2 = 6 * FemSolver.E * currentI / (L * L);
            double ei_l = 4 * FemSolver.E * currentI / L;
            double ei_l_2 = 2 * FemSolver.E * currentI / L;

            kLocal[0, 0] = ea_l; kLocal[0, 3] = -ea_l;
            kLocal[3, 0] = -ea_l; kLocal[3, 3] = ea_l;
            kLocal[1, 1] = ei_l3; kLocal[1, 2] = ei_l2; kLocal[1, 4] = -ei_l3; kLocal[1, 5] = ei_l2;
            kLocal[2, 1] = ei_l2; kLocal[2, 2] = ei_l; kLocal[2, 4] = -ei_l2; kLocal[2, 5] = ei_l_2;
            kLocal[4, 1] = -ei_l3; kLocal[4, 2] = -ei_l2; kLocal[4, 4] = ei_l3; kLocal[4, 5] = -ei_l2;
            kLocal[5, 1] = ei_l2; kLocal[5, 2] = ei_l_2; kLocal[5, 4] = -ei_l2; kLocal[5, 5] = ei_l;

            double[] ku_local = FemSolver.Multiply(kLocal, u_local);
            double[] f_ends = new double[6];
            for (int i = 0; i < 6; i++) f_ends[i] = ku_local[i] - f_eq[i];

            for (int i = 0; i <= segments; i++)
            {
                double x = (L / segments) * i;
                X[i] = x;
                N[i] = -f_ends[0] - w_local_x * x;
                V[i] = f_ends[1] + w_local_y * x;
                M[i] = -f_ends[2] + f_ends[1] * x + w_local_y * (x * x) / 2.0;

                double xL = x / L;
                double H1 = 1 - 3 * (xL * xL) + 2 * (xL * xL * xL);
                double H2 = x * Math.Pow(1 - xL, 2);
                double H3 = 3 * (xL * xL) - 2 * (xL * xL * xL);
                double H4 = x * (Math.Pow(xL, 2) - xL);

                double v_shape = u_local[1] * H1 + u_local[2] * H2 + u_local[4] * H3 + u_local[5] * H4;
                // Çökme hesabında güncel atalet (currentI) kullanımı
                double v_load = (w_local_y * x * x * Math.Pow(L - x, 2)) / (24.0 * FemSolver.E * currentI);
                D[i] = (v_shape + v_load) * 1000.0;
            }
        }
        private void CalculateAndDisplay()
        {
            if (cmbGroup.SelectedItem == null || cmbLine.SelectedIndex < 0) return;

            string group = cmbGroup.SelectedItem.ToString();
            Line ln = lines[cmbLine.SelectedIndex];

            double[] X, M, V, N, D;
            GetLineData(ln, group, 10, out X, out M, out V, out N, out D);

            dgv.Rows.Clear();
            for (int i = 0; i <= 10; i++)
            {
                dgv.Rows.Add(X[i].ToString("0.###"), M[i].ToString("0.###"), V[i].ToString("0.###"), N[i].ToString("0.###"), D[i].ToString("0.###"));
            }

            diagramCanvas.Invalidate();
        }

        private void DiagramCanvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (nodes.Count == 0 || cmbGroup.SelectedItem == null) return;

            try
            {
                int w = diagramCanvas.Width;
                int h = diagramCanvas.Height;
                float margin = 60f;

                if (w <= margin * 2 || h <= margin * 2) return;

                string group = cmbGroup.SelectedItem.ToString();
                string diagType = cmbDiagramType.SelectedItem.ToString();

                float minX = nodes.Values.Min(v => v.X); float maxX = nodes.Values.Max(v => v.X);
                float minY = nodes.Values.Min(v => v.Y); float maxY = nodes.Values.Max(v => v.Y);

                if (Math.Abs(maxX - minX) < 1e-6f) { minX -= 1; maxX += 1; }
                if (Math.Abs(maxY - minY) < 1e-6f) { minY -= 0.05f; maxY += 0.05f; }

                float sx = (w - 2 * margin) / (maxX - minX);
                float sy = (h - 2 * margin) / (maxY - minY);

                float dZoom = (Math.Abs(maxY - minY) <= 0.11f) ? sx : Math.Max(0.01f, Math.Min(sx, sy));

                float cx = (minX + maxX) / 2f;
                float cy = (minY + maxY) / 2f;

                float panX = w / 2f - cx * dZoom;
                float panY = h / 2f + cy * dZoom;

                Func<PointF, PointF> toPix = (world) => new PointF(panX + world.X * dZoom, panY - world.Y * dZoom);

                double maxAbsVal = 1e-6;
                var lineData = new Dictionary<int, double[]>();

                double[] X, M, V, N, D;

                foreach (var ln in lines)
                {
                    if (!nodes.ContainsKey(ln.A) || !nodes.ContainsKey(ln.B)) continue;

                    GetLineData(ln, group, 10, out X, out M, out V, out N, out D);
                    double[] vals = null;

                    if (diagType.StartsWith("M")) vals = M;
                    else if (diagType.StartsWith("V")) vals = V;
                    else if (diagType.StartsWith("N")) vals = N;
                    else if (diagType.StartsWith("d")) vals = D;

                    if (vals != null)
                    {
                        lineData[ln.Id] = vals;
                        double localMax = vals.Max(v => Math.Abs(v));
                        if (localMax > maxAbsVal) maxAbsVal = localMax;
                    }
                }

                float diagHeightLimit = Math.Min(80f, h / 3f);

                foreach (var ln in lines)
                {
                    if (!nodes.ContainsKey(ln.A) || !nodes.ContainsKey(ln.B)) continue;

                    var pA = toPix(nodes[ln.A]);
                    var pB = toPix(nodes[ln.B]);

                    if (lineData.ContainsKey(ln.Id))
                    {
                        double[] vals = lineData[ln.Id];
                        float dx = pB.X - pA.X; float dy = pB.Y - pA.Y;
                        float L_pix = (float)Math.Sqrt(dx * dx + dy * dy);

                        if (L_pix > 0)
                        {
                            float nx = -dy / L_pix; float ny = dx / L_pix;

                            List<PointF> poly = new List<PointF>();
                            poly.Add(pA);

                            int maxIdx = 0;
                            int minIdx = 0;
                            double maxValInLine = double.MinValue;
                            double minValInLine = double.MaxValue;

                            for (int i = 0; i <= 10; i++)
                            {
                                float px = pA.X + (dx * i / 10f);
                                float py = pA.Y + (dy * i / 10f);

                                float offset = (float)(vals[i] / maxAbsVal * diagHeightLimit);
                                if (diagType.StartsWith("d")) offset = -offset;

                                poly.Add(new PointF(px + nx * offset, py + ny * offset));

                                if (vals[i] > maxValInLine) { maxValInLine = vals[i]; maxIdx = i; }
                                if (vals[i] < minValInLine) { minValInLine = vals[i]; minIdx = i; }
                            }
                            poly.Add(pB);

                            Color fillColor = Color.FromArgb(100, 100, 100, 200);
                            if (diagType.StartsWith("M")) fillColor = Color.FromArgb(100, 200, 100, 100);
                            else if (diagType.StartsWith("V")) fillColor = Color.FromArgb(100, 100, 200, 100);
                            else if (diagType.StartsWith("d")) fillColor = Color.FromArgb(100, 200, 200, 100);

                            using (var b = new SolidBrush(fillColor)) g.FillPolygon(b, poly.ToArray());
                            using (var p = new Pen(Color.Gray, 1f)) g.DrawPolygon(p, poly.ToArray());

                            Action<int> drawTextAt = (idx) => {
                                string txt = vals[idx].ToString("0.###");
                                var tSz = g.MeasureString(txt, this.Font);

                                float px = pA.X + (dx * idx / 10f);
                                float py = pA.Y + (dy * idx / 10f);
                                float offset = (float)(vals[idx] / maxAbsVal * diagHeightLimit);
                                if (diagType.StartsWith("d")) offset = -offset;

                                var pt = new PointF(px + nx * offset, py + ny * offset);
                                var basePt = new PointF(px, py);

                                using (var dashPen = new Pen(Color.Gray, 1.5f))
                                {
                                    dashPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                                    g.DrawLine(dashPen, basePt, pt);
                                }

                                float pushDir = offset >= 0 ? 1f : -1f;
                                float drawX = pt.X + nx * pushDir * 12f - tSz.Width / 2f;
                                float drawY = pt.Y + ny * pushDir * 12f - tSz.Height / 2f;

                                using (var textBgBrush = new SolidBrush(Color.FromArgb(180, Color.White)))
                                {
                                    g.FillRectangle(textBgBrush, drawX - 2, drawY - 1, tSz.Width + 4, tSz.Height + 2);
                                }

                                g.DrawString(txt, this.Font, Brushes.Black, drawX, drawY);
                            };

                            drawTextAt(0);
                            drawTextAt(10);

                            if (maxIdx != 0 && maxIdx != 10 && Math.Abs(maxValInLine) > 1e-4) drawTextAt(maxIdx);
                            if (minIdx != 0 && minIdx != 10 && minIdx != maxIdx && Math.Abs(minValInLine) > 1e-4) drawTextAt(minIdx);
                        }
                    }

                    bool isSelected = (cmbLine.SelectedIndex >= 0 && lines[cmbLine.SelectedIndex].Id == ln.Id);
                    Color lineCol = isSelected ? Color.Red : Color.Black;
                    float lineW = isSelected ? 3f : 2f;
                    using (var p = new Pen(lineCol, lineW)) g.DrawLine(p, pA, pB);
                }

                foreach (var node in nodes.Values)
                {
                    var p = toPix(node);
                    g.FillEllipse(Brushes.Black, p.X - 3, p.Y - 3, 6, 6);
                }
            }
            catch (Exception ex)
            {
                g.DrawString($"Çizim Hatası: {ex.Message}", this.Font, Brushes.Red, 10, 10);
            }
        }
    }
}
