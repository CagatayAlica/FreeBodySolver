using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FreeBodySolver.Models;
using FreeBodySolver.Sections;
using FreeBodySolver.Core;

namespace FreeBodySolver.Solver
{
    public class FemSolver
    {
        // Varsayılan (Kesit atanmamış çizgiler için) Değerler
        public static double E = 2e8; // 200 GPa
        public static double A = 0.01;
        public static double I = 5e-5;

        public static Dictionary<string, (double[] U, double[] R)> Solve(
            Dictionary<int, PointF> nodes,
            List<Line> lines,
            Dictionary<int, SupportType> supports,
            Dictionary<int, List<LoadEntry>> nodeLoads,
            Dictionary<int, List<LoadEntry>> lineLoads)
        {
            var nodeIds = nodes.Keys.OrderBy(k => k).ToList();
            var nodeMap = new Dictionary<int, int>();
            for (int i = 0; i < nodeIds.Count; i++) nodeMap[nodeIds[i]] = i;

            int nNodes = nodeIds.Count;
            int nDof = nNodes * 3;

            double[,] K = new double[nDof, nDof];

            // --- YENİ: Kütüphanedeki kesitleri yükle ve özelliklerini hesaplayıp önbelleğe al ---
            var profiles = SectionDatabase.LoadProfiles();
            var profileProps = new Dictionary<string, (double A, double I)>();
            SectionGenerator generator = new SectionGenerator();

            foreach (var prof in profiles)
            {
                if (prof.ShapeType == "Lipped C Shape")
                    generator.GenerateLippedC(prof.A, prof.B, prof.C, prof.t, prof.R, 2, 4, 4, 3);
                else
                    generator.GenerateUShape(prof.A, prof.B, prof.t, prof.R, 4, 4, 3);

                // Birim dönüşümü: mm²'den m²'ye (1e-6), mm⁴'ten m⁴'e (1e-12)
                double area_m2 = generator.Area * 1e-6;
                double ixx_m4 = generator.Ixx * 1e-12;

                profileProps[prof.Name] = (area_m2, ixx_m4);
            }
            // --------------------------------------------------------------------------------

            foreach (var ln in lines)
            {
                var p1 = nodes[ln.A];
                var p2 = nodes[ln.B];

                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double L = Math.Sqrt(dx * dx + dy * dy);
                if (L < 1e-6) continue;

                double c = dx / L;
                double s = dy / L;

                // --- YENİ: Bu çizgiye atanmış kesitin değerlerini kullan ---
                double currentA = A;
                double currentI = I;

                if (!string.IsNullOrEmpty(ln.SectionName) && profileProps.ContainsKey(ln.SectionName))
                {
                    currentA = profileProps[ln.SectionName].A;
                    currentI = profileProps[ln.SectionName].I;
                }
                // -----------------------------------------------------------

                double[,] kLocal = new double[6, 6];
                double ea_l = E * currentA / L;
                double ei_l3 = 12 * E * currentI / (L * L * L);
                double ei_l2 = 6 * E * currentI / (L * L);
                double ei_l = 4 * E * currentI / L;
                double ei_l_2 = 2 * E * currentI / L;

                kLocal[0, 0] = ea_l; kLocal[0, 3] = -ea_l;
                kLocal[3, 0] = -ea_l; kLocal[3, 3] = ea_l;
                kLocal[1, 1] = ei_l3; kLocal[1, 2] = ei_l2; kLocal[1, 4] = -ei_l3; kLocal[1, 5] = ei_l2;
                kLocal[2, 1] = ei_l2; kLocal[2, 2] = ei_l; kLocal[2, 4] = -ei_l2; kLocal[2, 5] = ei_l_2;
                kLocal[4, 1] = -ei_l3; kLocal[4, 2] = -ei_l2; kLocal[4, 4] = ei_l3; kLocal[4, 5] = -ei_l2;
                kLocal[5, 1] = ei_l2; kLocal[5, 2] = ei_l_2; kLocal[5, 4] = -ei_l2; kLocal[5, 5] = ei_l;

                double[,] T = new double[6, 6];
                T[0, 0] = c; T[0, 1] = s;
                T[1, 0] = -s; T[1, 1] = c;
                T[2, 2] = 1;
                T[3, 3] = c; T[3, 4] = s;
                T[4, 3] = -s; T[4, 4] = c;
                T[5, 5] = 1;

                double[,] kG = MultiplyMatrix(Transpose(T), MultiplyMatrix(kLocal, T));
                int[] dofs = new int[] { nodeMap[ln.A] * 3, nodeMap[ln.A] * 3 + 1, nodeMap[ln.A] * 3 + 2, nodeMap[ln.B] * 3, nodeMap[ln.B] * 3 + 1, nodeMap[ln.B] * 3 + 2 };

                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 6; j++)
                        K[dofs[i], dofs[j]] += kG[i, j];
            }

