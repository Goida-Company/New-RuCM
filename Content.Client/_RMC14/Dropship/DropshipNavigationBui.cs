using Content.Client.Message;
using Content.Shared._CMU14.Dropship.TacticalLand;
using Content.Shared._RMC14.Dropship;
using Content.Shared.Doors.Components;
using Content.Shared.Shuttles.Systems;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;
using Robust.Shared.Localization; // RuMC edit

namespace Content.Client._RMC14.Dropship;

[UsedImplicitly]
public sealed partial class DropshipNavigationBui : BoundUserInterface
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;

    [ViewVariables]
    private DropshipNavigationWindow? _window;

    private readonly Dictionary<DropshipButton, string> _destinations = new();
    private NetEntity? _selected;
    private bool _tacticalLandActive;
    private bool _tacticalHoverActive;

    public DropshipNavigationBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();
        OpenWindow();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        OpenWindow();

        switch (state)
        {
            case DropshipNavigationTacticalLandBuiState s:
                Set(s);
                return;
            case DropshipNavigationDestinationsBuiState s:
                Set(s);
                return;
            case DropshipNavigationTravellingBuiState s:
                Set(s);
                return;
        }
    }

    private void OpenWindow()
    {
        if (_window != null)
            return;

        _window = this.CreateWindow<DropshipNavigationWindow>();
        _window.OnClose += OnClose;
        // RuMC edit start
        SetFlightHeader(Loc.GetString("rmc-dropship-nav-flight-controls"));
        SetDoorHeader(Loc.GetString("rmc-dropship-nav-door-controls"));
        SetRemoteControlHeader(Loc.GetString("rmc-dropship-nav-remote-control"));
        SetLaunchAlarmHeader(Loc.GetString("rmc-dropship-nav-launch-alarm"));
        // RuMC edit end

        if (_entities.TryGetComponent(Owner, out TransformComponent? transform) &&
            _entities.TryGetComponent(transform.ParentUid, out MetaDataComponent? metaData))
        {
            _window.Title = $"{metaData.EntityName} {Loc.GetString("rmc-dropship-nav-title")}"; // RuMC edit
        }

        _window.CancelButton.Button.OnPressed += _ =>
        {
            if (_tacticalLandActive)
            {
                SendPredictedMessage(new DropshipNavigationTacticalLandCancelMsg());
                return;
            }

            if (_tacticalHoverActive)
            {
                SendPredictedMessage(new DropshipNavigationTacticalHoverCancelMsg());
                SetCancelDisabled(true);
                return;
            }

            SetLaunchDisabled(true);
            SetCancelDisabled(true);
            _selected = null;
            ResetDestinationButtons();
            CancelFlyby();
        };

        _window.LaunchButton.Button.OnPressed += _ =>
        {
            if (_tacticalLandActive)
            {
                SendPredictedMessage(new DropshipNavigationTacticalLandConfirmMsg());
                return;
            }

            if (_selected != null)
                SendPredictedMessage(new DropshipNavigationLaunchMsg(_selected.Value));

            SetLaunchDisabled(true);
            _selected = null;
            ResetDestinationButtons();
        };

        _window.LockdownButton.Button.OnPressed += _ => SendPredictedMessage(new DropshipLockdownMsg(DoorLocation.None));
        _window.LockdownButtonAft.Button.OnPressed += _ => SendPredictedMessage(new DropshipLockdownMsg(DoorLocation.Aft));
        _window.LockdownButtonPort.Button.OnPressed += _ => SendPredictedMessage(new DropshipLockdownMsg(DoorLocation.Port));
        _window.LockdownButtonStarboard.Button.OnPressed += _ => SendPredictedMessage(new DropshipLockdownMsg(DoorLocation.Starboard));
        _window.RemoteControlButton.Button.OnPressed += _ => SendPredictedMessage(new DropshipRemoteControlToggleMsg());
        _window.LaunchAlarmButton.Button.OnPressed += _ => SendPredictedMessage(new DropshipLaunchAlarmToggleMsg());
        _entities.System<DropshipSystem>().Uis.Add(this);
    }

    private void OnClose()
    {
        _entities.System<DropshipSystem>().Uis.Remove(this);
        Close();
    }

    private void Set(DropshipNavigationDestinationsBuiState destinations)
    {
        if (_window == null)
            return;

        _tacticalLandActive = false;
        _tacticalHoverActive = destinations.CanCancelTacticalHover;
        // RuMC edit start
        _window.LaunchButton.Text = Loc.GetString("rmc-dropship-nav-launch");
        _window.CancelButton.Text = _tacticalHoverActive
            ? Loc.GetString("rmc-dropship-nav-return-now")
            : Loc.GetString("rmc-dropship-nav-cancel");

        SetFlightHeader(Loc.GetString("rmc-dropship-nav-flight-controls"));
        // RuMC edit end

        _window.DestinationsContainer.Visible = true;
        _window.ProgressBarContainer.Visible = false;
        _window.CancelButton.Visible = true;
        _window.LaunchButton.Visible = true;
        _window.CancelButton.Button.Disabled = !_tacticalHoverActive;
        _window.LaunchButton.Button.Disabled = true;

        _window.DestinationsContainer.DisposeAllChildren();

        DropshipButton DestinationButton(string name, bool disabled, Action onPressed)
        {
            var button = new DropshipButton();

            button.Text = name;
            button.Disabled = _tacticalHoverActive || disabled;
            button.BorderColor = Color.Transparent;
            button.BorderThickness = new Thickness(0);
            button.Button.ToggleMode = false;
            button.Button.OnPressed += _ =>
            {
                ResetDestinationButtons();
                button.Text = $"> {name}";
                SetLaunchDisabled(false);
                SetCancelDisabled(false);
                onPressed();
            };

            return button;
        }

        _destinations.Clear();
        if (_tacticalHoverActive)
        {
            var hoverStatus = new DropshipButton
            {
                Text = Loc.GetString("rmc-dropship-nav-hover-active"), // RuMC edit
                Disabled = true,
                BorderColor = Color.FromHex("#4E6B8E"),
                BorderThickness = new Thickness(1),
            };
            _window.DestinationsContainer.AddChild(hoverStatus);
        }

        if (destinations.FlyBy is { } flyBy)
        {
            var flyByName = Loc.GetString("rmc-dropship-nav-flyby"); // RuMC edit
            var flyByButton = DestinationButton(flyByName, false, () => _selected = flyBy);
            _destinations[flyByButton] = flyByName;
            _window.DestinationsContainer.AddChild(flyByButton);
        }

        foreach (var destination in destinations.Destinations)
        {
            var name = destination.Name;
            if (destination.Primary)
                name += $" {Loc.GetString("rmc-dropship-nav-primary")}"; // RuMC edit

            var button = DestinationButton(name, destination.Occupied, () => _selected = destination.Id);

            _destinations[button] = name;
            _window.DestinationsContainer.AddChild(button);
        }

        if (destinations.CanTacticalLand)
        {
            var tacticalButton = new DropshipButton
            {
                Text = Loc.GetString("rmc-dropship-nav-tactical-land"), // RuMC edit
                Disabled = false,
                BorderColor = Color.FromHex("#2A6D2A"),
                BorderThickness = new Thickness(1),
            };
            tacticalButton.Button.ToggleMode = false;
            tacticalButton.Button.OnPressed += _ => SendPredictedMessage(new DropshipNavigationTacticalLandStartMsg());
            _window.DestinationsContainer.AddChild(tacticalButton);
        }

        if (destinations.CanWithdrawReturn)
        {
            var returnButton = new DropshipButton
            {
                Text = Loc.GetString("rmc-dropship-nav-evac"), // RuMC edit
                Disabled = _tacticalHoverActive,
                BackgroundColor = Color.FromHex("#4A1010"),
                BorderColor = Color.FromHex("#CC2222"),
                BorderThickness = new Thickness(2),
            };
            returnButton.Button.ToggleMode = false;
            returnButton.Button.OnPressed += _ => SendPredictedMessage(new DropshipWithdrawReturnMsg());
            _window.DestinationsContainer.AddChild(returnButton);
        }

        RefreshDoorLockStatus(destinations.DoorLockStatus);
        SetRemoteControl(destinations.RemoteControlStatus);
        RefreshLaunchAlarmStatus(destinations.LaunchAlarmStatus);
    }

    private void Set(DropshipNavigationTacticalLandBuiState tactical)
    {
        if (_window == null)
            return;

        _tacticalLandActive = true;
        _tacticalHoverActive = false;

        // RuMC edit start
        SetFlightHeader(tactical.TacticalHover
            ? Loc.GetString("rmc-dropship-nav-tactical-hover-header")
            : Loc.GetString("rmc-dropship-nav-tactical-landing-header"));
        // RuMC edit end

        _window.DestinationsContainer.Visible = true;
        _window.ProgressBarContainer.Visible = false;
        _window.CancelButton.Visible = true;
        _window.LaunchButton.Visible = true;

        _window.DestinationsContainer.DisposeAllChildren();
        _destinations.Clear();

        var status = new DropshipButton
        {
            Text = tactical.ClearForLanding
                ? tactical.TacticalHover
                // RuMC edit start
                    ? Loc.GetString("rmc-dropship-nav-wasd-hover-clear")
                    : Loc.GetString("rmc-dropship-nav-wasd-land-clear")
                : Loc.GetString("rmc-dropship-nav-wasd-obstructed"),
                // RuMC edit end
            Disabled = true,
            BorderColor = tactical.ClearForLanding ? Color.FromHex("#2A6D2A") : Color.FromHex("#7A2A2A"),
            BorderThickness = new Thickness(1),
        };
        _window.DestinationsContainer.AddChild(status);

        var upButton = new DropshipButton
        {
            Text = Loc.GetString("rmc-dropship-nav-ascend"), // RuMC edit
            Disabled = !tactical.CanMoveUp,
            BorderColor = Color.FromHex("#4E6B8E"),
            BorderThickness = new Thickness(1),
        };
        upButton.Button.ToggleMode = false;
        upButton.Button.OnPressed += _ => SendPredictedMessage(new DropshipNavigationTacticalLandMoveUpMsg());
        _window.DestinationsContainer.AddChild(upButton);

        var downButton = new DropshipButton
        {
            Text = Loc.GetString("rmc-dropship-nav-descend"), // RuMC edit
            Disabled = !tactical.CanMoveDown,
            BorderColor = Color.FromHex("#4E6B8E"),
            BorderThickness = new Thickness(1),
        };
        downButton.Button.ToggleMode = false;
        downButton.Button.OnPressed += _ => SendPredictedMessage(new DropshipNavigationTacticalLandMoveDownMsg());
        _window.DestinationsContainer.AddChild(downButton);

        // RuMC edit start
        _window.LaunchButton.Text = tactical.TacticalHover
            ? Loc.GetString("rmc-dropship-nav-hover")
            : Loc.GetString("rmc-dropship-nav-land");
        // RuMC edit end
        _window.LaunchButton.Button.Disabled = !tactical.ClearForLanding;
        _window.CancelButton.Text = Loc.GetString("rmc-dropship-nav-cancel"); // RuMC edit
        _window.CancelButton.Button.Disabled = false;

        RefreshDoorLockStatus(tactical.DoorLockStatus);
        SetRemoteControl(tactical.RemoteControlStatus);
    }

    private void Set(DropshipNavigationTravellingBuiState travelling)
    {
        if (_window == null)
            return;

        _tacticalLandActive = false;
        _tacticalHoverActive = travelling.CanCancelTacticalHover;
        _window.DestinationsContainer.Visible = false;
        _window.ProgressBarContainer.Visible = true;
        _window.LaunchButton.Visible = false;
        _window.ProgressBar.Margin = new Thickness(0, 5, 0, 0);

        // RuMC edit start
        _window.CancelButton.Text = _tacticalHoverActive
            ? Loc.GetString("rmc-dropship-nav-return-now")
            : Loc.GetString("rmc-dropship-nav-cancel");
        // RuMC edit end
        _window.CancelButton.Visible = _tacticalHoverActive || travelling.Destination == travelling.DepartureLocation;
        _window.CancelButton.Button.Disabled = false;

        var time = Math.Ceiling((travelling.Time.End - _timing.CurTime).TotalSeconds);
        if (time < 0.01)
            time = 0;

        var destination = travelling.Destination;
        string Msg(string msg) => $"[color=#02E74E][bold]{msg}[/bold][/color]";

        switch (travelling.State)
        {
            case FTLState.Starting:
                // RuMC edit start
                SetFlightHeader(Loc.GetString("rmc-dropship-nav-launching"));
                _window.ProgressBarHeader.SetMarkup(Msg(Loc.GetString("rmc-dropship-nav-launching-progress",
                    ("time", time), ("destination", destination))));
                // RuMC edit end
                SetLockDownDisabled(false);
                break;
            case FTLState.Travelling:
                // RuMC edit start
                SetFlightHeader(Loc.GetString("rmc-dropship-nav-in-flight",
                    ("destination", destination)));
                _window.ProgressBarHeader.SetMarkup(Msg(Loc.GetString("rmc-dropship-nav-time-to-destination",
                    ("time", time))));
                // RuMC edit end
                SetLockDownDisabled(true);
                SetCancelDisabled(false);
                break;
            case FTLState.Arriving:
                // RuMC edit start
                SetFlightHeader(Loc.GetString("rmc-dropship-nav-final-approach",
                    ("destination", destination)));
                _window.ProgressBarHeader.SetMarkup(Msg(Loc.GetString("rmc-dropship-nav-time-to-landing",
                    ("time", time))));
                // RuMC edit end
                SetLockDownDisabled(true);
                SetCancelDisabled(true);
                break;
            case FTLState.Cooldown:
                // RuMC edit start
                SetFlightHeader(Loc.GetString("rmc-dropship-nav-refueling"));
                _window.ProgressBarHeader.SetMarkup(Msg(Loc.GetString("rmc-dropship-nav-ready-to-launch",
                    ("time", time))));
                // RuMC edit end
                SetLockDownDisabled(false);
                SetCancelDisabled(true);
                break;
            default:
                return;
        }

        if (_tacticalHoverActive)
            SetCancelDisabled(false);

        RefreshDoorLockStatus(travelling.DoorLockStatus);
        SetRemoteControl(travelling.RemoteControlStatus);
        RefreshLaunchAlarmStatus(travelling.LaunchAlarmStatus);

        var startEndTime = travelling.Time;
        _window.ProgressBar.MinValue = 0;
        _window.ProgressBar.MaxValue = (float) startEndTime.Length.TotalSeconds;
        _window.ProgressBar.SetAsRatio(1 - startEndTime.ProgressAt(_timing.CurTime));
    }

    private void SetFlightHeader(string label)
    {
        _window?.Header.SetMarkup($"[color=#0BDC49][font size=16][bold]{label}[/bold][/font][/color]");
    }

    private void SetDoorHeader(string label)
    {
        _window?.DoorHeader.SetMarkup($"[color=#0BDC49][font size=16][bold]{label}[/bold][/font][/color]");
    }

    private void SetRemoteControlHeader(string label)
    {
        _window?.RemoteControlHeader.SetMarkup($"[color=#0BDC49][font size=16][bold]{label}[/bold][/font][/color]");
    }

    private void SetLaunchAlarmHeader(string label)
    {
        _window?.LaunchAlarmHeader.SetMarkup($"[color=#0BDC49][font size=16][bold]{label}[/bold][/font][/color]");
    }

    private void SetLaunchDisabled(bool disabled)
    {
        if (_window == null)
            return;

        _window.LaunchButton.Button.Disabled = disabled;
    }

    private void SetCancelDisabled(bool disabled)
    {
        if (_window == null)
            return;

        _window.CancelButton.Button.Disabled = disabled;
    }

    private void SetLockDownDisabled(bool disabled)
    {
        if (_window == null)
            return;

        _window.LockdownButton.Button.Disabled = disabled;
        _window.LockdownButtonAft.Button.Disabled = disabled;
        _window.LockdownButtonPort.Button.Disabled = disabled;
        _window.LockdownButtonStarboard.Button.Disabled = disabled;
    }

    private void SetRemoteControl(bool status)
    {
        if (_window == null)
            return;

        // RuMC edit start
        _window.RemoteControlButton.Text = status
            ? Loc.GetString("rmc-dropship-nav-remote-enabled")
            : Loc.GetString("rmc-dropship-nav-remote-disabled");
        // RuMC edit end
    }

    private void ResetDestinationButtons()
    {
        if (_window == null)
            return;

        foreach (var destination in _window.DestinationsContainer.Children)
        {
            if (destination is not DropshipButton button ||
                !_destinations.TryGetValue(button, out var name))
            {
                continue;
            }

            button.Text = name;
        }
    }

    private void CancelFlyby()
    {
        if (_window == null)
            return;

        SendPredictedMessage(new DropshipNavigationCancelMsg());
    }

    private void RefreshDoorLockStatus(Dictionary<DoorLocation, bool> dooorLockStatus)
    {
        if (_window == null)
            return;

        dooorLockStatus.TryGetValue(DoorLocation.Aft, out var aftStatus);
        dooorLockStatus.TryGetValue(DoorLocation.Port, out var portStatus);
        dooorLockStatus.TryGetValue(DoorLocation.Starboard, out var starboardStatus);
        var lockdownStatus = aftStatus && portStatus && starboardStatus;

        // RuMC edit start
        _window.LockdownButton.Text = lockdownStatus
            ? Loc.GetString("rmc-dropship-nav-lift-lockdown")
            : Loc.GetString("rmc-dropship-nav-lockdown");
        _window.LockdownButtonAft.Text = aftStatus
            ? Loc.GetString("rmc-dropship-nav-unlock-aft")
            : Loc.GetString("rmc-dropship-nav-lock-aft");
        _window.LockdownButtonPort.Text = portStatus
            ? Loc.GetString("rmc-dropship-nav-unlock-port")
            : Loc.GetString("rmc-dropship-nav-lock-port");
        _window.LockdownButtonStarboard.Text = starboardStatus
            ? Loc.GetString("rmc-dropship-nav-unlock-starboard")
            : Loc.GetString("rmc-dropship-nav-lock-starboard");
        // RuMC edit end
    }

    private void RefreshLaunchAlarmStatus(bool launchAlarmStatus)
    {
        if (_window == null)
            return;

        // RuMC edit start
        _window.LaunchAlarmButton.Text = launchAlarmStatus
            ? Loc.GetString("rmc-dropship-nav-stop-alarm")
            : Loc.GetString("rmc-dropship-nav-start-alarm");
        // RuMC edit end
    }

    public override void Update()
    {
        if (_window == null || _window.Disposed)
            return;

        if (State is DropshipNavigationTravellingBuiState s)
            Set(s);
    }
}
