using Oxce.Engine.Input;
using Oxce.Gameplay.Battles;

namespace Oxce.Engine.Battlescape;

/// <summary>
/// <c>BriefingState</c>: the mission's title, description and the target and craft lines. Enter is <c>btnOkClick</c> (a
/// <see cref="ConfirmBriefing"/> command; the pre-battle inventory follows); a refusal of the rules stays on the screen as its code.
/// The cutscene, music and background are the client's presentation and are listed, not played.
/// </summary>
public sealed class BattleBriefingScreen : BattleScreenBase
{
    private BattleBriefing? _briefing;

    public BattleBriefingScreen(BattleUiContext context, string targetName = "", string craftOrBaseName = "") : base(context)
    {
        // The names are the client's composed lines; a second open of the briefing is refused by the rules and ignored.
        Context.Commands.Execute(new BeginBriefing(targetName, craftOrBaseName));
        _briefing = Context.Briefing.QueryBriefing();
        Changed();
    }

    public BattleBriefing? Briefing => _briefing;

    protected override bool Handle(in GameInputEvent input)
    {
        if (input.Kind != GameInputEventKind.KeyPressed || input.IsRepeat || input.KeyCode is not (KeyEnter or KeyEscape)) return false;
        var result = Context.Commands.Execute(new ConfirmBriefing());
        Message = Code(result);
        if (Message.Length == 0)
            Context.Screens.Replace(new BattleInventoryScreen(Context, preBattle: true));
        return true;
    }

    protected override bool Poll()
    {
        var current = Context.Briefing.QueryBriefing();
        if (Equals(current, _briefing)) return false;
        _briefing = current;
        return true;
    }

    protected override void Draw()
    {
        if (_briefing is not { } briefing)
        {
            Text("No briefing", 16, 16, BattleUiContext.Color.Orange);
            return;
        }
        var y = 16;
        Text(Context.Localize(briefing.Title), 16, y, BattleUiContext.Color.Green);
        y += 24;
        if (briefing.ShowTarget)
        {
            // BriefingState: with operation names the target line is the "operation" text with the name in it.
            var target = briefing.TargetName;
            if (briefing.OperationName)
            {
                var template = Context.Localize("STR_OPERATION_UC");
                target = template.Contains("{0}", StringComparison.Ordinal) ? template.Replace("{0}", target, StringComparison.Ordinal) : $"{template} {target}";
            }
            Text(target, 16, y);
            y += 14;
        }
        if (briefing.ShowCraft) { Text(briefing.CraftOrBaseName, 16, y); y += 14; }
        y += 8;
        foreach (var line in Context.Localize(briefing.Description).Split('\n'))
        {
            Text(line, 16, y);
            y += 12;
        }
        Text("Enter or Esc: OK", 16, 372);
        if (Message.Length != 0) Text(Message, 16, 388, BattleUiContext.Color.Orange);
    }
}