            double[,] K_orig = new double[nDof, nDof];
            Array.Copy(K, K_orig, K.Length);

            Action<int> constrainDof = (dof) => {
                for (int i = 0; i < nDof; i++)
                {
                    K[dof, i] = 0.0;
                    K[i, dof] = 0.0;
                }
                K[dof, dof] = 1.0;
            };

            foreach (var kv in supports)
            {
                if (!nodeMap.ContainsKey(kv.Key)) continue;
                int idx = nodeMap[kv.Key];
                if (kv.Value == SupportType.Pin)
                {
                    constrainDof(idx * 3);
                    constrainDof(idx * 3 + 1);
                }
                else if (kv.Value == SupportType.Roller)
                {
                    constrainDof(idx * 3 + 1);
                }
                else if (kv.Value == SupportType.Fixed)
                {
                    constrainDof(idx * 3);
                    constrainDof(idx * 3 + 1);
                    constrainDof(idx * 3 + 2);
                }
            }

            var uniqueGroups = new HashSet<string>();
            foreach (var kv in nodeLoads) foreach (var ld in kv.Value) uniqueGroups.Add(ld.Group);
            foreach (var kv in lineLoads) foreach (var ld in kv.Value) uniqueGroups.Add(ld.Group);

            if (uniqueGroups.Count == 0)
                throw new InvalidOperationException("Sistemde çözülecek herhangi bir yük bulunamadı.");

            var resultsByGroup = new Dictionary<string, (double[] U, double[] R)>();

            foreach (var group in uniqueGroups)
            {
                double[] F = new double[nDof];

                foreach (var ln in lines)
                {
                    if (lineLoads.ContainsKey(ln.Id))
                    {
                        double W_X = 0, W_Y = 0, loc_x = 0, loc_y = 0;
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

                        if (Math.Abs(W_X) > 1e-6 || Math.Abs(W_Y) > 1e-6 || Math.Abs(loc_x) > 1e-6 || Math.Abs(loc_y) > 1e-6)
                        {
                            var p1 = nodes[ln.A]; var p2 = nodes[ln.B];
                            double L = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
                            double c = (p2.X - p1.X) / L;
                            double s = (p2.Y - p1.Y) / L;

                            double w_glob_x = W_X + (loc_x * c - loc_y * s);
                            double w_glob_y = W_Y + (loc_x * s + loc_y * c);
                            double w_loc_y = loc_y + (-W_X * s + W_Y * c);

                            double M = w_loc_y * L * L / 12.0;

                            int[] dofs = new int[] { nodeMap[ln.A] * 3, nodeMap[ln.A] * 3 + 1, nodeMap[ln.A] * 3 + 2, nodeMap[ln.B] * 3, nodeMap[ln.B] * 3 + 1, nodeMap[ln.B] * 3 + 2 };
                            F[dofs[0]] += w_glob_x * L / 2.0;
                            F[dofs[1]] += w_glob_y * L / 2.0;
                            F[dofs[2]] += M;
                            F[dofs[3]] += w_glob_x * L / 2.0;
                            F[dofs[4]] += w_glob_y * L / 2.0;
                            F[dofs[5]] -= M;
                        }
                    }
                }

                foreach (var kv in nodeLoads)
                {
                    if (!nodeMap.ContainsKey(kv.Key)) continue;
                    int idx = nodeMap[kv.Key];
                    foreach (var ld in kv.Value.Where(l => l.Group == group))
                    {
                        if (ld.Dir == LoadDirection.PlusX) F[idx * 3] += ld.Value;
                        if (ld.Dir == LoadDirection.MinusX) F[idx * 3] -= ld.Value;
                        if (ld.Dir == LoadDirection.PlusY) F[idx * 3 + 1] += ld.Value;
                        if (ld.Dir == LoadDirection.MinusY) F[idx * 3 + 1] -= ld.Value;
                    }
                }

                double[] F_orig = new double[nDof];
                Array.Copy(F, F_orig, nDof);

                foreach (var kv in supports)
                {
                    if (!nodeMap.ContainsKey(kv.Key)) continue;
                    int idx = nodeMap[kv.Key];
                    if (kv.Value == SupportType.Pin)
                    {
                        F[idx * 3] = 0;
                        F[idx * 3 + 1] = 0;
                    }
                    else if (kv.Value == SupportType.Roller)
                    {
                        F[idx * 3 + 1] = 0;
                    }
                    else if (kv.Value == SupportType.Fixed)
                    {
                        F[idx * 3] = 0;
                        F[idx * 3 + 1] = 0;
                        F[idx * 3 + 2] = 0;
                    }
                }

                double[] U = SolveGauss(K, F);

                double[] R = new double[nDof];
                for (int i = 0; i < nDof; i++)
                {
                    double ku = 0;
                    for (int j = 0; j < nDof; j++) ku += K_orig[i, j] * U[j];
                    R[i] = ku - F_orig[i];
                }

                resultsByGroup[group] = (U, R);
            }

