using Oxce.Core.Diagnostics;
using Oxce.Scripting.Binding;
using Oxce.Scripting.Api;
using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Lexing;
using Oxce.Scripting.Symbols;
using Oxce.Scripting.Syntax;
using Oxce.Scripting.Types;

namespace Oxce.Scripting.Compilation;

public sealed class ScriptCompiler
{
    private static readonly ScriptTypeDefinition ScalarType =
        new(ScriptPrimitiveTypes.Scalar, "int", sizeof(int), sizeof(int));

    private readonly ScriptParserDefinition _definition;
    private readonly ScriptCompilerOptions _options;
    private readonly List<DiagnosticEvent> _diagnostics = [];
    private readonly List<ScriptInstruction> _instructions = [];
    private readonly List<ScriptRegisterDefinition> _registers = [];
    private readonly Dictionary<int, ScriptBindingDeclaration> _bindings = [];
    private readonly ScriptSymbolTable _symbols = new();
    private readonly ScriptRegisterLayout _layout = new();
    private readonly Stack<ControlFrame> _frames = new();
    private bool _rootReturned;

    private ScriptCompiler(ScriptParserDefinition definition, ScriptCompilerOptions options)
    {
        _definition = definition;
        _options = options;
        _frames.Push(new ControlFrame(ControlFrameKind.Root));
    }

    public static ScriptCompileResult Compile(
        string source,
        ScriptParserDefinition definition,
        string sourceName = "<script>",
        ScriptCompilerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(definition);
        options ??= new ScriptCompilerOptions();
        options.Validate();
        var compiler = new ScriptCompiler(definition, options);
        return compiler.Run(source, sourceName);
    }

    private ScriptCompileResult Run(string source, string sourceName)
    {
        var syntax = ScriptSyntaxParser.Parse(source, sourceName);
        _diagnostics.AddRange(syntax.Diagnostics);
        if (!syntax.IsValid)
        {
            return Failure();
        }

        DeclareParameters();
        DeclareConstants();
        foreach (var statement in syntax.Statements)
        {
            CompileStatement(statement);
            if (HasErrors)
            {
                return Failure();
            }
        }

        if (_frames.Count != 1)
        {
            Error(ScriptDiagnosticCodes.InvalidControlFlow, "Script has a missing 'end;' statement.",
                syntax.Statements.Count == 0 ? null : syntax.Statements[^1].Span);
        }
        else if (syntax.Statements.Count != 0 && !_rootReturned)
        {
            Error(ScriptDiagnosticCodes.MissingReturn,
                $"Script '{_definition.Name}' must end with a return statement.",
                syntax.Statements[^1].Span);
        }
        if (HasErrors)
        {
            return Failure();
        }

        var outputs = _registers.Where(static register => register.IsOutput).Select(static register => register.Type);
        var program = new ScriptProgram(
            _definition.Name,
            _instructions,
            outputs,
            _layout.PeakBytes,
            _registers,
            _bindings.Values.OrderBy(static binding => binding.Id.Value));
        return new ScriptCompileResult(program, _diagnostics);
    }

    private void DeclareParameters()
    {
        foreach (var output in _definition.Outputs)
        {
            DeclareParameter(output, isOutput: true);
        }
        foreach (var input in _definition.Inputs)
        {
            DeclareParameter(input, isOutput: false);
        }
    }

    private void DeclareParameter(ScriptNamedValueDeclaration value, bool isOutput)
    {
        var type = value.Type.IsRegister
            ? value.Type
            : new ScriptTypeRef(value.Type.Id, value.Type.Modifiers | ScriptTypeModifier.Register);
        var definition = ResolveType(type.Id);
        if (!_layout.TryAllocate(definition, type.IsReference, out var offset))
        {
            throw new InvalidOperationException("Parser parameter definitions exceed the validated register limit.");
        }
        if (!_symbols.TryDeclare(new ScriptSymbol(value.Name, ScriptSymbolKind.Parameter, type, offset)))
        {
            throw new InvalidOperationException($"Duplicate validated parser parameter name '{value.Name}'.");
        }
        _registers.Add(new ScriptRegisterDefinition(value.Name, type, offset, isOutput));
    }

