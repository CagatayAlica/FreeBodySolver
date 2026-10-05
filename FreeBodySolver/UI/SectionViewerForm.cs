using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FreeBodySolver.Sections;
using FreeBodySolver.Models;
using FreeBodySolver.Core;

namespace FreeBodySolver.UI
{
    public class SectionViewerForm : Form
    {
        private PictureBox canvas;
        private SectionGenerator section;
        private DataGridView dgvProperties;
        private ListBox lstLibrary;

        // Üst panel elemanları
        private ComboBox cmbShapeType;
        private TextBox txtA, txtB, txtC, txtR, txtT, txtProfileName;
        private Button btnGenerate, btnSaveProfile;

        private float zoom = 1f;
        private PointF pan = new PointF(0, 0);
        private bool isUpdatingUI = false;

        public SectionViewerForm()
        {
            this.Text = "Kesit Kütüphanesi ve Görünümü";
            this.Size = new Size(1100, 750);
            this.StartPosition = FormStartPosition.CenterParent;

            section = new SectionGenerator();

            // Önce Üst Paneli Ekliyoruz
            InitializeTopPanel();

            // Ana bölücü: Sol (Çizim) - Sağ (Kütüphane ve Özellikler)
            SplitContainer splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 700
            };

            canvas = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White };
            canvas.Paint += Canvas_Paint;
            canvas.Resize += (s, e) => { ZoomExtent(); canvas.Invalidate(); };

            canvas.MouseWheel += (s, e) => {
                if (section.Nodes.Count == 0) return;
                var mousePt = e.Location;
                var worldBefore = new PointF((mousePt.X - pan.X) / zoom, (pan.Y - mousePt.Y) / zoom);
                float factor = (e.Delta > 0) ? 1.15f : (1f / 1.15f);
                zoom *= factor;
                pan = new PointF(mousePt.X - worldBefore.X * zoom, mousePt.Y + worldBefore.Y * zoom);
                canvas.Invalidate();
            };
            canvas.MouseEnter += (s, e) => canvas.Focus();

