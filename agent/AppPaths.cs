using System;
using System.IO;
public sealed class LaunchOptions {
    public bool Background,Paused;
    public static LaunchOptions Parse(string[] args){var result=new LaunchOptions();foreach(string arg in args){if(arg=="--background")result.Background=true;else if(arg=="--paused")result.Paused=true;else throw new ArgumentException("Unknown launch option: "+arg);}return result;}
}
public static class AppPaths {
    public static string InstallRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Documents","Codex","G602-Control");}}
    public static string InstalledExe {get{return Path.Combine(InstallRoot,"App","G602Control.exe");}}
    public static string DataDirectory(string codeDirectory){
        // Installed release layout keeps mutable data beside, never inside App.
        string marker=Path.Combine(codeDirectory,"installed-layout.txt");
        return File.Exists(marker)?Path.GetFullPath(Path.Combine(codeDirectory,"..","Data")):Path.GetFullPath(codeDirectory);
    }
    public static bool IsInstalled(string executable){return String.Equals(Path.GetFullPath(executable),Path.GetFullPath(InstalledExe),StringComparison.OrdinalIgnoreCase);}
}
