using System;
using System.Windows.Forms;

namespace FreeBodySolver
{
    public static class Class1
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new NodeLineForm());
        }
    }
}
