using AngleSharp.Dom;

namespace AngleSharpExperiments.AngleSharpRendering;

public class EventDelegator
{
    internal void RemoveListener(ulong eventHandlerId)
    {
        throw new NotImplementedException();
    }

    internal void SetListener(IElement toDomElement, string eventName, ulong eventHandlerId, int componentId)
    {
        throw new NotImplementedException();
    }

    internal void SetPreventDefault(IElement element, string eventName, bool v)
    {
        throw new NotImplementedException();
    }

    internal void SetStopPropagation(IElement element, string eventName, bool v)
    {
        throw new NotImplementedException();
    }
}