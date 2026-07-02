using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SharpDX;
using SharpDX.DirectInput;

namespace XboxMetroLauncher.Input;

public sealed class ControllerInputService : IDisposable
{
    private const int ErrorSuccess = 0;
    private const ushort XinputGamepadDpadUp = 0x0001;
    private const ushort XinputGamepadDpadDown = 0x0002;
    private const ushort XinputGamepadDpadLeft = 0x0004;
    private const ushort XinputGamepadDpadRight = 0x0008;
    private const ushort XinputGamepadStart = 0x0010;
    private const ushort XinputGamepadBack = 0x0020;
    private const ushort XinputGamepadLeftShoulder = 0x0100;
    private const ushort XinputGamepadRightShoulder = 0x0200;
    private const ushort XinputGamepadA = 0x1000;
    private const ushort XinputGamepadB = 0x2000;
    private const ushort XinputGamepadX = 0x4000;
    private const ushort XinputGamepadY = 0x8000;
    private const short ThumbDeadzone = 16000;
    private const byte TriggerThreshold = 120;
    private const int DirectInputAxisCenter = 32767;
    private const int DirectInputThumbDeadzone = 14000;
    private const int DirectInputPovNeutral = -1;

    private readonly Timer _timer;
    private readonly Action<DashboardInputAction> _onAction;
    private readonly Func<bool> _isEnabled;
    private readonly Dictionary<DashboardInputAction, DateTimeOffset> _lastMoveTimes = [];
    private readonly DirectInput _directInput;
    private ushort _previousButtons;
    private XInputState _previousState;
    private JoystickState _previousDirectInputState = new();
    private DashboardInputAction? _previousStickAction;
    private DateTimeOffset _lastStickMove = DateTimeOffset.MinValue;
    private static readonly TimeSpan MoveRepeatDelay = TimeSpan.FromMilliseconds(185);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(95);
    private bool _isDispatching;
    private int _isPolling;
    private bool _guideButtonDown;
    private InputBackend _activeBackend;
    private Joystick? _directInputJoystick;
    private DirectInputButtonMap _directInputButtonMap = DirectInputButtonMap.Generic;
    private bool _isRunning;

