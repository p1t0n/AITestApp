using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Tools.DemoRoster;

/// <summary>
/// Industry-agnostic pools: names, places, spoken languages, degrees. Surnames are invented
/// on purpose — every generated person must be obviously fake at a glance.
/// </summary>
public static class PeoplePools
{
    public static readonly IReadOnlyList<string> FirstNames =
    [
        "Avery", "Rowan", "Quinn", "Marlow", "Sable", "Idris", "Nova", "Caspian",
        "Wren", "Juniper", "Orson", "Lyric", "Bellamy", "Fenwick", "Isolde", "Peregrine",
        "Saffron", "Thaddeus", "Ottilie", "Lazlo", "Vesper", "Cormac", "Delphine", "Ezra",
        "Fable", "Greer",
    ];

    public static readonly IReadOnlyList<string> LastNames =
    [
        "Brightforge", "Cloudmarsh", "Hexworth", "Ashgrove", "Cinderholt", "Dawnfield",
        "Emberwick", "Fennelgrove", "Gearhart", "Hollowbrook", "Ironwood", "Junipergate",
        "Kestrelmoor", "Lanternfell", "Mossvale", "Nightingaleworth", "Oakenshaw", "Pemberquill",
        "Quillstone", "Rooksmere", "Silverbirch", "Thornbury-Falk", "Umbermoor", "Violetfield",
        "Wintersgate", "Yarrowdale",
    ];

    public static readonly IReadOnlyList<string> Locations =
    [
        "Berlin, Germany", "Lisbon, Portugal", "Warsaw, Poland", "Amsterdam, Netherlands",
        "Madrid, Spain", "Kyiv, Ukraine", "Prague, Czech Republic", "Stockholm, Sweden",
        "London, United Kingdom", "Dublin, Ireland", "Vienna, Austria", "Zagreb, Croatia",
        "Tallinn, Estonia", "Sofia, Bulgaria", "Remote (EU)", "Remote (worldwide)",
        "Toronto, Canada", "Austin, TX, USA", "Wrocław, Poland", "Porto, Portugal",
    ];

    /// <summary>Additional languages beyond English, with the levels people plausibly hold.</summary>
    public static readonly IReadOnlyList<(string Language, LanguageLevel[] Levels)> ExtraLanguages =
    [
        ("German", [LanguageLevel.Basic, LanguageLevel.Conversational, LanguageLevel.Professional, LanguageLevel.Native]),
        ("Spanish", [LanguageLevel.Basic, LanguageLevel.Conversational, LanguageLevel.Fluent, LanguageLevel.Native]),
        ("French", [LanguageLevel.Basic, LanguageLevel.Conversational, LanguageLevel.Professional]),
        ("Portuguese", [LanguageLevel.Conversational, LanguageLevel.Fluent, LanguageLevel.Native]),
        ("Polish", [LanguageLevel.Conversational, LanguageLevel.Native]),
        ("Ukrainian", [LanguageLevel.Fluent, LanguageLevel.Native]),
        ("Dutch", [LanguageLevel.Basic, LanguageLevel.Conversational, LanguageLevel.Native]),
        ("Italian", [LanguageLevel.Basic, LanguageLevel.Conversational, LanguageLevel.Native]),
        ("Swedish", [LanguageLevel.Conversational, LanguageLevel.Native]),
        ("Japanese", [LanguageLevel.Basic, LanguageLevel.Conversational]),
        ("Hindi", [LanguageLevel.Fluent, LanguageLevel.Native]),
        ("Mandarin", [LanguageLevel.Basic, LanguageLevel.Conversational]),
    ];

    public static readonly IReadOnlyList<string> Universities =
    [
        "Northgate Technical University", "University of Westmere", "Harrowick Institute of Technology",
        "Saint Elmsworth University", "Polytechnic of Greyhaven", "Eastvale State University",
        "Brindlewood College of Engineering", "University of Coppermill",
    ];

    public static readonly IReadOnlyList<(string Degree, string Field)> Degrees =
    [
        ("BSc Computer Science", "Computer Science"),
        ("MSc Computer Science", "Distributed Systems"),
        ("BSc Software Engineering", "Software Engineering"),
        ("MSc Software Engineering", "Software Architecture"),
        ("BEng Electrical Engineering", "Electrical Engineering"),
        ("MSc Data Science", "Machine Learning"),
        ("BSc Applied Mathematics", "Applied Mathematics"),
        ("MEng Computer Engineering", "Embedded Systems"),
    ];
}
