using AngleSharp;
using AngleSharp.Html.Parser;
using AngleSharpExperiments.AngleSharpRendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace AngleSharpExperiments.Rendering;

public partial class BunitRenderer : Renderer
{
    private readonly AngleSharp.IConfiguration angleSharpConfiguration;
    private readonly IBrowsingContext angleSharpContext;
    private readonly HtmlParser htmlParser;
    private readonly AngleSharpRenderer angleSharpRenderer;
    private TaskCompletionSource<BunitComponentState> renderCompleted = new();

    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    public IServiceProvider Services { get; }

    public BunitRenderer(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
        : base(serviceProvider, loggerFactory)
    {
        htmlParser = new HtmlParser(new HtmlParserOptions
        {
            IsAcceptingCustomElementsEverywhere = true,
            IsEmbedded = true,
            IsKeepingSourceReferences = true,
            IsPreservingAttributeNames = true,
        });
        angleSharpRenderer = new AngleSharpRenderer(htmlParser);
        Services = serviceProvider;

        angleSharpConfiguration = Configuration
            .Default
            .WithOnly<IHtmlParser>(htmlParser)
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

        //await renderCompleted.Task;
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
            var doc = angleSharpContext.OpenAsync(r => r.Content(string.Empty)).GetAwaiter().GetResult();
            angleSharpRenderer.AttachRootComponentToLogicalElement(componentId, doc.Body!);
            return new BunitRootComponentState(this, componentId, component, doc);
        }
    }

    internal new BunitComponentState GetComponentState(int componentId)
        => (BunitComponentState)base.GetComponentState(componentId);

    internal BunitRootComponentState GetRootComponentState(int componentId)
        => (BunitRootComponentState)base.GetComponentState(componentId);

    internal new ArrayRange<RenderTreeFrame> GetCurrentRenderTreeFrames(int componentId)
        => base.GetCurrentRenderTreeFrames(componentId);

    /// <inheritdoc />
    //protected override Task UpdateDisplayAsync(in RenderBatch batch)
    //{
    //    var componentState = GetRootComponentState(0); // TODO dont hardcode componentId here.
    //    componentState.UpdateDom();
    //    renderCompleted.TrySetResult(componentState);
    //    return Task.CompletedTask;
    //}

    protected override Task UpdateDisplayAsync(in RenderBatch batch)
    {
        var updatedComponents = batch.UpdatedComponents;
        var referenceFrames = batch.ReferenceFrames;

        for (int i = 0; i < batch.UpdatedComponents.Count; i++)
        {
            ref var diff = ref updatedComponents.Array[i];
            var componentId = diff.ComponentId;
            var edits = diff.Edits;
            angleSharpRenderer.UpdateComponent(batch, componentId, edits, referenceFrames);
        }

        var disposedComponentIDs = batch.DisposedComponentIDs;
        for (int i = 0; i < disposedComponentIDs.Count; i++)
        {
            var componentId = disposedComponentIDs.Array[i];
            angleSharpRenderer.DisposeComponent(componentId);
        }

        var disposedEventHandlerIDs = batch.DisposedEventHandlerIDs;
        for (int i = 0; i < disposedEventHandlerIDs.Count; i++)
        {
            var eventHandlerId = disposedEventHandlerIDs.Array[i];
            angleSharpRenderer.DisposeEventHandler(eventHandlerId);
        }

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
