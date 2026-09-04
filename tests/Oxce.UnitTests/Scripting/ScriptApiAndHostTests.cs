using Oxce.Scripting.Api;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Runtime;
using Oxce.Scripting.Types;
using Xunit;

namespace Oxce.UnitTests.Scripting;

public sealed class ScriptApiAndHostTests
{
    [Fact]
    public void DeclaredBindingCompilesWithoutProviderAndFailsAsMissingCapability()
    {
        var compiled = Compile("set result GLOBAL_VALUE; adjust result 5; return result;");
        Assert.True(compiled.Succeeded);

        var result = ScriptVm.Execute(compiled.Program!);

        Assert.Equal(ScriptExecutionStatus.MissingCapability, result.Status);
        Assert.Equal(7, result.Outputs["result"]);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.MissingBindingProvider);
    }

    [Fact]
    public void InstalledProviderUpdatesWritableArguments()
    {
        var compiled = Compile("set result GLOBAL_VALUE; adjust result 5; return result;");
        var providers = new ScriptHostBindingsBuilder();
        providers.Add(AdjustId, static arguments =>
        {
            arguments[0] += arguments[1];
            return ScriptBindingResult.Success;
        });

        var result = ScriptVm.Execute(compiled.Program!, hostBindings: providers.Build());

        Assert.Equal(ScriptExecutionStatus.Completed, result.Status);
        Assert.Equal(12, result.Outputs["result"]);
    }

    [Fact]
    public void ProviderFailureIsDistinctFromMissingProvider()
    {
        var compiled = Compile("adjust result 5; return result;");
        var providers = new ScriptHostBindingsBuilder();
        providers.Add(AdjustId, static _ => ScriptBindingResult.Failure("probe rejected the value"));

        var result = ScriptVm.Execute(compiled.Program!, hostBindings: providers.Build());

        Assert.Equal(ScriptExecutionStatus.RuntimeError, result.Status);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.BindingOperationFailed &&
                diagnostic.Message.Contains("probe rejected", StringComparison.Ordinal));
    }

    [Fact]
    public void ParserMembershipHidesBindingsOutsideTheirGroup()
    {
        var definition = new ScriptParserDefinition("Other", ["result"], Catalog, ["Other"]);

        var compiled = ScriptCompiler.Compile("adjust result 1; return result;", definition);

        Assert.False(compiled.Succeeded);
        Assert.Contains(compiled.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.UnknownOperation);
    }

    [Fact]
    public void ParserGroupIndexesReuseCandidatesAndPreserveDeclarationOrder()
    {
        var first = new ScriptBindingDeclaration(
            new ScriptBindingId(20_001), "probe", [], ["First"], Reference);
        var shared = new ScriptBindingDeclaration(
            new ScriptBindingId(20_002), "probe", [], ["First", "Second"], Reference);
        var second = new ScriptBindingDeclaration(
            new ScriptBindingId(20_003), "probe", [], ["Second"], Reference);
        var catalog = new ScriptApiCatalog(
            [first, shared, second],
            [
                new ScriptConstantDeclaration("FIRST", 1, ["First"], Reference),
                new ScriptConstantDeclaration("SHARED", 2, ["First", "Second"], Reference),
                new ScriptConstantDeclaration("SECOND", 3, ["Second"], Reference),
            ]);
        IReadOnlySet<string> firstGroup = new HashSet<string>(["First"], StringComparer.Ordinal);
        IReadOnlySet<string> secondGroup = new HashSet<string>(["Second"], StringComparer.Ordinal);
        IReadOnlySet<string> bothGroups = new HashSet<string>(["First", "Second"], StringComparer.Ordinal);

        var firstLookup = catalog.GetBindings("probe", firstGroup);

        Assert.Same(firstLookup, catalog.GetBindings("probe", firstGroup));
        Assert.Equal([first, shared], firstLookup);
        Assert.Equal([shared, second], catalog.GetBindings("probe", secondGroup));
        Assert.Equal([first, shared, second], catalog.GetBindings("probe", bothGroups));
    }

    [Fact]
    public void EqualBestBindingsRemainAmbiguousAfterSinglePassSelection()
    {
        var catalog = new ScriptApiCatalog(
            [
                new ScriptBindingDeclaration(
                    new ScriptBindingId(21_001), "adjust",
                    [new ScriptBindingParameter("target", WritableInt, true),
                     new ScriptBindingParameter("delta", Int, false)],
                    ["Probe"], Reference),
                new ScriptBindingDeclaration(
                    new ScriptBindingId(21_002), "adjust",
                    [new ScriptBindingParameter("target", WritableInt, true),
                     new ScriptBindingParameter("delta", Int, false)],
                    ["Probe"], Reference),
            ]);

        var compiled = ScriptCompiler.Compile(
            "adjust result 1; return result;",
            new ScriptParserDefinition("Probe", ["result"], catalog, ["Probe"]));

        Assert.False(compiled.Succeeded);
        Assert.Contains(compiled.Diagnostics,
            static diagnostic => diagnostic.Code == ScriptDiagnosticCodes.AmbiguousOverload);
    }

    [Fact]
    public void ConstantScopeReusesSharedCatalogIndexes()
    {
        var scope = Catalog.CreateScope(
            [new ScriptConstantDeclaration("FILE_VALUE", 11, ["Probe"], Reference)]);

        Assert.True(scope.IsScope);
        Assert.Same(Catalog.Bindings, scope.Bindings);
        Assert.Same(Catalog.Parsers, scope.Parsers);
        Assert.Same(Catalog.Types, scope.Types);
        Assert.Single(Catalog.Constants);
        Assert.Equal(2, scope.Constants.Count);
        var compiled = ScriptCompiler.Compile(
            "set result FILE_VALUE; return result;",
            new ScriptParserDefinition("Probe", ["result"], scope, ["Probe"]));
        Assert.True(compiled.Succeeded);
        Assert.Equal(11, ScriptVm.Execute(compiled.Program!).Outputs["result"]);
    }

    [Fact]
    public void WritableBindingArgumentsRequireRegisters()
    {
        var compiled = Compile("adjust 1 2; return result;");

        Assert.False(compiled.Succeeded);
        Assert.Contains(compiled.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.NoMatchingOverload);
    }

    [Fact]
    public void ArgumentSeparatorParticipatesInOverloadMatchingWithoutRuntimeStorage()
    {
        var separated = new ScriptApiCatalog(
            [
                new ScriptBindingDeclaration(
                    new ScriptBindingId(10_002),
                    "separated",
                    [
                        new ScriptBindingParameter("target", WritableInt, true),
                        new ScriptBindingParameter("separator", new ScriptTypeRef(ScriptPrimitiveTypes.Separator), false),
                        new ScriptBindingParameter("value", Int, false),
                    ],
                    ["Probe"],
                    Reference),
            ]);
        var definition = new ScriptParserDefinition("Probe", ["result"], separated, ["Probe"]);

        var compiled = ScriptCompiler.Compile("separated result __ 1; return result;", definition);

        Assert.True(compiled.Succeeded);
        Assert.Equal(10_002, Assert.Single(compiled.Program!.Bindings).Id.Value);
    }

    [Fact]
    public void ReferenceTypeDeclarationsAndNullInitializersCompile()
    {
        var definition = ScriptParserDefinition.FromCatalog(
            "newTurnItem", ReferenceScriptApiCatalog.Instance);

        var compiled = ScriptCompiler.Compile(
            "var ptr RuleItem optional_rule null; var Position position; return;", definition);

        Assert.True(compiled.Succeeded, string.Join(Environment.NewLine,
            compiled.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Contains(compiled.Program!.Registers,
            register => register.Name == "optional_rule" && register.Type.IsReference);
        Assert.Contains(compiled.Program.Registers,
            register => register.Name == "position" && !register.Type.IsReference);
    }

    [Fact]
    public void EditablePointerOverloadWinsOverReadonlyCompatibleOverload()
    {
        var definition = ScriptParserDefinition.FromCatalog(
            "newTurnItem", ReferenceScriptApiCatalog.Instance);

        var compiled = ScriptCompiler.Compile(
            "var ptre BattleUnit unit; item.getOwner unit; return;", definition);

        Assert.True(compiled.Succeeded, Messages(compiled));
        Assert.Equal(10067, Assert.Single(compiled.Program!.Bindings).Id.Value);
    }

    [Fact]
    public void CustomTypeBindingsCanOverrideScalarCoreOperationNames()
    {
        var definition = ScriptParserDefinition.FromCatalog(
            "newTurnItem", ReferenceScriptApiCatalog.Instance);

        var compiled = ScriptCompiler.Compile(
            "var Position position; set position 1 2 3; mul position 2; sub position position; return;",
            definition);

        Assert.True(compiled.Succeeded, Messages(compiled));
        Assert.Equal(["mul", "set", "sub"],
            compiled.Program!.Bindings.Select(static binding => binding.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CatalogListLoopLowersToHiddenInitAndTypedStepCalls()
    {
        var definition = ScriptParserDefinition.FromCatalog(
            "newTurnUnit", ReferenceScriptApiCatalog.Instance);

        var compiled = ScriptCompiler.Compile(
            "loop var inventory_item battle_game.getItems.list; end; return;", definition);

        Assert.True(compiled.Succeeded, Messages(compiled));
        Assert.Contains(compiled.Program!.Bindings,
            binding => binding.Name == "BattleGame.getItems.init");
        Assert.Contains(compiled.Program.Bindings,
            binding => binding.Name == "BattleGame.getItems.list");
        Assert.Contains(compiled.Program.Registers,
            register => register.Name == "inventory_item" && register.Type.IsEditableReference);
    }

    [Fact]
    public void HiddenCustomValueCopyCompilesAsTypedHostCall()
    {
        var definition = ScriptParserDefinition.FromCatalog(
            "vaporParticleAmmo", ReferenceScriptApiCatalog.Instance);

        var compiled = ScriptCompiler.Compile(
            "set subvoxel_velocity subvoxel_trajectory_forward; return;", definition);

        Assert.True(compiled.Succeeded, Messages(compiled));
        var binding = Assert.Single(compiled.Program!.Bindings);
        Assert.Equal("set", binding.Name);
        Assert.Equal(binding.Parameters[0].Type.Id, binding.Parameters[1].Type.Id);
    }

    private static string Messages(ScriptCompileResult compiled) => string.Join(
        Environment.NewLine,
        compiled.Diagnostics.Select(static diagnostic => diagnostic.Message));

    private static readonly ScriptBindingId AdjustId = new(10_001);
    private static readonly ScriptTypeRef WritableInt = new(
        ScriptPrimitiveTypes.Scalar,
        ScriptTypeModifier.Register | ScriptTypeModifier.Writable);
    private static readonly ScriptTypeRef Int = new(ScriptPrimitiveTypes.Scalar);
    private static readonly ScriptReferenceLocation Reference = new("src/Probe.cpp", 10);
    private static readonly ScriptApiCatalog Catalog = new(
        [
            new ScriptBindingDeclaration(
                AdjustId,
                "adjust",
                [new ScriptBindingParameter("target", WritableInt, true), new ScriptBindingParameter("delta", Int, false)],
                ["Probe"],
                Reference),
        ],
        [new ScriptConstantDeclaration("GLOBAL_VALUE", 7, ["Probe"], Reference)],
        [new ScriptParserDeclaration("ProbeParser", "Probe", ["result"], true, Reference)]);

    private static ScriptCompileResult Compile(string source) => ScriptCompiler.Compile(
        source,
        new ScriptParserDefinition("Probe", ["result"], Catalog, ["Probe"]));
}
