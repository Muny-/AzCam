using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LcBuddy
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            if (System.Diagnostics.Process.GetProcessesByName("AzCam").Length > 0)
            {
                try
                {
                    TcpClient client = new TcpClient("127.0.0.1", 28934);
                }
                catch
                {

                }
            }
            else
            {
                System.Diagnostics.Process.Start(@"F:\Users\Kevin\Documents\Visual Studio 2013\Projects\LcBuddy-wpf\AzCam\bin\Debug\AzCam.exe");
            }
        }
    }
}
