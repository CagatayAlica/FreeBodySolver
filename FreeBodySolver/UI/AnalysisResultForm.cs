using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FreeBodySolver.Models;

namespace FreeBodySolver.UI
{
    public class AnalysisResultForm : Form
    {
        private DataGridView dgv;
        private ComboBox cmbGroups;
        private Dictionary<string, (double[] U, double[] R)> results;
        private List<int> nodeIds;
        private Dictionary<int, int> nodeMap;
        private Dictionary<int, SupportType> supports;

        private List<Line> lines;
        private Dictionary<int, PointF> nodes;
        private Dictionary<int, List<LoadEntry>> lineLoads;

        public AnalysisResultForm(List<int> nodeIds, Dictionary<int, int> nodeMap, Dictionary<string, (double[] U, double[] R)> results, Dictionary<int, SupportType> supports, List<Line> lines, Dictionary<int, PointF> nodes, Dictionary<int, List<LoadEntry>> lineLoads)
        {
            this.Text = "Sonlu Elemanlar Analizi (FEM) Sonuçları";
            this.Size = new Size(800, 450);
            this.StartPosition = FormStartPosition.CenterParent;

            this.results = results;
            this.nodeIds = nodeIds;
            this.nodeMap = nodeMap;
            this.supports = supports;
            this.lines = lines;
            this.nodes = nodes;
            this.lineLoads = lineLoads;

            Panel pnlTop = new Panel { Dock = DockStyle.Top, Height = 45 };
            Label lbl = new Label { Text = "İncelenecek Yük Grubu:", Location = new Point(10, 15), AutoSize = true, Font = new Font(this.Font, FontStyle.Bold) };
            cmbGroups = new ComboBox { Location = new Point(170, 12), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };

            foreach (var g in results.Keys) cmbGroups.Items.Add(g);
            if (cmbGroups.Items.Count > 0) cmbGroups.SelectedIndex = 0;

            cmbGroups.SelectedIndexChanged += (s, e) => UpdateTable();

            Button btnInternalForces = new Button { Text = "İç Kuvvetleri Göster", Location = new Point(340, 10), Width = 150, Height = 25, BackColor = Color.LightSkyBlue };
            btnInternalForces.Click += BtnInternalForces_Click;

            pnlTop.Controls.Add(lbl);
            pnlTop.Controls.Add(cmbGroups);
            pnlTop.Controls.Add(btnInternalForces);

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false
            };

            dgv.Columns.Add("Node", "Node ID");
            dgv.Columns.Add("UX", "UX Yer Değiştirme (m)");
            dgv.Columns.Add("UY", "UY Yer Değiştirme (m)");
            dgv.Columns.Add("RZ", "RZ Dönme (rad)");
            dgv.Columns.Add("RX", "X Tepkisi (kN)");
            dgv.Columns.Add("RY", "Y Tepkisi (kN)");
            dgv.Columns.Add("RM", "M Tepkisi (kNm)");

            this.Controls.Add(dgv);
            this.Controls.Add(pnlTop);

            UpdateTable();
        }

        private void UpdateTable()
        {
            if (cmbGroups.SelectedItem == null) return;
            string group = cmbGroups.SelectedItem.ToString();
            if (!results.ContainsKey(group)) return;

            var (U, R) = results[group];
            dgv.Rows.Clear();

            foreach (var nid in nodeIds)
            {
                int idx = nodeMap[nid];
                double ux = U[idx * 3];
                double uy = U[idx * 3 + 1];
                double rz = U[idx * 3 + 2];

                SupportType st = SupportType.None;
                if (supports.ContainsKey(nid)) st = supports[nid];

                string rx = (st == SupportType.Pin || st == SupportType.Fixed) ? R[idx * 3].ToString("0.###") : "-";
                string ry = (st == SupportType.Pin || st == SupportType.Roller || st == SupportType.Fixed) ? R[idx * 3 + 1].ToString("0.###") : "-";
                string rm = (st == SupportType.Fixed) ? R[idx * 3 + 2].ToString("0.###") : "-";

                dgv.Rows.Add(nid.ToString(), ux.ToString("0.######"), uy.ToString("0.######"), rz.ToString("0.######"), rx, ry, rm);
            }
        }

        private void BtnInternalForces_Click(object sender, EventArgs e)
        {
            if (cmbGroups.SelectedItem == null) return;
            using (var frm = new InternalForcesForm(lines, nodes, nodeMap, results, lineLoads, cmbGroups.SelectedItem.ToString()))
            {
                frm.ShowDialog(this);
            }
        }
    }
}
