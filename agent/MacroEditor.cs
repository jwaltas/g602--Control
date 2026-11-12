using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

public static class MacroText {
    static readonly string plain="1234567890-=[]\\;'`,./ ";
    static readonly int[] usages={30,31,32,33,34,35,36,37,38,39,45,46,47,48,49,51,52,53,54,55,56,44};
    static readonly string shifted="!@#$%^&*()_+{}|:\"~<>?";
    static readonly int[] shiftedUsages={30,31,32,33,34,35,36,37,38,39,45,46,47,48,49,51,52,53,54,55,56};
    static int NamedKey(string name){
        name=name.ToUpperInvariant();
        if(name.Length==1 && name[0]>='A' && name[0]<='Z')return name[0]-'A'+4;
        if(name.Length==1){int at=plain.IndexOf(name[0]);if(at>=0)return usages[at];}
        switch(name){case "ENTER":case "RETURN":return 40;case "ESC":case "ESCAPE":return 41;case "BACKSPACE":return 42;case "TAB":return 43;case "SPACE":return 44;}
        int function;if(name.StartsWith("F") && Int32.TryParse(name.Substring(1),out function) && function>=1 && function<=12)return 57+function;
        throw new FormatException("Unknown key: "+name);
    }
    static void Key(List<object> events,int usage,bool down){events.Add(new Dictionary<string,object>{{"kind","key"},{"usage",usage},{"down",down}});}
    static void Stroke(List<object> events,int usage,List<int> modifiers){foreach(int mod in modifiers)Key(events,mod,true);Key(events,usage,true);Key(events,usage,false);for(int i=modifiers.Count-1;i>=0;i--)Key(events,modifiers[i],false);}
    public static Dictionary<string,object> Compile(string text,bool appendEnter=true){
        if(String.IsNullOrWhiteSpace(text))return null;
        if(text.Length>2048)throw new FormatException("Macro text is limited to 2048 characters");
        string normalized=text.Replace("\r\n","\n").Replace('\r','\n');var keys=new List<object>();int last=0;
        for(int i=0;i<normalized.Length;i++){
            char c=normalized[i];var modifiers=new List<int>();int usage;
            if(c=='{' && (i+1>=normalized.Length || normalized[i+1]!='{')){
                int end=normalized.IndexOf('}',i+1);if(end<0)throw new FormatException("Close the key token with }");
                string[] parts=normalized.Substring(i+1,end-i-1).ToUpperInvariant().Split('+');
                for(int m=0;m<parts.Length-1;m++){
                    int mod;switch(parts[m]){case "CTRL":case "CONTROL":mod=224;break;case "SHIFT":mod=225;break;case "ALT":mod=226;break;default:throw new FormatException("Unknown modifier: "+parts[m]);}
                    if(modifiers.Contains(mod))throw new FormatException("Duplicate modifier");modifiers.Add(mod);
                }
                usage=NamedKey(parts[parts.Length-1]);i=end;
            }else{
                if(c=='{' && i+1<normalized.Length && normalized[i+1]=='{')i++;
                else if(c=='}'){if(i+1<normalized.Length && normalized[i+1]=='}')i++;else throw new FormatException("Use }} to type a literal closing brace");}
                if(c=='\n')usage=40;
                else if(c=='\t')usage=43;
                else if(c>='a' && c<='z')usage=c-'a'+4;
                else if(c>='A' && c<='Z'){usage=c-'A'+4;modifiers.Add(225);}
                else{int at=plain.IndexOf(c);if(at>=0)usage=usages[at];else{at=shifted.IndexOf(c);if(at<0)throw new FormatException("Unsupported character: "+c+". Use ordinary command text or key tokens.");usage=shiftedUsages[at];modifiers.Add(225);}}
            }
            Stroke(keys,usage,modifiers);last=usage;
        }
        if(appendEnter && last!=40)Stroke(keys,40,new List<int>());
        var events=new List<object>();foreach(object key in keys){if(events.Count>0)events.Add(new Dictionary<string,object>{{"kind","delay"},{"milliseconds",25}});events.Add(key);}
        return new Dictionary<string,object>{{"name",text.Replace('\r',' ').Replace('\n',' ')},{"editor_text",text},{"append_enter",appendEnter},{"events",events.ToArray()},
            {"use_default_delay",false},{"default_delay_ms",25},{"timing_status","editor events; playback uses selected typing delay"}};
    }
    public static string Display(Dictionary<string,object> macro){
        if(macro==null)return "";if(macro.ContainsKey("editor_text"))return (string)macro["editor_text"];
        var text=new StringBuilder();
        foreach(object item in (IEnumerable)macro["events"]){var e=(Dictionary<string,object>)item;if((string)e["kind"]!="key" || !(bool)e["down"])continue;int key=Convert.ToInt32(e["usage"]);
            if(key>=4 && key<=29)text.Append((char)('a'+key-4));else if(key==40)text.Append("\n");else throw new FormatException("Original macro needs its preserved event representation");}
        string value=text.ToString();if(value.EndsWith("\n"))value=value.Substring(0,value.Length-1);
        if(macro.ContainsKey("name") && String.Equals(value,(string)macro["name"],StringComparison.OrdinalIgnoreCase))return (string)macro["name"];
        return value.Replace("\n",Environment.NewLine);
    }
    public static void Validate(Dictionary<string,object> macro){
        if(!macro.ContainsKey("name") || !(macro["name"] is string))throw new FormatException("Macro needs a name");
        if(macro.ContainsKey("editor_text") && !(macro["editor_text"] is string))throw new FormatException("Invalid editor text");
        if(macro.ContainsKey("append_enter") && !(macro["append_enter"] is bool))throw new FormatException("Invalid Enter option");
        var held=new HashSet<int>();int count=0;
        foreach(object item in (IEnumerable)macro["events"]){if(++count>20000)throw new FormatException("Macro has too many events");var e=(Dictionary<string,object>)item;
            if((string)e["kind"]=="delay"){int ms=Convert.ToInt32(e["milliseconds"]);if(ms<0 || ms>10000)throw new FormatException("Invalid delay");}
            else if((string)e["kind"]=="key"){int usage=Convert.ToInt32(e["usage"]);WindowsKeyboardSink.ScanCode(usage);bool down=(bool)e["down"];if(down?!held.Add(usage):!held.Remove(usage))throw new FormatException("Unbalanced key events");}
            else throw new FormatException("Unknown macro event");}
        if(held.Count!=0)throw new FormatException("Macro ends with held keys");
    }
}

