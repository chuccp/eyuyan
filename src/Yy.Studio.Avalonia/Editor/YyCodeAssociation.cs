using Yy.Compiler.Forms;
using Yy.Language.Structured;

namespace Yy.Studio.Avalonia.Editor;

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

/// <summary>窗体与用户代码之间的只读事件关系视图，用于补全、备注和导航。</summary>
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
        AddFormEvent(formEvents, form, form.LoadEvent, EventHandlerNaming.FormLoad, functions);
        AddFormEvent(formEvents, form, form.OpenEvent, EventHandlerNaming.FormOpen, functions);
        AddFormEvent(formEvents, form, form.CloseEvent, EventHandlerNaming.FormClose, functions);
        return new YyCodeAssociation(form.Name, controls, formEvents);
    }

    /// <summary>把事件处理名映射到“控件 · 事件”备注文本，供方法声明表显示。</summary>
    public IReadOnlyDictionary<string, string> ToRemarkMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var association in FormEvents.Concat(Controls.SelectMany(control => control.Events)))
        {
            map[association.EventName] =
                association.ControlName + " · " + association.EventDisplayName;
        }

        return map;
    }

    private static void AddFormEvent(
        ICollection<YyCodeEventAssociation> events,
        YyFormModel form,
        string? handler,
        string displayName,
        HashSet<string> functions)
    {
        if (!string.IsNullOrWhiteSpace(handler))
        {
            events.Add(CreateEvent(form.Name, "窗体", (handler, displayName), functions));
        }
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
