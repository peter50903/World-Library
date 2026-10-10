using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// #119: The world controls live in a real resizable Godot Window rather than
/// permanently occupying map width. This is TEMP presentation only; no Core mutation.
/// </summary>
public partial class ObserverRoot
{
    private Window _controlsWindow = null!;
    private Button _openControlsButton = null!;

    private void CreateControlsWindow(VBoxContainer heading)
    {
        _openControlsButton = new Button
        {
            Text = "開啟世界控制台（獨立可縮放視窗）",
            TooltipText = "另開視窗；可移動、縮放或關閉，不會遮擋地圖布局。",
            CustomMinimumSize = new Vector2(0, 42)
        };
        _openControlsButton.Pressed += ShowControlsWindow;
        heading.AddChild(_openControlsButton);

        _controlsWindow = new Window
        {
            Title = "世界模擬遊戲｜世界控制台（TEMP）",
            Visible = false,
            MinSize = new Vector2I(440, 400),
            Size = new Vector2I(750, 780),
            Transient = false,
            Exclusive = false
        };
        AddChild(_controlsWindow);
        _controlsWindow.CloseRequested += HideControlsWindow;
        _controlsWindow.SizeChanged += ResizeControlsWindow;
        ResizeControlsWindow();
    }

    private void ShowControlsWindow()
    {
        if (!_controlsWindow.Visible)
        {
            // The original PopupCentered() could cover the map even on a roomy
            // desktop. Prefer the space OUTSIDE the main map window instead.
            if (FitAndPlaceControlsWindow())
                _controlsWindow.Show();
            else
                _controlsWindow.PopupCentered(); // Headless/no usable screen geometry.
        }
        _controlsWindow.GrabFocus();
        _openControlsButton.Text = "世界控制台已開啟（點此切回視窗）";
    }

    /// <summary>
    /// Keep the separate, resizable control window entirely on the usable desktop.
    /// When possible dock it beside (rather than over) the main map window.
    /// On smaller desktops place it within the screen; the existing ScrollContainer
    /// preserves full access without shrinking the font below 100%.
    /// This changes Godot presentation only and never modifies Core world state.
    /// </summary>
    private bool FitAndPlaceControlsWindow()
    {
        var main = GetWindow();
        // Use the monitor containing the map, not an unrelated primary screen.
        var screen = DisplayServer.ScreenGetUsableRect(main.CurrentScreen);
        if (screen.Size.X <= 0 || screen.Size.Y <= 0)
            return false;

        const int margin = 12;
        var maxWidth = Math.Max(1, screen.Size.X - 2 * margin);
        var maxHeight = Math.Max(1, screen.Size.Y - 2 * margin);
        _controlsWindow.MinSize = new Vector2I(Math.Min(440, maxWidth), Math.Min(400, maxHeight));
        var width = Math.Min(_controlsWindow.Size.X, maxWidth);
        var height = Math.Min(_controlsWindow.Size.Y, maxHeight);

        var mainLeft = main.Position.X;
        var mainRight = main.Position.X + main.Size.X;
        var leftSpace = mainLeft - screen.Position.X - margin;
        var rightSpace = screen.Position.X + screen.Size.X - mainRight - margin;
        int x;

        // A window can only stay outside the map if its actual minimum readable
        // width fits there. If both sides fit, prefer the roomier side.
        if (Math.Max(leftSpace, rightSpace) >= _controlsWindow.MinSize.X)
        {
            if (rightSpace >= leftSpace)
            {
                width = Math.Min(width, rightSpace);
                x = mainRight + margin;
            }
            else
            {
                width = Math.Min(width, leftSpace);
                x = mainLeft - margin - width;
            }
        }
        else
        {
            // On a single small screen some overlap is unavoidable; always keep
            // the close/reopen controls and scrollable content within the desktop.
            x = screen.Position.X + screen.Size.X - margin - width;
        }

        var maxX = screen.Position.X + screen.Size.X - width;
        var maxY = screen.Position.Y + screen.Size.Y - height;
        _controlsWindow.Size = new Vector2I(width, height);
        _controlsWindow.Position = new Vector2I(
            Math.Clamp(x, screen.Position.X, maxX),
            Math.Clamp(main.Position.Y, screen.Position.Y, maxY));
        ResizeControlsWindow();
        return true;
    }

    private void HideControlsWindow()
    {
        _controlsWindow.Hide();
        _openControlsButton.Text = "開啟世界控制台（獨立可縮放視窗）";
    }

    private void VerifyControlsWindowSmoke()
    {
        if (_controlsWindow.Visible) throw new InvalidOperationException("Controls must initially be detached and hidden.");
        if (_hexMap.Size.X <= 0) throw new InvalidOperationException("Map did not fill its main window.");
        _openControlsButton.EmitSignal(BaseButton.SignalName.Pressed);
        if (!_controlsWindow.Visible) throw new InvalidOperationException("Controls button failed to open window.");
        _controlsWindow.Size = new Vector2I(1000, 650);
        ResizeControlsWindow();
        if (_controlsWindow.ContentScaleFactor < 1.1f)
            throw new InvalidOperationException("Large controls window did not increase content scale.");
        _controlsWindow.Size = new Vector2I(450, 410);
        ResizeControlsWindow();
        if (_controlsWindow.ContentScaleFactor < 1f)
            throw new InvalidOperationException("Controls became illegibly small.");
        HideControlsWindow();
        if (_controlsWindow.Visible) throw new InvalidOperationException("Close request failed.");
        ShowControlsWindow();
        if (!_controlsWindow.Visible) throw new InvalidOperationException("Controls cannot reopen.");
        HideControlsWindow();
        GD.Print("WORLD_SIM_WINDOW_OK: Detached=1 Resizable=1 Autoscale=1 Reopen=1");
    }

    private void ResizeControlsWindow()
    {
        // Never shrink text below its normal legible size on a small screen. Wide
        // windows scale the controls up; the scroll container owns vertical overflow.
        _controlsWindow.ContentScaleFactor = Mathf.Clamp(
            _controlsWindow.Size.X / 740.0f, 1.0f, 1.35f);
    }
}
