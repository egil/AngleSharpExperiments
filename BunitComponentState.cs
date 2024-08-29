using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace AngleSharpExperiments;

public class BunitComponentState : ComponentState
{
    private readonly IDocument? document;
    private readonly List<BunitComponentState> children = [];
    private INodeList nodes = BunitComponentNodeList.Empty;

    public IDocument Document => document ?? Parent!.Document;

    public BunitComponentState? Parent { get; }

    public IReadOnlyList<BunitComponentState> Children => children;

    public INodeList Nodes { get => nodes; set => nodes = value; }

    public BunitComponentState(Renderer renderer, int componentId, IComponent component, IDocument document)
        : base(renderer, componentId, component, null)
    {
        this.document = document;
    }

    public BunitComponentState(Renderer renderer, int componentId, IComponent component, BunitComponentState parentComponentState)
        : base(renderer, componentId, component, parentComponentState)
    {
        Parent = parentComponentState;
        Parent.children.Add(this);
    }

    public override ValueTask DisposeAsync()
    {
        Parent?.children.Remove(this);
        document?.Dispose();
        return base.DisposeAsync();
    }
}
