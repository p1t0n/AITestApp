using System.Text.Json;

namespace ExpertToJob.Agents.Agents;

/// <summary>One vetted interview question. <see cref="Evidence"/> is non-null only when the
/// model's quote was verified verbatim against the captured CV — checked, never trusted.</summary>
public sealed record InterviewQuestionItem(string Question, string? Probes, string? Evidence);

/// <summary>The pinned POST /agents/interview-kit response: turn 1's markdown kit verbatim, plus
/// the vetted structured questions (empty on the degrade path — the kit still ships).</summary>
public sealed record InterviewKitResponse(string Answer, IReadOnlyList<InterviewQuestionItem> Questions);

/// <summary>
/// Endpoint-side composition of the interview-kit response. The answer is turn 1's markdown
/// verbatim; the structured questions come from the model's turn-2 JSON, with every evidence
/// claim validated against the captured cv_get result: a quote that does not appear verbatim
/// (modulo whitespace and case) in the CV corpus is dropped from the question — the question
/// itself survives, its evidence claim does not. Corruption always degrades to fewer/leaner
/// questions, never to a failed request or fabricated evidence.
/// </summary>
public static class InterviewKitComposer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static InterviewKitResponse Compose(InterviewKitOutcome outcome, ILogger logger)
        => new(outcome.Reply.Text, ComposeQuestions(outcome, logger));

    private static List<InterviewQuestionItem> ComposeQuestions(InterviewKitOutcome outcome, ILogger logger)
    {
        var entries = TryParseEntries(outcome.QuestionsText);
        if (entries is null)
        {
            return [];
        }

        var corpus = BuildCorpus(outcome.Cv);
        var questions = new List<InterviewQuestionItem>();
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Question))
            {
                continue;
            }

            var evidence = Normalize(entry.Evidence);
            if (evidence is not null && !EvidenceExists(evidence, corpus))
            {
                logger.LogWarning(
                    "Dropping unverifiable interview-kit evidence quote ({Length} chars); the question ships without it.",
                    evidence.Length);
                evidence = null;
            }

            questions.Add(new InterviewQuestionItem(
                entry.Question.Trim(),
                Normalize(entry.Probes),
                evidence));
        }

        return questions;
    }

    /// <summary>Every CV text an evidence quote may legitimately come from: the professional
    /// summary, each experience summary, and each achievement bullet.</summary>
    private static List<string> BuildCorpus(InterviewCvPayload? cv)
    {
        if (cv is null)
        {
            return [];
        }

        var corpus = new List<string>();
        if (!string.IsNullOrWhiteSpace(cv.Summary))
        {
            corpus.Add(cv.Summary!);
        }

        foreach (var experience in cv.Experiences)
        {
            if (!string.IsNullOrWhiteSpace(experience.Summary))
            {
                corpus.Add(experience.Summary!);
            }

            corpus.AddRange(experience.Achievements.Select(a => a.Text));
        }

        return corpus;
    }

    /// <summary>Verbatim membership modulo whitespace runs and case: the quote must appear inside
    /// one corpus text. Paraphrases fail; exact bullets and sub-spans of them pass.</summary>
    private static bool EvidenceExists(string evidence, IReadOnlyList<string> corpus)
    {
        var needle = CollapseWhitespace(evidence);
        return corpus.Any(text => CollapseWhitespace(text)
            .Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string CollapseWhitespace(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Normalize(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Parses the model's questions JSON leniently: tolerates surrounding prose or
    /// markdown fences, returns null when nothing parseable remains (answer-only degrade).</summary>
    private static List<QuestionEntry?>? TryParseEntries(string modelText)
    {
        if (TryDeserialize(modelText) is { } direct)
        {
            return direct;
        }

        var start = modelText.IndexOf('[');
        var end = modelText.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return TryDeserialize(modelText[start..(end + 1)]);
        }

        return null;
    }

    private static List<QuestionEntry?>? TryDeserialize(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<List<QuestionEntry?>>(text, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record QuestionEntry(string? Question, string? Probes, string? Evidence);
}
