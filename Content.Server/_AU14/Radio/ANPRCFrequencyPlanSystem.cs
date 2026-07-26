using System.Linq;
using System.Text;
using Content.Shared._AU14.CCVar;
using Content.Shared._AU14.Radio;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Paper;
using Content.Shared.Radio;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._AU14.Radio;

// rolls a fresh signal operating plan every round: faction net frequencies are
// randomized so last round's numbers are worthless and captured frequency cards
// are worth something
public sealed partial class ANPRCFrequencyPlanSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private PaperSystem _paper = default!;

    private const int FrequencyMin = 1000;
    private const int FrequencyMax = 2999;

    private Dictionary<string, int>? _plan;
    private Dictionary<int, ProtoId<RadioChannelPrototype>>? _channelsByFrequency;

    private bool _commsEnabled;

    public override void Initialize()
    {
        Subs.CVar(_config, AU14CCVars.NewCommsSystem, OnCommsToggled, true);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<ANPRCFreqCardComponent, MapInitEvent>(OnFreqCardMapInit);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _plan = null;
        _channelsByFrequency = null;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _plan = null;
        _channelsByFrequency = null;
    }

    private void OnCommsToggled(bool enabled)
    {
        _commsEnabled = enabled;
        _channelsByFrequency = null;

        // cards printed before the toggle carry the wrong numbers, reprint them
        var query = EntityQueryEnumerator<ANPRCFreqCardComponent, PaperComponent>();

        while (query.MoveNext(out var uid, out var card, out var paper))
        {
            _paper.SetContent((uid, paper), GenerateSoi(card.Faction));
        }
    }

    public int GetFrequency(RadioChannelPrototype channel)
    {
        if (!_commsEnabled || channel.Frequency <= 0 || string.IsNullOrEmpty(channel.Faction))
            return channel.Frequency;

        return GetPlan().TryGetValue(channel.ID, out var frequency)
            ? frequency
            : channel.Frequency;
    }

    // a frequency the operator already holds: an unfactioned net, or one belonging to
    // their own faction. the sweep uses this so nobody is made to grind out a number
    // that is already printed on their own freq card
    public bool IsKnownTo(int frequency, string? operatorFaction)
    {
        if (!TryGetChannelByFrequency(frequency, out var channel) ||
            !_prototype.TryIndex(channel, out var proto))
        {
            return false;
        }

        return string.IsNullOrEmpty(proto.Faction) ||
               string.IsNullOrEmpty(operatorFaction) ||
               string.Equals(proto.Faction, operatorFaction, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryGetChannelByFrequency(int frequency, out ProtoId<RadioChannelPrototype> channel)
    {
        if (_channelsByFrequency == null)
        {
            _channelsByFrequency = new Dictionary<int, ProtoId<RadioChannelPrototype>>();

            foreach (var proto in _prototype.EnumeratePrototypes<RadioChannelPrototype>())
            {
                if (proto.ID == SharedChatSystem.HivemindChannel.Id)
                    continue;

                _channelsByFrequency.TryAdd(GetFrequency(proto), new ProtoId<RadioChannelPrototype>(proto.ID));
            }
        }

        return _channelsByFrequency.TryGetValue(frequency, out channel);
    }

    private Dictionary<string, int> GetPlan()
    {
        if (_plan != null)
            return _plan;

        _plan = new Dictionary<string, int>();

        // static channels keep their book frequencies, keep the plan clear of them
        var taken = new HashSet<int>();

        foreach (var proto in _prototype.EnumeratePrototypes<RadioChannelPrototype>())
        {
            if (proto.Frequency > 0)
                taken.Add(proto.Frequency);
        }

        // deterministic enumeration order so the roll count is stable
        var randomized = _prototype.EnumeratePrototypes<RadioChannelPrototype>()
            .Where(proto => proto.Frequency > 0 && !string.IsNullOrEmpty(proto.Faction))
            .OrderBy(proto => proto.ID, StringComparer.Ordinal);

        foreach (var proto in randomized)
        {
            int frequency;

            do
            {
                frequency = _random.Next(FrequencyMin, FrequencyMax + 1);
            }
            while (!taken.Add(frequency));

            _plan[proto.ID] = frequency;
        }

        return _plan;
    }

    private void OnFreqCardMapInit(Entity<ANPRCFreqCardComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp(ent.Owner, out PaperComponent? paper))
            return;

        _paper.SetContent((ent.Owner, paper), GenerateSoi(ent.Comp.Faction));
    }

    private string GenerateSoi(string faction)
    {
        var sb = new StringBuilder();

        sb.AppendLine("[head=2]ИНСТРУКЦИЯ ПО ОРГАНИЗАЦИИ СВЯЗИ[/head]");
        sb.AppendLine("[head=3]ЧАСТОТЫ СЕТЕЙ AN/PRC-117G[/head]");
        sb.AppendLine();

        var channels = _prototype.EnumeratePrototypes<RadioChannelPrototype>()
            .Where(proto => proto.Frequency > 0 &&
                            string.Equals(proto.Faction, faction, StringComparison.OrdinalIgnoreCase))
            .OrderBy(proto => proto.LocalizedName, StringComparer.OrdinalIgnoreCase);

        foreach (var channel in channels)
        {
            sb.AppendLine($"[bold]{channel.LocalizedName}[/bold] - {TunableFrequencySystem.FormatFreq(GetFrequency(channel))} МГц");
        }

        sb.AppendLine();
        sb.AppendLine("Введите частоту во вкладке «ЧАСТОТА» (с точкой или без неё: 2592 и 2.592 равнозначны), чтобы назначить сеть ячейке.");
        sb.AppendLine();
        sb.Append("[italic]COMSEC: контролируемый документ. Уничтожить при угрозе захвата. Частоты назначаются на операцию и перестают действовать после её завершения.[/italic]");

        return sb.ToString();
    }
}
