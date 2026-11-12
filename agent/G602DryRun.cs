// Bounded, event-driven Windows prototype. There is deliberately no SendInput,
// HID output, suppression hook, startup registration, or playback-enable switch.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class G602DryRun : NativeWindow, IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct Registration { public ushort Page, Usage; public uint Flags; public IntPtr Window; }
    [StructLayout(LayoutKind.Sequential)] struct Header { public uint Type, Size; public IntPtr Device; public UIntPtr Param; }
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(Registration[] devices,uint count,uint size);
    [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint headerSize);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,System.Text.StringBuilder name,ref uint size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4000000 };
    readonly Dictionary<string,object> config;
    readonly Dictionary<string,object> buttons;
    readonly Dictionary<IntPtr,string> names = new Dictionary<IntPtr,string>();
    readonly Dictionary<IntPtr,ushort> states = new Dictionary<IntPtr,ushort>();
    readonly List<object> observations = new List<object>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly List<string> errors = new List<string>();
    readonly System.Windows.Forms.Timer stopTimer = new System.Windows.Forms.Timer();
    int triggerCount, outsideRhino, reportCount;
    bool registered;
    public G602DryRun(Dictionary<string,object> settings) {
        config=settings; buttons=(Dictionary<string,object>)settings["buttons"];
        if ((string)settings["mode"] != "dry-run-only" || buttons.Count != 6) throw new Exception("Invalid dry-run configuration");
    }
    public void Start(int seconds) {
        CreateHandle(new CreateParams { Caption="G602 dry-run observer", Parent=new IntPtr(-3) });
        var registrations=new[]{new Registration {Page=0xFF80,Usage=0x80,Flags=0x100,Window=Handle}};
        if (!RegisterRawInputDevices(registrations,1,(uint)Marshal.SizeOf(typeof(Registration)))) throw new System.ComponentModel.Win32Exception();
        registered=true;
        stopTimer.Interval=seconds*1000;
        stopTimer.Tick += delegate { stopTimer.Stop(); Application.ExitThread(); };
        stopTimer.Start();
    }
    static bool RhinoFocused() {
        uint pid; GetWindowThreadProcessId(GetForegroundWindow(),out pid);
        try { using(var p=Process.GetProcessById((int)pid)) return String.Equals(p.ProcessName,"Rhino",StringComparison.OrdinalIgnoreCase); }
        catch {return false;}
    }
    string DeviceName(IntPtr device) {
        string name; if(names.TryGetValue(device,out name)) return name;
        uint size=0;
        if(GetRawInputDeviceInfo(device,0x20000007,null,ref size)==UInt32.MaxValue) return "";
        var text=new System.Text.StringBuilder((int)size+1);
        if(GetRawInputDeviceInfo(device,0x20000007,text,ref size)==UInt32.MaxValue) return "";
        name=text.ToString(); names[device]=name; return name;
    }
    public static ushort Mask(byte[] report) {
        if(report.Length!=5 || report[0]!=0x80) throw new Exception("Invalid report 80");
        return (ushort)(report[1] | report[2]<<8);
    }
    public void Observe(byte[] report,IntPtr device,bool rhino,bool simulated) {
        ushort current=Mask(report),previous;
        states.TryGetValue(device,out previous); states[device]=current;
        ushort pressed=(ushort)(current & ~previous); reportCount++;
        foreach(var pair in buttons) {
            var macro=(Dictionary<string,object>)pair.Value;
            if((pressed & Convert.ToInt32(macro["mask"]))==0) continue;
            if(!rhino) {outsideRhino++; continue;}
            triggerCount++;
            var timeline=new List<object>(); int offset=0,defaultOffset=0;
            foreach(object item in (IEnumerable)macro["events"]) {
                var e=(Dictionary<string,object>)item;
                if((string)e["kind"]=="delay") {
                    offset+=Convert.ToInt32(e["milliseconds"]);
                    defaultOffset+=(bool)macro["use_default_delay"]?Convert.ToInt32(macro["default_delay_ms"]):Convert.ToInt32(e["milliseconds"]);
                }
                else timeline.Add(new {at_explicit_ms=offset,at_default_delay_ms=defaultOffset,usage=Convert.ToInt32(e["usage"]),down=(bool)e["down"]});
            }
            if(observations.Count<1000) observations.Add(new {button="G"+pair.Key,name=macro["name"],
                elapsed_seconds=clock.Elapsed.TotalSeconds,simulated_focus=simulated,
                explicit_duration_ms=offset,default_delay_duration_ms=defaultOffset,
                timing_status=macro["timing_status"],planned_key_events=timeline,keys_sent=0});
        }
    }
    protected override void WndProc(ref Message message) {
        if(message.Msg==0xFF) {
            IntPtr buffer=IntPtr.Zero;
            try {
                uint headerSize=(uint)Marshal.SizeOf(typeof(Header)),size=0;
                if(GetRawInputData(message.LParam,0x10000003,IntPtr.Zero,ref size,headerSize)==UInt32.MaxValue) throw new Exception("Raw Input size query failed");
                if(size<headerSize || size>65536) throw new Exception("Invalid packet length");
                buffer=Marshal.AllocHGlobal((int)size);
                if(GetRawInputData(message.LParam,0x10000003,buffer,ref size,headerSize)==UInt32.MaxValue) throw new Exception("Raw Input read failed");
                var h=(Header)Marshal.PtrToStructure(buffer,typeof(Header));
                string path=DeviceName(h.Device).ToUpperInvariant();
                if(h.Type==2 && path.Contains("VID_046D&PID_C537") && path.Contains("&COL06")) {
                    if(size<headerSize+8) throw new Exception("Truncated HID batch header");
                    int pos=(int)headerSize,bytes=Marshal.ReadInt32(buffer,pos),count=Marshal.ReadInt32(buffer,pos+4);
                    if(bytes!=5 || count<0 || (long)count*bytes>size-headerSize-8) throw new Exception("Invalid report batch");
                    bool rhino=RhinoFocused();
                    for(int i=0;i<count;i++) {var report=new byte[5];Marshal.Copy(IntPtr.Add(buffer,pos+8+i*5),report,0,5);Observe(report,h.Device,rhino,false);}
                }
            } catch(Exception ex) {if(errors.Count<50) errors.Add(ex.Message);}
            finally {if(buffer!=IntPtr.Zero) Marshal.FreeHGlobal(buffer);}
        }
        base.WndProc(ref message);
    }
    public object Result(string mode,double cpuMs,long workingSet) {
        return new {mode=mode,playback_enabled=false,keys_sent=0,elapsed_seconds=clock.Elapsed.TotalSeconds,
            vendor_reports=reportCount,macro_candidates=triggerCount,ignored_outside_rhino=outsideRhino,
            observations=observations,errors=errors,cpu_milliseconds=cpuMs,working_set_bytes=workingSet,
            mapping_status=config["mapping_status"],timing_status="planned timeline only; scheduling and playback untested"};
    }
    public void Dispose() {
        stopTimer.Dispose();
        if(registered) RegisterRawInputDevices(new[]{new Registration {Page=0xFF80,Usage=0x80,Flags=1,Window=IntPtr.Zero}},1,(uint)Marshal.SizeOf(typeof(Registration)));
        if(Handle!=IntPtr.Zero) DestroyHandle();
    }
    static byte[] Hex(string text) {var bytes=new byte[text.Length/2];for(int i=0;i<bytes.Length;i++)bytes[i]=Convert.ToByte(text.Substring(i*2,2),16);return bytes;}
    [STAThread] static int Main(string[] args) {
        string output=null;
        try {
            var options=new Dictionary<string,string>();
            for(int i=0;i<args.Length;i+=2) {if(i+1>=args.Length)throw new Exception("Each option needs a value");options.Add(args[i],args[i+1]);}
            foreach(string key in options.Keys) if(key!="--config" && key!="--output" && key!="--seconds" && key!="--replay") throw new Exception("Unknown option; playback cannot be enabled");
            output=options["--output"];
            var settings=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(options["--config"]));
            bool created;
            using(var mutex=new Mutex(true,"Local\\G602DryRunPrototype",out created)) {
                if(!created) throw new Exception("Another G602 prototype is already running");
                using(var app=new G602DryRun(settings)) {
                    var process=Process.GetCurrentProcess(); var initialCpu=process.TotalProcessorTime;
                    string mode="live-dry-run";
                    if(options.ContainsKey("--replay")) {
                        mode="offline-replay-simulated-rhino";
                        var capture=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(options["--replay"]));
                        foreach(object item in (IEnumerable)capture["events"]) {
                            var e=(Dictionary<string,object>)item;
                            if((string)e["kind"]!="hid" || !((string)e["device"]).ToUpperInvariant().Contains("VID_046D&PID_C537") || !((string)e["device"]).ToUpperInvariant().Contains("&COL06")) continue;
                            foreach(object raw in (IEnumerable)e["reports"]) app.Observe(Hex((string)raw),new IntPtr(1),true,true);
                        }
                    } else {
                        int seconds=options.ContainsKey("--seconds")?Int32.Parse(options["--seconds"]):60;
                        if(seconds<1 || seconds>3600) throw new Exception("Duration must be 1–3600 seconds");
                        app.Start(seconds); Console.WriteLine("Dry run active: no keystrokes can be sent"); Application.Run();
                    }
                    process.Refresh();
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                    File.WriteAllText(output,Json.Serialize(app.Result(mode,(process.TotalProcessorTime-initialCpu).TotalMilliseconds,process.WorkingSet64)));
                    Console.WriteLine("Saved dry-run result; keys sent: 0");
                }
                mutex.ReleaseMutex();
            }
            return 0;
        } catch(Exception ex) {Console.Error.WriteLine(ex.Message);return 1;}
    }
}
