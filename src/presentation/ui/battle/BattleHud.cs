using System;
using System.Collections.Generic;
using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Arena match overlay: name plates with HP/MP, the command diamond, skill
/// cooldowns, the field stepper, and the result card. Reads the sim through
/// <see cref="ArenaBout"/> and pushes commands back through it.
/// </summary>
public partial class BattleHud : CanvasLayer
{
    private ArenaBout? _bout;
    private Label _message = null!;
    private Label _playerLine = null!;
    private Label _enemyLine = null!;
    private Label _playerMode = null!;
    private Label _enemyMode = null!;
    private ColorRect _playerHp = null!;
    private ColorRect _enemyHp = null!;
    private ColorRect _playerMp = null!;
    private ColorRect _enemyMp = null!;
    private Label _playerHpText = null!;
    private Label _enemyHpText = null!;
    private Label _playerMpText = null!;
    private Label _enemyMpText = null!;
    private readonly Button[] _commands = new Button[4];
    private readonly BattleCommand[] _commandOrder =
    {
        BattleCommand.Attack,
        BattleCommand.Moderate,
        BattleCommand.Distance,
        BattleCommand.Defend,
    };
    private Control _result = null!;
    private Label _resultText = null!;
    private Button _resultButton = null!;
    private Label _mapLabel = null!;
    private float _resultWait = -1f;
    private bool _resultShown;
    private readonly List<SkillSlot> _playerSkills = new();
    private readonly List<SkillSlot> _enemySkills = new();

    /// <summary>Step the field by ±1. Debug only.</summary>
    public Action<int>? StepField { get; set; }
    /// <summary>Pressed on the result card.</summary>
    public Action? Leave { get; set; }
    /// <summary>Fired once when the result card appears, before it is shown.</summary>
    public Action? ResultShown { get; set; }

    public void Build(ArenaBout? bout, string fieldCaption)
    {
        _bout = bout;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        (_playerLine, _playerMode, _playerHp, _playerHpText, _playerMp, _playerMpText) = StatBlock(root, false);
        (_enemyLine, _enemyMode, _enemyHp, _enemyHpText, _enemyMp, _enemyMpText) = StatBlock(root, true);

        _message = HudLabel(22);
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _message.OffsetLeft = -420;
        _message.OffsetTop = 18;
        _message.OffsetRight = 420;
        _message.OffsetBottom = 52;
        root.AddChild(_message);

        var mapRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
        };
        mapRow.AddThemeConstantOverride("separation", 6);
        mapRow.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        mapRow.OffsetLeft = -420;
        mapRow.OffsetTop = 72;
        mapRow.OffsetRight = -16;
        mapRow.OffsetBottom = 108;
        root.AddChild(mapRow);
        mapRow.AddChild(StepButton("<", -1));
        _mapLabel = HudLabel(15);
        _mapLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _mapLabel.CustomMinimumSize = new Vector2(160, 28);
        _mapLabel.Text = fieldCaption;
        mapRow.AddChild(_mapLabel);
        mapRow.AddChild(StepButton(">", 1));

        BuildDiamond(root);
        if (bout != null)
        {
            BuildSkillRow(root, bout.Player, false, _playerSkills);
            BuildSkillRow(root, bout.Enemy, true, _enemySkills);
        }

