using System;
using Microsoft.Win32;
public interface IStartupValue {string Read();void Write(string command);void Remove();}
public sealed class RegistryStartupValue:IStartupValue {
    const string Key="Software\\Microsoft\\Windows\\CurrentVersion\\Run";const string Name="G602Control";
    public string Read(){using(var key=Registry.CurrentUser.OpenSubKey(Key))return key==null?null:key.GetValue(Name) as string;}
    public void Write(string command){using(var key=Registry.CurrentUser.CreateSubKey(Key))key.SetValue(Name,command,RegistryValueKind.String);}
    public void Remove(){using(var key=Registry.CurrentUser.OpenSubKey(Key,true))if(key!=null)key.DeleteValue(Name,false);}
}
public static class StartupSettings {
    public static string Command(string executable,string mode){
        if(mode!="active" && mode!="paused")throw new ArgumentException("Unknown startup mode");
        if(executable.Contains("\"") || executable.Contains("\r") || executable.Contains("\n"))throw new ArgumentException("Invalid executable path");
        string command="\""+System.IO.Path.GetFullPath(executable)+"\" --background"+(mode=="paused"?" --paused":"");
        if(command.Length>260)throw new ArgumentException("Windows startup command is too long");return command;
    }
    public static string Mode(IStartupValue value,string executable){string current=value.Read();if(String.IsNullOrEmpty(current))return "off";if(current==Command(executable,"active"))return "active";if(current==Command(executable,"paused"))return "paused";return "other";}
    public static void Set(IStartupValue value,string executable,string mode){if(mode=="off")value.Remove();else value.Write(Command(executable,mode));}
}
