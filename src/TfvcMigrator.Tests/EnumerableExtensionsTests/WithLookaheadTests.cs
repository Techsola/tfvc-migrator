using NSubstitute;

namespace TfvcMigrator.Tests.EnumerableExtensionsTests;

public static class WithLookaheadTests
{
    [Test]
    public static async Task Current_value_should_not_become_available_before_MoveNextAsync_succeeds_asynchronously()
    {
        var tcs = new TaskCompletionSource<bool>();

        var inner = Substitute.For<IAsyncEnumerable<int>>();
        inner.GetAsyncEnumerator().Current.Returns(42);
        inner.GetAsyncEnumerator().MoveNextAsync().Returns(
            new ValueTask<bool>(tcs.Task),
            new ValueTask<bool>(false));

        var enumerator = inner.WithLookahead().GetAsyncEnumerator();

        enumerator.Current.ShouldBe(0);
        var moveNextTask = enumerator.MoveNextAsync();
        enumerator.Current.ShouldBe(0);

        tcs.SetResult(true);
        await moveNextTask;
    }

    [Test]
    public static async Task Current_value_should_become_available_when_MoveNextAsync_succeeds_synchronously()
    {
        var inner = Substitute.For<IAsyncEnumerable<int>>();
        inner.GetAsyncEnumerator().Current.Returns(42);
        inner.GetAsyncEnumerator().MoveNextAsync().Returns(
            new ValueTask<bool>(true),
            new ValueTask<bool>(false));

        var enumerator = inner.WithLookahead().GetAsyncEnumerator();

        await enumerator.MoveNextAsync();
        enumerator.Current.ShouldBe(42);
    }

    [Test]
    public static async Task Current_value_should_become_available_when_MoveNextAsync_succeeds_asynchronously()
    {
        var tcs = new TaskCompletionSource<bool>();

        var inner = Substitute.For<IAsyncEnumerable<int>>();
        inner.GetAsyncEnumerator().Current.Returns(42);
        inner.GetAsyncEnumerator().MoveNextAsync().Returns(
            new ValueTask<bool>(tcs.Task),
            new ValueTask<bool>(false));

        var enumerator = inner.WithLookahead().GetAsyncEnumerator();

        var moveNextTask = enumerator.MoveNextAsync();
        tcs.SetResult(true);
        await moveNextTask;

        enumerator.Current.ShouldBe(42);
    }

    [Test]
    public static async Task Immediate_second_MoveNextAsync_call_should_not_be_detected_as_overlapping()
    {
        var tcs = new TaskCompletionSource<bool>();

        var inner = Substitute.For<IAsyncEnumerable<int>>();
        inner.GetAsyncEnumerator().MoveNextAsync().Returns(
            new ValueTask<bool>(tcs.Task),
            new ValueTask<bool>(false));

        var enumerator = inner.WithLookahead().GetAsyncEnumerator();

        var awaitAssertionsTask = CallAgainImmediatelyAfterAwaiting();
        async Task CallAgainImmediatelyAfterAwaiting()
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();
            (await enumerator.MoveNextAsync()).ShouldBeFalse();
        }

        tcs.SetResult(true);

        await awaitAssertionsTask;
    }

    [Test]
    public static async Task MoveNextAsync_call_after_sync_fault_should_not_be_detected_as_overlapping()
    {
        var inner = Substitute.For<IAsyncEnumerable<int>>();
        inner.GetAsyncEnumerator().MoveNextAsync().Returns(
            new ValueTask<bool>(Task.FromException<bool>(new Exception())),
            new ValueTask<bool>(false));

        var enumerator = inner.WithLookahead().GetAsyncEnumerator();

        await Should.ThrowAsync<Exception>(enumerator.MoveNextAsync().AsTask());
        (await enumerator.MoveNextAsync()).ShouldBeFalse();
    }

    [Test]
    public static async Task MoveNextAsync_call_after_async_fault_should_not_be_detected_as_overlapping()
    {
        var tcs = new TaskCompletionSource<bool>();

        var inner = Substitute.For<IAsyncEnumerable<int>>();
        inner.GetAsyncEnumerator().MoveNextAsync().Returns(
            new ValueTask<bool>(tcs.Task),
            new ValueTask<bool>(false));

        var enumerator = inner.WithLookahead().GetAsyncEnumerator();

        var awaitAssertionsTask = CallAgainImmediatelyAfterAwaiting();
        async Task CallAgainImmediatelyAfterAwaiting()
        {
            await Should.ThrowAsync<Exception>(enumerator.MoveNextAsync().AsTask());
            (await enumerator.MoveNextAsync()).ShouldBeFalse();
        }

        tcs.SetException(new Exception());

        await awaitAssertionsTask;
    }
}