        _result = new CenterContainer { Visible = false };
        _result.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_result);
        var card = new NinePatchRect
        {
            Texture = UiChrome.DialogueTexture,
            PatchMarginLeft = 24,
            PatchMarginTop = 24,
            PatchMarginRight = 24,
            PatchMarginBottom = 24,
            CustomMinimumSize = new Vector2(460, 180),
        };
        _result.AddChild(card);
        var cardBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        cardBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        cardBox.OffsetLeft = 28;
        cardBox.OffsetTop = 24;
        cardBox.OffsetRight = -28;
        cardBox.OffsetBottom = -20;
        cardBox.AddThemeConstantOverride("separation", 16);
        card.AddChild(cardBox);
        _resultText = HudLabel(28);
        _resultText.HorizontalAlignment = HorizontalAlignment.Center;
        cardBox.AddChild(_resultText);
        _resultButton = UiChrome.Button("Return to the lobby", () => Leave?.Invoke());
        cardBox.AddChild(_resultButton);
    }

    public void SetFieldCaption(string caption) => _mapLabel.Text = caption;

    public void SetMessage(string text) => _message.Text = text;

    public void SetLeaveLabel(string text) => _resultButton.Text = text;

    /// <summary>Repaint from the sim. Reveals the result card shortly after the bout ends.</summary>
    public void Refresh(float delta)
    {
        if (_bout == null)
            return;
        Combatant player = _bout.Player;
        Combatant enemy = _bout.Enemy;
        _playerLine.Text = player.Name;
        _enemyLine.Text = enemy.Name;
        _playerMode.Text = player.Command.Label();
        _enemyMode.Text = enemy.Command.Label();
        SetMeter(_playerHp, _playerHpText, player.Hp, player.MaxHp);
        SetMeter(_playerMp, _playerMpText, player.Mp, player.MaxMp);
        SetMeter(_enemyHp, _enemyHpText, enemy.Hp, enemy.MaxHp);
        SetMeter(_enemyMp, _enemyMpText, enemy.Mp, enemy.MaxMp);
        _message.Text = _bout.Message;

        for (int i = 0; i < _commands.Length; i++)
            PaintOrb(_commands[i], OrbColor(_commandOrder[i]), player.Command == _commandOrder[i]);
        PaintSkills(_playerSkills, player);
        PaintSkills(_enemySkills, enemy);

        if (!_bout.Over || _resultShown)
            return;
        if (_resultWait < 0f)
            _resultWait = 1.7f;
        _resultWait -= delta;
        if (_resultWait > 0f)
            return;
        _resultShown = true;
        _resultText.Text = _bout.Message;
        ResultShown?.Invoke();
        _result.Visible = true;
    }

    private Button StepButton(string text, int step)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(36, 28),
        };
        button.AddThemeFontSizeOverride("font_size", 16);
        button.Pressed += () => StepField?.Invoke(step);
        return button;
    }

    private void BuildSkillRow(Control root, Combatant fighter, bool enemy, List<SkillSlot> slots)
    {
        float size = enemy ? 46f : 68f;
        var row = new HBoxContainer
        {
            Alignment = enemy ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", enemy ? 6 : 10);
        if (enemy)
        {
            row.SetAnchorsPreset(Control.LayoutPreset.TopRight, true);
            row.OffsetLeft = -360;
            row.OffsetTop = 122;
            row.OffsetRight = -28;
            row.OffsetBottom = 122 + size;
        }
        else
        {
            row.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
            row.OffsetLeft = -220;
            row.OffsetRight = 220;
            row.OffsetTop = -118;
            row.OffsetBottom = -118 + size;
        }
        root.AddChild(row);

        foreach (AbilityRecord ability in fighter.Kit.Abilities)
        {
            var slot = new SkillSlot();
            var frame = new Control
            {
                CustomMinimumSize = new Vector2(size, size),
                TooltipText = ability.Mp > 0 ? ability.Name : $"{ability.Name}  (auto)",
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            var icon = new TextureRect
            {
                Texture = UiChrome.Load(DigimonKit.IconFor(ability)),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            frame.AddChild(icon);

            var shade = new ColorRect
            {
                Color = new Color(0.02f, 0.04f, 0.08f, 0.72f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            };
            shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            frame.AddChild(shade);

            Label time = HudLabel(enemy ? 14 : 18);
            time.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            time.HorizontalAlignment = HorizontalAlignment.Center;
            time.VerticalAlignment = VerticalAlignment.Center;
            time.MouseFilter = Control.MouseFilterEnum.Ignore;
            frame.AddChild(time);

            row.AddChild(frame);
            slot.Icon = icon;
            slot.Shade = shade;
            slot.Time = time;
            slots.Add(slot);
        }
    }

    private static void PaintSkills(List<SkillSlot> slots, Combatant fighter)
    {
        int count = Mathf.Min(slots.Count, fighter.Cooldown.Length);
        for (int i = 0; i < count; i++)
        {
            float cool = fighter.Cooldown[i];
            float max = fighter.CooldownMax[i];
            float covered = max > 0.01f ? Mathf.Clamp(cool / max, 0f, 1f) : 0f;
            bool waiting = cool > 0.05f;
            slots[i].Shade.Visible = waiting;
            slots[i].Shade.AnchorTop = 0f;
            slots[i].Shade.AnchorBottom = covered;
            slots[i].Shade.OffsetTop = 0f;
            slots[i].Shade.OffsetBottom = 0f;
            slots[i].Time.Text = waiting ? Mathf.CeilToInt(cool).ToString() : "";
            slots[i].Icon.Modulate = waiting ? new Color(0.45f, 0.48f, 0.55f) : Colors.White;
        }
    }

    private sealed class SkillSlot
    {
        public TextureRect Icon = null!;
        public ColorRect Shade = null!;
        public Label Time = null!;
    }

    private void BuildDiamond(Control root)
    {
        var diamond = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        diamond.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        diamond.OffsetLeft = 36;
        diamond.OffsetTop = -330;
        diamond.OffsetRight = 360;
        diamond.OffsetBottom = -28;
        root.AddChild(diamond);

        var caption = HudLabel(15);
        caption.Text = "Command";
        caption.AddThemeColorOverride("font_color", new Color(1f, 0.86f, 0.45f));
        caption.Position = new Vector2(112, 0);
        diamond.AddChild(caption);

        // Attack north, Defend west, Moderate east, Distance south.
        PlaceOrb(diamond, BattleCommand.Attack, 108, 36);
        PlaceOrb(diamond, BattleCommand.Defend, 8, 112);
        PlaceOrb(diamond, BattleCommand.Moderate, 208, 112);
        PlaceOrb(diamond, BattleCommand.Distance, 108, 198);
    }

    private void PlaceOrb(Control diamond, BattleCommand command, float x, float y)
    {
        var button = new Button
        {
            Text = command.Label(),
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(84, 84),
            Position = new Vector2(x, y),
        };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", new Color(1f, 0.95f, 0.75f));
        button.Pressed += () => _bout?.SetCommand(command);
        diamond.AddChild(button);
        int slot = Array.IndexOf(_commandOrder, command);
        _commands[slot] = button;
        PaintOrb(button, OrbColor(command), false);
    }

    private static (Label Name, Label Mode, ColorRect Hp, Label HpText, ColorRect Mp, Label MpText) StatBlock(Control root, bool enemy)
    {
        var plate = new TextureRect
        {
            Texture = UiChrome.Load(enemy ? "plate_red.png" : "plate_blue.png"),
            FlipH = enemy,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        plate.SetAnchorsPreset(enemy ? Control.LayoutPreset.TopRight : Control.LayoutPreset.TopLeft, true);
        if (enemy)
        {
            plate.OffsetLeft = -400;
            plate.OffsetTop = 4;
            plate.OffsetRight = -8;
            plate.OffsetBottom = 112;
        }
        else
        {
            plate.OffsetLeft = 8;
            plate.OffsetTop = 4;
            plate.OffsetRight = 400;
            plate.OffsetBottom = 112;
        }
        root.AddChild(plate);

        var block = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        block.AddThemeConstantOverride("separation", 2);
        block.SetAnchorsPreset(enemy ? Control.LayoutPreset.TopRight : Control.LayoutPreset.TopLeft, true);
        if (enemy)
        {
            block.OffsetLeft = -340;
            block.OffsetTop = 16;
            block.OffsetRight = -28;
            block.OffsetBottom = 118;
        }
        else
        {
            block.OffsetLeft = 28;
            block.OffsetTop = 16;
            block.OffsetRight = 340;
            block.OffsetBottom = 118;
        }
        root.AddChild(block);

        Label name = HudLabel(22);
        Label mode = HudLabel(15);
        mode.AddThemeColorOverride("font_color", new Color(1f, 0.86f, 0.45f));
        if (enemy)
        {
            name.HorizontalAlignment = HorizontalAlignment.Right;
            mode.HorizontalAlignment = HorizontalAlignment.Right;
        }
        block.AddChild(name);
        block.AddChild(mode);
        (ColorRect hp, Label hpText) = MeterRow(block, new Color(0.93f, 0.93f, 0.95f), enemy);
        (ColorRect mp, Label mpText) = MeterRow(block, new Color(0.35f, 0.66f, 0.95f), enemy);
        return (name, mode, hp, hpText, mp, mpText);
    }

    private static (ColorRect Fill, Label Value) MeterRow(VBoxContainer block, Color fill, bool enemy)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.Alignment = enemy ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Begin;
        block.AddChild(row);

        var track = new ColorRect
        {
            Color = new Color(0.05f, 0.07f, 0.1f, 0.55f),
            CustomMinimumSize = new Vector2(MeterWidth, 8),
        };
        var bar = new ColorRect
        {
            Color = fill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        track.AddChild(bar);

        Label value = HudLabel(14);
        value.CustomMinimumSize = new Vector2(48, 0);
        value.HorizontalAlignment = enemy ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        if (enemy)
        {
            row.AddChild(value);
            row.AddChild(track);
        }
        else
        {
            row.AddChild(track);
            row.AddChild(value);
        }
        return (bar, value);
    }

    private const float MeterWidth = 210f;

    private static void SetMeter(ColorRect fill, Label value, int current, int max)
    {
        float k = max > 0 ? Mathf.Clamp((float)current / max, 0f, 1f) : 0f;
        fill.AnchorRight = k;
        fill.OffsetRight = 0f;
        value.Text = current.ToString();
    }

    private static Color OrbColor(BattleCommand command)
    {
        return command switch
        {
            BattleCommand.Attack => new Color(0.90f, 0.28f, 0.22f),
            BattleCommand.Distance => new Color(0.28f, 0.70f, 0.38f),
            BattleCommand.Defend => new Color(0.28f, 0.50f, 0.86f),
            _ => new Color(0.86f, 0.72f, 0.22f),
        };
    }

    private static void PaintOrb(Button button, Color color, bool lit)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(color.R, color.G, color.B, lit ? 0.95f : 0.78f),
            CornerRadiusTopLeft = 42,
            CornerRadiusTopRight = 42,
            CornerRadiusBottomRight = 42,
            CornerRadiusBottomLeft = 42,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
            BorderColor = new Color(1f, 0.86f, 0.35f),
        };
        int edge = lit ? 4 : 1;
        box.SetBorderWidthAll(edge);
        if (!lit)
            box.BorderColor = new Color(1f, 1f, 1f, 0.28f);
        button.AddThemeStyleboxOverride("normal", box);
        button.AddThemeStyleboxOverride("hover", box);
        button.AddThemeStyleboxOverride("pressed", box);
        button.AddThemeStyleboxOverride("focus", box);
    }

    private static Label HudLabel(int size)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.05f, 0.1f));
        label.AddThemeConstantOverride("outline_size", 6);
        return label;
    }
}
