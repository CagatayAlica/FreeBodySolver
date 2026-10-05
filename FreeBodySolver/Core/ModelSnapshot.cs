using System.Collections.Generic;
using System.Drawing;
using FreeBodySolver.Models;

namespace FreeBodySolver.Core
{
    public class ModelSnapshot
    {
        public Dictionary<int, PointF> Nodes { get; set; }
        public List<Line> Lines { get; set; }
        public Dictionary<int, SupportType> Supports { get; set; }
        public Dictionary<int, List<LoadEntry>> NodeLoads { get; set; }
        public Dictionary<int, List<LoadEntry>> LineLoads { get; set; }
        public int NextLineId { get; set; }
    }
}
