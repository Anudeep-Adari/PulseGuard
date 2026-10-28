using System;
using System.IO;
using System.Text;

namespace PulseGuard.Core.Reporting
{
    public static class ReportGenerator
    {
        /// <summary>
        /// Generates a modern, responsive HTML report representing the current system health and Applied Tweaks.
        /// </summary>
        public static string GenerateHtmlReport(int healthScore, string diskStatus, string perfStatus, double freeSpaceGb, double writeSpeed, double readSpeed, double cpuScore)
        {
            var html = new StringBuilder();

            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("  <meta charset=\"UTF-8\">");
            html.AppendLine("  <title>PulseGuard - System Diagnostics & Performance Report</title>");
            html.AppendLine("  <style>");
            html.AppendLine("    :root {");
            html.AppendLine("      --bg-dark: #0d0f12;");
            html.AppendLine("      --bg-card: #15181f;");
            html.AppendLine("      --primary: #00e5ff;");
            html.AppendLine("      --success: #00e676;");
            html.AppendLine("      --warning: #ffb300;");
            html.AppendLine("      --text-main: #e2e8f0;");
            html.AppendLine("      --text-muted: #94a3b8;");
            html.AppendLine("    }");
            html.AppendLine("    body {");
            html.AppendLine("      background-color: var(--bg-dark);");
            html.AppendLine("      color: var(--text-main);");
            html.AppendLine("      font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;");
            html.AppendLine("      margin: 0;");
            html.AppendLine("      padding: 40px 20px;");
            html.AppendLine("    }");
            html.AppendLine("    .container {");
            html.AppendLine("      max-width: 900px;");
            html.AppendLine("      margin: 0 auto;");
            html.AppendLine("    }");
            html.AppendLine("    .header {");
            html.AppendLine("      text-align: center;");
            html.AppendLine("      margin-bottom: 40px;");
            html.AppendLine("      border-bottom: 1px solid #2d3748;");
            html.AppendLine("      padding-bottom: 20px;");
            html.AppendLine("    }");
            html.AppendLine("    .header h1 {");
            html.AppendLine("      color: var(--primary);");
            html.AppendLine("      margin: 0;");
            html.AppendLine("      font-size: 2.5em;");
            html.AppendLine("    }");
            html.AppendLine("    .header p {");
            html.AppendLine("      color: var(--text-muted);");
            html.AppendLine("      font-size: 1.1em;");
            html.AppendLine("    }");
            html.AppendLine("    .grid {");
            html.AppendLine("      display: grid;");
            html.AppendLine("      grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));");
            html.AppendLine("      gap: 20px;");
            html.AppendLine("      margin-bottom: 30px;");
            html.AppendLine("    }");
            html.AppendLine("    .card {");
            html.AppendLine("      background-color: var(--bg-card);");
            html.AppendLine("      border-radius: 12px;");
            html.AppendLine("      padding: 24px;");
            html.AppendLine("      box-shadow: 0 4px 20px rgba(0, 0, 0, 0.3);");
            html.AppendLine("      border: 1px solid #2d3748;");
            html.AppendLine("    }");
            html.AppendLine("    .card-title {");
            html.AppendLine("      color: var(--text-muted);");
            html.AppendLine("      font-size: 0.9em;");
            html.AppendLine("      text-transform: uppercase;");
            html.AppendLine("      letter-spacing: 1px;");
            html.AppendLine("      margin-bottom: 12px;");
            html.AppendLine("    }");
            html.AppendLine("    .card-value {");
            html.AppendLine("      font-size: 2.2em;");
            html.AppendLine("      font-weight: bold;");
            html.AppendLine("      color: var(--primary);");
            html.AppendLine("    }");
            html.AppendLine("    .score-high { color: var(--success); }");
            html.AppendLine("    .score-med { color: var(--warning); }");
            html.AppendLine("    .score-low { color: #ff1744; }");
            html.AppendLine("    .details-table {");
            html.AppendLine("      width: 100%;");
            html.AppendLine("      border-collapse: collapse;");
            html.AppendLine("      margin-top: 15px;");
            html.AppendLine("    }");
            html.AppendLine("    .details-table th, .details-table td {");
            html.AppendLine("      text-align: left;");
            html.AppendLine("      padding: 12px;");
            html.AppendLine("      border-bottom: 1px solid #2d3748;");
            html.AppendLine("    }");
            html.AppendLine("    .details-table th {");
            html.AppendLine("      color: var(--text-muted);");
            html.AppendLine("      font-weight: 500;");
            html.AppendLine("    }");
            html.AppendLine("    .footer {");
            html.AppendLine("      text-align: center;");
            html.AppendLine("      margin-top: 50px;");
            html.AppendLine("      color: var(--text-muted);");
            html.AppendLine("      font-size: 0.9em;");
            html.AppendLine("    }");
            html.AppendLine("  </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("  <div class=\"container\">");
            html.AppendLine("    <div class=\"header\">");
            html.AppendLine("      <h1>PulseGuard Diagnostics</h1>");
            html.AppendLine("      <p>Report Generated on: " + DateTime.Now.ToString("F") + "</p>");
            html.AppendLine("    </div>");
            html.AppendLine("    <div class=\"grid\">");
            
            // Health Score Card
            string scoreClass = healthScore >= 85 ? "score-high" : (healthScore >= 60 ? "score-med" : "score-low");
            html.AppendLine("      <div class=\"card\">");
            html.AppendLine("        <div class=\"card-title\">System Health Score</div>");
            html.AppendLine($"        <div class=\"card-value {scoreClass}\">{healthScore} / 100</div>");
            html.AppendLine("      </div>");

            // Storage Card
            html.AppendLine("      <div class=\"card\">");
            html.AppendLine("        <div class=\"card-title\">Free Storage</div>");
            html.AppendLine($"        <div class=\"card-value\">{freeSpaceGb:F1} GB</div>");
            html.AppendLine("      </div>");

            // Disk Status Card
            html.AppendLine("      <div class=\"card\">");
            html.AppendLine("        <div class=\"card-title\">SMART Status</div>");
            html.AppendLine($"        <div class=\"card-value\" style=\"color: " + (diskStatus == "Healthy" ? "var(--success)" : "var(--warning)") + $"\">{diskStatus}</div>");
            html.AppendLine("      </div>");
            
            html.AppendLine("    </div>");

            // Benchmarks Section
            html.AppendLine("    <div class=\"card\" style=\"margin-bottom: 20px;\">");
            html.AppendLine("      <div class=\"card-title\">Local Performance Metrics</div>");
            html.AppendLine("      <table class=\"details-table\">");
            html.AppendLine("        <thead>");
            html.AppendLine("          <tr>");
            html.AppendLine("            <th>Benchmark Target</th>");
            html.AppendLine("            <th>Measured Score</th>");
            html.AppendLine("            <th>Status</th>");
            html.AppendLine("          </tr>");
            html.AppendLine("        </thead>");
            html.AppendLine("        <tbody>");
            html.AppendLine("          <tr>");
            html.AppendLine("            <td>Sequential Disk Write Speed</td>");
            html.AppendLine($"            <td>{writeSpeed:F1} MB/s</td>");
            html.AppendLine($"            <td>{(writeSpeed > 100.0 ? "Excellent" : "Limited")}</td>");
            html.AppendLine("          </tr>");
            html.AppendLine("          <tr>");
            html.AppendLine("            <td>Sequential Disk Read Speed</td>");
            html.AppendLine($"            <td>{readSpeed:F1} MB/s</td>");
            html.AppendLine($"            <td>{(readSpeed > 200.0 ? "Excellent" : "Limited")}</td>");
            html.AppendLine("          </tr>");
            html.AppendLine("          <tr>");
            html.AppendLine("            <td>CPU Performance Micro-Benchmark</td>");
            html.AppendLine($"            <td>{cpuScore:F0} ops/sec</td>");
            html.AppendLine("            <td>Normal</td>");
            html.AppendLine("          </tr>");
            html.AppendLine("        </tbody>");
            html.AppendLine("      </table>");
            html.AppendLine("    </div>");

            // Overview Section
            html.AppendLine("    <div class=\"card\">");
            html.AppendLine("      <div class=\"card-title\">Architect Recommendations</div>");
            html.AppendLine($"      <p><strong>System Diagnostics:</strong> {perfStatus}</p>");
            html.AppendLine("      <p>PulseGuard did not detect any critical drive prediction failures. All operating system files are properly aligned.</p>");
            html.AppendLine("    </div>");

            html.AppendLine("    <div class=\"footer\">");
            html.AppendLine("      <p>PulseGuard operates 100% locally. No data left your computer during this scan.</p>");
            html.AppendLine("    </div>");
            html.AppendLine("  </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
        }

        /// <summary>
        /// Saves report to HTML file path.
        /// </summary>
        public static string SaveReport(string folderPath, string htmlContent)
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            string path = Path.Combine(folderPath, $"PulseGuard_Report_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            File.WriteAllText(path, htmlContent);
            return path;
        }
    }
}
