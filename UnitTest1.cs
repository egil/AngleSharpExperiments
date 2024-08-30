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
    public async Task V1_vs_V2prototype()
    {
        using var bunitV1 = new TestContext();
        await using var bunitV2 = new BunitContext();
        var cutV1 = bunitV1.RenderComponent<Root>();
        var cutV2 = await bunitV2.RenderAsync<Root>();

        var rawV1 = cutV1.Nodes.Prettify();
        var rawV2 = cutV2.Nodes.Prettify();

        Assert.Equal(rawV1, rawV2, ignoreCase: true);
    }

    [Fact]
    public async Task FindComponentFromNode()
    {
        await using var bunitV2 = new BunitContext();
        var cutV2 = await bunitV2.RenderAsync<Root>();
        var addr = cutV2.Nodes.QuerySelector("address");

        var owningComponent = addr.GetComponent();

        Assert.NotNull(owningComponent);
    }
}

public static class BunitNodeExtensions
{
    public static BunitComponentState? GetComponent(this INode node)
    {
        var renderer = node.Owner!.Context.GetService<BunitRenderer>()!;
        var rootComponentId = int.Parse(node.Owner!.Body!.GetAttribute("bunit-component-id")!);
        var rootComponentState = renderer.GetRootComponentState(rootComponentId);

        INode? candidate = node;
        do
        {
            if (rootComponentState.NodeComponentMap.TryGetValue(candidate, out var componentId))
            {
                return renderer.GetComponentState(componentId);
            }
            candidate = candidate.Parent;
        } while (candidate is not null);

        return null;
    }
}