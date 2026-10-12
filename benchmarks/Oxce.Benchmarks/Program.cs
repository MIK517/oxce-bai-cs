using BenchmarkDotNet.Running;

namespace Oxce.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args is ["--time-profile", ..])
        {
            Environment.ExitCode = TimeAdvanceProfiler.Run(args[1..]);
            return;
        }
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
