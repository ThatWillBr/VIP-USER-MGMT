using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using VIP1132.Models;

namespace VIP1132.Services;

public sealed class ZoomUiConfigurator
{
    private readonly ZoomProfileReport _report = new();
    private AutomationElement? _settingsWindow;

    public ZoomProfileReport Apply()
    {
        try
        {
            var zoomPath = ZoomService.FindZoomExecutable()
                ?? throw new FileNotFoundException("Zoom.exe was not found after installation.");

            Process.Start(new ProcessStartInfo(zoomPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(zoomPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            });
            var mainWindow = WaitFor(
                () => FindTopLevelWindowWithDescendant("Settings", ControlType.Button),
                TimeSpan.FromSeconds(75));

            if (mainWindow is null)
                throw new InvalidOperationException("Zoom opened, but its main window did not become available.");

            var settingsButton = Find(mainWindow, "Settings", ControlType.Button);
            if (settingsButton is null || !TryInvoke(settingsButton))
                throw new InvalidOperationException("Zoom's Settings button was not accessible.");

            _settingsWindow = WaitFor(
                () => FindTopLevel("Settings", ControlType.Window),
                TimeSpan.FromSeconds(15));
            if (_settingsWindow is null)
                throw new InvalidOperationException("Zoom Settings did not open.");

            ApplyDarkMode();

            _report.ZoomStarted = true;
            TryCloseSettings();
        }
        catch (Exception ex)
        {
            _report.Error = ex.Message;
            _report.ZoomStarted = Process.GetProcessesByName("Zoom").Length > 0;
        }

        _report.CompletedUtc = DateTimeOffset.UtcNow;
        return _report;
    }

    private void ApplyDarkMode()
    {
        SelectCategory("General");
        SetRadio("General", "Color mode: Dark", x => x.Contains("Color mode", StringComparison.OrdinalIgnoreCase) && x.EndsWith("Dark", StringComparison.OrdinalIgnoreCase));
    }

    private void SelectCategory(string name)
    {
        if (_settingsWindow is null) return;
        var element = Find(_settingsWindow, name, ControlType.ListItem);
        if (element is not null && element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
        {
            ((SelectionItemPattern)pattern).Select();
            Thread.Sleep(450);
        }
    }

    private void SetToggle(string category, string setting, bool desired)
    {
        if (_settingsWindow is null) return;
        var element = Find(_settingsWindow, setting, ControlType.CheckBox);
        if (element is null)
        {
            Add(category, setting, desired ? "On" : "Off", ZoomSettingStatus.Unavailable, "Control not exposed by this Zoom version.");
            return;
        }

        try
        {
            if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out var raw))
                throw new InvalidOperationException("Toggle pattern unavailable.");
            var pattern = (TogglePattern)raw;
            var isOn = pattern.Current.ToggleState == ToggleState.On;
            if (isOn == desired)
            {
                Add(category, setting, desired ? "On" : "Off", ZoomSettingStatus.AlreadySet);
                return;
            }
            pattern.Toggle();
            Thread.Sleep(120);
            isOn = pattern.Current.ToggleState == ToggleState.On;
            Add(category, setting, desired ? "On" : "Off",
                isOn == desired ? ZoomSettingStatus.Applied : ZoomSettingStatus.Failed,
                isOn == desired ? null : "Zoom did not retain the requested state.");
        }
        catch (Exception ex)
        {
            Add(category, setting, desired ? "On" : "Off", ZoomSettingStatus.Failed, ex.Message);
        }
    }