            return resultsByGroup;
        }

        public static double[,] MultiplyMatrix(double[,] A, double[,] B)
        {
            int rA = A.GetLength(0); int cA = A.GetLength(1); int cB = B.GetLength(1);
            double[,] res = new double[rA, cB];
            for (int i = 0; i < rA; i++)
                for (int j = 0; j < cB; j++)
                    for (int k = 0; k < cA; k++)
                        res[i, j] += A[i, k] * B[k, j];
            return res;
        }

        public static double[] Multiply(double[,] A, double[] B)
        {
            int r = A.GetLength(0); int c = A.GetLength(1);
            double[] res = new double[r];
            for (int i = 0; i < r; i++)
                for (int j = 0; j < c; j++)
                    res[i] += A[i, j] * B[j];
            return res;
        }

        public static double[,] Transpose(double[,] A)
        {
            int r = A.GetLength(0); int c = A.GetLength(1);
            double[,] res = new double[c, r];
            for (int i = 0; i < r; i++)
                for (int j = 0; j < c; j++)
                    res[j, i] = A[i, j];
            return res;
        }

        private static double[] SolveGauss(double[,] A, double[] B)
        {
            int n = B.Length;
            double[] x = new double[n];
            double[,] a = new double[n, n + 1];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) a[i, j] = A[i, j];
                a[i, n] = B[i];
            }

            for (int i = 0; i < n; i++)
            {
                double maxEl = Math.Abs(a[i, i]);
                int maxRow = i;
                for (int k = i + 1; k < n; k++)
                {
                    if (Math.Abs(a[k, i]) > maxEl) { maxEl = Math.Abs(a[k, i]); maxRow = k; }
                }
                for (int k = i; k < n + 1; k++)
                {
                    double tmp = a[maxRow, k]; a[maxRow, k] = a[i, k]; a[i, k] = tmp;
                }

                if (Math.Abs(a[i, i]) < 1e-6)
                    throw new InvalidOperationException("Sistem stabil değil (Unstable) veya mekanizma durumu var. Lütfen mesnetlerinizi ve çubuk bağlantılarınızı kontrol edin.");

                for (int k = i + 1; k < n; k++)
                {
                    double c = -a[k, i] / a[i, i];
                    for (int j = i; j < n + 1; j++)
                    {
                        if (i == j) a[k, j] = 0;
                        else a[k, j] += c * a[i, j];
                    }
                }
            }

            for (int i = n - 1; i >= 0; i--)
            {
                if (Math.Abs(a[i, i]) < 1e-6)
                    throw new InvalidOperationException("Sistem stabil değil (Unstable). Rijitlik matrisi tekil.");

                x[i] = a[i, n] / a[i, i];
                for (int k = i - 1; k >= 0; k--) a[k, n] -= a[k, i] * x[i];
            }
            return x;
        }
    }
}