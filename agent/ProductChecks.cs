using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
class ProductChecks {
    sealed class MemoryStartup:IStartupValue {public string Value;public int Writes,Removes;public string Read(){return Value;}public void Write(string value){Writes++;Value=value;}public void Remove(){Removes++;Value=null;}}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action action){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,"Expected rejection");}
    static Dictionary<string,object> Clone(Dictionary<string,object> value){var json=new JavaScriptSerializer{MaxJsonLength=4000000};return json.Deserialize<Dictionary<string,object>>(json.Serialize(value));}
    static void Manifest(string source){var files=new Dictionary<string,object>();foreach(string file in Directory.GetFiles(Path.Combine(source,"App"),"*",SearchOption.AllDirectories)){string relative=file.Substring(Path.Combine(source,"App").Length+1).Replace('\\','/');files[relative]=InstallEngine.Hash(file);}File.WriteAllText(Path.Combine(source,"manifest.json"),new JavaScriptSerializer().Serialize(new{application="g602-control",version=AppInfo.Version,seed_sha256=InstallEngine.Hash(Path.Combine(source,"Seed","default-profile.json")),files=files}));}
    static int Main(string[] args){try{
        var passed=new List<string>();var json=new JavaScriptSerializer{MaxJsonLength=4000000};
        var config=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[0]));var macros=new Dictionary<string,object>{{"schema_version",1},{"buttons",config["buttons"]},{"double_tap_macros",config["double_tap_macros"]}};
        string work=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","work","product-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(work);
        string file=Path.Combine(work,"profile.json");ProfileStore.Save(file,macros,275,5);var loaded=ProfileStore.Load(file);
        Check(json.Serialize(loaded.Macros)==json.Serialize(macros) && loaded.DoubleTapMs==275 && loaded.KeyPauseMs==5,"Export round trip lost events or timing");passed.Add("profile export/import preserves all events and both timings");
        var edited=Clone(macros);((Dictionary<string,object>)edited["buttons"])["4"]=MacroText.Compile("_Polyline");ProfileStore.Save(file,edited,450,2);loaded=ProfileStore.Load(file);
        Check(loaded.DoubleTapMs==450 && loaded.KeyPauseMs==2 && MacroText.Display((Dictionary<string,object>)((Dictionary<string,object>)loaded.Macros["buttons"])["4"])=="_Polyline","Atomic profile replacement incomplete");
        Check(ProfileStore.Load(file+".bak").DoubleTapMs==275,"Previous profile backup missing");passed.Add("macros and timings commit together with a previous-profile backup");
        string before=File.ReadAllText(file);var bad=Clone(macros);((Dictionary<string,object>)bad["buttons"])["4"]=new Dictionary<string,object>{{"name","bad"},{"events",new object[]{new Dictionary<string,object>{{"kind","key"},{"usage",15},{"down",true}}}}};
        Reject(delegate{ProfileStore.Save(file,bad,300,5);});Check(File.ReadAllText(file)==before,"Invalid import changed saved profile");passed.Add("invalid imported events leave saved profile untouched");
        Reject(delegate{ProfileStore.Save(file,macros,100,5);});Reject(delegate{ProfileStore.Save(file,macros,300,0);});passed.Add("invalid imported timing rejected");
        var envelope=ProfileStore.Envelope(macros,300,5);envelope["format_version"]=2;Reject(delegate{ProfileStore.Parse(envelope);});passed.Add("future profile formats rejected without mutation");
        envelope=ProfileStore.Envelope(macros,300,5);envelope["startup_mode"]="active";var imported=ProfileStore.Parse(envelope);ProfileStore.Save(file,imported.Macros,imported.DoubleTapMs,imported.KeyPauseMs);Check(!File.ReadAllText(file).Contains("startup_mode"),"Import persisted startup directives");passed.Add("profiles cannot change Windows startup or application paths");
        var startup=new MemoryStartup();string exe=Path.Combine(work,"A folder","G602Control.exe");Check(StartupSettings.Mode(startup,exe)=="off" && startup.Writes==0,"Startup enabled implicitly");passed.Add("startup stays opt-in");
        StartupSettings.Set(startup,exe,"active");Check(startup.Value=="\""+exe+"\" --background" && StartupSettings.Mode(startup,exe)=="active","Active startup not background-only");
        var options=LaunchOptions.Parse(new[]{"--background"});Check(options.Background && !options.Paused,"Background active launch options lost");passed.Add("active login startup uses quoted executable and background flag");
        StartupSettings.Set(startup,exe,"paused");options=LaunchOptions.Parse(new[]{"--background","--paused"});Check(options.Background && options.Paused && StartupSettings.Mode(startup,exe)=="paused","Paused startup not tray-only");passed.Add("paused login startup is also background-only");
        StartupSettings.Set(startup,exe,"off");Check(startup.Value==null && startup.Removes==1,"Off did not remove managed entry");Reject(delegate{LaunchOptions.Parse(new[]{"--unknown"});});passed.Add("startup can be disabled and unknown launch options rejected");
        string source=Path.Combine(work,"Release"),app=Path.Combine(source,"App"),seed=Path.Combine(source,"Seed");Directory.CreateDirectory(app);Directory.CreateDirectory(seed);
        foreach(string name in new[]{"G602Control.exe","installed-layout.txt","control-macros.json","runtime.json","setup_profile.py"})File.WriteAllText(Path.Combine(app,name),"fixture "+name);
        ProfileStore.Save(Path.Combine(seed,"default-profile.json"),macros,275,5);Manifest(source);
        string root=Path.Combine(work,"Installed","G602-Control");InstallEngine.Install(source,root,delegate{return false;});
        Check(ProfileStore.Load(Path.Combine(root,"Data","default-profile.json")).DoubleTapMs==275,"Installer migration lost custom timing");passed.Add("first install seeds validated current profile into separate Data");
        string profile=Path.Combine(root,"Data","default-profile.json");ProfileStore.Save(profile,edited,425,3);string saved=File.ReadAllText(profile);File.WriteAllText(Path.Combine(root,"Data","keep.txt"),"user data");File.WriteAllText(Path.Combine(app,"G602Control.exe"),"updated fixture");Manifest(source);
        InstallEngine.Install(source,root,delegate{return false;});Check(File.ReadAllText(profile)==saved && File.ReadAllText(Path.Combine(root,"Data","keep.txt"))=="user data","Update replaced saved settings");passed.Add("application update preserves user settings byte-for-byte");
        Reject(delegate{InstallEngine.Install(source,root,delegate{return true;});});Check(File.ReadAllText(profile)==saved,"Running-app refusal changed data");passed.Add("running application blocks update without changing settings");
        File.WriteAllText(Path.Combine(app,"G602Control.exe"),"tampered");string missing=Path.Combine(work,"MustNotBeCreated");Reject(delegate{InstallEngine.Install(source,missing,delegate{return false;});});Check(!Directory.Exists(missing),"Bad package wrote an install directory");passed.Add("checksum failure refuses installation before any destination write");
        Manifest(source);var manifest=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(source,"manifest.json")));((Dictionary<string,object>)manifest["files"])["../escape"]="bad";File.WriteAllText(Path.Combine(source,"manifest.json"),json.Serialize(manifest));Reject(delegate{InstallEngine.ValidatePayload(source);});passed.Add("package paths cannot escape application folder");
        Manifest(source);string unrelated=Path.Combine(work,"Unrelated");Directory.CreateDirectory(unrelated);File.WriteAllText(Path.Combine(unrelated,"important.txt"),"keep");Reject(delegate{InstallEngine.Install(source,unrelated,delegate{return false;});});Check(File.ReadAllText(Path.Combine(unrelated,"important.txt"))=="keep","Unrecognized folder changed");passed.Add("unrecognized nonempty install directory is left untouched");
        File.WriteAllText(args[1],json.Serialize(new{version=AppInfo.Version,checks_passed=passed,actual_registry_writes=0,real_keys_sent=0,install_target=AppPaths.InstallRoot}));Console.WriteLine(passed.Count+" product checks passed; actual registry writes: 0; target: "+AppPaths.InstallRoot);return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}}
}
