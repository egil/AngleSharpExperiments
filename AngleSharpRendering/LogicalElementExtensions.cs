using AngleSharp.Dom;

namespace AngleSharpExperiments.AngleSharpRendering;

public static class LogicalElementExtensions
{
    private static readonly Dictionary<INode, LogicalElement> map = [];

    public static LogicalElement ToLogicalElement(INode element, bool allowExistingContents = false)
    {
        if (map.TryGetValue(element, out LogicalElement? value))
        {
            // If it's already a logical element, return it
            return value;
        }

        var logicalElement = new LogicalElement(element);
        map[element] = logicalElement;

        if (element.ChildNodes.Length > 0)
        {
            if (!allowExistingContents)
            {
                throw new InvalidOperationException("New logical elements must start empty, or allowExistingContents must be true");
            }

            foreach (var child in element.ChildNodes)
            {
                var childLogicalElement = ToLogicalElement(child, true);
                childLogicalElement.LogicalParent = logicalElement;
                logicalElement.LogicalChildren.Add(childLogicalElement);
            }
        }

        return logicalElement;
    }

    public static void EmptyLogicalElement(LogicalElement element)
    {
        var childrenArray = GetLogicalChildrenArray(element);
        while (childrenArray?.Count > 0)
        {
            RemoveLogicalChild(element, 0);
        }
    }

    public static LogicalElement CreateAndInsertLogicalContainer(LogicalElement parent, int childIndex)
    {
        var containerElement = parent.Node.Owner!.CreateComment("!");
        InsertLogicalChild(containerElement, parent, childIndex);
        return ToLogicalElement(containerElement);
    }

    public static void InsertLogicalChild(INode child, LogicalElement parent, int childIndex)
    {
        var childAsLogicalElement = ToLogicalElement(child);

        // If the child is a component comment with logical children, its children
        // need to be inserted into the parent node
        INode nodeToInsert = child;
        if (child is IComment)
        {
            var existingGrandchildren = GetLogicalChildrenArray(childAsLogicalElement);
            if (existingGrandchildren?.Count > 0)
            {
                var lastNodeToInsert = FindLastDomNodeInRange(childAsLogicalElement);
                var range = child.Owner.CreateRange();
                range.StartBefore(child);
                range.EndAfter(lastNodeToInsert);
                nodeToInsert = range.ExtractContent();
            }
        }

        // If the node we're inserting already has a logical parent,
        // remove it from its sibling array
        var existingLogicalParent = childAsLogicalElement.LogicalParent;
        if (existingLogicalParent is not null)
        {
            var existingSiblingArray = GetLogicalChildrenArray(existingLogicalParent);
            var existingChildIndex = existingSiblingArray.IndexOf(childAsLogicalElement);
            existingSiblingArray.RemoveAt(existingChildIndex);
            childAsLogicalElement.LogicalParent = null;
        }

        var newSiblings = GetLogicalChildrenArray(parent);
        if (childIndex < newSiblings.Count)
        {
            // Insert
            var nextSibling = newSiblings[childIndex].Node;
            nextSibling.Parent.InsertBefore(nodeToInsert, nextSibling);
            newSiblings.Insert(childIndex, childAsLogicalElement);
        }
        else
        {
            // Append
            AppendDomNode(nodeToInsert, parent);
            newSiblings.Add(childAsLogicalElement);
        }

        childAsLogicalElement.LogicalParent = parent;
    }

    public static void RemoveLogicalChild(LogicalElement parent, int childIndex)
    {
        var childrenArray = GetLogicalChildrenArray(parent);
        var childToRemove = childrenArray[childIndex];
        childrenArray.RemoveAt(childIndex);

        // If it's a logical container, also remove its descendants
        if (childToRemove.Node is IComment)
        {
            var grandchildrenArray = GetLogicalChildrenArray(childToRemove);
            if (grandchildrenArray != null)
            {
                while (grandchildrenArray.Count > 0)
                {
                    RemoveLogicalChild(childToRemove, 0);
                }
            }
        }

        // Finally, remove the node itself
        var domNodeToRemove = childToRemove.Node;
        domNodeToRemove.Parent!.RemoveChild(domNodeToRemove);

        // Added in C# version
        childToRemove.LogicalParent = null;
        map.Remove(domNodeToRemove);
    }

