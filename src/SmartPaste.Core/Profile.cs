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
