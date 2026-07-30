using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server._RMC14.Language.Systems;
using Content.Server._RMC14.LinkAccount;
using Content.Server.Chat.Systems;
using Content.Shared._RMC14.Marines;
using Content.Shared._RMC14.Language.Prototypes;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Corvax.TTS;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Players.RateLimiting;
using Content.Shared.Radio;
using Content.Shared._RMC14.Survivor;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server.Radio.EntitySystems;
using Content.Server.Radio.Components;
using Content.Shared.Radio.Components;
using Content.Server.Radio;
using Content.Shared._RMC14.Radio;
using Content.Shared.Administration;
using Robust.Server.Player;

namespace Content.Server.Corvax.TTS;

// ReSharper disable once InconsistentNaming
public sealed partial class TTSSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly TTSManager _ttsManager = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly IRobustRandom _rng = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly LinkAccountManager _linkAccount = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private readonly List<string> _sampleText =
        new()
        {
            "Съешь же ещё этих мягких французских булок, да выпей чаю.",
            "Клоун, прекрати разбрасывать банановые кожурки офицерам под ноги!",
            "Капитан, вы уверены что хотите назначить клоуна на должность главы персонала?",
            "Эс Бэ! Тут человек в сером костюме, с тулбоксом и в маске! Помогите!!",
            "Учёные, тут странная аномалия в баре! Она уже съела мима!",
            "Я надеюсь что инженеры внимательно следят за сингулярностью...",
            "Вы слышали эти странные крики в техах? Мне кажется туда ходить небезопасно.",
            "Вы не видели Гамлета? Мне кажется он забегал к вам на кухню.",
            "Здесь есть доктор? Человек умирает от отравленного пончика! Нужна помощь!",
            "Вам нужно согласие и печать квартирмейстера, если вы хотите сделать заказ на партию дробовиков.",
            "Возле эвакуационного шаттла разгерметизация! Инженеры, нам срочно нужна ваша помощь!",
            "Бармен, налей мне самого крепкого вина, которое есть в твоих запасах!"
        };

    private const int MaxMessageChars = 100 * 2; // same as SingleBubbleCharLimit * 2
    private static readonly TimeSpan RadioGhostTtsDedupeTime = TimeSpan.FromSeconds(2);
    private bool _isEnabled = false;
    private bool _referenceVoiceDonorOnly;
    private EntityQuery<TelecomExemptComponent> _exemptQuery;
    private readonly Dictionary<int, TimeSpan> _radioGhostTtsSentAt = new();
    private readonly object _radioGhostTtsLock = new();
    private static readonly TimeSpan ReferenceVoiceCooldown = TimeSpan.FromSeconds(30);
    private readonly Dictionary<NetUserId, TimeSpan> _referenceVoiceCooldowns = new();
    private readonly HashSet<NetUserId> _referenceVoiceUploads = new();
    private readonly HashSet<string> _customVoices = new(StringComparer.Ordinal);
    private readonly HashSet<string> _referenceVoiceOperations = new(StringComparer.Ordinal);
    private uint _nextPlaybackId = 1;
    private Task<bool>? _catalogLoadTask;
    private bool _catalogLoaded;
    private TimeSpan _nextCatalogLoadAttempt;

    public override void Initialize()
    {
        _cfg.OnValueChanged(CCCVars.TTSEnabled, v => _isEnabled = v, true);
        _cfg.OnValueChanged(CCCVars.TTSReferenceVoiceDonorOnly, OnReferenceVoiceDonorOnlyChanged, true);

        SubscribeLocalEvent<TransformSpeechEvent>(OnTransformSpeech);

        SubscribeLocalEvent<TTSComponent, EntitySpokeEvent>(OnEntitySpoke,
            before: new[] { typeof(RadioSystem), typeof(HeadsetSystem) });

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        SubscribeLocalEvent<ActorComponent, HeadsetRadioReceiveRelayEvent>(OnHeadsetRadioReceive);
        SubscribeLocalEvent<TTSComponent, RadioReceiveEvent>(OnIntrinsicRadioReceive);
        SubscribeLocalEvent<RMCAnnouncementMadeEvent>(OnAnnouncementMade);
        SubscribeNetworkEvent<RequestPreviewTTSEvent>(OnRequestPreviewTTS);
        SubscribeNetworkEvent<AddReferenceVoiceRequest>(OnAddReferenceVoice);
        SubscribeNetworkEvent<ReferenceVoiceCatalogRequest>(OnReferenceVoiceCatalogRequest);
        SubscribeNetworkEvent<DeleteReferenceVoiceRequest>(OnDeleteReferenceVoice);

        RegisterRateLimits();
        _linkAccount.PatronUpdated += OnPatronUpdated;
        if (_isEnabled)
            _ = EnsureReferenceVoiceCatalogLoaded();
    }

    public override void Shutdown()
    {
        _linkAccount.PatronUpdated -= OnPatronUpdated;
        base.Shutdown();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _ttsManager.ResetCache();
        _referenceVoiceCooldowns.Clear();
        _referenceVoiceUploads.Clear();
        _referenceVoiceOperations.Clear();
    }

    private async void OnRequestPreviewTTS(RequestPreviewTTSEvent ev, EntitySessionEventArgs args)
    {
        if (!_isEnabled ||
            !TryResolveSpeaker(ev.VoiceId, out var speaker))
            return;

        if (HandleRateLimit(args.SenderSession) != RateLimitStatus.Allowed)
            return;
        Logger.Debug("Вот что у нас вышло: " + ev.VoiceId);
        var previewText = _rng.Pick(_sampleText);
        var soundData = await GenerateTTS(previewText, speaker);
        if (soundData is null)
            return;

        RaiseNetworkEvent(new PlayTTSEvent(soundData), Filter.SinglePlayer(args.SenderSession));
    }

    private async void OnAddReferenceVoice(AddReferenceVoiceRequest ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (!_isEnabled)
        {
            SendReferenceVoiceResult(session, ev.SpeakerName, AddReferenceVoiceResult.Disabled);
            return;
        }

        if (!CanCreateReferenceVoice(session))
        {
            SendReferenceVoiceResult(session, ev.SpeakerName, AddReferenceVoiceResult.NotDonor);
            return;
        }

        if (!CustomTTSVoice.IsValidSpeakerName(ev.SpeakerName))
        {
            SendReferenceVoiceResult(session, ev.SpeakerName, AddReferenceVoiceResult.InvalidName);
            return;
        }

        var userId = session.UserId;
        if (_referenceVoiceUploads.Contains(userId) ||
            _referenceVoiceCooldowns.TryGetValue(userId, out var cooldown) && cooldown > _timing.CurTime)
        {
            SendReferenceVoiceResult(session, ev.SpeakerName, AddReferenceVoiceResult.RateLimited);
            return;
        }

        if (ev.Audio.Length > CustomTTSVoice.MaxAudioBytes)
        {
            SendReferenceVoiceResult(session, ev.SpeakerName, AddReferenceVoiceResult.FileTooLarge);
            return;
        }

        if (!CustomTTSVoice.IsValidWaveFile(ev.Audio))
        {
            SendReferenceVoiceResult(session, ev.SpeakerName, AddReferenceVoiceResult.InvalidAudio);
            return;
        }

        if (!await EnsureReferenceVoiceCatalogLoaded())
        {
            SendReferenceVoiceResult(session, ev.SpeakerName, AddReferenceVoiceResult.ApiError);
            return;
        }

        if (_customVoices.Contains(ev.SpeakerName))
        {
            SendReferenceVoiceResult(session, ev.SpeakerName, AddReferenceVoiceResult.AlreadyExists);
            return;
        }

        if (!_referenceVoiceOperations.Add(ev.SpeakerName))
        {
            SendReferenceVoiceResult(session, ev.SpeakerName, AddReferenceVoiceResult.AlreadyExists);
            return;
        }

        _referenceVoiceUploads.Add(userId);
        _referenceVoiceCooldowns[userId] = _timing.CurTime + ReferenceVoiceCooldown;

        try
        {
            var success = await _ttsManager.AddSpeaker(ev.SpeakerName, ev.Audio);
            if (success)
            {
                _customVoices.Add(ev.SpeakerName);
                BroadcastReferenceVoiceCatalog();
            }

            SendReferenceVoiceResult(session,
                ev.SpeakerName,
                success ? AddReferenceVoiceResult.Success : AddReferenceVoiceResult.ApiError);
        }
        finally
        {
            _referenceVoiceUploads.Remove(userId);
            _referenceVoiceOperations.Remove(ev.SpeakerName);
        }
    }

    private async void OnReferenceVoiceCatalogRequest(ReferenceVoiceCatalogRequest ev, EntitySessionEventArgs args)
    {
        await EnsureReferenceVoiceCatalogLoaded();
        SendReferenceVoiceCatalog(args.SenderSession);
        SendReferenceVoiceAccess(args.SenderSession);
    }

    private void OnPatronUpdated((NetUserId Id, Content.Shared._RMC14.LinkAccount.SharedRMCPatronFull Patron) update)
    {
        if (_playerManager.TryGetSessionById(update.Id, out var session))
            SendReferenceVoiceAccess(session);
    }

    private void SendReferenceVoiceAccess(ICommonSession session)
    {
        RaiseNetworkEvent(new ReferenceVoiceAccessResponse(CanCreateReferenceVoice(session)), session);
    }

    private bool CanCreateReferenceVoice(ICommonSession session)
    {
        return !_referenceVoiceDonorOnly || _linkAccount.GetConnectedPatron(session)?.Tier != null;
    }

    private void OnReferenceVoiceDonorOnlyChanged(bool donorOnly)
    {
        _referenceVoiceDonorOnly = donorOnly;
        foreach (var session in _playerManager.SessionsDict.Values)
            SendReferenceVoiceAccess(session);
    }

    private async void OnDeleteReferenceVoice(DeleteReferenceVoiceRequest ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_adminManager.HasAdminFlag(session, AdminFlags.Host))
        {
            SendDeleteReferenceVoiceResult(session, ev.SpeakerName, DeleteReferenceVoiceResult.Forbidden);
            return;
        }

        if (!CustomTTSVoice.IsValidSpeakerName(ev.SpeakerName))
        {
            SendDeleteReferenceVoiceResult(session, ev.SpeakerName, DeleteReferenceVoiceResult.InvalidName);
            return;
        }

        if (!await EnsureReferenceVoiceCatalogLoaded())
        {
            SendDeleteReferenceVoiceResult(session, ev.SpeakerName, DeleteReferenceVoiceResult.ApiError);
            return;
        }

        // Only names returned by NTTS as custom voices can be deleted. This protects built-in speakers.
        if (!_customVoices.Contains(ev.SpeakerName))
        {
            SendDeleteReferenceVoiceResult(session, ev.SpeakerName, DeleteReferenceVoiceResult.NotFound);
            return;
        }

        if (!_referenceVoiceOperations.Add(ev.SpeakerName))
        {
            SendDeleteReferenceVoiceResult(session, ev.SpeakerName, DeleteReferenceVoiceResult.ApiError);
            return;
        }

        try
        {
            if (!await _ttsManager.DeleteSpeaker(ev.SpeakerName))
            {
                SendDeleteReferenceVoiceResult(session, ev.SpeakerName, DeleteReferenceVoiceResult.ApiError);
                return;
            }

            _customVoices.Remove(ev.SpeakerName);
            _ttsManager.ResetCache();
            BroadcastReferenceVoiceCatalog();
            SendDeleteReferenceVoiceResult(session, ev.SpeakerName, DeleteReferenceVoiceResult.Success);
        }
        finally
        {
            _referenceVoiceOperations.Remove(ev.SpeakerName);
        }
    }

    private Task<bool> EnsureReferenceVoiceCatalogLoaded()
    {
        if (!_isEnabled)
            return Task.FromResult(false);

        if (_catalogLoaded)
            return Task.FromResult(true);

        if (_catalogLoadTask != null)
            return _catalogLoadTask;

        if (_timing.CurTime < _nextCatalogLoadAttempt)
            return Task.FromResult(false);

        _catalogLoadTask = LoadReferenceVoiceCatalog();
        return _catalogLoadTask;
    }

    private async Task<bool> LoadReferenceVoiceCatalog()
    {
        // Ensure EnsureReferenceVoiceCatalogLoaded assigns the coalesced task before this method can clear it.
        await Task.Yield();
        try
        {
            var voices = await _ttsManager.GetCustomSpeakers();
            if (voices == null)
            {
                _nextCatalogLoadAttempt = _timing.CurTime + TimeSpan.FromSeconds(30);
                return false;
            }

            _customVoices.Clear();
            _customVoices.UnionWith(voices);
            _catalogLoaded = true;
            BroadcastReferenceVoiceCatalog();
            return true;
        }
        finally
        {
            _catalogLoadTask = null;
        }
    }

    private void SendReferenceVoiceCatalog(ICommonSession session)
    {
        RaiseNetworkEvent(new ReferenceVoiceCatalogResponse(GetReferenceVoiceCatalog()), session);
    }

    private void BroadcastReferenceVoiceCatalog()
    {
        RaiseNetworkEvent(new ReferenceVoiceCatalogResponse(GetReferenceVoiceCatalog()));
    }

    private string[] GetReferenceVoiceCatalog()
    {
        return _customVoices.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void SendDeleteReferenceVoiceResult(
        ICommonSession session,
        string speakerName,
        DeleteReferenceVoiceResult result)
    {
        RaiseNetworkEvent(new DeleteReferenceVoiceResponse(speakerName, result), session);
    }

    private void SendReferenceVoiceResult(
        ICommonSession session,
        string speakerName,
        AddReferenceVoiceResult result)
    {
        RaiseNetworkEvent(new AddReferenceVoiceResponse(speakerName, result), session);
    }

    private bool TryResolveSpeaker(string voiceId, out string speaker)
    {
        if (_prototypeManager.TryIndex<TTSVoicePrototype>(voiceId, out var prototype))
        {
            speaker = prototype.Speaker;
            return true;
        }

        return CustomTTSVoice.TryGetSpeaker(voiceId, out speaker) && _customVoices.Contains(speaker);
    }

    private async void OnEntitySpoke(EntityUid uid, TTSComponent component, EntitySpokeEvent args)
    {
        var voiceId = component.VoicePrototypeId;

        if (!_isEnabled ||
            args.Message.Length > MaxMessageChars ||
            voiceId == null ||
            args.Channel != null)
        {
            RaiseUnavailable(uid);
            return;
        }

        if (!_prototypeManager.TryIndex(args.Language, out LanguagePrototype? languageProto) ||
            !languageProto.NeedsSpeech)
        {
            RaiseUnavailable(uid);
            return;
        }

        var voiceEv = new TransformSpeakerVoiceEvent(uid, voiceId);
        RaiseLocalEvent(uid, voiceEv);
        voiceId = voiceEv.VoiceId;
        if (!TryResolveSpeaker(voiceId, out var speaker))
        {
            RaiseUnavailable(uid);
            return;
        }

        // Обработка шепота
        if (args.ObfuscatedMessage != null)
        {
            HandleWhisper(uid, args.Message, args.ObfuscatedMessage, speaker, args.Language);
            return;
        }

        HandleSay(uid, args.Message, speaker, component.Faction, args.Language);
    }

    private bool CanReceiveLanguageTts(EntityUid speaker, EntityUid listener, ProtoId<LanguagePrototype> language)
    {
        return listener == speaker || _language.CanUnderstand(listener, language);
    }

    private async void HandleSay(
        EntityUid uid,
        string message,
        string speaker,
        HearingFaction faction,
        ProtoId<LanguagePrototype> language)
    {
        var soundData = await GenerateTTS(message, speaker);
        if (soundData is null)
        {
            RaiseUnavailable(uid);
            return;
        }

        var playbackId = _nextPlaybackId++;
        if (_nextPlaybackId == 0)
            _nextPlaybackId = 1;

        var delivered = new HashSet<NetUserId>();
        var recipients = Filter.Pvs(uid).Recipients;

        foreach (var session in recipients)
        {
            if (!session.AttachedEntity.HasValue)
                continue;

            var listener = session.AttachedEntity.Value;

            if (!TryComp<TTSComponent>(listener, out var listenerTts))
                continue;

            if (listenerTts.Faction != faction)
                continue;

            if (!CanReceiveLanguageTts(uid, listener, language))
                continue;

            RaiseNetworkEvent(new PlayTTSEvent(soundData, GetNetEntity(uid), playbackId: playbackId), session);
            delivered.Add(session.UserId);
        }

        SendGhostTTS(uid, new PlayTTSEvent(soundData), ChatSystem.VoiceRange);

        var dispatched = new TTSUtteranceDispatchedEvent(playbackId, delivered);
        RaiseLocalEvent(uid, ref dispatched);
    }

    private void RaiseUnavailable(EntityUid uid)
    {
        var unavailable = new TTSUtteranceUnavailableEvent();
        RaiseLocalEvent(uid, ref unavailable);
    }

    private async void HandleWhisper(
        EntityUid uid,
        string message,
        string obfMessage,
        string speaker,
        ProtoId<LanguagePrototype> language)
    {
        var fullSoundData = await GenerateTTS(message, speaker, true);
        if (fullSoundData is null) return;

        var obfSoundData = await GenerateTTS(obfMessage, speaker, true);
        if (obfSoundData is null) return;

        var fullTtsEvent = new PlayTTSEvent(fullSoundData, GetNetEntity(uid), true);
        var obfTtsEvent = new PlayTTSEvent(obfSoundData, GetNetEntity(uid), true);

        // TODO: Check obstacles
        var xformQuery = GetEntityQuery<TransformComponent>();
        var sourcePos = _xforms.GetWorldPosition(xformQuery.GetComponent(uid), xformQuery);
        var receptions = Filter.Pvs(uid).Recipients;
        foreach (var session in receptions)
        {
            if (!session.AttachedEntity.HasValue) continue;
            var listener = session.AttachedEntity.Value;
            if (!CanReceiveLanguageTts(uid, listener, language))
                continue;

            var xform = xformQuery.GetComponent(listener);
            var distance = (sourcePos - _xforms.GetWorldPosition(xform, xformQuery)).Length();
            if (distance > ChatSystem.VoiceRange * ChatSystem.VoiceRange)
                continue;

            RaiseNetworkEvent(distance > ChatSystem.WhisperClearRange ? obfTtsEvent : fullTtsEvent, session);
        }

        SendGhostTTS(uid, new PlayTTSEvent(fullSoundData, isWhisper: true), ChatSystem.WhisperMuffledRange);
    }

    private void OnHeadsetRadioReceive(EntityUid uid, ActorComponent actor, ref HeadsetRadioReceiveRelayEvent args)
    {
        _ = HandleRadioTTS(uid, actor, args.RelayedEvent);
    }

    private void OnIntrinsicRadioReceive(EntityUid uid, TTSComponent component, ref RadioReceiveEvent args)
    {
        if (TryComp<ActorComponent>(uid, out var actor))
            _ = HandleRadioTTS(uid, actor, args);
    }

    private bool IsGhostInTTSRange(
        EntityUid listener,
        TransformComponent sourceXform,
        float range,
        EntityQuery<TransformComponent> xformQuery)
    {
        if (!HasComp<GhostComponent>(listener) ||
            !xformQuery.TryGetComponent(listener, out var listenerXform) ||
            listenerXform.MapID != sourceXform.MapID)
        {
            return false;
        }

        return sourceXform.Coordinates.TryDistance(EntityManager, listenerXform.Coordinates, out var distance) &&
               distance <= range;
    }

    private void SendGhostTTS(EntityUid source, PlayTTSEvent ev, float range)
    {
        var xformQuery = GetEntityQuery<TransformComponent>();
        if (!xformQuery.TryGetComponent(source, out var sourceXform))
            return;

        var filter = Filter.Empty()
            .AddWhereAttachedEntity(e => IsGhostInTTSRange(e, sourceXform, range, xformQuery));

        foreach (var session in filter.Recipients)
        {
            RaiseNetworkEvent(ev, session);
        }
    }

    private bool TrySendRadioTtsToGhosts(RadioReceiveEvent ev)
    {
        var key = ev.ChatMsg.GetHashCode();
        var now = _timing.CurTime;

        lock (_radioGhostTtsLock)
        {
            foreach (var (sentKey, sentAt) in _radioGhostTtsSentAt.ToArray())
            {
                if (now - sentAt > RadioGhostTtsDedupeTime)
                    _radioGhostTtsSentAt.Remove(sentKey);
            }

            if (_radioGhostTtsSentAt.ContainsKey(key))
                return false;

            _radioGhostTtsSentAt[key] = now;
            return true;
        }
    }

    private Filter BuildAnnouncementFilter(RMCAnnouncementMadeEvent args)
    {
        if (args.Filter != null)
            return args.Filter;

        var filter = Filter.Empty().AddWhereAttachedEntity(e =>
        {
            var targetFaction = string.IsNullOrWhiteSpace(args.Faction) ? "govfor" : args.Faction.ToLowerInvariant();

            if (TryComp<MarineComponent>(e, out var marine))
            {
                return !string.IsNullOrWhiteSpace(marine.Faction) &&
                       string.Equals(marine.Faction, targetFaction, StringComparison.OrdinalIgnoreCase);
            }

            return HasComp<GhostComponent>(e);
        });

        if (args.ExcludeSurvivors)
            filter.RemoveWhereAttachedEntity(HasComp<RMCSurvivorComponent>);

        var xformQuery = GetEntityQuery<TransformComponent>();
        if (args.Source is { Valid: true } source &&
            xformQuery.TryGetComponent(source, out var sourceXform))
        {
            filter.RemoveWhereAttachedEntity(e =>
                HasComp<GhostComponent>(e) &&
                !IsGhostInTTSRange(e, sourceXform, ChatSystem.VoiceRange, xformQuery));
        }
        else
        {
            filter.RemoveWhereAttachedEntity(HasComp<GhostComponent>);
        }

        return filter;
    }

    private async void OnAnnouncementMade(RMCAnnouncementMadeEvent args)
    {
        var voiceId = "TURRET_FLOOR";
        if (TryComp<TTSComponent>(args.Source, out var component))
            voiceId = component.VoicePrototypeId;
        if (voiceId is null)
            voiceId = "TURRET_FLOOR";

        if (!_isEnabled)
            return;

        if (!TryResolveSpeaker(voiceId, out var speakerName))
            return;
        var soundData = await GenerateTTS(args.RawMessage, speakerName);
        if (soundData is null)
            return;

        var filter = BuildAnnouncementFilter(args);

        foreach (var session in filter.Recipients)
        {
            RaiseNetworkEvent(new PlayTTSEvent(soundData, isRadio: true), session);
        }
    }

    public async Task HandleRadioTTS(
    EntityUid receiver,
    ActorComponent actor,
    RadioReceiveEvent ev)
    {
        if (!_isEnabled)
            return;

        var speaker = ev.MessageSource;

        if (!speaker.IsValid() || !TryComp<TTSComponent>(speaker, out var tts) || tts.VoicePrototypeId == null)
            return;

        if (!CanReceiveLanguageTts(speaker, receiver, ev.Language))
            return;

        if (!TryResolveSpeaker(tts.VoicePrototypeId, out var speakerName))
            return;

        var sendToGhosts = TrySendRadioTtsToGhosts(ev);
        var sound = await GenerateTTS(ev.Message, speakerName);

        if (sound == null)
            return;

        RaiseNetworkEvent(
            new PlayTTSEvent(sound, GetNetEntity(speaker), isRadio: true),
            Filter.SinglePlayer(actor.PlayerSession));

        if (sendToGhosts)
            SendGhostTTS(speaker, new PlayTTSEvent(sound, GetNetEntity(speaker), isRadio: true), ChatSystem.VoiceRange);
    }
    // ReSharper disable once InconsistentNaming
    public async Task<byte[]?> GenerateTTS(string text, string speaker, bool isWhisper = false)
    {
        var textSanitized = Sanitize(text);
        if (textSanitized == "") return null;
        if (char.IsLetter(textSanitized[^1]))
            textSanitized += ".";

        var ssmlTraits = SoundTraits.RateFast;
        if (isWhisper)
            ssmlTraits = SoundTraits.PitchVerylow;
        var textSsml = ToSsmlText(textSanitized, ssmlTraits);

        return await _ttsManager.ConvertTextToSpeech(speaker, textSsml);
    }
}
