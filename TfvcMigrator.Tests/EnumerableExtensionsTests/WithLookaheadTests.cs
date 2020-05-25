using System;
using NSubstitute;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TfvcMigrator.Tests.EnumerableExtensionsTests
{
    public static class WithLookaheadTests
    {
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
}
