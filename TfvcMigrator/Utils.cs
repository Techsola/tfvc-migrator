using System;
using System.Buffers;
using System.IO;
using System.Threading;

namespace TfvcMigrator
{
    public static class Utils
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

        public static bool ContainsCrlf(Stream stream, out ReadOnlyMemory<byte> crlfBytes, out int crLength)
        {
            using var reader = new StreamReader(stream, leaveOpen: true);

            while (true)
            {
                var c = reader.Read();
                while (c == '\r')
                {
                    c = reader.Read();
                    if (c == '\n')
                    {
                        crlfBytes = reader.CurrentEncoding.GetBytes("\r\n");
                        crLength = reader.CurrentEncoding.GetByteCount("\r");
                        return true;
                    }
                }
                if (c == -1) break;
            }

            crlfBytes = default;
            crLength = default;
            return false;
        }

        public static Stream? RenormalizeCrlfIfNeeded(UnmanagedMemoryStream stream)
        {
            return ContainsCrlf(stream, out var crlfBytes, out var crLength)
                ? new CrlfRenormalizingStream(stream, crlfBytes, crLength)
                : null;
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

#pragma warning disable IDE0052 // Remove unread private members
            private readonly object? keepAlive;
#pragma warning restore IDE0052

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
