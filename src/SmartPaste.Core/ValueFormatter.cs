using System.Globalization;

namespace SmartPaste.Core;

public static class ValueFormatter
{
    private static readonly Dictionary<string, string> Regions = BuildRegions();
    private static readonly Dictionary<string, string> Countries = BuildCountries();

    public static string Format(string key, string value, FieldContext field)
    {
        var hint = LabelNormalizer.Normalize(string.Join(" ", new[] { field.Name, field.HelpText, field.LabeledBy,
            LabelNormalizer.Humanize(field.AutomationId ?? ""), field.OcrText }.Where(x => !string.IsNullOrWhiteSpace(x))));
        return key switch
        {
            FieldKeys.State => FormatRegion(value, hint, field),
            FieldKeys.Phone => FormatPhone(value, hint, string.Join(" ", new[] { field.Name, field.HelpText, field.LabeledBy, field.AutomationId }), field.MaxLength),
            FieldKeys.DateOfBirth => FormatDate(value, hint),
            FieldKeys.Country => FormatCountry(value, hint, field.MaxLength),
            FieldKeys.Zip when field.MaxLength == 5 || Has(hint, "5 digit") => value.Length >= 5 && value[..5].All(char.IsDigit) ? value[..5] : value,
            _ => value
        };
    }

    private static string FormatRegion(string value, string hint, FieldContext field)
    {
        var pair = Regions.FirstOrDefault(p => p.Key.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase) || p.Value.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (pair.Key is null) return value;
        if (HasAny(hint, "abbr", "code", "2 letter") || field.MaxLength <= 3) return pair.Key;
        if (HasAny(hint, "full", "name") || ((field.MaxLength is null || field.MaxLength > 3) && field.ControlType?.Equals("ComboBox", StringComparison.OrdinalIgnoreCase) == true)) return pair.Value;
        return value;
    }

    private static string FormatPhone(string value, string hint, string rawHint, int? maxLength)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (HasAny(hint, "digits", "numbers only", "no dashes") || maxLength is 10 or 11) return digits;
        var domestic = digits.Length == 11 && digits[0] == '1' ? digits[1..] : digits;
        if ((Has(hint, "xxx xxx xxxx") || rawHint.Contains("###-###-####", StringComparison.Ordinal)) && domestic.Length == 10)
            return $"{domestic[..3]}-{domestic[3..6]}-{domestic[6..]}";
        if ((HasAny(hint, "e 164", "international", "country code") || rawHint.Contains("+1", StringComparison.Ordinal)) && domestic.Length == 10)
            return "+1" + domestic;
        return value;
    }

    private static string FormatDate(string value, string hint)
    {
        var formats = new[] { "M/d/yyyy", "M/d/yy", "yyyy-MM-dd", "MMMM d, yyyy", "MMMM d yyyy", "d MMMM yyyy", "MMM d, yyyy", "d MMM yyyy" };
        if (!DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return value;
        string? format = null;
        if (Has(hint, "mm dd yyyy")) format = "MM/dd/yyyy";
        else if (Has(hint, "dd mm yyyy")) format = "dd/MM/yyyy";
        else if (Has(hint, "yyyy mm dd")) format = "yyyy-MM-dd";
        else if (Has(hint, "mm yy")) format = "MM/yy";
        return format is null ? value : date.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatCountry(string value, string hint, int? maxLength)
    {
        var normalized = value.Trim().TrimEnd('.');
        var code = Countries.Keys.FirstOrDefault(k => k.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        code ??= Countries.FirstOrDefault(p => p.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)).Key;
        if (normalized.Equals("USA", StringComparison.OrdinalIgnoreCase) || normalized.Equals("U.S", StringComparison.OrdinalIgnoreCase) || normalized.Equals("United States of America", StringComparison.OrdinalIgnoreCase)) code = "US";
        if (normalized.Equals("UK", StringComparison.OrdinalIgnoreCase)) code = "GB";
        if (code is null) return value;
        if (HasAny(hint, "code", "iso") || maxLength == 2) return code;
        if (value.Trim().Length == 2 || normalized.Equals("USA", StringComparison.OrdinalIgnoreCase) || normalized.Equals("U.S", StringComparison.OrdinalIgnoreCase) || normalized.Equals("UK", StringComparison.OrdinalIgnoreCase)) return Countries[code];
        return value;
    }

    private static bool HasAny(string hint, params string[] terms) => terms.Any(t => Has(hint, t));
    private static bool Has(string hint, string term) => (" " + hint + " ").Contains(" " + term + " ", StringComparison.Ordinal);

    private static Dictionary<string, string> BuildRegions()
    {
        const string pairs = "AL:Alabama|AK:Alaska|AZ:Arizona|AR:Arkansas|CA:California|CO:Colorado|CT:Connecticut|DE:Delaware|FL:Florida|GA:Georgia|HI:Hawaii|ID:Idaho|IL:Illinois|IN:Indiana|IA:Iowa|KS:Kansas|KY:Kentucky|LA:Louisiana|ME:Maine|MD:Maryland|MA:Massachusetts|MI:Michigan|MN:Minnesota|MS:Mississippi|MO:Missouri|MT:Montana|NE:Nebraska|NV:Nevada|NH:New Hampshire|NJ:New Jersey|NM:New Mexico|NY:New York|NC:North Carolina|ND:North Dakota|OH:Ohio|OK:Oklahoma|OR:Oregon|PA:Pennsylvania|RI:Rhode Island|SC:South Carolina|SD:South Dakota|TN:Tennessee|TX:Texas|UT:Utah|VT:Vermont|VA:Virginia|WA:Washington|WV:West Virginia|WI:Wisconsin|WY:Wyoming|DC:District of Columbia|PR:Puerto Rico|GU:Guam|VI:U.S. Virgin Islands|AS:American Samoa|MP:Northern Mariana Islands|AB:Alberta|BC:British Columbia|MB:Manitoba|NB:New Brunswick|NL:Newfoundland and Labrador|NS:Nova Scotia|NT:Northwest Territories|NU:Nunavut|ON:Ontario|PE:Prince Edward Island|QC:Quebec|SK:Saskatchewan|YT:Yukon";
        return pairs.Split('|').Select(p => p.Split(':', 2)).ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildCountries()
    {
        const string pairs = "US:United States|CA:Canada|GB:United Kingdom|AU:Australia|NZ:New Zealand|IN:India|DE:Germany|FR:France|ES:Spain|IT:Italy|NL:Netherlands|BE:Belgium|CH:Switzerland|AT:Austria|SE:Sweden|NO:Norway|DK:Denmark|FI:Finland|IE:Ireland|PT:Portugal|GR:Greece|PL:Poland|CZ:Czechia|SK:Slovakia|HU:Hungary|RO:Romania|BG:Bulgaria|HR:Croatia|SI:Slovenia|EE:Estonia|LV:Latvia|LT:Lithuania|UA:Ukraine|TR:Turkey|RU:Russia|JP:Japan|CN:China|KR:South Korea|TW:Taiwan|HK:Hong Kong|SG:Singapore|MY:Malaysia|TH:Thailand|VN:Vietnam|PH:Philippines|ID:Indonesia|PK:Pakistan|BD:Bangladesh|AE:United Arab Emirates|SA:Saudi Arabia|IL:Israel|EG:Egypt|ZA:South Africa|NG:Nigeria|KE:Kenya|MX:Mexico|BR:Brazil|AR:Argentina|CL:Chile|CO:Colombia|PE:Peru|UY:Uruguay|CR:Costa Rica|PA:Panama|JM:Jamaica";
        return pairs.Split('|').Select(p => p.Split(':', 2)).ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
    }
}
