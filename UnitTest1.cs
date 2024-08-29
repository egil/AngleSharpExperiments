using AngleSharp;
using AngleSharpExperiments.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngleSharpExperiments;

public class UnitTest1
{
    [Fact]
    public async Task Test1()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var renderer = new BunitRenderer(services, NullLoggerFactory.Instance);
        var cut = await renderer.RenderComponentAsync(typeof(Root), ParameterView.Empty);
        var raw = cut.Document.Prettify();
    }
}