    public static LogicalElement? GetLogicalParent(LogicalElement element)
    {
        return element.LogicalParent;
    }

    public static LogicalElement GetLogicalChild(LogicalElement parent, int childIndex)
    {
        var childrenArray = GetLogicalChildrenArray(parent);
        return childrenArray[childIndex];
    }

    /// <summary>
    /// SVG elements support `foreignObject` children that can hold arbitrary HTML.
    /// For these scenarios, the parent SVG and `foreignObject` elements should
    /// be rendered under the SVG namespace, while the HTML content should be rendered
    /// under the XHTML namespace. If the correct namespaces are not provided, most
    /// browsers will fail to render the foreign object content. Here, we ensure that if
    /// we encounter a `foreignObject` in the SVG, then all its children will be placed
    /// under the XHTML namespace.
    /// </summary>
    public static bool IsSvgElement(LogicalElement element)
    {
        // Note: This check is intentionally case-sensitive since we expect this element
        // to appear as a child of an SVG element and SVGs are case-sensitive.
        var closestElement = GetClosestDomElement(element);
        return closestElement.NamespaceUri == "http://www.w3.org/2000/svg" 
            && closestElement.TagName != "foreignObject";
    }

    public static List<LogicalElement> GetLogicalChildrenArray(LogicalElement element)
    {
        return element.LogicalChildren;
    }

    public static LogicalElement? GetLogicalNextSibling(LogicalElement element)
    {
        var parent = GetLogicalParent(element);
        if (parent is null)
        {
            return null;
        }

        var siblings = GetLogicalChildrenArray(parent);
        var siblingIndex = siblings.IndexOf(element);
        return siblingIndex >= 0 && siblingIndex < siblings.Count - 1
            ? siblings[siblingIndex + 1]
            : null;
    }

    public static bool IsLogicalElement(INode element)
    {
        return map.ContainsKey(element);
    }

    public static void InsertLogicalChildBefore(INode child, LogicalElement parent, LogicalElement? before)
    {
        var childrenArray = GetLogicalChildrenArray(parent);
        int childIndex;
        if (before is not null)
        {
            childIndex = childrenArray.IndexOf(before);
            if (childIndex < 0)
            {
                throw new Exception("Could not find logical element in the parent logical node list");
            }
        }
        else
        {
            childIndex = childrenArray.Count;
        }
        InsertLogicalChild(child, parent, childIndex);
    }