    private ScriptTypeDefinition ResolveType(ScriptTypeId id)
    {
        if (_definition.ApiCatalog.TryGetType(id, out var definition))
        {
            return definition!;
        }
        return id == ScriptPrimitiveTypes.Scalar
            ? ScalarType
            : throw new InvalidOperationException($"Script type '{id}' has no layout declaration.");
    }

    private void DeclareConstants()
    {
        var type = new ScriptTypeRef(ScriptPrimitiveTypes.Scalar);
        foreach (var constant in _definition.ApiCatalog.GetConstants(_definition.ParserGroups))
        {
            if (!_symbols.TryDeclare(new ScriptSymbol(
                constant.Name,
                ScriptSymbolKind.Constant,
                type,
                ConstantValue: constant.Value)))
            {
                Error(ScriptDiagnosticCodes.DuplicateSymbol,
                    $"Global script symbol '{constant.Name}' conflicts with another declaration.",
                    null);
                return;
            }
        }
    }

    private void CompileStatement(ScriptStatementSyntax statement)
    {
        if (statement.Label is not null)
        {
            Error(ScriptDiagnosticCodes.InvalidLabel,
                $"Named label '{statement.Label.Lexeme}' is not a valid public core-language declaration.",
                statement.Label.Span);
            return;
        }

        var name = statement.Operation.Lexeme;
        var isBoundary = name is "else" or "end";
        if (Current.Terminated && !isBoundary)
        {
            Error(ScriptDiagnosticCodes.UnreachableCode,
                $"Unreachable code after return, break, or continue: '{name}'.",
                statement.Span);
            return;
        }

        switch (name)
        {
            case "var": CompileVariable(statement); break;
            case "const": CompileConstant(statement); break;
            case "begin": CompileBegin(statement); break;
            case "if": CompileIf(statement); break;
            case "else": CompileElse(statement); break;
            case "loop": CompileLoop(statement); break;
            case "break": CompileBreak(statement); break;
            case "continue": CompileContinue(statement); break;
            case "end": CompileEnd(statement); break;
            case "return": CompileReturn(statement); break;
            case "debug_log" or "debug_assert": Current.CanDeclare = false; break;
            default: CompileOperation(statement); break;
        }
    }

    private void CompileVariable(ScriptStatementSyntax statement)
    {
        if (!Current.CanDeclare)
        {
            Error(ScriptDiagnosticCodes.InvalidDeclaration,
                "Variable declarations must precede ordinary operations in their block.",
                statement.Span);
            return;
        }
        if (statement.Arguments.Count is not (2 or 3) || statement.Arguments[0].Lexeme != "int" ||
            statement.Arguments[1].Kind != ScriptTokenKind.Symbol)
        {
            Error(ScriptDiagnosticCodes.InvalidDeclaration,
                "An integer variable declaration has the form 'var int name [value];'.",
                statement.Span);
            return;
        }

        var name = statement.Arguments[1].Lexeme;
        if (!_layout.TryAllocate(ScalarType, useReferenceLayout: false, out var offset))
        {
            Error(ScriptDiagnosticCodes.RegisterLimitExceeded,
                $"Variable '{name}' exceeds the script register limit.",
                statement.Arguments[1].Span);
            return;
        }
        var type = new ScriptTypeRef(
            ScriptPrimitiveTypes.Scalar,
            ScriptTypeModifier.Register | ScriptTypeModifier.Writable);
        if (!_symbols.TryDeclare(new ScriptSymbol(name, ScriptSymbolKind.Local, type, offset)))
        {
            Error(ScriptDiagnosticCodes.DuplicateSymbol, $"Script symbol '{name}' is already defined.",
                statement.Arguments[1].Span);
            return;
        }
        _registers.Add(new ScriptRegisterDefinition(name, type, offset, IsOutput: false));
        Emit(
            statement.Arguments.Count == 2 ? CoreScriptOperation.Clear : CoreScriptOperation.Set,
            statement.Span,
            statement.Arguments.Count == 2
                ? [ScriptOperand.Register(offset)]
                : [ScriptOperand.Register(offset), ReadValue(statement.Arguments[2])]);
    }