    public ControllerInputService(Action<DashboardInputAction> onAction, Func<bool>? isEnabled = null)
    {
        _onAction = onAction;
        _isEnabled = isEnabled ?? (() => true);
        _directInput = new DirectInput();
        _timer = new Timer(OnTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public bool IsRunning => _isRunning;

    public void Start()
    {
        _isRunning = true;
        _timer.Change(TimeSpan.Zero, PollInterval);
    }

    public void Dispose()
    {
        _isRunning = false;
        _timer.Dispose();
        ResetDirectInputJoystick();
        _directInput.Dispose();
    }

    private void OnTick(object? state)
    {
        if (Interlocked.Exchange(ref _isPolling, 1) == 1)
        {
            return;
        }

        try
        {
            PollController();
        }
        catch (Exception ex)
        {
            App.LogException(ex, "ControllerInputService.OnTick");
            ResetInputState();
        }
        finally
        {
            Interlocked.Exchange(ref _isPolling, 0);
        }
    }

    private void PollController()
    {
        if (!_isEnabled())
        {
            ResetInputState();
            return;
        }

        if (TryPollXInput(out var xinputState))
        {
            ProcessXInputState(xinputState);
            return;
        }

        if (TryPollDirectInput(out var directInputState))
        {
            ProcessDirectInputState(directInputState);
            return;
        }

        ResetInputState();
    }

    private bool TryPollXInput(out XInputState state)
    {
        try
        {
            if (XInputGetState(0, out state) == ErrorSuccess)
            {
                SwitchBackend(InputBackend.XInput);
                return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SEHException)
        {
            App.LogException(ex, "ControllerInputService.XInput");
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        state = default;
        return false;
    }

    private bool TryPollDirectInput(out JoystickState state)
    {
        state = new JoystickState();

        try
        {
            var joystick = EnsureDirectInputJoystick();
            if (joystick is null)
            {
                return false;
            }

            try
            {
                joystick.Poll();
                state = joystick.GetCurrentState();
            }
            catch (SharpDXException ex) when (ex.ResultCode == ResultCode.NotAcquired || ex.ResultCode == ResultCode.InputLost)
            {
                joystick.Acquire();
                joystick.Poll();
                state = joystick.GetCurrentState();
            }

            SwitchBackend(InputBackend.DirectInput);
            return true;
        }
        catch (Exception ex)
        {
            App.LogException(ex, "ControllerInputService.DirectInput");
            ResetDirectInputJoystick();
            return false;
        }
    }

    private Joystick? EnsureDirectInputJoystick()
    {
        if (_directInputJoystick is not null)
        {
            return _directInputJoystick;
        }

        var device = _directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly).FirstOrDefault()
                     ?? _directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly).FirstOrDefault();

        if (device is null)
        {
            return null;
        }

        var joystick = new Joystick(_directInput, device.InstanceGuid);
        joystick.Properties.BufferSize = 16;
        joystick.Acquire();
        _directInputJoystick = joystick;
        _directInputButtonMap = DirectInputButtonMap.FromProductName(device.ProductName);
        return _directInputJoystick;
    }

    private void ProcessXInputState(XInputState state)
    {
        var buttons = state.Gamepad.Buttons;
        var guideCombo = (ushort)(XinputGamepadBack | XinputGamepadStart);
        var guideDown = (buttons & guideCombo) == guideCombo;
        if (guideDown)
        {
            if (!_guideButtonDown)
            {
                DispatchAction(DashboardInputAction.Guide);
            }

            _guideButtonDown = true;
            _previousStickAction = null;
            _previousButtons = buttons;
            _previousState = state;
            return;
        }

        _guideButtonDown = false;
        var handledMove = FireDpadMove(buttons);
        FireOnPress(buttons, XinputGamepadA, DashboardInputAction.Activate);
        FireOnPress(buttons, XinputGamepadB, DashboardInputAction.Back);
        FireOnPress(buttons, XinputGamepadX, DashboardInputAction.Details);
        FireOnPress(buttons, XinputGamepadY, DashboardInputAction.Search);
        FireOnPress(buttons, XinputGamepadStart, DashboardInputAction.Options);
        FireOnPress(buttons, XinputGamepadBack, DashboardInputAction.Back);
        FireOnPress(buttons, XinputGamepadLeftShoulder, DashboardInputAction.PreviousTab);
        FireOnPress(buttons, XinputGamepadRightShoulder, DashboardInputAction.NextTab);
        FireOnTriggerPress(state.Gamepad.LeftTrigger, _previousState.Gamepad.LeftTrigger, DashboardInputAction.LeftTrigger);
        FireOnTriggerPress(state.Gamepad.RightTrigger, _previousState.Gamepad.RightTrigger, DashboardInputAction.RightTrigger);

        var stickAction = GetStickAction(state.Gamepad.LeftThumbX, state.Gamepad.LeftThumbY);
        if (!handledMove && stickAction is not null && ShouldFireStick(stickAction.Value))
        {
            DispatchAction(stickAction.Value);
        }

        _previousStickAction = stickAction;
        _previousButtons = buttons;
        _previousState = state;
    }

    private void ProcessDirectInputState(JoystickState state)
    {
        var guideDown = IsDirectInputGuideDown(state);
        if (guideDown)
        {
            if (!_guideButtonDown)
            {
                DispatchAction(DashboardInputAction.Guide);
            }

            _guideButtonDown = true;
            _previousStickAction = null;
            _previousDirectInputState = state;
            return;
        }

        _guideButtonDown = false;
        var handledMove = FireDirectInputPovMove(state);
        FireOnDirectInputPress(state, _directInputButtonMap.Activate, DashboardInputAction.Activate);
        FireOnDirectInputPress(state, _directInputButtonMap.Back, DashboardInputAction.Back);
        FireOnDirectInputPress(state, _directInputButtonMap.Details, DashboardInputAction.Details);
        FireOnDirectInputPress(state, _directInputButtonMap.Search, DashboardInputAction.Search);
        FireOnDirectInputPress(state, _directInputButtonMap.Options, DashboardInputAction.Options);
        FireOnDirectInputPress(state, _directInputButtonMap.SecondaryBack, DashboardInputAction.Back);
        FireOnDirectInputPress(state, _directInputButtonMap.PreviousTab, DashboardInputAction.PreviousTab);
        FireOnDirectInputPress(state, _directInputButtonMap.NextTab, DashboardInputAction.NextTab);
        FireOnDirectInputPress(state, _directInputButtonMap.LeftTrigger, DashboardInputAction.LeftTrigger);
        FireOnDirectInputPress(state, _directInputButtonMap.RightTrigger, DashboardInputAction.RightTrigger);

        var stickAction = GetDirectInputStickAction(state.X, state.Y);
        if (!handledMove && stickAction is not null && ShouldFireStick(stickAction.Value))
        {
            DispatchAction(stickAction.Value);
        }

        _previousStickAction = stickAction;
        _previousDirectInputState = state;
    }

    private bool FireDpadMove(ushort buttons)
    {
        var action = GetDpadAction(buttons);
        if (action is null)
        {
            return false;
        }

        var mask = GetButtonMask(action.Value);
        FireMoveWhileDown(buttons, mask, action.Value);
        return true;
    }

    private bool FireDirectInputPovMove(JoystickState state)
    {
        var action = GetDirectInputPovAction(state);
        if (action is null)
        {
            return false;
        }

        FireDirectInputMoveWhileDown(state, action.Value);
        return true;
    }

    private void FireOnPress(ushort buttons, ushort mask, DashboardInputAction action)
    {
        if ((buttons & mask) != 0 && (_previousButtons & mask) == 0)
        {
            DispatchAction(action);
        }
    }

    private void FireOnDirectInputPress(JoystickState state, int buttonIndex, DashboardInputAction action)
    {
        if (IsDirectInputButtonDown(state, buttonIndex) && !IsDirectInputButtonDown(_previousDirectInputState, buttonIndex))
        {
            DispatchAction(action);
        }
    }

    private void FireOnTriggerPress(byte value, byte previousValue, DashboardInputAction action)
    {
        if (value >= TriggerThreshold && previousValue < TriggerThreshold)
        {
            DispatchAction(action);
        }
    }

    private void FireMoveWhileDown(ushort buttons, ushort mask, DashboardInputAction action)
    {
        if ((buttons & mask) == 0)
        {
            return;
        }

        var isNewPress = (_previousButtons & mask) == 0;
        if (isNewPress)
        {
            _lastMoveTimes[action] = DateTimeOffset.UtcNow;
        }
        else if (!ShouldRepeat(action))
        {
            return;
        }

        DispatchAction(action);
    }

    private void FireDirectInputMoveWhileDown(JoystickState state, DashboardInputAction action)
    {
        var previousAction = GetDirectInputPovAction(_previousDirectInputState);
        var isNewPress = previousAction != action;
        if (isNewPress)
        {
            _lastMoveTimes[action] = DateTimeOffset.UtcNow;
        }
        else if (!ShouldRepeat(action))
        {
            return;
        }

        DispatchAction(action);
    }

    private bool ShouldRepeat(DashboardInputAction action)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastMoveTimes.TryGetValue(action, out var lastMove) && now - lastMove < MoveRepeatDelay)
        {
            return false;
        }

        _lastMoveTimes[action] = now;
        return true;
    }

