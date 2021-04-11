using NUnit.Framework;
using Shouldly;
using System.IO;

namespace TfvcMigrator.Tests.UtilsTests
{
    public static class ContainsCrlfTests
    {
        private const byte CR = (byte)'\r', LF = (byte)'\n';
        
        [Test]
        public static void False_for_only_CR()
        {
            using var stream = new MemoryStream(new[] { CR, CR });

            Utils.ContainsCrlf(stream, out _, out _).ShouldBeFalse();
        }

        [Test]
        public static void False_for_only_LF()
        {
            using var stream = new MemoryStream(new[] { LF, LF });

            Utils.ContainsCrlf(stream, out _, out _).ShouldBeFalse();
        }

        [Test]
        public static void Detects_CRLF_following_CR()
        {
            using var stream = new MemoryStream(new[] { CR, CR, LF });

            Utils.ContainsCrlf(stream, out _, out _).ShouldBeTrue();
        }
    }
}
