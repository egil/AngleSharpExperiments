using System.Diagnostics;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Mathml.Dom;
using AngleSharp.Svg.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AngleSharpExperiments.Rendering;

class BunitElementFactory : IElementFactory<Document, HtmlElement>, IElementFactory<Document, MathElement>, IElementFactory<Document, SvgElement>
{
    MathElement IElementFactory<Document, MathElement>.Create(Document document, string localName, string? prefix, NodeFlags flags)
    {
        throw new NotImplementedException();
    }

    SvgElement IElementFactory<Document, SvgElement>.Create(Document document, string localName, string? prefix, NodeFlags flags)
    {
        throw new NotImplementedException();
    }

    HtmlElement IElementFactory<Document, HtmlElement>.Create(Document document, string localName, string? prefix, NodeFlags flags)
    {
        throw new NotImplementedException();
    }
}

public partial class BunitRenderer : Renderer
{
    private readonly IConfiguration angleSharpConfiguration;
    private readonly IBrowsingContext angleSharpContext;
    private TaskCompletionSource<BunitComponentState> renderCompleted = new();

    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    public IServiceProvider Services { get; }

    public BunitRenderer(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
        : base(serviceProvider, loggerFactory)
    {
        var elmFactory = new BunitElementFactory();
        Services = serviceProvider;
        angleSharpConfiguration = Configuration
            .Default
            //.Without<IElementFactory<Document, HtmlElement>>()
            //.Without<IElementFactory<Document, MathElement>>()
            //.Without<IElementFactory<Document, SvgElement>>()
            //.With(elmFactory)
            .WithOnly<IHtmlParser>(_ => new HtmlParser(new HtmlParserOptions
            {
                IsAcceptingCustomElementsEverywhere = true,
                IsEmbedded = true,
                IsKeepingSourceReferences = true,
                IsPreservingAttributeNames = true,
            }))
            .WithOnly(this);

        angleSharpContext = BrowsingContext.New(angleSharpConfiguration);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            angleSharpContext.Dispose();
        }

        base.Dispose(disposing);
    }

    public async Task<BunitRootComponentState> RenderComponentAsync(Type componentType, ParameterView parameters)
    {
        var componentId = await Dispatcher.InvokeAsync(async () =>
        {
            var component = InstantiateComponent(componentType);
            var componentId = AssignRootComponentId(component);
            await RenderRootComponentAsync(componentId, parameters);
            return componentId;
        });

        await renderCompleted.Task;
        var rootComponentState = GetRootComponentState(componentId);
        return rootComponentState;
    }

    protected override ComponentState CreateComponentState(int componentId, IComponent component, ComponentState? parentComponentState)
    {
        if (parentComponentState is BunitComponentState parent)
        {
            return new BunitComponentState(this, componentId, component, parent);
        }
        else
        {
            var doc = angleSharpContext.OpenAsync(r => r.Content(string.Empty));
            Debug.Assert(doc.IsCompleted, "Should complete immediately");
            return new BunitRootComponentState(this, componentId, component, doc.Result);
        }
    }

    internal new BunitComponentState GetComponentState(int componentId)
        => (BunitComponentState)base.GetComponentState(componentId);

    internal BunitRootComponentState GetRootComponentState(int componentId)
        => (BunitRootComponentState)base.GetComponentState(componentId);

    internal new ArrayRange<RenderTreeFrame> GetCurrentRenderTreeFrames(int componentId)
        => base.GetCurrentRenderTreeFrames(componentId);

    /// <inheritdoc />
    protected override Task UpdateDisplayAsync(in RenderBatch batch)
    {
        var componentState = GetRootComponentState(0); // TODO dont hardcode componentId here.
        componentState.UpdateDom();
        renderCompleted.TrySetResult(componentState);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void HandleException(Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            foreach (var innerException in aggregateException.Flatten().InnerExceptions)
            {
                renderCompleted.TrySetException(innerException);
            }
        }
        else
        {
            renderCompleted.TrySetException(exception);
        }
    }
}
