using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExpertToJob.Domain.Status;

/// <summary>
/// A closed set of statuses that is stored and published as one string per case (EXP-76).
///
/// <para>Every implementer used to be a <c>static class</c> of <c>const string</c>s. That shape
/// left two holes: nothing stopped a typo'd string reaching a column, and no <c>switch</c> over the
/// set was ever exhaustive, so adding a case was a silent behaviour change rather than a build
/// error. A C# 15 <c>closed</c> hierarchy closes both — but the strings those classes produced are
/// in rows written months ago and in clients already parsing them, so the spelling is not a detail
/// the refactor gets to choose. This interface is where that spelling is pinned: one
/// <see cref="IClosedStatus.Value"/> per case, one <see cref="TryParse"/> back, and the same pair used by both the
/// JSON converter below and the EF value converters in <c>AppDbContext</c>.</para>
/// </summary>
/// <typeparam name="TSelf">The closed hierarchy itself.</typeparam>
public interface IClosedStatus<TSelf> : IClosedStatus
    where TSelf : class, IClosedStatus<TSelf>
{
    /// <summary>The inverse of <see cref="Value"/>. False for anything outside the set — including
    /// null — so the caller decides whether an unreadable value degrades or throws.
    ///
    /// <para>Every implementation ends in a discard arm, and it is the one the closed set keeps:
    /// this switches over a string read back from a column or a payload, not over the hierarchy, so
    /// "none of them" is a real case. <see cref="ClosedStatus.Parse{T}"/> is the caller that turns
    /// it into a throw; the JSON converter below is the caller that turns it into a 400.</para>
    /// </summary>
    static abstract bool TryParse(string? value, [NotNullWhen(true)] out TSelf? status);
}

/// <summary>
/// The strict half of <see cref="IClosedStatus{TSelf}.TryParse"/>, for callers that have already
/// established the value is one of the set (a column this code wrote, a request already validated).
///
/// <para>One generic method rather than a <c>static abstract Parse</c> each hierarchy implements
/// (EXP-98). The six copies that shape produced differed only in how they spelled the error, and
/// nothing read the difference: <c>Parse</c>'s only callers are the three EF value converters in
/// <c>AppDbContext</c>, and three of the six copies had no caller at all. What a converter needs
/// from the failure is the value that broke the row and which set it missed — the type name carries
/// the second, so the wording does not have to be written out six times to get it.</para>
/// </summary>
public static class ClosedStatus
{
    /// <inheritdoc cref="ClosedStatus"/>
    /// <exception cref="FormatException">The value is not one of the cases.</exception>
    public static T Parse<T>(string value)
        where T : class, IClosedStatus<T> =>
        T.TryParse(value, out var status)
            ? status
            : throw new FormatException($"'{value}' is not a {typeof(T).Name}.");
}

/// <summary>
/// The non-generic half of <see cref="IClosedStatus{TSelf}"/>, for callers holding a
/// <see cref="Type"/> rather than a type argument — the OpenAPI schema transformer, which has to
/// recognise one of these to go on describing it as the string it serializes as.
/// </summary>
public interface IClosedStatus
{
    /// <summary>The one string this case is stored and serialized as. Frozen by
    /// <c>StatusStorageFreezeTests</c> and <c>StatusJsonFreezeTests</c>.</summary>
    string Value { get; }
}

/// <summary>
/// Serializes an <see cref="IClosedStatus{TSelf}"/> as its bare <see cref="IClosedStatus.Value"/>
/// string, which is the whole point: the wire shape is what it was when the status was a
/// <c>const string</c>, and the type behind it is free to change.
///
/// <para>An unrecognised string is a <see cref="JsonException"/>, deliberately. That is the one
/// failure the readers of these payloads already handle — <c>StaffingHandoffDocument.TryDeserialize</c>
/// catches it and degrades a stored document to null rather than throwing over it, and ASP.NET maps
/// it to a 400 on an inbound body. Throwing anything else would turn an unparseable column into a
/// 500 on a page that used to render.</para>
/// </summary>
public sealed class ClosedStatusJsonConverter<T> : JsonConverter<T>
    where T : class, IClosedStatus<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected a {typeof(T).Name} string, found {reader.TokenType}.");
        }

        var raw = reader.GetString();
        return T.TryParse(raw, out var status)
            ? status
            : throw new JsonException($"'{raw}' is not a {typeof(T).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
