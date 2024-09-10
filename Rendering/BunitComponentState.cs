using AngleSharp;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace AngleSharpExperiments.Rendering;

public class BunitComponentState : ComponentState
{
    private readonly BunitRenderer renderer;
    private readonly List<BunitComponentState> children = [];
    private INodeList nodes = BunitComponentNodeList.Empty;
    private string? markup;

    public IReadOnlyList<BunitComponentState> Children => children;

    public INodeList Nodes
    {
        get => nodes;
        internal set
        {
            markup = null;
            nodes = value;
        }
    }

    public BunitRootComponentState? Root { get; }

    public BunitComponentState? Parent { get; }

    public string Markup
    {
        get
        {
            if (markup is null)
            {
                markup = nodes.Prettify();
            }

            return markup;
        }
    }

    public BunitComponentState(BunitRenderer renderer, int componentId, IComponent component, BunitComponentState parentComponentState)
        : base(renderer, componentId, component, parentComponentState)
    {
        this.renderer = renderer;
        Parent = parentComponentState;
        Parent.AddChild(this);
        Root = parentComponentState is BunitRootComponentState root
            ? root
            : Parent.Root;
    }

    protected BunitComponentState(BunitRenderer renderer, int componentId, IComponent component)
        : base(renderer, componentId, component, null)
    {
        this.renderer = renderer;
    }

    public override ValueTask DisposeAsync()
    {
        Parent?.RemoveChild(this);
        return base.DisposeAsync();
    }

    internal ArrayRange<RenderTreeFrame> GetCurrentRenderTreeFrames()
        => renderer.GetCurrentRenderTreeFrames(ComponentId);

    protected void AddChild(BunitComponentState componentState)
        => children.Add(componentState);

    protected void RemoveChild(BunitComponentState componentState)
        => children.Remove(componentState);
}
