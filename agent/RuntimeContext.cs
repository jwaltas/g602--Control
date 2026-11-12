using System;
using System.Windows.Forms;
class G602ApplicationContext:ApplicationContext {
    public G602ApplicationContext(G602Control form,bool background,bool initialize=true){
        // MainForm intentionally remains null: Application.Run must not show
        // a hidden-start form automatically. The handle is message-only use.
        form.ShowInTaskbar=!background;IntPtr handle=form.Handle;
        form.FormClosed+=delegate{ExitThread();};
        if(!background)form.OpenEditor();
        if(initialize)form.BeginInvoke(new Action(async delegate{await form.InitializeRuntime();}));
    }
}
