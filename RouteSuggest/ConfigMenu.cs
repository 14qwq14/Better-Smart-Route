using Godot;
using System;
using System.Linq;

namespace RouteSuggest;

public partial class ConfigMenu : CanvasLayer
{
    private Panel _mainPanel;
    private VBoxContainer _listContainer;

    public override void _Ready()
    {
        Layer = 100; // UI Layer on top

        _mainPanel = new Panel();
        _mainPanel.CustomMinimumSize = new Vector2(500, 400);
        _mainPanel.SetAnchorsPreset(Control.LayoutPreset.Center, true);
        _mainPanel.Hide();
        AddChild(_mainPanel);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 10);
        
        // Add padding
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        
        margin.AddChild(vbox);
        _mainPanel.AddChild(margin);

        var title = new Label();
        title.Text = "RouteSuggest Configuration (F10)";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        _listContainer = new VBoxContainer();
        
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_listContainer);
        vbox.AddChild(scroll);

        var btnContainer = new HBoxContainer();
        btnContainer.Alignment = BoxContainer.AlignmentMode.Center;
        
        var saveBtn = new Button();
        saveBtn.Text = "Save & Apply";
        saveBtn.Pressed += SaveAndReload;
        btnContainer.AddChild(saveBtn);

        var closeBtn = new Button();
        closeBtn.Text = "Close";
        closeBtn.Pressed += () => _mainPanel.Hide();
        btnContainer.AddChild(closeBtn);

        vbox.AddChild(btnContainer);
        
        RefreshList();
    }

    private void RefreshList()
    {
        foreach (Node child in _listContainer.GetChildren())
        {
            child.QueueFree();
        }

        var htHbox = new HBoxContainer();
        var htLabel = new Label { Text = "Highlight Type:" };
        htHbox.AddChild(htLabel);
        
        var htOption = new OptionButton();
        htOption.AddItem("One", 0);
        htOption.AddItem("All", 1);
        htOption.Selected = ConfigManager.CurrentHighlightType == HighlightType.One ? 0 : 1;
        htOption.ItemSelected += (idx) => {
            ConfigManager.CurrentHighlightType = idx == 0 ? HighlightType.One : HighlightType.All;
        };
        htHbox.AddChild(htOption);
        _listContainer.AddChild(htHbox);
        _listContainer.AddChild(new HSeparator());

        foreach (var config in ConfigManager.PathConfigs)
        {
            var hbox = new HBoxContainer();
            
            var cb = new CheckBox();
            cb.Text = config.Name;
            cb.ButtonPressed = config.Enabled;
            // Use closure correctly
            var cfgParams = config; 
            cb.Toggled += (pressed) => {
                cfgParams.Enabled = pressed;
            };
            hbox.AddChild(cb);

            var colorRect = new ColorRect();
            colorRect.CustomMinimumSize = new Vector2(20, 20);
            colorRect.Color = config.Color;
            hbox.AddChild(colorRect);

            _listContainer.AddChild(hbox);
        }
    }

    private void SaveAndReload()
    {
        ConfigManager.SaveConfiguration();
        RouteCalculator.InvalidateCache();
        RouteCalculator.UpdateBestPath();
        MapHighlighter.RequestHighlightOnMapOpen();
        _mainPanel.Hide();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.F10)
        {
            _mainPanel.Visible = !_mainPanel.Visible;
            if (_mainPanel.Visible)
            {
                // Refresh just in case it was edited manually
                ConfigManager.Initialize();
                RefreshList();
            }
            GetViewport().SetInputAsHandled();
        }
    }
}
