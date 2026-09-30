using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace FreeBodySolver
{
    public class NodeLineForm : Form
    {
        private PictureBox canvas;
        private Panel topPanel;
        private Label lblNodeId, lblNodeX, lblLineA, lblLineB;
        private TextBox txtNodeId, txtNodeX, txtLineA, txtLineB;
        private Button btnAddNode, btnAddLine, btnDeleteNode, btnDeleteLine, btnClear, btnZoomExtent, btnAddLoad;
        // Keep load direction enum to record orientation of loads on nodes.
        private enum LoadDirection { PlusX, MinusX, PlusY, MinusY }
        // Load entry container: value, group name and direction.
        private class LoadEntry { public float Value; public string Group; public LoadDirection Dir; public LoadEntry(float v, string g, LoadDirection d){Value=v;Group=g;Dir=d;} }
        private Dictionary<int, List<LoadEntry>> nodeLoads = new Dictionary<int, List<LoadEntry>>();
        // line loads and selection helpers
        private Dictionary<int, List<LoadEntry>> lineLoads = new Dictionary<int, List<LoadEntry>>();
        private HashSet<int> selectedNodeIds = new HashSet<int>();
        private HashSet<int> selectedLineIds = new HashSet<int>();
        private Button btnAddLoadLine;
        private ListBox lstNodes, lstLines;

        private const float NodeRadius = 6f;

        // store nodes as ID -> world coordinate (X,Y)
        private Dictionary<int, PointF> nodes = new Dictionary<int, PointF>();
        private class Line { public int Id; public int A; public int B; public Line(int id,int a,int b){Id=id;A=a;B=b;} }
        private List<Line> lines = new List<Line>();
        private int nextLineId = 1;
        private enum SupportType { None, Pin, Sliding }
        private Dictionary<int, SupportType> supports = new Dictionary<int, SupportType>();
        private int selectedLineIndex = -1;
        private ContextMenuStrip lineContextMenu;
        private int selectedNodeId = -1;
        private ContextMenuStrip nodeContextMenu;
        private bool addLineMode = false;
        private int addLineFirstNode = -1;
        private Button btnAddLineBySelect;
        private Label statusLabel;
        private float zoom = 1f;
        private PointF pan = new PointF(0, 0);
        private bool isPanning = false;
        private Point lastPanPoint;

        public NodeLineForm()
        {
            InitializeComponents();
        }


        private void BtnAddLineBySelect_Click(object sender, EventArgs e)
        {
            addLineMode = !addLineMode;
            addLineFirstNode = -1;
            if (addLineMode)
            {
                btnAddLineBySelect.Text = "Cancel Add Line";
                statusLabel.Text = "Add line by selecting two nodes.";
            }
            else
            {
                btnAddLineBySelect.Text = "Add Line (select)";
                statusLabel.Text = string.Empty;
            }
            selectedNodeId = -1;
            selectedLineIndex = -1;
            canvas.Invalidate();
        }

        private void ZoomExtent_Click(object sender, EventArgs e)
        {
            if (nodes.Count == 0) return;
            // reset pan and zoom to fit all nodes
            var w = canvas.ClientSize.Width;
            var h = canvas.ClientSize.Height;
            float margin = 30f;
            float minX = nodes.Values.Min(v => v.X);
            float maxX = nodes.Values.Max(v => v.X);
            float minY = nodes.Values.Min(v => v.Y);
            float maxY = nodes.Values.Max(v => v.Y);
            if (Math.Abs(maxX - minX) < 1e-6f) { minX -= 1; maxX += 1; }
            if (Math.Abs(maxY - minY) < 1e-6f) { minY -= 1; maxY += 1; }

            // compute target zoom to fit bounding box (with some margin) and clamp it
            float sx = (w - 2 * margin) / (maxX - minX);
            float sy = (h - 2 * margin) / (maxY - minY);
            float targetZoom = Math.Min(sx, sy) * 0.9f; // leave 10% breathing room
            // clamp targetZoom to reasonable range
            targetZoom = Math.Max(0.05f, Math.Min(targetZoom, 10f));
            zoom = targetZoom;
            // center world bbox in view: compute pixel of world center with zero pan then set pan so it maps to view center
            var worldCenter = new PointF((minX + maxX) / 2f, (minY + maxY) / 2f);
            var viewCenter = new PointF(w / 2f, h / 2f);
            var oldPan = pan;
            pan = new PointF(0, 0);
            var px = WorldToPixel(worldCenter, w, h, margin, minX, maxX, minY, maxY);
            pan = new PointF(viewCenter.X - px.X, viewCenter.Y - px.Y);
            canvas.Invalidate();
        }

        private void InitializeComponents()
        {
            this.Text = "Nodes & Lines Editor";
            this.ClientSize = new Size(900, 600);

            topPanel = new Panel { Dock = DockStyle.Top, Height = 110 };

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

            // support controls
            var lblSupportFor = new Label { Text = "Support Node:", Location = new Point(8, 36), AutoSize = true };
            var txtSupportNode = new TextBox { Name = "txtSupportNode", Location = new Point(90, 34), Width = 60 };
            var cmbSupportType = new ComboBox { Name = "cmbSupportType", Location = new Point(160, 34), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbSupportType.Items.AddRange(new object[] { "Pin", "Sliding" });
            cmbSupportType.SelectedIndex = 0;
            var btnAddSupport = new Button { Text = "Add Support", Location = new Point(270, 32), Width = 100 };
            btnAddSupport.Click += (s, e) => BtnAddSupport_Click(s, e, txtSupportNode, cmbSupportType);
            var btnDeleteSupport = new Button { Text = "Delete Support", Location = new Point(380, 32), Width = 100 };
            btnDeleteSupport.Click += BtnDeleteSupport_Click;

            btnDeleteNode = new Button { Text = "Delete Node", Location = new Point(250, 36), Width = 90 };
            btnDeleteNode.Click += BtnDeleteNode_Click;
            btnDeleteLine = new Button { Text = "Delete Line", Location = new Point(350, 36), Width = 90 };
            btnDeleteLine.Click += BtnDeleteLine_Click;

            btnClear = new Button { Text = "Clear All", Location = new Point(600, 36), Width = 90 };
            btnClear.Click += (s, e) => { nodes.Clear(); lines.Clear(); RefreshLists(); canvas.Invalidate(); };
            btnZoomExtent = new Button { Text = "Zoom Extent", Location = new Point(700, 36), Width = 90 };
            btnZoomExtent.Click += ZoomExtent_Click;

            // arrange top controls in three centered rows: Node inputs, Line inputs, Support controls
            var topTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(6),
                AutoSize = false
            };
            topTable.RowStyles.Add(new RowStyle(SizeType.Percent, 33f));
            topTable.RowStyles.Add(new RowStyle(SizeType.Percent, 33f));
            topTable.RowStyles.Add(new RowStyle(SizeType.Percent, 34f));

            var row1 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None };
            row1.Controls.AddRange(new Control[] { lblNodeId, txtNodeId, lblNodeX, txtNodeX, btnAddNode, btnAddLoad });

            var row2 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None };
            // add a button to add loads to selected lines
            btnAddLoadLine = new Button { Text = "Add Line Load", Width = 110 };
            btnAddLoadLine.Click += BtnAddLoadLine_Click;
            row2.Controls.AddRange(new Control[] { lblLineA, txtLineA, lblLineB, txtLineB, btnAddLine, btnAddLineBySelect, btnAddLoadLine });

            var row3 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None };
            row3.Controls.AddRange(new Control[] { lblSupportFor, txtSupportNode, cmbSupportType, btnAddSupport, btnDeleteSupport, btnDeleteNode, btnDeleteLine, btnClear, btnZoomExtent });

            // center the rows inside the table by placing them in single-cell panels
            var p1 = new Panel { Dock = DockStyle.Fill }; p1.Controls.Add(row1); row1.Location = new Point((p1.ClientSize.Width - row1.PreferredSize.Width) / 2, (p1.ClientSize.Height - row1.PreferredSize.Height) / 2); row1.Anchor = AnchorStyles.None;
            var p2 = new Panel { Dock = DockStyle.Fill }; p2.Controls.Add(row2); row2.Location = new Point((p2.ClientSize.Width - row2.PreferredSize.Width) / 2, (p2.ClientSize.Height - row2.PreferredSize.Height) / 2); row2.Anchor = AnchorStyles.None;
            var p3 = new Panel { Dock = DockStyle.Fill }; p3.Controls.Add(row3); row3.Location = new Point((p3.ClientSize.Width - row3.PreferredSize.Width) / 2, (p3.ClientSize.Height - row3.PreferredSize.Height) / 2); row3.Anchor = AnchorStyles.None;

            topTable.Controls.Add(p1, 0, 0);
            topTable.Controls.Add(p2, 0, 1);
            topTable.Controls.Add(p3, 0, 2);

            topPanel.Controls.Add(topTable);

            // initialize node loads dict for existing nodes (empty)
            nodeLoads = new Dictionary<int, List<LoadEntry>>();

            canvas = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White };
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.MouseEnter += (s, e) => { canvas.Focus(); };
            canvas.MouseWheel += Canvas_MouseWheel;

            // enable mouse wheel on PictureBox
            canvas.Focus();

            statusLabel = new Label { Dock = DockStyle.Bottom, Height = 22, Text = string.Empty, TextAlign = ContentAlignment.MiddleLeft };

            lstNodes = new ListBox { Dock = DockStyle.Right, Width = 160 }; // show nodes
            lstNodes.SelectionMode = SelectionMode.MultiExtended;
            lstNodes.SelectedIndexChanged += (s, e) =>
            {
                selectedNodeIds.Clear();
                foreach (var it in lstNodes.SelectedItems)
                {
                    var line = it.ToString();
                    if (line == null) continue;
                    var idPart = line.Split(':')[0];
                    if (int.TryParse(idPart, out int nid)) selectedNodeIds.Add(nid);
                }
                // set selectedNodeId to last selected if any
                if (selectedNodeIds.Count > 0) selectedNodeId = selectedNodeIds.Last();
                else selectedNodeId = -1;
                canvas.Invalidate();
            };
            lstLines = new ListBox { Dock = DockStyle.Right, Width = 160 }; // show lines
            lstLines.SelectionMode = SelectionMode.MultiExtended;
            lstLines.SelectedIndexChanged += (s, e) =>
            {
                selectedLineIds.Clear();
                foreach (var it in lstLines.SelectedItems)
                {
                    var line = it.ToString();
                    if (line == null) continue;
                    var idPart = line.Split(':')[0];
                    if (int.TryParse(idPart, out int lid)) selectedLineIds.Add(lid);
                }
                canvas.Invalidate();
            };

            // order: add right lists first so last added is rightmost
            this.Controls.Add(statusLabel);
            this.Controls.Add(canvas);
            this.Controls.Add(lstLines);
            this.Controls.Add(lstNodes);
            this.Controls.Add(topPanel);
            // No-op reorder confirm: preserve the above control addition order for stable layout.

            this.Resize += (s, e) => canvas.Invalidate();
        }

        private void BtnAddNode_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(txtNodeId.Text.Trim(), out int id))
            {
                MessageBox.Show("Geçerli bir Node ID girin.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var txtX = txtNodeX.Text.Trim();
            bool parsedX = float.TryParse(txtX, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out float x)
                || float.TryParse(txtX, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out x);
            if (!parsedX)
            {
                MessageBox.Show("Geçerli bir X koordinatı girin (ör. 12 veya 12.5 veya 12,5).", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (nodes.ContainsKey(id))
            {
                MessageBox.Show("Bu ID zaten var.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var newNodePoint = new PointF(x, 0f);
            // check if a node already exists at this coordinate (within small tolerance)
            float coordTol = 1e-6f;
            foreach (var kv in nodes)
            {
                if (Distance(kv.Value, newNodePoint) <= coordTol)
                {
                    MessageBox.Show("O noktada bir node var.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            nodes[id] = newNodePoint;
            RefreshLists();
            canvas.Invalidate();

            // after adding node, check if it lies on any existing line; if so, split that line
            if (lines.Count > 0)
            {
                float minX = nodes.Values.Min(v => v.X);
                float maxX = nodes.Values.Max(v => v.X);
                float minY = nodes.Values.Min(v => v.Y);
                float maxY = nodes.Values.Max(v => v.Y);
                float range = Math.Max(maxX - minX, maxY - minY);
                float tolWorld = (range <= 0) ? 0.1f : range * 0.005f; // tolerance in world units

                int splitIndex = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    var ln = lines[i];
                    if (!nodes.ContainsKey(ln.A) || !nodes.ContainsKey(ln.B)) continue;
                    var aPt = nodes[ln.A];
                    var bPt = nodes[ln.B];
                    float dist = DistancePointToSegment(nodes[id], aPt, bPt);
                    if (dist <= tolWorld)
                    {
                        splitIndex = i;
                        break;
                    }
                }

                if (splitIndex >= 0)
                {
                    var orig = lines[splitIndex];
                    var msg = $"Node {id} çizgi {orig.Id} üzerinde görünüyor. Çizgiyi bölmek istiyor musunuz?";
                    var dr = MessageBox.Show(msg, "Bölme onayı", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dr == DialogResult.Yes)
                    {
                        // replace orig with orig.A - newNode and create new line newNode - orig.B
                        lines.RemoveAt(splitIndex);
                        // keep original ID for first segment
                        lines.Insert(splitIndex, new Line(orig.Id, orig.A, id));
                        lines.Insert(splitIndex + 1, new Line(nextLineId++, id, orig.B));
                        statusLabel.Text = $"Line {orig.Id} split by node {id}, new line {nextLineId - 1} created.";
                        RefreshLists();
                        canvas.Invalidate();
                    }
                    else
                    {
                        statusLabel.Text = "Bölme iptal edildi.";
                    }
                }
            }
        }

        private void BtnAddLine_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(txtLineA.Text.Trim(), out int a) || !int.TryParse(txtLineB.Text.Trim(), out int b))
            {
                MessageBox.Show("Geçerli iki node ID girin.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (a == b)
            {
                MessageBox.Show("Başlangıç ve bitiş farklı olmalı.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!nodes.ContainsKey(a) || !nodes.ContainsKey(b))
            {
                MessageBox.Show("Belirtilen node ID bulunamadı.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (lines.Any(l => (l.A == a && l.B == b) || (l.A == b && l.B == a)))
            {
                MessageBox.Show("Bu çizgi zaten var.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            lines.Add(new Line(nextLineId++, a, b));
            RefreshLists();
            canvas.Invalidate();
        }

        private void BtnDeleteNode_Click(object sender, EventArgs e)
        {
            if (lstNodes.SelectedItem == null) return;
            var s = lstNodes.SelectedItem.ToString();
            var id = int.Parse(s.Split(':')[0]);
            // remove lines referencing
            lines.RemoveAll(t => t.A == id || t.B == id);
            nodes.Remove(id);
            supports.Remove(id);
            RefreshLists();
            canvas.Invalidate();
        }

        private void BtnDeleteLine_Click(object sender, EventArgs e)
        {
            if (lstLines.SelectedItem == null) return;
            var s = lstLines.SelectedItem.ToString();
            // expected format: "ID: A - B"
            var idPart = s.Split(':')[0].Trim();
            if (int.TryParse(idPart, out int lid))
            {
                lines.RemoveAll(l => l.Id == lid);
                RefreshLists();
                canvas.Invalidate();
            }

        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPanning)
            {
                var dx = e.Location.X - lastPanPoint.X;
                var dy = e.Location.Y - lastPanPoint.Y;
                pan = new PointF(pan.X + dx, pan.Y + dy);
                lastPanPoint = e.Location;
                canvas.Invalidate();
                return;
            }
            // Existing behavior: change cursor when hovering over nodes or lines.
            var hitNode = FindNodeAt(e.Location);
            var hitLine = FindLineAt(e.Location);
            if (hitNode >= 0) canvas.Cursor = Cursors.Hand;
            else if (hitLine >= 0) canvas.Cursor = Cursors.Default;
            else canvas.Cursor = Cursors.Cross;
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (isPanning && e.Button == MouseButtons.Middle)
            {
                isPanning = false;
                canvas.Cursor = Cursors.Default;
            }
        }

        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            if (nodes.Count == 0) return;
            var w = canvas.ClientSize.Width;
            var h = canvas.ClientSize.Height;
            float margin = 30f;
            float minX = nodes.Values.Min(v => v.X);
            float maxX = nodes.Values.Max(v => v.X);
            float minY = nodes.Values.Min(v => v.Y);
            float maxY = nodes.Values.Max(v => v.Y);
            if (Math.Abs(maxX - minX) < 1e-6f) { minX -= 1; maxX += 1; }
            if (Math.Abs(maxY - minY) < 1e-6f) { minY -= 1; maxY += 1; }

            var mousePt = e.Location;
            // world point under mouse before zoom
            var worldBefore = PixelToWorld(mousePt, w, h, margin, minX, maxX, minY, maxY);
            float factor = (e.Delta > 0) ? 1.15f : (1f / 1.15f);
            zoom *= factor;
            // clamp zoom
            zoom = Math.Max(0.1f, Math.Min(zoom, 20f));

            // compute new pixel of the same world point and adjust pan so the point stays under mouse
            var newPixel = WorldToPixel(worldBefore, w, h, margin, minX, maxX, minY, maxY);
            pan = new PointF(pan.X + (mousePt.X - newPixel.X), pan.Y + (mousePt.Y - newPixel.Y));
            canvas.Invalidate();
        }

        private void BtnAddSupport_Click(object sender, EventArgs e, TextBox txtSupportNode, ComboBox cmbSupportType)
        {
            if (!int.TryParse(txtSupportNode.Text.Trim(), out int id))
            {
                MessageBox.Show("Geçerli bir Node ID girin.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!nodes.ContainsKey(id))
            {
                MessageBox.Show("Belirtilen node ID bulunamadı.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var typeStr = cmbSupportType.SelectedItem?.ToString();
            SupportType st = SupportType.Pin;
            if (string.Equals(typeStr, "Sliding", StringComparison.OrdinalIgnoreCase)) st = SupportType.Sliding;
            supports[id] = st;
            RefreshLists();
            canvas.Invalidate();
        }

        private void BtnDeleteSupport_Click(object sender, EventArgs e)
        {
            if (lstNodes.SelectedItem == null) return;
            var s = lstNodes.SelectedItem.ToString();
            var id = int.Parse(s.Split(':')[0]);
            if (supports.ContainsKey(id)) supports.Remove(id);
            RefreshLists();
            canvas.Invalidate();
        }

        private void BtnAddLoad_Click(object sender, EventArgs e)
        {
            if (selectedNodeId < 0)
            {
                MessageBox.Show("Önce bir node seçin.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // open dialog to enter load value and select group
            using (var f = new Form())
            {
                f.Text = $"Add Load - Node {selectedNodeId}";
                f.ClientSize = new Size(320, 140);
                var lblVal = new Label { Text = "Yük (value):", Location = new Point(8, 12), AutoSize = true };
                var txtVal = new TextBox { Location = new Point(100, 10), Width = 200 };
                var lblGroup = new Label { Text = "Group:", Location = new Point(8, 44), AutoSize = true };
                var cmbGroup = new ComboBox { Location = new Point(100, 42), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
                cmbGroup.Items.AddRange(new object[] { "Dead Load", "Live Load", "WindLoadPressure", "WindLoadSuction", "Snow Load" });
                cmbGroup.SelectedIndex = 0;
                var lblDir = new Label { Text = "Direction:", Location = new Point(8, 74), AutoSize = true };
                var cmbDir = new ComboBox { Location = new Point(100, 72), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
                cmbDir.Items.AddRange(new object[] { "+X", "-X", "+Y", "-Y" });
                cmbDir.SelectedIndex = 2; // default +Y
                var btnOk = new Button { Text = "OK", Location = new Point(100, 80), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Cancel", Location = new Point(200, 80), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lblVal, txtVal, lblGroup, cmbGroup, lblDir, cmbDir, btnOk, btnCancel });
                f.AcceptButton = btnOk; f.CancelButton = btnCancel;
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    var sVal = txtVal.Text.Trim();
                    bool parsed = float.TryParse(sVal, out float v) || float.TryParse(sVal, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                    if (!parsed)
                    {
                        MessageBox.Show("Geçerli sayı girin.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    var grp = cmbGroup.SelectedItem.ToString();
                    var dir = LoadDirection.PlusY;
                    switch (cmbDir.SelectedItem?.ToString())
                    {
                        case "+X": dir = LoadDirection.PlusX; break;
                        case "-X": dir = LoadDirection.MinusX; break;
                        case "+Y": dir = LoadDirection.PlusY; break;
                        case "-Y": dir = LoadDirection.MinusY; break;
                    }
                    if (!nodeLoads.ContainsKey(selectedNodeId)) nodeLoads[selectedNodeId] = new List<LoadEntry>();
                    // add same load to all currently selected nodes
                    var targets = selectedNodeIds.Count > 0 ? selectedNodeIds : new HashSet<int> { selectedNodeId };
                    foreach (var nid in targets)
                    {
                        if (!nodeLoads.ContainsKey(nid)) nodeLoads[nid] = new List<LoadEntry>();
                        nodeLoads[nid].Add(new LoadEntry(v, grp, dir));
                    }
                    statusLabel.Text = $"Load added to node {selectedNodeId}: {v} ({grp})";
                    RefreshLists();
                    canvas.Invalidate();
                }
            }
        }

        private void BtnAddLoadLine_Click(object sender, EventArgs e)
        {
            // determine selected lines from listbox selection (or selectedLineIndex)
            var selectedLines = new List<int>();
            foreach (var it in lstLines.SelectedItems)
            {
                var lineStr = it.ToString();
                if (string.IsNullOrEmpty(lineStr)) continue;
                var idPart = lineStr.Split(':')[0];
                if (int.TryParse(idPart, out int lid)) selectedLines.Add(lid);
            }
            if (selectedLines.Count == 0 && selectedLineIndex >= 0 && selectedLineIndex < lines.Count)
            {
                selectedLines.Add(lines[selectedLineIndex].Id);
            }
            if (selectedLines.Count == 0)
            {
                MessageBox.Show("Önce bir çizgi seçin.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var f = new Form())
            {
                f.Text = "Add Load - Lines";
                f.ClientSize = new Size(360, 160);
                var lblVal = new Label { Text = "Yük (value):", Location = new Point(8, 12), AutoSize = true };
                var txtVal = new TextBox { Location = new Point(120, 10), Width = 220 };
                var lblGroup = new Label { Text = "Group:", Location = new Point(8, 44), AutoSize = true };
                var cmbGroup = new ComboBox { Location = new Point(120, 42), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
                cmbGroup.Items.AddRange(new object[] { "Dead Load", "Live Load", "WindLoadPressure", "WindLoadSuction", "Snow Load" });
                cmbGroup.SelectedIndex = 0;
                var lblDir = new Label { Text = "Direction:", Location = new Point(8, 76), AutoSize = true };
                var cmbDir = new ComboBox { Location = new Point(120, 74), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
                cmbDir.Items.AddRange(new object[] { "+X", "-X", "+Y", "-Y" }); cmbDir.SelectedIndex = 2;
                var btnOk = new Button { Text = "OK", Location = new Point(120, 110), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Cancel", Location = new Point(220, 110), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lblVal, txtVal, lblGroup, cmbGroup, lblDir, cmbDir, btnOk, btnCancel });
                f.AcceptButton = btnOk; f.CancelButton = btnCancel;
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    var sVal = txtVal.Text.Trim();
                    bool parsed = float.TryParse(sVal, out float v) || float.TryParse(sVal, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                    if (!parsed)
                    {
                        MessageBox.Show("Geçerli sayı girin.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    var grp = cmbGroup.SelectedItem.ToString();
                    var dir = LoadDirection.PlusY;
                    switch (cmbDir.SelectedItem?.ToString())
                    {
                        case "+X": dir = LoadDirection.PlusX; break;
                        case "-X": dir = LoadDirection.MinusX; break;
                        case "+Y": dir = LoadDirection.PlusY; break;
                        case "-Y": dir = LoadDirection.MinusY; break;
                    }
                    foreach (var lid in selectedLines)
                    {
                        if (!lineLoads.ContainsKey(lid)) lineLoads[lid] = new List<LoadEntry>();
                        lineLoads[lid].Add(new LoadEntry(v, grp, dir));
                    }
                    statusLabel.Text = $"Load added to lines: {string.Join(",", selectedLines)}";
                    RefreshLists();
                    canvas.Invalidate();
                }
            }
        }

        private void RefreshLists()
        {
            lstNodes.Items.Clear();
            foreach (var kv in nodes.OrderBy(k => k.Key))
            {
                var sup = supports.ContainsKey(kv.Key) ? supports[kv.Key].ToString() : "";
                var add = string.IsNullOrEmpty(sup) ? "" : $" [{sup}]";
                lstNodes.Items.Add($"{kv.Key}: X={kv.Value.X.ToString(CultureInfo.CurrentCulture)}, Y={kv.Value.Y.ToString(CultureInfo.CurrentCulture)}{add}");
            }
            lstLines.Items.Clear();
            foreach (var ln in lines) lstLines.Items.Add($"{ln.Id}: {ln.A} - {ln.B}");
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            var pt = e.Location;
            // start panning with middle mouse
            if (e.Button == MouseButtons.Middle)
            {
                isPanning = true;
                lastPanPoint = e.Location;
                canvas.Cursor = Cursors.Hand;
                return; // don't process selection while starting pan
            }
            // prefer node hit over line hit
            int nodeHit = FindNodeAt(pt);
            int lineHit = FindLineAt(pt);

            // If add-line-by-select mode is active, handle selection of two nodes
            if (addLineMode)
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (nodeHit >= 0)
                    {
                        if (addLineFirstNode == -1)
                        {
                            addLineFirstNode = nodeHit;
                            selectedNodeId = nodeHit;
                        }
                        else
                        {
                            int a = addLineFirstNode;
                            int b = nodeHit;
                            if (a == b)
                            {
                                statusLabel.Text = "Başlangıç ve bitiş aynı olamaz.";
                            }
                            else if (lines.Any(t => (t.A == a && t.B == b) || (t.A == b && t.B == a)))
                            {
                                statusLabel.Text = "Bu çizgi zaten var.";
                            }
                            else
                            {
                                lines.Add(new Line(nextLineId++, a, b));
                                statusLabel.Text = $"Çizgi eklendi: {a} - {b}. Yeni çizgi eklemek için birinci düğümü seçin veya sağ tık ile bitirin.";
                            }
                            // stay in add-line mode: reset first node so user can add more lines
                            addLineFirstNode = -1;
                            selectedNodeId = -1;
                            RefreshLists();
                        }
                        canvas.Invalidate();
                    }
                }
                else if (e.Button == MouseButtons.Right)
                {
                    // cancel
                    addLineMode = false;
                    addLineFirstNode = -1;
                    btnAddLineBySelect.Text = "Add Line (select)";
                    selectedNodeId = -1;
                    statusLabel.Text = string.Empty;
                    canvas.Invalidate();
                }
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                // support multiple selection with Ctrl
                if (nodeHit >= 0)
                {
                    if ((ModifierKeys & Keys.Control) == Keys.Control)
                    {
                        // toggle selection
                        if (selectedNodeIds.Contains(nodeHit)) selectedNodeIds.Remove(nodeHit);
                        else selectedNodeIds.Add(nodeHit);
                        // set selectedNodeId to last clicked for dialogs
                        selectedNodeId = nodeHit;
                    }
                    else
                    {
                        selectedNodeIds.Clear();
                        selectedNodeIds.Add(nodeHit);
                        selectedNodeId = nodeHit;
                    }
                    selectedLineIndex = -1;
                }
                else
                {
                    // clicked empty space or on line: clear node multi-selection
                    selectedNodeIds.Clear();
                    selectedNodeId = -1;
                    selectedLineIndex = lineHit;
                }
                canvas.Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (nodeHit >= 0)
                {
                    selectedNodeId = nodeHit;
                    selectedLineIndex = -1;
                    EnsureNodeContextMenu();
                    nodeContextMenu.Show(canvas, e.Location);
                }
                else if (lineHit >= 0)
                {
                    selectedLineIndex = lineHit;
                    selectedNodeId = -1;
                    EnsureLineContextMenu();
                    lineContextMenu.Show(canvas, e.Location);
                }
                else
                {
                    selectedNodeId = -1;
                    selectedLineIndex = -1;
                }
                canvas.Invalidate();
            }
        }

        private void EnsureLineContextMenu()
        {
            if (lineContextMenu != null) return;
            lineContextMenu = new ContextMenuStrip();
            var miDelete = new ToolStripMenuItem("Delete Line");
            miDelete.Click += (s, e) =>
            {
                if (selectedLineIndex >= 0 && selectedLineIndex < lines.Count)
                {
                    lines.RemoveAt(selectedLineIndex);
                    selectedLineIndex = -1;
                    RefreshLists();
                    canvas.Invalidate();
                }
            };
            lineContextMenu.Items.Add(miDelete);
        }

        private void EnsureNodeContextMenu()
        {
            if (nodeContextMenu != null) return;
            nodeContextMenu = new ContextMenuStrip();
            var miSupport = new ToolStripMenuItem("Support");
            var miPin = new ToolStripMenuItem("Pin");
            var miSliding = new ToolStripMenuItem("Sliding");
            var miRemoveSupport = new ToolStripMenuItem("Sil");
            miPin.Click += (s, e) =>
            {
                if (selectedNodeId >= 0) { supports[selectedNodeId] = SupportType.Pin; RefreshLists(); canvas.Invalidate(); }
            };
            miSliding.Click += (s, e) =>
            {
                if (selectedNodeId >= 0) { supports[selectedNodeId] = SupportType.Sliding; RefreshLists(); canvas.Invalidate(); }
            };
            miRemoveSupport.Click += (s, e) =>
            {
                if (selectedNodeId >= 0 && supports.ContainsKey(selectedNodeId))
                {
                    supports.Remove(selectedNodeId);
                    RefreshLists();
                    canvas.Invalidate();
                }
            };
            miSupport.DropDownItems.Add(miPin);
            miSupport.DropDownItems.Add(miSliding);
            miSupport.DropDownItems.Add(new ToolStripSeparator());
            miSupport.DropDownItems.Add(miRemoveSupport);

            var miCoordinate = new ToolStripMenuItem("Coordinate");
            miCoordinate.Click += (s, e) =>
            {
                if (selectedNodeId >= 0) ShowCoordinateDialog(selectedNodeId);
            };

            var miLoadNode = new ToolStripMenuItem("Load");
            miLoadNode.Click += (s, e) =>
            {
                if (selectedNodeId >= 0)
                {
                    // make this node the selection and open load dialog
                    selectedNodeIds.Clear();
                    selectedNodeIds.Add(selectedNodeId);
                    BtnAddLoad_Click(s, EventArgs.Empty);
                }
            };

            var miDeleteNode = new ToolStripMenuItem("Delete Node");
            miDeleteNode.Click += (s, e) =>
            {
                if (selectedNodeId >= 0)
                {
                    int id = selectedNodeId;
                    lines.RemoveAll(t => t.A == id || t.B == id);
                    nodes.Remove(id);
                    supports.Remove(id);
                    selectedNodeId = -1;
                    RefreshLists();
                    canvas.Invalidate();
                }
            };

            nodeContextMenu.Items.Add(miSupport);
            nodeContextMenu.Items.Add(miLoadNode);
            nodeContextMenu.Items.Add(miCoordinate);
            nodeContextMenu.Items.Add(miDeleteNode);
        }

        private void ShowCoordinateDialog(int nodeId)
        {
            if (!nodes.ContainsKey(nodeId)) return;
            var cur = nodes[nodeId];
            using (var f = new Form())
            {
                f.Text = $"Edit Coordinate - {nodeId}";
                f.ClientSize = new Size(260, 110);
                var lblX = new Label { Text = "X:", Location = new Point(8, 10), AutoSize = true };
                var txtX = new TextBox { Location = new Point(40, 8), Width = 200, Text = cur.X.ToString(CultureInfo.CurrentCulture) };
                var lblY = new Label { Text = "Y:", Location = new Point(8, 40), AutoSize = true };
                var txtY = new TextBox { Location = new Point(40, 38), Width = 200, Text = cur.Y.ToString(CultureInfo.CurrentCulture) };
                var btnOk = new Button { Text = "OK", Location = new Point(40, 70), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Cancel", Location = new Point(140, 70), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lblX, txtX, lblY, txtY, btnOk, btnCancel });
                f.AcceptButton = btnOk;
                f.CancelButton = btnCancel;
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    var sx = txtX.Text.Trim();
                    var sy = txtY.Text.Trim();
                    bool px = float.TryParse(sx, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out float nx)
                        || float.TryParse(sx, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out nx);
                    bool py = float.TryParse(sy, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out float ny)
                        || float.TryParse(sy, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out ny);
                    if (!px || !py)
                    {
                        MessageBox.Show("Geçerli sayılar girin.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    nodes[nodeId] = new PointF(nx, ny);
                    RefreshLists();
                    canvas.Invalidate();
                }
            }
        }

        private int FindLineAt(PointF p)
        {
            var w = canvas.ClientSize.Width;
            var h = canvas.ClientSize.Height;
            float margin = 30f;
            if (nodes.Count == 0) return -1;
            float minX = nodes.Values.Min(v => v.X);
            float maxX = nodes.Values.Max(v => v.X);
            float minY = nodes.Values.Min(v => v.Y);
            float maxY = nodes.Values.Max(v => v.Y);
            if (Math.Abs(maxX - minX) < 1e-6f) { minX -= 1; maxX += 1; }
            if (Math.Abs(maxY - minY) < 1e-6f) { minY -= 1; maxY += 1; }
            Func<PointF, PointF> toPixel = (world) => WorldToPixel(world, w, h, margin, minX, maxX, minY, maxY);

            float tol = 6f;
            for (int i = 0; i < lines.Count; i++)
            {
                var ln = lines[i];
                if (!nodes.ContainsKey(ln.A) || !nodes.ContainsKey(ln.B)) continue;
                var p1 = toPixel(nodes[ln.A]);
                var p2 = toPixel(nodes[ln.B]);
                float dist = DistancePointToSegment(p, p1, p2);
                if (dist <= tol) return i;
            }
            return -1;
        }

        private int FindNodeAt(PointF p)
        {
            if (nodes.Count == 0) return -1;
            var w = canvas.ClientSize.Width;
            var h = canvas.ClientSize.Height;
            float margin = 30f;
            float minX = nodes.Values.Min(v => v.X);
            float maxX = nodes.Values.Max(v => v.X);
            float minY = nodes.Values.Min(v => v.Y);
            float maxY = nodes.Values.Max(v => v.Y);
            if (Math.Abs(maxX - minX) < 1e-6f) { minX -= 1; maxX += 1; }
            if (Math.Abs(maxY - minY) < 1e-6f) { minY -= 1; maxY += 1; }
            Func<PointF, PointF> toPixel = (world) => WorldToPixel(world, w, h, margin, minX, maxX, minY, maxY);
            float tol = NodeRadius * 2f;
            foreach (var kv in nodes)
            {
                var id = kv.Key;
                var world = kv.Value;
                var pp = WorldToPixel(world, canvas.ClientSize.Width, canvas.ClientSize.Height, 30f, minX, maxX, minY, maxY);
                if (Distance(pp, p) <= tol) return id;
            }
            return -1;
        }

        private float DistancePointToSegment(PointF p, PointF a, PointF b)
        {
            float vx = b.X - a.X;
            float vy = b.Y - a.Y;
            float wx = p.X - a.X;
            float wy = p.Y - a.Y;
            float c1 = vx * wx + vy * wy;
            if (c1 <= 0) return Distance(p, a);
            float c2 = vx * vx + vy * vy;
            if (c2 <= c1) return Distance(p, b);
            float t = c1 / c2;
            var proj = new PointF(a.X + t * vx, a.Y + t * vy);
            return Distance(p, proj);
        }

        private float Distance(PointF p1, PointF p2)
        {
            float dx = p1.X - p2.X;
            float dy = p1.Y - p2.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private PointF WorldToPixel(PointF world, int w, int h, float margin, float minX, float maxX, float minY, float maxY)
        {
            float px = margin + (world.X - minX) / (maxX - minX) * (w - 2 * margin);
            // invert Y so larger world Y is up
            float py = margin + (maxY - world.Y) / (maxY - minY) * (h - 2 * margin);
            var basePt = new PointF(px, py);
            var viewCenter = new PointF(w / 2f, h / 2f);
            // apply zoom and pan
            var tx = new PointF((basePt.X - viewCenter.X) * zoom + viewCenter.X + pan.X,
                (basePt.Y - viewCenter.Y) * zoom + viewCenter.Y + pan.Y);
            return tx;
        }

        private PointF PixelToWorld(PointF pixel, int w, int h, float margin, float minX, float maxX, float minY, float maxY)
        {
            var viewCenter = new PointF(w / 2f, h / 2f);
            // undo pan and zoom
            var baseX = (pixel.X - pan.X - viewCenter.X) / zoom + viewCenter.X;
            var baseY = (pixel.Y - pan.Y - viewCenter.Y) / zoom + viewCenter.Y;
            // convert base pixel back to world
            float wx = minX + (baseX - margin) / (w - 2 * margin) * (maxX - minX);
            float wy = maxY - (baseY - margin) / (h - 2 * margin) * (maxY - minY);
            return new PointF(wx, wy);
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var w = canvas.ClientSize.Width;
            var h = canvas.ClientSize.Height;
            float margin = 30f;

            if (nodes.Count == 0) return;

            float minX = nodes.Values.Min(v => v.X);
            float maxX = nodes.Values.Max(v => v.X);
            float minY = nodes.Values.Min(v => v.Y);
            float maxY = nodes.Values.Max(v => v.Y);
            if (Math.Abs(maxX - minX) < 1e-6f) { minX -= 1; maxX += 1; }
            if (Math.Abs(maxY - minY) < 1e-6f) { minY -= 1; maxY += 1; }
            Func<PointF, PointF> toPixel = (world) => WorldToPixel(world, w, h, margin, minX, maxX, minY, maxY);

            // draw lines (highlight selected)
            for (int i = 0; i < lines.Count; i++)
            {
                var ln = lines[i];
                if (!nodes.ContainsKey(ln.A) || !nodes.ContainsKey(ln.B)) continue;
                var p1 = toPixel(nodes[ln.A]);
                var p2 = toPixel(nodes[ln.B]);
                bool isLineSelected = selectedLineIds.Contains(ln.Id) || (i == selectedLineIndex);
                Color col = isLineSelected ? Color.Red : Color.Black;
                float wth = isLineSelected ? 4f : 2f;
                using (var pen = new Pen(col, wth))
                {
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLine(pen, p1, p2);
                }
                // draw line ID at midpoint
                try
                {
                    var mid = new PointF((p1.X + p2.X) / 2f, (p1.Y + p2.Y) / 2f);
                    string idStr = ln.Id.ToString();
                    var idSz = g.MeasureString(idStr, this.Font);
                    // background for readability
                    using (var bg = new SolidBrush(Color.FromArgb(200, Color.White)))
                    {
                        var bgRect = new RectangleF(mid.X - idSz.Width / 2f - 2f, mid.Y - idSz.Height / 2f - 1f, idSz.Width + 4f, idSz.Height + 2f);
                        g.FillRectangle(bg, bgRect);
                    }
                    g.DrawString(idStr, this.Font, Brushes.Black, mid.X - idSz.Width / 2f, mid.Y - idSz.Height / 2f);
                }
                catch { }
            }

            // draw line loads as a series of arrows distributed along the entire line
            foreach (var kv in lineLoads)
            {
                int lid = kv.Key;
                var ln = lines.FirstOrDefault(x => x.Id == lid);
                if (ln == null) continue;
                if (!nodes.ContainsKey(ln.A) || !nodes.ContainsKey(ln.B)) continue;
                var a = toPixel(nodes[ln.A]);
                var b = toPixel(nodes[ln.B]);
                // tangent from A->B
                var dir = new PointF(b.X - a.X, b.Y - a.Y);
                float lenDir = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                if (lenDir <= 1e-3f) continue;
                var ut = new PointF(dir.X / lenDir, dir.Y / lenDir);
                // normal (perp) in screen coords
                var un = new PointF(-ut.Y, ut.X);

                // spacing in pixels between arrows
                float spacing = 40f;
                int count = Math.Max(1, (int)(lenDir / spacing));
                float step = lenDir / (count + 1);

                // compute maximum arrow length among loads to size the surrounding rectangle
                float maxArrowLen = 0f;
                foreach (var ldtemp in kv.Value) maxArrowLen = Math.Max(maxArrowLen, 20f + Math.Min(60f, Math.Abs(ldtemp.Value) * 4f));

                // draw thin rectangle surrounding the line load area; left/right match line endpoints
                var left = Math.Min(a.X, b.X);
                var right = Math.Max(a.X, b.X);
                var midY = (a.Y + b.Y) / 2f;
                float pad = maxArrowLen + 8f;
                var rect = new RectangleF(left, midY - pad, right - left, pad * 2f);
                try { using (var penRect = new Pen(Color.DarkRed, 1f)) { g.DrawRectangle(penRect, Rectangle.Round(rect)); } } catch { }

                foreach (var ld in kv.Value)
                {
                    float arrowLen = 20f + Math.Min(60f, Math.Abs(ld.Value) * 4f);
                    // compute axis-based unit for axis directions
                    PointF axis = new PointF(0, 0);
                    bool useAxis = true;
                    switch (ld.Dir)
                    {
                        case LoadDirection.PlusX: axis = new PointF(1, 0); break;
                        case LoadDirection.MinusX: axis = new PointF(-1, 0); break;
                        case LoadDirection.PlusY: axis = new PointF(0, -1); break; // screen Y up
                        case LoadDirection.MinusY: axis = new PointF(0, 1); break;
                        default: useAxis = false; break;
                    }

                    for (int i = 1; i <= count; i++)
                    {
                        var pos = new PointF(a.X + ut.X * (step * i), a.Y + ut.Y * (step * i));
                        // tip should touch the line at pos; tail is offset away so arrow points toward the line
                        PointF D;
                        if (useAxis)
                        {
                            // axis is in screen axes; we want arrow pointing toward the line (tip at pos),
                            // so take the opposite of axis as direction from tail->tip.
                            D = new PointF(-axis.X, -axis.Y);
                        }
                        else
                        {
                            // un is perpendicular to the line; use -un to point toward the line
                            D = new PointF(-un.X, -un.Y);
                        }
                        // normalize D
                        var dlen = (float)Math.Sqrt(D.X * D.X + D.Y * D.Y);
                        if (dlen <= 1e-6f) dlen = 1f;
                        D = new PointF(D.X / dlen, D.Y / dlen);
                        var tail = new PointF(pos.X - D.X * arrowLen, pos.Y - D.Y * arrowLen);
                        var tip = pos;
                        using (var pen = new Pen(Color.Red, 5f)) { pen.EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor; g.DrawLine(pen, tail, tip); }
                        try { var txt = ld.Value.ToString(CultureInfo.CurrentCulture); var tsize = g.MeasureString(txt, this.Font); var txtPos = new PointF(tail.X - D.X * 8f - tsize.Width / 2f, tail.Y - D.Y * 8f - tsize.Height / 2f); g.DrawString(txt, this.Font, Brushes.Red, txtPos); } catch { }
                    }
                }
            }

            // draw nodes
            foreach (var kv in nodes)
            {
                var id = kv.Key;
                var x = kv.Value;
                var p = toPixel(x);
                var rect = new RectangleF(p.X - NodeRadius, p.Y - NodeRadius, NodeRadius * 2, NodeRadius * 2);
                bool isSelected = selectedNodeIds.Contains(id) || id == selectedNodeId;
                using (var brush = new SolidBrush(isSelected ? Color.Yellow : Color.Orange)) g.FillEllipse(brush, rect);
                using (var pen = new Pen(isSelected ? Color.Red : Color.DarkBlue, isSelected ? 2f : 1f)) g.DrawEllipse(pen, rect);
                var str = id.ToString();
                var sz = g.MeasureString(str, this.Font);
                g.DrawString(str, this.Font, Brushes.Black, p.X - sz.Width / 2, p.Y - sz.Height / 2 - NodeRadius - 2);

                // draw support if present
                if (supports.TryGetValue(id, out SupportType st) && st != SupportType.None)
                {
                    float apexY = p.Y + NodeRadius + 2f; // apex touches node underside
                    float triH = 12f;
                    float halfBase = 10f;
                    PointF apex = new PointF(p.X, apexY);
                    PointF left = new PointF(p.X - halfBase, apexY + triH);
                    PointF right = new PointF(p.X + halfBase, apexY + triH);
                    var pts = new PointF[] { apex, right, left };
                    using (var brushSup = new SolidBrush(Color.Gray))
                        g.FillPolygon(brushSup, pts);
                    using (var penSup = new Pen(Color.Black, 1f))
                        g.DrawPolygon(penSup, pts);

                    if (st == SupportType.Sliding)
                    {
                        float yLine = apexY + triH + 6f;
                        using (var penLine = new Pen(Color.Black, 3f))
                        {
                            g.DrawLine(penLine, p.X - halfBase - 6f, yLine, p.X + halfBase + 6f, yLine);
                        }
                    }
                }
            }
            // draw loads on nodes (thick red arrows)
            foreach (var kvLoad in nodeLoads)
            {
                var nid = kvLoad.Key;
                if (!nodes.ContainsKey(nid)) continue;
                var p = toPixel(nodes[nid]);
                foreach (var le in kvLoad.Value)
                {
                    PointF dirVec;
                    switch (le.Dir)
                    {
                        case LoadDirection.PlusX: dirVec = new PointF(1f, 0f); break;
                        case LoadDirection.MinusX: dirVec = new PointF(-1f, 0f); break;
                        case LoadDirection.PlusY: dirVec = new PointF(0f, -1f); break;
                        case LoadDirection.MinusY: dirVec = new PointF(0f, 1f); break;
                        default: dirVec = new PointF(0f, -1f); break;
                    }
                    float offset = NodeRadius + 2f;
                    float len = 25f;
                    var start = new PointF(p.X + dirVec.X * offset, p.Y + dirVec.Y * offset);
                    var end = new PointF(p.X + dirVec.X * (offset + len), p.Y + dirVec.Y * (offset + len));
                    using (var pen = new Pen(Color.Red, 5f))
                    {
                        pen.EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor;
                        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        g.DrawLine(pen, start, end);
                    }
                }
            }
        }
    }
}
