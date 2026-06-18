using Content.Shared.Guidebook;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests._CMU14.Guidebook;

[TestFixture]
public sealed class CMUGuidebookPlacementTest
{
    private static readonly ProtoId<GuideEntryPrototype> CmuGuide = "RMC14";
    private static readonly ProtoId<GuideEntryPrototype> HumanoidRoles = "RMCGuideMarineRoles";
    private static readonly ProtoId<GuideEntryPrototype> MedicalV3 = "AU14MedicalV2";
    private static readonly ProtoId<GuideEntryPrototype> Surgery = "MedicalSurgery";

    [Test]
    public async Task MedicalV3GuideIsListedUnderCMUGuideRoot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var cmuGuide = prototypes.Index(CmuGuide);
            var humanoidRoles = prototypes.Index(HumanoidRoles);
            var medicalV3 = prototypes.Index<GuideEntryPrototype>(MedicalV3);

            Assert.Multiple(() =>
            {
                Assert.That(cmuGuide.Children, Does.Contain(MedicalV3));
                Assert.That(humanoidRoles.Children, Does.Not.Contain(MedicalV3));
                Assert.That(medicalV3.Name, Is.EqualTo("Medical V3"));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SurgeryGuideIsListedUnderMedicalV3Guide()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var medicalV3 = prototypes.Index(MedicalV3);
            var surgery = prototypes.Index<GuideEntryPrototype>(Surgery);

            Assert.Multiple(() =>
            {
                Assert.That(medicalV3.Children, Does.Contain(Surgery));
                Assert.That(surgery.Text.ToString(), Is.EqualTo("/ServerInfo/Guidebook/Medical/Surgery.xml"));
            });
        });

        await pair.CleanReturnAsync();
    }
}
