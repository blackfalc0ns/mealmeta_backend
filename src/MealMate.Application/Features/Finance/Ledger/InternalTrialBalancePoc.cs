using System.Globalization;
using System.Xml.Linq;

namespace MealMate.Application.Features.Finance.Ledger;

/// <summary>A test-only internal mapping experiment; output is deliberately not an XBRL instance or filing.</summary>
public static class InternalTrialBalancePoc
{
    public const string Marker = "TEST_ONLY";
    private static readonly XNamespace Namespace = "urn:mealmate:finance:internal-trial-balance-poc:v1";

    public static string Map(LedgerTrialBalanceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Rows.Count != result.TotalRowCount || result.Rows.Select(x => x.AccountId).Distinct().Count() != result.Rows.Count)
            throw new ArgumentException("The internal PoC requires a complete, uniquely keyed trial-balance result.", nameof(result));
        var document = new XDocument(new XElement(Namespace + "internal-trial-balance",
            new XAttribute("marker", Marker),
            new XAttribute("entity-id", result.LegalEntityId),
            new XAttribute("period-id", result.PeriodId),
            new XAttribute("as-of-date", result.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XAttribute("close-version", result.CloseVersion.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("currency", "KWD"),
            new XElement(Namespace + "accounts", result.Rows.Select(row => new XElement(Namespace + "account",
                new XAttribute("code", row.Code), new XAttribute("account-id", row.AccountId),
                Value("opening-debit", row.OpeningDebit), Value("opening-credit", row.OpeningCredit),
                Value("period-debit", row.PeriodDebit), Value("period-credit", row.PeriodCredit),
                Value("closing-debit", row.ClosingDebit), Value("closing-credit", row.ClosingCredit)))),
            new XElement(Namespace + "totals",
                Value("opening-debit", result.TotalOpeningDebit), Value("opening-credit", result.TotalOpeningCredit),
                Value("period-debit", result.TotalPeriodDebit), Value("period-credit", result.TotalPeriodCredit),
                Value("closing-debit", result.TotalClosingDebit), Value("closing-credit", result.TotalClosingCredit))));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static bool Validate(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return false;
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root?.Name != Namespace + "internal-trial-balance" || (string?)root.Attribute("marker") != Marker ||
                (string?)root.Attribute("currency") != "KWD" || !Guid.TryParse((string?)root.Attribute("entity-id"), out _) ||
                !Guid.TryParse((string?)root.Attribute("period-id"), out _) ||
                !DateOnly.TryParseExact((string?)root.Attribute("as-of-date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                !long.TryParse((string?)root.Attribute("close-version"), NumberStyles.None, CultureInfo.InvariantCulture, out _)) return false;
            var accounts = root.Element(Namespace + "accounts")?.Elements(Namespace + "account").ToArray();
            var totals = root.Element(Namespace + "totals");
            var expected = new[] { "opening-debit", "opening-credit", "period-debit", "period-credit", "closing-debit", "closing-credit" };
            if (accounts is null || totals is null || accounts.Any(x => string.IsNullOrWhiteSpace((string?)x.Attribute("code")) ||
                !Guid.TryParse((string?)x.Attribute("account-id"), out _) ||
                !x.Elements().Select(e => e.Name).SequenceEqual(expected.Select(name => Namespace + name)) ||
                x.Elements().Any(element => !TryDecimal(element.Value, out _)))) return false;
            var accountIds = accounts.Select(x => (string?)x.Attribute("account-id")).ToArray();
            if (accountIds.Distinct(StringComparer.Ordinal).Count() != accountIds.Length) return false;
            if (!totals.Elements().Select(e => e.Name).SequenceEqual(expected.Select(name => Namespace + name)) ||
                totals.Elements().Any(element => !TryDecimal(element.Value, out _))) return false;
            var values = expected.ToDictionary(name => name, name => ParseDecimal(totals.Element(Namespace + name)!.Value), StringComparer.Ordinal);
            foreach (var account in accounts)
            {
                var row = expected.ToDictionary(name => name, name => ParseDecimal(account.Element(Namespace + name)!.Value), StringComparer.Ordinal);
                if (Net(row["opening-debit"], row["opening-credit"]) + Net(row["period-debit"], row["period-credit"]) !=
                    Net(row["closing-debit"], row["closing-credit"])) return false;
            }
            foreach (var name in expected)
                if (accounts.Sum(account => ParseDecimal(account.Element(Namespace + name)!.Value)) != values[name]) return false;
            return values["opening-debit"] == values["opening-credit"] && values["period-debit"] == values["period-credit"] &&
                values["closing-debit"] == values["closing-credit"];
        }
        catch (System.Xml.XmlException) { return false; }
        catch (OverflowException) { return false; }
    }

    private static XElement Value(string name, decimal value) => new(Namespace + name, value.ToString("0.000", CultureInfo.InvariantCulture));
    private static decimal ParseDecimal(string value) => decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    private static decimal Net(decimal debit, decimal credit) => debit - credit;
    private static bool TryDecimal(string value, out decimal result) => decimal.TryParse(value, NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out result) && result >= 0 && decimal.Round(result, 3) == result;
}
