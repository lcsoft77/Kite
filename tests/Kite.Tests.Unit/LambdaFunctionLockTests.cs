using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class LambdaFunctionLockTests
{
    [Fact]
    public async Task FunctionLock_RestartBlocksWhileInvocationHoldsLock()
    {
        using var function = new LambdaFunction { Name = "test-fn" };

        // Simulate an invocation acquiring the lock
        await function.FunctionLock.WaitAsync();

        // Attempt a restart (acquire lock again) concurrently — it should block
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var restartAcquireTask = function.FunctionLock.WaitAsync(cts.Token);

        // Give it a moment; the lock should NOT yet be obtainable
        var completedEarly = await Task.WhenAny(restartAcquireTask, Task.Delay(50));
        completedEarly.Should().NotBe(restartAcquireTask, "restart must wait while an invocation holds the lock");

        // Release the invocation lock
        function.FunctionLock.Release();

        // Now the restart acquire should complete without throwing
        await restartAcquireTask;

        // Clean up
        function.FunctionLock.Release();
    }

    [Fact]
    public async Task FunctionLock_CancellationDoesNotLeaveLockAcquired()
    {
        using var function = new LambdaFunction { Name = "test-fn" };

        // Invocation holds the lock
        await function.FunctionLock.WaitAsync();

        using var cts = new CancellationTokenSource();

        // Start waiting while lock is held (waiter is queued)
        var waitTask = function.FunctionLock.WaitAsync(cts.Token);

        // Ensure the task has started waiting before cancelling
        await Task.Delay(50);

        // Cancel while the waiter is queued
        cts.Cancel();

        // WaitAsync must throw OperationCanceledException
        var act = async () => await waitTask;
        await act.Should().ThrowAsync<OperationCanceledException>(
            "WaitAsync should propagate cancellation");

        // Release the invocation lock
        function.FunctionLock.Release();

        // Semaphore must be back to 1 — not left permanently locked
        function.FunctionLock.CurrentCount.Should().Be(1,
            "the cancelled WaitAsync must not have decremented the semaphore count");
    }
}
