using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._AU14.Insurgency.Editor;

/// <summary>
///     A plain, scrollable explanation of every field in the INSFOR faction editor, so a host can build a
///     faction without reading code. Opened by the Help button at the top of the editor. Kept as simple
///     bold-heading + paragraph pairs; no markup tricks, so it stays readable in the terminal-style UI.
/// </summary>
public sealed class InsurgencyEditorHelpWindow : DefaultWindow
{
    public InsurgencyEditorHelpWindow()
    {
        Title = Loc.GetString("insfor-editor-help-title");
        MinSize = new Vector2(640, 620);

        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        scroll.AddChild(body);
        Contents.AddChild(scroll);
        InsforUiStyle.Apply(this);

        Intro(body, Loc.GetString("insfor-editor-help-intro"));

        Section(body, "insfor-editor-help-list-heading", "insfor-editor-help-list-body");
        Section(body, "insfor-editor-help-identity-heading", "insfor-editor-help-identity-body");
        Section(body, "insfor-editor-help-default-heading", "insfor-editor-help-default-body");
        Section(body, "insfor-editor-help-opposed-heading", "insfor-editor-help-opposed-body");
        Section(body, "insfor-editor-help-economy-heading", "insfor-editor-help-economy-body");
        Section(body, "insfor-editor-help-analyzer-heading", "insfor-editor-help-analyzer-body");
        Section(body, "insfor-editor-help-machines-heading", "insfor-editor-help-machines-body");
        Section(body, "insfor-editor-help-placeables-heading", "insfor-editor-help-placeables-body");
        Section(body, "insfor-editor-help-vendors-heading", "insfor-editor-help-vendors-body");
        Section(body, "insfor-editor-help-vendor-items-heading", "insfor-editor-help-vendor-items-body");
        Section(body, "insfor-editor-help-loadouts-heading", "insfor-editor-help-loadouts-body");
        Section(body, "insfor-editor-help-saving-heading", "insfor-editor-help-saving-body");
    }

    private static void Intro(BoxContainer body, string text)
    {
        body.AddChild(new RichTextLabel { Margin = new Thickness(10, 8) }.SetMessageWrapped(text));
    }

    private static void Section(BoxContainer body, string headingKey, string textKey)
    {
        body.AddChild(new Label { Text = Loc.GetString(headingKey), StyleClasses = { "LabelHeading" }, Margin = new Thickness(10, 10, 10, 2) });
        body.AddChild(new RichTextLabel { Margin = new Thickness(10, 0, 10, 6) }.SetMessageWrapped(Loc.GetString(textKey)));
    }
}

file static class HelpLabelExtensions
{
    // Small helper so the long help paragraphs word-wrap instead of running off the window.
    public static RichTextLabel SetMessageWrapped(this RichTextLabel label, string text)
    {
        label.SetMessage(text);
        return label;
    }
}
