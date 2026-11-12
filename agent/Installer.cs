using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
class Installer {
    [STAThread] static int Main(string[] args){
        bool silent=args.Length==1 && args[0]=="--install-only";
        if(args.Length==1 && args[0]=="--show-target"){Console.WriteLine(AppPaths.InstallRoot);return 0;}
        if(args.Length>0 && !silent)return 2;
        bool created;using(var mutex=new Mutex(true,"Local\\G602ControlInstall",out created)){
            if(!created){if(!silent)MessageBox.Show("Another G602 update is already running.");return 1;}
            try{
                InstallEngine.Install(AppDomain.CurrentDomain.BaseDirectory,AppPaths.InstallRoot,InstallEngine.Running);
                Console.WriteLine("Installed "+AppInfo.Version+" at "+AppPaths.InstallRoot+"; settings preserved; Windows startup unchanged");
                if(!silent)MessageBox.Show("G602 Control "+AppInfo.Version+" is installed. Your settings were kept.\n\nClose the old app, then use Open G602 Control to launch the permanent version.\nWindows startup has not been enabled automatically.","G602 Control update complete");
                return 0;
            }catch(Exception ex){Console.Error.WriteLine(ex.Message);if(!silent)MessageBox.Show(ex.Message,"G602 Control update stopped");return 1;}
            finally{mutex.ReleaseMutex();}
        }
    }
}