    private void CompileConstant(ScriptStatementSyntax statement)
    {
        if (!Current.CanDeclare || statement.Arguments.Count != 3 ||
            statement.Arguments[0].Lexeme != "int" || statement.Arguments[1].Kind != ScriptTokenKind.Symbol)
        {
            Error(ScriptDiagnosticCodes.InvalidDeclaration,
                "An integer constant declaration has the form 'const int name value;'.",
                statement.Span);
            return;
        }
        var name = statement.Arguments[1].Lexeme;
        var value = ReadConstant(statement.Arguments[2]);
        if (HasErrors)
        {
            return;
        }
        var type = new ScriptTypeRef(ScriptPrimitiveTypes.Scalar);
        if (!_symbols.TryDeclare(new ScriptSymbol(name, ScriptSymbolKind.Constant, type, ConstantValue: value)))
        {
            Error(ScriptDiagnosticCodes.DuplicateSymbol, $"Script symbol '{name}' is already defined.",
                statement.Arguments[1].Span);
            return;
        }
    }

    private void CompileBegin(ScriptStatementSyntax statement)
    {
        RequireArgumentCount(statement, 0);
        if (HasErrors)
        {
            return;
        }
        PushFrame(new ControlFrame(ControlFrameKind.Begin));
    }

    private void CompileIf(ScriptStatementSyntax statement)
    {
        var operands = ReadCondition(statement);
        if (HasErrors)
        {
            return;
        }
        Current.CanDeclare = false;
        var branch = Emit(CoreScriptOperation.BranchCondition, statement.Span, [.. operands, ScriptOperand.Label(0)]);
        PushFrame(new ControlFrame(ControlFrameKind.If) { BranchInstruction = branch });
    }

    private void CompileElse(ScriptStatementSyntax statement)
    {
        if (_frames.Count == 1 || Current.Kind != ControlFrameKind.If)
        {
            Error(ScriptDiagnosticCodes.InvalidControlFlow, "Unexpected 'else'.", statement.Span);
            return;
        }
        var previous = PopFrame();
        var jump = Emit(CoreScriptOperation.Jump, statement.Span, [ScriptOperand.Label(0)]);
        PatchTarget(previous.BranchInstruction, _instructions.Count);
        var finalJumps = previous.FinalJumpInstructions;
        finalJumps.Add(jump);
        if (statement.Arguments.Count == 0)
        {
            PushFrame(new ControlFrame(ControlFrameKind.Else, finalJumps));
            return;
        }

        var operands = ReadCondition(statement);
        if (HasErrors)
        {
            return;
        }
        var branch = Emit(CoreScriptOperation.BranchCondition, statement.Span,
            [.. operands, ScriptOperand.Label(0)]);
        PushFrame(new ControlFrame(ControlFrameKind.If, finalJumps) { BranchInstruction = branch });
    }

    private void CompileLoop(ScriptStatementSyntax statement)
    {
        if (statement.Arguments.Count != 3 || statement.Arguments[0].Lexeme != "var" ||
            statement.Arguments[1].Kind != ScriptTokenKind.Symbol)
        {
            Error(ScriptDiagnosticCodes.InvalidArguments,
                "A counted loop has the form 'loop var name limit;'.",
                statement.Span);
            return;
        }
        Current.CanDeclare = false;
        PushFrame(new ControlFrame(ControlFrameKind.Loop));
        var frame = Current;
        var limitValue = ReadValue(statement.Arguments[2]);
        frame.LimitRegister = AllocateHidden(statement.Span);
        frame.CounterRegister = AllocateHidden(statement.Span);
        var variableName = statement.Arguments[1].Lexeme;
        frame.LoopVariable = AllocateLocal(variableName, statement.Arguments[1].Span);
        if (HasErrors)
        {
            return;
        }
        Emit(CoreScriptOperation.Set, statement.Span,
            [ScriptOperand.Register(frame.LimitRegister), limitValue]);
        Emit(CoreScriptOperation.Clear, statement.Span,
            [ScriptOperand.Register(frame.CounterRegister)]);
        frame.LoopStart = _instructions.Count;
        frame.BranchInstruction = Emit(CoreScriptOperation.BranchCondition, statement.Span,
            [ScriptOperand.IntegerValue((int)ScriptConditionKind.All),
             ScriptOperand.IntegerValue((int)ScriptConditionKind.LessThan),
             ScriptOperand.Register(frame.CounterRegister), ScriptOperand.Register(frame.LimitRegister),
             ScriptOperand.Label(0)]);
        Emit(CoreScriptOperation.Set, statement.Span,
            [ScriptOperand.Register(frame.LoopVariable), ScriptOperand.Register(frame.CounterRegister)]);
        Emit(CoreScriptOperation.Add, statement.Span,
            [ScriptOperand.Register(frame.CounterRegister), ScriptOperand.IntegerValue(1)]);
    }

