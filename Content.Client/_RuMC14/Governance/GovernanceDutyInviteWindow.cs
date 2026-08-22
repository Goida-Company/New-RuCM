using System.Numerics;
using Content.Shared._RuMC14.Governance;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._RuMC14.Governance;

public sealed class GovernanceDutyInviteWindow : DefaultWindow
{
    public Button AcceptButton { get; }
    public Button DeclineButton { get; }
    public Button RecuseButton { get; }

    private readonly RichTextLabel _description;

    public GovernanceDutyInviteWindow()
    {
        Title = Loc.GetString("governance-duty-invite-title");
        MinSize = new Vector2(520, 220);

        _description = new RichTextLabel
        {
            VerticalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };

        AcceptButton = new Button
        {
            StyleClasses = { "ButtonAccept" },
        };
        DeclineButton = new Button
        {
            StyleClasses = { "ButtonCaution" },
        };
        RecuseButton = new Button
        {
            Text = Loc.GetString("governance-duty-invite-recuse"),
        };

        var buttons = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            Align = AlignMode.Center,
            SeparationOverride = 12,
        };
        buttons.AddChild(AcceptButton);
        buttons.AddChild(DeclineButton);
        buttons.AddChild(RecuseButton);

        var layout = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 12,
        };
        layout.AddChild(_description);
        layout.AddChild(buttons);
        Contents.AddChild(layout);
    }

    public void UpdateInvitation(
        GovernanceInviteKind kind,
        string entityId,
        DateTime expiresAt,
        int acceptReward,
        int declinePenalty,
        int expiryPenalty)
    {
        Title = Loc.GetString(kind == GovernanceInviteKind.Jury
            ? "governance-jury-invite-title"
            : "governance-duty-invite-title");
        _description.Text = Loc.GetString(
            kind == GovernanceInviteKind.Jury
                ? "governance-jury-invite-description"
                : "governance-duty-invite-description",
            (kind == GovernanceInviteKind.Jury ? "case" : "round", entityId),
            ("expires", expiresAt.ToLocalTime().ToString("HH:mm:ss")),
            ("acceptReward", acceptReward),
            ("declinePenalty", declinePenalty),
            ("expiryPenalty", expiryPenalty));
        AcceptButton.Text = Loc.GetString(
            "governance-duty-invite-accept",
            ("reward", acceptReward));
        DeclineButton.Text = Loc.GetString(
            "governance-duty-invite-decline",
            ("penalty", declinePenalty));
    }
}
