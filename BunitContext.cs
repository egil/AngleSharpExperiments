using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngleSharpExperiments;

internal class BunitContext : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly BunitRenderer renderer;

    public BunitContext()
    {
        services = new ServiceCollection().BuildServiceProvider();
        renderer = new BunitRenderer(services, NullLoggerFactory.Instance);
    }

    public async ValueTask<BunitComponentState> RenderAsync<TComponent>()
        where TComponent : IComponent 
        => await renderer.RenderComponentAsync(typeof(TComponent), ParameterView.Empty);

    public async ValueTask DisposeAsync()
    {
        await renderer.DisposeAsync();
        await services.DisposeAsync();
    }
}
