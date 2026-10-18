using System.Text;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>
/// The monthly report as <c>MonthlyReportState</c> shows it: the closed month, its rating, funding and council
/// decision. Texts are string IDs; the country lists are country rule IDs.
/// </summary>
public sealed record CampaignMonthlyReport(
    int Month,
    int Year,
    int Rating,
    string RatingText,
    long CountryFunding,
    int FundingChange,
    long Maintenance,
    int PerformanceBonus,
    long Balance,
    string CouncilText,
    bool DebtWarning,
    IReadOnlyList<string> HappyCountries,
    IReadOnlyList<string> UnhappyCountries,
    IReadOnlyList<string> SignedPacts,
    IReadOnlyList<string> CancelledPacts)
{
    /// <summary>0 when play goes on; 1 after two bad months; 2 for funds still too low after a warning.</summary>
    public int GameOver { get; init; }
}

/// <summary>The month rolled over and the report is waiting for its acknowledgement.</summary>
public sealed record MonthlyReportReady(CampaignMonthlyReport Report) : ICampaignEvent;

/// <summary>
/// MonthlyReportState::btnOkClick. For a failed campaign this is the second OK, after the failure screen; it plays
/// <see cref="Cutscene"/>, which ends the campaign when the video is <c>loseGame</c>.
/// </summary>
public sealed record AcknowledgeMonthlyReport : ICampaignCommand;

public sealed record MonthlyReportAcknowledged(string? Cutscene) : ICampaignEvent;

/// <summary>One commendation a soldier was given or raised this month, at its new level.</summary>
public sealed record CommendationAward(int SoldierId, string Commendation, string Noun, int DecorationLevel);

/// <summary>
/// CommendationState: the soldiers medalled for this month's service, with what each was given. The windows the
/// acknowledgement opens stack in the order of its events, so the last one (a save) is on top and shows first.
/// </summary>
public sealed record SoldiersCommended(IReadOnlyList<CommendationAward> Awards) : ICampaignEvent;

/// <summary>PsiTrainingState: a base has a psi lab and soldiers train only at the month boundary.</summary>
public sealed record PsiTrainingReminder(IReadOnlyList<int> BaseIds) : ICampaignEvent;

public enum CampaignSaveKind { Autosave, Ironman }

/// <summary>SaveGameState after the monthly report: an ironman save, or an autosave when the option is on.</summary>
public sealed record CampaignSaveRequested(CampaignSaveKind Kind) : ICampaignEvent;

public interface ICampaignMonthlyReportQuery
{
    /// <summary>The report waiting for its acknowledgement, or null.</summary>
    CampaignMonthlyReport? QueryMonthlyReport();
}

public sealed partial class CampaignState : ICampaignMonthlyReportQuery
{
    internal const string MonthlyReportOpen = "The monthly report is open.";
    private const string NewMonthCountryParser = "newMonthCountry";

    /// <summary>The report the month rollover opened. It is modal and not saved (D2).</summary>
    private CampaignMonthlyReport? _monthlyReport;

    /// <summary>The tick that opened the report: a popup pauses after the tick, so that tick's later levels still run.</summary>
    private CampaignTime _monthlyReportTick;

    private readonly ScriptExecutionFrame _countryScriptFrame = new();
    private readonly ScriptRuntimeValue[] _countryScriptInputs = new ScriptRuntimeValue[5];
    private readonly ScriptRuntimeValue[] _countryScriptOutputs = new ScriptRuntimeValue[4];

