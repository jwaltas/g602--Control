using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
class Launcher {
    [STAThread] static int Main(){try{string file=AppPaths.InstalledExe;if(!File.Exists(file))throw new IOException("Run Install or Update G602 Control first.");Process.Start(new ProcessStartInfo(file){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(file)});return 0;}catch(Exception ex){MessageBox.Show(ex.Message,"G602 Control");return 1;}}
}
