using Oxce.Scripting.Binding;
using Oxce.Scripting.Types;
using Xunit;

namespace Oxce.UnitTests.Scripting;

public sealed class ScriptTypeAndOverloadTests
{
    private static readonly ScriptTypeRef Scalar = new(ScriptPrimitiveTypes.Scalar);
    private static readonly ScriptTypeRef ReadRegister = new(
        ScriptPrimitiveTypes.Scalar,
        ScriptTypeModifier.Register);
    private static readonly ScriptTypeRef WritableRegister = new(
        ScriptPrimitiveTypes.Scalar,
        ScriptTypeModifier.Register | ScriptTypeModifier.Writable);

    [Fact]
    public void CompatibilityScoringMatchesReferenceSpecializationPenalties()
    {
        Assert.Equal(255, ScriptOverloadResolver.CompatibilityScore(Scalar, Scalar, 0));
        Assert.Equal(255, ScriptOverloadResolver.CompatibilityScore(WritableRegister, WritableRegister, 0));
        Assert.Equal(191, ScriptOverloadResolver.CompatibilityScore(ReadRegister, WritableRegister, 0));
        Assert.Equal(0, ScriptOverloadResolver.CompatibilityScore(WritableRegister, ReadRegister, 0));
        Assert.Equal(0, ScriptOverloadResolver.CompatibilityScore(Scalar, ReadRegister, 0));
        Assert.Equal(247, ScriptOverloadResolver.CompatibilityScore(Scalar, Scalar, 20));
    }

    [Fact]
    public void SelectsHighestScoringOverload()
    {
        var read = Overload(1, ReadRegister);
        var writable = Overload(2, WritableRegister);

        var result = ScriptOverloadResolver.Resolve([read, writable], [(ScriptArgumentType)WritableRegister]);

        Assert.Equal(ScriptOverloadResolutionKind.Selected, result.Kind);
        Assert.Same(writable, result.Selected);
        Assert.Equal(255, result.Score);
    }

    [Fact]
    public void ReportsAmbiguousAndMissingOverloads()
    {
        var first = Overload(1, Scalar);
        var second = Overload(2, Scalar);

        Assert.Equal(
            ScriptOverloadResolutionKind.Ambiguous,
            ScriptOverloadResolver.Resolve([first, second], [(ScriptArgumentType)Scalar]).Kind);
        Assert.Equal(
            ScriptOverloadResolutionKind.NoMatch,
            ScriptOverloadResolver.Resolve([first], [(ScriptArgumentType)ReadRegister]).Kind);
    }

    [Fact]
    public void UnknownArgumentsParticipateInReferenceOverloadInference()
    {
        var first = Overload(1, Scalar);
        var second = Overload(2, ReadRegister);

        Assert.Equal(
            ScriptOverloadResolutionKind.Selected,
            ScriptOverloadResolver.Resolve([first], [ScriptArgumentType.UnknownSimple]).Kind);
        Assert.Equal(
            ScriptOverloadResolutionKind.Ambiguous,
            ScriptOverloadResolver.Resolve([first, second], [ScriptArgumentType.UnknownSegment]).Kind);
    }

    [Fact]
    public void EditableReferenceCanFlowToReadOnlyReferenceWithPenalty()
    {
        var type = new ScriptTypeId(ScriptPrimitiveTypes.FirstCustomTypeValue);
        var read = new ScriptTypeRef(type, ScriptTypeModifier.Reference);
        var editable = new ScriptTypeRef(
            type,
            ScriptTypeModifier.Reference | ScriptTypeModifier.EditableReference);

        Assert.Equal(127, ScriptOverloadResolver.CompatibilityScore(read, editable, 0));
        Assert.Equal(0, ScriptOverloadResolver.CompatibilityScore(editable, read, 0));
    }

    [Fact]
    public void DeclarationCatalogUsesStableOrdinalNamesAndIds()
    {
        var builder = new ScriptDeclarationCatalogBuilder();
        builder.AddType(new ScriptTypeDefinition(ScriptPrimitiveTypes.Scalar, "int", 4, 4));
        builder.AddOperation(Overload(1, Scalar));
        builder.AddOperation(new ScriptOperationOverload(
            new ScriptOperationId(2),
            "probe",
            [[ReadRegister]]));

        var catalog = builder.Build();

        Assert.True(catalog.TryGetType("int", out var byName));
        Assert.True(catalog.TryGetType(ScriptPrimitiveTypes.Scalar, out var byId));
        Assert.Same(byName, byId);
        Assert.Equal(2, catalog.GetOperations("probe").Count);
        Assert.Empty(catalog.GetOperations("Probe"));
    }

    [Fact]
    public void DeclarationCatalogRejectsDuplicateStableIds()
    {
        var builder = new ScriptDeclarationCatalogBuilder();
        builder.AddOperation(Overload(1, Scalar));

        Assert.Throws<ArgumentException>(() => builder.AddOperation(Overload(1, ReadRegister)));
    }

    private static ScriptOperationOverload Overload(int id, ScriptTypeRef argument) =>
        new(new ScriptOperationId(id), "probe", [[argument]]);
}