    private void CompileBreak(ScriptStatementSyntax statement)
    {
        RequireArgumentCount(statement, 0);
        if (HasErrors)
        {
            return;
        }
        var loop = _frames.FirstOrDefault(static frame => frame.Kind == ControlFrameKind.Loop);
        if (loop is null)
        {
            Error(ScriptDiagnosticCodes.InvalidControlFlow, "Operation 'break' is outside a loop.", statement.Span);
            return;
        }
        loop.BreakInstructions.Add(Emit(CoreScriptOperation.Jump, statement.Span, [ScriptOperand.Label(0)]));
        Current.Terminated = true;
        Current.CanDeclare = false;
    }

    private void CompileContinue(ScriptStatementSyntax statement)
    {
        RequireArgumentCount(statement, 0);
        if (HasErrors)
        {
            return;
        }
        var loop = _frames.FirstOrDefault(static frame => frame.Kind == ControlFrameKind.Loop);
        if (loop is null)
        {
            Error(ScriptDiagnosticCodes.InvalidControlFlow, "Operation 'continue' is outside a loop.", statement.Span);
            return;
        }
        Emit(CoreScriptOperation.Jump, statement.Span, [ScriptOperand.Label(loop.LoopStart)]);
        Current.Terminated = true;
        Current.CanDeclare = false;
    }

    private void CompileEnd(ScriptStatementSyntax statement)
    {
        RequireArgumentCount(statement, 0);
        if (HasErrors)
        {
            return;
        }
        if (_frames.Count == 1)
        {
            Error(ScriptDiagnosticCodes.InvalidControlFlow, "Unexpected 'end'.", statement.Span);
            return;
        }
        var frame = PopFrame();
        switch (frame.Kind)
        {
            case ControlFrameKind.If:
                PatchTarget(frame.BranchInstruction, _instructions.Count);
                foreach (var instruction in frame.FinalJumpInstructions)
                {
                    PatchTarget(instruction, _instructions.Count);
                }
                break;
            case ControlFrameKind.Else:
                foreach (var instruction in frame.FinalJumpInstructions)
                {
                    PatchTarget(instruction, _instructions.Count);
                }
                break;
            case ControlFrameKind.Loop:
                Emit(CoreScriptOperation.Jump, statement.Span, [ScriptOperand.Label(frame.LoopStart)]);
                PatchTarget(frame.BranchInstruction, _instructions.Count);
                foreach (var instruction in frame.BreakInstructions)
                {
                    PatchTarget(instruction, _instructions.Count);
                }
                break;
        }
        Current.CanDeclare = false;
        Current.Terminated = false;
    }

    private void CompileReturn(ScriptStatementSyntax statement)
    {
        if (statement.Arguments.Count != _definition.OutputNames.Count)
        {
            Error(ScriptDiagnosticCodes.InvalidArguments,
                $"Return requires {_definition.OutputNames.Count} value(s).",
                statement.Span);
            return;
        }
        var operands = statement.Arguments.Select(ReadValue).ToArray();
        if (HasErrors)
        {
            return;
        }
        Emit(CoreScriptOperation.Return, statement.Span, operands);
        Current.Terminated = true;
        Current.CanDeclare = false;
        if (_frames.Count == 1)
        {
            _rootReturned = true;
        }
    }

    private void CompileOperation(ScriptStatementSyntax statement)
    {
        if (!CoreScriptOperationNames.TryGet(statement.Operation.Lexeme, out var operation))
        {
            CompileBinding(statement);
            return;
        }
        var expected = ArgumentCount(operation);
        if (statement.Arguments.Count != expected &&
            _definition.ApiCatalog.GetBindings(statement.Operation.Lexeme, _definition.ParserGroups).Count != 0)
        {
            CompileBinding(statement);
            return;
        }
        Current.CanDeclare = false;
        var typedArguments = statement.Arguments.Select(ReadTypedValue).ToArray();
        if (HasErrors)
        {
            return;
        }
        var arguments = typedArguments.Select(static argument => argument.Operand).ToArray();
        if (arguments.Length != expected || expected != 0 && arguments[0].Kind != ScriptOperandKind.Register ||
            operation == CoreScriptOperation.Swap && arguments[1].Kind != ScriptOperandKind.Register ||
            typedArguments.Any(static argument => argument.Type.Id != ScriptPrimitiveTypes.Scalar))
        {
            Error(ScriptDiagnosticCodes.InvalidArguments,
                $"Operation '{statement.Operation.Lexeme}' requires {expected} compatible argument(s).",
                statement.Span);
            return;
        }
        Emit(operation, statement.Span, arguments);
    }

