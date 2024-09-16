using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AngleSharpExperiments.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using static AngleSharpExperiments.AngleSharpRendering.LogicalElementExtensions;

namespace AngleSharpExperiments.AngleSharpRendering;

internal class AngleSharpRenderer
{
    private const string internalAttributeNamePrefix = "__internal_";
    private const string eventPreventDefaultAttributeNamePrefix = "preventDefault_";
    private const string eventStopPropagationAttributeNamePrefix = "stopPropagation_";
    private readonly IHtmlParser htmlParser;
    private readonly EventDelegator eventDelegator;

    private Dictionary<int, LogicalElement> childComponentLocations = [];

    public AngleSharpRenderer(HtmlParser htmlParser)
    {
        eventDelegator = new EventDelegator();
        this.htmlParser = htmlParser;

        // We don't yet know whether or not navigation interception will be enabled, but in case it will be,
        // we wire up the navigation manager to the event delegator so it has the option to participate
        // in the synthetic event bubbling process later
        //NavigationManager.AttachToEventDelegator(eventDelegator);
    }

    public void AttachRootComponentToLogicalElement(int componentId, IElement element)
    {
        var logicalElement = ToLogicalElement(element);
        childComponentLocations[componentId] = logicalElement;
    }

    public void DisposeComponent(int componentId)
    {
        childComponentLocations.Remove(componentId);
    }

    public void DisposeEventHandler(ulong eventHandlerId)
    {
        eventDelegator.RemoveListener(eventHandlerId);
    }

    public void UpdateComponent(RenderBatch batch, int componentId, ArrayBuilderSegment<RenderTreeEdit> edits, ArrayRange<RenderTreeFrame> referenceFrames)
    {
        var element = childComponentLocations[componentId];
        if (element == null)
        {
            throw new InvalidOperationException($"No element is currently associated with component {componentId}");
        }

        var ownerDocument = GetClosestDomElement(element)?.GetRoot() as IDocument;
        var activeElementBefore = ownerDocument?.ActiveElement;

        ApplyEdits(batch, componentId, element, 0, edits, referenceFrames);

        // Try to restore focus in case it was lost due to an element move
        if (activeElementBefore is IHtmlElement htmlElement && ownerDocument?.ActiveElement != activeElementBefore)
        {
            htmlElement.DoFocus();
        }
    }

