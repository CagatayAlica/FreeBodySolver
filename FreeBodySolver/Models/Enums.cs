using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreeBodySolver.Models
{
    public enum LoadDirection
    {
        PlusX, MinusX, PlusY, MinusY, LocalPlusX, LocalMinusX, LocalPlusY, LocalMinusY
    }

    public enum SupportType
    {
        None, Pin, Roller, Fixed
    }
}
