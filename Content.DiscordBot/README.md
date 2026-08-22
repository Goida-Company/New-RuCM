# RussianCM Discord bot and Community Court

`Content.DiscordBot` is the only Discord process used by RussianCM. It handles both
the existing SS14 account linking flow and Community Court. The game server and the
bot are separate processes from this repository and share the game PostgreSQL.

Community Governance uses `GovernanceDbContext`; PostgreSQL is authoritative for
identity, immutable civic-rating entries, qualifications, conflicts, invitations,
court workflow, duty sessions, capability grants, AHelp, live incidents, quorum,
event review/manifests, audits, Discord thread IDs, and publication. The bot applies
the idempotent EF migration on startup.
It also keeps a session-level PostgreSQL advisory lock, so a second Court process
exits before logging into Discord. Never run two processes with the same bot token.

## Configuration

Copy `config.example.json` to `config.json` next to the process, set environment
variables, or pass `--env-file <path>`. Both the C# names (`DATABASE_STRING`,
`DISCORD_GUILD`, `COURT_CHANNEL`) and the former prototype names
(`GAME_DATABASE_URL`, `DISCORD_GUILD_ID`, `COURT_FORUM_CHANNEL_ID`) are accepted.
PostgreSQL URLs are converted to Npgsql connection strings. `CourtChannel` may be a
Discord Forum or a text channel where the bot can create, write, lock, and archive
public threads.

The bot uses the Guilds, Guild Messages, and Message Content gateway intents.
**MESSAGE CONTENT INTENT must be enabled for the bot in Discord Developer Portal**;
it is required so the claimant and defendant can write ordinary messages directly in
their Court thread without a modal/button. Invite the bot to the configured guild
with application-command, thread, message, manage-message, and member-view
permissions before enabling the scheduler. Use the owner-only `/аккаунт панель`
command to create the existing linking button.

```powershell
dotnet run --project Content.DiscordBot/Content.DiscordBot.csproj
dotnet run --project Content.DiscordBot/Content.DiscordBot.csproj -- --env-file .env --migrate-only
dotnet run --project Content.DiscordBot/Content.DiscordBot.csproj -- --env-file .env --governance-doctor
```

`rmc.discord_token` is unrelated: it belongs to the in-game admin/mentor chat bridge
and is not read by Community Court. The former Python `rucm_court` process is not a
runtime dependency and must not be started after this bot is deployed.

## Discord commands

- `/суд жалоба` accepts the defendant's SS14 game nickname, resolves the linked
  Discord identity from the game PostgreSQL, and creates the case and public thread.
- During the defense stage the claimant and defendant write ordinary messages
  directly in the case thread. Other users' messages are removed and are not accepted
  as Court material. Each accepted party message is also persisted in PostgreSQL.
- When discussion is finished, both claimant and defendant independently press
  `Закончить защиту`; after the second confirmation the thread is locked and the case
  moves to jury formation. Defense does not advance to the jury merely because time
  passed: both party confirmations are required for the normal transition.
- `/суд свидетель-добавить` and `/суд свидетельство` retain the controlled witness
  flow; witnesses do not receive ordinary write access to the case thread.
- `/суд присяжный` mirrors the in-game invitation response transactionally.
- `/суд голос` records a secret guilt-phase vote.
- `/суд наказание` records a secret sentencing-phase vote.
- `/суд история` exposes non-secret prior sanctions only to active sentencing jurors.
- `/суд статус` displays the current PostgreSQL state.
- `/управление профиль|друг-добавить|друг-удалить` manages transparent selection
  conflicts and shows independent jury/moderation/event qualifications.
- Native in-game AHelp creates the PostgreSQL queue; active responder observers claim and answer
  tickets through `governance_ahelp`. Discord mirrors the AHelp status and the full
  game-side conversation automatically; the Discord AHelp thread itself remains
  read-only to users. LiveIncident, scoped moderation proposals and quorum controls
  remain PostgreSQL-authoritative. `freeze` needs one approval; `round_remove` needs
  two independent approvals.
- `/событие ...` implements proposal, three-reviewer decision, a bounded resource
  manifest, temporary `event.*` capabilities, action audit, and automatic revocation.
- `/руководство ...` is limited to the configured role or guild owner. Every override
  has a reason and immutable audit row; court cancellation also reverses an executed
  game ban, job ban, or warning.

The scheduler expires invitations, synchronizes in-game responses, selects
conflict-free jurors, replaces timed-out nonvoters, sends DMs, executes
warnings/bans/job bans directly in the game tables, and publishes and archives final
decisions. The defense stage itself advances only after the claimant and defendant
both confirm completion. Guilty measures are capped at seven days. No Discord
administrator chooses the verdict and there is no ordinary appeal path.

## In-game enforcement

Set `governance.enabled true` on the game server. Observer-only duty staffing scales
with online population and the open AHelp backlog. Accepted duty creates a bounded
`DutySession`; qualification level 1 grants freeze/explanation/log capabilities and
level 2 also grants `moderation.round_remove`. The actual commands require a matching
approved PostgreSQL action and target:

```text
governance_freeze <player> <seconds> <action-id> <reason>
governance_explanation <player> <action-id> <reason>
governance_logs <player> <action-id>
governance_round_remove <player> <action-id> <reason>
```

Round removal blocks reconnects until the round changes. Capabilities expire or are
revoked with their duty/event session; they are not permanent admin permissions.
