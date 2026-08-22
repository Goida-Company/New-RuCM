namespace Content.DiscordBot.Governance;

public sealed class ReputationCoordinator(
    GovernanceIdentityService identities,
    ReputationService reputation,
    Config config)
{
    public async Task RunSchedulerAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Clamp(config.ReputationSchedulerSeconds, 60, 3600));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync();
            }
            catch (Exception exception)
            {
                await Logger.Error("Governance reputation scheduler iteration failed", exception);
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    public async Task ProcessOnceAsync()
    {
        await identities.EnsureAllSs14UsersAsync();
        await reputation.ReconcileOperationalEvidenceAsync();
        await reputation.RefreshAllAsync();
        await reputation.ReconcileQualificationsAsync();
    }
}
