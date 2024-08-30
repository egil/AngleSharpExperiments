using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AngleSharpExperiments.Rendering;

public class BunitRootComponentState : BunitComponentState
{
    private readonly AngleSharpDomBuilder domBuilder;

    public Dictionary<INode, int> NodeComponentMap { get; } = new();

    public IDocument Document { get; }

    public BunitRootComponentState(BunitRenderer renderer, int componentId, IComponent component, IDocument document)
        : base(renderer, componentId, component)
    {
        Document = document;
        Document.Body!.SetAttribute("bunit-component-id", componentId.ToString());
        NodeComponentMap.Add(Document.Body, componentId);
        domBuilder = new AngleSharpDomBuilder(
            this,
            renderer,
            renderer.Services.GetService<NavigationManager>(),
            document.Context.GetService<IHtmlParser>() ?? throw new InvalidOperationException("Missing IHtmlParser in AngleSharp context."));
    }

    public override ValueTask DisposeAsync()
    {
        Document.Dispose();
        return base.DisposeAsync();
    }

    internal void UpdateDom()
    {
        domBuilder.BuildRootComponentDom();
    }
}
