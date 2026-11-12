"""Passive Windows input recorder. No HID output or input injection.

Records only VID_046D&PID_C537 events, ignoring motion and other keyboards.
Creates a message-only window and exits after the requested duration. Optional
digit comparison observes (and always passes through) only keyboard digits 1–3.
"""
import argparse
import ctypes as C
from ctypes import wintypes as W
import datetime
import json
from pathlib import Path
import struct
import time

TARGET = 'VID_046D&PID_C537'
UINT_ERROR = 0xFFFFFFFF

def decode_payload(kind, data):
    if kind == 0:
        if len(data) < 24:
            raise ValueError('Truncated RAWMOUSE')
        flags, value = struct.unpack_from('<HH', data, 4)
        if not flags:
            return None  # Do not log pointer movement.
        return {'kind': 'mouse', 'button_flags': flags, 'button_data': value}
    if kind == 1:
        if len(data) < 16:
            raise ValueError('Truncated RAWKEYBOARD')
        scan, flags, reserved, vk, message, extra = struct.unpack_from('<HHHHII', data)
        return {'kind': 'keyboard', 'scan_code': scan, 'flags': flags,
                'virtual_key': vk, 'message': message, 'is_up': bool(flags & 1),
                'extra_information': extra}
    if kind == 2:
        if len(data) < 8:
            raise ValueError('Truncated RAWHID header')
        size, count = struct.unpack_from('<II', data)
        if size * count > len(data) - 8 or size * count > 65536:
            raise ValueError('Truncated or oversized RAWHID payload')
        return {'kind': 'hid', 'reports': [data[8+i*size:8+(i+1)*size].hex() for i in range(count)]}
    raise ValueError('Unknown Raw Input type')

def is_target(name):
    return TARGET in name.upper()

