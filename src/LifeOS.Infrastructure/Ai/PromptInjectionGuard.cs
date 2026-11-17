using System.Text.RegularExpressions;
using LifeOS.Application.Abstractions.Ai;

namespace LifeOS.Infrastructure.Ai;

/// <summary>
/// Neutralizes prompt-injection attempts in user-authored content (notes, journal entries, task
/// titles). This is defence in depth, not the primary control: the system prompt already states
/// that text inside &lt;user_data&gt; blocks is data. The guard exists so a successful injection
/// attempt is at least visible in the audit trail.
/// </summary>
public sealed partial class PromptInjectionGuard : IPromptInjectionGuard
{
    /// <summary>
    /// Patterns that indicate an attempt to override the system prompt or exfiltrate it. Kept
    /// deliberately narrow to avoid mangling legitimate Persian text.
    /// </summary>
    private static readonly (string Name, Regex Pattern)[] Patterns =
    [
        ("ignore-instructions", IgnoreInstructionsRegex()),
        ("ignore-instructions-fa", IgnoreInstructionsPersianRegex()),
        ("reveal-system-prompt", RevealPromptRegex()),
        ("reveal-system-prompt-fa", RevealPromptPersianRegex()),
        ("role-override", RoleOverrideRegex()),
        ("role-override-fa", RoleOverridePersianRegex()),
        ("delimiter-escape", DelimiterEscapeRegex()),
        ("tool-invocation", ToolInvocationRegex())
    ];

    public PromptInjectionScan Scan(string userContent)
    {
        if (string.IsNullOrWhiteSpace(userContent))
        {
            return new PromptInjectionScan(false, [], userContent ?? string.Empty);
        }

        var matched = new List<string>();
        var sanitized = userContent;

        foreach (var (name, pattern) in Patterns)
        {
            if (!pattern.IsMatch(sanitized))
            {
                continue;
            }

            matched.Add(name);
            sanitized = pattern.Replace(sanitized, "[حذف‌شده]");
        }

        // Neutralize any attempt to close the data block early. This is also reported as a match:
        // a user has no legitimate reason to emit the delimiter, so its presence is itself the
        // signal, even when the surrounding text looks harmless.
        var hadDelimiter =
            sanitized.Contains("</user_data>", StringComparison.OrdinalIgnoreCase)
            || sanitized.Contains("<user_data>", StringComparison.OrdinalIgnoreCase);

        if (hadDelimiter)
        {
            if (!matched.Contains("delimiter-escape"))
            {
                matched.Add("delimiter-escape");
            }

            sanitized = sanitized
                .Replace("</user_data>", "[حذف‌شده]", StringComparison.OrdinalIgnoreCase)
                .Replace("<user_data>", "[حذف‌شده]", StringComparison.OrdinalIgnoreCase);
        }

        return new PromptInjectionScan(matched.Count > 0, matched, sanitized);
    }

    [GeneratedRegex(
        @"\b(ignore|disregard|forget)\b[^.\n]{0,40}\b(previous|prior|above|earlier|all)\b[^.\n]{0,40}\b(instruction|prompt|rule|direction)s?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IgnoreInstructionsRegex();

    /// <summary>
    /// Persian equivalent of <see cref="IgnoreInstructionsRegex"/>. The UI is Persian, so an
    /// English-only guard would miss the most likely real attempt.
    /// </summary>
    [GeneratedRegex(
        @"(دستور|دستورات|دستورها|قوانین|قاعده|پرامپت)[^\n.]{0,40}(نادیده|فراموش)|(نادیده|فراموش)[^\n.]{0,40}(دستور|قوانین|قاعده|پرامپت)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IgnoreInstructionsPersianRegex();

    [GeneratedRegex(
        @"\b(system\s*prompt|initial\s*instruction|your\s*instructions|repeat\s+(the\s+)?(text|prompt)\s+above)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RevealPromptRegex();

    [GeneratedRegex(
        @"(پرامپت|دستور|متن)\s*(سیستم|اولیه|اصلی|بالا|قبلی)\s*(را)?\s*(تکرار|بازگو|نشان|چاپ)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RevealPromptPersianRegex();

    [GeneratedRegex(
        @"\b(you\s+are\s+now|from\s+now\s+on\s+you|act\s+as\s+(a\s+)?(system|admin|developer)|new\s+role\s*:)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleOverrideRegex();

    [GeneratedRegex(
        @"از\s*این\s*به\s*بعد\s*(تو|شما)|نقش\s*(جدید|دیگر)\s*(را)?\s*(بپذیر|بگیر)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleOverridePersianRegex();

    [GeneratedRegex(
        @"(^|\n)\s*(###\s*(system|assistant)|<\|\s*(system|im_start|im_end)\s*\|>)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DelimiterEscapeRegex();

    [GeneratedRegex(
        @"\b(call|invoke|execute)\s+(the\s+)?(tool|function|command)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToolInvocationRegex();
}