    public void ApplyEdits(RenderBatch batch, int componentId, LogicalElement parent, int childIndex, ArrayBuilderSegment<RenderTreeEdit> edits, ArrayRange<RenderTreeFrame> referenceFrames)
    {
        int currentDepth = 0;
        int childIndexAtCurrentDepth = childIndex;
        List<PermutationListEntry>? permutationList = null;

        var editsValues = edits.Array;
        var editsOffset = edits.Offset;
        var editsLength = edits.Count;
        var maxEditIndexExcl = editsOffset + editsLength;

        for (int editIndex = editsOffset; editIndex < maxEditIndexExcl; editIndex++)
        {
            var edit = editsValues[editIndex];
            switch (edit.Type)
            {
                case RenderTreeEditType.PrependFrame:
                    {
                        var frameIndex = edit.ReferenceFrameIndex;
                        var frame = referenceFrames.Array[frameIndex];
                        var siblingIndex = edit.SiblingIndex;
                        InsertFrame(batch, componentId, parent, childIndexAtCurrentDepth + siblingIndex, referenceFrames, frame, frameIndex);
                        break;
                    }
                case RenderTreeEditType.RemoveFrame:
                    {
                        var siblingIndex = edit.SiblingIndex;
                        RemoveLogicalChild(parent, childIndexAtCurrentDepth + siblingIndex);
                        break;
                    }
                case RenderTreeEditType.SetAttribute:
                    {
                        var frameIndex = edit.ReferenceFrameIndex;
                        var frame = referenceFrames.Array[frameIndex];
                        var siblingIndex = edit.SiblingIndex;
                        var element = GetLogicalChild(parent, childIndexAtCurrentDepth + siblingIndex);
                        if (element.Node is IElement domElement)
                        {
                            ApplyAttribute(batch, componentId, domElement, frame);
                        }
                        else
                        {
                            throw new InvalidOperationException("Cannot set attribute on non-element child");
                        }
                        break;
                    }
                case RenderTreeEditType.RemoveAttribute:
                    {
                        var siblingIndex = edit.SiblingIndex;
                        var element = GetLogicalChild(parent, childIndexAtCurrentDepth + siblingIndex);
                        if (element.Node is IElement domElement)
                        {
                            var attributeName = edit.RemovedAttributeName;
                            SetOrRemoveAttributeOrProperty(domElement, attributeName, null);
                        }
                        else
                        {
                            throw new InvalidOperationException("Cannot remove attribute from non-element child");
                        }
                        break;
                    }
                case RenderTreeEditType.UpdateText:
                    {
                        var frameIndex = edit.ReferenceFrameIndex;
                        var frame = referenceFrames.Array[frameIndex];
                        var siblingIndex = edit.SiblingIndex;
                        var textNode = GetLogicalChild(parent, childIndexAtCurrentDepth + siblingIndex);
                        if (textNode.Node is IText textElement)
                        {
                            textElement.TextContent = frame.TextContent;
                        }
                        else
                        {
                            throw new InvalidOperationException("Cannot set text content on non-text child");
                        }
                        break;
                    }
                case RenderTreeEditType.UpdateMarkup:
                    {
                        var frameIndex = edit.ReferenceFrameIndex;
                        var frame = referenceFrames.Array[frameIndex];
                        var siblingIndex = edit.SiblingIndex;
                        RemoveLogicalChild(parent, childIndexAtCurrentDepth + siblingIndex);
                        InsertMarkup(batch, parent, childIndexAtCurrentDepth + siblingIndex, frame);
                        break;
                    }
                case RenderTreeEditType.StepIn:
                    {
                        var siblingIndex = edit.SiblingIndex;
                        parent = GetLogicalChild(parent, childIndexAtCurrentDepth + siblingIndex);
                        currentDepth++;
                        childIndexAtCurrentDepth = 0;
                        break;
                    }
                case RenderTreeEditType.StepOut:
                    {
                        parent = GetLogicalParent(parent);
                        currentDepth--;
                        childIndexAtCurrentDepth = currentDepth == 0 ? childIndex : 0;
                        break;
                    }
                case RenderTreeEditType.PermutationListEntry:
                    {
                        permutationList ??= [];
                        permutationList.Add(new PermutationListEntry()
                        {
                            FromSiblingIndex = childIndexAtCurrentDepth + edit.SiblingIndex,
                            ToSiblingIndex = childIndexAtCurrentDepth + edit.MoveToSiblingIndex
                        });
                        break;
                    }
                case RenderTreeEditType.PermutationListEnd:
                    {
                        PermuteLogicalChildren(parent, permutationList);
                        permutationList = null;
                        break;
                    }
                default:
                    throw new InvalidOperationException($"Unknown edit type: {edit.Type}");
            }
        }
    }

    private int InsertFrame(RenderBatch batch, int componentId, LogicalElement parent, int childIndex, ArrayRange<RenderTreeFrame> frames, RenderTreeFrame frame, int frameIndex)
    {
        switch (frame.FrameType)
        {
            case RenderTreeFrameType.Element:
                InsertElement(batch, componentId, parent, childIndex, frames, frame, frameIndex);
                return 1;
            case RenderTreeFrameType.Text:
                InsertText(batch, parent, childIndex, frame);
                return 1;
            case RenderTreeFrameType.Attribute:
                throw new Exception("Attribute frames should only be present as leading children of element frames.");
            case RenderTreeFrameType.Component:
                InsertComponent(batch, parent, childIndex, frame);
                return 1;
            case RenderTreeFrameType.Region:
                return InsertFrameRange(batch, componentId, parent, childIndex, frames, frameIndex + 1, frameIndex + frame.RegionSubtreeLength);
            case RenderTreeFrameType.ElementReferenceCapture:
                if (parent.Node is IElement element)
                {
                    ApplyCaptureIdToElement(element, frame.ElementReferenceCaptureId);
                    return 0; // A "capture" is a child in the diff, but has no node in the DOM
                }
                else
                {
                    throw new Exception("Reference capture frames can only be children of element frames.");
                }
            case RenderTreeFrameType.Markup:
                InsertMarkup(batch, parent, childIndex, frame);
                return 1;
            case RenderTreeFrameType.NamedEvent: // Not used on the JS side
                return 0;
            default:
                throw new Exception($"Unknown frame type: {frame.FrameType}");
        }
    }