    private void CompileBinding(ScriptStatementSyntax statement)
    {
        var operationName = statement.Operation.Lexeme;
        var arguments = new List<TypedOperand>();
        var separator = operationName.IndexOf('.', StringComparison.Ordinal);
        if (separator > 0 && _symbols.TryResolve(operationName[..separator], out var receiver) &&
            receiver?.RegisterOffset is int receiverOffset &&
            _definition.ApiCatalog.TryGetType(receiver.Type.Id, out var receiverType))
        {
            operationName = $"{receiverType!.Name}.{operationName[(separator + 1)..]}";
            arguments.Add(new TypedOperand(ScriptOperand.Register(receiverOffset), receiver.Type));
        }
        var candidates = _definition.ApiCatalog.GetBindings(operationName, _definition.ParserGroups);
        if (candidates.Count == 0)
        {
            Error(ScriptDiagnosticCodes.UnknownOperation,
                $"Invalid operation '{statement.Operation.Lexeme}'.",
                statement.Operation.Span);
            return;
        }

        arguments.AddRange(statement.Arguments.Select(ReadTypedValue));
        if (HasErrors)
        {
            return;
        }
        var matching = candidates.Where(candidate => BindingMatches(candidate, arguments)).ToArray();
        if (matching.Length != 1)
        {
            Error(
                matching.Length == 0 ? ScriptDiagnosticCodes.NoMatchingOverload : ScriptDiagnosticCodes.AmbiguousOverload,
                matching.Length == 0
                    ? $"No overload of '{statement.Operation.Lexeme}' accepts these arguments."
                    : $"Operation '{statement.Operation.Lexeme}' is ambiguous for these arguments.",
                statement.Span);
            return;
        }

        var selected = matching[0];
        _bindings.TryAdd(selected.Id.Value, selected);
        Emit(CoreScriptOperation.HostCall, statement.Span,
            [ScriptOperand.Binding(selected.Id.Value), .. arguments.Select(static argument => argument.Operand)]);
    }

    private static bool BindingMatches(
        ScriptBindingDeclaration declaration,
        IReadOnlyList<TypedOperand> arguments)
    {
        if (declaration.Parameters.Count != arguments.Count)
        {
            return false;
        }
        for (var index = 0; index < arguments.Count; index++)
        {
            var parameter = declaration.Parameters[index];
            var argument = arguments[index];
            if (parameter.Type.Id != argument.Type.Id ||
                parameter.Writable && (!argument.Type.IsWritable || argument.Operand.Kind != ScriptOperandKind.Register) ||
                parameter.Type.IsReference && !argument.Type.IsReference ||
                parameter.Type.IsEditableReference && !argument.Type.IsEditableReference ||
                parameter.Type.Id == ScriptPrimitiveTypes.Separator && argument.Type.Id != ScriptPrimitiveTypes.Separator)
            {
                return false;
            }
        }
        return true;
    }

    private ScriptOperand[] ReadCondition(ScriptStatementSyntax statement)
    {
        var arguments = statement.Arguments;
        if (arguments.Count < 3)
        {
            Error(ScriptDiagnosticCodes.InvalidArguments, "A condition requires an operator and two values.", statement.Span);
            return [];
        }
        var result = new List<ScriptOperand>();
        var start = 0;
        var overall = ScriptConditionKind.All;
        if (arguments[0].Lexeme is "and" or "or")
        {
            overall = arguments[0].Lexeme == "and" ? ScriptConditionKind.All : ScriptConditionKind.Any;
            start = 1;
        }
        if ((arguments.Count - start) % 3 != 0)
        {
            Error(ScriptDiagnosticCodes.InvalidArguments, "Combined conditions require complete three-argument clauses.", statement.Span);
            return [];
        }
        result.Add(ScriptOperand.IntegerValue((int)overall));
        for (var index = start; index < arguments.Count; index += 3)
        {
            if (!TryCondition(arguments[index].Lexeme, out var condition))
            {
                Error(ScriptDiagnosticCodes.InvalidArguments,
                    $"Unknown condition '{arguments[index].Lexeme}'.",
                    arguments[index].Span);
                return [];
            }
            result.Add(ScriptOperand.IntegerValue((int)condition));
            result.Add(ReadValue(arguments[index + 1]));
            result.Add(ReadValue(arguments[index + 2]));
        }
        return result.ToArray();
    }

