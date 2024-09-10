using System.Diagnostics;
using System.Text.Encodings.Web;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Bunit.Web.AngleSharp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;

namespace AngleSharpExperiments.Rendering;

internal class AngleSharpDomBuilder
{
    private readonly TextEncoder javaScriptEncoder = JavaScriptEncoder.Default;
    private readonly BunitRootComponentState rootComponentState;
    private readonly BunitRenderer renderer;
    private readonly NavigationManager? navigationManager;
    private readonly IHtmlParser htmlParser;

    private TextEncoder htmlEncoder = HtmlEncoder.Default;
    private string? closestSelectValueAsString;

    public AngleSharpDomBuilder(
        BunitRootComponentState rootComponentState,
        BunitRenderer renderer,
        NavigationManager? navigationManager,
        IHtmlParser htmlParser)
    {
        this.rootComponentState = rootComponentState;
        this.renderer = renderer;
        this.navigationManager = navigationManager;
        this.htmlParser = htmlParser;
    }

    /// <summary>
    /// Renders the specified component as HTML to the body.
    /// </summary>
    /// <param name="componentId">The ID of the component whose current HTML state is to be rendered.</param>
    /// <param name="output">The body destination.</param>
    public void BuildRootComponentDom()
    {
        var frames = rootComponentState.GetCurrentRenderTreeFrames();
        var nodes = new BunitComponentNodeList(rootComponentState.Document.Body!);
        rootComponentState.Nodes = nodes;

        RenderFrames(
            rootComponentState.ComponentId,
            rootComponentState.Document.Body!,
            nodes,
            frames,
            position: 0,
            frames.Count);

        // Reset Dom builder state
        closestSelectValueAsString = null;
        TextEncoder htmlEncoder = HtmlEncoder.Default;
    }

    /// <summary>
    /// Renders the specified component as HTML to the body.
    /// </summary>
    /// <param name="componentId">The ID of the component whose current HTML state is to be rendered.</param>
    /// <param name="output">The body destination.</param>
    private void BuildComponentDom(int componentId, IElement parent, BunitComponentNodeList parentComponentNodes)
    {
        var compnentState = renderer.GetComponentState(componentId);
        var frames = compnentState.GetCurrentRenderTreeFrames();
        var nodes = new BunitComponentNodeList(parent);
        compnentState.Nodes = nodes;
        RenderFrames(componentId, parent, nodes, frames, position: 0, frames.Count);

        // Add all nodes from the child component to the parent components nodes list,
        // if they share the same parent.
        foreach (var node in nodes)
        {
            if (ReferenceEquals(parentComponentNodes.Parent, node.Parent))
            {
                parentComponentNodes.Add(node);
            }
        }
    }

    private int RenderFrames(int componentId, IElement parent, BunitComponentNodeList componentNodes, ArrayRange<RenderTreeFrame> frames, int position, int maxElements)
    {
        var nextPosition = position;
        var endPosition = position + maxElements;
        while (position < endPosition)
        {
            nextPosition = RenderCore(componentId, parent, componentNodes, frames, position);
            if (position == nextPosition)
            {
                throw new InvalidOperationException("We didn't consume any input.");
            }
            position = nextPosition;
        }

        return nextPosition;
    }