public static class EditorStore {
    public static Dictionary<string,object> Build(Dictionary<int,string> singles,Dictionary<int,string> doubles,Dictionary<string,object> oldSingles,Dictionary<string,object> oldDoubles,Dictionary<int,bool> singleEnter=null,Dictionary<int,bool> doubleEnter=null){
        var result=new Dictionary<string,object>();
        foreach(bool twice in new[]{false,true}){
            var texts=twice?doubles:singles;var old=twice?oldDoubles:oldSingles;var updated=new Dictionary<string,object>();
            for(int i=4;i<=9;i++){
                string key=i.ToString();Dictionary<string,object> macro=null;
                var enter=twice?doubleEnter:singleEnter;bool append=enter==null?true:enter[i];
                try{if(old.ContainsKey(key) && texts[i]==MacroText.Display((Dictionary<string,object>)old[key]) && append==(!((Dictionary<string,object>)old[key]).ContainsKey("append_enter") || (bool)((Dictionary<string,object>)old[key])["append_enter"]))macro=(Dictionary<string,object>)old[key];else macro=MacroText.Compile(texts[i],append);if(macro!=null){MacroText.Validate(macro);updated.Add(key,macro);}}
                catch(Exception ex){throw new FormatException("G"+i+(twice?" double tap: ":" single tap: ")+ex.Message);}
            }
            result[twice?"double_tap_macros":"buttons"]=updated;
        }
        result["schema_version"]=1;return result;
    }
    public static void Save(string path,Dictionary<string,object> value){
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                using(var writer=new StreamWriter(stream,new UTF8Encoding(false))){writer.Write(new JavaScriptSerializer{MaxJsonLength=4000000}.Serialize(value));writer.Flush();stream.Flush(true);}
            }
            if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static Dictionary<string,object> Load(string path){
        if(new FileInfo(path).Length>4000000)throw new FormatException("Saved macros file is too large");
        var result=new JavaScriptSerializer{MaxJsonLength=4000000}.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
        Validate(result);return result;
    }
    public static void Validate(Dictionary<string,object> result){
        if(Convert.ToInt32(result["schema_version"])!=1)throw new FormatException("Unknown saved macro format");
        foreach(string section in new[]{"buttons","double_tap_macros"})foreach(var pair in (Dictionary<string,object>)result[section]){
            int button;if(!Int32.TryParse(pair.Key,out button) || button<4 || button>9)throw new FormatException("Unknown button in saved macros");MacroText.Validate((Dictionary<string,object>)pair.Value);
        }
    }
}
