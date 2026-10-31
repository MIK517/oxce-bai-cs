using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Rendering;

namespace Oxce.Engine.Battlescape;

/// <summary>Frame, palette, revision and message handling the battle screens share.</summary>
public abstract class BattleScreenBase(BattleUiContext context) : IIndexedLoopClient
{
    protected const uint KeyEscape = 27, KeyEnter = 13, KeyTab = 9, KeyRight = 0x4000004f, KeyLeft = 0x40000050, KeyDown = 0x40000051,
        KeyUp = 0x40000052, KeyPageUp = 0x4000004b, KeyPageDown = 0x4000004e;

    private long _revision;

    protected BattleUiContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

    public IndexedSurface Frame { get; } = new(640, 400);

    public IndexedPalette Palette { get; } = BattleUiContext.CreatePalette();

    public long PresentationRevision => _revision;

    public bool ExitRequested { get; protected set; }

    /// <summary>The refusal code or note of the last action, shown on the screen.</summary>
    public string Message { get; protected set; } = "";

    public void HandleInput(in GameInputEvent input)
    {
        if (Handle(input)) Changed();
    }

    public virtual void Tick(TimeSpan fixedInterval)
    {
        // The battle changes only through commands: nothing here advances by itself.
        if (Poll()) Changed();
    }

    /// <summary>Handles one input; true when the screen needs drawing again.</summary>
    protected abstract bool Handle(in GameInputEvent input);

    /// <summary>Looks for a change made elsewhere (another client's command); true when the screen needs drawing again.</summary>
    protected virtual bool Poll() => false;

    protected abstract void Draw();

    protected void Changed()
    {
        Frame.Clear(BattleUiContext.Color.Background);
        Draw();
        _revision++;
    }

    protected void Text(string text, int x, int y, int group = BattleUiContext.Color.White) =>
        Context.Font.DrawText(Frame, text, x, y, group);

    protected static string Code(CampaignCommandResult result) =>
        result.Events.OfType<CampaignActionBlocked>().FirstOrDefault()?.Code ?? "";

    protected static bool Shift(in GameInputEvent input) => (input.Modifiers & InputKeyModifiers.Shift) != 0;
}
