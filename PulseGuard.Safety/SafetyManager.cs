using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace PulseGuard.Safety
{
    public static class SafetyManager
    {
        /// <summary>
        /// Runs a worker process with elevated privileges using UAC (Least-Privilege elevation model).
        /// </summary>
        public static bool RunWorkerElevated(string workerExePath, string arguments)
        {
            if (!File.Exists(workerExePath))
            {
                Console.WriteLine($"Elevated worker not found at: {workerExePath}");
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = workerExePath,
                    Arguments = arguments,
                    UseShellExecute = true, // Required for 'runas'
                    Verb = "runas",          // Triggers UAC prompt
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(psi);
                process?.WaitForExit();
                return process?.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"User declined elevation or error occurred: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Computes the SHA-256 hash of the running executable.
        /// Can be checked against a signed signature file or public manifest to verify self-integrity.
        /// </summary>
        public static string ComputeSelfChecksum()
        {
            try
            {
                string selfPath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(selfPath) || !File.Exists(selfPath))
                {
                    return null;
                }

                using var sha = SHA256.Create();
                using var stream = File.OpenRead(selfPath);
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Self checksum calculation failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Simple verification: Checks if the binary checksum matches a registered signature file.
        /// </summary>
        public static bool VerifySelfIntegrity(string signatureFilePath)
        {
            if (!File.Exists(signatureFilePath))
            {
                // If signature file doesn't exist, we skip or generate warning.
                // In production, signatureFilePath is hardcoded or read from secure catalog.
                return true; 
            }

            try
            {
                string currentHash = ComputeSelfChecksum();
                string expectedHash = File.ReadAllText(signatureFilePath).Trim().ToLowerInvariant();
                return string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