    private void RegisterMonthlyCycle(CampaignCapabilityRegistry registry)
    {
        registry.Query<ICampaignMonthlyReportQuery>(this);
        registry.Query<ICampaignFundingQuery>(this);
        // D2: the report's OK has gameplay effects that a reload would lose, so the campaign cannot be saved under it.
        registry.SaveGate(() => _monthlyReport is not null ? MonthlyReportOpen : null);
        registry.Command<AcknowledgeMonthlyReport>(_ => AcknowledgeReport());
        // A modal popup: nothing but its OK reaches the campaign while it is open. Time refuses in its preflight, as
        // for the other windows, so it still reports that no tick ran.
        registry.CommandGate(command => _monthlyReport is not null && command is not (AcknowledgeMonthlyReport or AdvanceCampaignTime)
            ? new CampaignCommandResult([new CampaignActionBlocked(MonthlyReportOpen)]) : null);
        registry.Preflight(CampaignPreflightOrder.MonthlyReport, "monthly report", (next, _) =>
            _monthlyReport is not null && next != _monthlyReportTick ? MonthlyReportOpen : null);
        // ADR 0035: time1Month changes much of what the rest of the tick reads, so the rest is preflighted again.
        registry.StagePoint(CampaignTimeTrigger.OneMonth, "month rollover",
            static (_, highest) => highest >= CampaignTimeTrigger.OneMonth);
        registry.Transient("monthly report", this, static (source, shadow) =>
            (shadow._monthlyReport, shadow._monthlyReportTick) = (source._monthlyReport, source._monthlyReportTick));
        registry.Timed(CampaignTimeTrigger.OneMonth, CampaignTimeOrder.MonthPsiTraining, "monthly psi training", TrainPsiMonthly);
        registry.Timed(CampaignTimeTrigger.OneMonth, CampaignTimeOrder.MonthFunding, "monthly funding", _ =>
            MonthlyCycle.MonthlyFunding(Ledger, MonthlyCycle.CountryFunding(MonthlyCountries()), BaseMaintenance()));
        registry.Timed(CampaignTimeTrigger.OneMonth, CampaignTimeOrder.MonthReport, "monthly report", OpenMonthlyReport);
    }

    CampaignMonthlyReport? ICampaignMonthlyReportQuery.QueryMonthlyReport() => Read(() => _monthlyReport);

    private MonthlyLedger Ledger => new(_funds, _maintenance, _incomes, _expenditures, _researchScores);

    /// <summary>GeoscapeState::time1Month: psi training without <c>anytimePsiTraining</c>, at bases with a psi lab.</summary>
    private void TrainPsiMonthly(TimeEffects _)
    {
        if (Options.AnytimePsiTraining) return;
        foreach (var owner in _bases)
        {
            if (AvailablePsi(owner) <= 0) continue;
            for (var index = 0; index < owner.Soldiers.Count; index++)
            {
                var soldier = owner.Soldiers[index];
                if (soldier.Personal is not { PsiTraining: true } personal) continue;
                // Soldier::calcStatString follows; stat strings are presentation (Phase 8).
                owner.Soldiers[index] = soldier with
                {
                    Personal = SoldierReadiness.TrainPsiMonthly(personal, _content.RuntimeRules.Soldiers[soldier.Rule].Value,
                        Options.AllowPsiStrengthImprovement, _random),
                };
            }
        }
    }

    /// <summary>SavedGame::getBaseMaintenance.</summary>
    private int BaseMaintenance()
    {
        var total = 0;
        foreach (var owner in _bases) total = checked(total + checked((int)CalculateMaintenance(owner).Total));
        return total;
    }

    private MonthlyCountry[] MonthlyCountries()
    {
        var rules = _content.RuntimeRules.Countries;
        return [.. _countries.Select(country => new MonthlyCountry(rules.GetExternalId(country.Rule), rules[country.Rule].Value.FundingCap,
            country.Funding, country.ActivityXcom, country.ActivityAlien, country.Pact, country.NewPact, country.CancelPact))];
    }

