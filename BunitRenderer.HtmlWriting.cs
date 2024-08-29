using System.Diagnostics;
using System.Text.Encodings.Web;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;

namespace AngleSharpExperiments;

public partial class BunitRenderer
{
    private readonly TextEncoder _javaScriptEncoder = JavaScriptEncoder.Default;
    private TextEncoder _htmlEncoder = HtmlEncoder.Default;
    private string? _closestSelectValueAsString;

    /// <summary>
    /// Renders the specified component as HTML to the output.
    /// </summary>
    /// <param name="componentId">The ID of the component whose current HTML state is to be rendered.</param>
    /// <param name="output">The output destination.</param>
    protected internal virtual void WriteComponentHtml(int componentId, IElement output)
    {
        // We're about to walk over some buffers inside the renderer that can be mutated during rendering.
        // So, we require exclusive access to the renderer during this synchronous process.
        Dispatcher.AssertAccess();

        var frames = GetCurrentRenderTreeFrames(componentId);
        RenderFrames(componentId, output, frames, 0, frames.Count);
    }

    /// <summary>
    /// Renders the specified component frame as HTML to the output.
    /// </summary>
    /// <param name="output">The output destination.</param>
    /// <param name="componentFrame">The <see cref="RenderTreeFrame"/> representing the component to be rendered.</param>
    protected virtual void RenderChildComponent(IElement output, ref RenderTreeFrame componentFrame)
    {
        WriteComponentHtml(componentFrame.ComponentId, output);
    }

