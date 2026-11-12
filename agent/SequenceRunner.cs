using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

// Transport-independent runner with injectable timing and keyboard output.
public interface IKeySink { void Key(int usage,bool down); }
public interface IKeyValidator {void ValidateUsage(int usage);}
public interface ISequenceWait { bool Delay(int milliseconds,WaitHandle cancellation); }
public sealed class InterruptibleWait : ISequenceWait {
    public bool Delay(int milliseconds,WaitHandle cancellation) {return !cancellation.WaitOne(milliseconds);}
}
public sealed class CappedWait : ISequenceWait {
    readonly ISequenceWait inner;readonly int cap;
    public CappedWait(ISequenceWait inner,int cap){if(cap<1 || cap>25)throw new ArgumentException("Typing pause must be 1–25 ms");this.inner=inner;this.cap=cap;}
    public bool Delay(int milliseconds,WaitHandle cancellation){return inner.Delay(Math.Min(milliseconds,cap),cancellation);}
}
public sealed class SequenceResult {
    public string Status;
    public int KeyEvents;
    public readonly List<string> CleanupErrors=new List<string>();
}
public sealed class SequenceRunner {
    int busy;
    sealed class Step {public bool Delay,Down;public int Value;}
    static List<Step> Parse(Dictionary<string,object> macro) {
        var steps=new List<Step>(); var held=new HashSet<int>();
        foreach(object item in (IEnumerable)macro["events"]) {
            var e=(Dictionary<string,object>)item; string kind=(string)e["kind"];
            if(kind=="delay") {
                int ms=Convert.ToInt32(e["milliseconds"]);
                if(ms<0 || ms>10000) throw new ArgumentException("Invalid delay");
                steps.Add(new Step {Delay=true,Value=ms});
            } else if(kind=="key") {
                int usage=Convert.ToInt32(e["usage"]);bool down=(bool)e["down"];
                if(usage<0 || usage>255 || (down?!held.Add(usage):!held.Remove(usage))) throw new ArgumentException("Unbalanced key events");
                steps.Add(new Step {Value=usage,Down=down});
            } else throw new ArgumentException("Unknown event; refusing to discard it");
        }
        if(held.Count!=0) throw new ArgumentException("Sequence ends with held keys");
        return steps;
    }
    public SequenceResult Play(Dictionary<string,object> macro,IKeySink sink,ISequenceWait wait,Func<bool> focus,WaitHandle cancel) {
        if(Interlocked.CompareExchange(ref busy,1,0)!=0) return new SequenceResult {Status="busy_rejected"};
        var result=new SequenceResult {Status="completed"};var held=new List<int>();
        try {
            // Validate the entire macro before the first observable key event.
            var steps=Parse(macro);
            var validator=sink as IKeyValidator;
            if(validator!=null) foreach(var step in steps) if(!step.Delay) validator.ValidateUsage(step.Value);
            foreach(var step in steps) {
                if(cancel.WaitOne(0)) {result.Status="cancelled";break;}
                if(!focus()) {result.Status="focus_lost";break;}
                if(step.Delay) {
                    if(!wait.Delay(step.Value,cancel)) {result.Status="cancelled";break;}
                } else {
                    // Track down before sending: even a partially failed sink
                    // gets a release attempt. Keep failed ups for cleanup too.
                    if(step.Down) held.Add(step.Value);
                    sink.Key(step.Value,step.Down);result.KeyEvents++;
                    if(!step.Down) held.Remove(step.Value);
                }
            }
        } catch(Exception ex) {result.Status="error: "+ex.Message;}
        finally {
            // Releases are attempted even after focus loss, in reverse press
            // order; otherwise interrupted overlapping presses could stay down.
            for(int i=held.Count-1;i>=0;i--) {
                try {sink.Key(held[i],false);}
                catch(Exception ex) {result.CleanupErrors.Add(ex.Message);}
            }
            Interlocked.Exchange(ref busy,0);
        }
        return result;
    }
}
