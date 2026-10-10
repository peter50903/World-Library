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
            _controlsWindow.PopupCentered();
        else
            _controlsWindow.GrabFocus();
        _openControlsButton.Text = "世界控制台已開啟（點此切回視窗）";
    }

    private void HideControlsWindow()
    {
        _controlsWindow.Hide();
        _openControlsButton.Text = "開啟世界控制台（獨立可縮放視窗）";
    }

    private void ResizeControlsWindow()
    {
        // Never shrink text below its normal legible size on a small screen. Wide
        // windows scale the controls up; the scroll container owns vertical overflow.
        _controlsWindow.ContentScaleFactor = Mathf.Clamp(
            _controlsWindow.Size.X / 740.0f, 1.0f, 1.35f);
    }
}
