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
    }
}
