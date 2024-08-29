using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Parser.Tokens.Struct;

namespace AngleSharpExperiments;

public class UnitTest1
{
    [Fact]
    public async Task Test1()
    {
        var config = Configuration.Default.With<IHtmlParser>(_ => new HtmlParser(new HtmlParserOptions
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
        IDocument document = await context.OpenAsync(r => r.Content("""
            <div id='div1'>
                hi<p>world</p>
                component 2 content
                component 3 content
                <p>component 
                    <span>4 content</span>
                </p>
            </div>
            """));
    }
}