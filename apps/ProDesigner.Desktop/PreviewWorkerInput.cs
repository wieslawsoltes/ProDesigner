using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using ProDesigner.Core;

namespace ProDesigner.Desktop;

/// <summary>Owns remote pressed-state so pointer capture and modifier resets survive batching and focus loss.</summary>
internal sealed class PreviewWorkerInput
{
    private readonly HashSet<int> _buttons = [];
    private readonly HashSet<Key> _keys = [];
    private Point _position;
    public void Apply(Window window, PreviewInput input)
    {
        input.Validate();
        _position = new(input.X, input.Y);
        var modifiers = Raw(input.Modifiers);
        switch (input.Kind)
        {
            case PreviewInputKind.Move: window.MouseMove(_position, modifiers | Buttons()); break;
            case PreviewInputKind.Down:
                if (_buttons.Add(input.Button)) window.MouseDown(_position, Button(input.Button), modifiers | Buttons());
                break;
            case PreviewInputKind.Up:
                if (_buttons.Remove(input.Button)) window.MouseUp(_position, Button(input.Button), modifiers | Buttons());
                break;
            case PreviewInputKind.Wheel: window.MouseWheel(_position, new(input.DeltaX, input.DeltaY), modifiers | Buttons()); break;
            case PreviewInputKind.KeyDown:
            case PreviewInputKind.KeyUp:
                if (!Enum.TryParse<Key>(input.Key, out var key) || !Enum.IsDefined(key)) throw new InvalidDataException("Unknown key name.");
                if (input.Kind == PreviewInputKind.KeyDown) { _keys.Add(key); window.KeyPress(key, modifiers, PhysicalKey.None, null); }
                else { _keys.Remove(key); window.KeyRelease(key, modifiers, PhysicalKey.None, null); }
                break;
            case PreviewInputKind.Text: window.KeyTextInput(input.Text ?? ""); break;
            case PreviewInputKind.Reset: Reset(window); break;
        }
    }
    public void Reset(Window window)
    {
        _position = new(-10000, -10000);
        window.MouseMove(_position, Buttons());
        foreach (var button in _buttons.ToArray()) { _buttons.Remove(button); window.MouseUp(_position, Button(button), Buttons()); }
        foreach (var key in _keys) window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        _keys.Clear();
    }
    private RawInputModifiers Buttons() => (_buttons.Contains(0) ? RawInputModifiers.LeftMouseButton : 0) |
        (_buttons.Contains(1) ? RawInputModifiers.MiddleMouseButton : 0) | (_buttons.Contains(2) ? RawInputModifiers.RightMouseButton : 0);
    private static MouseButton Button(int value) => value switch { 0 => MouseButton.Left, 1 => MouseButton.Middle, _ => MouseButton.Right };
    private static RawInputModifiers Raw(PreviewModifiers value) => ((value & PreviewModifiers.Shift) != 0 ? RawInputModifiers.Shift : 0) |
        ((value & PreviewModifiers.Control) != 0 ? RawInputModifiers.Control : 0) | ((value & PreviewModifiers.Alt) != 0 ? RawInputModifiers.Alt : 0) |
        ((value & PreviewModifiers.Meta) != 0 ? RawInputModifiers.Meta : 0);
}
