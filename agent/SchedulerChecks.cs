using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

class SchedulerChecks {
    sealed class FakeWait : ISequenceWait {
        public int Elapsed;public Action AfterDelay;
        public bool Delay(int ms,WaitHandle cancel) {Elapsed+=ms;if(AfterDelay!=null)AfterDelay();return !cancel.WaitOne(0);}
    }
    sealed class RecordingSink : IKeySink {
        public readonly List<string> Events=new List<string>();
        public readonly List<object> Timeline=new List<object>();
        public FakeWait Clock;public Action OnKey;
        public void Key(int usage,bool down) {
            Events.Add(usage+":"+down);Timeline.Add(new {milliseconds=Clock.Elapsed,usage=usage,down=down});
            if(OnKey!=null) OnKey();
        }
    }
    static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    static Dictionary<string,object> Event(int usage,bool down) {return new Dictionary<string,object>{{"kind","key"},{"usage",usage},{"down",down}};}
    static Dictionary<string,object> Macro() {return new Dictionary<string,object>{{"events",new object[]{Event(15,true),Event(12,true),new Dictionary<string,object>{{"kind","delay"},{"milliseconds",25}},Event(15,false),Event(12,false)}}};}
    static void InterruptionChecks(List<string> passed) {
        using(var cancel=new ManualResetEvent(false)) {
            var runner=new SequenceRunner();var wait=new FakeWait();var sink=new RecordingSink {Clock=wait};bool focused=true;
            wait.AfterDelay=delegate {focused=false;};
            var r=runner.Play(Macro(),sink,wait,delegate{return focused;},cancel);
            Check(r.Status=="focus_lost","Focus loss not detected");
            Check(String.Join(",",sink.Events)=="15:True,12:True,12:False,15:False","Held keys not released in reverse order");
            passed.Add("focus loss releases overlapping keys");
            wait=new FakeWait();sink=new RecordingSink {Clock=wait};wait.AfterDelay=delegate{cancel.Set();};
            r=runner.Play(Macro(),sink,wait,delegate{return true;},cancel);
            Check(r.Status=="cancelled" && sink.Events.Count==4,"Cancellation cleanup failed");passed.Add("cancellation releases held keys");
            cancel.Reset();wait=new FakeWait();sink=new RecordingSink {Clock=wait};
            sink.OnKey=delegate {Check(runner.Play(Macro(),sink,wait,delegate{return true;},cancel).Status=="busy_rejected","Reentrant macro accepted");};
            r=runner.Play(Macro(),sink,wait,delegate{return true;},cancel);
            Check(r.Status=="completed","Normal playback failed");passed.Add("busy runner rejects overlap without queueing");
            wait=new FakeWait();sink=new RecordingSink {Clock=wait};
            r=runner.Play(Macro(),sink,wait,delegate{return false;},cancel);
            Check(r.Status=="focus_lost" && sink.Events.Count==0,"Unfocused sequence emitted keys");passed.Add("unfocused start emits nothing");
            var invalid=Macro();invalid["events"]=new object[]{Event(15,true)};
            r=runner.Play(invalid,sink,wait,delegate{return true;},cancel);
            Check(r.Status.StartsWith("error:") && sink.Events.Count==0,"Invalid macro partially emitted");passed.Add("invalid sequence rejected before output");
            wait=new FakeWait();sink=new RecordingSink {Clock=wait};int calls=0;
            sink.OnKey=delegate {if(++calls==2)throw new Exception("simulated sink failure");};
            r=runner.Play(Macro(),sink,wait,delegate{return true;},cancel);
            Check(r.Status.StartsWith("error:") && sink.Events.Count==4,"Sink failure cleanup failed");passed.Add("sink failure still attempts key cleanup");
            wait=new FakeWait();sink=new RecordingSink {Clock=wait};focused=true;
            wait.AfterDelay=delegate{focused=false;};
            sink.OnKey=delegate{if(sink.Events.Count==3)throw new Exception("simulated cleanup failure");};
            r=runner.Play(Macro(),sink,wait,delegate{return focused;},cancel);
            Check(r.CleanupErrors.Count==1 && sink.Events.Count==4,"One cleanup failure prevented other releases");
            passed.Add("one failed cleanup does not skip remaining releases");
        }
    }
    static int Main(string[] args) {
        try {
            if(args.Length<2 || args.Length>3)throw new Exception("Usage: G602SchedulerChecks config.json result.json [live-config.json]");
            var json=new JavaScriptSerializer {MaxJsonLength=4000000};
            var config=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[0]));
            var buttons=(Dictionary<string,object>)config["buttons"];
            if(args.Length==3){var live=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[2]));foreach(var pair in (Dictionary<string,object>)live["double_tap_macros"])buttons.Add(pair.Key+" double",pair.Value);}
            var passed=new List<string>();var runs=new List<object>();
            InterruptionChecks(passed);
            foreach(var pair in buttons) {
                var macro=(Dictionary<string,object>)pair.Value;var wait=new FakeWait();var sink=new RecordingSink {Clock=wait};
                using(var cancel=new ManualResetEvent(false)) {
                    var result=new SequenceRunner().Play(macro,sink,wait,delegate{return true;},cancel);
                    Check(result.Status=="completed" && result.CleanupErrors.Count==0,"Macro failed: "+pair.Key);
                    var expected=new List<string>();int duration=0;
                    foreach(object item in (IEnumerable)macro["events"]) {
                        var e=(Dictionary<string,object>)item;
                        if((string)e["kind"]=="delay")duration+=Convert.ToInt32(e["milliseconds"]);
                        else expected.Add(Convert.ToInt32(e["usage"])+":"+(bool)e["down"]);
                    }
                    Check(String.Join(",",sink.Events)==String.Join(",",expected) && wait.Elapsed==duration,"Order/timing mismatch");
                    passed.Add("exact sequence and virtual delays: "+macro["name"]);
                    runs.Add(new {button="G"+pair.Key,name=macro["name"],virtual_duration_ms=wait.Elapsed,timeline=sink.Timeline});
                    var fastWait=new FakeWait();var fastSink=new RecordingSink{Clock=fastWait};
                    result=new SequenceRunner().Play(macro,fastSink,new CappedWait(fastWait,5),delegate{return true;},cancel);
                    int fastDuration=0;foreach(object item in (IEnumerable)macro["events"]){var e=(Dictionary<string,object>)item;if((string)e["kind"]=="delay")fastDuration+=Math.Min(5,Convert.ToInt32(e["milliseconds"]));}
                    Check(result.Status=="completed" && String.Join(",",fastSink.Events)==String.Join(",",expected),"Fast typing changed key order");
                    Check(fastWait.Elapsed==fastDuration && fastDuration<=duration,"Fast typing did not cap delays");
                    passed.Add("fast typing preserves event order and caps delays: "+macro["name"]);
                    runs.Add(new {button="G"+pair.Key,name=macro["name"],mode="fast-5ms-cap",virtual_duration_ms=fastWait.Elapsed,timeline=fastSink.Timeline});
                }
            }
            // A real cancellation handle also wakes an otherwise long wait.
            using(var cancelled=new ManualResetEvent(true)) Check(!new InterruptibleWait().Delay(10000,cancelled),"Cancellation wait did not wake");
            passed.Add("real wait respects cancellation handle");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
            File.WriteAllText(args[1],json.Serialize(new {mode="offline-simulated-keyboard",real_keys_sent=0,checks_passed=passed,runs=runs}));
            Console.WriteLine(passed.Count+" scheduler checks passed; real keys sent: 0");return 0;
        } catch(Exception ex) {Console.Error.WriteLine(ex.Message);return 1;}
    }
}
