using Oxce.Core.Diagnostics;
using Oxce.Scripting.Binding;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Symbols;
using Oxce.Scripting.Types;
using Xunit;

namespace Oxce.UnitTests.Scripting;

public sealed class ScriptSymbolsAndIrTests
{
    [Fact]
    public void SymbolsCannotShadowAnyVisibleReference()
    {
        var symbols = new ScriptSymbolTable();
        var type = new ScriptTypeRef(ScriptPrimitiveTypes.Scalar, ScriptTypeModifier.Register);
        Assert.True(symbols.TryDeclare(new ScriptSymbol("value", ScriptSymbolKind.Local, type, 0)));

        symbols.PushScope();

        Assert.False(symbols.TryDeclare(new ScriptSymbol("value", ScriptSymbolKind.Local, type, 4)));
        Assert.True(symbols.TryResolve("value", out var resolved));
        Assert.Equal(0, resolved?.RegisterOffset);
        symbols.PopScope();
    }

    [Fact]
    public void RegisterLayoutUsesAlignmentAndReferenceLimit()
    {
        var layout = new ScriptRegisterLayout(16);
        var byteValue = new ScriptTypeDefinition(ScriptPrimitiveTypes.Scalar, "byte", 1, 1);
        var wideValue = new ScriptTypeDefinition(new ScriptTypeId(6), "wide", 8, 8);

        Assert.True(layout.TryAllocate(byteValue, useReferenceLayout: false, out var first));
        Assert.True(layout.TryAllocate(wideValue, useReferenceLayout: false, out var second));
        Assert.False(layout.TryAllocate(byteValue, useReferenceLayout: true, out var failed));
        Assert.Equal(0, first);
        Assert.Equal(8, second);
        Assert.Equal(-1, failed);
        Assert.Equal(16, layout.UsedBytes);
    }

    [Fact]
    public void RegisterLayoutReclaimsNestedScopeStorage()
    {
        var layout = new ScriptRegisterLayout(8);
        var scalar = new ScriptTypeDefinition(ScriptPrimitiveTypes.Scalar, "scalar", 4, 4);
        Assert.True(layout.TryAllocate(scalar, useReferenceLayout: false, out _));
        layout.PushScope();
        Assert.True(layout.TryAllocate(scalar, useReferenceLayout: false, out var nested));

        layout.PopScope();

        Assert.Equal(4, layout.UsedBytes);
        Assert.True(layout.TryAllocate(scalar, useReferenceLayout: false, out var reused));
        Assert.Equal(nested, reused);
    }

    [Fact]
    public void ProgramDefensivelyCopiesInstructionsAndOutputs()
    {
        var instructions = new List<ScriptInstruction>();
        var outputs = new List<ScriptTypeRef> { new(ScriptPrimitiveTypes.Scalar) };
        var span = new SourceSpan("probe", new SourcePosition(1, 1, 0), new SourcePosition(1, 2, 1));
        instructions.Add(new ScriptInstruction(
            new ScriptOperationId(1),
            [ScriptOperand.IntegerValue(42)],
            span));

        var program = new ScriptProgram("Probe", instructions, outputs, 4);
        instructions.Clear();
        outputs.Clear();

        Assert.Single(program.Instructions);
        Assert.Single(program.Outputs);
        Assert.Equal(42, Assert.Single(program.Instructions[0].Operands).Scalar);
    }
}
