using System.Runtime.CompilerServices;
using Oxce.Scripting.Api;
using Oxce.Scripting.Binding;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Events;
using Oxce.Scripting.Runtime;
using Oxce.Scripting.Types;
using Xunit;

namespace Oxce.UnitTests.Scripting;

public sealed class ScriptRuntimeFrameTests
{
    [Fact]
    public void PackedProgramDoesNotRetainInstructionObjectsOrOperandArrays()
    {
        var (program, instruction) = CreatePackedProgramProbe();

        Collect();

        Assert.False(instruction.IsAlive);
        Assert.Single(program.Instructions);
        Assert.Equal(42, Assert.Single(program.Instructions[0].Operands).Scalar);
    }

    [Fact]
    public void PreparedBindingFreeScalarExecutionAllocatesNothing()
    {
        var program = Compile("set result 1; loop var i 8; add result i; end; return result;", Definition());
        var frame = new ScriptExecutionFrame();
        frame.Prepare(program);
        Span<int> initial = stackalloc int[] { 0 };
        Span<int> output = stackalloc int[1];
        Assert.True(ScriptVm.ExecuteScalar(program, initial, output, frame).Succeeded);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100; index++)
        {
            Assert.True(ScriptVm.ExecuteScalar(program, initial, output, frame).Succeeded);
        }
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(allocatedBefore, allocatedAfter);
        Assert.Equal(29, output[0]);
    }

    [Fact]
    public void ContextProviderCanExecuteNestedProgramInSameBoundedFrame()
    {
        var child = Compile("add result 3; return result;", Definition());
        var (outer, bindingId) = CompileUnaryBinding("nested");
        var providers = new ScriptHostBindingsBuilder();
        providers.Add(bindingId, (context, arguments) =>
        {
            Span<int> input = stackalloc int[] { arguments[0].Scalar };
            Span<int> output = stackalloc int[1];
            var nested = context.ExecuteNestedScalar(child, input, output);
            if (!nested.Succeeded)
            {
                return ScriptBindingResult.Failure(nested.DiagnosticCode ?? "nested execution failed");
            }
            arguments[0] = ScriptRuntimeValue.FromScalar(output[0]);
            return ScriptBindingResult.Success;
        });
        var frame = new ScriptExecutionFrame();
        frame.Prepare(outer);
        frame.Prepare(child, 1);
        Span<int> initial = stackalloc int[] { 4 };
        Span<int> output = stackalloc int[1];

        var outcome = ScriptVm.ExecuteScalar(outer, initial, output, frame, hostBindings: providers.Build());

        Assert.True(outcome.Succeeded);
        Assert.Equal(7, output[0]);
        Assert.Equal(0, frame.CallDepth);
    }

    [Fact]
    public void RecursionLimitAndProviderFailureDoNotCommitWritableValues()
    {
        var (program, bindingId) = CompileUnaryBinding("recurse");
        var providers = new ScriptHostBindingsBuilder();
        providers.Add(bindingId, (context, arguments) =>
        {
            arguments[0] = ScriptRuntimeValue.FromScalar(arguments[0].Scalar + 1);
            Span<int> initial = stackalloc int[] { arguments[0].Scalar };
            Span<int> output = stackalloc int[1];
            var nested = context.ExecuteNestedScalar(program, initial, output);
            return nested.Succeeded
                ? ScriptBindingResult.Success
                : ScriptBindingResult.Failure(
                    nested.FailureMessage ?? "recursion failed",
                    nested.Status,
                    nested.DiagnosticCode);
        });
        var options = new ScriptExecutionOptions { MaximumCallDepth = 2 };
        var frame = new ScriptExecutionFrame(2);
        frame.Prepare(program, 0);
        frame.Prepare(program, 1);
        Span<int> initial = stackalloc int[] { 5 };
        Span<int> output = stackalloc int[] { 99 };

        var outcome = ScriptVm.ExecuteScalar(program, initial, output, frame, options, providers.Build());

        Assert.Equal(ScriptExecutionStatus.ExecutionLimit, outcome.Status);
        Assert.Equal(ScriptDiagnosticCodes.CallDepthExceeded, outcome.DiagnosticCode);
        Assert.Contains("call depth exceeds", outcome.FailureMessage, StringComparison.Ordinal);
        Assert.Equal(99, output[0]);
        Assert.Equal(0, frame.CallDepth);
    }

    [Fact]
    public void ContextProviderReceivesTextAndCommitsReferenceOnlyAfterSuccess()
    {
        var textId = new ScriptBindingId(30_001);
        var reference = new ScriptReferenceLocation("probe", 1);
        var textCatalog = new ScriptApiCatalog(
        [
            new ScriptBindingDeclaration(
                textId,
                "observe",
                [new ScriptBindingParameter("value", new ScriptTypeRef(ScriptPrimitiveTypes.Text), false)],
                ["Probe"],
                reference),
        ]);
        var textProgram = Compile(
            "observe \"stable text\"; return result;",
            new ScriptParserDefinition("Probe", ["result"], textCatalog, ["Probe"]));
        var textProviders = new ScriptHostBindingsBuilder();
        string? observed = null;
        textProviders.Add(textId, (_, arguments) =>
        {
            observed = arguments[0].Text;
            return ScriptBindingResult.Success;
        });
        var textFrame = new ScriptExecutionFrame();
        Span<int> scalarOutput = stackalloc int[1];
        Assert.True(ScriptVm.ExecuteScalar(textProgram, [], scalarOutput, textFrame,
            hostBindings: textProviders.Build()).Succeeded);
        Assert.Equal("stable text", observed);

        var replaceId = new ScriptBindingId(30_002);
        var referenceType = new ScriptTypeRef(
            new ScriptTypeId(ScriptPrimitiveTypes.FirstCustomTypeValue),
            ScriptTypeModifier.Register | ScriptTypeModifier.Writable | ScriptTypeModifier.Reference);
        var declaration = new ScriptBindingDeclaration(
            replaceId,
            "replace",
            [new ScriptBindingParameter("value", referenceType, true)],
            ["Probe"],
            reference);
        var referenceProgram = new ScriptProgram(
            "Probe",
            [
                new ScriptInstruction(new ScriptOperationId((int)CoreScriptOperation.HostCall),
                    [ScriptOperand.Binding(replaceId.Value), ScriptOperand.Register(0)], default),
                new ScriptInstruction(new ScriptOperationId((int)CoreScriptOperation.Return),
                    [ScriptOperand.Register(0)], default),
            ],
            [referenceType],
            IntPtr.Size,
            [new ScriptRegisterDefinition("result", referenceType, 0, true)],
            [declaration]);
        var original = new object();
        var replacement = new object();
        var referenceProviders = new ScriptHostBindingsBuilder();
        referenceProviders.Add(replaceId, (_, arguments) =>
        {
            Assert.Same(original, arguments[0].Reference);
            arguments[0] = ScriptRuntimeValue.FromReference(replacement);
            return ScriptBindingResult.Success;
        });
        var runtimeFrame = new ScriptExecutionFrame();
        var initialValues = new[] { ScriptRuntimeValue.FromReference(original) };
        var outputValues = new ScriptRuntimeValue[1];

        var result = ScriptVm.Execute(referenceProgram, initialValues, outputValues, runtimeFrame,
            hostBindings: referenceProviders.Build());

        Assert.True(result.Succeeded);
        Assert.Same(replacement, outputValues[0].Reference);
    }

    [Fact]
    public void EventFrameCommitsOnlyAfterTheCompleteChainSucceeds()
    {
        var before = Compile("add result 2; return result;", Definition());
        var current = Compile("div result 0; return result;", Definition());
        var composed = ScriptEventComposer.Compose(
        [
            new ScriptEventMutation(
                ScriptEventMutationKind.Append,
                string.Empty,
                -100,
                before,
                "probe",
                1),
        ]);
        var frame = new ScriptExecutionFrame();
        frame.Prepare(before);
        frame.Prepare(current);
        var initial = new[] { ScriptRuntimeValue.FromScalar(3) };
        var outputs = new[] { ScriptRuntimeValue.FromScalar(99) };

        var outcome = ScriptEventRunner.Execute(composed.Plan!, current, initial, outputs, frame);

        Assert.Equal(ScriptExecutionStatus.RuntimeError, outcome.Status);
        Assert.Equal(99, outputs[0].Scalar);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ScriptProgram Program, WeakReference Instruction) CreatePackedProgramProbe()
    {
        var instruction = new ScriptInstruction(
            new ScriptOperationId((int)CoreScriptOperation.Set),
            [ScriptOperand.IntegerValue(42)],
            default);
        var weak = new WeakReference(instruction);
        return (new ScriptProgram("Probe", [instruction], [], 4), weak);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static (ScriptProgram Program, ScriptBindingId BindingId) CompileUnaryBinding(string name)
    {
        var id = new ScriptBindingId(31_000 + name.Length);
        var writable = new ScriptTypeRef(
            ScriptPrimitiveTypes.Scalar,
            ScriptTypeModifier.Register | ScriptTypeModifier.Writable);
        var declaration = new ScriptBindingDeclaration(
            id,
            name,
            [new ScriptBindingParameter("target", writable, true)],
            ["Probe"],
            new ScriptReferenceLocation("probe", 1));
        var catalog = new ScriptApiCatalog([declaration]);
        return (Compile($"{name} result; return result;",
            new ScriptParserDefinition("Probe", ["result"], catalog, ["Probe"])), id);
    }

    private static ScriptParserDefinition Definition() => new("Probe", ["result"]);

    private static ScriptProgram Compile(string source, ScriptParserDefinition definition)
    {
        var compiled = ScriptCompiler.Compile(source, definition);
        Assert.True(compiled.Succeeded, string.Join(Environment.NewLine,
            compiled.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        return compiled.Program!;
    }
}
