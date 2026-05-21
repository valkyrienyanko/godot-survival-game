using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Godot;

/// <summary>
/// Lightweight profiling helper that aggregates timings per call site.
/// </summary>
public sealed class MyProfiler
{
    private static readonly Dictionary<string, Entry> _entries = [];
    private static readonly Formatter _formatter = new();
    private static readonly bool _logEverythingInMs;

    /// <summary>
    /// Starts timing for a call site and creates its entry on first use.
    /// </summary>
    public static void Begin(string id = "",
        [CallerFilePath] string filePath = "",
        [CallerMemberName] string methodName = "")
    {
        string internalId = id + filePath + methodName;
        // Create a new entry the first time this call site is seen.
        if (!_entries.TryGetValue(internalId, out Entry entry))
        {
            Entry newEntry = new(filePath, methodName, id);
            _entries[internalId] = newEntry;
            newEntry.Restart();
            return;
        }
        entry.Restart();
    }

    /// <summary>
    /// Stops timing for a call site if it is being tracked.
    /// </summary>
    public static void End(string id = "",
        [CallerFilePath] string filePath = "",
        [CallerMemberName] string methodName = "")
    {
        string internalId = id + filePath + methodName;
        // Only stop timers that have been started for this call site.
        if (_entries.TryGetValue(internalId, out Entry entry))
        {
            entry.Stop();
        }
    }

    /// <summary>
    /// Prints the formatted profiling summary to the Godot console.
    /// </summary>
    public static void Summary()
    {
        GD.Print(_formatter.BuildSummary(_entries.Values));
    }

    /// <summary>
    /// Stores timing samples for a single call site.
    /// </summary>
    private sealed class Entry(string filePath, string methodName, string id)
    {
        public string FileName { get; } = Path.GetFileName(filePath).Replace(".cs", "");
        public string MethodName { get; } = methodName;
        public string Id { get; } = id;

        private readonly Stopwatch _stopwatch = new();
        private readonly List<double> _times = new();

        /// <summary>
        /// Restarts the underlying stopwatch for this entry.
        /// </summary>
        public void Restart() => _stopwatch.Restart();

        /// <summary>
        /// Stops the stopwatch and records the elapsed time in microseconds.
        /// </summary>
        public void Stop()
        {
            _stopwatch.Stop();
            _times.Add(_stopwatch.Elapsed.TotalMicroseconds);
        }

        /// <summary>
        /// Returns the number of samples recorded.
        /// </summary>
        public int GetCount() => _times.Count;

        /// <summary>
        /// Returns the average time in microseconds.
        /// </summary>
        public double GetAvg() => _times.Count > 0 ? _times.Average() : 0;

        /// <summary>
        /// Returns the minimum time in microseconds.
        /// </summary>
        public double GetMin() => _times.Count > 0 ? _times.Min() : 0;

        /// <summary>
        /// Returns the maximum time in microseconds.
        /// </summary>
        public double GetMax() => _times.Count > 0 ? _times.Max() : 0;

        /// <summary>
        /// Returns the total time in microseconds.
        /// </summary>
        public double GetTotal() => _times.Sum();
    }

    /// <summary>
    /// Formats profiling results into a readable table.
    /// </summary>
    private sealed class Formatter
    {
        // Width of the method-name column inside the table borders, in characters.
        private const int MethodColumnWidth = 37;
        // Width of each Avg/Min/Max column inside the table borders, in characters.
        private const int MetricColumnWidth = 9;
        // Width of the Total column inside the table borders, in characters.
        private const int TotalColumnWidth = 11;
        // Number of leading spaces added before method names in the method column.
        private const int MethodIndentSpaces = 4;

        /// <summary>
        /// Groups entries by file and captures an average time used for ordering.
        /// </summary>
        private sealed class SummaryGroup(
            string fileName, 
            List<Entry> methods, 
            double averageTime)
        {
            public string FileName { get; } = fileName;
            public List<Entry> Methods { get; } = methods;
            public double AverageTime { get; } = averageTime;
        }

