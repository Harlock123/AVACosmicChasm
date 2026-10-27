using Avalonia.Input;

namespace AVACosmicChasm.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: turn left or right, thrust, fire, and the shield (which is
/// the drill too). Held keys are tracked by the physical key as well, so a Shift let go as Caps Lock is let go.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    /// <summary>
    /// Left and right (or A and D) turn the ship, up (or W) thrusts; Space, Z or Ctrl fire; down (or S), X or Shift
    /// raise the shield, which drills through a plugged doorway when it's pushed against one. On a gamepad: the left
    /// stick or D-pad turns and pushes up to thrust (or B, or the right trigger), A fires, and X, the left trigger or
    /// down on the stick raises the shield.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int turn = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            if (turn == 0) turn = pad.DPadLeft || pad.LeftStickX < -0.35f ? -1 : pad.DPadRight || pad.LeftStickX > 0.35f ? 1 : 0;
            bool thrust = Any(Key.Up, Key.W, Key.NumPad8) || pad.DPadUp || pad.LeftStickY < -0.5f || pad.ButtonB || pad.RightTrigger > 0.5f;
            bool fire = Any(Key.Space, Key.Z, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonA;
            bool shield = Any(Key.Down, Key.S, Key.NumPad2, Key.X, Key.LeftShift, Key.RightShift) || pad.DPadDown || pad.LeftStickY > 0.5f
                || pad.ButtonX || pad.LeftTrigger > 0.5f;
            return new Controls(turn, thrust, fire, shield);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
