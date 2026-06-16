using System.Text.Json;
using System.Text.RegularExpressions;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA2;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging;
using Windows.Agent.Interface;
using Windows.Agent.Uia;

namespace Windows.Agent.Services;

public sealed class UiaService : IUiaService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ILogger<UiaService> _logger;

    public UiaService(ILogger<UiaService> logger)
    {
        _logger = logger;
    }

    public Task<string> GetTreeAsync(string windowTitleRegex, int depth = 3, string backend = "uia3")
        => RunStaAsync(() => GetTreeCore(windowTitleRegex, depth, backend));

    public Task<string> FindAsync(string windowTitleRegex, UiaSelector selector, int limit = 5, string backend = "uia3")
        => RunStaAsync(() => FindCore(windowTitleRegex, selector, limit, backend));

    public Task<string> InvokeAsync(string windowTitleRegex, UiaSelector selector, string backend = "uia3")
        => RunStaAsync(() => InvokeCore(windowTitleRegex, selector, backend));

    public Task<string> SetValueAsync(string windowTitleRegex, UiaSelector selector, string value, string backend = "uia3")
        => RunStaAsync(() => SetValueCore(windowTitleRegex, selector, value, backend));

    private string GetTreeCore(string windowTitleRegex, int depth, string backend)
    {
        if (string.IsNullOrWhiteSpace(windowTitleRegex))
        {
            return JsonSerializer.Serialize(new { success = false, message = "windowTitleRegex is required" }, JsonOptions);
        }

        if (depth < 0 || depth > 20)
        {
            return JsonSerializer.Serialize(new { success = false, message = "depth must be between 0 and 20" }, JsonOptions);
        }

        try
        {
            var regex = CompileTitleRegex(windowTitleRegex);
            if (!TryCreateAutomation(backend, out var automation, out var backendError))
            {
                return JsonSerializer.Serialize(new { success = false, backend, message = backendError }, JsonOptions);
            }

            using (automation)
            {
                var (window, matches, matchError) = FindUniqueWindow(automation, regex);
            if (window == null)
            {
                    return JsonSerializer.Serialize(new { success = false, backend, message = matchError, matches }, JsonOptions);
            }

            var node = BuildNode(window, depth, maxChildrenPerNode: 80);
            var payload = new
            {
                success = true,
                backend,
                windowTitleRegex,
                depth,
                window = ToElementInfo(window),
                tree = node
            };
            return JsonSerializer.Serialize(payload, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build UIA tree");
            return JsonSerializer.Serialize(new { success = false, backend, windowTitleRegex, message = ex.Message }, JsonOptions);
        }
    }

    private string FindCore(string windowTitleRegex, UiaSelector selector, int limit, string backend)
    {
        if (string.IsNullOrWhiteSpace(windowTitleRegex))
        {
            return JsonSerializer.Serialize(new { success = false, message = "windowTitleRegex is required" }, JsonOptions);
        }

        if (limit <= 0 || limit > 50)
        {
            return JsonSerializer.Serialize(new { success = false, message = "limit must be between 1 and 50" }, JsonOptions);
        }

        try
        {
            var regex = CompileTitleRegex(windowTitleRegex);
            if (!TryCreateAutomation(backend, out var automation, out var backendError))
            {
                return JsonSerializer.Serialize(new { success = false, backend, message = backendError }, JsonOptions);
            }

            using (automation)
            {
                var (window, _, matchError) = FindUniqueWindow(automation, regex);
            if (window == null)
            {
                    return JsonSerializer.Serialize(new { success = false, backend, message = matchError }, JsonOptions);
            }

            var conditionError = TryBuildCondition(selector, out var condition);
            if (conditionError != null || condition == null)
            {
                    return JsonSerializer.Serialize(new { success = false, backend, message = conditionError ?? "invalid selector" }, JsonOptions);
            }

            var elements = window.FindAllDescendants(condition);
            var matches = elements.Take(limit).Select(ToElementInfo).ToArray();

            if (elements.Length == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    backend,
                    windowTitleRegex,
                    selector,
                    message = "UIA element not found"
                }, JsonOptions);
            }

            if (elements.Length != 1)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    backend,
                    windowTitleRegex,
                    selector,
                    message = $"UIA selector matched {elements.Length} elements (expected exactly 1)",
                    matchCount = elements.Length,
                    matches
                }, JsonOptions);
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                backend,
                windowTitleRegex,
                selector,
                matchCount = elements.Length,
                element = matches[0]
            }, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find UIA element");
            return JsonSerializer.Serialize(new { success = false, backend, windowTitleRegex, selector, message = ex.Message }, JsonOptions);
        }
    }

    private string InvokeCore(string windowTitleRegex, UiaSelector selector, string backend)
    {
        if (string.IsNullOrWhiteSpace(windowTitleRegex))
        {
            return JsonSerializer.Serialize(new { success = false, message = "windowTitleRegex is required" }, JsonOptions);
        }

        try
        {
            var regex = CompileTitleRegex(windowTitleRegex);
            if (!TryCreateAutomation(backend, out var automation, out var backendError))
            {
                return JsonSerializer.Serialize(new { success = false, backend, message = backendError }, JsonOptions);
            }

            using (automation)
            {
                var (window, _, matchError) = FindUniqueWindow(automation, regex);
            if (window == null)
            {
                    return JsonSerializer.Serialize(new { success = false, backend, message = matchError }, JsonOptions);
            }

            var resolve = ResolveUniqueElement(window, selector);
            if (!resolve.Success || resolve.Element == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    backend,
                    windowTitleRegex,
                    selector,
                    message = resolve.Message,
                    matchCount = resolve.MatchCount,
                    matches = resolve.Matches
                }, JsonOptions);
            }

            window.Focus();

            var invoke = resolve.Element.Patterns.Invoke.PatternOrDefault;
            if (invoke == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    backend,
                    windowTitleRegex,
                    selector,
                    element = ToElementInfo(resolve.Element),
                    message = "Invoke pattern is not supported on the matched element"
                }, JsonOptions);
            }

            invoke.Invoke();

            return JsonSerializer.Serialize(new
            {
                success = true,
                backend,
                windowTitleRegex,
                selector,
                element = ToElementInfo(resolve.Element),
                message = "Invoked"
            }, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invoke UIA element");
            return JsonSerializer.Serialize(new { success = false, backend, windowTitleRegex, selector, message = ex.Message }, JsonOptions);
        }
    }

    private string SetValueCore(string windowTitleRegex, UiaSelector selector, string value, string backend)
    {
        if (string.IsNullOrWhiteSpace(windowTitleRegex))
        {
            return JsonSerializer.Serialize(new { success = false, message = "windowTitleRegex is required" }, JsonOptions);
        }

        if (value == null)
        {
            return JsonSerializer.Serialize(new { success = false, message = "value is required" }, JsonOptions);
        }

        try
        {
            var regex = CompileTitleRegex(windowTitleRegex);
            if (!TryCreateAutomation(backend, out var automation, out var backendError))
            {
                return JsonSerializer.Serialize(new { success = false, backend, message = backendError }, JsonOptions);
            }

            using (automation)
            {
                var (window, _, matchError) = FindUniqueWindow(automation, regex);
            if (window == null)
            {
                    return JsonSerializer.Serialize(new { success = false, backend, message = matchError }, JsonOptions);
            }

            var resolve = ResolveUniqueElement(window, selector);
            if (!resolve.Success || resolve.Element == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    backend,
                    windowTitleRegex,
                    selector,
                    message = resolve.Message,
                    matchCount = resolve.MatchCount,
                    matches = resolve.Matches
                }, JsonOptions);
            }

            window.Focus();

            var valuePattern = resolve.Element.Patterns.Value.PatternOrDefault;
            if (valuePattern == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    backend,
                    windowTitleRegex,
                    selector,
                    element = ToElementInfo(resolve.Element),
                    message = "Value pattern is not supported on the matched element"
                }, JsonOptions);
            }

            valuePattern.SetValue(value);

            return JsonSerializer.Serialize(new
            {
                success = true,
                backend,
                windowTitleRegex,
                selector,
                element = ToElementInfo(resolve.Element),
                message = "Value set"
            }, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set value on UIA element");
            return JsonSerializer.Serialize(new { success = false, backend, windowTitleRegex, selector, message = ex.Message }, JsonOptions);
        }
    }

    private static Regex CompileTitleRegex(string windowTitleRegex)
    {
        return new Regex(
            windowTitleRegex,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            matchTimeout: TimeSpan.FromSeconds(2));
    }

    private static (AutomationElement? Window, object[] Matches, string? Error) FindUniqueWindow(AutomationBase automation, Regex titleRegex)
    {
        var desktop = automation.GetDesktop();
        var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
        var matches = windows
            .Where(w => !string.IsNullOrWhiteSpace(w.Name) && titleRegex.IsMatch(w.Name))
            .ToArray();

        if (matches.Length == 0)
        {
            return (null, Array.Empty<object>(), "No window matched windowTitleRegex");
        }

        if (matches.Length != 1)
        {
            var list = matches.Select(w => new { title = w.Name, className = w.ClassName }).Cast<object>().ToArray();
            return (null, list, $"Multiple windows matched windowTitleRegex: {matches.Length}");
        }

        return (matches[0], Array.Empty<object>(), null);
    }

    private static bool TryCreateAutomation(string backend, out AutomationBase automation, out string message)
    {
        var normalized = (backend ?? "uia3").Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "uia3":
            case "3":
                automation = new UIA3Automation();
                message = "OK";
                return true;
            case "uia2":
            case "2":
                automation = new UIA2Automation();
                message = "OK";
                return true;
            default:
                automation = null!;
                message = "backend must be uia3 or uia2";
                return false;
        }
    }

    private static string? TryBuildCondition(UiaSelector selector, out Func<ConditionFactory, ConditionBase>? condition)
    {
        condition = null;

        if (selector == null)
        {
            return "selector is required";
        }

        ControlType? controlType = null;
        if (!string.IsNullOrWhiteSpace(selector.ControlType))
        {
            controlType = TryGetControlType(selector.ControlType);
            if (controlType == null)
            {
                return $"Unknown controlType: '{selector.ControlType}'";
            }
        }

        condition = cf =>
        {
            ConditionBase? current = null;

            if (!string.IsNullOrWhiteSpace(selector.AutomationId))
            {
                current = cf.ByAutomationId(selector.AutomationId);
            }

            if (!string.IsNullOrWhiteSpace(selector.Name))
            {
                var c = cf.ByName(selector.Name);
                current = current == null ? c : current.And(c);
            }

            if (!string.IsNullOrWhiteSpace(selector.ClassName))
            {
                var c = cf.ByClassName(selector.ClassName);
                current = current == null ? c : current.And(c);
            }

            if (controlType.HasValue)
            {
                var c = cf.ByControlType(controlType.Value);
                current = current == null ? c : current.And(c);
            }

            return current ?? cf.ByControlType(ControlType.Custom);
        };

        return null;
    }

    private static ControlType? TryGetControlType(string controlType)
    {
        return controlType.Trim().ToLowerInvariant() switch
        {
            "button" => ControlType.Button,
            "edit" => ControlType.Edit,
            "textbox" => ControlType.Edit,
            "text" => ControlType.Text,
            "window" => ControlType.Window,
            "menu" => ControlType.Menu,
            "menuitem" => ControlType.MenuItem,
            "list" => ControlType.List,
            "listitem" => ControlType.ListItem,
            "combobox" => ControlType.ComboBox,
            "checkbox" => ControlType.CheckBox,
            "radiobutton" => ControlType.RadioButton,
            "tab" => ControlType.Tab,
            "tabitem" => ControlType.TabItem,
            "pane" => ControlType.Pane,
            "group" => ControlType.Group,
            "toolbar" => ControlType.ToolBar,
            "image" => ControlType.Image,
            "hyperlink" => ControlType.Hyperlink,
            _ => null
        };
    }

    private ResolveResult ResolveUniqueElement(AutomationElement window, UiaSelector selector)
    {
        var conditionError = TryBuildCondition(selector, out var condition);
        if (conditionError != null || condition == null)
        {
            return new ResolveResult(false, null, conditionError ?? "invalid selector", 0, Array.Empty<object>());
        }

        var elements = window.FindAllDescendants(condition);
        if (elements.Length == 0)
        {
            return new ResolveResult(false, null, "UIA element not found", 0, Array.Empty<object>());
        }

        var matches = elements.Take(5).Select(ToElementInfo).Cast<object>().ToArray();
        if (elements.Length != 1)
        {
            return new ResolveResult(false, null, $"UIA selector matched {elements.Length} elements (expected exactly 1)", elements.Length, matches);
        }

        return new ResolveResult(true, elements[0], "OK", 1, matches);
    }

    private static object BuildNode(AutomationElement element, int maxDepth, int maxChildrenPerNode, int depth = 0)
    {
        var info = ToElementInfo(element);
        if (depth >= maxDepth)
        {
            return info;
        }

        try
        {
            var children = element.FindAllChildren();
            var truncated = children.Length > maxChildrenPerNode;
            var list = children.Take(maxChildrenPerNode).Select(c => BuildNode(c, maxDepth, maxChildrenPerNode, depth + 1)).ToArray();

            return new
            {
                info,
                children = list,
                childrenCount = children.Length,
                childrenTruncated = truncated
            };
        }
        catch
        {
            return info;
        }
    }

    private static object ToElementInfo(AutomationElement element)
    {
        var rect = element.BoundingRectangle;
        return new
        {
            name = element.Name,
            automationId = element.AutomationId,
            className = element.ClassName,
            controlType = element.ControlType.ToString(),
            boundingRectangle = new { x = rect.Left, y = rect.Top, width = rect.Width, height = rect.Height },
            isEnabled = element.IsEnabled,
            isOffscreen = element.IsOffscreen
        };
    }

    private static Task<string> RunStaAsync(Func<string> func)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    private sealed record ResolveResult(bool Success, AutomationElement? Element, string Message, int MatchCount, object[] Matches);
}
