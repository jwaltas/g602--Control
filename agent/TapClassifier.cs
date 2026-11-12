using System;
using System.Collections.Generic;

public sealed class TapDecision {
    public int Button;public bool Double;public long Window;public uint Process;
}
public sealed class TapClassifier {
    sealed class Pending {public int Button;public long Due,Window;public uint Process;}
    readonly Dictionary<int,Pending> pending=new Dictionary<int,Pending>();
    public int WindowMilliseconds {get;private set;}
    public TapClassifier(int milliseconds){SetWindow(milliseconds);}
    public void SetWindow(int milliseconds){if(milliseconds<150 || milliseconds>600)throw new ArgumentException("Tap window must be 150–600 ms");Clear();WindowMilliseconds=milliseconds;}
    public void Clear(){pending.Clear();}
    public long? NextDue {
        get {long? due=null;foreach(var p in pending.Values)if(!due.HasValue || p.Due<due.Value)due=p.Due;return due;}
    }
    public List<TapDecision> Flush(long now){
        var due=new List<Pending>();foreach(var p in pending.Values)if(p.Due<=now)due.Add(p);
        due.Sort(delegate(Pending a,Pending b){int byTime=a.Due.CompareTo(b.Due);return byTime!=0?byTime:a.Button.CompareTo(b.Button);});
        var result=new List<TapDecision>();foreach(var p in due){pending.Remove(p.Button);result.Add(new TapDecision{Button=p.Button,Window=p.Window,Process=p.Process});}return result;
    }
    public List<TapDecision> Press(int button,bool doubleAssigned,long now,long window,uint process){
        var result=Flush(now);
        if(!doubleAssigned){result.Add(new TapDecision{Button=button,Window=window,Process=process});return result;}
        Pending first;
        if(pending.TryGetValue(button,out first) && first.Window==window && first.Process==process){
            pending.Remove(button);result.Add(new TapDecision{Button=button,Double=true,Window=window,Process=process});return result;
        }
        pending[button]=new Pending{Button=button,Due=now+WindowMilliseconds,Window=window,Process=process};return result;
    }
}
