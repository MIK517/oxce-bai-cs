namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Suppress future detected-contact alerts for this current UFO contact.</summary>
public sealed record IgnoreUfoContact(int UfoUniqueId, int ContactId) : ICampaignCommand;

public sealed record UfoContactIgnored(int UfoUniqueId, int ContactId) : ICampaignEvent;

internal sealed partial class CampaignWorld
{
    // SavedGame::_ignoredUfos is deliberately transient; reload clears it in the reference.
    private readonly HashSet<int> _ignoredUfoContacts = [];

    private CampaignCommandResult IgnoreContact(IgnoreUfoContact command)
    {
        foreach (var ufo in _ufos)
        {
            if (ufo.UniqueId != command.UfoUniqueId || command.UfoUniqueId <= 0) continue;
            if (ufo.Id != command.ContactId || command.ContactId <= 0 || !ufo.Detected ||
                ufo.Status is not (UfoStatus.Flying or UfoStatus.Landed))
                return Blocked(WorldCommandReasons.ContactGone);
            if (!_ignoredUfoContacts.Contains(ufo.Id) && _ignoredUfoContacts.Count >= 100_000)
                return Blocked(WorldCommandReasons.ContactLimit);
            _ignoredUfoContacts.Add(ufo.Id);
            return new([new UfoContactIgnored(ufo.UniqueId, ufo.Id)]);
        }
        return Blocked(WorldCommandReasons.ContactGone);
    }
}