    private void InsertElement(RenderBatch batch, int componentId, LogicalElement parent, int childIndex, ArrayRange<RenderTreeFrame> frames, RenderTreeFrame frame, int frameIndex)
    {
        var tagName = frame.ElementName;

        var newDomElementRaw = (tagName == "svg" || IsSvgElement(parent)) ?
            parent.Node.Owner!.CreateElement("http://www.w3.org/2000/svg", tagName) :
            parent.Node.Owner!.CreateElement(tagName);
        var newElement = ToLogicalElement(newDomElementRaw);

        bool inserted = false;

        // Apply attributes
        var descendantsEndIndexExcl = frameIndex + frame.ElementSubtreeLength;
        for (var descendantIndex = frameIndex + 1; descendantIndex < descendantsEndIndexExcl; descendantIndex++)
        {
            var descendantFrame = batch.ReferenceFrames.Array[descendantIndex];
            if (descendantFrame.FrameType == RenderTreeFrameType.Attribute)
            {
                ApplyAttribute(batch, componentId, newDomElementRaw, descendantFrame);
            }
            else
            {
                InsertLogicalChild(newDomElementRaw, parent, childIndex);
                inserted = true;
                // As soon as we see a non-attribute child, all the subsequent child frames are
                // not attributes, so bail out and insert the remnants recursively
                InsertFrameRange(batch, componentId, newElement, 0, frames, descendantIndex, descendantsEndIndexExcl);
                break;
            }
        }

        // This element did not have any children, so it's not inserted yet.
        if (!inserted)
        {
            InsertLogicalChild(newDomElementRaw, parent, childIndex);
        }

        ApplyAnyDeferredValue(newDomElementRaw);
    }

    private void InsertComponent(RenderBatch batch, LogicalElement parent, int childIndex, RenderTreeFrame frame)
    {
        var containerElement = CreateAndInsertLogicalContainer(parent, childIndex);

        // All we have to do is associate the child component ID with its location. We don't actually
        // do any rendering here, because the diff for the child will appear later in the render batch.
        var childComponentId = frame.ComponentId;
        AttachComponentToElement(childComponentId, containerElement);
    }

    private void InsertText(RenderBatch batch, LogicalElement parent, int childIndex, RenderTreeFrame textFrame)
    {
        var textContent = textFrame.TextContent;
        var newTextNode = parent.Node.Owner!.CreateTextNode(textContent);
        InsertLogicalChild(newTextNode, parent, childIndex);
    }

    private void InsertMarkup(RenderBatch batch, LogicalElement parent, int childIndex, RenderTreeFrame markupFrame)
    {
        var markupContainer = CreateAndInsertLogicalContainer(parent, childIndex);

        var markupContent = markupFrame.MarkupContent;
        var parsedMarkup = ParseMarkup(parent, markupContent, IsSvgElement(parent));
        int logicalSiblingIndex = 0;
        while (parsedMarkup.FirstChild is not null)
        {
            InsertLogicalChild(parsedMarkup.FirstChild, markupContainer, logicalSiblingIndex++);
        }
    }

    private void ApplyAttribute(RenderBatch batch, int componentId, IElement toDomElement, RenderTreeFrame attributeFrame)
    {
        var attributeName = attributeFrame.AttributeName;
        var eventHandlerId = attributeFrame.AttributeEventHandlerId;

        if (eventHandlerId > 0)
        {
            var eventName = StripOnPrefix(attributeName);
            eventDelegator.SetListener(toDomElement, eventName, eventHandlerId, componentId);
            return;
        }

        SetOrRemoveAttributeOrProperty(toDomElement, attributeName, attributeFrame.AttributeValue?.ToString());
    }

