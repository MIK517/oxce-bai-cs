namespace Oxce.Gameplay.Campaigns;

/// <summary>Calendar and initial accounting owned by the campaign, invoked by the world capability.</summary>
internal interface ICampaignInitialWorldCalendar
{
    bool IsPending { get; }
    InitialWorldEntry Prepare();
    void Begin(InitialWorldEntry entry);
    void Finish(InitialWorldEntry entry);
}

internal readonly record struct InitialWorldEntry(int Days, long Funds);

public sealed partial class CampaignState : ICampaignInitialWorldCalendar
{
    internal ICampaignInitialWorldCalendar InitialWorldCalendar => _handlers.Operation<ICampaignInitialWorldCalendar>();

    bool ICampaignInitialWorldCalendar.IsPending =>
        MonthsPassed == -1 && _bases.Count != 0 && _bases[0].IsPlaced;

    InitialWorldEntry ICampaignInitialWorldCalendar.Prepare()
    {
        var maintenance = checked(_bases.Sum(owner => CalculateMaintenance(owner).Total) -
            CalculateMaintenance(_bases[0]).Personnel);
        return new(checked(DaysPassed + 1), checked(_funds[^1] - maintenance));
    }

    void ICampaignInitialWorldCalendar.Begin(InitialWorldEntry entry)
    {
        MonthsPassed = 0;
        DaysPassed = entry.Days;
    }

    void ICampaignInitialWorldCalendar.Finish(InitialWorldEntry entry) => _funds[^1] = entry.Funds;
}
