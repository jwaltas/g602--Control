using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
class ActivationChecks {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static int Main(string[] args){try{
        var passed=new List<string>();var profile=new ProfileActivation();
        Check(!profile.Active,"Unverified profile active");profile.Verified=true;profile.InputReady=true;profile.MonitoringReady=true;
        Check(profile.Active,"Default profile did not become active automatically");passed.Add("verified ready default profile activates without Start");
        profile.Editing=true;Check(!profile.Active,"Unsaved editing left playback active");profile.Editing=false;Check(profile.Active,"Save did not reactivate default profile");passed.Add("editing suspends and successful save resumes automatically");
        profile.PauseRequested=true;profile.Editing=true;profile.Editing=false;Check(!profile.Active,"Save undid explicit pause");passed.Add("saving cannot override emergency or tray pause");
        profile.PauseRequested=false;Check(profile.Active,"Explicit resume failed");passed.Add("tray resume restores ready profile");
        profile.BusySetup=true;Check(!profile.Active,"Playback active during setup");profile.PauseRequested=true;profile.BusySetup=false;Check(!profile.Active,"Setup completion undid pause");passed.Add("pause during setup remains paused afterwards");
        profile.PauseRequested=false;profile.Verified=false;Check(!profile.Active,"Failed setup enabled playback");profile.Verified=true;Check(profile.Active,"Successful setup did not activate default");passed.Add("setup/read verification gates automatic activation");
        profile.Faulted=true;Check(!profile.Active,"Fault did not lock playback");profile.PauseRequested=false;Check(!profile.Active,"Resume bypassed fault");passed.Add("critical input/output fault remains locked");
        profile.Faulted=false;profile.Closing=true;Check(!profile.Active,"Closing profile active");passed.Add("closing disables playback");
        profile.Closing=false;profile.InputReady=false;Check(!profile.Active,"Missing receiver input ignored");profile.InputReady=true;profile.MonitoringReady=false;Check(!profile.Active,"Missing stop/focus monitoring ignored");passed.Add("input and monitoring are both required");
        File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{checks_passed=passed,real_keys_sent=0}));Console.WriteLine(passed.Count+" activation checks passed; no keyboard output");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}}
}