            // Sağ Taraf Bölücüsü: Üst (Kütüphane) - Alt (Hesaplanan Özellikler)
            SplitContainer splitRight = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 250
            };

            // Kütüphane Listesi
            lstLibrary = new ListBox { Dock = DockStyle.Fill, Font = new Font(this.Font.FontFamily, 10f) };
            lstLibrary.SelectedIndexChanged += LstLibrary_SelectedIndexChanged;

            // Silme işlemi için sağ tık menüsü
            ContextMenuStrip ctxLibrary = new ContextMenuStrip();
            var miDelete = new ToolStripMenuItem("Sil (Delete)");
            miDelete.Click += (s, e) => {
                if (lstLibrary.SelectedItem != null)
                {
                    SectionDatabase.DeleteProfile(lstLibrary.SelectedItem.ToString());
                    RefreshLibraryList();
                }
            };
            ctxLibrary.Items.Add(miDelete);
            lstLibrary.ContextMenuStrip = ctxLibrary;

            GroupBox grpLibrary = new GroupBox { Text = "Kayıtlı Kesitler", Dock = DockStyle.Fill };
            grpLibrary.Controls.Add(lstLibrary);

            // Özellikler Tablosu
            dgvProperties = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.WhiteSmoke
            };
            dgvProperties.Columns.Add("Prop", "Özellik / Düğüm");
            dgvProperties.Columns.Add("Val", "Değer");

            GroupBox grpProps = new GroupBox { Text = "Kesit Özellikleri", Dock = DockStyle.Fill };
            grpProps.Controls.Add(dgvProperties);

            splitRight.Panel1.Controls.Add(grpLibrary);
            splitRight.Panel2.Controls.Add(grpProps);

            splitMain.Panel1.Controls.Add(canvas);
            splitMain.Panel2.Controls.Add(splitRight);

            this.Controls.Add(splitMain);

            // ÇÖZÜM 1: Üst menü ile gövdenin birbirini yutmasını/örtmesini engelliyoruz (Z-Order düzeltmesi)
            splitMain.BringToFront();

            RefreshLibraryList();
            GenerateSection();
        }

        // ÇÖZÜM 2: Form tamamen ekrana çizildikten sonra Zoom işlemi yapıp tam ortaya oturtuyoruz
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ZoomExtent();
            canvas.Invalidate();
        }

        private void InitializeTopPanel()
        {
            // Yüksekliği iki satır rahat sığacak şekilde 100'e çıkardık
            Panel pnlTop = new Panel { Dock = DockStyle.Top, Height = 100 };
            FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(5) };

            Label lblShape = new Label { Text = "Tip:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
            cmbShapeType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            cmbShapeType.Items.AddRange(new object[] { "Lipped C Shape", "U Shape" });
            cmbShapeType.SelectedIndex = 0;

            cmbShapeType.SelectedIndexChanged += (s, e) => {
                txtC.Enabled = cmbShapeType.SelectedIndex == 0;
            };

            Label lblA = new Label { Text = "A:", AutoSize = true, Margin = new Padding(10, 8, 3, 3) };
            txtA = new TextBox { Width = 45, Text = "90" };

            Label lblB = new Label { Text = "B:", AutoSize = true, Margin = new Padding(10, 8, 3, 3) };
            txtB = new TextBox { Width = 45, Text = "50" };

            Label lblC = new Label { Text = "C:", AutoSize = true, Margin = new Padding(10, 8, 3, 3) };
            txtC = new TextBox { Width = 45, Text = "15" };

            Label lblR = new Label { Text = "R:", AutoSize = true, Margin = new Padding(10, 8, 3, 3) };
            txtR = new TextBox { Width = 45, Text = "2.5" };

            Label lblT = new Label { Text = "t:", AutoSize = true, Margin = new Padding(10, 8, 3, 3) };
            txtT = new TextBox { Width = 45, Text = "1.5" };

            btnGenerate = new Button { Text = "Çiz", Width = 60, BackColor = Color.LightGreen, Margin = new Padding(15, 3, 3, 3) };
            btnGenerate.Click += (s, e) => GenerateSection();

            // Kaydetme Grubu
            Label lblName = new Label { Text = "Profil Adı:", AutoSize = true, Margin = new Padding(30, 8, 3, 3), Font = new Font(this.Font, FontStyle.Bold) };
            txtProfileName = new TextBox { Width = 140, Text = "C90x50x15x1.5" };
            btnSaveProfile = new Button { Text = "Kaydet", Width = 80, BackColor = Color.LightSkyBlue, Margin = new Padding(3, 3, 3, 3) };
            btnSaveProfile.Click += BtnSaveProfile_Click;

            flow.Controls.AddRange(new Control[] {
                lblShape, cmbShapeType,
                lblA, txtA, lblB, txtB, lblC, txtC, lblR, txtR, lblT, txtT,
                btnGenerate,
                lblName, txtProfileName, btnSaveProfile
            });

            pnlTop.Controls.Add(flow);
            this.Controls.Add(pnlTop);
        }

        private void RefreshLibraryList()
        {
            isUpdatingUI = true;
            lstLibrary.Items.Clear();
            var profiles = SectionDatabase.LoadProfiles();
            foreach (var p in profiles)
            {
                lstLibrary.Items.Add(p.Name);
            }
            isUpdatingUI = false;
        }

        private void LstLibrary_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (isUpdatingUI || lstLibrary.SelectedItem == null) return;

            string selectedName = lstLibrary.SelectedItem.ToString();
            var profile = SectionDatabase.LoadProfiles().FirstOrDefault(x => x.Name == selectedName);

            if (profile != null)
            {
                cmbShapeType.SelectedItem = profile.ShapeType;
                txtA.Text = profile.A.ToString(CultureInfo.InvariantCulture);
                txtB.Text = profile.B.ToString(CultureInfo.InvariantCulture);
                txtC.Text = profile.C.ToString(CultureInfo.InvariantCulture);
                txtR.Text = profile.R.ToString(CultureInfo.InvariantCulture);
                txtT.Text = profile.t.ToString(CultureInfo.InvariantCulture);
                txtProfileName.Text = profile.Name;

                GenerateSection();
            }
        }

        private void BtnSaveProfile_Click(object sender, EventArgs e)
        {
            string pName = txtProfileName.Text.Trim();
            if (string.IsNullOrEmpty(pName))
            {
                MessageBox.Show("Lütfen bir profil adı girin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // --- YENİ EKLENEN KONTROL (Aynı İsimde Kayıt Var mı?) ---
            var existingProfiles = SectionDatabase.LoadProfiles();
            // Büyük/küçük harf duyarsız (case-insensitive) olarak isimleri karşılaştırıyoruz
            if (existingProfiles.Any(x => x.Name.Equals(pName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"'{pName}' isminde bir kesit kütüphanede zaten mevcut!\nLütfen üzerine yazmamak için farklı bir isim belirleyin.", "İsim Çakışması", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return; // Kaydetme işlemini burada iptal et
            }
            // ---------------------------------------------------------

            var profile = new SectionProfile
            {
                Name = pName,
                ShapeType = cmbShapeType.SelectedItem.ToString(),
                A = ParseVal(txtA, 90f),
                B = ParseVal(txtB, 50f),
                C = ParseVal(txtC, 15f),
                R = ParseVal(txtR, 2.5f),
                t = ParseVal(txtT, 1.5f)
            };

            SectionDatabase.SaveProfile(profile);
            RefreshLibraryList();

            MessageBox.Show($"'{pName}' kütüphaneye başarıyla kaydedildi.", "Kayıt Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private float ParseVal(TextBox txt, float defaultVal)
        {
            if (float.TryParse(txt.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out float result))
                return result;
            return defaultVal;
        }

        private void GenerateSection()
        {
            float A = ParseVal(txtA, 90f);
            float B = ParseVal(txtB, 50f);
            float C = ParseVal(txtC, 15f);
            float R = ParseVal(txtR, 2.5f);
            float t = ParseVal(txtT, 1.5f);

            section = new SectionGenerator();

            if (cmbShapeType.SelectedIndex == 0) // Lipped C
            {
                section.GenerateLippedC(A, B, C, t, R, 2, 4, 4, 3);
            }
            else // U Shape
            {
                section.GenerateUShape(A, B, t, R, 4, 4, 3);
            }

            LoadPropertiesToGrid();
            ZoomExtent();
            canvas.Invalidate();
        }

        private void LoadPropertiesToGrid()
        {
            dgvProperties.Rows.Clear();
            dgvProperties.Rows.Add("Alan (A)", $"{section.Area:0.00} mm²");
            dgvProperties.Rows.Add("Ağırlık Mer. X (Cx)", $"{section.Cx:0.00} mm");
            dgvProperties.Rows.Add("Ağırlık Mer. Y (Cy)", $"{section.Cy:0.00} mm");
            dgvProperties.Rows.Add("Atalet (Ixx)", $"{section.Ixx:0.00} mm⁴");
            dgvProperties.Rows.Add("Atalet (Iyy)", $"{section.Iyy:0.00} mm⁴");
            dgvProperties.Rows.Add("Toplam Node Sayısı", $"{section.Nodes.Count}");
            dgvProperties.Rows.Add("---", "---");

            for (int i = 0; i < section.Nodes.Count; i++)
            {
                dgvProperties.Rows.Add($"Node {i + 1}", $"X: {section.Nodes[i].X:0.00} | Y: {section.Nodes[i].Y:0.00}");
            }
        }

        private void ZoomExtent()
        {
            if (section.Nodes.Count == 0) return;

            float minX = section.Nodes.Min(n => n.X);
            float maxX = section.Nodes.Max(n => n.X);
            float minY = section.Nodes.Min(n => n.Y);
            float maxY = section.Nodes.Max(n => n.Y);

            if (Math.Abs(maxX - minX) < 1e-3f) { minX -= 10; maxX += 10; }
            if (Math.Abs(maxY - minY) < 1e-3f) { minY -= 10; maxY += 10; }

            int w = canvas.ClientSize.Width;
            int h = canvas.ClientSize.Height;

            // Eğer form henüz boyutlandırılmadıysa geçersiz değerler üretmesini engelle
            if (w <= 0 || h <= 0) return;

            float margin = 50f;

            float sx = (w - 2 * margin) / (maxX - minX);
            float sy = (h - 2 * margin) / (maxY - minY);

            zoom = Math.Max(0.1f, Math.Min(sx, sy));

            float cx = (minX + maxX) / 2f;
            float cy = (minY + maxY) / 2f;

            pan = new PointF(w / 2f - cx * zoom, h / 2f + cy * zoom);
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (section.Nodes.Count < 2) return;

            Func<PointF, PointF> toPix = (world) => new PointF(pan.X + world.X * zoom, pan.Y - world.Y * zoom);

            // Ağırlık Merkezini Çiz
            PointF cgPix = toPix(new PointF(section.Cx, section.Cy));
            g.DrawLine(Pens.LightGray, cgPix.X - 20, cgPix.Y, cgPix.X + 20, cgPix.Y);
            g.DrawLine(Pens.LightGray, cgPix.X, cgPix.Y - 20, cgPix.X, cgPix.Y + 20);
            g.DrawString("CG", this.Font, Brushes.Gray, cgPix.X + 2, cgPix.Y + 2);

            // Kesiti Çiz
            using (var pen = new Pen(Color.DarkBlue, 2f))
            {
                for (int i = 0; i < section.Nodes.Count - 1; i++)
                {
                    PointF p1 = toPix(section.Nodes[i]);
                    PointF p2 = toPix(section.Nodes[i + 1]);
                    g.DrawLine(pen, p1, p2);
                }
            }

            // Düğümleri Çiz
            foreach (var node in section.Nodes)
            {
                PointF p = toPix(node);
                g.FillEllipse(Brushes.Red, p.X - 3, p.Y - 3, 6, 6);
            }
        }
    }
}