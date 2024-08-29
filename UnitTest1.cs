using System.Collections;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace AngleSharpExperiments;

public class UnitTest1
{
    [Fact]
    public async Task Test1()
    {
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

        var context = BrowsingContext.New(config);
        var raw = """
            <div id='div1'>
                hi<p>world</p>
                component 2 content
                component 3 content
                <p>component 
                    <span>4 content</span>
                </p>
            </div>
            """;
        IDocument document = await context.OpenAsync(r => r.Content(raw));

        var startIndexC2 = raw.IndexOf("component 2 content");
        var endIndexC2 = startIndexC2 + "component 2 content".Length;
        var component2Nodes = new NodeListSlice(document.Body!.ChildNodes, startIndexC2, endIndexC2);

    }

    private class NodeListSlice(INodeList nodes, int startIndex, int endIndex) : INodeList
    {
        public INode this[int index] => throw new NotImplementedException();

        public int Length { get; }

        public IEnumerator<INode> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public void ToHtml(TextWriter writer, IMarkupFormatter formatter)
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}