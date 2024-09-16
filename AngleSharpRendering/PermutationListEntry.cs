using AngleSharp.Dom;

namespace AngleSharpExperiments.AngleSharpRendering;

public class PermutationListEntry
{
    public int FromSiblingIndex { get; set; }
    public int ToSiblingIndex { get; set; }
    public LogicalElement MoveRangeStart { get; set; }
    public INode MoveRangeEnd { get; set; }
    public INode MoveToBeforeMarker { get; set; }
}
