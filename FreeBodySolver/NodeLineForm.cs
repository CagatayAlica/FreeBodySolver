using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FreeBodySolver.Models;
using FreeBodySolver.Solver;
using FreeBodySolver.UI;
using FreeBodySolver.Core;

namespace FreeBodySolver
{
    public class NodeLineForm : Form
    {
        private string NodeLoadUnit = "kN";
        private string LineLoadUnit = "kN/m";

        private PictureBox canvas;
        private Panel topPanel;
        private Label lblNodeId, lblNodeX, lblLineA, lblLineB;
        private TextBox txtNodeId, txtNodeX, txtLineA, txtLineB;
        private Button btnAddNode, btnAddLine, btnDeleteNode, btnDeleteLine, btnClear, btnZoomExtent, btnAddLoad, btnSolve;
        private Button btnNewModel, btnSaveModel, btnSaveAsModel, btnLoadModel;
        private Button btnUndo, btnRedo, btnSection;
        private ComboBox cmbAssignSection;
        private Button btnAssignSection;

        private string currentFilePath = null;

        private Dictionary<int, List<LoadEntry>> nodeLoads = new Dictionary<int, List<LoadEntry>>();
        private Dictionary<int, List<LoadEntry>> lineLoads = new Dictionary<int, List<LoadEntry>>();

        private HashSet<int> selectedNodeIds = new HashSet<int>();
        private HashSet<int> selectedLineIds = new HashSet<int>();
        private bool isSyncing = false;

        private bool isBoxSelecting = false;
        private Point boxStartPoint;
        private Point boxCurrentPoint;

        private Button btnAddLoadLine;
        private ListBox lstNodes, lstLines;
        private const float NodeRadius = 6f;

        private Dictionary<int, PointF> nodes = new Dictionary<int, PointF>();
        private List<Line> lines = new List<Line>();
        private int nextLineId = 1;

        private Dictionary<int, SupportType> supports = new Dictionary<int, SupportType>();

        private ContextMenuStrip lineContextMenu;
        private ContextMenuStrip nodeContextMenu;
        private bool addLineMode = false;
        private int addLineFirstNode = -1;
        private Button btnAddLineBySelect;
        private Label statusLabel;
        private float zoom = 1f;
        private PointF pan = new PointF(0, 0);
        private bool isPanning = false;
        private Point lastPanPoint;

        private Stack<ModelSnapshot> undoStack = new Stack<ModelSnapshot>();
        private Stack<ModelSnapshot> redoStack = new Stack<ModelSnapshot>();

        public NodeLineForm()
        {
            InitializeComponents();
        }

        private ModelSnapshot CreateSnapshot()
        {
            return new ModelSnapshot
            {
                Nodes = this.nodes.ToDictionary(k => k.Key, v => new PointF(v.Value.X, v.Value.Y)),
                Lines = this.lines.Select(l => new Line(l.Id, l.A, l.B, l.SectionName)).ToList(),
                Supports = this.supports.ToDictionary(k => k.Key, v => v.Value),
                NodeLoads = this.nodeLoads.ToDictionary(k => k.Key, v => v.Value.Select(l => new LoadEntry(l.Value, l.Group, l.Dir)).ToList()),
                LineLoads = this.lineLoads.ToDictionary(k => k.Key, v => v.Value.Select(l => new LoadEntry(l.Value, l.Group, l.Dir)).ToList()),
                NextLineId = this.nextLineId
            };
        }

        private void SaveState()
        {
            undoStack.Push(CreateSnapshot());
            redoStack.Clear();
            UpdateUndoRedoButtons();
        }

        private void RestoreSnapshot(ModelSnapshot snap)
        {
            this.nodes = snap.Nodes.ToDictionary(k => k.Key, v => new PointF(v.Value.X, v.Value.Y));
            this.lines = snap.Lines.Select(l => new Line(l.Id, l.A, l.B, l.SectionName)).ToList();
            this.supports = snap.Supports.ToDictionary(k => k.Key, v => v.Value);
            this.nodeLoads = snap.NodeLoads.ToDictionary(k => k.Key, v => v.Value.Select(l => new LoadEntry(l.Value, l.Group, l.Dir)).ToList());
            this.lineLoads = snap.LineLoads.ToDictionary(k => k.Key, v => v.Value.Select(l => new LoadEntry(l.Value, l.Group, l.Dir)).ToList());
            this.nextLineId = snap.NextLineId;

            selectedNodeIds.Clear();
            selectedLineIds.Clear();

            RefreshLists();
            canvas.Invalidate();
            UpdateUndoRedoButtons();
        }

        private void UpdateUndoRedoButtons()
        {
            if (btnUndo != null) btnUndo.Enabled = undoStack.Count > 0;
            if (btnRedo != null) btnRedo.Enabled = redoStack.Count > 0;
        }

        private Color GetGroupColor(string groupName)
        {
            switch (groupName)
            {
                case "Dead Load": return Color.SaddleBrown;
                case "Live Load": return Color.Blue;
                case "WindLoadPressure": return Color.DarkGreen;
                case "WindLoadSuction": return Color.Magenta;
                case "Snow Load": return Color.DeepSkyBlue;
                default: return Color.DarkOrange;
            }
        }

