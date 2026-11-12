using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

class WindowsOutputChecks {
    sealed class FakeKeyboard : IWindowsKeyboard {
        public bool Focused=true,Held=false;public int Sends;public bool Fail;
        public List<object> Events=new List<object>();
        public bool TargetFocused(){return Focused;}
        public bool KeyAlreadyDown(int usage){return Held;}
        public void Send(ushort scan,bool down){Sends++;if(Fail)throw new Exception("simulated native failure");Events.Add(new {scan=scan,down=down});}
    }
    sealed class NoWait : ISequenceWait {public bool Delay(int ms,WaitHandle cancel){return !cancel.WaitOne(0);}}
    static void Check(bool c,string m){if(!c)throw new Exception(m);}
    static void Reject(Action action){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Expected refusal");}
    static int Main(string[] args) {
        try {
            if(args.Length<2 || args.Length>3)throw new Exception("Usage: G602WindowsOutputChecks config.json result.json [live-config.json]");
            var passed=new List<string>();var json=new JavaScriptSerializer {MaxJsonLength=4000000};
            Check(IntPtr.Size==8 && Marshal.SizeOf(typeof(NativeRhinoKeyboard.Input))==40,"Incorrect x64 native layout");passed.Add("x64 INPUT size is 40 bytes");
            var press=NativeRhinoKeyboard.BuildInput(0x1C,true);var release=NativeRhinoKeyboard.BuildInput(0x1C,false);
            Check(press.Type==1 && press.Data.Keyboard.Scan==0x1C && press.Data.Keyboard.Flags==8 && release.Data.Keyboard.Flags==10,"Incorrect scan flags");
            Check(press.Data.Keyboard.Extra.ToUInt64()==0x47363032u,"Missing ownership tag");passed.Add("scan press/release flags and ownership tag");
            var fake=new FakeKeyboard();var sink=new WindowsKeyboardSink(fake);fake.Focused=false;
            Reject(delegate{sink.Key(15,true);});Check(fake.Sends==0,"Unfocused key sent");passed.Add("wrong target rejects key down");
            fake.Focused=true;fake.Held=true;Reject(delegate{sink.Key(15,true);});sink.Key(15,false);
            Check(fake.Sends==0,"Physical held key was released");passed.Add("physical held key remains untouched");
            fake.Held=false;sink.Key(15,true);fake.Focused=false;sink.Key(15,false);
            Check(fake.Sends==2,"Release after focus loss failed");passed.Add("owned key can be released after focus loss");
            fake=new FakeKeyboard();sink=new WindowsKeyboardSink(fake);fake.Fail=true;
            try{sink.Key(15,true);}catch(Exception){} fake.Fail=false;sink.Key(15,false);
            Check(fake.Sends==2,"Ambiguous failed down lost cleanup ownership");passed.Add("failed native send retains cleanup ownership");
            bool unsupported=false;try{sink.Key(255,true);}catch(ArgumentException){unsupported=true;}
            Check(unsupported && fake.Sends==2,"Unknown HID usage reached output");passed.Add("unsupported HID usage refused");
            var invalid=new Dictionary<string,object>{{"events",new object[]{
                new Dictionary<string,object>{{"kind","key"},{"usage",15},{"down",true}},
                new Dictionary<string,object>{{"kind","key"},{"usage",15},{"down",false}},
                new Dictionary<string,object>{{"kind","key"},{"usage",255},{"down",true}},
                new Dictionary<string,object>{{"kind","key"},{"usage",255},{"down",false}}}}};
            fake=new FakeKeyboard();sink=new WindowsKeyboardSink(fake);
            using(var cancelled=new ManualResetEvent(false)) {
                var r=new SequenceRunner().Play(invalid,sink,new NoWait(),delegate{return true;},cancelled);
                Check(r.Status.StartsWith("error:") && fake.Sends==0,"Late unsupported key caused partial output");
            }
            passed.Add("entire native key mapping validated before first output");
            var config=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[0]));
            var buttons=(Dictionary<string,object>)config["buttons"];
            if(args.Length==3){var live=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[2]));foreach(var pair in (Dictionary<string,object>)live["double_tap_macros"])buttons.Add(pair.Key+" double",pair.Value);}
            var runs=new List<object>();
            foreach(var pair in buttons) {
                var macro=(Dictionary<string,object>)pair.Value;fake=new FakeKeyboard();sink=new WindowsKeyboardSink(fake);
                using(var cancel=new ManualResetEvent(false)) {
                    var result=new SequenceRunner().Play(macro,sink,new NoWait(),delegate{return true;},cancel);
                    Check(result.Status=="completed" && result.CleanupErrors.Count==0,"Original macro failed Windows adapter");
                    int expected=0;foreach(object item in (IEnumerable)macro["events"])if((string)((Dictionary<string,object>)item)["kind"]=="key")expected++;
                    Check(fake.Sends==expected,"Missing key events");
                    passed.Add("complete macro through fake Windows boundary: "+macro["name"]);
                    runs.Add(new {button="G"+pair.Key,name=macro["name"],native_packets=fake.Events});
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
            File.WriteAllText(args[1],json.Serialize(new {mode="fake-native-boundary",real_SendInput_calls=0,checks_passed=passed,runs=runs}));
            Console.WriteLine(passed.Count+" Windows-output checks passed; real SendInput calls: 0");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
    }
}
