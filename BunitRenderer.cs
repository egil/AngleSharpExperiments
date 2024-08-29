using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AngleSharpExperiments;

public partial class BunitRenderer : Renderer
{
    private readonly NavigationManager? _navigationManager;
    private readonly IBrowsingContext context;
    private readonly IHtmlParser htmlParser;
    private IDocument document;

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

    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    public async Task<IDocument> RenderComponentAsync(Type componentType, ParameterView parameters, string domElementSelector)
    {
        var component = InstantiateComponent(componentType);
        var componentId = AssignRootComponentId(component);
        await RenderRootComponentAsync(componentId, parameters);
        document = await context.OpenAsync(r => r.Content(""));
        return document;
    }

    /// <inheritdoc />
    protected override Task UpdateDisplayAsync(in RenderBatch batch)
    {
        if (document?.Body is { } body)
        {
            WriteComponentHtml(0, body);
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
                Console.WriteLine(innerException.Message);
            }
        }
        else
        {
            Console.WriteLine(exception.Message);
        }
    }
}
