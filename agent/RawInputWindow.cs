using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public sealed class RawInputWindow : NativeWindow,IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct Reg {public ushort Page,Usage;public uint Flags;public IntPtr Window;}
    [StructLayout(LayoutKind.Sequential)] struct Header {public uint Type,Size;public IntPtr Device;public UIntPtr Param;}
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(Reg[] r,uint count,uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input,uint cmd,IntPtr data,ref uint size,uint headerSize);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr device,uint cmd,StringBuilder text,ref uint size);
    readonly Dictionary<IntPtr,ushort> states=new Dictionary<IntPtr,ushort>();
    readonly Dictionary<IntPtr,string> names=new Dictionary<IntPtr,string>();
    public event Action<int> Pressed;public event Action<string> Error;public event Action DeviceChanged;
    public static int[] DecodeButtons(ushort previous,byte[] report,out ushort current){
        if(report.Length!=5 || report[0]!=0x80)throw new ArgumentException("Expected report 80");
        current=(ushort)(report[1]|report[2]<<8);int pressed=current & ~previous;var buttons=new List<int>();
        for(int button=4;button<=9;button++)if((pressed & (1<<(button-1)))!=0)buttons.Add(button);
        return buttons.ToArray();
    }
    public RawInputWindow() {
        CreateHandle(new CreateParams {Parent=new IntPtr(-3),Caption="G602 input"});
        if(!RegisterRawInputDevices(new[]{new Reg {Page=0xFF80,Usage=0x80,Flags=0x2100,Window=Handle}},1,(uint)Marshal.SizeOf(typeof(Reg)))){var error=new System.ComponentModel.Win32Exception();DestroyHandle();throw error;}
    }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0xFE){states.Clear();names.Clear();if(DeviceChanged!=null)DeviceChanged();}
        if(m.Msg==0xFF) {
            IntPtr buffer=IntPtr.Zero;
            try {
                uint size=0,hs=(uint)Marshal.SizeOf(typeof(Header));
                if(GetRawInputData(m.LParam,0x10000003,IntPtr.Zero,ref size,hs)==UInt32.MaxValue || size<hs+8 || size>65536)throw new Exception("Input packet unavailable");
                buffer=Marshal.AllocHGlobal((int)size);
                if(GetRawInputData(m.LParam,0x10000003,buffer,ref size,hs)==UInt32.MaxValue)throw new Exception("Input packet read failed");
                var h=(Header)Marshal.PtrToStructure(buffer,typeof(Header));string name;
                if(!names.TryGetValue(h.Device,out name)) {
                    uint length=0;GetRawInputDeviceInfo(h.Device,0x20000007,null,ref length);
                    var text=new StringBuilder((int)length+1);
                    if(GetRawInputDeviceInfo(h.Device,0x20000007,text,ref length)==UInt32.MaxValue)throw new Exception("Device identity unavailable");
                    name=text.ToString().ToUpperInvariant();names[h.Device]=name;
                }
                if(h.Type==2 && name.Contains("VID_046D&PID_C537") && name.Contains("&COL06")) {
                    int pos=(int)hs,bytes=Marshal.ReadInt32(buffer,pos),count=Marshal.ReadInt32(buffer,pos+4);
                    if(bytes!=5 || count<0 || (long)bytes*count>size-hs-8)throw new Exception("Invalid button report");
                    for(int i=0;i<count;i++) {
                        int at=pos+8+i*5;if(Marshal.ReadByte(buffer,at)!=0x80)continue;
                        ushort current,previous;states.TryGetValue(h.Device,out previous);var report=new byte[5];Marshal.Copy(IntPtr.Add(buffer,at),report,0,5);
                        int[] buttons=DecodeButtons(previous,report,out current);states[h.Device]=current;
                        foreach(int button in buttons)if(Pressed!=null)Pressed(button);
                    }
                }
            }catch(Exception ex){if(Error!=null)Error(ex.Message);}
            finally{if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);}
        }
        base.WndProc(ref m);
    }
    public void Dispose(){RegisterRawInputDevices(new[]{new Reg{Page=0xFF80,Usage=0x80,Flags=1}},1,(uint)Marshal.SizeOf(typeof(Reg)));DestroyHandle();}
}
