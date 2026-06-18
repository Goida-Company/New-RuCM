using System.Linq;
using Content.Shared._RMC14.Vendors;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests._CMU14.ZLevels;

[TestFixture]
public sealed class CMUDeployableZLevelLadderContentTest
{
    private const int LadderPoints = 10;
    private const string EngineeringSupplies = "Engineering Supplies";
    private const string FixOVein = "CMUFixOVein";

    private static readonly EntProtoId DeployableLadder = "CMUDeployableZLevelLadder";
    private static readonly EntProtoId SurgicalCase = "CMSurgicalCase";
    private static readonly EntProtoId SurgicalCaseFilled = "CMSurgicalCaseFilled";

    private static readonly EntProtoId[] CombatTechnicianVendors =
    [
        "AU14ComtechGearVendor",
        "AU14RMCComtechGearVendor",
        "AU14ComtechGearVendorUPP",
    ];

    [Test]
    public async Task SurgicalCaseCarriesFixOVein()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var factory = server.EntMan.ComponentFactory;

            Assert.That(prototypes.TryIndex<EntityPrototype>(SurgicalCase, out var surgicalCase), Is.True);
            Assert.That(surgicalCase!.TryGetComponent<StorageComponent>(out var storage, factory), Is.True);
            Assert.That(storage!.Whitelist?.Tags, Is.Not.Null);
            Assert.That(storage.Whitelist!.Tags!.Select(tag => tag.ToString()), Does.Contain(FixOVein));

            Assert.That(prototypes.TryIndex<EntityPrototype>(SurgicalCaseFilled, out var filledCase), Is.True);
            Assert.That(filledCase!.TryGetComponent<StorageFillComponent>(out var fill, factory), Is.True);
            Assert.That(fill!.Contents.Any(entry => entry.PrototypeId?.ToString() == FixOVein), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CombatTechnicianVendorsSellDeployableLadder()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var factory = server.EntMan.ComponentFactory;

            Assert.That(prototypes.TryIndex<EntityPrototype>(DeployableLadder, out var ladder), Is.True);
            Assert.That(ladder!.Components.ContainsKey(DeployableLadder.Id), Is.True);

            foreach (var vendorId in CombatTechnicianVendors)
            {
                Assert.That(prototypes.TryIndex<EntityPrototype>(vendorId, out var vendorProto), Is.True, vendorId.Id);
                Assert.That(vendorProto!.TryGetComponent<CMAutomatedVendorComponent>(out var vendor, factory), Is.True, vendorId.Id);

                var engineering = vendor!.Sections.SingleOrDefault(section => section.Name == EngineeringSupplies);
                Assert.That(engineering, Is.Not.Null, vendorId.Id);

                var entry = engineering!.Entries.SingleOrDefault(entry => entry.Id.ToString() == DeployableLadder.Id);
                Assert.That(entry, Is.Not.Null, $"{vendorId} should sell {DeployableLadder.Id}");
                Assert.That(entry!.Points, Is.EqualTo(LadderPoints), vendorId.Id);
            }
        });

        await pair.CleanReturnAsync();
    }
}
