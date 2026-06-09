using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace XboxMetroLauncher.Input;

public sealed class GlobalHotkeyService : IDisposable
{
	private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

	private struct Kbdllhookstruct
	{
		public int VkCode;

		public int ScanCode;

		public int Flags;

		public int Time;

		public nint ExtraInfo;
	}

	private const int HotkeyId = 864;

	private const uint ModShift = 4u;

	private const uint ModWin = 8u;

	private const int WmHotkey = 786;

	private const int WhKeyboardLl = 13;

	private const int WmKeyDown = 256;

	private const int WmKeyUp = 257;

	private const int WmSysKeyDown = 260;

	private const int WmSysKeyUp = 261;

	private const int VkLShift = 160;

	private const int VkLControl = 162;

	private const int VkLWin = 91;

	private const int VkRWin = 92;

	private HwndSource? _source;

	private LowLevelKeyboardProc? _keyboardProc;

	private nint _keyboardHook;

	private nint _handle;

	private bool _registered;

	private bool _leftShiftDown;

	private bool _leftCtrlDown;

	private bool _winDown;

	private bool _hotkeyDown;

	private DateTimeOffset _lastHotkeyRaised = DateTimeOffset.MinValue;

	public event EventHandler? HotkeyPressed;

	public void Register(Window window)
	{
		if (!_registered)
		{
			_handle = new WindowInteropHelper(window).Handle;
			if (_handle != IntPtr.Zero)
			{
				_source = HwndSource.FromHwnd(_handle);
				_source?.AddHook(WndProc);
				uint vk = (uint)KeyInterop.VirtualKeyFromKey((Key)118);
				_registered = RegisterHotKey(_handle, 864, 12u, vk);
				InstallKeyboardHook();
			}
		}
	}

	public void Dispose()
	{
		if (_registered)
		{
			UnregisterHotKey(_handle, 864);
			_registered = false;
		}
		_source?.RemoveHook(WndProc);
		_source = null;
		if (_keyboardHook != IntPtr.Zero)
		{
			UnhookWindowsHookEx(_keyboardHook);
			_keyboardHook = IntPtr.Zero;
		}
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg == 786 && ((IntPtr)wParam).ToInt32() == 864)
		{
			handled = true;
			RaiseHotkeyPressed();
		}
		return IntPtr.Zero;
	}

	private void InstallKeyboardHook()
	{
		if (_keyboardHook == IntPtr.Zero)
		{
			_keyboardProc = KeyboardHookCallback;
			_keyboardHook = SetWindowsHookEx(13, _keyboardProc, GetModuleHandle(null), 0u);
		}
	}

	private nint KeyboardHookCallback(int nCode, nint wParam, nint lParam)
	{
		if (nCode >= 0)
		{
			int num = ((IntPtr)wParam).ToInt32();
			bool flag = (num == 256 || num == 260);
			bool flag2 = flag;
			flag = (num == 257 || num == 261);
			bool flag3 = flag;
			if (flag2 || flag3)
			{
				UpdateModifierState(Marshal.PtrToStructure<Kbdllhookstruct>(lParam).VkCode, flag2);
			}
		}
		return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
	}

	private void UpdateModifierState(int virtualKey, bool isDown)
	{
		switch (virtualKey)
		{
		case 160:
			_leftShiftDown = isDown;
			break;
		case 162:
			_leftCtrlDown = isDown;
			break;
		case 91:
		case 92:
			_winDown = isDown;
			break;
		}
		if (_leftShiftDown && _leftCtrlDown && _winDown)
		{
			if (!_hotkeyDown)
			{
				_hotkeyDown = true;
				Application current = Application.Current;
				if (current != null)
				{
					((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)new Action(RaiseHotkeyPressed), Array.Empty<object>());
				}
			}
		}
		else
		{
			_hotkeyDown = false;
		}
	}

	private void RaiseHotkeyPressed()
	{
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		if (!(utcNow - _lastHotkeyRaised < TimeSpan.FromMilliseconds(350.0)))
		{
			_lastHotkeyRaised = utcNow;
			this.HotkeyPressed?.Invoke(this, EventArgs.Empty);
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnregisterHotKey(nint hWnd, int id);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll")]
	private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern nint GetModuleHandle(string? lpModuleName);
}
