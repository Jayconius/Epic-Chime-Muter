using System;
using System.Windows.Forms;

namespace EpicChimeMuter
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(new ChimeManager()));
        }
    }
}
