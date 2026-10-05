using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreeBodySolver.Models
{
    public class LoadEntry
    {
        public float Value { get; set; }
        public string Group { get; set; }
        public LoadDirection Dir { get; set; }

        public LoadEntry(float v, string g, LoadDirection d)
        {
            Value = v;
            Group = g;
            Dir = d;
        }
    }
}
