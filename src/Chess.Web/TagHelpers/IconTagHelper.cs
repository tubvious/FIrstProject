using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Chess.Web.TagHelpers;

/// <summary>Renders <c>&lt;icon name="flag" /&gt;</c> as an inline reference into the SVG icon sprite.</summary>
[HtmlTargetElement("icon", TagStructure = TagStructure.WithoutEndTag)]
public sealed class IconTagHelper : TagHelper
{
    public const string SpritePath = "/img/icons.svg";

    public required string Name { get; set; }

    public string? Class { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Attributes.SetAttribute("focusable", "false");
        if (!string.IsNullOrEmpty(Class))
        {
            output.Attributes.SetAttribute("class", Class);
        }

        output.Content.SetHtmlContent($"<use href=\"{SpritePath}#{Name}\"></use>");
    }
}