        /// <summary>
        /// Builds a formatted table summary for the given entries.
        /// </summary>
        public string BuildSummary(IEnumerable<Entry> entries)
        {
            List<Entry> entryList = entries.ToList();

            List<SummaryGroup> groupedEntries = entryList
                .GroupBy(e => e.FileName)
                .Select(g =>
                {
                    List<Entry> methods = g.OrderByDescending(m => m.GetAvg()).ToList();
                    double totalTime = g.Sum(m => m.GetTotal());
                    int totalSamples = g.Sum(m => m.GetCount());
                    double averageTime = totalSamples > 0 ? totalTime / totalSamples : 0;
                    return new SummaryGroup(g.Key, methods, averageTime);
                })
                .OrderByDescending(g => g.AverageTime)
                .ToList();

            // Skip output when no entries have been recorded.
            if (groupedEntries.Count == 0)
            {
                return string.Empty;
            }

            int avgWidth = GetColumnWidth("Avg", MetricColumnWidth, entryList, m => FormatTime(m.GetAvg()));
            int minWidth = GetColumnWidth("Min", MetricColumnWidth, entryList, m => FormatTime(m.GetMin()));
            int maxWidth = GetColumnWidth("Max", MetricColumnWidth, entryList, m => FormatTime(m.GetMax()));
            int totalWidth = GetColumnWidth("Total", TotalColumnWidth, entryList, m => FormatTotal(m.GetTotal()));

            StringBuilder sb = new();

            foreach (var group in groupedEntries)
            {
                AppendRow(sb, "┌", "┬", "┐",
                    BuildSectionTitleCell(group.FileName, MethodColumnWidth),
                    BuildHeaderCell("Avg", avgWidth),
                    BuildHeaderCell("Min", minWidth),
                    BuildHeaderCell("Max", maxWidth),
                    BuildHeaderCell("Total", totalWidth));

                foreach (var m in group.Methods)
                {
                    AppendRow(sb, "│", "│", "│",
                        BuildMethodCell(m),
                        BuildValueCell(FormatTime(m.GetAvg()), avgWidth),
                        BuildValueCell(FormatTime(m.GetMin()), minWidth),
                        BuildValueCell(FormatTime(m.GetMax()), maxWidth),
                        BuildValueCell(FormatTotal(m.GetTotal()), totalWidth));
                }

                AppendRow(sb, "└", "┴", "┘",
                    BuildSeparatorCell(MethodColumnWidth),
                    BuildSeparatorCell(avgWidth),
                    BuildSeparatorCell(minWidth),
                    BuildSeparatorCell(maxWidth),
                    BuildSeparatorCell(totalWidth));

                if (!ReferenceEquals(group, groupedEntries[^1]))
                {
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Appends a table row with borders and cell contents.
        /// </summary>
        private static void AppendRow(StringBuilder sb, string left, string middle, string right, params string[] cells)
        {
            sb.Append(left);
            for (int i = 0; i < cells.Length; i++)
            {
                sb.Append(cells[i]);
                sb.Append(i == cells.Length - 1 ? right : middle);
            }
            sb.AppendLine();
        }

        /// <summary>
        /// Builds the section title cell for a file group.
        /// </summary>
        private static string BuildSectionTitleCell(string title, int width)
        {
            // Avoid building cells with non-positive width.
            if (width <= 0)
                return string.Empty;

            string prefix = $"── {title} ";
            
            // Clamp the title to the column width when it would overflow.
            if (prefix.Length >= width)
            {
                return prefix.Length > width ? prefix[..width] : prefix;
            }

            return prefix + new string('─', width - prefix.Length);
        }

        /// <summary>
        /// Builds a centered header cell for a column.
        /// </summary>
        private static string BuildHeaderCell(string title, int width)
        {
            // Avoid building cells with non-positive width.
            if (width <= 0)
                return string.Empty;

            string label = $" {title} ";
            // Clamp the header to the column width when it would overflow.
            if (label.Length >= width)
            {
                return label.Length > width ? label[..width] : label;
            }

            int dashCount = width - label.Length;
            int left = dashCount / 2;
            int right = dashCount - left;
            return new string('─', left) + label + new string('─', right);
        }

        /// <summary>
        /// Builds a separator cell filled with horizontal rules.
        /// </summary>
        private static string BuildSeparatorCell(int width) => width > 0 ? new string('─', width) : string.Empty;

        /// <summary>
        /// Builds the method-name cell with indentation and id suffix.
        /// </summary>
        private static string BuildMethodCell(Entry entry)
        {
            int indent = Math.Min(MethodIndentSpaces, MethodColumnWidth);
            int nameWidth = Math.Max(0, MethodColumnWidth - indent);
            string suffix = string.IsNullOrWhiteSpace(entry.Id) ? string.Empty : $" [{entry.Id}]";
            string displayName = entry.MethodName + suffix;
            string name = displayName.Length > nameWidth ? displayName[..nameWidth] : displayName;
            return new string(' ', indent) + name.PadRight(nameWidth);
        }

        /// <summary>
        /// Determines a column width that fits the header and all formatted values.
        /// </summary>
        private static int GetColumnWidth(string header, int minWidth, IEnumerable<Entry> methods, Func<Entry, string> formatter)
        {
            int width = Math.Max(minWidth, $" {header} ".Length);
            foreach (Entry method in methods)
            {
                string value = formatter(method);
                int required = value.Length + 2;
                if (required > width)
                {
                    width = required;
                }
            }
            return width;
        }

        /// <summary>
        /// Builds a centered value cell with padding.
        /// </summary>
        private static string BuildValueCell(string value, int width)
        {
            // Avoid building cells with non-positive width.
            if (width <= 0)
                return string.Empty;

            int innerWidth = Math.Max(0, width - 2);
            // Clamp the value to the column width when it would overflow.
            string trimmed = value.Length > innerWidth ? value[..innerWidth] : value;
            return " " + trimmed.PadRight(innerWidth) + " ";
        }

        /// <summary>
        /// Formats a single time value in ms/us/ns based on the configured unit mode.
        /// </summary>
        private static string FormatTime(double us)
        {
            return FormatValue(us, includeUnitPadding: true);
        }

        /// <summary>
        /// Formats a total time value in ms/us/ns with aligned units.
        /// </summary>
        private static string FormatTotal(double us)
        {
            return FormatValue(us, includeUnitPadding: true);
        }

        /// <summary>
        /// Formats a time value in ms/us/ns based on the configured unit mode.
        /// </summary>
        private static string FormatValue(double us, bool includeUnitPadding)
        {
            string spacer = includeUnitPadding ? " " : string.Empty;
            if (_logEverythingInMs)
            {
                return $"{us / 1000:F1}{spacer}ms";
            }

            if (us >= 1000)
            {
                return $"{us / 1000:F1}{spacer}ms";
            }

            if (us >= 1)
            {
                return $"{us:F1}{spacer}us";
            }

            return $"{us * 1000:F1}{spacer}ns";
        }
    }
}
