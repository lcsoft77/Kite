using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class LambdaFunctionModelTests
{
    [Fact]
    public void LambdaFunction_DefaultValues_AreCorrect()
    {
        using var fn = new LambdaFunction();
        fn.Name.Should().BeEmpty();
        fn.Handler.Should().BeEmpty();
        fn.DllPath.Should().BeEmpty();
        fn.MemoryMb.Should().Be(128);
        fn.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        fn.AssemblyLoadContext.Should().BeNull();
        fn.RequestLog.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void LambdaFunction_NotifyChange_FiresEvent()
    {
        using var fn = new LambdaFunction { Name = "test" };
        var fired = false;
        fn.OnChange += () => fired = true;

        fn.NotifyChange();

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task LambdaFunction_Dispose_DisposesLock()
    {
        var fn = new LambdaFunction { Name = "test" };
        fn.Dispose();

        // After dispose, WaitAsync should throw ObjectDisposedException
        var act = async () => await fn.FunctionLock.WaitAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void LambdaFunction_FunctionLock_InitialCountIsOne()
    {
        using var fn = new LambdaFunction { Name = "test" };
        fn.FunctionLock.CurrentCount.Should().Be(1);
    }
}
