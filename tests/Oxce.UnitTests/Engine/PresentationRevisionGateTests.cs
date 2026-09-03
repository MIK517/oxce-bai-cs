using Oxce.Engine;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class PresentationRevisionGateTests
{
    [Fact]
    public void UnchangedFramesAreSuppressedUntilRevisionChangesOrGateResets()
    {
        var gate = new PresentationRevisionGate();

        Assert.True(gate.TryAccept(0));
        for (var index = 0; index < 10_000; index++) Assert.False(gate.TryAccept(0));
        Assert.True(gate.TryAccept(1));
        Assert.False(gate.TryAccept(1));
        gate.Reset();
        Assert.True(gate.TryAccept(1));
    }
}