    private int InsertFrameRange(RenderBatch batch, int componentId, LogicalElement parent, int childIndex, ArrayRange<RenderTreeFrame> frames, int startIndex, int endIndexExcl)
    {
        int origChildIndex = childIndex;
        for (int index = startIndex; index < endIndexExcl; index++)
        {
            var frame = batch.ReferenceFrames.Array[index];
            int numChildrenInserted = InsertFrame(batch, componentId, parent, childIndex, frames, frame, index);
            childIndex += numChildrenInserted;

            // Skip over any descendants, since they are already dealt with recursively
            index += CountDescendantFrames(batch, frame);
        }

        return (childIndex - origChildIndex); // Total number of children inserted
    }

    private void SetOrRemoveAttributeOrProperty(IElement element, string name, string? valueOrNullToRemove)
    {
        // First see if we have special handling for this attribute
        if (!element.TryApplySpecialProperty(name, valueOrNullToRemove))
        {
            // If not, maybe it's one of our internal attributes
            if (name.StartsWith(internalAttributeNamePrefix))
            {
                ApplyInternalAttribute(element, name.Substring(internalAttributeNamePrefix.Length), valueOrNullToRemove);
            }
            else
            {
                // If not, treat it as a regular DOM attribute
                if (valueOrNullToRemove is not null)
                {
                    element.SetAttribute(name, valueOrNullToRemove);
                }
                else
                {
                    element.RemoveAttribute(name);
                }
            }
        }
    }

    private void ApplyInternalAttribute(IElement element, string internalAttributeName, object? value)
    {
        if (internalAttributeName.StartsWith(eventStopPropagationAttributeNamePrefix))
        {
            // Stop propagation
            var eventName = StripOnPrefix(internalAttributeName.Substring(eventStopPropagationAttributeNamePrefix.Length));
            eventDelegator.SetStopPropagation(element, eventName, value is not null);
        }
        else if (internalAttributeName.StartsWith(eventPreventDefaultAttributeNamePrefix))
        {
            // Prevent default
            var eventName = StripOnPrefix(internalAttributeName.Substring(eventPreventDefaultAttributeNamePrefix.Length));
            eventDelegator.SetPreventDefault(element, eventName, value is not null);
        }
        else
        {
            // The prefix makes this attribute name reserved, so any other usage is disallowed
            throw new Exception($"Unsupported internal attribute '{internalAttributeName}'");
        }
    }

    private int CountDescendantFrames(RenderBatch batch, RenderTreeFrame frame)
    {
        switch (frame.FrameType)
        {
            // The following frame types have a subtree length. Other frames may use that memory slot
            // to mean something else, so we must not read it. We should consider having nominal subtypes
            // of RenderTreeFramePointer that prevent access to non-applicable fields.
            case RenderTreeFrameType.Component:
                return frame.ComponentSubtreeLength - 1;
            case RenderTreeFrameType.Element:
                return frame.ElementSubtreeLength - 1;
            case RenderTreeFrameType.Region:
                return frame.RegionSubtreeLength - 1;
            default:
                return 0;
        }
    }


    private IDocumentFragment ParseMarkup(LogicalElement parent, string markupContent, bool isSvg)
    {
        var nodes = htmlParser.ParseFragment(markupContent, (IElement)parent.Node);
        var result = parent.Node.Owner.CreateDocumentFragment();
        foreach (var node in nodes)
        {
            result.AppendChild(node);
        }
        return result;
    }

    private void AttachComponentToElement(int childComponentId, LogicalElement containerElement)
    {
        childComponentLocations[childComponentId] = containerElement;
    }

    private void ApplyAnyDeferredValue(IElement newDomElementRaw)
    {
        // spacial case handling over in D:\dotnet\aspnetcore\src\Components\Web.JS\src\Rendering\DomSpecialPropertyUtil.ts
    }

    private void ApplyCaptureIdToElement(IElement element, string referenceCaptureId)
    {
        element.SetAttribute($"_bl_{referenceCaptureId}", null);
    }

    private string StripOnPrefix(string attributeName)
    {
        if (attributeName.StartsWith("on"))
        {
            return attributeName.Substring(2);
        }

        throw new Exception($"Attribute should be an event name, but doesn't start with 'on'. Value: '{attributeName}'");
    }
}