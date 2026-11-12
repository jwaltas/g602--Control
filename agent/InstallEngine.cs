using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

public static class InstallEngine {
    static string Under(string root,string relative){
        if(Path.IsPathRooted(relative) || relative.Contains(":"))throw new IOException("Unsafe package path");
        foreach(string part in relative.Replace('\\','/').Split('/'))if(part==".." || part=="." || part=="")throw new IOException("Unsafe package path");
        string path=Path.GetFullPath(Path.Combine(root,relative));
        if(!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Package path escaped its folder");return path;
    }
    public static string Hash(string file){using(var stream=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    public static Dictionary<string,object> ValidatePayload(string source){
        var json=new JavaScriptSerializer{MaxJsonLength=4000000};
        var manifest=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(source,"manifest.json")));
        if((string)manifest["application"]!="g602-control" || (string)manifest["version"]!=AppInfo.Version)throw new IOException("Wrong application/version package");
        var files=(Dictionary<string,object>)manifest["files"];
        if(files.Count<5 || files.Count>1000 || !files.ContainsKey("G602Control.exe") || !files.ContainsKey("installed-layout.txt"))throw new IOException("Incomplete package");
        foreach(var pair in files){string path=Under(Path.Combine(source,"App"),pair.Key);if(!File.Exists(path) || (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0 || Hash(path)!=(string)pair.Value)throw new IOException("Package verification failed: "+pair.Key);}
        string seed=Path.Combine(source,"Seed","default-profile.json");if(Hash(seed)!=(string)manifest["seed_sha256"])throw new IOException("Seed profile verification failed");ProfileStore.Load(seed);
        if(manifest.ContainsKey("launcher_sha256") && Hash(Path.Combine(source,"Open G602 Control.exe"))!=(string)manifest["launcher_sha256"])throw new IOException("Launcher verification failed");return files;
    }
    public static bool Running(string appDirectory){
        string exe=Path.GetFullPath(Path.Combine(appDirectory,"G602Control.exe"));
        foreach(var process in Process.GetProcessesByName("G602Control"))using(process){try{if(String.Equals(process.MainModule.FileName,exe,StringComparison.OrdinalIgnoreCase))return true;}catch{}}
        if(File.Exists(exe)){try{using(var file=new FileStream(exe,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} }catch(IOException){return true;}}
        return false;
    }
    public static void Install(string source,string root,Func<string,bool> running){
        var files=ValidatePayload(source);string app=Under(root,"App"),data=Under(root,"Data"),state=Under(root,"install-state.json");
        var json=new JavaScriptSerializer();
        if(Directory.Exists(root) && Directory.GetFileSystemEntries(root).Length>0){
            if(!File.Exists(state) || (string)json.Deserialize<Dictionary<string,object>>(File.ReadAllText(state))["application"]!="g602-control")throw new IOException("Install folder is not recognized; it was left untouched");
        }
        if(Directory.Exists(app) && running(app))throw new IOException("Close G602 Control from the tray before updating. Your settings have not changed.");
        Directory.CreateDirectory(root);
        if(!File.Exists(state))File.WriteAllText(state,json.Serialize(new{application="g602-control",status="preparing",version=AppInfo.Version}));
        string stage=Under(root,"App-staging-"+Guid.NewGuid().ToString("N")),previous=null;
        Directory.CreateDirectory(stage);
        try{
            foreach(var pair in files){string target=Under(stage,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(Under(Path.Combine(source,"App"),pair.Key),target,false);if(Hash(target)!=(string)pair.Value)throw new IOException("Copied file failed verification");}
            Directory.CreateDirectory(data);
            string profile=Under(data,"default-profile.json");
            if(!File.Exists(profile) && !File.Exists(Under(data,"user-macros.json"))){var seed=ProfileStore.Load(Path.Combine(source,"Seed","default-profile.json"));ProfileStore.Save(profile,seed.Macros,seed.DoubleTapMs,seed.KeyPauseMs);}
            if(Directory.Exists(app)){previous=Under(root,"App-previous-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.Move(app,previous);}
            try{Directory.Move(stage,app);}catch{if(previous!=null && !Directory.Exists(app))Directory.Move(previous,app);throw;}
            string launcher=Path.Combine(source,"Open G602 Control.exe");if(File.Exists(launcher))File.Copy(launcher,Under(root,"Open G602 Control.exe"),true);
            string readme=Path.Combine(source,"README.txt");if(File.Exists(readme))File.Copy(readme,Under(root,"README.txt"),true);
            File.WriteAllText(state,json.Serialize(new{application="g602-control",status="ready",version=AppInfo.Version,settings=data}));
        }finally{
            // Only this invocation's verified, bounded staging directory may be removed.
            string fullStage=Path.GetFullPath(stage),fullRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(Directory.Exists(fullStage) && fullStage.StartsWith(fullRoot,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(fullStage).StartsWith("App-staging-",StringComparison.Ordinal))Directory.Delete(fullStage,true);
        }
    }
}