    private ScriptOperand ReadValue(ScriptToken token)
        => ReadTypedValue(token).Operand;

    private TypedOperand ReadTypedValue(ScriptToken token)
    {
        if (token.Kind == ScriptTokenKind.Numeric)
        {
            return new TypedOperand(
                ScriptOperand.IntegerValue(token.NumericValue!.Value),
                new ScriptTypeRef(ScriptPrimitiveTypes.Scalar));
        }
        if (token.Kind == ScriptTokenKind.Text)
        {
            return new TypedOperand(
                ScriptOperand.TextValue(token.TextValue!),
                new ScriptTypeRef(ScriptPrimitiveTypes.Text));
        }
        if (token.Kind == ScriptTokenKind.Symbol && token.Lexeme == "__")
        {
            return new TypedOperand(
                ScriptOperand.IntegerValue(0),
                new ScriptTypeRef(ScriptPrimitiveTypes.Separator));
        }
        if (token.Kind == ScriptTokenKind.Symbol)
        {
            if (_symbols.TryResolve(token.Lexeme, out var symbol))
            {
                if (symbol?.ConstantValue is int constant)
                {
                    return new TypedOperand(
                        ScriptOperand.IntegerValue(constant),
                        new ScriptTypeRef(ScriptPrimitiveTypes.Scalar));
                }
                if (symbol?.RegisterOffset is int offset)
                {
                    return new TypedOperand(ScriptOperand.Register(offset), symbol.Type);
                }
            }
        }
        Error(ScriptDiagnosticCodes.UnknownSymbol, $"Unknown integer value '{token.Lexeme}'.", token.Span);
        return new TypedOperand(
            ScriptOperand.IntegerValue(0),
            new ScriptTypeRef(ScriptPrimitiveTypes.Scalar));
    }

    private int ReadConstant(ScriptToken token)
    {
        var operand = ReadValue(token);
        if (operand.Kind != ScriptOperandKind.Scalar)
        {
            Error(ScriptDiagnosticCodes.InvalidDeclaration, "A constant value must be another constant or literal.", token.Span);
        }
        return operand.Scalar;
    }

    private int AllocateHidden(SourceSpan span)
    {
        if (_layout.TryAllocate(ScalarType, useReferenceLayout: false, out var offset))
        {
            return offset;
        }
        Error(ScriptDiagnosticCodes.RegisterLimitExceeded, "Loop temporaries exceed the script register limit.", span);
        return 0;
    }

    private int AllocateLocal(string name, SourceSpan span)
    {
        var offset = AllocateHidden(span);
        var type = new ScriptTypeRef(
            ScriptPrimitiveTypes.Scalar,
            ScriptTypeModifier.Register | ScriptTypeModifier.Writable);
        if (!_symbols.TryDeclare(new ScriptSymbol(name, ScriptSymbolKind.Local, type, offset)))
        {
            Error(ScriptDiagnosticCodes.DuplicateSymbol, $"Script symbol '{name}' is already defined.", span);
        }
        _registers.Add(new ScriptRegisterDefinition(name, type, offset, IsOutput: false));
        return offset;
    }

    private void PushFrame(ControlFrame frame)
    {
        _symbols.PushScope();
        _layout.PushScope();
        _frames.Push(frame);
    }

    private ControlFrame PopFrame()
    {
        var frame = _frames.Pop();
        _symbols.PopScope();
        _layout.PopScope();
        return frame;
    }

