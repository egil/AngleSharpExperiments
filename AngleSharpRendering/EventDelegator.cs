using AngleSharp.Dom;

namespace AngleSharpExperiments.AngleSharpRendering;

public class EventDelegator
{
    private readonly Dictionary<ulong, IElement> eventHandlerMap = new();

    internal void RemoveListener(ulong eventHandlerId)
    {
        eventHandlerMap.Remove(eventHandlerId);
    }

    internal void SetListener(IElement element, string eventName, ulong eventHandlerId, int componentId)
    {
        eventHandlerMap[eventHandlerId] = element;
        element.SetAttribute("bunit:event-handler-id", eventHandlerId.ToString());
        element.SetAttribute("bunit:event-type", eventName);
    }

    internal void SetPreventDefault(IElement element, string eventName, bool v)
    {
        element.SetAttribute("bunit:event-stop-default", null);
    }

    internal void SetStopPropagation(IElement element, string eventName, bool v)
    {
        element.SetAttribute("bunit:event-stop-propagation", null);
    }
}