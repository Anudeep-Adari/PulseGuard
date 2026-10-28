using System;
using System.Management;

namespace PulseGuard.Safety
{
    public static class RestorePointManager
    {
        /// <summary>
        /// Creates a Windows System Restore Point.
        /// Requires Administrator privileges.
        /// </summary>
        /// <param name="description">The display name of the restore point.</param>
        /// <returns>True if created successfully, otherwise false.</returns>
        public static bool CreateRestorePoint(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("Description cannot be empty.", nameof(description));

            try
            {
                var scope = new ManagementScope(@"\root\default");
                scope.Connect();

                var path = new ManagementPath("SystemRestore");
                using var sysRestoreClass = new ManagementClass(scope, path, null);
                
                using var inParams = sysRestoreClass.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = description;
                inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
                inParams["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE

                using var outParams = sysRestoreClass.InvokeMethod("CreateRestorePoint", inParams, null);
                
                uint result = (uint)outParams["ReturnValue"];
                if (result == 0)
                {
                    Console.WriteLine($"System Restore Point created successfully: '{description}'");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Failed to create System Restore Point. Return value: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception while creating System Restore Point: {ex.Message}");
                return false;
            }
        }
    }
}
