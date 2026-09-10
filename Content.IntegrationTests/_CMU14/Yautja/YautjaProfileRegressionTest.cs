using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._CMU14.Yautja.Lobby;
using Content.Shared._CMU14.Yautja;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests._CMU14.Yautja;

[TestFixture]
public sealed class YautjaProfileRegressionTest
{
    [Test]
    public async Task EverySelectableCasterCanBeSpawned()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            foreach (var material in YautjaCharacterProfile.CasterMaterialOrder)
            {
                var prototype = YautjaCharacterProfile.Default.WithCaster(material).CasterPrototype;
                var caster = entities.SpawnEntity(prototype, MapCoordinates.Nullspace);
                entities.DeleteEntity(caster);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EquipmentCardsStayInsideTheirPanelsAfterResizing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var editor = new YautjaProfileEditor();
            editor.SetProfile(HumanoidCharacterProfile.DefaultWithSpecies("Human"));
            var pages = (Dictionary<YautjaProfileEditorCategory, Control>) typeof(YautjaProfileEditor)
                .GetField("_categoryPageControls", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(editor)!;
            var casters = (Control) typeof(YautjaProfileEditor)
                .GetField("_casterSections", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(editor)!;
            Assert.That(Descendants(casters).OfType<YautjaProfileGrid>().Single().ChildCount,
                Is.EqualTo(YautjaCharacterProfile.CasterMaterialOrder.Length));

            foreach (var category in new[] { YautjaProfileEditorCategory.Equipment, YautjaProfileEditorCategory.Sets })
            {
                var page = pages[category];
                page.Visible = true;
                // Cross wrapping thresholds in both directions, including panel padding and the scrollbar.
                foreach (var width in new[] { 700, 470, 350, 250, 470, 700 })
                {
                    var size = new Vector2(width, 600);
                    page.Measure(size);
                    page.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    var cards = Descendants(page).OfType<Button>()
                        .Where(button => button.MinSize == new Vector2(108, 108)).ToArray();
                    Assert.That(cards, Is.Not.Empty);
                    foreach (var card in cards)
                    {
                        var parent = card.Parent;
                        while (parent != null && parent is not PanelContainer)
                            parent = parent.Parent;
                        Assert.That(parent, Is.Not.Null, $"{category}: {card.ToolTip} needs a group panel");
                        var offset = card.GlobalPosition - parent!.GlobalPosition;
                        Assert.Multiple(() =>
                        {
                            Assert.That(offset.X, Is.GreaterThanOrEqualTo(0));
                            Assert.That(offset.Y, Is.GreaterThanOrEqualTo(0));
                            Assert.That(offset.X + card.Width, Is.LessThanOrEqualTo(parent.Width + 1),
                                $"{category}: {card.ToolTip} overflows horizontally at width {width}");
                            Assert.That(offset.Y + card.Height, Is.LessThanOrEqualTo(parent.Height + 1),
                                $"{category}: {card.ToolTip} overflows vertically at width {width}");
                        });
                    }
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
