using System.Globalization;
using AngleSharp.Dom;
using AngleSharpExperiments.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;

namespace AngleSharpExperiments;

public static class BunitNodeExtensions
{
    public static IElement Find(this BunitComponentState componentState, string cssSelector)
        => componentState.Nodes.QuerySelector(cssSelector) ?? throw new InvalidOperationException($"Element not found: {cssSelector}");

    public static Task DispatchEventAsync<TEventArgs>(this IElement element, TEventArgs eventArgs) where TEventArgs : EventArgs
        => eventArgs switch
        {
            ChangeEventArgs changeEventArgs => element.DispatchEventAsync("onchange", changeEventArgs),
            _ => throw new InvalidOperationException($"Unsupported event args type: {typeof(TEventArgs).Name}")
        };

    public static Task DispatchEventAsync(this IElement element, string eventName, EventArgs? eventArgs)
    {
        if (!ulong.TryParse(element.GetAttribute("bunit:event-handler-id"), NumberStyles.Number, CultureInfo.InvariantCulture, out var eventHandlerId))
        {
            throw new InvalidOperationException($"Element does not have an event handler attached.");
        }

        var renderer = element.GetRenderer();

        return renderer
            .Dispatcher
            .InvokeAsync(() => renderer
                .DispatchEventAsync(
                    eventHandlerId,
                    new EventFieldInfo
                    {
                        FieldValue = eventName
                    },
                    eventArgs ?? EventArgs.Empty,
                    waitForQuiescence: true));
    }

    public static BunitComponentState? GetComponent(this INode node)
    {
        var renderer = node.GetRenderer();
        var rootComponentId = int.Parse(node.Owner!.Body!.GetAttribute("bunit:component-id")!);
        var rootComponentState = renderer.GetRootComponentState(rootComponentId);

        INode? candidate = node;
        do
        {
            if (rootComponentState.NodeComponentMap.TryGetValue(candidate, out var componentId))
            {
                return renderer.GetComponentState(componentId);
            }
            candidate = candidate.Parent;
        } while (candidate is not null);

        return null;
    }

    public static BunitRenderer GetRenderer(this INode node)
        => node.Owner?.Context.GetService<BunitRenderer>() ?? throw new InvalidOperationException("Renderer could not be found via node.");
}