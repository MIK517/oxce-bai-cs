using Oxce.Core.Graphics;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Rendering;

namespace Oxce.Engine.Battlescape;

/// <summary>
/// What the battle screens share (Phase 7 branch 3 S6): the command target, the queries for one viewer, the font, the language and
/// the stack the screens sit on. Screens lay out what the queries answer and send commands; they decide nothing.
/// </summary>
public sealed class BattleUiContext
{
    public BattleUiContext(ICampaignCommandTarget commands, IBattleQuery battle, IBattleViewQuery view, IBattleInventoryQuery inventory,
        IBattleBriefingQuery briefing, BattleViewer viewer, ScreenNavigator screens, IndexedSpriteFont? font = null,
        Func<string, string>? localize = null, Func<string, int, IndexedSurface?>? sprites = null)
    {
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        Battle = battle ?? throw new ArgumentNullException(nameof(battle));
        View = view ?? throw new ArgumentNullException(nameof(view));
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Briefing = briefing ?? throw new ArgumentNullException(nameof(briefing));
        Screens = screens ?? throw new ArgumentNullException(nameof(screens));
        Viewer = viewer;
        Font = font ?? IndexedInterfaceFont.Create();
        Localize = localize ?? (static key => key);
        Sprites = sprites;
        Model = new BattleViewModel(commands, battle, view, viewer);
    }

    public ICampaignCommandTarget Commands { get; }
    public IBattleQuery Battle { get; }
    public IBattleViewQuery View { get; }
    public IBattleInventoryQuery Inventory { get; }
    public IBattleBriefingQuery Briefing { get; }
    public BattleViewer Viewer { get; }
    public ScreenNavigator Screens { get; }
    public IndexedSpriteFont Font { get; }
    public Func<string, string> Localize { get; }

    /// <summary>The sprite of a terrain object (data set and index), or null when the client has none; a missing one is a diagnostic.</summary>
    public Func<string, int, IndexedSurface?>? Sprites { get; }

    /// <summary>The client's own selection and camera; every screen of this client shares it.</summary>
    public BattleViewModel Model { get; }

    /// <summary>Missing sprites and other things a screen drew a placeholder for, each once.</summary>
    public SortedSet<string> Diagnostics { get; } = [];

    /// <summary>The screen for the battle's phase: the briefing, the pre-battle inventory or the battlescape (a loaded battle opens it directly).</summary>
    public IIndexedLoopClient? CreateScreen(string targetName = "", string craftOrBaseName = "")
    {
        var overview = Battle.QueryBattle(Viewer);
        if (overview is null) return null;
        return overview.Phase switch
        {
            BattlePhase.Briefing => new BattleBriefingScreen(this, targetName, craftOrBaseName),
            BattlePhase.Inventory => new BattleInventoryScreen(this, preBattle: true),
            _ => new BattlescapeClient(this),
        };
    }

    /// <summary>Pushes the screen of the battle's phase on the stack; false while there is no battle.</summary>
    public bool Open(string targetName = "", string craftOrBaseName = "")
    {
        if (CreateScreen(targetName, craftOrBaseName) is not { } screen) return false;
        Screens.Push(screen);
        return true;
    }

    internal static IndexedPalette CreatePalette()
    {
        var colors = new Rgba32[256];
        Rgba32[] fixedColors =
        [
            new(0, 0, 0), new(10, 18, 32), new(28, 52, 76), new(70, 90, 60), new(150, 120, 80), new(120, 100, 160), new(110, 110, 110),
            new(150, 150, 170), new(240, 120, 30), new(230, 220, 90), new(60, 200, 90), new(220, 60, 60), new(190, 190, 60), new(255, 255, 255),
            new(0, 220, 220), new(60, 60, 80),
        ];
        for (var index = 0; index < fixedColors.Length; index++) colors[index] = fixedColors[index];
        for (var shade = 0; shade < 16; shade++)
        {
            var brightness = (16 - shade) / 16.0;
            colors[16 + shade] = new((byte)(225 * brightness), (byte)(237 * brightness), (byte)(245 * brightness));
            colors[32 + shade] = new((byte)(125 * brightness), (byte)(215 * brightness), (byte)(165 * brightness));
            colors[48 + shade] = new((byte)(255 * brightness), (byte)(170 * brightness), (byte)(120 * brightness));
        }
        return new(colors);
    }

    /// <summary>Palette indices of the placeholder drawing.</summary>
    public static class Color
    {
        public const byte Background = 1, Panel = 2, Floor = 3, WestWall = 4, NorthWall = 5, ObjectPart = 6, Smoke = 7, Fire = 8, Item = 9,
            Xcom = 10, Hostile = 11, Neutral = 12, Selected = 13, Camera = 14, Frame = 15;

        public const int White = 1, Green = 2, Orange = 3;
    }
}