    private bool ShouldFireStick(DashboardInputAction action)
    {
        var now = DateTimeOffset.UtcNow;
        if (_previousStickAction != action || now - _lastStickMove >= MoveRepeatDelay)
        {
            _lastStickMove = now;
            return true;
        }

        return false;
    }

    private void DispatchAction(DashboardInputAction action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => DispatchActionOnUiThread(action)), DispatcherPriority.Send);
            return;
        }

        DispatchActionOnUiThread(action);
    }

    private void DispatchActionOnUiThread(DashboardInputAction action)
    {
        if (_isDispatching)
        {
            return;
        }

        try
        {
            _isDispatching = true;
            _onAction(action);
        }
        catch (Exception ex)
        {
            App.LogException(ex, "ControllerInputService.DispatchAction");
        }
        finally
        {
            _isDispatching = false;
        }
    }

    private static DashboardInputAction? GetDpadAction(ushort buttons)
    {
        var left = (buttons & XinputGamepadDpadLeft) != 0;
        var right = (buttons & XinputGamepadDpadRight) != 0;
        var up = (buttons & XinputGamepadDpadUp) != 0;
        var down = (buttons & XinputGamepadDpadDown) != 0;

        if (left == right && up == down)
        {
            return null;
        }

        if (left != right)
        {
            return left ? DashboardInputAction.MoveLeft : DashboardInputAction.MoveRight;
        }

        return up ? DashboardInputAction.MoveUp : DashboardInputAction.MoveDown;
    }

    private static DashboardInputAction? GetDirectInputPovAction(JoystickState state)
    {
        var pov = state.PointOfViewControllers.FirstOrDefault(DirectInputPovNeutral);
        if (pov == DirectInputPovNeutral)
        {
            return null;
        }

        if (pov is > 31500 or < 4500)
        {
            return DashboardInputAction.MoveUp;
        }

        if (pov is >= 4500 and < 13500)
        {
            return DashboardInputAction.MoveRight;
        }

        if (pov is >= 13500 and < 22500)
        {
            return DashboardInputAction.MoveDown;
        }

        return DashboardInputAction.MoveLeft;
    }

    private static ushort GetButtonMask(DashboardInputAction action)
        => action switch
        {
            DashboardInputAction.MoveLeft => XinputGamepadDpadLeft,
            DashboardInputAction.MoveRight => XinputGamepadDpadRight,
            DashboardInputAction.MoveUp => XinputGamepadDpadUp,
            DashboardInputAction.MoveDown => XinputGamepadDpadDown,
            _ => 0
        };

    private void ResetInputState()
    {
        _previousButtons = 0;
        _previousState = default;
        _previousDirectInputState = new JoystickState();
        _previousStickAction = null;
        _guideButtonDown = false;
        _activeBackend = InputBackend.None;
        _lastMoveTimes.Clear();
        ResetDirectInputJoystick();
    }

    private void ResetDirectInputJoystick()
    {
        try
        {
            _directInputJoystick?.Unacquire();
            _directInputJoystick?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _directInputJoystick = null;
        }
    }

    private static DashboardInputAction? GetStickAction(short x, short y)
    {
        var absX = Math.Abs((int)x);
        var absY = Math.Abs((int)y);

        if (absX < ThumbDeadzone && absY < ThumbDeadzone)
        {
            return null;
        }

        if (absX > absY)
        {
            return x > 0 ? DashboardInputAction.MoveRight : DashboardInputAction.MoveLeft;
        }

        return y > 0 ? DashboardInputAction.MoveUp : DashboardInputAction.MoveDown;
    }

    private static DashboardInputAction? GetDirectInputStickAction(int x, int y)
    {
        var deltaX = x - DirectInputAxisCenter;
        var deltaY = y - DirectInputAxisCenter;
        var absX = Math.Abs(deltaX);
        var absY = Math.Abs(deltaY);

        if (absX < DirectInputThumbDeadzone && absY < DirectInputThumbDeadzone)
        {
            return null;
        }

        if (absX > absY)
        {
            return deltaX > 0 ? DashboardInputAction.MoveRight : DashboardInputAction.MoveLeft;
        }

        return deltaY > 0 ? DashboardInputAction.MoveDown : DashboardInputAction.MoveUp;
    }

    private void SwitchBackend(InputBackend backend)
    {
        if (_activeBackend == backend)
        {
            return;
        }

        _activeBackend = backend;
        _previousButtons = 0;
        _previousState = default;
        _previousDirectInputState = new JoystickState();
        _previousStickAction = null;
        _guideButtonDown = false;
        _lastMoveTimes.Clear();
    }

    private bool IsDirectInputGuideDown(JoystickState state)
        => IsDirectInputButtonDown(state, _directInputButtonMap.Guide)
           || (IsDirectInputButtonDown(state, _directInputButtonMap.SecondaryBack) && IsDirectInputButtonDown(state, _directInputButtonMap.Options));

    private static bool IsDirectInputButtonDown(JoystickState state, int index)
        => state.Buttons is { Length: > 0 } buttons
           && index >= 0
           && index < buttons.Length
           && buttons[index];

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState(int dwUserIndex, out XInputState pState);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    private enum InputBackend
    {
        None,
        XInput,
        DirectInput
    }

    private readonly record struct DirectInputButtonMap(
        int Activate,
        int Back,
        int Details,
        int Search,
        int PreviousTab,
        int NextTab,
        int LeftTrigger,
        int RightTrigger,
        int SecondaryBack,
        int Options,
        int Guide)
    {
        public static DirectInputButtonMap PlayStation { get; } = new(
            Activate: 1,
            Back: 2,
            Details: 0,
            Search: 3,
            PreviousTab: 4,
            NextTab: 5,
            LeftTrigger: 6,
            RightTrigger: 7,
            SecondaryBack: 8,
            Options: 9,
            Guide: 12);

        public static DirectInputButtonMap Switch { get; } = new(
            Activate: 0,
            Back: 1,
            Details: 2,
            Search: 3,
            PreviousTab: 4,
            NextTab: 5,
            LeftTrigger: 6,
            RightTrigger: 7,
            SecondaryBack: 8,
            Options: 9,
            Guide: 12);

        public static DirectInputButtonMap Generic { get; } = new(
            Activate: 0,
            Back: 1,
            Details: 2,
            Search: 3,
            PreviousTab: 4,
            NextTab: 5,
            LeftTrigger: 6,
            RightTrigger: 7,
            SecondaryBack: 8,
            Options: 9,
            Guide: 12);

        public static DirectInputButtonMap FromProductName(string? productName)
        {
            var name = productName ?? string.Empty;
            if (name.Contains("playstation", StringComparison.OrdinalIgnoreCase)
                || name.Contains("dualshock", StringComparison.OrdinalIgnoreCase)
                || name.Contains("dualsense", StringComparison.OrdinalIgnoreCase)
                || name.Contains("wireless controller", StringComparison.OrdinalIgnoreCase))
            {
                return PlayStation;
            }

            if (name.Contains("switch", StringComparison.OrdinalIgnoreCase)
                || name.Contains("joy-con", StringComparison.OrdinalIgnoreCase)
                || name.Contains("joycon", StringComparison.OrdinalIgnoreCase)
                || name.Contains("pro controller", StringComparison.OrdinalIgnoreCase))
            {
                return Switch;
            }

            return Generic;
        }
    }
}
