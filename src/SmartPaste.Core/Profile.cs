namespace SmartPaste.Core;

public static class FieldKeys
{
    public const string FirstName = "first_name", MiddleName = "middle_name", LastName = "last_name", FullName = "full_name",
        Street = "street", Unit = "unit", City = "city", State = "state", Zip = "zip", Country = "country",
        Email = "email", Phone = "phone", Company = "company", Title = "title", DateOfBirth = "date_of_birth",
        Website = "website", Linkedin = "linkedin";

    public static readonly IReadOnlyList<string> Standard = Array.AsReadOnly(new[]
    {
        FirstName, MiddleName, LastName, FullName, Street, Unit, City, State, Zip, Country,
        Email, Phone, Company, Title, DateOfBirth, Website, Linkedin
    });

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.Ordinal)
    {
        [FirstName] = "First name", [MiddleName] = "Middle name", [LastName] = "Last name", [FullName] = "Full name",
        [Street] = "Street", [Unit] = "Apt / unit", [City] = "City", [State] = "State", [Zip] = "ZIP / postal code",
        [Country] = "Country", [Email] = "Email", [Phone] = "Phone", [Company] = "Company", [Title] = "Job title",
        [DateOfBirth] = "Date of birth", [Website] = "Website", [Linkedin] = "LinkedIn"
    };

    // What each standard key holds, in words precise enough for Jev to tell neighbours apart
    // (first name vs full name, street vs unit).
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        [FirstName] = "First name (given name) only",
        [MiddleName] = "Middle name or middle initial only",
        [LastName] = "Last name (family name, surname) only",
        [FullName] = "Full name: first and last name together",
        [Street] = "Street address: house number and street (address line 1)",
        [Unit] = "Apartment, suite, unit, or floor (address line 2)",
        [City] = "City or town",
        [State] = "State, province, or region",
        [Zip] = "ZIP or postal code",
        [Country] = "Country",
        [Email] = "Email address",
        [Phone] = "Phone number",
        [Company] = "Company, employer, or organization name",
        [Title] = "Job title or position",
        [DateOfBirth] = "Date of birth",
        [Website] = "Personal website or portfolio URL",
        [Linkedin] = "LinkedIn profile URL"
    };

    // A name people can read: "First name" for standard keys, "Favorite color" for an extra key like favorite_color.
    public static string DisplayName(string key)
    {
        if (DisplayNames.TryGetValue(key, out var name)) return name;
        var words = key.Replace('_', ' ').Replace('-', ' ').Trim();
        return words.Length == 0 ? key : char.ToUpperInvariant(words[0]) + words[1..];
    }

    // A short plain-English description of what the key holds. Extra keys get their display name.
    public static string Describe(string key) =>
        Descriptions.TryGetValue(key, out var description) ? description : DisplayName(key);
}

public sealed record Entity(string Label, IReadOnlyDictionary<string, string> Fields);

public sealed class Profile
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public Profile(IReadOnlyList<Entity> entities, DateTimeOffset createdAt)
    {
        Entities = entities.ToArray();
        CreatedAt = createdAt;
        var all = new HashSet<string>(Entities.SelectMany(e => e.Fields.Keys), StringComparer.Ordinal);
        Keys = FieldKeys.Standard.Where(all.Remove)
            .Concat(all.OrderBy(k => k, StringComparer.Ordinal)).ToArray();
    }

    public IReadOnlyList<Entity> Entities { get; }
    public DateTimeOffset CreatedAt { get; }
    public bool IsFresh(DateTimeOffset now) => now - CreatedAt < Lifetime;
    public IReadOnlyList<string> Keys { get; }
    public int DistinctFieldTypes => Keys.Count;
    public bool IsMultiField => DistinctFieldTypes >= 2;

    public IReadOnlyList<(Entity Entity, string Value)> ValuesFor(string key) =>
        Entities.Where(e => e.Fields.ContainsKey(key))
            .Select(e => (e, e.Fields[key])).ToArray();
}
