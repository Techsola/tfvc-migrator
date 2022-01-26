using System;
using System.Buffers;
using System.IO;

namespace TfvcMigrator
{
    internal sealed class CrlfRenormalizingStream : Stream
    {
        private readonly ReadOnlyMemory<byte> crlfBytes;
        private readonly int crLength;
        private ReadOnlySequence<byte> remaining;
        private ReadOnlySequence<byte> unreadLine;

        public CrlfRenormalizingStream(UnmanagedMemoryStream source, ReadOnlyMemory<byte> crlfBytes, int crLength)
        {
            this.crlfBytes = crlfBytes;
            this.crLength = crLength;
            remaining = source.GetReadOnlySequence();
        }

        public override int Read(Span<byte> buffer)
        {
            var bytesRead = 0;

            while (true)
            {
                while (unreadLine.IsEmpty)
                {
                    if (remaining.IsEmpty) return bytesRead;

                    var reader = new SequenceReader<byte>(remaining);
                    if (reader.TryReadTo(out unreadLine, crlfBytes.Span, advancePastDelimiter: false))
                    {
                        reader.Advance(crLength);
                        remaining = reader.UnreadSequence;
                    }
                    else
                    {
                        unreadLine = reader.UnreadSequence;
                        remaining = ReadOnlySequence<byte>.Empty;
                    }
                }

                if (unreadLine.Length < buffer.Length)
                {
                    bytesRead += unchecked((int)unreadLine.Length);
                    unreadLine.CopyTo(buffer);
                    buffer = buffer[unchecked((int)unreadLine.Length)..];
                    unreadLine = ReadOnlySequence<byte>.Empty;
                }
                else
                {
                    bytesRead += buffer.Length;
                    unreadLine.Slice(0, buffer.Length).CopyTo(buffer);
                    unreadLine = unreadLine.Slice(buffer.Length);
                    return bytesRead;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
