namespace Oxce.Mods;

public static class ModDiagnosticCodes
{
    public const string MissingMetadata = "OXCE-MOD-0001";
    public const string InvalidMetadata = "OXCE-MOD-0002";
    public const string DuplicateId = "OXCE-MOD-0003";
    public const string MissingMaster = "OXCE-MOD-0004";
    public const string DependencyCycle = "OXCE-MOD-0005";
    public const string DependentRemoved = "OXCE-MOD-0006";
    public const string InactiveForMaster = "OXCE-MOD-0007";
    public const string RequiredMasterVersion = "OXCE-MOD-0008";
    public const string InvalidVersion = "OXCE-MOD-0009";
    public const string RequiredVersionWithoutMaster = "OXCE-MOD-0010";
    public const string MissingActivation = "OXCE-MOD-0011";
    public const string InvalidArchive = "OXCE-MOD-0012";
    public const string UnsafeArchiveEntry = "OXCE-MOD-0013";
    public const string MissingExternalResource = "OXCE-MOD-0014";
    public const string RequiredExtendedEngine = "OXCE-MOD-0015";
    public const string MultipleActiveMasters = "OXCE-MOD-0016";
    public const string NoAvailableMaster = "OXCE-MOD-0017";
    public const string DuplicateNewRule = "OXCE-MOD-0018";
    public const string MissingOverrideRule = "OXCE-MOD-0019";
    public const string MissingUpdateRule = "OXCE-MOD-0020";
    public const string UnconsumedRuleProperty = "OXCE-MOD-0021";
    public const string DeferredRuleProperty = "OXCE-MOD-0022";
    public const string MissingDeclaredResource = "OXCE-MOD-0023";
    public const string MissingRuleReference = "OXCE-MOD-0024";
    public const string InvalidRuleRelationship = "OXCE-MOD-0025";
    public const string DeferredRuleReference = "OXCE-MOD-0026";
    public const string InvalidScriptContent = "OXCE-MOD-0027";
    public const string InvalidResourceDescriptor = "OXCE-MOD-0028";
    public const string ResourceLimitExceeded = "OXCE-MOD-0029";
    public const string InvalidRuntimeRuleLink = "OXCE-MOD-0030";
}