    private int Emit(CoreScriptOperation operation, SourceSpan span, IEnumerable<ScriptOperand> operands)
    {
        if (_instructions.Count >= _options.MaximumInstructions)
        {
            Error(ScriptDiagnosticCodes.ProgramLimitExceeded,
                $"Compiled script exceeds the {_options.MaximumInstructions}-instruction limit.",
                span);
            return Math.Max(0, _instructions.Count - 1);
        }
        var index = _instructions.Count;
        _instructions.Add(new ScriptInstruction(new ScriptOperationId((int)operation), operands, span));
        return index;
    }

    private void PatchTarget(int instructionIndex, int target)
    {
        var instruction = _instructions[instructionIndex];
        var operands = instruction.Operands.ToArray();
        operands[^1] = ScriptOperand.Label(target);
        _instructions[instructionIndex] = new ScriptInstruction(instruction.Operation, operands, instruction.Source);
    }

    private void RequireArgumentCount(ScriptStatementSyntax statement, int expected)
    {
        if (statement.Arguments.Count != expected)
        {
            Error(ScriptDiagnosticCodes.InvalidArguments,
                $"Operation '{statement.Operation.Lexeme}' requires {expected} argument(s).",
                statement.Span);
        }
    }

    private static int ArgumentCount(CoreScriptOperation operation) => operation switch
    {
        CoreScriptOperation.Clear or CoreScriptOperation.BitNot or CoreScriptOperation.BitCount or
            CoreScriptOperation.SquareRoot or CoreScriptOperation.Absolute => 1,
        CoreScriptOperation.Set or CoreScriptOperation.Swap or CoreScriptOperation.Add or
            CoreScriptOperation.Subtract or CoreScriptOperation.Multiply or CoreScriptOperation.Divide or
            CoreScriptOperation.Modulo or CoreScriptOperation.ShiftLeft or CoreScriptOperation.ShiftRight or
            CoreScriptOperation.BitAnd or CoreScriptOperation.BitOr or CoreScriptOperation.BitXor or
            CoreScriptOperation.Power or CoreScriptOperation.LimitUpper or CoreScriptOperation.LimitLower or
            CoreScriptOperation.GetColor or CoreScriptOperation.SetColor or CoreScriptOperation.GetShade or
            CoreScriptOperation.SetShade or CoreScriptOperation.AddShade => 2,
        CoreScriptOperation.Aggregate or CoreScriptOperation.Offset or CoreScriptOperation.MultiplyDivide or
            CoreScriptOperation.Limit or CoreScriptOperation.WaveSine or CoreScriptOperation.WaveCosine => 3,
        CoreScriptOperation.OffsetModulo or CoreScriptOperation.WaveRectangle or
            CoreScriptOperation.WaveSaw or CoreScriptOperation.WaveTriangle => 4,
        _ => 0,
    };

    private static bool TryCondition(string name, out ScriptConditionKind condition)
    {
        condition = name switch
        {
            "eq" => ScriptConditionKind.Equal,
            "neq" => ScriptConditionKind.NotEqual,
            "le" => ScriptConditionKind.LessThanOrEqual,
            "gt" => ScriptConditionKind.GreaterThan,
            "ge" => ScriptConditionKind.GreaterThanOrEqual,
            "lt" => ScriptConditionKind.LessThan,
            _ => default,
        };
        return name is "eq" or "neq" or "le" or "gt" or "ge" or "lt";
    }

    private bool HasErrors => _diagnostics.Any(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Error);

    private ControlFrame Current => _frames.Peek();

    private ScriptCompileResult Failure() => new(null, _diagnostics);

    private void Error(string code, string message, SourceSpan? span) =>
        _diagnostics.Add(new DiagnosticEvent(code, DiagnosticSeverity.Error, message, span));

    private enum ControlFrameKind { Root, Begin, If, Else, Loop }

    private readonly record struct TypedOperand(ScriptOperand Operand, ScriptTypeRef Type);

    private sealed class ControlFrame(ControlFrameKind kind, List<int>? finalJumpInstructions = null)
    {
        public ControlFrameKind Kind { get; } = kind;
        public bool CanDeclare { get; set; } = true;
        public bool Terminated { get; set; }
        public int BranchInstruction { get; set; }
        public int LoopStart { get; set; }
        public int LimitRegister { get; set; }
        public int CounterRegister { get; set; }
        public int LoopVariable { get; set; }
        public List<int> FinalJumpInstructions { get; } = finalJumpInstructions ?? [];
        public List<int> BreakInstructions { get; } = [];
    }
}
