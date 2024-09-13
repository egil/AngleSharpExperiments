using AngleSharp.Dom;

namespace AngleSharpExperiments.AngleSharpRendering;

public class PermutationListEntry
{
    public int FromSiblingIndex { get; set; }
    public int ToSiblingIndex { get; set; }
    public required LogicalElement MoveRangeStart { get; set; }
    public required INode MoveRangeEnd { get; set; }
    public required INode MoveToBeforeMarker { get; set; }
}
