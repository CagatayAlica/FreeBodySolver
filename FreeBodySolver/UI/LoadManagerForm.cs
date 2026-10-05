using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FreeBodySolver.Models;

namespace FreeBodySolver.UI
{
    public class LoadManagerForm : Form
    {
        public List<LoadEntry> Loads { get; private set; }
        private ListBox lstLoads;
        private TextBox txtVal;
        private ComboBox cmbGroup, cmbDir;

        public LoadManagerForm(List<LoadEntry> existingLoads, string title)
        {
            this.Text = title; this.ClientSize = new Size(420, 320); this.StartPosition = FormStartPosition.CenterParent;
            Loads = existingLoads == null ? new List<LoadEntry>() : existingLoads.Select(l => new LoadEntry(l.Value, l.Group, l.Dir)).ToList();
            lstLoads = new ListBox { Location = new Point(10, 10), Size = new Size(390, 120), SelectionMode = SelectionMode.MultiExtended };
            lstLoads.SelectedIndexChanged += (s, e) => {
                if (lstLoads.SelectedIndices.Count == 1)
                {
                    var ld = Loads[lstLoads.SelectedIndex]; txtVal.Text = ld.Value.ToString(CultureInfo.InvariantCulture);
                    cmbGroup.SelectedItem = ld.Group;
                    string selectedDir = "+Y";
                    switch (ld.Dir)
                    {
                        case LoadDirection.PlusX: selectedDir = "+X"; break;
                        case LoadDirection.MinusX: selectedDir = "-X"; break;
                        case LoadDirection.PlusY: selectedDir = "+Y"; break;
                        case LoadDirection.MinusY: selectedDir = "-Y"; break;
                        case LoadDirection.LocalPlusX: selectedDir = "Lokal +X"; break;
                        case LoadDirection.LocalMinusX: selectedDir = "Lokal -X"; break;
                        case LoadDirection.LocalPlusY: selectedDir = "Lokal +Y"; break;
                        case LoadDirection.LocalMinusY: selectedDir = "Lokal -Y"; break;
                    }
                    cmbDir.SelectedItem = selectedDir;
                }
                else if (lstLoads.SelectedIndices.Count > 1) txtVal.Clear();
            };

            var lblVal = new Label { Text = "Değer:", Location = new Point(10, 142), AutoSize = true };
            txtVal = new TextBox { Location = new Point(100, 140), Width = 110 };
            var lblGroup = new Label { Text = "Grup:", Location = new Point(10, 172), AutoSize = true };
            cmbGroup = new ComboBox { Location = new Point(100, 170), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbGroup.Items.AddRange(new object[] { "Dead Load", "Live Load", "WindLoadPressure", "WindLoadSuction", "Snow Load" });
            cmbGroup.SelectedIndex = 0;
            var lblDir = new Label { Text = "Yön:", Location = new Point(10, 202), AutoSize = true };
            cmbDir = new ComboBox { Location = new Point(100, 200), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbDir.Items.AddRange(new object[] { "+X", "-X", "+Y", "-Y" });
            if (title.Contains("Lines")) { cmbDir.Items.AddRange(new object[] { "Lokal +X", "Lokal -X", "Lokal +Y", "Lokal -Y" }); }
            cmbDir.SelectedIndex = 2;

            var btnAdd = new Button { Text = "Ekle", Location = new Point(230, 139), Width = 75 };
            btnAdd.Click += (s, e) => { if (TryParseLoad(out float v, out string g, out LoadDirection d)) { Loads.Add(new LoadEntry(v, g, d)); ConsolidateLoads(); RefreshList(); } };
            var btnUpdate = new Button { Text = "Güncelle", Location = new Point(315, 139), Width = 75 };
            btnUpdate.Click += (s, e) => { if (lstLoads.SelectedIndices.Count == 1 && TryParseLoad(out float v, out string g, out LoadDirection d)) { Loads.RemoveAt(lstLoads.SelectedIndex); Loads.Add(new LoadEntry(v, g, d)); ConsolidateLoads(); RefreshList(); } };
            var btnDelete = new Button { Text = "Sil", Location = new Point(230, 169), Width = 160 };
            btnDelete.Click += (s, e) => { if (lstLoads.SelectedIndices.Count > 0) { for (int i = lstLoads.SelectedIndices.Count - 1; i >= 0; i--) Loads.RemoveAt(lstLoads.SelectedIndices[i]); ConsolidateLoads(); RefreshList(); txtVal.Clear(); } };

            var btnOk = new Button { Text = "Tamam", Location = new Point(230, 270), Width = 75, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "İptal", Location = new Point(315, 270), Width = 75, DialogResult = DialogResult.Cancel };
            this.Controls.AddRange(new Control[] { lstLoads, lblVal, txtVal, lblGroup, cmbGroup, lblDir, cmbDir, btnAdd, btnUpdate, btnDelete, btnOk, btnCancel });
            this.AcceptButton = btnOk; this.CancelButton = btnCancel;
            RefreshList();
        }

        private void ConsolidateLoads()
        {
            var consolidated = new List<LoadEntry>();
            var groups = Loads.Select(l => l.Group).Distinct().OrderBy(g => g).ToList();
            foreach (var g in groups)
            {
                float sumX = 0, sumY = 0, sumLocX = 0, sumLocY = 0;
                foreach (var l in Loads.Where(x => x.Group == g))
                {
                    if (l.Dir == LoadDirection.PlusX) sumX += l.Value;
                    else if (l.Dir == LoadDirection.MinusX) sumX -= l.Value;
                    else if (l.Dir == LoadDirection.PlusY) sumY += l.Value;
                    else if (l.Dir == LoadDirection.MinusY) sumY -= l.Value;
                    else if (l.Dir == LoadDirection.LocalPlusX) sumLocX += l.Value;
                    else if (l.Dir == LoadDirection.LocalMinusX) sumLocX -= l.Value;
                    else if (l.Dir == LoadDirection.LocalPlusY) sumLocY += l.Value;
                    else if (l.Dir == LoadDirection.LocalMinusY) sumLocY -= l.Value;
                }
                if (Math.Abs(sumX) > 1e-4f) consolidated.Add(new LoadEntry(Math.Abs(sumX), g, sumX > 0 ? LoadDirection.PlusX : LoadDirection.MinusX));
                if (Math.Abs(sumY) > 1e-4f) consolidated.Add(new LoadEntry(Math.Abs(sumY), g, sumY > 0 ? LoadDirection.PlusY : LoadDirection.MinusY));
                if (Math.Abs(sumLocX) > 1e-4f) consolidated.Add(new LoadEntry(Math.Abs(sumLocX), g, sumLocX > 0 ? LoadDirection.LocalPlusX : LoadDirection.LocalMinusX));
                if (Math.Abs(sumLocY) > 1e-4f) consolidated.Add(new LoadEntry(Math.Abs(sumLocY), g, sumLocY > 0 ? LoadDirection.LocalPlusY : LoadDirection.LocalMinusY));
            }
            Loads = consolidated;
        }

        private void RefreshList()
        {
            var selectedIds = new List<int>(); foreach (int idx in lstLoads.SelectedIndices) selectedIds.Add(idx);
            lstLoads.Items.Clear();
            foreach (var ld in Loads)
            {
                string dirStr = "+Y";
                switch (ld.Dir)
                {
                    case LoadDirection.PlusX: dirStr = "+X"; break;
                    case LoadDirection.MinusX: dirStr = "-X"; break;
                    case LoadDirection.PlusY: dirStr = "+Y"; break;
                    case LoadDirection.MinusY: dirStr = "-Y"; break;
                    case LoadDirection.LocalPlusX: dirStr = "Lokal +X"; break;
                    case LoadDirection.LocalMinusX: dirStr = "Lokal -X"; break;
                    case LoadDirection.LocalPlusY: dirStr = "Lokal +Y"; break;
                    case LoadDirection.LocalMinusY: dirStr = "Lokal -Y"; break;
                }
                lstLoads.Items.Add($"Değer: {ld.Value} | Grup: {ld.Group} | Yön: {dirStr}");
            }
            foreach (int idx in selectedIds) { if (idx >= 0 && idx < lstLoads.Items.Count) lstLoads.SetSelected(idx, true); }
        }

        private bool TryParseLoad(out float v, out string g, out LoadDirection d)
        {
            v = 0; g = ""; d = LoadDirection.PlusY;
            if (!float.TryParse(txtVal.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            g = cmbGroup.SelectedItem?.ToString() ?? "Dead Load";
            string dirStr = cmbDir.SelectedItem?.ToString() ?? "+Y";
            d = dirStr == "+X" ? LoadDirection.PlusX : dirStr == "-X" ? LoadDirection.MinusX : dirStr == "+Y" ? LoadDirection.PlusY : dirStr == "-Y" ? LoadDirection.MinusY : dirStr == "Lokal +X" ? LoadDirection.LocalPlusX : dirStr == "Lokal -X" ? LoadDirection.LocalMinusX : dirStr == "Lokal +Y" ? LoadDirection.LocalPlusY : LoadDirection.LocalMinusY;
            return true;
        }
    }
}
