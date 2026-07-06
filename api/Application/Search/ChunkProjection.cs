using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EmployeeManager.Domain.Entities;
using EmployeeManager.Domain.Enums;

namespace EmployeeManager.Application.Search;

/// <summary>
/// Renders an employee's free-text career narrative into the set of chunks that semantic roster
/// search embeds: one chunk per work <see cref="Experience"/> (role + company + dates header, then
/// its summary and achievements), plus one chunk for the employee's professional summary.
///
/// <para>Pure and deterministic: the same employee always renders the same text and the same
/// SHA-256 <c>ContentHash</c>, which is what lets the reconciler detect real changes.</para>
/// </summary>
public static class ChunkProjection
{
    /// <summary>
    /// Project an employee into its desired chunks. Requires <see cref="Employee.Experiences"/>
    /// (with their achievements) to be loaded.
    /// </summary>
    public static IReadOnlyList<DesiredChunk> Project(Employee employee)
    {
        var chunks = new List<DesiredChunk>();

        if (!string.IsNullOrWhiteSpace(employee.Summary))
        {
            chunks.Add(Make(employee.Id, SearchChunkSource.Summary, employee.Id, employee.Summary!.Trim()));
        }

        foreach (var experience in employee.Experiences)
        {
            chunks.Add(Make(employee.Id, SearchChunkSource.Experience, experience.Id, RenderExperience(experience)));
        }

        return chunks;
    }

    /// <summary>SHA-256 (lowercase hex) of the content — the reconciler's change signal.</summary>
    public static string Hash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }

    private static DesiredChunk Make(Guid employeeId, SearchChunkSource type, Guid sourceId, string content)
        => new(employeeId, type, sourceId, content, Hash(content));

    private static string RenderExperience(Experience experience)
    {
        var sb = new StringBuilder();

        // Header: "Title @ Company (2019-03–present)" — anchors the narrative to role, org, dates.
        sb.Append(experience.Title).Append(" @ ").Append(experience.Company);
        sb.Append(" (").Append(FormatMonth(experience.StartDate)).Append('–');
        sb.Append(experience.EndDate is { } end ? FormatMonth(end) : "present").Append(')');

        if (!string.IsNullOrWhiteSpace(experience.Summary))
        {
            sb.Append('\n').Append(experience.Summary!.Trim());
        }

        // Achievements in display order, one bullet per line.
        foreach (var achievement in experience.Achievements.OrderBy(a => a.Order))
        {
            if (!string.IsNullOrWhiteSpace(achievement.Text))
            {
                sb.Append("\n- ").Append(achievement.Text.Trim());
            }
        }

        return sb.ToString();
    }

    private static string FormatMonth(DateOnly date)
        => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
