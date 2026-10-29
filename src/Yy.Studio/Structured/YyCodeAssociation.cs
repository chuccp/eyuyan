using Yy.Language.Structured;
using Yy.Studio.Forms;

namespace Yy.Studio.Structured;

public sealed record YyCodeEventAssociation(
    string ControlName,
    string ControlType,
    string EventName,
    string EventDisplayName,
    bool IsImplemented);

public sealed record YyCodeControlAssociation(
    string ControlName,
    string ControlType,
    IReadOnlyList<YyCodeEventAssociation> Events);

public sealed class YyCodeAssociation
{
    private YyCodeAssociation(
        string formName,
        IReadOnlyList<YyCodeControlAssociation> controls,
        IReadOnlyList<YyCodeEventAssociation> formEvents)
    {
        FormName = formName;
        Controls = controls;
        FormEvents = formEvents;
    }

    public string FormName { get; }
    public IReadOnlyList<YyCodeControlAssociation> Controls { get; }
    public IReadOnlyList<YyCodeEventAssociation> FormEvents { get; }
    public int EventCount => Controls.Sum(control => control.Events.Count) + FormEvents.Count;
    public int MissingEventCount =>
        Controls.Sum(control => control.Events.Count(item => !item.IsImplemented)) +
        FormEvents.Count(item => !item.IsImplemented);

    public static YyCodeAssociation Build(YyFormModel form, YyCodeDocument document)
    {
        var functions = document.Functions
            .Select(function => function.Name)
            .ToHashSet(StringComparer.Ordinal);
        var controls = form.Controls
            .Select(control => new YyCodeControlAssociation(
                control.Name,
                control.Type,
                GetControlEvents(control)
                    .Select(item => CreateEvent(control.Name, control.Type, item, functions))
                    .ToArray()))
            .ToArray();

        var formEvents = new List<YyCodeEventAssociation>();
        if (!string.IsNullOrWhiteSpace(form.LoadEvent))
        {
            formEvents.Add(CreateEvent(
                form.Name,
                "窗体",
                (form.LoadEvent, "载入"),
                functions));
        }

        if (!string.IsNullOrWhiteSpace(form.CloseEvent))
        {
            formEvents.Add(CreateEvent(
                form.Name,
                "窗体",
                (form.CloseEvent, "关闭"),
                functions));
        }

        return new YyCodeAssociation(form.Name, controls, formEvents);
    }

    private static IEnumerable<(string EventName, string DisplayName)> GetControlEvents(
        YyControlModel control)
    {
        if (!string.IsNullOrWhiteSpace(control.ClickEvent))
        {
            yield return (control.ClickEvent, "单击");
        }

        if (!string.IsNullOrWhiteSpace(control.TextChangedEvent))
        {
            yield return (control.TextChangedEvent, "文本改变");
        }

        if (!string.IsNullOrWhiteSpace(control.SelectionChangedEvent))
        {
            yield return (control.SelectionChangedEvent, "选择改变");
        }

        if (!string.IsNullOrWhiteSpace(control.CheckedChangedEvent))
        {
            yield return (control.CheckedChangedEvent, "选中改变");
        }
    }

    private static YyCodeEventAssociation CreateEvent(
        string controlName,
        string controlType,
        (string EventName, string DisplayName) eventInfo,
        HashSet<string> functions) =>
        new(
            controlName,
            controlType,
            eventInfo.EventName,
            eventInfo.DisplayName,
            functions.Contains(eventInfo.EventName));
}
