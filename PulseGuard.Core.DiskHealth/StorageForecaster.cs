using System;
using System.IO;
using System.Linq;
using PulseGuard.Data;

namespace PulseGuard.Core.DiskHealth
{
    public static class StorageForecaster
    {
        public class ForecastResult
        {
            public string DriveLetter { get; set; }
            public double TotalSizeGb { get; set; }
            public double FreeSpaceGb { get; set; }
            public double DailyUsageGb { get; set; } // Negative indicates freeing up space, positive is consumption
            public int DaysRemaining { get; set; }  // -1 if infinite/stable
            public DateTime? EstimatedDepletionDate { get; set; }
        }

        /// <summary>
        /// Forecasts space depletion on a drive based on local drive info and historical consumption logs.
        /// </summary>
        public static ForecastResult ForecastCapacity(string driveLetter)
        {
            string driveRoot = driveLetter.Trim().Substring(0, 1) + ":\\";
            var driveInfo = new DriveInfo(driveRoot);
            
            double totalSizeGb = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
            double freeSpaceGb = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);

            var result = new ForecastResult
            {
                DriveLetter = driveLetter.Trim().Substring(0, 1).ToUpper(),
                TotalSizeGb = totalSizeGb,
                FreeSpaceGb = freeSpaceGb,
                DailyUsageGb = 0.0,
                DaysRemaining = -1,
                EstimatedDepletionDate = null
            };

            try
            {
                var history = DatabaseManager.GetHealthScoreHistory();
                if (history == null || history.Count < 3)
                {
                    // Insufficient data points for regression, return current stats
                    return result;
                }

                // Collect data points: (x = Days from first record, y = FreeSpaceGb)
                // In a production scenario, we record drive space per drive.
                // Here we map history items to daily trend logs.
                var dataPoints = history
                    .OrderBy(h => h.Timestamp)
                    .Select(h => {
                        DateTime.TryParse(h.Timestamp, out var dt);
                        return new { Date = dt, CleanupSize = h.CleanupSizeMb };
                    })
                    .ToList();

                var firstDate = dataPoints[0].Date;
                var regressionData = dataPoints.Select(d => new
                {
                    X = (d.Date - firstDate).TotalDays,
                    Y = freeSpaceGb - (d.CleanupSize / 1024.0) // Adjust baseline usage
                }).ToList();

                int n = regressionData.Count;
                double sumX = regressionData.Sum(d => d.X);
                double sumY = regressionData.Sum(d => d.Y);
                double sumXY = regressionData.Sum(d => d.X * d.Y);
                double sumXX = regressionData.Sum(d => d.X * d.X);

                // Formula for slope (m) of linear regression: y = mx + c
                double denominator = (n * sumXX) - (sumX * sumX);
                if (Math.Abs(denominator) < 0.0001)
                {
                    return result; // Flat slope
                }

                double slope = ((n * sumXY) - (sumX * sumY)) / denominator;

                // If slope is negative, free space is decreasing (disk is filling up)
                if (slope < 0)
                {
                    double dailyRateGb = -slope; // rate of free space loss per day
                    result.DailyUsageGb = dailyRateGb;

                    double daysLeft = freeSpaceGb / dailyRateGb;
                    result.DaysRemaining = (int)Math.Ceiling(daysLeft);
                    result.EstimatedDepletionDate = DateTime.Now.AddDays(daysLeft);
                }
                else
                {
                    result.DailyUsageGb = -slope; // space is growing/stable
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Capacity forecasting failed: {ex.Message}");
            }

            return result;
        }
    }
}