    /// <summary>
    /// The permutationList must represent a valid permutation, i.e., the list of 'from' indices
    /// is distinct, and the list of 'to' indices is a permutation of it. The algorithm here
    /// relies on that assumption.
    ///
    /// Each of the phases here has to happen separately, because each one is designed not to
    /// interfere with the indices or DOM entries used by subsequent phases.
    /// </summary>
    public static void PermuteLogicalChildren(LogicalElement parent, List<PermutationListEntry> permutationList)
    {
        // Phase 1: track which nodes we will move
        var siblings = GetLogicalChildrenArray(parent);
        foreach (var listEntry in permutationList)
        {
            listEntry.MoveRangeStart = siblings[listEntry.FromSiblingIndex];
            listEntry.MoveRangeEnd = FindLastDomNodeInRange(listEntry.MoveRangeStart);
        }

        // Phase 2: insert markers
        foreach (var listEntry in permutationList)
        {
            var marker = parent.Node.Owner.CreateComment("marker");
            listEntry.MoveToBeforeMarker = marker;
            var insertBeforeNode = siblings.ElementAtOrDefault(listEntry.ToSiblingIndex + 1)?.Node;
            if (insertBeforeNode != null)
            {
                insertBeforeNode.Parent.InsertBefore(marker, insertBeforeNode);
            }
            else
            {
                AppendDomNode(marker, parent);
            }
        }

        // Phase 3: move descendants & remove markers
        foreach (var listEntry in permutationList)
        {
            var insertBefore = listEntry.MoveToBeforeMarker;
            var parentDomNode = insertBefore.Parent;
            var elementToMove = listEntry.MoveRangeStart.Node;
            var moveEndNode = listEntry.MoveRangeEnd;
            var nextToMove = elementToMove;
            while (nextToMove is not null)
            {
                var nextNext = nextToMove.NextSibling;
                parentDomNode.InsertBefore(nextToMove, insertBefore);

                if (nextToMove == moveEndNode)
                {
                    break;
                }
                else
                {
                    nextToMove = nextNext;
                }
            }

            parentDomNode.RemoveChild(insertBefore);
        }

        // Phase 4: update siblings index
        foreach (var listEntry in permutationList)
        {
            siblings[listEntry.ToSiblingIndex] = listEntry.MoveRangeStart;
        }
    }

    public static IElement? GetClosestDomElement(LogicalElement logicalElement)
    {
        if (logicalElement.Node is IElement element)
        {
            return element;
        }
        else if (logicalElement.Node is IComment comment)
        {
            return comment.ParentElement;
        }
        else
        {
            throw new InvalidOperationException("Not a valid logical element");
        }
    }

    public static void AppendDomNode(INode child, LogicalElement parent)
    {
        // This function only puts 'child' into the DOM in the right place relative to 'parent'
        // It does not update the logical children array of anything
        if (parent.Node is IElement || parent.Node is IDocumentFragment)
        {
            parent.Node.AppendChild(child);
        }
        else if (parent.Node is IComment)
        {
            var parentLogicalNextSibling = GetLogicalNextSibling(parent)?.Node;
            if (parentLogicalNextSibling != null)
            {
                // Since the parent has a logical next-sibling, its appended child goes right before that
                parentLogicalNextSibling.Parent.InsertBefore(child, parentLogicalNextSibling);
            }
            else
            {
                // Since the parent has no logical next-sibling, keep recursing upwards until we find
                // a logical ancestor that does have a next-sibling or is a physical element.
                var logicalParent = GetLogicalParent(parent);
                if (logicalParent != null)
                {
                    AppendDomNode(child, logicalParent);
                }
                else
                {
                    throw new InvalidOperationException("Cannot append node because the parent is not a valid logical element.");
                }
            }
        }
        else
        {
            // Should never happen
            throw new InvalidOperationException("Cannot append node because the parent is not a valid logical element.");
        }
    }

    public static INode? FindLastDomNodeInRange(LogicalElement element)
    {
        // Returns the final node (in depth-first evaluation order) that is a descendant of the logical element.
        // As such, the entire subtree is between 'element' and 'findLastDomNodeInRange(element)' inclusive.
        if (element.Node is IElement || element.Node is IDocumentFragment)
        {
            return element.Node;
        }

        var nextSibling = GetLogicalNextSibling(element);
        if (nextSibling is not null)
        {
            // Simple case: not the last logical sibling, so take the node before the next sibling
            return nextSibling.Node.PreviousSibling;
        }
        else
        {
            // Harder case: there's no logical next-sibling, so recurse upwards until we find
            // a logical ancestor that does have one, or a physical element
            var logicalParent = GetLogicalParent(element);
            if (logicalParent != null)
            {
                return logicalParent.Node is IElement || logicalParent.Node is IDocumentFragment
                    ? logicalParent.Node.LastChild
                    : FindLastDomNodeInRange(logicalParent);
            }
            else
            {
                throw new InvalidOperationException("Logical element has no parent.");
            }
        }
    }
}