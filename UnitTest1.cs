using System;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharpExperiments.Components;
using AngleSharpExperiments.Rendering;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AngleSharpExperiments;

public class UnitTest1
{
    [Fact]
    public async Task Test1()
    {
        await using var ctx = new BunitContext();
        var cut = await ctx.RenderAsync<Root>();
        var rawDoc = cut.Document.Body!.ChildNodes.Prettify();
        var raw = cut.Nodes.Prettify();

        foreach (var child in cut.Children)
        {
            var rawChild = child.Nodes.Prettify();
        }
        var elm = cut.Children[2].Children[0].Nodes[0].LastChild!.TextContent = "HELLO WORLD!";
        var rawDoc2 = cut.Document.Body.ChildNodes.Prettify();
        var raw2 = cut.Nodes.Prettify();
    }

    [Fact]
    public async Task Adhoc_vs_bunit()
    {
        using var bunitV1 = new TestContext();
        await using var bunitV2 = new BunitContext();
        var cutV1 = bunitV1.RenderComponent<Root>();
        var cutV2 = await bunitV2.RenderAsync<Root>();

        var rawV1 = cutV1.Nodes.Prettify();
        var rawV2 = cutV2.Nodes.Prettify();

        Assert.Equal(rawV1, rawV2, ignoreCase: true);
    }
}