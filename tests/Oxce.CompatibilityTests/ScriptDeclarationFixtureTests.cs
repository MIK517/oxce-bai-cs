using Oxce.FixtureSupport;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Runtime;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class ScriptDeclarationFixtureTests
{
    [Theory]
    [InlineData("detectUfoFromBase")]
    [InlineData("detectUfoFromCraft")]
    public void InitializerCompatibilityMatchesNativeOverloads(string parser)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("script-declaration");
        var actual = new List<object>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var source = row.GetProperty("source").GetString()!;
            var compiled = ScriptCompiler.Compile(source, ScriptParserDefinition.FromCatalog(parser));
            Assert.Equal(row.GetProperty("compiled").GetBoolean(), compiled.Succeeded);
            var detectionType = 0;
            var detectionChance = 0;
            if (compiled.Succeeded)
            {
                var providers = new ScriptHostBindingsBuilder();
                foreach (var binding in compiled.Program!.Bindings)
                {
                    Assert.Equal("set", binding.Name);
                    providers.Add(binding.Id, static (_, arguments) =>
                    {
                        arguments[0] = arguments[1];
                        return ScriptBindingResult.Success;
                    });
                }
                var executed = ScriptVm.Execute(compiled.Program!, hostBindings: providers.Build());
                Assert.Equal(ScriptExecutionStatus.Completed, executed.Status);
                detectionType = executed.Outputs["detection_type"];
                detectionChance = executed.Outputs["detection_chance"];
            }
            else
            {
                Assert.Null(compiled.Program);
                Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code ==
                    Oxce.Scripting.Diagnostics.ScriptDiagnosticCodes.InvalidDeclaration);
            }
            actual.Add(new
            {
                name = row.GetProperty("name").GetString(),
                source,
                compiled = compiled.Succeeded,
                detectionType,
                detectionChance,
            });
        }
        Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(oracle.RootElement.GetRawText())),
            CanonicalJson.Normalize(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { cases = actual })));
    }
}