        private void InitializeComponents()
        {
            this.Text = "Arkitech Beam Analyzer";
            this.ClientSize = new Size(1100, 650);

            topPanel = new Panel { Dock = DockStyle.Top, Height = 145 };

            lblNodeId = new Label { Text = "Node ID:", Location = new Point(8, 8), AutoSize = true };
            txtNodeId = new TextBox { Location = new Point(70, 6), Width = 60 };
            lblNodeX = new Label { Text = "X:", Location = new Point(140, 8), AutoSize = true };
            txtNodeX = new TextBox { Location = new Point(160, 6), Width = 80 };
            btnAddNode = new Button { Text = "Add Node", Location = new Point(250, 4), Width = 90 };
            btnAddNode.Click += BtnAddNode_Click;
            btnAddLoad = new Button { Text = "Add Load", Location = new Point(350, 4), Width = 90 };
            btnAddLoad.Click += BtnAddLoad_Click;
            lblLineA = new Label { Text = "Line A:", Location = new Point(360, 8), AutoSize = true };
            txtLineA = new TextBox { Location = new Point(415, 6), Width = 60 };
            lblLineB = new Label { Text = "Line B:", Location = new Point(485, 8), AutoSize = true };
            txtLineB = new TextBox { Location = new Point(530, 6), Width = 60 };
            btnAddLine = new Button { Text = "Add Line", Location = new Point(600, 4), Width = 90 };
            btnAddLine.Click += BtnAddLine_Click;
            btnAddLineBySelect = new Button { Text = "Add Line (select)", Location = new Point(700, 4), Width = 120 };
            btnAddLineBySelect.Click += BtnAddLineBySelect_Click;

            var lblSupportFor = new Label { Text = "Support Node:", Location = new Point(8, 36), AutoSize = true };
            var txtSupportNode = new TextBox { Name = "txtSupportNode", Location = new Point(90, 34), Width = 60 };
            var cmbSupportType = new ComboBox { Name = "cmbSupportType", Location = new Point(160, 34), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };

            cmbSupportType.Items.AddRange(new object[] { "Pin", "Roller", "Fixed" });
            cmbSupportType.SelectedIndex = 0;

            var btnAddSupport = new Button { Text = "Add Support", Location = new Point(270, 32), Width = 100 };
            btnAddSupport.Click += (s, e) => BtnAddSupport_Click(s, e, txtSupportNode, cmbSupportType);
            var btnDeleteSupport = new Button { Text = "Delete Support", Location = new Point(380, 32), Width = 100 };
            btnDeleteSupport.Click += BtnDeleteSupport_Click;
            btnDeleteNode = new Button { Text = "Delete Node", Location = new Point(250, 36), Width = 90 };
            btnDeleteNode.Click += BtnDeleteNode_Click;
            btnDeleteLine = new Button { Text = "Delete Line", Location = new Point(350, 36), Width = 90 };
            btnDeleteLine.Click += BtnDeleteLine_Click;
            btnClear = new Button { Text = "Seçimi Temizle", Location = new Point(600, 36), Width = 100 };
            btnClear.Click += (s, e) => { selectedNodeIds.Clear(); selectedLineIds.Clear(); SyncListSelections(); canvas.Invalidate(); };
            btnZoomExtent = new Button { Text = "Zoom Extent", Location = new Point(700, 36), Width = 90 };
            btnZoomExtent.Click += ZoomExtent_Click;

            btnSolve = new Button { Text = "Analiz Yap (Solve)", BackColor = Color.LightGreen, Font = new Font(this.Font, FontStyle.Bold), Width = 130, Height = 30 };
            btnSolve.Click += BtnSolve_Click;

            btnNewModel = new Button { Text = "Yeni (New)", Width = 100, Height = 28 };
            btnNewModel.Click += BtnNewModel_Click;
            btnSaveModel = new Button { Text = "Kaydet (Save)", Width = 110, Height = 28 };
            btnSaveModel.Click += BtnSaveModel_Click;
            btnSaveAsModel = new Button { Text = "Farklı Kaydet (Save As)", Width = 150, Height = 28 };
            btnSaveAsModel.Click += BtnSaveAsModel_Click;
            btnLoadModel = new Button { Text = "Yükle (Load)", Width = 110, Height = 28 };
            btnLoadModel.Click += BtnLoadModel_Click;

            btnUndo = new Button { Text = "⏪ Geri (Undo)", Width = 110, Height = 28, Enabled = false };
            btnUndo.Click += (s, e) => {
                if (undoStack.Count > 0)
                {
                    redoStack.Push(CreateSnapshot());
                    RestoreSnapshot(undoStack.Pop());
                }
            };

            btnRedo = new Button { Text = "İleri (Redo) ⏩", Width = 110, Height = 28, Enabled = false };
            btnRedo.Click += (s, e) => {
                if (redoStack.Count > 0)
                {
                    undoStack.Push(CreateSnapshot());
                    RestoreSnapshot(redoStack.Pop());
                }
            };

            btnSection = new Button { Text = "Kesit Tanımla", Width = 110, Height = 28, BackColor = Color.LightYellow };
            btnSection.Click += (s, e) => {
                using (var frm = new SectionViewerForm()) { frm.ShowDialog(this); }
            };

            cmbAssignSection = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbAssignSection.DropDown += (s, e) => {
                cmbAssignSection.Items.Clear();
                cmbAssignSection.Items.Add("Varsayılan Kesit");
                foreach (var p in SectionDatabase.LoadProfiles()) cmbAssignSection.Items.Add(p.Name);
            };
            btnAssignSection = new Button { Text = "Kesit Ata", Width = 80, BackColor = Color.LightCyan };
            btnAssignSection.Click += (s, e) => {
                if (selectedLineIds.Count == 0) { MessageBox.Show("Önce çizgi seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (cmbAssignSection.SelectedItem == null) { MessageBox.Show("Açılır menüden bir kesit seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

                string sec = cmbAssignSection.SelectedItem.ToString();
                if (sec == "Varsayılan Kesit") sec = "";

                SaveState();
                foreach (var lid in selectedLineIds)
                {
                    var line = lines.FirstOrDefault(l => l.Id == lid);
                    if (line != null) line.SectionName = sec;
                }
                RefreshLists();
                canvas.Invalidate();
            };

            var topTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(6), AutoSize = false };
            topTable.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
            topTable.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
            topTable.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
            topTable.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

            var row1 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None };
            row1.Controls.AddRange(new Control[] { lblNodeId, txtNodeId, lblNodeX, txtNodeX, btnAddNode, btnAddLoad });

            var row2 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None };
            btnAddLoadLine = new Button { Text = "Add Line Load", Width = 110 };
            btnAddLoadLine.Click += BtnAddLoadLine_Click;
            row2.Controls.AddRange(new Control[] { lblLineA, txtLineA, lblLineB, txtLineB, btnAddLine, btnAddLineBySelect, btnAddLoadLine, cmbAssignSection, btnAssignSection });

            var row3 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None };
            row3.Controls.AddRange(new Control[] { lblSupportFor, txtSupportNode, cmbSupportType, btnAddSupport, btnDeleteSupport, btnDeleteNode, btnDeleteLine, btnClear, btnZoomExtent, btnSolve });

