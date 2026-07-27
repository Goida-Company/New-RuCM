using Content.Client._RMC14.UserInterface;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._AU14.Chemistry.Reagents;
using Content.Shared._AU14.Chemistry.Research;
using Content.Shared._CMU14.Chemistry.Reagent;
using Content.Shared._RMC14.Chemistry.Reagent;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Content.Client._AU14.Chemistry.Research;

[UsedImplicitly]
public sealed partial class ResearchDataTerminalBui : BoundUserInterface
{
    [Dependency] private IGameTiming _time = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    private RMCReagentSystem _reagent = default!;
    private ResearchDataTerminalWindow? _window;

    // Кэш строк таблицы DataTable — ключи в порядке отображения
    private readonly List<int> _dataKeys = new();
    private readonly Dictionary<int, DataRowControl> _dataRows = new();

    // Кэш карточек химикатов
    private readonly List<ChemCardControl> _chemCards = new();

    // StyleBoxFlat — static readonly, создаются один раз на всё время жизни приложения
    private static readonly StyleBoxFlat StylePanel = new()
    {
        BackgroundColor = Color.FromHex("#0f0f00"),
        BorderColor = Color.FromHex("#ffbf00"),
        BorderThickness = new Thickness(2f)
    };
    private static readonly StyleBoxFlat StylePanelL = new()
    {
        BackgroundColor = Color.FromHex("#0f0f00"),
        BorderColor = Color.FromHex("#ffbf00"),
        BorderThickness = new Thickness(2f, 1f, 1f, 1f)
    };
    private static readonly StyleBoxFlat StylePanelM = new()
    {
        BackgroundColor = Color.FromHex("#0f0f00"),
        BorderColor = Color.FromHex("#ffbf00"),
        BorderThickness = new Thickness(1f, 1f, 1f, 1f)
    };
    private static readonly StyleBoxFlat StylePanelR = new()
    {
        BackgroundColor = Color.FromHex("#0f0f00"),
        BorderColor = Color.FromHex("#ffbf00"),
        BorderThickness = new Thickness(1f, 1f, 2f, 1f)
    };
    private static readonly StyleBoxFlat StylePanelLB = new()
    {
        BackgroundColor = Color.FromHex("#0f0f00"),
        BorderColor = Color.FromHex("#ffbf00"),
        BorderThickness = new Thickness(2f, 1f, 1f, 2f)
    };
    private static readonly StyleBoxFlat StylePanelMB = new()
    {
        BackgroundColor = Color.FromHex("#0f0f00"),
        BorderColor = Color.FromHex("#ffbf00"),
        BorderThickness = new Thickness(1f, 1f, 1f, 2f)
    };
    private static readonly StyleBoxFlat StylePanelRB = new()
    {
        BackgroundColor = Color.FromHex("#0f0f00"),
        BorderColor = Color.FromHex("#ffbf00"),
        BorderThickness = new Thickness(1f, 1f, 2f, 2f)
    };
    private static readonly StyleBoxFlat StyleButton = new()
    {
        BackgroundColor = Color.FromHex("#ffbf00")
    };

    private readonly ResearchDataTerminalAttemptUpgradeBuiMsg _upgradeAttempt = new();
    private readonly ResearchDataTerminalPrintLastBuiMsg _printLast = new();