def capture(seconds, compare_digits=False):
    if not 1 <= seconds <= 120:
        raise ValueError('Capture duration must be 1–120 seconds')
    user = C.WinDLL('user32', use_last_error=True)
    kernel = C.WinDLL('kernel32', use_last_error=True)
    LRESULT = C.c_ssize_t
    WPARAM = C.c_size_t
    LPARAM = C.c_ssize_t
    PROC = C.WINFUNCTYPE(LRESULT, W.HWND, W.UINT, WPARAM, LPARAM)
    HOOKPROC = C.WINFUNCTYPE(LRESULT, C.c_int, WPARAM, LPARAM)

    class DEVICE(C.Structure):
        _fields_ = [('handle', W.HANDLE), ('kind', W.DWORD)]
    class HEADER(C.Structure):
        _fields_ = [('kind', W.DWORD), ('size', W.DWORD), ('device', W.HANDLE), ('param', WPARAM)]
    class REG(C.Structure):
        _fields_ = [('page', W.USHORT), ('usage', W.USHORT), ('flags', W.DWORD), ('window', W.HWND)]
    class CLASS(C.Structure):
        _fields_ = [('style', W.UINT), ('proc', PROC), ('class_extra', C.c_int),
                    ('window_extra', C.c_int), ('instance', W.HINSTANCE), ('icon', W.HICON),
                    ('cursor', W.HANDLE), ('background', W.HBRUSH),
                    ('menu', W.LPCWSTR), ('name', W.LPCWSTR)]
    class KEYHOOK(C.Structure):
        _fields_ = [('vk', W.DWORD), ('scan', W.DWORD), ('flags', W.DWORD),
                    ('tick', W.DWORD), ('extra', WPARAM)]

    user.GetRawInputDeviceList.argtypes = [C.POINTER(DEVICE), C.POINTER(W.UINT), W.UINT]
    user.GetRawInputDeviceList.restype = W.UINT
    user.GetRawInputDeviceInfoW.argtypes = [W.HANDLE, W.UINT, W.LPVOID, C.POINTER(W.UINT)]
    user.GetRawInputDeviceInfoW.restype = W.UINT
    user.GetRawInputData.argtypes = [W.HANDLE, W.UINT, W.LPVOID, C.POINTER(W.UINT), W.UINT]
    user.GetRawInputData.restype = W.UINT
    user.RegisterRawInputDevices.argtypes = [C.POINTER(REG), W.UINT, W.UINT]
    user.RegisterRawInputDevices.restype = W.BOOL
    user.RegisterClassW.argtypes = [C.POINTER(CLASS)]
    user.RegisterClassW.restype = W.ATOM
    user.CreateWindowExW.argtypes = [W.DWORD, W.LPCWSTR, W.LPCWSTR, W.DWORD,
        C.c_int, C.c_int, C.c_int, C.c_int, W.HWND, W.HMENU, W.HINSTANCE, W.LPVOID]
    user.CreateWindowExW.restype = W.HWND
    user.DefWindowProcW.argtypes = [W.HWND, W.UINT, WPARAM, LPARAM]
    user.DefWindowProcW.restype = LRESULT
    user.GetMessageW.argtypes = [C.POINTER(W.MSG), W.HWND, W.UINT, W.UINT]
    user.GetMessageW.restype = W.BOOL
    user.TranslateMessage.argtypes = [C.POINTER(W.MSG)]
    user.DispatchMessageW.argtypes = [C.POINTER(W.MSG)]
    user.DispatchMessageW.restype = LRESULT
    user.SetTimer.argtypes = [W.HWND, WPARAM, W.UINT, W.LPVOID]
    user.SetTimer.restype = WPARAM
    user.KillTimer.argtypes = [W.HWND, WPARAM]
    user.DestroyWindow.argtypes = [W.HWND]
    user.UnregisterClassW.argtypes = [W.LPCWSTR, W.HINSTANCE]
    kernel.GetModuleHandleW.argtypes = [W.LPCWSTR]
    kernel.GetModuleHandleW.restype = W.HMODULE
    user.SetWindowsHookExW.argtypes = [C.c_int, HOOKPROC, W.HINSTANCE, W.DWORD]
    user.SetWindowsHookExW.restype = W.HANDLE
    user.CallNextHookEx.argtypes = [W.HANDLE, C.c_int, WPARAM, LPARAM]
    user.CallNextHookEx.restype = LRESULT
    user.UnhookWindowsHookEx.argtypes = [W.HANDLE]
    user.UnhookWindowsHookEx.restype = W.BOOL

    def device_name(handle):
        size = W.UINT()
        if user.GetRawInputDeviceInfoW(handle, 0x20000007, None, C.byref(size)) == UINT_ERROR:
            return ''
        value = C.create_unicode_buffer(size.value + 1)
        if user.GetRawInputDeviceInfoW(handle, 0x20000007, value, C.byref(size)) == UINT_ERROR:
            return ''
        return value.value

    count = W.UINT()
    if user.GetRawInputDeviceList(None, C.byref(count), C.sizeof(DEVICE)) == UINT_ERROR:
        raise C.WinError(C.get_last_error())
    devices = (DEVICE * count.value)()
    n = user.GetRawInputDeviceList(devices, C.byref(count), C.sizeof(DEVICE))
    if n == UINT_ERROR:
        raise C.WinError(C.get_last_error())
    names = {d.handle: device_name(d.handle) for d in devices[:n]}
    found = [{'path': name, 'kind': d.kind} for d in devices[:n]
             if is_target(name := names[d.handle])]
    result = {'started_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'duration_requested_seconds': seconds, 'devices': found, 'events': [], 'errors': [],
              'policy': 'passive Raw Input; target receiver only; no suppression or injection',
              'physical_button_order': ['G4','G5','G6','G7','G8','G9'],
              'physical_labels_verified': False}
    if compare_digits:
        result.update(policy='passive digit comparison; 1–3 from all keyboards plus G602 reports; no suppression or injection',
                      comparison_order=['mouse G7','mouse G8','mouse G9','keyboard 1','keyboard 2','keyboard 3'],
                      keyboard_devices=[{'path':names[d.handle]} for d in devices[:n] if d.kind == 1],
                      hook_events=[])
    if not found:
        result['status'] = 'receiver_not_visible'
        return result
    started = time.monotonic()

    @PROC
    def window_proc(hwnd, msg, wp, lp):
        try:
            if msg == 0x00FF:  # WM_INPUT
                size = W.UINT()
                if user.GetRawInputData(lp, 0x10000003, None, C.byref(size), C.sizeof(HEADER)) == UINT_ERROR:
                    raise C.WinError(C.get_last_error())
                if not C.sizeof(HEADER) <= size.value <= 65536:
                    raise ValueError('Invalid Raw Input packet length')
                buffer = C.create_string_buffer(size.value)
                got = user.GetRawInputData(lp, 0x10000003, buffer, C.byref(size), C.sizeof(HEADER))
                if got == UINT_ERROR:
                    raise C.WinError(C.get_last_error())
                header = HEADER.from_buffer_copy(buffer)
                name = names.get(header.device)
                if name is None:
                    name = names[header.device] = device_name(header.device)
                if is_target(name) or (compare_digits and header.kind == 1):
                    event = decode_payload(header.kind, buffer.raw[C.sizeof(HEADER):got])
                    if compare_digits and event and event['kind'] == 'keyboard' and event['virtual_key'] not in (49,50,51):
                        event = None
                    if event and len(result['events']) < 10000:
                        event.update(elapsed_seconds=round(time.monotonic()-started, 4), device=name)
                        result['events'].append(event)
            elif msg == 0x0113:  # WM_TIMER, wakes the blocking message loop only to stop it.
                if time.monotonic() - started >= seconds:
                    user.PostQuitMessage(0)
        except Exception as exc:
            if len(result['errors']) < 50:
                result['errors'].append(str(exc))
        return user.DefWindowProcW(hwnd, msg, wp, lp)

    instance = kernel.GetModuleHandleW(None)
    name = 'G602PassiveInputMonitor'
    wc = CLASS(0, window_proc, 0, 0, instance, None, None, None, None, name)
    if not user.RegisterClassW(C.byref(wc)):
        raise C.WinError(C.get_last_error())
    hwnd = user.CreateWindowExW(0, name, name, 0, 0, 0, 0, 0, W.HWND(-3), None, instance, None)
    if not hwnd:
        user.UnregisterClassW(name, instance)
        raise C.WinError(C.get_last_error())
    usages = [(1,2), (1,6), (1,0x80), (12,1), (0xFF00,1), (0xFF00,2), (0xFF80,0x80)]
    regs = (REG * len(usages))(*(REG(p,u,0x100,hwnd) for p,u in usages))
    hook = None

    @HOOKPROC
    def keyboard_proc(code, wp, lp):
        try:
            if code >= 0:
                key = C.cast(lp, C.POINTER(KEYHOOK)).contents
                if key.vk in (49,50,51) and len(result['hook_events']) < 10000:
                    result['hook_events'].append({'virtual_key':key.vk, 'scan_code':key.scan,
                        'flags':key.flags, 'is_up':bool(key.flags & 0x80),
                        'injected':bool(key.flags & 0x10), 'extra_information':key.extra,
                        'windows_tick':key.tick,
                        'elapsed_seconds':round(time.monotonic()-started, 4)})
        except Exception as exc:
            if len(result['errors']) < 50:
                result['errors'].append(str(exc))
        # Observation only: every event continues to Windows unchanged.
        return user.CallNextHookEx(None, code, wp, lp)

    try:
        if not user.RegisterRawInputDevices(regs, len(regs), C.sizeof(REG)):
            raise C.WinError(C.get_last_error())
        if not user.SetTimer(hwnd, 1, 250, None):
            raise C.WinError(C.get_last_error())
        if compare_digits:
            hook = user.SetWindowsHookExW(13, keyboard_proc, instance, 0)
            result['hook_installed'] = bool(hook)
            if not hook:
                result['hook_error'] = str(C.WinError(C.get_last_error()))
        print('RECORDING: digit comparison' if compare_digits else 'RECORDING: receiver input only', flush=True)
        message = W.MSG()
        while True:
            status = user.GetMessageW(C.byref(message), None, 0, 0)
            if status == -1:
                raise C.WinError(C.get_last_error())
            if not status:
                break
            user.TranslateMessage(C.byref(message))
            user.DispatchMessageW(C.byref(message))
        result['status'] = 'capture_complete'
        result['duration_actual_seconds'] = round(time.monotonic()-started, 3)
    finally:
        if hook:
            result['hook_removed'] = bool(user.UnhookWindowsHookEx(hook))
        remove = (REG * len(usages))(*(REG(p,u,1,None) for p,u in usages))
        user.RegisterRawInputDevices(remove, len(remove), C.sizeof(REG))
        user.KillTimer(hwnd, 1)
        user.DestroyWindow(hwnd)
        user.UnregisterClassW(name, instance)
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--seconds', type=int, default=35)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--compare-digits', action='store_true', help='Observe only 1–3 across keyboards and a pass-through keyboard hook')
    args = parser.parse_args()
    try:
        result = capture(args.seconds, args.compare_digits)
    except Exception as exc:
        result = {'status': 'capture_failed', 'error': str(exc)}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(result['status'], 'events:', len(result.get('events', [])), flush=True)
