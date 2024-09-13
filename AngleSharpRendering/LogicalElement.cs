using AngleSharp.Dom;
using System;
using System.Collections.Generic;

namespace AngleSharpExperiments.AngleSharpRendering;

//  A LogicalElement plays the same role as an Element instance from the point of view of the
//  API consumer. Inserting and removing logical elements updates the browser DOM just the same.
//
//  The difference is that, unlike regular DOM mutation APIs, the LogicalElement APIs don't use
//  the underlying DOM structure as the data storage for the element hierarchy. Instead, the
//  LogicalElement APIs take care of tracking hierarchical relationships separately. The point
//  of this is to permit a logical tree structure in which parent/child relationships don't
//  have to be materialized in terms of DOM element parent/child relationships. And the reason
//  why we want that is so that hierarchies of Razor components can be tracked even when those
//  components' render output need not be a single literal DOM element.
//
//  Consumers of the API don't need to know about the implementation, but how it's done is:
//  - Each LogicalElement is materialized in the DOM as either:
//    - A Node instance, for actual Node instances inserted using 'insertLogicalChild' or
//      for Element instances promoted to LogicalElement via 'toLogicalElement'
//    - A Comment instance, for 'logical container' instances inserted using 'createAndInsertLogicalContainer'
//  - Then, on that instance (i.e., the Node or Comment), we store an array of 'logical children'
//    instances, e.g.,
//      [firstChild, secondChild, thirdChild, ...]
//    ... plus we store a reference to the 'logical parent' (if any)
//  - The 'logical children' array means we can look up in O(1):
//    - The number of logical children (not currently implemented because not required, but trivial)
//    - The logical child at any given index
//  - Whenever a logical child is added or removed, we update the parent's array of logical children
public class LogicalElement(INode node, LogicalElement? parent = null)
{
    private List<LogicalElement>? logicalChildren;

    public INode Node { get; } = node;

    public List<LogicalElement> LogicalChildren { get => logicalChildren ?? (logicalChildren = []); }

    public LogicalElement? LogicalParent { get; set; } = parent;
}