    private int RenderFrames(int componentId, IElement output, ArrayRange<RenderTreeFrame> frames, int position, int maxElements)
    {
        var nextPosition = position;
        var endPosition = position + maxElements;
        while (position < endPosition)
        {
            nextPosition = RenderCore(componentId, output, frames, position);
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
        IElement output,
        ArrayRange<RenderTreeFrame> frames,
        int position)
    {
        ref var frame = ref frames.Array[position];
        switch (frame.FrameType)
        {
            case RenderTreeFrameType.Element:
                return RenderElement(componentId, output, frames, position);
            case RenderTreeFrameType.Attribute:
                throw new InvalidOperationException($"Attributes should only be encountered within {nameof(RenderElement)}");
            case RenderTreeFrameType.Text:
                output.AppendChild(output.Owner!.CreateTextNode(frame.TextContent));
                return ++position;
            case RenderTreeFrameType.Markup:
                var nodes = htmlParser.ParseFragment(frame.MarkupContent, output);
                for (int i = 0; i < nodes.Length; i++)
                {
                    output.AppendChild(nodes[i]);
                }
                return ++position;
            case RenderTreeFrameType.Component:
                return RenderChildComponent(output, frames, position);
            case RenderTreeFrameType.Region:
                return RenderFrames(componentId, output, frames, position + 1, frame.RegionSubtreeLength - 1);
            case RenderTreeFrameType.ElementReferenceCapture:
            case RenderTreeFrameType.ComponentReferenceCapture:
                return ++position;
            case RenderTreeFrameType.NamedEvent:
                //RenderHiddenFieldForNamedSubmitEvent(componentId, output, frames, position);
                return ++position;
            default:
                throw new InvalidOperationException($"Invalid element frame type '{frame.FrameType}'.");
        }
    }

    private int RenderElement(int componentId, IElement output, ArrayRange<RenderTreeFrame> frames, int position)
    {
        var parent = output;
        ref var frame = ref frames.Array[position];
        output = output.Owner!.CreateElement(frame.ElementName);
        parent.AppendChild(output);
        int afterElement;
        var isTextArea = string.Equals(frame.ElementName, "textarea", StringComparison.OrdinalIgnoreCase);
        var isForm = string.Equals(frame.ElementName, "form", StringComparison.OrdinalIgnoreCase);

        // We don't want to include value attribute of textarea element.
        var afterAttributes = RenderAttributes(output, frames, position + 1, frame.ElementSubtreeLength - 1, !isTextArea, isForm: isForm, out var capturedValueAttribute);

        // When we see an <option> as a descendant of a <select>, and the option's "value" attribute matches the
        // "value" attribute on the <select>, then we auto-add the "selected" attribute to that option. This is
        // a way of converting Blazor's select binding feature to regular static HTML.
        if (_closestSelectValueAsString != null
            && string.Equals(frame.ElementName, "option", StringComparison.OrdinalIgnoreCase)
            && string.Equals(capturedValueAttribute, _closestSelectValueAsString, StringComparison.Ordinal))
        {
            output.SetAttribute("selected", null);
        }

        var remainingElements = frame.ElementSubtreeLength + position - afterAttributes;
        if (remainingElements > 0 || isTextArea)
        {
            var isSelect = string.Equals(frame.ElementName, "select", StringComparison.OrdinalIgnoreCase);
            if (isSelect)
            {
                _closestSelectValueAsString = capturedValueAttribute;
            }

            if (isTextArea && !string.IsNullOrEmpty(capturedValueAttribute))
            {
                // Textarea is a special type of form field where the value is given as text content instead of a 'value' attribute
                // So, if we captured a value attribute, use that instead of any child content
                output.TextContent = capturedValueAttribute;
                afterElement = position + frame.ElementSubtreeLength; // Skip descendants
            }
            else if (string.Equals(frame.ElementName/*ElementNameField*/, "script", StringComparison.OrdinalIgnoreCase))
            {
                afterElement = RenderScriptElementChildren(componentId, output, frames, afterAttributes, remainingElements);
            }
            else
            {
                afterElement = RenderChildren(componentId, output, frames, afterAttributes, remainingElements);
            }

            if (isSelect)
            {
                // There's no concept of nested <select> elements, so as soon as we're exiting one of them,
                // we can safely say there is no longer any value for this
                _closestSelectValueAsString = null;
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

    private int RenderScriptElementChildren(int componentId, IElement output, ArrayRange<RenderTreeFrame> frames, int position, int maxElements)
    {
        // Inside a <script> context, AddContent calls should result in the text being
        // JavaScript encoded rather than HTML encoded. It's not that we recommend inserting
        // user-supplied content inside a <script> block, but that if someone does, we
        // want the encoding style to match the context for correctness and safety. This is
        // also consistent with .cshtml's treatment of <script>.
        var originalEncoder = _htmlEncoder;
        try
        {
            _htmlEncoder = _javaScriptEncoder;
            return RenderChildren(componentId, output, frames, position, maxElements);
        }
        finally
        {
            _htmlEncoder = originalEncoder;
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
        IElement output,
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
            EmitFormActionIfNotExplicit(output, isForm, hasExplicitActionValue: false);
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

                EmitFormActionIfNotExplicit(output, isForm, hasExplicitActionValue);
                return candidateIndex;
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
                    output.SetAttribute(frame.AttributeName, null);
                    break;
                case string value:
                    output.SetAttribute(frame.AttributeName, value);
                    break;
                default:
                    break;
            }
        }

        EmitFormActionIfNotExplicit(output, isForm, hasExplicitActionValue);

        return position + maxElements;

        void EmitFormActionIfNotExplicit(IElement output, bool isForm, bool hasExplicitActionValue)
        {
            if (isForm && _navigationManager != null && !hasExplicitActionValue)
            {
                output.SetAttribute("action", GetRootRelativeUrlForFormAction(_navigationManager));
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

    private int RenderChildren(int componentId, IElement output, ArrayRange<RenderTreeFrame> frames, int position, int maxElements)
    {
        if (maxElements == 0)
        {
            return position;
        }

        return RenderFrames(componentId, output, frames, position, maxElements);
    }

    private int RenderChildComponent(IElement output, ArrayRange<RenderTreeFrame> frames, int position)
    {
        ref var frame = ref frames.Array[position];

        RenderChildComponent(output, ref frame);

        return position + frame.ComponentSubtreeLength;
    }
}