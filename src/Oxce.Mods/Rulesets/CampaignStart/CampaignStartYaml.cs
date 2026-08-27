using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.CampaignStart;

internal static class CampaignStartYaml
{
    public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    public static GeographicArea ReadArea(YamlNode node)
    {
        if (node is not YamlSequenceNode sequence || sequence.Items.Count < 4)
        {
            throw new YamlFormatException("Geographic areas must contain at least four values.", node.Span);
        }
        var longitudeMinimum = YamlValueReader.ReadDouble(sequence.Items[0]);
        var longitudeMaximum = YamlValueReader.ReadDouble(sequence.Items[1]);
        var latitudeMinimum = DegreesToRadians(YamlValueReader.ReadDouble(sequence.Items[2]));
        var latitudeMaximum = DegreesToRadians(YamlValueReader.ReadDouble(sequence.Items[3]));
        if (latitudeMinimum > latitudeMaximum) (latitudeMinimum, latitudeMaximum) = (latitudeMaximum, latitudeMinimum);
        return new GeographicArea(
            DegreesToRadians(longitudeMinimum), DegreesToRadians(longitudeMaximum), latitudeMinimum, latitudeMaximum);
    }

    public static MissionArea ReadMissionArea(YamlNode node)
    {
        if (node is not YamlSequenceNode sequence || sequence.Items.Count < 4)
        {
            throw new YamlFormatException("Mission areas must contain four to six values.", node.Span);
        }
        var longitudeMinimum = DegreesToRadians(YamlValueReader.ReadDouble(sequence.Items[0]));
        var longitudeMaximum = DegreesToRadians(YamlValueReader.ReadDouble(sequence.Items[1]));
        var latitudeMinimum = DegreesToRadians(YamlValueReader.ReadDouble(sequence.Items[2]));
        var latitudeMaximum = DegreesToRadians(YamlValueReader.ReadDouble(sequence.Items[3]));
        if (latitudeMinimum > latitudeMaximum) (latitudeMinimum, latitudeMaximum) = (latitudeMaximum, latitudeMinimum);
        return new MissionArea(
            longitudeMinimum,
            longitudeMaximum,
            latitudeMinimum,
            latitudeMaximum,
            sequence.Items.Count >= 5 ? YamlValueReader.ReadInt32(sequence.Items[4]) : 0,
            sequence.Items.Count >= 6 ? YamlValueReader.ReadString(sequence.Items[5]) : string.Empty);
    }

    public static FacilityPosition ReadPosition(YamlNode node)
    {
        var value = YamlValueReader.ReadTuple(
            node, YamlValueReader.ReadInt32, YamlValueReader.ReadInt32, YamlValueReader.ReadInt32);
        return new FacilityPosition(value.First, value.Second, value.Third);
    }

    public static void ApplyEditableNames(List<string> destination, YamlNode node, bool unique)
    {
        if (node is not YamlSequenceNode)
        {
            throw new YamlFormatException("Editable name collections must be sequences.", node.Span);
        }
        var values = YamlValueReader.ReadSequence(node, YamlValueReader.ReadString);
        switch (node.Tag)
        {
            case null:
            case "!!seq":
            case "!info":
                destination.Clear();
                Add(values);
                break;
            case "!add":
                Add(values);
                break;
            case "!remove":
                foreach (var value in values) destination.RemoveAll(item => item == value);
                break;
            default:
                throw new YamlFormatException($"Unsupported collection tag '{node.Tag}'.", node.Span);
        }

        void Add(IEnumerable<string> valuesToAdd)
        {
            foreach (var value in valuesToAdd)
            {
                if (!unique || !destination.Contains(value, StringComparer.Ordinal)) destination.Add(value);
            }
        }
    }
}
