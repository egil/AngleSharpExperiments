using System.Diagnostics;
using AngleSharp;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AngleSharpExperiments;

public partial class BunitRenderer : Renderer
{
    private readonly NavigationManager? _navigationManager;
    private readonly IBrowsingContext context;
    private readonly IHtmlParser htmlParser;
    private TaskCompletionSource<BunitComponentState> renderCompleted = new();

    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    public BunitRenderer(IServiceProvider serviceProvider, ILoggerFactory loggerFactory) : base(serviceProvider, loggerFactory)
    {
        _navigationManager = serviceProvider.GetService<NavigationManager>();
        var config = Configuration.Default
            .With<IHtmlParser>(_ => new HtmlParser(new HtmlParserOptions
            {
                IsAcceptingCustomElementsEverywhere = true,
                IsEmbedded = true,
                IsKeepingSourceReferences = true,
                IsPreservingAttributeNames = true,
                OnCreated = (elm, pos) =>
                {
                },
            }));

        context = BrowsingContext.New(config);
        htmlParser = context.GetService<IHtmlParser>()!;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            context.Dispose();
        }

        base.Dispose(disposing);
    }

    public async Task<BunitComponentState> RenderComponentAsync(Type componentType, ParameterView parameters)
    {
        var componentId = await Dispatcher.InvokeAsync(async () =>
        {
            var component = InstantiateComponent(componentType);
            var componentId = AssignRootComponentId(component);
            await RenderRootComponentAsync(componentId, parameters);
            return componentId;
        });

        return (BunitComponentState)GetComponentState(componentId);
    }

    protected override ComponentState CreateComponentState(int componentId, IComponent component, ComponentState? parentComponentState)
    {
        if (parentComponentState is BunitComponentState parent)
        {
            return new BunitComponentState(this, componentId, component, parent);
        }
        else
        {
            var doc = context.OpenAsync(r => r.Content(string.Empty));
            Debug.Assert(doc.IsCompletedSuccessfully, "Should complete immediately");
            return new BunitComponentState(this, componentId, component, doc.Result);
        }
    }

    /// <inheritdoc />
    protected override Task UpdateDisplayAsync(in RenderBatch batch)
    {
        var componentState = (BunitComponentState)GetComponentState(0);
        WriteComponentHtml(0, componentState.Document.Body!);
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
