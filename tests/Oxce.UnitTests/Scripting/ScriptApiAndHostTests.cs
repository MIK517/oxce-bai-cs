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