    private int RenderCore(
        int componentId,
        IElement parent,
        BunitComponentNodeList componentNodes,
        ArrayRange<RenderTreeFrame> frames,
        int position)
    {
        ref var frame = ref frames.Array[position];
        switch (frame.FrameType)
        {
            case RenderTreeFrameType.Element:
                return RenderElement(componentId, parent, componentNodes, frames, position);
            case RenderTreeFrameType.Attribute:
                throw new InvalidOperationException($"Attributes should only be encountered within {nameof(RenderElement)}");
            case RenderTreeFrameType.Text:
                {
                    var node = parent.Owner!.CreateTextNode(htmlEncoder.Encode(frame.TextContent));
                    parent.AppendChild(node);
                    if (ReferenceEquals(parent, componentNodes.Parent))
                        componentNodes.Add(node);
                    rootComponentState.NodeComponentMap.Add(node, componentId);
                    return ++position;
                }
            case RenderTreeFrameType.Markup:
                {
                    var nodes = htmlParser.ParseFragment(frame.MarkupContent, parent);
                    var addToComponentNodes = ReferenceEquals(parent, componentNodes.Parent);
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        var node = nodes[i];
                        parent.AppendChild(node);
                        if (addToComponentNodes)
                            componentNodes.Add(node);

                        rootComponentState.NodeComponentMap.Add(node, componentId);
                    }

                    return ++position;
                }
            case RenderTreeFrameType.Component:
                return RenderChildComponent(parent, componentNodes, frames, position);
            case RenderTreeFrameType.Region:
                return RenderFrames(componentId, parent, componentNodes, frames, position + 1, frame.RegionSubtreeLength - 1);
            case RenderTreeFrameType.ElementReferenceCapture:
            case RenderTreeFrameType.ComponentReferenceCapture:
                return ++position;
            case RenderTreeFrameType.NamedEvent:
                //RenderHiddenFieldForNamedSubmitEvent(componentId, body, frames, position);
                return ++position;
            default:
                throw new InvalidOperationException($"Invalid element frame type '{frame.FrameType}'.");
        }
    }

    private int RenderElement(int componentId, INode parent, BunitComponentNodeList componentNodes, ArrayRange<RenderTreeFrame> frames, int position)
    {
        ref var frame = ref frames.Array[position];
        var element = parent.Owner!.CreateElement(frame.ElementName);
        parent.AppendChild(element);
        componentNodes.Add(element);
        rootComponentState.NodeComponentMap.Add(element, componentId);
        int afterElement;
        var isTextArea = string.Equals(frame.ElementName, "textarea", StringComparison.OrdinalIgnoreCase);
        var isForm = string.Equals(frame.ElementName, "form", StringComparison.OrdinalIgnoreCase);

        // We don't want to include value attribute of textarea element.
        var afterAttributes = RenderAttributes(element, frames, position + 1, frame.ElementSubtreeLength - 1, !isTextArea, isForm: isForm, out var capturedValueAttribute);

        // When we see an <option> as a descendant of a <select>, and the option's "value" attribute matches the
        // "value" attribute on the <select>, then we auto-add the "selected" attribute to that option. This is
        // a way of converting Blazor's select binding feature to regular static HTML.
        if (closestSelectValueAsString != null
            && string.Equals(frame.ElementName, "option", StringComparison.OrdinalIgnoreCase)
            && string.Equals(capturedValueAttribute, closestSelectValueAsString, StringComparison.Ordinal))
        {
            element.SetAttribute("selected", null);
        }

        var remainingElements = frame.ElementSubtreeLength + position - afterAttributes;
        if (remainingElements > 0 || isTextArea)
        {
            var isSelect = string.Equals(frame.ElementName, "select", StringComparison.OrdinalIgnoreCase);
            if (isSelect)
            {
                closestSelectValueAsString = capturedValueAttribute;
            }

            if (isTextArea && !string.IsNullOrEmpty(capturedValueAttribute))
            {
                // Textarea is a special type of form field where the value is given as text content instead of a 'value' attribute
                // So, if we captured a value attribute, use that instead of any child content
                element.TextContent = htmlEncoder.Encode(capturedValueAttribute);
                afterElement = position + frame.ElementSubtreeLength; // Skip descendants
            }
            else if (string.Equals(frame.ElementName/*ElementNameField*/, "script", StringComparison.OrdinalIgnoreCase))
            {
                afterElement = RenderScriptElementChildren(componentId, element, componentNodes, frames, afterAttributes, remainingElements);
            }
            else
            {
                afterElement = RenderChildren(componentId, element, componentNodes, frames, afterAttributes, remainingElements);
            }

            if (isSelect)
            {
                // There's no concept of nested <select> elements, so as soon as we're exiting one of them,
                // we can safely say there is no longer any value for this
                closestSelectValueAsString = null;
            }

            Debug.Assert(afterElement == position + frame.ElementSubtreeLength);
            return afterElement;
        }
        else
        {
            Debug.Assert(afterAttributes == position + frame.ElementSubtreeLength);
            return afterAttributes;
        }
    }

    private int RenderScriptElementChildren(int componentId, IElement parent, BunitComponentNodeList componentNodes, ArrayRange<RenderTreeFrame> frames, int position, int maxElements)
    {
        // Inside a <script> angleSharpContext, AddContent calls should result in the text being
        // JavaScript encoded rather than HTML encoded. It's not that we recommend inserting
        // user-supplied content inside a <script> block, but that if someone does, we
        // want the encoding style to match the angleSharpContext for correctness and safety. This is
        // also consistent with .cshtml's treatment of <script>.
        var originalEncoder = htmlEncoder;
        try
        {
            htmlEncoder = javaScriptEncoder;
            return RenderChildren(componentId, parent, componentNodes, frames, position, maxElements);
        }
        finally
        {
            htmlEncoder = originalEncoder;
        }
    }

    private static bool TryFindEnclosingElementFrame(ArrayRange<RenderTreeFrame> frames, int frameIndex, out int result)
    {
        while (--frameIndex >= 0)
        {
            if (frames.Array[frameIndex].FrameType == RenderTreeFrameType.Element)
            {
                result = frameIndex;
                return true;
            }
        }

        result = default;
        return false;
    }

    private int RenderAttributes(
        IElement element,
        ArrayRange<RenderTreeFrame> frames,
        int position,
        int maxElements,
        bool includeValueAttribute,
        bool isForm,
        out string? capturedValueAttribute)
    {
        capturedValueAttribute = null;

        if (maxElements == 0)
        {
            EmitFormActionIfNotExplicit(element, isForm, hasExplicitActionValue: false);
            return position;
        }

        var hasExplicitActionValue = false;
        for (var i = 0; i < maxElements; i++)
        {
            var candidateIndex = position + i;
            ref var frame = ref frames.Array[candidateIndex];

            if (frame.FrameType != RenderTreeFrameType.Attribute)
            {
                if (frame.FrameType == RenderTreeFrameType.ElementReferenceCapture)
                {
                    continue;
                }

                EmitFormActionIfNotExplicit(element, isForm, hasExplicitActionValue);
                return candidateIndex;
            }

            if (frame.AttributeEventHandlerId > 0)
            {
                element.SetAttribute("bunit:event-handler-id", frame.AttributeEventHandlerId.ToString());
                continue;
            }

            if (frame.AttributeName.Equals("value", StringComparison.OrdinalIgnoreCase))
            {
                capturedValueAttribute = frame.AttributeValue as string;

                if (!includeValueAttribute)
                {
                    continue;
                }
            }

            if (isForm && frame.AttributeName.Equals("action", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(frame.AttributeValue as string))
            {
                hasExplicitActionValue = true;
            }

            switch (frame.AttributeValue)
            {
                case bool flag when flag:
                    element.SetAttribute(frame.AttributeName, null);
                    break;
                case string value:
                    element.SetAttribute(frame.AttributeName, htmlEncoder.Encode(value));
                    break;
                default:
                    break;
            }
        }

        EmitFormActionIfNotExplicit(element, isForm, hasExplicitActionValue);

        return position + maxElements;

        void EmitFormActionIfNotExplicit(IElement output, bool isForm, bool hasExplicitActionValue)
        {
            if (isForm && navigationManager != null && !hasExplicitActionValue)
            {
                output.SetAttribute("action", GetRootRelativeUrlForFormAction(navigationManager));
            }
        }
    }

    private static string GetRootRelativeUrlForFormAction(NavigationManager navigationManager)
    {
        // We want a root-relative URL because:
        // - if we used a base-relative one, then if currentUrl==baseHref, that would result
        //   in an empty string, but forms have special handling for action="" (it means "submit
        //   to the current URL, but that would be wrong if there's an uncommitted navigation in
        //   flight, e.g., after the user clicking 'back' - it would go to whatever's now in the
        //   address bar, ignoring where the form was rendered)
        // - if we used an absolute URL, then it creates a significant extra pit of failure for
        //   apps hosted behind a reverse proxy (e.g., container apps), because the server's view
        //   of the absolute URL isn't usable outside the container
        //   - of course, sites hosted behind URL rewriting that modifies the path will still be
        //     wrong, but developers won't do that often as it makes things like <a href> really
        //     difficult to get right. In that case, developers must emit an action attribute manually.
        return new Uri(navigationManager.Uri, UriKind.Absolute).PathAndQuery;
    }

    private int RenderChildren(int componentId, IElement parent, BunitComponentNodeList componentNodes, ArrayRange<RenderTreeFrame> frames, int position, int maxElements)
    {
        if (maxElements == 0)
        {
            return position;
        }

        return RenderFrames(componentId, parent, componentNodes, frames, position, maxElements);
    }

    private int RenderChildComponent(IElement parent, BunitComponentNodeList componentNodes, ArrayRange<RenderTreeFrame> frames, int position)
    {
        ref var frame = ref frames.Array[position];

        BuildComponentDom(frame.ComponentId, parent, componentNodes);

        return position + frame.ComponentSubtreeLength;
    }
}