using Content.Shared._RMC14.Onboarding;

namespace Content.Client._RMC14.Onboarding;

public sealed partial class RMCOnboardingSystem : EntitySystem
{
    private RMCOnboardingWindow? _window;
    private RMCOnboardingTaskWindow? _taskWindow;
    private bool _taskActive;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<RMCOnboardingOfferEvent>(OnOffer);
        SubscribeNetworkEvent<RMCOnboardingTaskEvent>(OnTask);
    }

    private void OnOffer(RMCOnboardingOfferEvent ev, EntitySessionEventArgs args)
    {
        _window?.Close();
        _window = new RMCOnboardingWindow();
        _window.SetTracks(ev.Tracks);
        _window.TrackSelected += OnTrackSelected;
        _window.Skipped += OnSkipped;
        _window.OnClose += OnWindowClosed;
        _window.OpenCentered();
    }

    public void RequestMenu()
    {
        RaiseNetworkEvent(new RMCOnboardingRequestMenuEvent());
    }

    private void OnTrackSelected(RMCOnboardingTrack track)
    {
        RaiseNetworkEvent(new RMCOnboardingSelectTrackEvent(true, track));
        _window?.Close();
    }

    private void OnSkipped()
    {
        _window?.Close();
    }

    private void OnTask(RMCOnboardingTaskEvent ev, EntitySessionEventArgs args)
    {
        if (!ev.Active)
        {
            _taskActive = false;
            _taskWindow?.Close();
            return;
        }

        _taskActive = true;

        if (_taskWindow == null)
        {
            _taskWindow = new RMCOnboardingTaskWindow();
            _taskWindow.ExitRequested += OnExitRequested;
            _taskWindow.OnClose += OnTaskWindowClosed;
        }

        _taskWindow.SetTask(ev);
        if (!_taskWindow.IsOpen)
            _taskWindow.OpenCenteredRight();
    }

    private void OnExitRequested()
    {
        RaiseNetworkEvent(new RMCOnboardingExitEvent());
    }

    private void OnTaskWindowClosed()
    {
        if (_taskWindow == null)
            return;

        _taskWindow.ExitRequested -= OnExitRequested;
        _taskWindow.OnClose -= OnTaskWindowClosed;
        _taskWindow = null;

        if (_taskActive)
        {
            _taskActive = false;
            RaiseNetworkEvent(new RMCOnboardingExitEvent());
        }
    }

    private void OnWindowClosed()
    {
        if (_window == null)
            return;

        _window.TrackSelected -= OnTrackSelected;
        _window.Skipped -= OnSkipped;
        _window.OnClose -= OnWindowClosed;
        _window = null;
    }
}