    public ResearchDataTerminalBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        // Явный вызов гарантирует инъекцию [Dependency] полей до использования
        IoCManager.InjectDependencies(this);
        _reagent = EntMan.System<RMCReagentSystem>();
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ResearchDataTerminalWindow>();
        _window.Reprint.OnPressed += _ => SendPredictedMessage(_printLast);
        _window.Upgrade.OnPressed += _ => SendPredictedMessage(_upgradeAttempt);
        if (State is ResearchDataTerminalBuiState s)
            RefreshState(s);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ResearchDataTerminalBuiState s)
            RefreshState(s);
    }

    private void RefreshState(ResearchDataTerminalBuiState state)
    {
        if (_window is null)
            return;

        // Статические элементы
        string clearance = state.Clearance == 6 ? "X" : state.Clearance.ToString();
        _window.Clearance.Text = Loc.GetString("research-data-ui-clearance", ("NUM", clearance));
        _window.Credits.Text = Loc.GetString("research-data-ui-credits", ("NUM", state.Credits));
        _window.Tabs.SetTabTitle(0, Loc.GetString("research-data-ui-manage"));
        _window.Tabs.SetTabTitle(1, Loc.GetString("research-data-ui-view"));
        _window.NextUpdate = state.NextUpdate;
        _window.TimeLeftBar.MaxValue = (float)(state.NextUpdate - state.LastTime).TotalMilliseconds;
        _window.Upgrade.Disabled = !(state.Credits >= state.UpgradeCost && state.Clearance != 6);
        _window.UpgradeText.Text = Loc.GetString("research-data-ui-improve", ("NUM", state.UpgradeCost));

        // --- DataTable ---
        // Сравниваем набор и порядок ключей с кэшем
        var newKeys = state.Data.Keys.ToList();
        bool keysChanged = !newKeys.SequenceEqual(_dataKeys);

        if (keysChanged)
        {
            // Пересоздаём все строки при изменении набора/порядка ключей
            _window.DataTable.RemoveChildrenAfter(_window.TableAfter.GetPositionInParent() + 1);
            _dataRows.Clear();
            _dataKeys.Clear();

            for (int i = 0; i < newKeys.Count; i++)
            {
                int key = newKeys[i];
                bool isLast = i == newKeys.Count - 1;
                // key захватывается напрямую — неизменяемый int, замыкание безопасно
                var row = new DataRowControl(key, k => SendPredictedMessage(new ResearchDataTerminalPrintChemBuiMsg(k)));
                row.Update(state.Data[key], isLast);
                _dataRows[key] = row;
                _dataKeys.Add(key);
                _window.DataTable.AddChild(row.A);
                _window.DataTable.AddChild(row.B);
                _window.DataTable.AddChild(row.C);
                _window.DataTable.AddChild(row.D);
            }
        }
        else
        {
            // Набор ключей не изменился — только обновляем данные
            for (int i = 0; i < _dataKeys.Count; i++)
            {
                int key = _dataKeys[i];
                bool isLast = i == _dataKeys.Count - 1;
                _dataRows[key].Update(state.Data[key], isLast);
            }
        }

        // --- ChemContainer ---
        int needed = state.IDs.Count;

        // Добавляем недостающие карточки
        while (_chemCards.Count < needed)
        {
            var card = new ChemCardControl(id => SendPredictedMessage(new ResearchDataTerminalPickChemBuiMsg(id)));
            _chemCards.Add(card);
            _window.ChemContainer.AddChild(card.Root);
        }

        // Удаляем лишние карточки с конца
        while (_chemCards.Count > needed)
        {
            var last = _chemCards[_chemCards.Count - 1];
            _window.ChemContainer.RemoveChild(last.Root);
            _chemCards.RemoveAt(_chemCards.Count - 1);
        }

        // Обновляем данные во всех карточках по индексу
        bool buttonDisabled = _time.CurTime < state.NextUpdate && state.Picked;
        for (int i = 0; i < needed; i++)
        {
            _chemCards[i].Update(state.IDs[i], buttonDisabled, _reagent, _protoMan);
        }
    }

    // Вспомогательный класс для строки таблицы DataTable (4 ячейки)
    private sealed class DataRowControl
    {
        public readonly PanelContainer A;
        public readonly PanelContainer B;
        public readonly PanelContainer C;
        public readonly PanelContainer D;

        private readonly RichTextLabel _timeLabel;
        private readonly RichTextLabel _analysisLabel;
        private readonly RichTextLabel _nameLabel;

        public DataRowControl(int key, Action<int> printAction)
        {
            _timeLabel = new RichTextLabel();
            _analysisLabel = new RichTextLabel();
            _nameLabel = new RichTextLabel();

            Button read = new Button();
            read.Margin = new(5f);
            read.StyleBoxOverride = StyleButton;
            RichTextLabel readl = new RichTextLabel();
            readl.Text = Loc.GetString("research-data-ui-read");
            read.AddChild(readl);
            read.Disabled = true;

            Button print = new Button();
            print.Margin = new(5f);
            print.StyleBoxOverride = StyleButton;
            RichTextLabel printl = new RichTextLabel();
            printl.Text = Loc.GetString("research-data-ui-print");
            print.AddChild(printl);
            // Подписка ОДИН РАЗ — захватывает key (неизменяемый int, не замыкание на переменную цикла)
            print.OnPressed += _ => printAction(key);

            BoxContainer con = new BoxContainer();
            con.Orientation = BoxContainer.LayoutOrientation.Horizontal;
            con.SeparationOverride = 20;
            con.AddChild(read);
            con.AddChild(print);

            A = new PanelContainer();
            B = new PanelContainer();
            C = new PanelContainer();
            D = new PanelContainer();
            A.AddChild(_timeLabel);
            B.AddChild(_analysisLabel);
            C.AddChild(_nameLabel);
            D.AddChild(con);
        }

        public void Update((string, string, TimeSpan, bool, GeneratedReagentData, bool, bool) datum, bool isLast)
        {
            var time = datum.Item3;
            var analysis = datum.Item4;
            var name = datum.Item5.Name;

            _timeLabel.Text = Loc.GetString("research-data-ui-scan-time-idx", ("TIME", time.ToString(@"h\:mm\:ss")));
            _analysisLabel.Text = analysis
                ? Loc.GetString("research-data-ui-analysis-sim")
                : Loc.GetString("research-data-ui-analysis-scan");
            _nameLabel.Text = Loc.GetString("research-data-ui-compound-idx", ("NAME", name));

            // Обновляем стили границ (последняя строка имеет нижнюю границу)
            A.PanelOverride = isLast ? StylePanelLB : StylePanelL;
            B.PanelOverride = isLast ? StylePanelMB : StylePanelM;
            C.PanelOverride = isLast ? StylePanelMB : StylePanelM;
            D.PanelOverride = isLast ? StylePanelRB : StylePanelR;
        }
    }

    // Вспомогательный класс для карточки химиката
    private sealed class ChemCardControl
    {
        public readonly BoxContainer Root;
        // public — обновляется через Update(), читается лямбдой в конструкторе
        public string _chemId = string.Empty;

        private readonly RichTextLabel _nameLabel;
        private readonly RichTextLabel _diffLabel;
        private readonly RichTextLabel _descLabel;
        private readonly Button _takeButton;

        public ChemCardControl(Action<string> pickAction)
        {
            Root = new BoxContainer();
            Root.Orientation = BoxContainer.LayoutOrientation.Vertical;

            PanelContainer panela = new PanelContainer();
            panela.VerticalExpand = true;
            panela.HorizontalExpand = true;
            panela.PanelOverride = StylePanel;

            _nameLabel = new RichTextLabel();
            _nameLabel.Margin = new Thickness(5f, 10f);
            panela.AddChild(_nameLabel);

            BoxContainer box2 = new BoxContainer();
            box2.Orientation = BoxContainer.LayoutOrientation.Vertical;

            HSpacer spacer = new HSpacer();
            spacer.Spacing = 15;

            _diffLabel = new RichTextLabel();
            _descLabel = new RichTextLabel();

            _takeButton = new Button();
            _takeButton.Margin = new(5, 5, 5, 5);
            _takeButton.StyleBoxOverride = StyleButton;
            RichTextLabel cont = new RichTextLabel();
            cont.Text = Loc.GetString("research-data-ui-chem-take");
            cont.HorizontalAlignment = Control.HAlignment.Left;
            _takeButton.AddChild(cont);
            // Подписка ОДИН РАЗ — читает _chemId при нажатии (не при создании)
            _takeButton.OnPressed += _ => pickAction(_chemId);

            PanelContainer panelb = new PanelContainer();
            panelb.PanelOverride = StylePanel;

            box2.AddChild(spacer);
            box2.AddChild(_diffLabel);
            box2.AddChild(_descLabel);
            box2.AddChild(_takeButton);
            panelb.AddChild(box2);

            Root.AddChild(panela);
            Root.AddChild(panelb);
        }

        public void Update(GeneratedReagentData chem, bool buttonDisabled, RMCReagentSystem reagent, IPrototypeManager protoMan)
        {
            _chemId = chem.ID;
            _nameLabel.Text = Loc.GetString("research-data-ui-chem-name", ("NAME", chem.Name));

            string difficulty = chem.GenTier >= 3
                ? Loc.GetString("research-data-ui-diff-hard")
                : chem.GenTier == 2
                    ? Loc.GetString("research-data-ui-diff-inter")
                    : Loc.GetString("research-data-ui-diff-easy");
            _diffLabel.Text = Loc.GetString("research-data-ui-chem-difficulty", ("DIFF", difficulty));

            // Локализация RecipeHint: ID реагента → локализованное имя через RMCReagentSystem
            string reciHint = chem.RecipeHint;
            if (reagent.TryIndex(chem.RecipeHint, out var reagentProto))
                reciHint = reagentProto.LocalizedName;

            // Локализация PropertyHint: ID свойства → локализованное имя через IPrototypeManager
            // Анализатор RMCA0000 блокирует только ReagentPrototype, не ReagentPropertyPrototype
            string propHint = chem.PropertyHint;
            if (protoMan.TryIndex<ReagentPropertyPrototype>(chem.PropertyHint, out var propProto))
                propHint = propProto.LocalizedName;

            _descLabel.Text = Loc.GetString("research-data-ui-chem-desc", ("RECIHINT", reciHint), ("PROPHINT", propHint));
            _takeButton.Disabled = buttonDisabled;
        }
    }
}
