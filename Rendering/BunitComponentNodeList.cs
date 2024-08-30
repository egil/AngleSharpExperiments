using System.Collections;
using System.Diagnostics;
using AngleSharp;
using AngleSharp.Dom;

namespace AngleSharpExperiments.Rendering;

internal class BunitComponentNodeList : INodeList
{
    internal static readonly BunitComponentNodeList Empty = new(null!);
    internal readonly List<INode> entries;

    public INode this[int index]
    {
        get => entries[index];
    }

    public int Length
    {
        get => entries.Count;
    }

    public INode Parent { get; }

    internal BunitComponentNodeList(INode parent)
    {
        entries = [];
        Parent = parent;
    }

    public void Add(INode node)
    {
        Debug.Assert(ReferenceEquals(node.Parent, Parent), "The list should only contain nodes at the same level in the DOM tree.");
        entries.Add(node);
    }

    public void AddRange(IEnumerable<INode> nodes)
    {
        foreach (var node in nodes)
        {
            Add(node);
        }
    }

    public void ToHtml(TextWriter writer, IMarkupFormatter formatter)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            entries[i].ToHtml(writer, formatter);
        }
    }

    public IEnumerator<INode> GetEnumerator() => entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => entries.GetEnumerator();
}
