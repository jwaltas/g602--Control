using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public interface IWindowsKeyboard {
    bool TargetFocused();
    bool KeyAlreadyDown(int usage);
    void Send(ushort scan,bool down);
}

// Keeps the native boundary injectable, so checks never call SendInput.
public sealed class WindowsKeyboardSink : IKeySink, IKeyValidator {
    readonly IWindowsKeyboard keyboard;
    readonly HashSet<int> owned=new HashSet<int>();
    public WindowsKeyboardSink(IWindowsKeyboard keyboard) {this.keyboard=keyboard;}
    public void ValidateUsage(int usage){ScanCode(usage);}
    public void Key(int usage,bool down) {
        ushort scan=ScanCode(usage); // Reject unsupported keys before native I/O.
        if(down) {
            if(!keyboard.TargetFocused()) throw new InvalidOperationException("Original Rhino window lost focus");
            if(owned.Contains(usage) || keyboard.KeyAlreadyDown(usage)) throw new InvalidOperationException("Key is already held; refusing to take ownership");
            // Native output failure can be ambiguous; retain ownership so the
            // sequence runner's cleanup attempts a release.
            owned.Add(usage);
            keyboard.Send(scan,true);
        } else {
            if(!owned.Contains(usage)) return; // Never release a key we did not press.
            keyboard.Send(scan,false);
            owned.Remove(usage);
        }
    }
    public static ushort ScanCode(int usage) {
        // USB HID A–Z to PC set-1 scan positions. Preserve physical key events,
        // rather than replacing a sequence with Unicode text or Rhino commands.
        ushort[] letters={0x1E,0x30,0x2E,0x20,0x12,0x21,0x22,0x23,0x17,0x24,0x25,0x26,0x32,0x31,0x18,0x19,0x10,0x13,0x1F,0x14,0x16,0x2F,0x11,0x2D,0x15,0x2C};
        if(usage>=4 && usage<=29) return letters[usage-4];
        if(usage>=30 && usage<=39)return (ushort)(2+usage-30);
        if(usage>=58 && usage<=67)return (ushort)(0x3B+usage-58);
        switch(usage){case 40:return 0x1C;case 41:return 0x01;case 42:return 0x0E;case 43:return 0x0F;case 44:return 0x39;
            case 45:return 0x0C;case 46:return 0x0D;case 47:return 0x1A;case 48:return 0x1B;case 49:return 0x2B;
            case 51:return 0x27;case 52:return 0x28;case 53:return 0x29;case 54:return 0x33;case 55:return 0x34;case 56:return 0x35;
            case 68:return 0x57;case 69:return 0x58;case 224:return 0x1D;case 225:return 0x2A;case 226:return 0x38;}
        throw new ArgumentException("Unsupported HID usage: "+usage);
    }
}

public sealed class NativeRhinoKeyboard : IWindowsKeyboard {
    [StructLayout(LayoutKind.Sequential)] public struct KeyboardInput {
        public ushort VirtualKey,Scan;public uint Flags,Time;public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Explicit,Size=32)] public struct InputUnion {
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Input {
        public uint Type;public InputUnion Data;
    }
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] input,int size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code,uint type);
    readonly IntPtr target;readonly uint processId;
    public NativeRhinoKeyboard(IntPtr expectedWindow,uint expectedProcess) {
        if(expectedWindow==IntPtr.Zero || expectedProcess==0) throw new ArgumentException("A specific Rhino window/process is required");
        if(IntPtr.Size!=8 || Marshal.SizeOf(typeof(Input))!=40) throw new PlatformNotSupportedException("Validated x64 INPUT layout required");
        target=expectedWindow;processId=expectedProcess;
        using(var process=System.Diagnostics.Process.GetProcessById((int)expectedProcess)) {
            if(!String.Equals(process.ProcessName,"Rhino",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Target process is not Rhino");
            string path=process.MainModule.FileName;
            if(System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileMajorPart!=8) throw new ArgumentException("Target is not Rhino 8");
        }
    }
    public bool TargetFocused() {
        uint pid;IntPtr current=GetForegroundWindow();GetWindowThreadProcessId(current,out pid);
        return current==target && pid==processId;
    }
    public bool KeyAlreadyDown(int usage) {
        uint vk=MapVirtualKey(WindowsKeyboardSink.ScanCode(usage),3);
        if(vk==0) throw new InvalidOperationException("Current keyboard layout did not map this scan code");
        return (GetAsyncKeyState((int)vk) & 0x8000)!=0;
    }
    public static Input BuildInput(ushort scan,bool down) {
        return new Input {Type=1,Data=new InputUnion {Keyboard=new KeyboardInput {
            VirtualKey=0,Scan=scan,Flags=down?8u:10u,Time=0,Extra=new UIntPtr(0x47363032u)}}};
    }
    public void Send(ushort scan,bool down) {
        // A final focus check reduces the race between scheduler and output.
        // Key releases must still be possible after focus changes for cleanup.
        if(down && !TargetFocused()) throw new InvalidOperationException("Target changed before output");
        if(SendInput(1,new[]{BuildInput(scan,down)},Marshal.SizeOf(typeof(Input)))!=1)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Windows did not accept the key event; integrity-level restrictions are one possible cause");
    }
}
