using Content.Server._RuMC14.Governance;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Shared.Administration.Notes;
using Content.Shared.Database;
using Content.Shared.Eui;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Robust.Shared.Network;
using static Content.Shared.Administration.Notes.AdminNoteEuiMsg;

namespace Content.Server.Administration.Notes;

public sealed partial class AdminNotesEui : BaseEui
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IAdminNotesManager _notesMan = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private GovernanceManager _governance = default!;

    private readonly bool _governanceDutyReadOnly;

    public AdminNotesEui() : this(false)
    {
    }

    public AdminNotesEui(bool governanceDutyReadOnly)
    {
        _governanceDutyReadOnly = governanceDutyReadOnly;
        IoCManager.InjectDependencies(this);
    }

    private Guid NotedPlayer { get; set; }
    private string NotedPlayerName { get; set; } = string.Empty;
    private bool HasConnectedBefore { get; set; }
    private Dictionary<(int, NoteType), SharedAdminNote> Notes { get; set; } = new();

    public override async void Opened()
    {
        base.Opened();

        if (!await CanViewAsync())
        {
            Close();
            return;
        }

        _admins.OnPermsChanged += OnPermsChanged;
        _notesMan.NoteAdded += NoteModified;
        _notesMan.NoteModified += NoteModified;
        _notesMan.NoteDeleted += NoteDeleted;
    }

    public override void Closed()
    {
        base.Closed();

        _admins.OnPermsChanged -= OnPermsChanged;
        _notesMan.NoteAdded -= NoteModified;
        _notesMan.NoteModified -= NoteModified;
        _notesMan.NoteDeleted -= NoteDeleted;
    }

    public override EuiStateBase GetNewState()
    {
        return new AdminNotesEuiState(
            NotedPlayerName,
            Notes,
            !_governanceDutyReadOnly && _notesMan.CanCreate(Player) && HasConnectedBefore,
            !_governanceDutyReadOnly && _notesMan.CanDelete(Player),
            !_governanceDutyReadOnly && _notesMan.CanEdit(Player)
        );
    }

    public override async void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!await CanViewAsync())
        {
            Close();
            return;
        }

        // A Governance duty responder receives the complete notes history, including bans and
        // watchlists, but may not mutate permanent moderation records through this temporary role.
        if (_governanceDutyReadOnly)
            return;

        switch (msg)
        {
            case CreateNoteRequest request:
                {
                    if (!_notesMan.CanCreate(Player))
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(request.Message))
                    {
                        break;
                    }

                    if (request.ExpiryTime is not null && request.ExpiryTime <= DateTime.UtcNow)
                    {
                        break;
                    }

                    await _notesMan.AddAdminRemark(Player, NotedPlayer, request.NoteType, request.Message, request.NoteSeverity, request.Secret, request.ExpiryTime);
                    break;
                }
            case DeleteNoteRequest request:
                {
                    if (!_notesMan.CanDelete(Player))
                    {
                        break;
                    }

                    await _notesMan.DeleteAdminRemark(request.Id, request.Type, Player);
                    break;
                }
            case EditNoteRequest request:
                {
                    if (!_notesMan.CanEdit(Player))
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(request.Message))
                    {
                        break;
                    }

                    await _notesMan.ModifyAdminRemark(request.Id, request.Type, Player, request.Message, request.NoteSeverity, request.Secret, request.ExpiryTime);
                    break;
                }
        }
    }

    public async Task ChangeNotedPlayer(Guid notedPlayer)
    {
        if (!await CanViewAsync())
        {
            Close();
            return;
        }

        NotedPlayer = notedPlayer;
        await LoadFromDb();
    }

    private async Task<bool> CanViewAsync()
    {
        if (_notesMan.CanView(Player))
            return true;

        if (!_governanceDutyReadOnly)
            return false;

        var roundId = _entityManager.System<GameTicker>().RoundId;
        if (roundId <= 0)
            return false;

        return await _governance.AuthorizeAsync(Player.UserId, roundId, "moderation.view_logs") != null;
    }

    private void NoteModified(SharedAdminNote note)
    {
        if (note.Player != NotedPlayer)
            return;

        Notes[(note.Id, note.NoteType)] = note;
        StateDirty();
    }

    private void NoteDeleted(SharedAdminNote note)
    {
        if (note.Player != NotedPlayer)
            return;

        Notes.Remove((note.Id, note.NoteType));
        StateDirty();
    }

    private async Task LoadFromDb()
    {
        var locatedPlayer = await _locator.LookupIdAsync((NetUserId) NotedPlayer);
        NotedPlayerName = locatedPlayer?.Username ?? string.Empty;
        HasConnectedBefore = locatedPlayer?.LastAddress is not null;
        Notes = (from note in await _notesMan.GetAllAdminRemarks(NotedPlayer)
                 select note.ToShared())
            .ToDictionary(sharedNote => (sharedNote.Id, sharedNote.NoteType));
        StateDirty();
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player != Player)
        {
            return;
        }

        if (!_governanceDutyReadOnly && !_notesMan.CanView(Player))
        {
            Close();
        }
        else
        {
            StateDirty();
        }
    }
}