    /// <summary>
    /// The MonthlyReportState constructor: the report's decisions run inside the tick, then the popup waits. The game
    /// over decision and its cutscene wait for the acknowledgement.
    /// </summary>
    private void OpenMonthlyReport(TimeEffects effects)
    {
        var countries = MonthlyCountries();
        var regions = _regions.Select(static region => new MonthlyRegion(region.ActivityXcom, region.ActivityAlien)).ToArray();
        var campaign = _content.RuntimeRules.Campaign;
        var warned = Warned;
        _monthlyCountries = countries;
        MonthlyEvaluation evaluation;
        try
        {
            evaluation = MonthlyCycle.Evaluate(Ledger, regions, countries, Infiltrations(), MonthsPassed,
                new MonthlyReportRules(campaign.DefeatScore, campaign.DefeatFunds, campaign.Interception.MonthlyRatingThresholds,
                    campaign.Monthly.MonthlyRatings, campaign.PerformanceBonusFactor,
                    campaign.Interception.DifficultyCoefficient((int)Difficulty), (int)Difficulty),
                _random, ref warned, RunNewMonthCountry);
        }
        finally { _monthlyCountries = null; }
        Warned = warned;
        SyncMonthlyPacts(countries);
        // Modders' handles on the political situation: the pact events of the countries in the lists.
        var events = _handlers.Operation<ICampaignEventScheduling>();
        try
        {
            foreach (var id in evaluation.Pacts)
                if (CountryRule(id).SignedPactEvent is not null) events.Schedule(CountryRule(id).SignedPactEventId);
            foreach (var id in evaluation.CancelledPacts)
                if (CountryRule(id).RejoinedXcomEvent is not null) events.Schedule(CountryRule(id).RejoinedXcomEventId);
        }
        catch (InvalidOperationException error)
        {
            // The event history is full or a timer is out of range: refuse the month (staged ticks report it).
            throw new InvalidDataException(error.Message, error);
        }
        // The report names the month that just ended.
        var month = Time.Month - 1;
        var year = Time.Year;
        if (month == 0) (month, year) = (12, year - 1);
        _monthlyReport = new CampaignMonthlyReport(month, year, evaluation.RatingTotal, evaluation.RatingText,
            MonthlyCycle.CountryFunding(countries), evaluation.FundingDiff, BaseMaintenance(), evaluation.PerformanceBonus, _funds[^1],
            evaluation.CouncilText, evaluation.DebtWarning, evaluation.Happy, evaluation.Sad, evaluation.Pacts, evaluation.CancelledPacts)
        { GameOver = evaluation.GameOver };
        _monthlyReportTick = Time;
        effects.Notify(new MonthlyReportReady(_monthlyReport));
    }

    private RuntimeCountryRule CountryRule(string id) =>
        _content.RuntimeRules.Countries[_content.RuntimeRules.Countries.GetRequired(id)].Value;

