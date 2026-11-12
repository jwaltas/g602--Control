using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
class TapChecks {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static int Main(string[] args){
        try{
            var passed=new List<string>();var taps=new TapClassifier(300);
            Check(taps.Press(7,true,0,10,1).Count==0,"Single fired prematurely");
            Check(taps.Flush(299).Count==0,"Single fired before deadline");
            var actions=taps.Flush(300);Check(actions.Count==1 && !actions[0].Double,"Single not emitted at deadline");passed.Add("single tap waits exactly for deadline in virtual time");
            taps.Press(7,true,400,10,1);actions=taps.Press(7,true,600,10,1);
            Check(actions.Count==1 && actions[0].Double && !taps.NextDue.HasValue,"Double must replace single");
            Check(taps.Flush(1000).Count==0,"Single leaked after double");passed.Add("double emits once and suppresses pending single");
            actions=taps.Press(4,false,1100,10,1);Check(actions.Count==1 && !actions[0].Double,"Unassigned button delayed");passed.Add("other buttons remain immediate");
            taps.Press(7,true,1200,10,1);actions=taps.Press(7,true,1500,10,1);
            Check(actions.Count==1 && !actions[0].Double && taps.NextDue==1800,"Deadline boundary misclassified");passed.Add("press at exact deadline starts a new single");
            taps.Clear();Check(taps.Flush(2000).Count==0,"Stopped pending action fired");passed.Add("stop clears pending tap");
            taps.Press(7,true,2100,10,1);actions=taps.Press(7,true,2200,20,2);
            Check(actions.Count==0,"Double crossed windows");actions=taps.Flush(2500);Check(actions.Count==1 && actions[0].Window==20 && !actions[0].Double,"Wrong pending window retained");passed.Add("double never crosses target window/process");
            taps.Press(7,true,2600,10,1);taps.Clear();Check(taps.Flush(3000).Count==0,"Focus loss pending macro survived");passed.Add("focus loss can discard pending single");
            taps.Press(7,true,3100,10,1);taps.SetWindow(450);Check(!taps.NextDue.HasValue,"Changing settings retained an old tap");
            taps.Press(7,true,3200,10,1);Check(taps.Flush(3649).Count==0 && taps.Flush(3650).Count==1,"Adjusted window ignored");passed.Add("adjustable window clears old state");
            taps.SetWindow(300);taps.Press(7,true,4000,10,1);actions=taps.Press(7,true,4100,10,1);
            Check(actions.Count==1 && actions[0].Double,"Triple first pair failed");Check(taps.Press(7,true,4200,10,1).Count==0,"Third tap emitted prematurely");
            actions=taps.Flush(4500);Check(actions.Count==1 && !actions[0].Double,"Triple third tap not single");passed.Add("non-overlapping pairs classify triple as double then single");
            bool rejected=false;try{taps.SetWindow(0);}catch(ArgumentException){rejected=true;}Check(rejected,"Invalid window accepted");passed.Add("invalid window refused");
            var config=new JavaScriptSerializer{MaxJsonLength=4000000}.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[1]));
            var doubles=(Dictionary<string,object>)config["double_tap_macros"];
            Check(doubles.Count==1 && doubles.ContainsKey("7"),"Unexpected double assignments");
            var macro=(Dictionary<string,object>)doubles["7"];var letters=new List<int>();
            foreach(object item in (System.Collections.IEnumerable)macro["events"]){var e=(Dictionary<string,object>)item;if((string)e["kind"]=="key" && (bool)e["down"])letters.Add(Convert.ToInt32(e["usage"]));}
            Check(String.Join(",",letters)=="7,12,16,4,15,12,10,17,8,7,40","DimAligned command incorrectly encoded");passed.Add("only G7 double maps to full DimAligned plus Enter");
            File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{checks_passed=passed,real_keys_sent=0}));
            Console.WriteLine(passed.Count+" tap checks passed; no keyboard output");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
    }
}
