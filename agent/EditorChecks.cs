using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

class EditorChecks {
    sealed class FakeNative:IWindowsKeyboard {
        public readonly List<string> Events=new List<string>();public readonly HashSet<ushort> Held=new HashSet<ushort>();
        public bool TargetFocused(){return true;}public bool KeyAlreadyDown(int usage){return Held.Contains(WindowsKeyboardSink.ScanCode(usage));}
        public void Send(ushort scan,bool down){Events.Add(scan+":"+down);if(down)Held.Add(scan);else Held.Remove(scan);}
    }
    sealed class DecodeSink:IKeySink,IKeyValidator {
        public readonly StringBuilder Text=new StringBuilder();public readonly HashSet<int> Held=new HashSet<int>();public bool CtrlZ;
        public void ValidateUsage(int usage){WindowsKeyboardSink.ScanCode(usage);}
        public void Key(int usage,bool down){
            if(down){Held.Add(usage);if(usage==29 && Held.Contains(224))CtrlZ=true;
                if(usage>=4 && usage<=29)Text.Append((char)((Held.Contains(225)?'A':'a')+usage-4));
                else if(usage==40)Text.Append('\n');else if(usage==43)Text.Append('\t');
                else if(usage==44)Text.Append(' ');else if(usage==45)Text.Append(Held.Contains(225)?'_':'-');
                else if(usage>=30 && usage<=39)Text.Append("1234567890"[usage-30]);
                else if(usage==54)Text.Append(',');else if(usage==47)Text.Append(Held.Contains(225)?'{':'[');else if(usage==48)Text.Append(Held.Contains(225)?'}':']');
            }else Held.Remove(usage);
        }
    }
    sealed class NoWait:ISequenceWait {public bool Delay(int ms,WaitHandle cancellation){return !cancellation.WaitOne(0);}}
    static void Check(bool c,string m){if(!c)throw new Exception(m);}
    static DecodeSink Execute(string text,bool enter=true){var macro=MacroText.Compile(text,enter);MacroText.Validate(macro);var sink=new DecodeSink();using(var cancel=new ManualResetEvent(false)){var r=new SequenceRunner().Play(macro,sink,new CappedWait(new NoWait(),5),delegate{return true;},cancel);Check(r.Status=="completed" && sink.Held.Count==0,"Edited macro failed execution");}return sink;}
    static int Main(string[] args){
        try{
            var passed=new List<string>();var json=new JavaScriptSerializer{MaxJsonLength=4000000};
            var config=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[0]));var original=(Dictionary<string,object>)config["buttons"];var originalD=(Dictionary<string,object>)config["double_tap_macros"];
            var singles=new Dictionary<int,string>();var doubles=new Dictionary<int,string>();for(int i=4;i<=9;i++){singles[i]=MacroText.Display((Dictionary<string,object>)original[i.ToString()]);doubles[i]=originalD.ContainsKey(i.ToString())?MacroText.Display((Dictionary<string,object>)originalD[i.ToString()]):"";}
            var unchanged=EditorStore.Build(singles,doubles,original,originalD);foreach(var pair in original)Check(json.Serialize(pair.Value)==json.Serialize(((Dictionary<string,object>)unchanged["buttons"])[pair.Key]),"Untouched original was regenerated");passed.Add("unchanged single macros preserve original full events and overlaps");
            Check(MacroText.Display((Dictionary<string,object>)original["8"])=="rot","Rotate editor showed name instead of actual command");passed.Add("editor shows actual recorded command text");
            Check(Execute("DimAligned").Text.ToString()=="DimAligned\n","Case or Enter changed");passed.Add("case-preserving text compiles and executes with Enter");
            Check(Execute("_-Line 0,0,0").Text.ToString()=="_-Line 0,0,0\n","Numbers/punctuation changed");passed.Add("Rhino prefixes digits spaces and punctuation work");
            Check(Execute("Line\r\n0,0\r\n1,1").Text.ToString()=="Line\n0,0\n1,1\n","Multiple command lines failed");passed.Add("multiline input presses Enter between lines");
            Check(Execute("line{ENTER}").Text.ToString()=="line\n","Duplicate final Enter");passed.Add("explicit final Enter is not duplicated");
            Check(Execute("line",false).Text.ToString()=="line","Enter checkbox ignored");passed.Add("Enter can be disabled for prompt fragments");
            var shortcut=Execute("{ESC}{CTRL+Z}",false);Check(shortcut.CtrlZ && !shortcut.Text.ToString().Contains("\n") && shortcut.Held.Count==0,"Modifier shortcut failed");passed.Add("Escape and modifier shortcut release every key");
            var native=new FakeNative();using(var cancel=new ManualResetEvent(false)){var result=new SequenceRunner().Play(MacroText.Compile("{ESC}{CTRL+SHIFT+S}",false),new WindowsKeyboardSink(native),new NoWait(),delegate{return true;},cancel);Check(result.Status=="completed" && native.Held.Count==0 && String.Join(",",native.Events)=="1:True,1:False,29:True,42:True,31:True,31:False,42:False,29:False","Edited chord failed at Windows boundary");}passed.Add("edited modifier chord traverses native adapter and releases in reverse order");
            Check(Execute("{{test}}").Text.ToString()=="{test}\n","Literal braces failed");passed.Add("literal braces can be escaped");
            Check(MacroText.Compile("   ")==null,"Blank macro not disabled");passed.Add("blank fields disable a binding");
            foreach(string invalid in new[]{"{BAD}","{CTRL+CTRL+Z}","{ENTER","unclosed}","\u00e9",new string('a',2049)}){bool rejected=false;try{MacroText.Compile(invalid);}catch(FormatException){rejected=true;}Check(rejected,"Invalid text accepted");}passed.Add("invalid tokens unsupported characters and oversized edits rejected");
            singles[4]="Polyline";doubles[5]="{CTRL+Z}";
            var options=new Dictionary<int,bool>();for(int i=4;i<=9;i++)options[i]=i!=5;
            var edited=EditorStore.Build(singles,doubles,original,originalD,null,options);
            Check(((Dictionary<string,object>)edited["double_tap_macros"]).ContainsKey("5"),"New per-button double assignment missing");passed.Add("each button can gain its own independent double macro");
            string folder=Path.Combine(Path.GetTempPath(),"G602EditorChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try{
                string file=Path.Combine(folder,"macros.json");EditorStore.Save(file,unchanged);EditorStore.Save(file,edited);var loaded=EditorStore.Load(file);
                Check(MacroText.Display((Dictionary<string,object>)((Dictionary<string,object>)loaded["buttons"])["4"])=="Polyline","Saved macro not reloaded");
                Check(File.Exists(file+".bak"),"Previous settings backup absent");passed.Add("atomic save reloads edited macros and preserves previous file");
                EditorStore.Save(file,unchanged);var previous=EditorStore.Load(file+".bak");Check(MacroText.Display((Dictionary<string,object>)((Dictionary<string,object>)previous["buttons"])["4"])=="Polyline","Repeated save did not retain immediately previous settings");EditorStore.Save(file,edited);passed.Add("repeated atomic saves rotate the previous backup");
                string before=File.ReadAllText(file);doubles[9]="{BAD}";bool refused=false;try{EditorStore.Build(singles,doubles,original,originalD);}catch(FormatException ex){refused=ex.Message.Contains("G9 double tap");}Check(refused && File.ReadAllText(file)==before,"Invalid edit replaced saved file");passed.Add("invalid field identifies button and preserves saved settings");
                bool failed=false;try{EditorStore.Save(folder,edited);}catch(IOException){failed=true;}Check(failed && Directory.GetFiles(Path.GetDirectoryName(folder),Path.GetFileName(folder)+".*.tmp").Length==0,"Failed save leaked temporary file");passed.Add("failed replacement cleans temporary file");
            }finally{foreach(string file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}
            File.WriteAllText(args[1],json.Serialize(new{checks_passed=passed,real_keys_sent=0}));Console.WriteLine(passed.Count+" editor checks passed; no keyboard output");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
    }
}
