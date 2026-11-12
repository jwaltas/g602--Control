using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

public sealed class SavedProfile {
    public Dictionary<string,object> Macros;
    public int DoubleTapMs,KeyPauseMs;
}
public static class ProfileStore {
    public static Dictionary<string,object> Envelope(Dictionary<string,object> macros,int taps,int pause){
        EditorStore.Validate(macros);
        if(taps<150 || taps>600 || pause<1 || pause>25)throw new FormatException("Profile timing is outside the supported range");
        return new Dictionary<string,object>{{"format","g602-control-profile"},{"format_version",1},{"app_version",AppInfo.Version},{"name","Default"},
            {"macros",macros},{"timing",new Dictionary<string,object>{{"double_tap_ms",taps},{"typing_delay_ms",pause}}}};
    }
    public static SavedProfile Parse(Dictionary<string,object> value){
        if((string)value["format"]!="g602-control-profile" || Convert.ToInt32(value["format_version"])!=1)throw new FormatException("Unsupported profile file");
        var macros=(Dictionary<string,object>)value["macros"];var timing=(Dictionary<string,object>)value["timing"];
        int taps=Convert.ToInt32(timing["double_tap_ms"]),pause=Convert.ToInt32(timing["typing_delay_ms"]);
        Envelope(macros,taps,pause);return new SavedProfile{Macros=macros,DoubleTapMs=taps,KeyPauseMs=pause};
    }
    public static SavedProfile Load(string path){
        if(new FileInfo(path).Length>4000000)throw new FormatException("Profile file is too large");
        return Parse(new JavaScriptSerializer{MaxJsonLength=4000000}.Deserialize<Dictionary<string,object>>(File.ReadAllText(path)));
    }
    public static void Save(string path,Dictionary<string,object> macros,int taps,int pause){EditorStore.Save(path,Envelope(macros,taps,pause));}
}