    /// <summary>The infiltration missions in std::map name order (UTF-8 bytes), as Mod::getRandomMission walks them.</summary>
    private MonthlyInfiltration[] Infiltrations() =>
        [.. _content.RuntimeRules.AlienMissions.Rules
            .Where(static rule => rule.Value.Objective == RuntimeMissionObjective.Infiltration)
            .OrderBy(static rule => Encoding.UTF8.GetBytes(rule.Id), Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right)))
            .Select(static rule => new MonthlyInfiltration(rule.Value.Points, rule.Value.MissionWeights))];

    private CampaignCommandResult AcknowledgeReport()
    {
        if (_monthlyReport is not { } report) return Blocked("No monthly report is open.");
        _monthlyReport = null;
        var ironman = Identity.Ironman;
        if (report.GameOver != 0)
        {
            var monthly = _content.RuntimeRules.Campaign.Monthly;
            var cutscene = report.GameOver == 1 ? monthly.LoseRatingCutscene : monthly.LoseMoneyCutscene;
            List<ICampaignEvent> ended = [];
            // Mod::getVideo throws for an undefined cutscene; the port plays nothing and play goes on (ADR 0036).
            if (!_content.RuntimeRules.Campaign.Cutscenes.TryGetValue(cutscene, out var video)) ended.Add(new MonthlyReportAcknowledged(null));
            else
            {
                ended.Add(new MonthlyReportAcknowledged(cutscene));
                if (video.LoseGame)
                {
                    EndCampaign(2);
                    ended.Add(new CampaignEnded(2));
                }
            }
            if (ironman) ended.Add(new CampaignSaveRequested(CampaignSaveKind.Ironman));
            return new(ended);
        }

        List<ICampaignEvent> events = [new MonthlyReportAcknowledged(null)];
        // Medals for service time: every soldier at a base, in base and soldier order.
        var inputs = CommendationInputs();
        List<CommendationAward> awards = [];
        var commendations = new List<SoldierCommendation>();
        foreach (var owner in _bases)
        {
            for (var index = 0; index < owner.Soldiers.Count; index++)
            {
                var soldier = owner.Soldiers[index];
                if (soldier.Personal is not { } personal) continue;
                var diary = personal.Diary;
                personal = personal with { Diary = diary.WithCounter("monthsService", unchecked(diary.Counter("monthsService") + 1)) };
                commendations.Clear();
                commendations.AddRange(personal.Commendations);
                var type = _content.RuntimeRules.Soldiers.GetExternalId(soldier.Rule);
                if (SoldierCommendations.Manage(type, personal, commendations, inputs))
                {
                    awards.AddRange(commendations.Where(commendation => !personal.Commendations.Contains(commendation))
                        .Select(commendation => new CommendationAward(soldier.Id, commendation.RuleId, commendation.Noun, commendation.DecorationLevel)));
                    personal = personal with { Commendations = commendations.ToArray().AsReadOnly() };
                }
                owner.Soldiers[index] = soldier with { Personal = personal };
            }
        }
        if (awards.Count != 0) events.Add(new SoldiersCommended(awards));
        // Base::getAvailablePsiLabs as a truth value: any non-zero capacity.
        if (!Options.AnytimePsiTraining && _bases.Where(owner => AvailablePsi(owner) != 0).Select(static owner => owner.Id).ToArray() is { Length: > 0 } psiBases)
            events.Add(new PsiTrainingReminder(psiBases));
        if (ironman) events.Add(new CampaignSaveRequested(CampaignSaveKind.Ironman));
        else if (Options.Autosave) events.Add(new CampaignSaveRequested(CampaignSaveKind.Autosave));
        return new(events);
    }

    private CommendationInputs? _commendationInputs;

    private CommendationInputs CommendationInputs()
    {
        var rules = _content.RuntimeRules;
        var ordered = _commendationInputs?.Commendations ?? [.. rules.Commendations.Rules
            .OrderBy(static rule => Encoding.UTF8.GetBytes(rule.Id), Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right)))
            .Select(static rule => (rule.Id, rule.Value))];
        return _commendationInputs = new(ordered, research => research.All(_completedResearch.Contains), MissionStatistics,
            id => rules.Items.TryGet(id, out var handle) && rules.Items[handle].Value is var item
                ? new CommendationItem(item.BattleType, item.Damage.ResistType, item.MeleeDamage.ResistType) : null,
            rules.Campaign.MaximumDarknessToSeeUnits);
    }

    // The countries of the report being evaluated: Country::newMonth sets its pact at once, so a later country's
    // script reads the earlier countries' new pacts.
    private MonthlyCountry[]? _monthlyCountries;

    private void SyncMonthlyPacts(MonthlyCountry[] countries)
    {
        for (var index = 0; index < countries.Length; index++)
        {
            _countries[index].Pact = countries[index].Pact;
            _countries[index].NewPact = countries[index].NewPact;
            _countries[index].CancelPact = countries[index].CancelPact;
        }
    }

    /// <summary>
    /// Country::newMonth's <c>newMonthCountry</c> call: the global event scripts around the country's own script. The
    /// country, the game and the rules are read-only to it. A script the port cannot run fails the month, which a
    /// staged tick reports as a refusal before anything changes.
    /// </summary>
    private CountryMonthOutputs RunNewMonthCountry(MonthlyCountry country, int xcomTotal, int alienTotal, CountryMonthOutputs current)
    {
        if (_monthlyCountries is { } evaluating) SyncMonthlyPacts(evaluating);
        var rules = _content.RuntimeRules;
        var countryRule = rules.Countries[rules.Countries.GetRequired(country.Type)].Value;
        var artifact = countryRule.Scripts.Select(handle => rules.Scripts[handle].Value.Artifact)
            .FirstOrDefault(static candidate => candidate.ParserName == NewMonthCountryParser);
        var plan = _content.EventPlans.FirstOrDefault(static candidate => candidate.ParserName == NewMonthCountryParser)?.Plan;
        if (artifact is null && (plan is null || plan.Count == 0)) return current;
        ScriptProgram[] programs = [.. (plan?.Before.Select(static e => e.Program) ?? []),
            .. (artifact is null ? [] : new[] { artifact.Program }), .. (plan?.After.Select(static e => e.Program) ?? [])];
        if (programs.Length > ScriptLimits.DefaultMaximumEventExecutions)
            throw new InvalidDataException("Country scripts exceed the supported event execution limit.");
        var invocation = BuildPriceInvocation(programs, plan?.Count ?? 0, plan is not null);
        foreach (var check in invocation.Checks)
            foreach (var binding in check.Bindings)
                if (!binding.Supported) throw new InvalidDataException($"Country script requires unsupported binding {binding.Name}.");
        var state = _countries[_countries.FindIndex(candidate => rules.Countries.GetExternalId(candidate.Rule) == country.Type)];
        _countryScriptInputs[0] = ScriptRuntimeValue.FromReference(state);
        _countryScriptInputs[1] = ScriptRuntimeValue.FromReference(this);
        _countryScriptInputs[2] = ScriptRuntimeValue.FromReference(rules);
        _countryScriptInputs[3] = ScriptRuntimeValue.FromScalar(alienTotal);
        _countryScriptInputs[4] = ScriptRuntimeValue.FromScalar(xcomTotal);
        _countryScriptOutputs[0] = ScriptRuntimeValue.FromScalar(current.FundingChange);
        _countryScriptOutputs[1] = ScriptRuntimeValue.FromScalar(current.Satisfaction);
        _countryScriptOutputs[2] = ScriptRuntimeValue.FromScalar(current.NewPact ? 1 : 0);
        _countryScriptOutputs[3] = ScriptRuntimeValue.FromScalar(current.CancelPact ? 1 : 0);
        var outer = _priceCall;
        var forecast = new PriceForecastContext(Time, DaysPassed, false, 0);
        _priceCall = new(forecast, Time);
        try
        {
            foreach (var program in invocation.Programs)
            {
                var remaining = 1_000_000 - forecast.Work;
                if (remaining <= 0) throw new InvalidDataException("Country scripts exceed the supported work limit.");
                _countryScriptFrame.Prepare(program);
                var options = new ScriptExecutionOptions { MaximumSteps = (int)Math.Min(remaining, ScriptExecutionOptions.Default.MaximumSteps) };
                var result = ScriptVm.ExecuteWithInputs(program, _countryScriptOutputs, _countryScriptInputs, _countryScriptOutputs,
                    _countryScriptFrame, options, invocation.Providers);
                forecast.Work += result.Steps;
                if (!result.Succeeded)
                    throw new InvalidDataException($"Country script failed: {result.FailureMessage ?? result.DiagnosticCode}.");
            }
            return new(_countryScriptOutputs[0].Scalar, _countryScriptOutputs[1].Scalar, _countryScriptOutputs[2].Scalar != 0,
                _countryScriptOutputs[3].Scalar != 0);
        }
        finally
        {
            _priceCall = outer;
            Array.Clear(_countryScriptInputs);
            Array.Clear(_countryScriptOutputs);
        }
    }
}
