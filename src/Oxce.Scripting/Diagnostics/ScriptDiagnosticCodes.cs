namespace Oxce.Scripting.Diagnostics;

public static class ScriptDiagnosticCodes
{
    public const string InvalidToken = "OXCE-SCR-0001";
    public const string InvalidInteger = "OXCE-SCR-0002";
    public const string UnterminatedText = "OXCE-SCR-0003";
    public const string InvalidTextEscape = "OXCE-SCR-0004";
    public const string MissingStatementTerminator = "OXCE-SCR-0005";
    public const string MissingOperation = "OXCE-SCR-0006";
    public const string InvalidLabel = "OXCE-SCR-0007";
    public const string TooManyArguments = "OXCE-SCR-0008";
    public const string DuplicateSymbol = "OXCE-SCR-0009";
    public const string RegisterLimitExceeded = "OXCE-SCR-0010";
    public const string NoMatchingOverload = "OXCE-SCR-0011";
    public const string AmbiguousOverload = "OXCE-SCR-0012";
}
