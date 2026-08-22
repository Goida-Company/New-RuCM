# RUCM Community Governance: game boundary

This module is the server-side trust boundary between RussianCM and Community Governance.
The game owns moderation duty invitations, duty sessions and temporary capability grants. The
Discord bot owns the case workflow and jury selection, while the game delivers pending jury
invitations from the same PostgreSQL schema and records the player's response. The game never
accepts permissions, scope, expiry, rating amounts, or target state from a client.

## Deployment

1. Configure RussianCM to use the same PostgreSQL database as the Governance bot.
2. Apply the bot-owned `governance` schema before enabling this module. The required contract is:
   `users`, `duty_sessions`, `capability_grants`, and append-only `audit_events`.
3. Enable the module in the server configuration:

   ```toml
   [governance]
   enabled = true
   freeze_max_seconds = 120
   duty_target_responders = 1
   duty_check_seconds = 30
   duty_invite_seconds = 90
   duty_session_minutes = 240
   ```

SQLite deliberately fails closed: no duty session or capability can be authorized.

## In-game duty flow

During an active round the server periodically checks whether the configured responder target is
staffed. It randomly selects connected observers with Moderation Qualification level I or higher
and opens an in-game invitation. Accepting adds 10 Civic Rating and atomically creates a
round-scoped DutySession plus `moderation.freeze`; declining removes 15; recusal has no rating
effect. Expired invitations use the configured expiry penalty. Sessions and their grants are
closed automatically on timeout or when the round changes. Candidates must already have a
`governance.qualifications` row with `track = 'moderation'` and `level >= 1`; ordinary linked
accounts are synchronized at level 0 and are not invited.

## In-game jury flow

The Discord bot writes a `purpose = 'jury'`, `entity_type = 'court_case'` invitation after random
selection. The game polls pending invitations for connected SS14 UUIDs and opens the same trusted
EUI with the case number and rating effects. Accept, decline, recusal and expiry are committed
atomically to PostgreSQL; the bot polls that state and advances the Discord case. Jury candidates
do not need to be observers and receive no moderation capability. The Discord slash command is a
fallback for candidates who are not connected to the game server.

## In-game AHelp

The native Bwoink AHelp is the source of tickets. Its first player message atomically creates a
PostgreSQL `ahelp_tickets` row and every non-admin-only message is appended to immutable
`ahelp_messages`. Active responder observers use `governance_ahelp` to view the queue, claim a
ticket, open its existing Bwoink conversation, mark it waiting, or resolve it. The server grants
access only to the responder assigned to that ticket; selecting another player in the client UI
does not bypass the PostgreSQL capability and assignment checks.

## In-game commands

- `governance_status` refreshes and displays the caller's current duty session.
- `governance_ahelp` opens the responder's in-game AHelp queue.
- `governance_freeze <player|UUID> <seconds> <incident-id> <reason>` requires an active
  `moderation.freeze` grant scoped to the current round. The caller must be an observer,
  cannot target themselves, and cannot freeze for longer than 120 seconds.
- `governance_explanation <player|UUID> <action-id> <reason>` opens an assigned Bwoink
  conversation, sends the approved explanation request to the player and records replies in the
  immutable AHelp transcript.
- `governance_logs <player|UUID> <action-id>` returns at most 100 current-round admin-log entries
  involving that player. The approved action is one-shot and every access is audited.
- `governance_round_remove <player|UUID> <action-id> <reason>` requires qualification level II,
  a two-person quorum and blocks reconnects until the round changes.

Every successful or denied freeze attempt is appended to `governance.audit_events` when the
database is available. Long-term punishments remain outside this responder surface and are
executed by the Governance verdict pipeline.
