using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Web.Script.Serialization;
class AppChecks {
    static Bitmap Render(Control control){
        var image=new Bitmap(control.Width,control.Height);control.DrawToBitmap(image,new Rectangle(0,0,control.Width,control.Height));
        using(var graphics=Graphics.FromImage(image))foreach(Control child in control.Controls)using(var rendered=Render(child))graphics.DrawImageUnscaled(rendered,child.Left,child.Top);
        return image;
    }
    static void Fields(Control control,Dictionary<string,TextBox> fields){foreach(Control child in control.Controls){if(child is TextBox && child.Name.StartsWith("G"))fields.Add(child.Name,(TextBox)child);Fields(child,fields);}}
    sealed class FakeSink:IKeySink {public int Events;public void Key(int usage,bool down){Events++;}}
    sealed class FakeWait:ISequenceWait {public bool Delay(int ms,WaitHandle cancel){return !cancel.WaitOne(0);}}
    [STAThread] static int Main(string[] args) {
        try{
            Application.EnableVisualStyles();
            string folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","work","editor-ui-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);
            string baseline=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"control-macros.json"));File.WriteAllText(Path.Combine(folder,"control-macros.json"),baseline);
            using(var form=new G602Control(folder)){
                using(var image=new Bitmap(form.ClientSize.Width,form.ClientSize.Height))using(var graphics=Graphics.FromImage(image)){
                    graphics.Clear(form.BackColor);
                    foreach(Control child in form.Controls)using(var rendered=Render(child))graphics.DrawImageUnscaled(rendered,child.Left,child.Top);
                    image.Save(args[0]);
                }
                var fields=new Dictionary<string,TextBox>();Fields(form,fields);
                if(fields.Count!=12 || fields["G7_double"].Text!="DimAligned" || fields["G8_single"].Text!="rot")throw new Exception("Button editors not prefilled correctly");
                foreach(Control control in form.Controls)if(control is Button && (control.Text.StartsWith("Start") || control.Text=="Set up mouse triggers" || control.Text=="Restore original buttons"))throw new Exception("Legacy main-window playback/setup controls remain");
                fields["G4_double"].Text="_Polyline";
                typeof(G602Control).GetMethod("Stop",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(form,null);
                if(!(bool)typeof(G602Control).GetMethod("SaveEditors",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(form,null))throw new Exception("UI save failed");
                var activation=(ProfileActivation)typeof(G602Control).GetField("activation",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(form);
                if(!activation.PauseRequested)throw new Exception("Saving undid emergency pause");
            }
            using(var reloaded=new G602Control(folder)){
                var activation=(ProfileActivation)typeof(G602Control).GetField("activation",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(reloaded);
                if(activation.PauseRequested)throw new Exception("New launch did not request automatic default profile");
                var fields=new Dictionary<string,TextBox>();Fields(reloaded,fields);if(fields["G4_double"].Text!="_Polyline")throw new Exception("UI did not reload edited assignment");
                string saved=File.ReadAllText(Path.Combine(folder,"default-profile.json"));fields["G5_single"].Text="{BAD}";
                if((bool)typeof(G602Control).GetMethod("SaveEditors",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(reloaded,null))throw new Exception("UI accepted invalid edit");
                if(File.ReadAllText(Path.Combine(folder,"default-profile.json"))!=saved)throw new Exception("Invalid UI edit replaced saved file");
                typeof(G602Control).GetMethod("FillEditors",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(reloaded,null);
                ((CheckBox)reloaded.Controls.Find("G4_double_enter",true)[0]).Checked=false;
                if(!(bool)typeof(G602Control).GetMethod("SaveEditors",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(reloaded,null))throw new Exception("Enter option could not save");
                var loaded=ProfileStore.Load(Path.Combine(folder,"default-profile.json")).Macros;var edited=(Dictionary<string,object>)((Dictionary<string,object>)loaded["double_tap_macros"])["4"];
                if((bool)edited["append_enter"])throw new Exception("Enter checkbox did not persist");
                typeof(G602Control).GetMethod("Stop",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(reloaded,null);
                var imported=new SavedProfile{Macros=loaded,DoubleTapMs=450,KeyPauseMs=3};
                typeof(G602Control).GetMethod("ApplyImportedProfile",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(reloaded,new object[]{imported});
                var committed=ProfileStore.Load(Path.Combine(folder,"default-profile.json"));if(committed.DoubleTapMs!=450 || committed.KeyPauseMs!=3 || !activation.PauseRequested)throw new Exception("UI import lost timing or emergency pause");
            }
            if(File.ReadAllText(Path.Combine(folder,"control-macros.json"))!=baseline)throw new Exception("UI save changed original macro file");
            using(var quiet=new G602Control(folder))using(var context=new G602ApplicationContext(quiet,true,false))using(var timer=new System.Windows.Forms.Timer{Interval=250}){
                bool appeared=false;quiet.VisibleChanged+=delegate{if(quiet.Visible)appeared=true;};
                if(quiet.Visible || quiet.ShowInTaskbar)throw new Exception("Background launch showed an editor/taskbar window");
                timer.Tick+=delegate{timer.Stop();if(quiet.Visible || quiet.ShowInTaskbar || appeared)throw new Exception("Background window flashed during message loop");quiet.Close();if(quiet.IsDisposed || quiet.Visible || quiet.ShowInTaskbar)throw new Exception("Window close exited the app or left the editor visible");quiet.Close();if(quiet.IsDisposed)throw new Exception("Repeated editor close exited the app");quiet.RequestExit();if(!quiet.IsDisposed)throw new Exception("Explicit tray exit did not dispose the app");};timer.Start();Application.Run(context);
            }
            int events=0;using(var input=new RawInputWindow())using(var timer=new System.Windows.Forms.Timer{Interval=1000}){
                input.Pressed+=delegate{events++;};input.Error+=delegate(string error){throw new Exception(error);};
                timer.Tick+=delegate{timer.Stop();Application.ExitThread();};timer.Start();Application.Run();
            }
            int replays=0;var buttons=new List<int>();
            if(args.Length>2){
                var json=new JavaScriptSerializer{MaxJsonLength=4000000};
                var capture=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[2]));
                var config=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"control-macros.json")));
                var macros=(Dictionary<string,object>)config["buttons"];ushort state=0;
                foreach(object item in (IEnumerable)capture["events"]){
                    var e=(Dictionary<string,object>)item;
                    if((string)e["kind"]!="hid" || !((string)e["device"]).ToUpperInvariant().Contains("&COL06"))continue;
                    foreach(string raw in (IEnumerable)e["reports"]){
                        var packet=new byte[raw.Length/2];for(int i=0;i<packet.Length;i++)packet[i]=Convert.ToByte(raw.Substring(i*2,2),16);
                        foreach(int button in RawInputWindow.DecodeButtons(state,packet,out state)){
                            buttons.Add(button);var sink=new FakeSink();
                            using(var cancel=new ManualResetEvent(false)){
                                var result=new SequenceRunner().Play((Dictionary<string,object>)macros[button.ToString()],sink,new FakeWait(),delegate{return true;},cancel);
                                if(result.Status!="completed" || sink.Events<8)throw new Exception("Captured button did not execute full simulated macro");replays++;
                            }
                        }
                    }
                }
                if(String.Join(",",buttons)!="4,5,6,7,8,9")throw new Exception("Unexpected captured input mapping");
            }
            File.WriteAllText(args[1],"{\"ui_rendered\":true,\"twelve_prefilled_editors\":true,\"ui_save_and_restart_reload\":true,\"invalid_ui_edit_preserves_saved_file\":true,\"ui_import_preserves_pause_and_timings\":true,\"background_has_no_visible_editor_or_taskbar_flash\":true,\"editor_close_keeps_tray_alive\":true,\"explicit_exit_disposes_app\":true,\"original_macro_file_unchanged\":true,\"raw_input_registered_and_removed\":true,\"real_keys_sent\":0,\"captured_macros_replayed\":"+replays+",\"button_events\":"+events+"}");
            Console.WriteLine("UI render and live input registration smoke passed; no keyboard output");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
    }
}
