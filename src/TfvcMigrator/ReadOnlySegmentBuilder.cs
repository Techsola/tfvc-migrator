using System.Buffers;

namespace TfvcMigrator
{
    internal struct ReadOnlySegmentBuilder<T>
    {
        private Segment? startSegment, endSegment;

        public void Add(ReadOnlyMemory<T> memory)
        {
            if (endSegment is null)
            {
                startSegment = endSegment = new(memory, 0);
            }
            else
            {
                var next = new Segment(memory, endSegment.RunningIndex + 1);
                endSegment.SetNext(next);
                endSegment = next;
            }
        }

        public ReadOnlySequence<T> Build()
        {
            return startSegment is null
                ? ReadOnlySequence<T>.Empty
                : new ReadOnlySequence<T>(startSegment, startIndex: 0, endSegment!, endIndex: endSegment!.Memory.Length);
        }

        private sealed class Segment : ReadOnlySequenceSegment<T>
        {
            public Segment(ReadOnlyMemory<T> memory, long runningIndex)
            {
                Memory = memory;
                RunningIndex = runningIndex;
            }

            public void SetNext(ReadOnlySequenceSegment<T>? next)
            {
                Next = next;
            }
        }
    }
}