    private void SetRadio(string category, string displayName, Func<string, bool> nameMatch)
    {
        if (_settingsWindow is null) return;
        var element = FindFirst(_settingsWindow, ControlType.RadioButton, nameMatch);
        if (element is null)
        {
            Add(category, displayName, "Selected", ZoomSettingStatus.Unavailable);
            return;
        }
        try
        {
            if (!element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var raw))
                throw new InvalidOperationException("Selection pattern unavailable.");
            var pattern = (SelectionItemPattern)raw;
            if (pattern.Current.IsSelected)
            {
                Add(category, displayName, "Selected", ZoomSettingStatus.AlreadySet);
                return;
            }
            pattern.Select();
            Thread.Sleep(120);
            Add(category, displayName, "Selected",
                pattern.Current.IsSelected ? ZoomSettingStatus.Applied : ZoomSettingStatus.Failed);
        }
        catch (Exception ex)
        {
            Add(category, displayName, "Selected", ZoomSettingStatus.Failed, ex.Message);
        }
    }

    private void SelectCombo(string category, string comboPrefix, string desiredItemPrefix)
    {
        if (_settingsWindow is null) return;
        var combo = FindFirst(_settingsWindow, ControlType.ComboBox,
            x => x.StartsWith(comboPrefix, StringComparison.OrdinalIgnoreCase));
        if (combo is null)
        {
            Add(category, comboPrefix, desiredItemPrefix, ZoomSettingStatus.Unavailable);
            return;
        }
        try
        {
            var currentName = SafeName(combo);
            if (currentName.Contains(desiredItemPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Add(category, comboPrefix, desiredItemPrefix, ZoomSettingStatus.AlreadySet);
                return;
            }

            if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandRaw))
                ((ExpandCollapsePattern)expandRaw).Expand();
            Thread.Sleep(180);
            var item = FindFirst(combo, ControlType.ListItem,
                           x => x.StartsWith(desiredItemPrefix, StringComparison.OrdinalIgnoreCase))
                       ?? FindFirst(AutomationElement.RootElement, ControlType.ListItem,
                           x => x.StartsWith(desiredItemPrefix, StringComparison.OrdinalIgnoreCase));
            if (item is null || !item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectRaw))
                throw new InvalidOperationException($"Option '{desiredItemPrefix}' is not available on this computer.");
            ((SelectionItemPattern)selectRaw).Select();
            Thread.Sleep(150);
            Add(category, comboPrefix, desiredItemPrefix, ZoomSettingStatus.Applied);
        }
        catch (Exception ex)
        {
            Add(category, comboPrefix, desiredItemPrefix, ZoomSettingStatus.Unavailable, ex.Message);
        }
    }

    private void SetRange(string category, string sliderName, double desired)
    {
        if (_settingsWindow is null) return;
        var slider = Find(_settingsWindow, sliderName, ControlType.Slider);
        if (slider is null)
        {
            Add(category, sliderName, desired.ToString("0"), ZoomSettingStatus.Unavailable);
            return;
        }
        try
        {
            if (!slider.TryGetCurrentPattern(RangeValuePattern.Pattern, out var raw))
                throw new InvalidOperationException("Range pattern unavailable.");
            var pattern = (RangeValuePattern)raw;
            var value = Math.Clamp(desired, pattern.Current.Minimum, pattern.Current.Maximum);
            if (Math.Abs(pattern.Current.Value - value) < 0.5)
            {
                Add(category, sliderName, value.ToString("0"), ZoomSettingStatus.AlreadySet);
                return;
            }
            pattern.SetValue(value);
            Add(category, sliderName, value.ToString("0"), ZoomSettingStatus.Applied);
        }
        catch (Exception ex)
        {
            Add(category, sliderName, desired.ToString("0"), ZoomSettingStatus.Unavailable, ex.Message);
        }
    }

    private void TryCloseSettings()
    {
        try
        {
            if (_settingsWindow?.TryGetCurrentPattern(WindowPattern.Pattern, out var raw) == true)
                ((WindowPattern)raw).Close();
        }
        catch { }
    }

    private static AutomationElement? FindTopLevel(string name, ControlType type)
    {
        return Find(AutomationElement.RootElement, name, type, TreeScope.Children);
    }

    private static AutomationElement? FindTopLevelWindowWithDescendant(string descendantName, ControlType descendantType)
    {
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
        foreach (AutomationElement window in windows)
        {
            if (Find(window, descendantName, descendantType) is not null)
                return window;
        }
        return null;
    }

    private static AutomationElement? Find(
        AutomationElement root,
        string name,
        ControlType type,
        TreeScope scope = TreeScope.Descendants)
    {
        return FindFirst(root, type, x => x.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase), scope);
    }

    private static AutomationElement? FindFirst(
        AutomationElement root,
        ControlType type,
        Func<string, bool> nameMatch,
        TreeScope scope = TreeScope.Descendants)
    {
        try
        {
            var elements = root.FindAll(scope, new PropertyCondition(AutomationElement.ControlTypeProperty, type));
            foreach (AutomationElement element in elements)
            {
                if (nameMatch(SafeName(element)))
                    return element;
            }
        }
        catch { }
        return null;
    }

    private static string SafeName(AutomationElement element)
    {
        try { return element.Current.Name?.Trim() ?? ""; }
        catch { return ""; }
    }

    private static bool TryInvoke(AutomationElement element)
    {
        try
        {
            if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out var raw))
                return false;
            ((InvokePattern)raw).Invoke();
            return true;
        }
        catch { return false; }
    }

    private static T? WaitFor<T>(Func<T?> probe, TimeSpan timeout) where T : class
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            var value = probe();
            if (value is not null) return value;
            Thread.Sleep(250);
        }
        return null;
    }

    private void Add(string category, string setting, string desired, ZoomSettingStatus status, string? detail = null)
    {
        _report.Settings.Add(new ZoomSettingResult
        {
            Category = category,
            Setting = setting,
            DesiredValue = desired,
            Status = status,
            Detail = detail
        });
    }
}
