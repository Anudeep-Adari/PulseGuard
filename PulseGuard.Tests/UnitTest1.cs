using System;
using Xunit;
using PulseGuard.Safety;

namespace PulseGuard.Tests
{
    public class WhitelistGuardTests
    {
        [Fact]
        public void TestCriticalServicesBlocked()
        {
            // Verify critical infrastructure services cannot be modified
            Assert.False(WhitelistGuard.IsServiceModificationAllowed("Winmgmt"));
            Assert.False(WhitelistGuard.IsServiceModificationAllowed("EventLog"));
            Assert.False(WhitelistGuard.IsServiceModificationAllowed("RpcSs"));
            Assert.False(WhitelistGuard.IsServiceModificationAllowed("WinDefend"));
        }

        [Fact]
        public void TestNonCriticalServicesAllowed()
        {
            // Verify standard user-facing or telemetry services are optimizeable
            Assert.True(WhitelistGuard.IsServiceModificationAllowed("SysMain"));
            Assert.True(WhitelistGuard.IsServiceModificationAllowed("DiagTrack"));
            Assert.True(WhitelistGuard.IsServiceModificationAllowed("PrintSpooler"));
        }

        [Fact]
        public void TestSystemFoldersProtected()
        {
            // Verify operating system files cannot be modified
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string system32 = System.IO.Path.Combine(winDir, "System32");
            string testFile = System.IO.Path.Combine(system32, "drivers\\etc\\hosts");

            Assert.False(WhitelistGuard.IsPathModificationAllowed(system32));
            Assert.False(WhitelistGuard.IsPathModificationAllowed(testFile));
            Assert.False(WhitelistGuard.IsPathModificationAllowed(winDir));
        }

        [Fact]
        public void TestTempFoldersAllowed()
        {
            // Verify system temp path modifications are permitted
            string tempPath = System.IO.Path.GetTempPath();
            string testTempFile = System.IO.Path.Combine(tempPath, "pulseguard_test.tmp");

            Assert.True(WhitelistGuard.IsPathModificationAllowed(testTempFile));
        }
    }
}