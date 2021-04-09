using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace TfvcMigrator
{
    internal static class Utils
    {
        public static T InterlockedUpdate<T, TState>(ref T location, TState state, Func<T, TState, T> applyUpdate)
            where T : class?
        {
            for (var comparand = Volatile.Read(ref location);;)
            {
                var value = applyUpdate(comparand, state);
                var result = Interlocked.CompareExchange(ref location, value, comparand);
                if (result == comparand) return value;
                comparand = result;
            }
        }

        public static bool ContainsCrlf(
            Stream stream,
            [NotNullWhen(true)] out ReadOnlySpan<byte> crlfBytes,
            out int crLength)
        {
            using var reader = new StreamReader(stream, leaveOpen: true);

            while (true)
            {
                var c = reader.Read();
                if (c == -1) break;
                if (c == '\r')
                {
                    c = reader.Read();
                    if (c == -1) break;
                    if (c == '\n')
                    {
                        crlfBytes = reader.CurrentEncoding.GetBytes("\r\n");
                        crLength = reader.CurrentEncoding.GetByteCount("\r");
                        return true;
                    }
                }
            }

            crlfBytes = default;
            crLength = default;
            return false;
        }

        public static MemoryStream? RenormalizeCrlfIfNeeded(this UnmanagedMemoryStream stream)
        {
            if (!ContainsCrlf(stream, out var crlfBytes, out var crLength)) return null;

            if (stream.Length > int.MaxValue)
                throw new NotImplementedException("Renormalizing line endings in text files larger than 2 GB is not yet implemented.");

            var renormalizedStream = new MemoryStream(unchecked((int)stream.Length));

            var reader = new SequenceReader<byte>(stream.GetReadOnlySequence());

            while (reader.TryReadTo(out ReadOnlySequence<byte> line, crlfBytes, advancePastDelimiter: false))
            {
                foreach (var memory in line)
                    renormalizedStream.Write(memory.Span);

                reader.Advance(crLength);
            }

            foreach (var memory in reader.UnreadSequence)
                renormalizedStream.Write(memory.Span);

            renormalizedStream.Position = 0;

            return renormalizedStream;
        }

        public static ReadOnlySequence<byte> GetReadOnlySequence(this UnmanagedMemoryStream stream)
        {
            unsafe
            {
                if (stream.Length == 0) return ReadOnlySequence<byte>.Empty;

                var builder = new ReadOnlySegmentBuilder<byte>();

                var currentStart = stream.PositionPointer - stream.Position;
                var end = currentStart + stream.Length;
                for (; (end - currentStart) > int.MaxValue; currentStart += int.MaxValue)
                {
                    builder.Add(new UnmanagedMemoryManager(currentStart, int.MaxValue, keepAlive: stream).Memory);
                }

                builder.Add(new UnmanagedMemoryManager(currentStart, unchecked((int)(end - currentStart)), keepAlive: stream).Memory);

                return builder.Build();
            }
        }

        private sealed class UnmanagedMemoryManager : MemoryManager<byte>
        {
            private readonly unsafe byte* pointer;
            private readonly int length;
            private readonly object? keepAlive;

            public unsafe UnmanagedMemoryManager(byte* pointer, int length, object? keepAlive)
            {
                this.pointer = pointer;
                this.length = length;
                this.keepAlive = keepAlive;
            }

            public override Span<byte> GetSpan()
            {
                unsafe { return new(pointer, length); }
            }

            public override MemoryHandle Pin(int elementIndex = 0) => default;

            public override void Unpin() { }

            protected override void Dispose(bool disposing) { }
        }
    }
}