            var row4 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None };
            row4.Controls.AddRange(new Control[] { btnNewModel, btnSaveModel, btnSaveAsModel, btnLoadModel, btnUndo, btnRedo, btnSection });

            var p1 = new Panel { Dock = DockStyle.Fill }; p1.Controls.Add(row1); row1.Location = new Point((p1.ClientSize.Width - row1.PreferredSize.Width) / 2, (p1.ClientSize.Height - row1.PreferredSize.Height) / 2); row1.Anchor = AnchorStyles.None;
            var p2 = new Panel { Dock = DockStyle.Fill }; p2.Controls.Add(row2); row2.Location = new Point((p2.ClientSize.Width - row2.PreferredSize.Width) / 2, (p2.ClientSize.Height - row2.PreferredSize.Height) / 2); row2.Anchor = AnchorStyles.None;
            var p3 = new Panel { Dock = DockStyle.Fill }; p3.Controls.Add(row3); row3.Location = new Point((p3.ClientSize.Width - row3.PreferredSize.Width) / 2, (p3.ClientSize.Height - row3.PreferredSize.Height) / 2); row3.Anchor = AnchorStyles.None;
            var p4 = new Panel { Dock = DockStyle.Fill }; p4.Controls.Add(row4); row4.Location = new Point((p4.ClientSize.Width - row4.PreferredSize.Width) / 2, (p4.ClientSize.Height - row4.PreferredSize.Height) / 2); row4.Anchor = AnchorStyles.None;

            topTable.Controls.Add(p1, 0, 0); topTable.Controls.Add(p2, 0, 1); topTable.Controls.Add(p3, 0, 2); topTable.Controls.Add(p4, 0, 3);
            topPanel.Controls.Add(topTable);

            canvas = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White };
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.MouseEnter += (s, e) => { canvas.Focus(); };
            canvas.MouseWheel += Canvas_MouseWheel;

            canvas.Focus();
            statusLabel = new Label { Dock = DockStyle.Bottom, Height = 22, Text = string.Empty, TextAlign = ContentAlignment.MiddleLeft };
            lstNodes = new ListBox { Dock = DockStyle.Right, Width = 180 };
            lstNodes.SelectionMode = SelectionMode.MultiExtended;
            lstNodes.SelectedIndexChanged += (s, e) => {
                if (isSyncing) return;
                selectedNodeIds.Clear();
                foreach (var it in lstNodes.SelectedItems)
                {
                    var line = it.ToString();
                    if (string.IsNullOrEmpty(line)) continue;
                    var idPart = line.Split(':')[0];
                    if (int.TryParse(idPart, out int nid)) selectedNodeIds.Add(nid);
                }
                canvas.Invalidate();
            };

            lstLines = new ListBox { Dock = DockStyle.Right, Width = 180 };
            lstLines.SelectionMode = SelectionMode.MultiExtended;
            lstLines.SelectedIndexChanged += (s, e) => {
                if (isSyncing) return;
                selectedLineIds.Clear();
                foreach (var it in lstLines.SelectedItems)
                {
                    var line = it.ToString();
                    if (string.IsNullOrEmpty(line)) continue;
                    var idPart = line.Split(':')[0];
                    if (int.TryParse(idPart, out int lid)) selectedLineIds.Add(lid);
                }
                canvas.Invalidate();
            };

            this.Controls.Add(statusLabel);
            this.Controls.Add(canvas);
            this.Controls.Add(lstLines);
            this.Controls.Add(lstNodes);
            this.Controls.Add(topPanel);

            this.Resize += (s, e) => canvas.Invalidate();
        }

        private PointF WorldToPixel(PointF world) { return new PointF(pan.X + world.X * zoom, pan.Y - world.Y * zoom); }
        private PointF PixelToWorld(PointF pixel) { return new PointF((pixel.X - pan.X) / zoom, (pan.Y - pixel.Y) / zoom); }

        private float Distance(PointF p1, PointF p2) { float dx = p2.X - p1.X; float dy = p2.Y - p1.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }
        private float DistancePointToSegment(PointF pt, PointF p1, PointF p2)
        {
            float l2 = (p1.X - p2.X) * (p1.X - p2.X) + (p1.Y - p2.Y) * (p1.Y - p2.Y);
            if (l2 == 0) return Distance(pt, p1);
            float t = Math.Max(0, Math.Min(1, ((pt.X - p1.X) * (p2.X - p1.X) + (pt.Y - p1.Y) * (p2.Y - p1.Y)) / l2));
            PointF projection = new PointF(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y));
            return Distance(pt, projection);
        }
        private int FindNodeAt(Point pt)
        {
            float hitTolerance = NodeRadius + 4f; PointF clickPt = new PointF(pt.X, pt.Y);
            foreach (var kv in nodes)
            {
                PointF screenPos = WorldToPixel(kv.Value);
                if (Distance(clickPt, screenPos) <= hitTolerance) return kv.Key;
            }
            return -1;
        }
        private int FindLineAt(Point pt)
        {
            float hitTolerance = 5f; PointF clickPt = new PointF(pt.X, pt.Y);
            for (int i = 0; i < lines.Count; i++)
            {
                var ln = lines[i]; if (!nodes.ContainsKey(ln.A) || !nodes.ContainsKey(ln.B)) continue;
                PointF p1 = WorldToPixel(nodes[ln.A]); PointF p2 = WorldToPixel(nodes[ln.B]);
                if (DistancePointToSegment(clickPt, p1, p2) <= hitTolerance) return i;
            }
            return -1;
        }

        private void ZoomExtent_Click(object sender, EventArgs e)
        {
            if (nodes.Count == 0) return;
            var w = canvas.ClientSize.Width; var h = canvas.ClientSize.Height; float margin = 40f;
            float minX = nodes.Values.Min(v => v.X); float maxX = nodes.Values.Max(v => v.X);
            float minY = nodes.Values.Min(v => v.Y); float maxY = nodes.Values.Max(v => v.Y);
            if (Math.Abs(maxX - minX) < 1e-6f) { minX -= 1; maxX += 1; }
            if (Math.Abs(maxY - minY) < 1e-6f) { minY -= 1; maxY += 1; }
            float sx = (w - 2 * margin) / (maxX - minX); float sy = (h - 2 * margin) / (maxY - minY);
            zoom = Math.Max(0.01f, Math.Min(sx, sy));
            float cx = (minX + maxX) / 2f; float cy = (minY + maxY) / 2f;
            pan = new PointF(w / 2f - cx * zoom, h / 2f + cy * zoom);
            canvas.Invalidate();
        }

        private void BtnNewModel_Click(object sender, EventArgs e)
        {
            var dr = MessageBox.Show("Mevcut model silinecek ve yeni, boş bir model oluşturulacak. Emin misiniz?", "Yeni Model", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (dr == DialogResult.Yes)
            {
                SaveState();
                currentFilePath = null; nodes.Clear(); lines.Clear(); supports.Clear(); nodeLoads.Clear(); lineLoads.Clear();
                selectedNodeIds.Clear(); selectedLineIds.Clear(); nextLineId = 1;
                zoom = 50f; pan = new PointF(canvas.Width / 2f, canvas.Height / 2f);
                RefreshLists(); canvas.Invalidate();
            }
        }

        private void PerformSave(string filePath)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(filePath))
                {
                    sw.WriteLine("[NODES]"); foreach (var kv in nodes) sw.WriteLine($"{kv.Key};{kv.Value.X.ToString(CultureInfo.InvariantCulture)};{kv.Value.Y.ToString(CultureInfo.InvariantCulture)}");

                    sw.WriteLine("[LINES]"); foreach (var ln in lines) sw.WriteLine($"{ln.Id};{ln.A};{ln.B};{ln.SectionName}");

                    sw.WriteLine("[SUPPORTS]"); foreach (var kv in supports) sw.WriteLine($"{kv.Key};{kv.Value.ToString()}");
                    sw.WriteLine("[NODELOADS]"); foreach (var kv in nodeLoads) foreach (var ld in kv.Value) sw.WriteLine($"{kv.Key};{ld.Value.ToString(CultureInfo.InvariantCulture)};{ld.Group};{ld.Dir.ToString()}");
                    sw.WriteLine("[LINELOADS]"); foreach (var kv in lineLoads) foreach (var ld in kv.Value) sw.WriteLine($"{kv.Key};{ld.Value.ToString(CultureInfo.InvariantCulture)};{ld.Group};{ld.Dir.ToString()}");
                }
                statusLabel.Text = $"Model kaydedildi: {filePath}";
            }
            catch (Exception ex) { MessageBox.Show($"Kaydetme sırasında bir hata oluştu: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void BtnSaveModel_Click(object sender, EventArgs e) { if (!string.IsNullOrEmpty(currentFilePath)) PerformSave(currentFilePath); else BtnSaveAsModel_Click(sender, e); }
        private void BtnSaveAsModel_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "FreeBody Model Dosyası (*.fbm)|*.fbm|Tüm Dosyalar (*.*)|*.*"; sfd.Title = "Modeli Farklı Kaydet";
                if (sfd.ShowDialog() == DialogResult.OK) { currentFilePath = sfd.FileName; PerformSave(currentFilePath); }
            }
        }

        private void BtnLoadModel_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "FreeBody Model Dosyası (*.fbm)|*.fbm|Tüm Dosyalar (*.*)|*.*"; ofd.Title = "Modeli Yükle";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        SaveState();
                        nodes.Clear(); lines.Clear(); supports.Clear(); nodeLoads.Clear(); lineLoads.Clear();
                        selectedNodeIds.Clear(); selectedLineIds.Clear();
                        string currentSection = "";
                        using (StreamReader sr = new StreamReader(ofd.FileName))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                line = line.Trim(); if (string.IsNullOrEmpty(line)) continue; if (line.StartsWith("[")) { currentSection = line; continue; }
                                var parts = line.Split(';');
                                if (currentSection == "[NODES]") nodes[int.Parse(parts[0])] = new PointF(float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture));
                                else if (currentSection == "[LINES]")
                                {
                                    string sec = parts.Length > 3 ? parts[3] : "";
                                    lines.Add(new Line(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), sec));
                                }
                                else if (currentSection == "[SUPPORTS]") supports[int.Parse(parts[0])] = (SupportType)Enum.Parse(typeof(SupportType), parts[1]);
                                else if (currentSection == "[NODELOADS]") { int nid = int.Parse(parts[0]); if (!nodeLoads.ContainsKey(nid)) nodeLoads[nid] = new List<LoadEntry>(); nodeLoads[nid].Add(new LoadEntry(float.Parse(parts[1], CultureInfo.InvariantCulture), parts[2], (LoadDirection)Enum.Parse(typeof(LoadDirection), parts[3]))); }
                                else if (currentSection == "[LINELOADS]") { int lid = int.Parse(parts[0]); if (!lineLoads.ContainsKey(lid)) lineLoads[lid] = new List<LoadEntry>(); lineLoads[lid].Add(new LoadEntry(float.Parse(parts[1], CultureInfo.InvariantCulture), parts[2], (LoadDirection)Enum.Parse(typeof(LoadDirection), parts[3]))); }
                            }
                        }
                        currentFilePath = ofd.FileName; nextLineId = lines.Count > 0 ? lines.Max(l => l.Id) + 1 : 1;
                        RefreshLists(); ZoomExtent_Click(null, null); canvas.Invalidate();
                        statusLabel.Text = $"Model yüklendi: {currentFilePath}";
                    }
                    catch (Exception ex) { MessageBox.Show($"Yükleme sırasında bir hata oluştu: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
            }
        }

        private void BtnAddLineBySelect_Click(object sender, EventArgs e)
        {
            addLineMode = !addLineMode; addLineFirstNode = -1;
            if (addLineMode) { btnAddLineBySelect.Text = "Cancel Add Line"; statusLabel.Text = "Add line by selecting two nodes."; }
            else { btnAddLineBySelect.Text = "Add Line (select)"; statusLabel.Text = string.Empty; }
            canvas.Invalidate();
        }

        private void BtnSolve_Click(object sender, EventArgs e)
        {
            if (nodes.Count < 2 || lines.Count == 0) { MessageBox.Show("Analiz yapabilmek için en az 2 node ve 1 çizgi gereklidir.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (supports.Count == 0) { MessageBox.Show("Sistem kararsız (Unstable). Lütfen en az bir mesnet (Support) ekleyin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var connectedNodes = new HashSet<int>();
            foreach (var ln in lines) { connectedNodes.Add(ln.A); connectedNodes.Add(ln.B); }
            foreach (var nid in nodes.Keys) { if (!connectedNodes.Contains(nid)) { MessageBox.Show($"Node {nid} hiçbir çizgiye bağlı değil. Sistem kararsız (Unstable).\nLütfen boşta kalan noktaları silin veya sisteme bağlayın.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error); return; } }
            int totalConstraints = 0;
            foreach (var kv in supports)
            {
                if (kv.Value == SupportType.Fixed) totalConstraints += 3;
                else if (kv.Value == SupportType.Pin) totalConstraints += 2;
                else if (kv.Value == SupportType.Roller) totalConstraints += 1;
            }
            if (totalConstraints < 3) { MessageBox.Show("Sistem kararsız (Unstable). Global stabilite için en az 3 mesnet reaksiyonuna ihtiyaç vardır.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            try
            {
                var results = FemSolver.Solve(nodes, lines, supports, nodeLoads, lineLoads);

                var nodeIds = nodes.Keys.OrderBy(k => k).ToList();
                var nodeMap = new Dictionary<int, int>();
                for (int i = 0; i < nodeIds.Count; i++) nodeMap[nodeIds[i]] = i;

                using (var resForm = new AnalysisResultForm(nodeIds, nodeMap, results, supports, lines, nodes, lineLoads))
                {
                    resForm.ShowDialog();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Analiz Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void RefreshLists()
        {
            isSyncing = true; lstNodes.Items.Clear();
            foreach (var kv in nodes.OrderBy(k => k.Key))
            {
                var sup = supports.ContainsKey(kv.Key) ? supports[kv.Key].ToString() : ""; var add = string.IsNullOrEmpty(sup) ? "" : $" [{sup}]";
                lstNodes.Items.Add($"{kv.Key}: X={kv.Value.X.ToString(CultureInfo.CurrentCulture)}, Y={kv.Value.Y.ToString(CultureInfo.CurrentCulture)}{add}");
            }
            lstLines.Items.Clear();
            foreach (var ln in lines)
            {
                string secInfo = string.IsNullOrEmpty(ln.SectionName) ? "" : $" [{ln.SectionName}]";
                lstLines.Items.Add($"{ln.Id}: {ln.A} - {ln.B}{secInfo}");
            }
            isSyncing = false; SyncListSelections();
        }

        private void SyncListSelections()
        {
            if (isSyncing) return; isSyncing = true;
            lstNodes.ClearSelected();
            for (int i = 0; i < lstNodes.Items.Count; i++) { if (int.TryParse(lstNodes.Items[i].ToString().Split(':')[0], out int nid)) { if (selectedNodeIds.Contains(nid)) lstNodes.SetSelected(i, true); } }
            lstLines.ClearSelected();
            for (int i = 0; i < lstLines.Items.Count; i++) { if (int.TryParse(lstLines.Items[i].ToString().Split(':')[0].Trim(), out int lid)) { if (selectedLineIds.Contains(lid)) lstLines.SetSelected(i, true); } }
            isSyncing = false;
        }

        private void BtnAddNode_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(txtNodeId.Text.Trim(), out int id)) { MessageBox.Show("Geçerli bir Node ID girin.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            bool parsedX = float.TryParse(txtNodeX.Text.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out float x) || float.TryParse(txtNodeX.Text.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out x);
            if (!parsedX) { MessageBox.Show("Geçerli bir X koordinatı girin.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (nodes.ContainsKey(id)) { MessageBox.Show("Bu ID zaten var.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var newNodePoint = new PointF(x, 0f);
            foreach (var kv in nodes) { if (Distance(kv.Value, newNodePoint) <= 1e-6f) { MessageBox.Show("O noktada bir node var.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } }

            SaveState();
            nodes[id] = newNodePoint;
            RefreshLists(); canvas.Invalidate();

            if (lines.Count > 0)
            {
                float minX = nodes.Values.Min(v => v.X); float maxX = nodes.Values.Max(v => v.X);
                float minY = nodes.Values.Min(v => v.Y); float maxY = nodes.Values.Max(v => v.Y);
                float tolWorld = (Math.Max(maxX - minX, maxY - minY) <= 0) ? 0.1f : Math.Max(maxX - minX, maxY - minY) * 0.005f;
                int splitIndex = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    var ln = lines[i]; if (!nodes.ContainsKey(ln.A) || !nodes.ContainsKey(ln.B)) continue;
                    if (DistancePointToSegment(nodes[id], nodes[ln.A], nodes[ln.B]) <= tolWorld) { splitIndex = i; break; }
                }
                if (splitIndex >= 0)
                {
                    var orig = lines[splitIndex];
                    if (MessageBox.Show($"Node {id} çizgi {orig.Id} üzerinde bulunuyor. Çizgiyi bölmek istiyor musunuz?", "Bölme onayı", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        SaveState();
                        lines.RemoveAt(splitIndex);
                        lines.Insert(splitIndex, new Line(orig.Id, orig.A, id, orig.SectionName));
                        lines.Insert(splitIndex + 1, new Line(nextLineId++, id, orig.B, orig.SectionName));
                        statusLabel.Text = $"Line {orig.Id} split by node {id}.";
                        RefreshLists(); canvas.Invalidate();
                    }
                }
            }
        }

        private void BtnAddLine_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(txtLineA.Text.Trim(), out int a) || !int.TryParse(txtLineB.Text.Trim(), out int b)) { MessageBox.Show("Geçerli iki node ID girin.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (a == b) { MessageBox.Show("Başlangıç ve bitiş farklı olmalı.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!nodes.ContainsKey(a) || !nodes.ContainsKey(b)) { MessageBox.Show("Belirtilen node ID bulunamadı.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (lines.Any(l => (l.A == a && l.B == b) || (l.A == b && l.B == a))) { MessageBox.Show("Bu çizgi zaten var.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            SaveState();
            lines.Add(new Line(nextLineId++, a, b)); RefreshLists(); canvas.Invalidate();
        }

        private void BtnDeleteNode_Click(object sender, EventArgs e)
        {
            if (selectedNodeIds.Count == 0) return;
            SaveState();
            foreach (var id in selectedNodeIds.ToList()) { lines.RemoveAll(t => t.A == id || t.B == id); nodes.Remove(id); supports.Remove(id); }
            selectedNodeIds.Clear(); RefreshLists(); canvas.Invalidate();
        }

        private void BtnDeleteLine_Click(object sender, EventArgs e)
        {
            if (selectedLineIds.Count == 0) return;
            SaveState();
            foreach (var id in selectedLineIds.ToList()) { lines.RemoveAll(l => l.Id == id); lineLoads.Remove(id); }
            selectedLineIds.Clear(); RefreshLists(); canvas.Invalidate();
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            var pt = e.Location;
            if (e.Button == MouseButtons.Middle) { isPanning = true; lastPanPoint = e.Location; canvas.Cursor = Cursors.Hand; return; }
            int nodeHit = FindNodeAt(pt); int lineHit = FindLineAt(pt);

            if (addLineMode)
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (nodeHit >= 0)
                    {
                        if (addLineFirstNode == -1) addLineFirstNode = nodeHit;
                        else
                        {
                            int a = addLineFirstNode; int b = nodeHit;
                            if (a == b) statusLabel.Text = "Başlangıç ve bitiş aynı olamaz.";
                            else if (lines.Any(t => (t.A == a && t.B == b) || (t.A == b && t.B == a))) statusLabel.Text = "Bu çizgi zaten var.";
                            else
                            {
                                SaveState();
                                lines.Add(new Line(nextLineId++, a, b));
                                statusLabel.Text = $"Çizgi eklendi: {a} - {b}.";
                            }
                            addLineFirstNode = -1; RefreshLists();
                        }
                        canvas.Invalidate();
                    }
                }
                else if (e.Button == MouseButtons.Right) { addLineMode = false; addLineFirstNode = -1; btnAddLineBySelect.Text = "Add Line (select)"; statusLabel.Text = string.Empty; canvas.Invalidate(); }
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (nodeHit >= 0)
                {
                    if ((ModifierKeys & Keys.Control) == Keys.Control) selectedNodeIds.Add(nodeHit);
                    else if ((ModifierKeys & Keys.Shift) == Keys.Shift) selectedNodeIds.Remove(nodeHit);
                    else { selectedNodeIds.Clear(); selectedNodeIds.Add(nodeHit); }
                    selectedLineIds.Clear(); SyncListSelections();
                }
                else if (lineHit >= 0)
                {
                    var lineId = lines[lineHit].Id;
                    if ((ModifierKeys & Keys.Control) == Keys.Control) selectedLineIds.Add(lineId);
                    else if ((ModifierKeys & Keys.Shift) == Keys.Shift) selectedLineIds.Remove(lineId);
                    else { selectedLineIds.Clear(); selectedLineIds.Add(lineId); }
                    selectedNodeIds.Clear(); SyncListSelections();
                }
                else
                {
                    isBoxSelecting = true; boxStartPoint = e.Location; boxCurrentPoint = e.Location;
                    if ((ModifierKeys & Keys.Control) != Keys.Control && (ModifierKeys & Keys.Shift) != Keys.Shift)
                    {
                        selectedNodeIds.Clear(); selectedLineIds.Clear(); SyncListSelections();
                    }
                }
                canvas.Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (nodeHit >= 0)
                {
                    if (!selectedNodeIds.Contains(nodeHit)) { selectedNodeIds.Clear(); selectedNodeIds.Add(nodeHit); }
                    selectedLineIds.Clear(); SyncListSelections();
                    EnsureNodeContextMenu(); nodeContextMenu.Show(canvas, e.Location);
                }
                else if (lineHit >= 0)
                {
                    var lineId = lines[lineHit].Id;
                    if (!selectedLineIds.Contains(lineId)) { selectedLineIds.Clear(); selectedLineIds.Add(lineId); }
                    selectedNodeIds.Clear(); SyncListSelections();
                    EnsureLineContextMenu(); lineContextMenu.Show(canvas, e.Location);
                }
                else { selectedNodeIds.Clear(); selectedLineIds.Clear(); SyncListSelections(); }
                canvas.Invalidate();
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPanning) { pan = new PointF(pan.X + e.Location.X - lastPanPoint.X, pan.Y + e.Location.Y - lastPanPoint.Y); lastPanPoint = e.Location; canvas.Invalidate(); return; }
            if (isBoxSelecting) { boxCurrentPoint = e.Location; canvas.Invalidate(); return; }
            var hitNode = FindNodeAt(e.Location); var hitLine = FindLineAt(e.Location);
            if (hitNode >= 0) canvas.Cursor = Cursors.Hand; else if (hitLine >= 0) canvas.Cursor = Cursors.Default; else canvas.Cursor = Cursors.Cross;
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (isPanning && e.Button == MouseButtons.Middle) { isPanning = false; canvas.Cursor = Cursors.Default; }
            if (isBoxSelecting)
            {
                isBoxSelecting = false;
                if (Math.Abs(boxStartPoint.X - boxCurrentPoint.X) < 3 && Math.Abs(boxStartPoint.Y - boxCurrentPoint.Y) < 3) { canvas.Invalidate(); return; }
                Rectangle rect = new Rectangle(Math.Min(boxStartPoint.X, boxCurrentPoint.X), Math.Min(boxStartPoint.Y, boxCurrentPoint.Y), Math.Abs(boxStartPoint.X - boxCurrentPoint.X), Math.Abs(boxStartPoint.Y - boxCurrentPoint.Y));
                bool isCrossing = boxCurrentPoint.X < boxStartPoint.X;
                if (nodes.Count > 0)
                {
                    foreach (var kv in nodes)
                    {
                        var px = WorldToPixel(kv.Value);
                        if (rect.Contains((int)px.X, (int)px.Y))
                        {
                            if ((ModifierKeys & Keys.Shift) == Keys.Shift) selectedNodeIds.Remove(kv.Key);
                            else selectedNodeIds.Add(kv.Key);
                        }
                    }
                    foreach (var ln in lines)
                    {
                        if (!nodes.ContainsKey(ln.A) || !nodes.ContainsKey(ln.B)) continue;
                        var p1 = WorldToPixel(nodes[ln.A]); var p2 = WorldToPixel(nodes[ln.B]);
                        bool isSel = isCrossing ? LineIntersectsRect(p1, p2, rect) : rect.Contains((int)p1.X, (int)p1.Y) && rect.Contains((int)p2.X, (int)p2.Y);
                        if (isSel)
                        {
                            if ((ModifierKeys & Keys.Shift) == Keys.Shift) selectedLineIds.Remove(ln.Id);
                            else selectedLineIds.Add(ln.Id);
                        }
                    }
                }
                SyncListSelections(); canvas.Invalidate();
            }
        }

        private bool LineIntersectsRect(PointF p1, PointF p2, Rectangle r)
        {
            if (r.Contains((int)p1.X, (int)p1.Y) || r.Contains((int)p2.X, (int)p2.Y)) return true;
            if (LineIntersectsLine(p1, p2, new PointF(r.Left, r.Top), new PointF(r.Right, r.Top))) return true;
            if (LineIntersectsLine(p1, p2, new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom))) return true;
            if (LineIntersectsLine(p1, p2, new PointF(r.Left, r.Top), new PointF(r.Left, r.Bottom))) return true;
            if (LineIntersectsLine(p1, p2, new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom))) return true;
            return false;
        }

        private bool LineIntersectsLine(PointF p1, PointF p2, PointF p3, PointF p4)
        {
            float denominator = ((p1.X - p2.X) * (p3.Y - p4.Y)) - ((p1.Y - p2.Y) * (p3.X - p4.X));
            if (denominator == 0) return false;
            float t = (((p1.X - p3.X) * (p3.Y - p4.Y)) - ((p1.Y - p3.Y) * (p3.X - p4.X))) / denominator;
            float u = -(((p1.X - p2.X) * (p1.Y - p3.Y)) - ((p1.Y - p2.Y) * (p1.X - p3.X))) / denominator;
            return (t >= 0 && t <= 1 && u >= 0 && u <= 1);
        }

        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            if (nodes.Count == 0) return;
            var mousePt = e.Location; var worldBefore = PixelToWorld(mousePt);
            float factor = (e.Delta > 0) ? 1.15f : (1f / 1.15f); zoom *= factor; zoom = Math.Max(0.01f, Math.Min(zoom, 1000f));
            pan = new PointF(mousePt.X - worldBefore.X * zoom, mousePt.Y + worldBefore.Y * zoom);
            canvas.Invalidate();
        }

        private void BtnAddSupport_Click(object sender, EventArgs e, TextBox txtSupportNode, ComboBox cmbSupportType)
        {
            var targets = new HashSet<int>(selectedNodeIds);
            if (targets.Count == 0)
            {
                if (int.TryParse(txtSupportNode.Text.Trim(), out int id)) targets.Add(id);
                else { MessageBox.Show("Önce ekrandan/listeden node seçin.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            var typeStr = cmbSupportType.SelectedItem?.ToString(); SupportType st = SupportType.Pin;
            if (string.Equals(typeStr, "Roller", StringComparison.OrdinalIgnoreCase)) st = SupportType.Roller;
            if (string.Equals(typeStr, "Fixed", StringComparison.OrdinalIgnoreCase)) st = SupportType.Fixed;

            SaveState();
            bool updated = false;
            foreach (var id in targets) { if (nodes.ContainsKey(id)) { supports[id] = st; updated = true; } }
            if (updated) { RefreshLists(); canvas.Invalidate(); }
        }

        private void BtnDeleteSupport_Click(object sender, EventArgs e)
        {
            var targets = new HashSet<int>(selectedNodeIds);
            if (targets.Count == 0) { MessageBox.Show("Silmek için node seçin.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            SaveState();
            bool removed = false;
            foreach (var id in targets) { if (supports.ContainsKey(id)) { supports.Remove(id); removed = true; } }
            if (removed) { RefreshLists(); canvas.Invalidate(); }
        }

        private void EnsureLineContextMenu()
        {
            if (lineContextMenu != null) return;
            lineContextMenu = new ContextMenuStrip();

            // --- YENİ EKLENEN: Sağ Tık "Kesit Ata" Alt Menüsü ---
            var miAssignSection = new ToolStripMenuItem("Kesit Ata");

            // Menü her açıldığında kütüphanedeki güncel kesitleri yükle (Dinamik Menü)
            lineContextMenu.Opening += (s, e) => {
                miAssignSection.DropDownItems.Clear();

                var miDefault = new ToolStripMenuItem("Varsayılan Kesit (Kesiti Temizle)");
                miDefault.Click += (sender, args) => AssignSectionToSelectedLines("");
                miAssignSection.DropDownItems.Add(miDefault);

                miAssignSection.DropDownItems.Add(new ToolStripSeparator());

                // Kaydedilen kesitleri veritabanından çek ve alt menü olarak ekle
                var profiles = FreeBodySolver.Core.SectionDatabase.LoadProfiles();
                foreach (var p in profiles)
                {
                    var miProfile = new ToolStripMenuItem(p.Name);
                    miProfile.Click += (sender, args) => AssignSectionToSelectedLines(p.Name);
                    miAssignSection.DropDownItems.Add(miProfile);
                }
            };
            // ---------------------------------------------------

            var miLoadLine = new ToolStripMenuItem("Yük Ekle (Load)");
            miLoadLine.Click += (s, e) => { BtnAddLoadLine_Click(s, EventArgs.Empty); };

            var miDelete = new ToolStripMenuItem("Çizgiyi Sil (Delete Line)");
            miDelete.Click += (s, e) => {
                if (selectedLineIds.Count > 0)
                {
                    SaveState(); // UNDO STATE SAVE
                    foreach (var id in selectedLineIds.ToList()) { lines.RemoveAll(l => l.Id == id); lineLoads.Remove(id); }
                    selectedLineIds.Clear(); RefreshLists(); canvas.Invalidate();
                }
            };

            // Menü elemanlarını sırayla ekle
            lineContextMenu.Items.Add(miAssignSection);
            lineContextMenu.Items.Add(new ToolStripSeparator());
            lineContextMenu.Items.Add(miLoadLine);
            lineContextMenu.Items.Add(new ToolStripSeparator());
            lineContextMenu.Items.Add(miDelete);
        }

        // Sağ tık menüsünden seçilen kesiti seçili tüm çizgilere atayan yardımcı metot
        private void AssignSectionToSelectedLines(string sectionName)
        {
            if (selectedLineIds.Count == 0) return;

            SaveState(); // UNDO STATE SAVE
            foreach (var lid in selectedLineIds)
            {
                var line = lines.FirstOrDefault(l => l.Id == lid);
                if (line != null) line.SectionName = sectionName;
            }
            RefreshLists();
            canvas.Invalidate();
        }

        private void EnsureNodeContextMenu()
        {
            if (nodeContextMenu != null) return;
            nodeContextMenu = new ContextMenuStrip();
            var miSupport = new ToolStripMenuItem("Support");
            var miPin = new ToolStripMenuItem("Pin");
            var miRoller = new ToolStripMenuItem("Roller");
            var miFixed = new ToolStripMenuItem("Fixed (Ankastre)");
            var miRemoveSupport = new ToolStripMenuItem("Delete");

            miPin.Click += (s, e) => { SaveState(); foreach (var id in selectedNodeIds) supports[id] = SupportType.Pin; RefreshLists(); canvas.Invalidate(); };
            miRoller.Click += (s, e) => { SaveState(); foreach (var id in selectedNodeIds) supports[id] = SupportType.Roller; RefreshLists(); canvas.Invalidate(); };
            miFixed.Click += (s, e) => { SaveState(); foreach (var id in selectedNodeIds) supports[id] = SupportType.Fixed; RefreshLists(); canvas.Invalidate(); };
            miRemoveSupport.Click += (s, e) => { SaveState(); foreach (var id in selectedNodeIds) supports.Remove(id); RefreshLists(); canvas.Invalidate(); };

            miSupport.DropDownItems.Add(miPin); miSupport.DropDownItems.Add(miRoller); miSupport.DropDownItems.Add(miFixed); miSupport.DropDownItems.Add(new ToolStripSeparator()); miSupport.DropDownItems.Add(miRemoveSupport);
            var miCoordinate = new ToolStripMenuItem("Coordinate");
            miCoordinate.Click += (s, e) => { if (selectedNodeIds.Count > 0) ShowCoordinateDialog(selectedNodeIds.First()); };
            var miLoadNode = new ToolStripMenuItem("Load");
            miLoadNode.Click += (s, e) => { BtnAddLoad_Click(s, EventArgs.Empty); };
            var miDeleteNode = new ToolStripMenuItem("Delete Node");
            miDeleteNode.Click += (s, e) => {
                if (selectedNodeIds.Count > 0)
                {
                    SaveState();
                    foreach (var id in selectedNodeIds.ToList()) { lines.RemoveAll(t => t.A == id || t.B == id); nodes.Remove(id); supports.Remove(id); }
                    selectedNodeIds.Clear(); RefreshLists(); canvas.Invalidate();
                }
            };

            nodeContextMenu.Items.Add(miSupport); nodeContextMenu.Items.Add(miLoadNode); nodeContextMenu.Items.Add(miCoordinate); nodeContextMenu.Items.Add(miDeleteNode);
        }

        private void ShowCoordinateDialog(int nodeId)
        {
            if (!nodes.ContainsKey(nodeId)) return;
            var cur = nodes[nodeId];
            using (var f = new Form())
            {
                f.Text = $"Edit Coordinate - {nodeId}"; f.ClientSize = new Size(260, 110);
                var lblX = new Label { Text = "X:", Location = new Point(8, 10), AutoSize = true };
                var txtX = new TextBox { Location = new Point(40, 8), Width = 200, Text = cur.X.ToString(CultureInfo.CurrentCulture) };
                var lblY = new Label { Text = "Y:", Location = new Point(8, 40), AutoSize = true };
                var txtY = new TextBox { Location = new Point(40, 38), Width = 200, Text = cur.Y.ToString(CultureInfo.CurrentCulture) };
                var btnOk = new Button { Text = "OK", Location = new Point(40, 70), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Cancel", Location = new Point(140, 70), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lblX, txtX, lblY, txtY, btnOk, btnCancel }); f.AcceptButton = btnOk; f.CancelButton = btnCancel;
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    bool px = float.TryParse(txtX.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float nx);
                    bool py = float.TryParse(txtY.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float ny);
                    if (px && py)
                    {
                        SaveState();
                        nodes[nodeId] = new PointF(nx, ny); RefreshLists(); canvas.Invalidate();
                    }
                }
            }
        }

        private void BtnAddLoad_Click(object sender, EventArgs e)
        {
            if (selectedNodeIds.Count == 0) return;
            using (var f = new LoadManagerForm(nodeLoads.ContainsKey(selectedNodeIds.First()) ? nodeLoads[selectedNodeIds.First()] : new List<LoadEntry>(), $"Nodes - Yük Yönetimi"))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    SaveState();
                    foreach (var nid in selectedNodeIds) nodeLoads[nid] = f.Loads.Select(l => new LoadEntry(l.Value, l.Group, l.Dir)).ToList();
                    RefreshLists(); canvas.Invalidate();
                }
            }
        }

        private void BtnAddLoadLine_Click(object sender, EventArgs e)
        {
            if (selectedLineIds.Count == 0) return;
            using (var f = new LoadManagerForm(lineLoads.ContainsKey(selectedLineIds.First()) ? lineLoads[selectedLineIds.First()] : new List<LoadEntry>(), $"Lines - Yük Yönetimi"))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    SaveState();
                    foreach (var lid in selectedLineIds) lineLoads[lid] = f.Loads.Select(l => new LoadEntry(l.Value, l.Group, l.Dir)).ToList();
                    RefreshLists(); canvas.Invalidate();
                }
            }
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (nodes.Count == 0) return;

            float maxLineLoad = lineLoads.Any() && lineLoads.Values.Any(v => v.Any()) ? lineLoads.Values.SelectMany(v => v).Max(l => Math.Abs(l.Value)) : 1f;
            float maxNodeLoad = nodeLoads.Any() && nodeLoads.Values.Any(v => v.Any()) ? nodeLoads.Values.SelectMany(v => v).Max(l => Math.Abs(l.Value)) : 1f;
            if (maxLineLoad == 0) maxLineLoad = 1f; if (maxNodeLoad == 0) maxNodeLoad = 1f;

            for (int i = 0; i < lines.Count; i++)
            {
                var ln = lines[i];
                if (!nodes.ContainsKey(ln.A) || !nodes.ContainsKey(ln.B)) continue;
                var p1 = WorldToPixel(nodes[ln.A]); var p2 = WorldToPixel(nodes[ln.B]);

                bool isLineSelected = selectedLineIds.Contains(ln.Id);
                using (var pen = new Pen(isLineSelected ? Color.Red : Color.Black, isLineSelected ? 4f : 2f)) { pen.EndCap = System.Drawing.Drawing2D.LineCap.Round; pen.StartCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawLine(pen, p1, p2); }
                try
                {
                    var mid = new PointF((p1.X + p2.X) / 2f, (p1.Y + p2.Y) / 2f);
                    var idSz = g.MeasureString(ln.Id.ToString(), this.Font);
                    using (var bg = new SolidBrush(Color.FromArgb(200, Color.White))) { g.FillRectangle(bg, new RectangleF(mid.X - idSz.Width / 2f - 2f, mid.Y - idSz.Height / 2f - 1f, idSz.Width + 4f, idSz.Height + 2f)); }
                    g.DrawString(ln.Id.ToString(), this.Font, Brushes.Black, mid.X - idSz.Width / 2f, mid.Y - idSz.Height / 2f);

                    // --- YENİ: Çizgiye Atanan Kesit İsmini Ekrana Yazdır ---
                    if (!string.IsNullOrEmpty(ln.SectionName))
                    {
                        var secSz = g.MeasureString(ln.SectionName, this.Font);
                        float secY = mid.Y + idSz.Height / 2f + 2f; // ID yazısının altına yerleştir
                        using (var bg = new SolidBrush(Color.FromArgb(200, Color.LightYellow))) { g.FillRectangle(bg, new RectangleF(mid.X - secSz.Width / 2f - 2f, secY - 1f, secSz.Width + 4f, secSz.Height + 2f)); }
                        g.DrawString(ln.SectionName, this.Font, Brushes.DarkBlue, mid.X - secSz.Width / 2f, secY);
                    }
                    // --------------------------------------------------------
                }
                catch { }
            }

            foreach (var kv in lineLoads)
            {
                if (kv.Value == null || kv.Value.Count == 0) continue;
                var ln = lines.FirstOrDefault(x => x.Id == kv.Key);
                if (ln == null || !nodes.ContainsKey(ln.A) || !nodes.ContainsKey(ln.B)) continue;
                var a = WorldToPixel(nodes[ln.A]); var b = WorldToPixel(nodes[ln.B]);

                var p1_w = nodes[ln.A]; var p2_w = nodes[ln.B];
                float dx_w = p2_w.X - p1_w.X; float dy_w = p2_w.Y - p1_w.Y;
                float len_w = (float)Math.Sqrt(dx_w * dx_w + dy_w * dy_w);
                if (len_w <= 1e-6f) continue;

                float c_w = dx_w / len_w; float s_w = dy_w / len_w;

                var dir = new PointF(b.X - a.X, b.Y - a.Y);
                float lenDir = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                if (lenDir <= 1e-3f) continue;
                var ut = new PointF(dir.X / lenDir, dir.Y / lenDir);

                int count = Math.Max(5, Math.Min(200, (int)(len_w * 8f)));
                float startDist = lenDir * 0.05f; float endDist = lenDir * 0.95f;
                float step = (count > 1) ? (endDist - startDist) / (count - 1) : 0f;
                var sortedLineLoads = kv.Value.OrderBy(l => l.Group).ThenByDescending(l => Math.Abs(l.Value)).ToList();

                foreach (var ld in sortedLineLoads)
                {
                    float arrowLen = 15f + (Math.Abs(ld.Value) / maxLineLoad) * 35f;

                    PointF D = new PointF(0, 0);
                    switch (ld.Dir)
                    {
                        case LoadDirection.PlusX: D = new PointF(1, 0); break;
                        case LoadDirection.MinusX: D = new PointF(-1, 0); break;
                        case LoadDirection.PlusY: D = new PointF(0, -1); break;
                        case LoadDirection.MinusY: D = new PointF(0, 1); break;
                        case LoadDirection.LocalPlusX: D = new PointF(c_w, -s_w); break;
                        case LoadDirection.LocalMinusX: D = new PointF(-c_w, s_w); break;
                        case LoadDirection.LocalPlusY: D = new PointF(-s_w, -c_w); break;
                        case LoadDirection.LocalMinusY: D = new PointF(s_w, c_w); break;
                    }

                    Color baseColor = GetGroupColor(ld.Group);
                    Color transparentColor = Color.FromArgb(150, baseColor);

                    for (int i = 0; i < count; i++)
                    {
                        var pos = new PointF(a.X + ut.X * (startDist + step * i), a.Y + ut.Y * (startDist + step * i));
                        using (var pen = new Pen(transparentColor, 3f))
                        {
                            pen.EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor;
                            g.DrawLine(pen, new PointF(pos.X - D.X * arrowLen, pos.Y - D.Y * arrowLen), pos);
                        }
                    }
                }

                foreach (var dirGroup in sortedLineLoads.GroupBy(l => l.Dir))
                {
                    float maxArrowLen = 15f + (dirGroup.Max(l => Math.Abs(l.Value)) / maxLineLoad) * 35f;

                    PointF D = new PointF(0, 0);
                    switch (dirGroup.Key)
                    {
                        case LoadDirection.PlusX: D = new PointF(1, 0); break;
                        case LoadDirection.MinusX: D = new PointF(-1, 0); break;
                        case LoadDirection.PlusY: D = new PointF(0, -1); break;
                        case LoadDirection.MinusY: D = new PointF(0, 1); break;
                        case LoadDirection.LocalPlusX: D = new PointF(c_w, -s_w); break;
                        case LoadDirection.LocalMinusX: D = new PointF(-c_w, s_w); break;
                        case LoadDirection.LocalPlusY: D = new PointF(-s_w, -c_w); break;
                        case LoadDirection.LocalMinusY: D = new PointF(s_w, c_w); break;
                    }

                    var midLinePos = new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
                    var maxTail = new PointF(midLinePos.X - D.X * maxArrowLen, midLinePos.Y - D.Y * maxArrowLen);

                    float outwardX = -D.X; float outwardY = -D.Y; float stackStepY = (outwardY < -0.1f) ? -1f : 1f;
                    int labelOffset = 0;
                    foreach (var ld in dirGroup)
                    {
                        string lblText = $"{ld.Group}: {ld.Value.ToString(CultureInfo.CurrentCulture)} {LineLoadUnit}";
                        var tsize = g.MeasureString(lblText, this.Font);
                        float startX = maxTail.X + outwardX * 10f; float startY = maxTail.Y + outwardY * 10f;
                        if (outwardX < -0.1f) startX -= tsize.Width; else if (outwardX <= 0.1f) startX -= tsize.Width / 2f;
                        if (outwardY < -0.1f) startY -= tsize.Height; else if (outwardY <= 0.1f) startY -= tsize.Height / 2f;
                        float finalY = startY + (labelOffset * (tsize.Height + 2f) * stackStepY);
                        g.FillRectangle(Brushes.WhiteSmoke, new RectangleF(startX - 2f, finalY - 1f, tsize.Width + 4f, tsize.Height + 2f));
                        using (var bsh = new SolidBrush(GetGroupColor(ld.Group))) { g.DrawString(lblText, this.Font, bsh, new PointF(startX, finalY)); }
                        labelOffset++;
                    }
                }
            }

            foreach (var kv in nodes)
            {
                var id = kv.Key; var p = WorldToPixel(kv.Value);
                var rect = new RectangleF(p.X - NodeRadius, p.Y - NodeRadius, NodeRadius * 2, NodeRadius * 2);
                bool isSelected = selectedNodeIds.Contains(id);
                using (var brush = new SolidBrush(isSelected ? Color.Yellow : Color.Orange)) g.FillEllipse(brush, rect);
                using (var pen = new Pen(isSelected ? Color.Red : Color.DarkBlue, isSelected ? 2f : 1f)) g.DrawEllipse(pen, rect);
                var sz = g.MeasureString(id.ToString(), this.Font);
                g.DrawString(id.ToString(), this.Font, Brushes.Black, p.X - sz.Width / 2, p.Y - sz.Height / 2 - NodeRadius - 2);

                if (supports.TryGetValue(id, out SupportType st) && st != SupportType.None)
                {
                    float apexY = p.Y + NodeRadius + 2f; float triH = 12f; float halfBase = 10f;
                    var pts = new PointF[] { new PointF(p.X, apexY), new PointF(p.X + halfBase, apexY + triH), new PointF(p.X - halfBase, apexY + triH) };
                    using (var brushSup = new SolidBrush(Color.Gray)) g.FillPolygon(brushSup, pts);
                    using (var penSup = new Pen(Color.Black, 1f)) g.DrawPolygon(penSup, pts);
                    if (st == SupportType.Roller) { using (var penLine = new Pen(Color.Black, 3f)) g.DrawLine(penLine, p.X - halfBase - 6f, apexY + triH + 6f, p.X + halfBase + 6f, apexY + triH + 6f); }
                    if (st == SupportType.Fixed)
                    {
                        using (var penLine = new Pen(Color.Black, 4f)) g.DrawLine(penLine, p.X - halfBase - 4f, apexY + triH + 2f, p.X + halfBase + 4f, apexY + triH + 2f);
                    }
                }
            }

            foreach (var kvLoad in nodeLoads)
            {
                if (kvLoad.Value == null || kvLoad.Value.Count == 0 || !nodes.ContainsKey(kvLoad.Key)) continue;
                var p = WorldToPixel(nodes[kvLoad.Key]);
                var sortedNodeLoads = kvLoad.Value.OrderBy(l => l.Group).ThenByDescending(l => Math.Abs(l.Value)).ToList();

                foreach (var le in sortedNodeLoads)
                {
                    float len = 15f + (Math.Abs(le.Value) / maxNodeLoad) * 35f;
                    PointF dirVec = le.Dir == LoadDirection.PlusX ? new PointF(1f, 0f) : le.Dir == LoadDirection.MinusX ? new PointF(-1f, 0f) : le.Dir == LoadDirection.PlusY ? new PointF(0f, -1f) : new PointF(0f, 1f);

                    Color baseColor = GetGroupColor(le.Group);
                    Color transparentColor = Color.FromArgb(150, baseColor);

                    using (var pen = new Pen(transparentColor, 5f))
                    {
                        pen.EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor;
                        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        g.DrawLine(pen,
                            new PointF(p.X - dirVec.X * (NodeRadius + 2f + len), p.Y - dirVec.Y * (NodeRadius + 2f + len)),
                            new PointF(p.X - dirVec.X * (NodeRadius + 2f), p.Y - dirVec.Y * (NodeRadius + 2f)));
                    }
                }

                foreach (var dirGroup in sortedNodeLoads.GroupBy(l => l.Dir))
                {
                    float maxArrowLen = 15f + (dirGroup.Max(l => Math.Abs(l.Value)) / maxNodeLoad) * 35f;
                    PointF dirVec = dirGroup.Key == LoadDirection.PlusX ? new PointF(1f, 0f) : dirGroup.Key == LoadDirection.MinusX ? new PointF(-1f, 0f) : dirGroup.Key == LoadDirection.PlusY ? new PointF(0f, -1f) : new PointF(0f, 1f);
                    var maxTail = new PointF(p.X - dirVec.X * (NodeRadius + 2f + maxArrowLen), p.Y - dirVec.Y * (NodeRadius + 2f + maxArrowLen));
                    float outwardX = -dirVec.X; float outwardY = -dirVec.Y; float stackStepY = (outwardY < -0.1f) ? -1f : 1f;

                    int labelOffset = 0;
                    foreach (var le in dirGroup)
                    {
                        string lblText = $"{le.Group}: {le.Value.ToString(CultureInfo.CurrentCulture)} {NodeLoadUnit}";
                        var tsize = g.MeasureString(lblText, this.Font);
                        float startX = maxTail.X + outwardX * 10f; float startY = maxTail.Y + outwardY * 10f;
                        if (outwardX < -0.1f) startX -= tsize.Width; else if (outwardX <= 0.1f) startX -= tsize.Width / 2f;
                        if (outwardY < -0.1f) startY -= tsize.Height; else if (outwardY <= 0.1f) startY -= tsize.Height / 2f;
                        float finalY = startY + (labelOffset * (tsize.Height + 2f) * stackStepY);
                        g.FillRectangle(Brushes.WhiteSmoke, new RectangleF(startX - 2f, finalY - 1f, tsize.Width + 4f, tsize.Height + 2f));
                        using (var bsh = new SolidBrush(GetGroupColor(le.Group))) { g.DrawString(lblText, this.Font, bsh, new PointF(startX, finalY)); }
                        labelOffset++;
                    }
                }
            }

            if (isBoxSelecting)
            {
                Rectangle rect = new Rectangle(Math.Min(boxStartPoint.X, boxCurrentPoint.X), Math.Min(boxStartPoint.Y, boxCurrentPoint.Y), Math.Abs(boxStartPoint.X - boxCurrentPoint.X), Math.Abs(boxStartPoint.Y - boxCurrentPoint.Y));
                bool isCrossing = boxCurrentPoint.X < boxStartPoint.X;
                using (var brush = new SolidBrush(isCrossing ? Color.FromArgb(50, Color.Green) : Color.FromArgb(50, Color.Blue))) g.FillRectangle(brush, rect);
                using (var pen = new Pen(isCrossing ? Color.Green : Color.Blue, 1f)) { if (isCrossing) pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash; g.DrawRectangle(pen, rect); }
            }

            var hCanvas = canvas.ClientSize.Height;
            PointF originHUD = new PointF(40, hCanvas - 40);
            PointF xEnd = new PointF(90, hCanvas - 40);
            PointF yEnd = new PointF(40, hCanvas - 90);

            using (var pX = new Pen(Color.Red, 2f))
            {
                pX.CustomEndCap = new System.Drawing.Drawing2D.AdjustableArrowCap(4, 4);
                g.DrawLine(pX, originHUD, xEnd);
            }
            using (var pY = new Pen(Color.Green, 2f))
            {
                pY.CustomEndCap = new System.Drawing.Drawing2D.AdjustableArrowCap(4, 4);
                g.DrawLine(pY, originHUD, yEnd);
            }
            g.DrawString("X", this.Font, Brushes.Red, xEnd.X + 2, xEnd.Y - 8);
            g.DrawString("Y", this.Font, Brushes.Green, yEnd.X - 6, yEnd.Y - 20);
        }
    }
}