using System;
using System.Collections.Generic;
using System.IO;

namespace Palletes.Models
{
    public sealed class ValidationReport
    {
    public int ErrorCount => Errors.Count;
    public List<string> Errors { get; } = new();

    public string? LogFilePath { get; }

    public ValidationReport(string? logFilePath = null)
    {
        LogFilePath = logFilePath;
        if (!string.IsNullOrWhiteSpace(LogFilePath))
        {
            File.WriteAllText(LogFilePath!, "");
        }
    }

    public void AddError(string message)
    {
        Errors.Add(message);

        if (!string.IsNullOrWhiteSpace(LogFilePath))
        {
            File.AppendAllText(LogFilePath!, message + Environment.NewLine);
        }
    }
    }
}
