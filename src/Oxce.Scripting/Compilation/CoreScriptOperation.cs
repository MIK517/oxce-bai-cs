namespace Oxce.Scripting.Compilation;

public enum CoreScriptOperation
{
    Exit = 1,
    Jump,
    BranchCondition,
    Return,
    Set,
    Clear,
    Swap,
    Add,
    Subtract,
    Multiply,
    Aggregate,
    Offset,
    OffsetModulo,
    Divide,
    Modulo,
    MultiplyDivide,
    ShiftLeft,
    ShiftRight,
    BitAnd,
    BitOr,
    BitXor,
    BitNot,
    BitCount,
    Power,
    SquareRoot,
    Absolute,
    Limit,
    LimitUpper,
    LimitLower,
    WaveRectangle,
    WaveSaw,
    WaveTriangle,
    WaveSine,
    WaveCosine,
    GetColor,
    SetColor,
    GetShade,
    SetShade,
    AddShade,
}

public enum ScriptConditionKind
{
    Equal,
    NotEqual,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    All,
    Any,
}

internal static class CoreScriptOperationNames
{
    private static readonly Dictionary<string, CoreScriptOperation> Names =
        new Dictionary<string, CoreScriptOperation>(StringComparer.Ordinal)
        {
            ["set"] = CoreScriptOperation.Set,
            ["clear"] = CoreScriptOperation.Clear,
            ["swap"] = CoreScriptOperation.Swap,
            ["add"] = CoreScriptOperation.Add,
            ["sub"] = CoreScriptOperation.Subtract,
            ["mul"] = CoreScriptOperation.Multiply,
            ["aggregate"] = CoreScriptOperation.Aggregate,
            ["offset"] = CoreScriptOperation.Offset,
            ["offsetmod"] = CoreScriptOperation.OffsetModulo,
            ["div"] = CoreScriptOperation.Divide,
            ["mod"] = CoreScriptOperation.Modulo,
            ["muldiv"] = CoreScriptOperation.MultiplyDivide,
            ["shl"] = CoreScriptOperation.ShiftLeft,
            ["shr"] = CoreScriptOperation.ShiftRight,
            ["bit_and"] = CoreScriptOperation.BitAnd,
            ["bit_or"] = CoreScriptOperation.BitOr,
            ["bit_xor"] = CoreScriptOperation.BitXor,
            ["bit_not"] = CoreScriptOperation.BitNot,
            ["bit_count"] = CoreScriptOperation.BitCount,
            ["pow"] = CoreScriptOperation.Power,
            ["sqrt"] = CoreScriptOperation.SquareRoot,
            ["abs"] = CoreScriptOperation.Absolute,
            ["limit"] = CoreScriptOperation.Limit,
            ["limit_upper"] = CoreScriptOperation.LimitUpper,
            ["limit_lower"] = CoreScriptOperation.LimitLower,
            ["wavegen_rect"] = CoreScriptOperation.WaveRectangle,
            ["wavegen_saw"] = CoreScriptOperation.WaveSaw,
            ["wavegen_tri"] = CoreScriptOperation.WaveTriangle,
            ["wavegen_sin"] = CoreScriptOperation.WaveSine,
            ["wavegen_cos"] = CoreScriptOperation.WaveCosine,
            ["get_color"] = CoreScriptOperation.GetColor,
            ["set_color"] = CoreScriptOperation.SetColor,
            ["get_shade"] = CoreScriptOperation.GetShade,
            ["set_shade"] = CoreScriptOperation.SetShade,
            ["add_shade"] = CoreScriptOperation.AddShade,
        };

    public static bool TryGet(string name, out CoreScriptOperation operation) =>
        Names.TryGetValue(name, out operation);

    public static string Get(CoreScriptOperation operation) =>
        Names.FirstOrDefault(pair => pair.Value == operation).Key ?? operation.ToString();
}
