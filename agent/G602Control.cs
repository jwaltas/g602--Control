using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class G602Control : Form {
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint mods,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    delegate void WinEvent(IntPtr hook,uint type,IntPtr hwnd,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    readonly string home=AppDomain.CurrentDomain.BaseDirectory;
    readonly string codeHome=AppDomain.CurrentDomain.BaseDirectory;
    readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=4000000};
    readonly Label status=new Label(),detail=new Label();
    readonly ToolStripMenuItem setup=new ToolStripMenuItem("Set up mouse triggers"),restore=new ToolStripMenuItem("Restore original buttons"),trayPause=new ToolStripMenuItem("Pause profile");
    readonly ProfileActivation activation=new ProfileActivation();
    readonly Dictionary<int,TextBox> singleEditors=new Dictionary<int,TextBox>(),doubleEditors=new Dictionary<int,TextBox>();
    readonly Dictionary<int,CheckBox> singleEnter=new Dictionary<int,CheckBox>(),doubleEnter=new Dictionary<int,CheckBox>();
    readonly Button saveMacros=new Button(),revertEdits=new Button();
    bool dirty,loadingEditors,loadingTiming,runtimeStarted;string editorLoadError;int retryCount;
    readonly SequenceRunner runner=new SequenceRunner();
    readonly ManualResetEvent cancel=new ManualResetEvent(false);
    readonly System.Windows.Forms.Timer tapTimer=new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer readinessRetry=new System.Windows.Forms.Timer();
    readonly Stopwatch tapClock=Stopwatch.StartNew();
    readonly NumericUpDown tapWindow=new NumericUpDown();
    readonly NumericUpDown typingDelay=new NumericUpDown();
    TapClassifier taps;Dictionary<string,object> doubleMacros;
    Dictionary<string,object> macros;RawInputWindow input;Task playback;bool enabled,verified,closing,settingUp,playing;
    IntPtr activeWindow;WinEvent focusCallback;IntPtr focusHook;bool stopHotkey;
    readonly NotifyIcon tray=new NotifyIcon();
    readonly string sessionName="session-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".jsonl";
    public G602Control():this(AppPaths.DataDirectory(AppDomain.CurrentDomain.BaseDirectory),AppDomain.CurrentDomain.BaseDirectory,false){}
    public G602Control(string dataDirectory):this(dataDirectory,AppDomain.CurrentDomain.BaseDirectory,false){}
    public G602Control(string dataDirectory,string applicationDirectory,bool startPaused) {
        home=Path.GetFullPath(dataDirectory);
        codeHome=Path.GetFullPath(applicationDirectory);Directory.CreateDirectory(home);activation.PauseRequested=startPaused;
        Text="G602 Control "+AppInfo.Version+" — default profile";ClientSize=new Size(960,760);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
        Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterScreen;
        status.SetBounds(20,16,750,40);status.Text="Loading default profile…";status.Font=new Font(Font,FontStyle.Bold);Controls.Add(status);
        var advanced=new Button{Text="Advanced ▾"};advanced.SetBounds(800,16,140,32);Controls.Add(advanced);
        var advancedMenu=new ContextMenuStrip();advancedMenu.Items.Add("Recheck receiver",null,async delegate{await Profile(false);});advancedMenu.Items.Add(setup);advancedMenu.Items.Add(restore);advanced.Click+=delegate{advancedMenu.Show(advanced,new Point(0,advanced.Height));};
        advancedMenu.Items.Add(new ToolStripSeparator());advancedMenu.Items.Add("Export profile…",null,delegate{ExportProfile();});advancedMenu.Items.Add("Import profile…",null,delegate{ImportProfile();});
        var startup=new ToolStripMenuItem("Start with Windows");var startupItems=new Dictionary<string,ToolStripMenuItem>();
        foreach(string mode in new[]{"off","active","paused"}){string selected=mode;var item=new ToolStripMenuItem(mode=="off"?"Off":mode=="active"?"Active — tray only":"Paused — tray only");startupItems[mode]=item;startup.DropDownItems.Add(item);item.Click+=delegate{try{if(selected!="off" && !AppPaths.IsInstalled(Application.ExecutablePath))throw new Exception("Use the installed app before enabling Windows startup.");StartupSettings.Set(new RegistryStartupValue(),AppPaths.InstalledExe,selected);foreach(var pair in startupItems)pair.Value.Checked=pair.Key==selected;Status(selected=="off"?"Windows startup disabled.":"Windows startup saved — tray only, no editor window.");}catch(Exception ex){Status("Startup setting unchanged: "+ex.Message);}};}
        try{string mode=StartupSettings.Mode(new RegistryStartupValue(),AppPaths.InstalledExe);foreach(var pair in startupItems)pair.Value.Checked=pair.Key==mode;}catch{startup.Text="Start with Windows (unavailable)";startup.Enabled=false;}
        advancedMenu.Items.Add(startup);advancedMenu.Items.Add("Open settings folder",null,delegate{Process.Start(new ProcessStartInfo(home){UseShellExecute=true});});
        setup.Click+=async delegate{await Profile(true);};restore.Click+=async delegate{await Profile(true,true);};
        detail.SetBounds(20,60,920,42);detail.Text="Your saved profile is active by default in Rhino 8. Save macros to apply changes.\nEnter adds a final Enter; a new line presses Enter. Blank = disabled. Optional keys: {ESC}, {TAB}, {CTRL+Z}.";Controls.Add(detail);
        var config=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(codeHome,"control-macros.json")));
        if((string)config["mode"]!="automatic-default-profile")throw new Exception("Expected default-profile configuration");
        macros=(Dictionary<string,object>)config["buttons"];
        doubleMacros=(Dictionary<string,object>)config["double_tap_macros"];
        int window=Convert.ToInt32(config["double_tap_ms"]);
        int pause=Convert.ToInt32(config["typing_delay_ms"]);
        try{
            string profileFile=Path.Combine(home,"default-profile.json");
            if(File.Exists(profileFile)){var profile=ProfileStore.Load(profileFile);macros=(Dictionary<string,object>)profile.Macros["buttons"];doubleMacros=(Dictionary<string,object>)profile.Macros["double_tap_macros"];window=profile.DoubleTapMs;pause=profile.KeyPauseMs;}
            else{
                string saved=Path.Combine(home,"user-macros.json");if(File.Exists(saved)){var user=EditorStore.Load(saved);macros=(Dictionary<string,object>)user["buttons"];doubleMacros=(Dictionary<string,object>)user["double_tap_macros"];}
                saved=Path.Combine(home,"tap-settings.json");if(File.Exists(saved)){var settings=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(saved));window=Convert.ToInt32(settings["double_tap_ms"]);if(settings.ContainsKey("typing_delay_ms"))pause=Convert.ToInt32(settings["typing_delay_ms"]);}
                ProfileStore.Save(profileFile,CurrentMacros(),window,pause);
            }
        }catch(Exception ex){editorLoadError=ex.Message;}
        if(pause<1 || pause>25)pause=5;
        if(window<150 || window>600)window=300;taps=new TapClassifier(window);
        for(int i=4;i<=9;i++) {
            int col=(i-4)%2,row=(i-4)/2;
            var group=new GroupBox{Text="G"+i};group.SetBounds(20+col*470,110+row*174,450,164);Controls.Add(group);
            foreach(bool twice in new[]{false,true}){
                int top=twice?78:26;
                var label=new Label{Text=twice?"Double tap":"Single tap"};label.SetBounds(14,top+5,78,28);group.Controls.Add(label);
                var box=new TextBox{Name="G"+i+(twice?"_double":"_single"),Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Vertical};box.SetBounds(94,top,270,40);group.Controls.Add(box);
                (twice?doubleEditors:singleEditors).Add(i,box);
                box.TextChanged+=delegate{MarkDirty();};
                var enter=new CheckBox{Name="G"+i+(twice?"_double_enter":"_single_enter"),Text="Enter"};enter.SetBounds(370,top+5,64,28);enter.CheckedChanged+=delegate{MarkDirty();};group.Controls.Add(enter);(twice?doubleEnter:singleEnter).Add(i,enter);
            }
            var hint=new Label{Text="Blank disables · uncheck Enter for keyboard shortcuts",ForeColor=SystemColors.GrayText};hint.SetBounds(14,132,420,22);group.Controls.Add(hint);
        }
        FillEditors();
        saveMacros.Text="Save macros";saveMacros.SetBounds(20,640,160,36);saveMacros.Click+=delegate{SaveEditors();};Controls.Add(saveMacros);
        revertEdits.Text="Revert edits";revertEdits.SetBounds(190,640,150,36);revertEdits.Click+=delegate{CancelPlayback();FillEditors();RefreshPlayback();Status(enabled?"Edits reverted. Default profile active.":"Edits reverted. Profile paused or unavailable.");};Controls.Add(revertEdits);
        var windowLabel=new Label{Text="Double-tap window (ms):"};windowLabel.SetBounds(365,647,215,26);Controls.Add(windowLabel);
        tapWindow.Minimum=150;tapWindow.Maximum=600;tapWindow.Increment=25;tapWindow.Value=window;tapWindow.SetBounds(575,640,75,30);Controls.Add(tapWindow);
        var speedLabel=new Label{Text="Key pause (ms):"};speedLabel.SetBounds(690,647,145,26);Controls.Add(speedLabel);
        typingDelay.Minimum=1;typingDelay.Maximum=25;typingDelay.Value=pause;typingDelay.SetBounds(835,640,100,30);Controls.Add(typingDelay);
        tapWindow.ValueChanged+=delegate{if(!loadingTiming){CancelPlayback();taps.SetWindow((int)tapWindow.Value);SaveTiming();}};
        typingDelay.ValueChanged+=delegate{if(!loadingTiming){CancelPlayback();SaveTiming();}};
        var note=new Label{Text="v"+AppInfo.Version+" · Default profile · Ctrl + Alt + Shift + F12 pauses playback. Use the tray icon to resume.\nWindows startup is tray-only. Import/export and startup options are under Advanced."};note.SetBounds(20,704,920,44);Controls.Add(note);
        tapTimer.Tick+=delegate{tapTimer.Stop();foreach(var decision in taps.Flush(tapClock.ElapsedMilliseconds))Dispatch(decision);ScheduleTaps();};
        readinessRetry.Tick+=async delegate{readinessRetry.Stop();if(closing || settingUp || verified)return;retryCount++;await Profile(false);if(!verified && retryCount<3)ScheduleReadinessRetry();};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){
            bool systemExit=e.CloseReason==CloseReason.WindowsShutDown || e.CloseReason==CloseReason.TaskManagerClosing;
            if(settingUp && !systemExit){e.Cancel=true;Status("Finishing the profile operation. Please wait before closing.");return;}
            if(playback!=null && !playback.IsCompleted && !systemExit){e.Cancel=true;closing=true;Stop();Task.Delay(50).ContinueWith(delegate{Ui(delegate{Close();});});return;}
            closing=true;cancel.Set();ClearTaps();readinessRetry.Dispose();tapTimer.Dispose();if(input!=null)input.Dispose();if(stopHotkey)UnregisterHotKey(Handle,1);if(focusHook!=IntPtr.Zero)UnhookWinEvent(focusHook);tray.Dispose();if(playback==null || playback.IsCompleted)cancel.Dispose();
        };
        tray.Icon=SystemIcons.Application;tray.Text="G602 Control";
        var menu=new ContextMenuStrip();menu.Items.Add("Open",null,delegate{OpenEditor();});menu.Items.Add(trayPause);trayPause.Click+=delegate{if(!activation.PauseRequested)Stop();else{if(dirty || editorLoadError!=null){Status("Save valid edits before resuming.");return;}activation.PauseRequested=false;RefreshPlayback();Status(enabled?"Default profile active — Rhino 8 only.":"Profile unavailable. Check receiver or use Advanced → Recheck receiver.");}};menu.Items.Add("Exit",null,delegate{Close();});tray.ContextMenuStrip=menu;
        tray.DoubleClick+=delegate{OpenEditor();};
        Resize+=delegate{if(WindowState==FormWindowState.Minimized){tray.Visible=true;Hide();}};
    }
    public void OpenEditor(){if(closing)return;ShowInTaskbar=true;Show();WindowState=FormWindowState.Normal;Activate();}
    public void RequestOpen(){Ui(OpenEditor);}
    public async Task InitializeRuntime(){
        if(runtimeStarted || closing)return;runtimeStarted=true;
        try{
            tray.Visible=true;Log(new{event_name="startup",version=AppInfo.Version,paused=activation.PauseRequested,settings=home});
            try{input=new RawInputWindow();input.Pressed+=MousePressed;input.DeviceChanged+=delegate{verified=false;retryCount=0;CancelPlayback();RefreshPlayback();ScheduleReadinessRetry();};input.Error+=delegate(string e){activation.Faulted=true;CancelPlayback();RefreshPlayback();Status("Input stopped: "+e);};}
            catch(Exception ex){Log(new{error="raw_input_start",detail=ex.Message});}
            stopHotkey=RegisterHotKey(Handle,1,0x4000|1|2|4,0x7B);
            focusCallback=delegate(IntPtr h,uint type,IntPtr hwnd,int obj,int child,uint t,uint ms){if(!closing){ClearTaps();if(activeWindow!=IntPtr.Zero && hwnd!=activeWindow)cancel.Set();}};
            focusHook=SetWinEventHook(3,3,IntPtr.Zero,focusCallback,0,0,0);
            if(!stopHotkey || focusHook==IntPtr.Zero){RefreshPlayback();Status("Playback locked: Windows could not register stop/focus monitoring.");setup.Enabled=false;return;}
            await Profile(false);
            if(!verified)ScheduleReadinessRetry();
            if(input==null)Status("Mouse input unavailable. Reopen the app after checking the receiver connection.");
            if(editorLoadError!=null){Log(new{error="saved_macro_load",detail=editorLoadError});Status("Saved profile could not load. Its file is unchanged. "+editorLoadError);}
        }catch(Exception ex){activation.Faulted=true;CancelPlayback();RefreshPlayback();Log(new{error="runtime_start",detail=ex.Message});Status("Profile unavailable: "+ex.Message);}
    }
    void ScheduleReadinessRetry(){if(!closing && !verified && !activation.Faulted && retryCount<3){readinessRetry.Stop();readinessRetry.Interval=retryCount==0?2000:retryCount==1?5000:10000;readinessRetry.Start();}}
    Dictionary<string,object> CurrentMacros(){return new Dictionary<string,object>{{"schema_version",1},{"buttons",macros},{"double_tap_macros",doubleMacros}};}
    void ExportProfile(){
        if(dirty){Status("Save or revert macro edits before exporting.");return;}
        using(var dialog=new SaveFileDialog{Title="Export profile",Filter="G602 profiles (*.json)|*.json",DefaultExt="json",FileName="G602-default-"+DateTime.Now.ToString("yyyy-MM-dd")+".json"}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            try{ProfileStore.Save(dialog.FileName,CurrentMacros(),(int)tapWindow.Value,(int)typingDelay.Value);Status("Profile exported — macros and timing included.");}catch(Exception ex){Status("Export failed: "+ex.Message);}
        }
    }
    void ApplyImportedProfile(SavedProfile profile){
        CancelPlayback();ProfileStore.Save(Path.Combine(home,"default-profile.json"),profile.Macros,profile.DoubleTapMs,profile.KeyPauseMs);
        macros=(Dictionary<string,object>)profile.Macros["buttons"];doubleMacros=(Dictionary<string,object>)profile.Macros["double_tap_macros"];
        loadingTiming=true;try{tapWindow.Value=profile.DoubleTapMs;typingDelay.Value=profile.KeyPauseMs;taps.SetWindow(profile.DoubleTapMs);}finally{loadingTiming=false;}
        editorLoadError=null;FillEditors();RefreshPlayback();Status(enabled?"Profile imported and saved. Default profile active.":"Profile imported and saved. Profile paused or unavailable.");
    }
    void ImportProfile(){
        if(dirty){Status("Save or revert macro edits before importing.");return;}
        using(var dialog=new OpenFileDialog{Title="Import profile",Filter="G602 profiles (*.json)|*.json|All files (*.*)|*.*"}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            try{ApplyImportedProfile(ProfileStore.Load(dialog.FileName));}catch(Exception ex){Status("Profile not imported: "+ex.Message);}
        }
    }
    void Ui(Action action){try{if(!IsDisposed && IsHandleCreated)BeginInvoke(action);}catch(InvalidOperationException){}}
    void Status(string text){status.Text=text;}
    void MarkDirty(){if(!loadingEditors){CancelPlayback();dirty=true;RefreshPlayback();Status("Unsaved macro edits. Save macros to apply them.");}}
    void FillEditors(){loadingEditors=true;try{for(int i=4;i<=9;i++){foreach(bool twice in new[]{false,true}){var mapping=twice?doubleMacros:macros;var macro=mapping.ContainsKey(i.ToString())?(Dictionary<string,object>)mapping[i.ToString()]:null;(twice?doubleEditors:singleEditors)[i].Text=MacroText.Display(macro);(twice?doubleEnter:singleEnter)[i].Checked=macro==null || !macro.ContainsKey("append_enter") || (bool)macro["append_enter"];}}dirty=false;}finally{loadingEditors=false;}}
    bool SaveEditors(){
        if(settingUp){Status("Wait for the mouse operation to finish before saving.");return false;}CancelPlayback();
        try{var singles=new Dictionary<int,string>();var doubles=new Dictionary<int,string>();var singleOptions=new Dictionary<int,bool>();var doubleOptions=new Dictionary<int,bool>();for(int i=4;i<=9;i++){singles[i]=singleEditors[i].Text;doubles[i]=doubleEditors[i].Text;singleOptions[i]=singleEnter[i].Checked;doubleOptions[i]=doubleEnter[i].Checked;}
            var value=EditorStore.Build(singles,doubles,macros,doubleMacros,singleOptions,doubleOptions);ProfileStore.Save(Path.Combine(home,"default-profile.json"),value,(int)tapWindow.Value,(int)typingDelay.Value);
            macros=(Dictionary<string,object>)value["buttons"];doubleMacros=(Dictionary<string,object>)value["double_tap_macros"];dirty=false;editorLoadError=null;RefreshPlayback();Status(enabled?"Macros saved. Default profile active.":"Macros saved. Profile paused or unavailable.");return true;
        }catch(Exception ex){Status("Not saved: "+ex.Message);return false;}
    }
    void SaveTiming(){try{ProfileStore.Save(Path.Combine(home,"default-profile.json"),CurrentMacros(),(int)tapWindow.Value,(int)typingDelay.Value);RefreshPlayback();Status(enabled?"Timing saved. Default profile active.":"Timing saved. Profile paused or unavailable.");}catch(Exception ex){RefreshPlayback();Status("Timing changed for this session; could not save: "+ex.Message);}}
    void Log(object entry){try{string path=Path.Combine(home,"logs");Directory.CreateDirectory(path);File.AppendAllText(Path.Combine(path,sessionName),json.Serialize(entry)+Environment.NewLine);}catch{}}
    bool LogitechRunning(){bool found=false;foreach(string name in new[]{"lghub_agent","lghub","LCore"})foreach(var process in Process.GetProcessesByName(name)){found=true;process.Dispose();}return found;}
    void ClearTaps(){if(taps!=null)taps.Clear();tapTimer.Stop();}
    void ScheduleTaps(){tapTimer.Stop();if(taps.NextDue.HasValue){tapTimer.Interval=(int)Math.Max(1,taps.NextDue.Value-tapClock.ElapsedMilliseconds);tapTimer.Start();}}
    void Dispatch(TapDecision d){if(enabled)Play(d.Button,d.Double,d.Window,d.Process);}
    void MousePressed(int button){
        if(!enabled || playing || settingUp || closing)return;
        if(!macros.ContainsKey(button.ToString()) && !doubleMacros.ContainsKey(button.ToString()))return;
        IntPtr hwnd=GetForegroundWindow();uint pid;GetWindowThreadProcessId(hwnd,out pid);
        try{if(!new NativeRhinoKeyboard(hwnd,pid).TargetFocused())return;}catch{return;}
        foreach(var d in taps.Press(button,doubleMacros.ContainsKey(button.ToString()),tapClock.ElapsedMilliseconds,hwnd.ToInt64(),pid))Dispatch(d);
        ScheduleTaps();
    }
    void CancelPlayback(){ClearTaps();cancel.Set();}
    void RefreshPlayback(){
        activation.Verified=verified;activation.InputReady=input!=null;activation.MonitoringReady=stopHotkey && focusHook!=IntPtr.Zero;
        activation.Editing=dirty || editorLoadError!=null;activation.BusySetup=settingUp;activation.Closing=closing;
        bool active=activation.Active;if(enabled && !active)CancelPlayback();else if(!active)ClearTaps();enabled=active;
        trayPause.Text=activation.PauseRequested?"Resume profile":"Pause profile";
        tray.Text=enabled?"G602 Control "+AppInfo.Version+" — active":"G602 Control "+AppInfo.Version+" — paused or unavailable";
    }
    void Stop(){activation.PauseRequested=true;CancelPlayback();RefreshPlayback();Status("Default profile paused. Resume from the tray icon.");}
    void Play(int button,bool twice=false,long expectedWindow=0,uint expectedProcess=0){
        if(closing || settingUp || playing)return;
        if(!(twice?doubleMacros:macros).ContainsKey(button.ToString()))return;
        if(LogitechRunning()){Stop();Status("Close G HUB / Logitech Gaming Software, then resume from the tray.");return;}
        IntPtr hwnd=expectedWindow==0?GetForegroundWindow():new IntPtr(expectedWindow);uint pid;GetWindowThreadProcessId(hwnd,out pid);
        if(expectedProcess!=0 && pid!=expectedProcess)return;
        NativeRhinoKeyboard native;
        try{native=new NativeRhinoKeyboard(hwnd,pid);if(!native.TargetFocused())return;}
        catch(Exception){Status("No keys sent: select a Rhino 8 window first.");return;}
        activeWindow=hwnd;cancel.Reset();playing=true;var macro=(Dictionary<string,object>)(twice?doubleMacros:macros)[button.ToString()];
        Status("Running "+macro["name"]+"…");
        int keyPause=(int)typingDelay.Value;
        playback=Task.Run(delegate{
            var result=runner.Play(macro,new WindowsKeyboardSink(native),new CappedWait(new InterruptibleWait(),keyPause),native.TargetFocused,cancel);
            Ui(delegate{activeWindow=IntPtr.Zero;playing=false;Log(new{at=DateTime.UtcNow,button=button,double_tap=twice,key_pause_ms=keyPause,macro=macro["name"],status=result.Status,key_events=result.KeyEvents,cleanup_errors=result.CleanupErrors});if(result.CleanupErrors.Count>0){activation.Faulted=true;CancelPlayback();RefreshPlayback();}Status((enabled?"Default profile active · ":"Profile paused · ")+(string)macro["name"]+": "+result.Status+(result.CleanupErrors.Count>0?" — key-release error; reopen the app":""));});
        });
    }
    async Task Profile(bool apply,bool restoring=false){
        if(settingUp || playing)return;CancelPlayback();settingUp=true;RefreshPlayback();setup.Enabled=false;restore.Enabled=false;saveMacros.Enabled=false;revertEdits.Enabled=false;
        Status(apply?"Backing up and configuring the six trigger keys…":"Checking current mouse assignments…");
        try{
            if(LogitechRunning())throw new Exception("Close G HUB / Logitech Gaming Software, then use Set up mouse triggers.");
            var runtime=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(codeHome,"runtime.json")));
            string python=(string)runtime["python"];
            if(!Path.IsPathRooted(python))python=Path.GetFullPath(Path.Combine(codeHome,python));
            if(!File.Exists(python))throw new Exception("Python runtime not found at the packaged local path.");
            string outFile=Path.Combine(home,"logs",apply?"setup-result.json":"profile-check.json");
            string arguments="\""+Path.Combine(codeHome,"setup_profile.py")+"\" --hid-library \""+Path.Combine(codeHome,"hidapi")+"\" --output \""+outFile+"\" --scope six"+(apply?" --apply":"")+(restoring?" --restore":"");
            string error=await Task.Run(delegate{
                using(var process=new Process{StartInfo=new ProcessStartInfo(python,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=codeHome}}){
                    process.Start();string stderr=process.StandardError.ReadToEnd();process.StandardOutput.ReadToEnd();process.WaitForExit();return process.ExitCode==0?null:stderr.Trim();
                }
            });
            if(error!=null)throw new Exception(error);
            var result=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(outFile));string state=(string)result["status"];
            verified=state=="configured_and_readback_verified" || state=="already_configured" || state=="verified_existing_setup";
            Status(restoring?"Original buttons restored. Mouse macros unavailable until set up again.":verified?"Default profile verified.":"Mouse setup required. Use Advanced → Set up mouse triggers.");
        }catch(Exception ex){verified=false;Status("Mouse macros locked: "+ex.Message);}
        finally{settingUp=false;setup.Enabled=true;restore.Enabled=true;saveMacros.Enabled=true;revertEdits.Enabled=true;if(verified)readinessRetry.Stop();else if(runtimeStarted)ScheduleReadinessRetry();RefreshPlayback();if(enabled)Status("Default profile active — Rhino 8 only.");}
    }
    protected override void WndProc(ref Message m){if(m.Msg==0x312 && m.WParam.ToInt32()==1)Stop();base.WndProc(ref m);}
    [STAThread] static int Main(string[] args){
        LaunchOptions options;
        try{options=LaunchOptions.Parse(args);}catch(Exception ex){if(Array.IndexOf(args,"--background")<0)MessageBox.Show(ex.Message,"G602 Control");return 1;}
        bool created;using(var mutex=new Mutex(true,"Local\\G602ControlPrototype",out created)){
            if(!created){if(!options.Background){try{using(var open=EventWaitHandle.OpenExisting("Local\\G602ControlOpen"))open.Set();}catch{MessageBox.Show("Close the older G602 Control app before opening this version.");}}return 0;}
            try{
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                using(var form=new G602Control(AppPaths.DataDirectory(AppDomain.CurrentDomain.BaseDirectory),AppDomain.CurrentDomain.BaseDirectory,options.Paused))
                using(var open=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\G602ControlOpen"))
                using(var context=new G602ApplicationContext(form,options.Background)){
                    var listener=ThreadPool.RegisterWaitForSingleObject(open,delegate(object state,bool timeout){form.RequestOpen();},null,Timeout.Infinite,false);
                    try{Application.Run(context);}finally{listener.Unregister(null);}
                }
                return 0;
            }
            catch(Exception ex){try{string logs=Path.Combine(AppPaths.DataDirectory(AppDomain.CurrentDomain.BaseDirectory),"logs");Directory.CreateDirectory(logs);File.AppendAllText(Path.Combine(logs,"startup-errors.txt"),DateTime.UtcNow.ToString("o")+" "+ex+Environment.NewLine);}catch{}if(!options.Background)MessageBox.Show(ex.Message,"G602 Control could not start");return 1;}
            finally{mutex.ReleaseMutex();}
        }
    }
}
