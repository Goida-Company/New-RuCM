using Content.Shared._CMU14.Yautja;

namespace Content.IntegrationTests._CMU14.Yautja;

[TestFixture]
public sealed class YautjaSelfDestructParityTest
{
    [Test]
    public void ExplosionPolicyMatchesCmss13AndProtectsTilesInBigMode()
    {
        var big = new YautjaBracerComponent
        {
            SelfDestructExplosionType = YautjaSelfDestructExplosionType.Big,
            SelfDestructMaxTileBreak = 3,
        };
        var small = new YautjaBracerComponent
        {
            SelfDestructExplosionType = YautjaSelfDestructExplosionType.Small,
            SelfDestructMaxTileBreak = 3,
        };
        var thrall = new YautjaThrallBracerComponent();

        Assert.Multiple(() =>
        {
            Assert.That(YautjaSelfDestructSystem.SelfDestructTotalIntensity(big), Is.EqualTo(50 * MathF.PI / 3 * MathF.Pow(12, 3)).Within(0.01));
            Assert.That(YautjaSelfDestructSystem.SelfDestructMaxIntensity(big), Is.EqualTo(600));
            Assert.That(YautjaSelfDestructSystem.SelfDestructIntensitySlope(big), Is.EqualTo(50));
            Assert.That(YautjaSelfDestructSystem.SelfDestructMaxTileBreak(
                big.SelfDestructExplosionType, big.SelfDestructMaxTileBreak), Is.EqualTo(0),
                "The big predator self-destruct must not break tiles down to space.");
            Assert.That(YautjaSelfDestructSystem.SelfDestructTotalIntensity(small), Is.EqualTo(550 * MathF.PI / 3 * MathF.Pow(800f / 550, 3)).Within(0.01));
            Assert.That(YautjaSelfDestructSystem.SelfDestructMaxIntensity(small), Is.EqualTo(800));
            Assert.That(YautjaSelfDestructSystem.SelfDestructIntensitySlope(small), Is.EqualTo(550));
            Assert.That(YautjaSelfDestructSystem.SelfDestructMaxTileBreak(
                small.SelfDestructExplosionType, small.SelfDestructMaxTileBreak), Is.EqualTo(3));
            Assert.That(thrall.SelfDestructTotalIntensity, Is.EqualTo(800),
                "CMSS13 remote thrall self-destruct calls cell_explosion(T, 800, 550, LINEAR).");
            Assert.That(thrall.SelfDestructMaxIntensity, Is.EqualTo(550));
        });
    }
}
