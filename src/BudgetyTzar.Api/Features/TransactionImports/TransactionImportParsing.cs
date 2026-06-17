using System.Globalization;
using System.Text;

namespace BudgetyTzar.Api.Features;

public sealed record ParsedImportRow(
    int RowNumber,
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    TransactionDirection Direction,
    string? SourceAccount,
    string? ExternalReference,
    string? Notes);

public static class TransactionImportParsing
{
    private static readonly string[] RequiredHeader =
    [
        "date",
        "description",
        "amount",
        "direction",
        "source account",
        "external reference",
        "notes"
    ];

    public static IReadOnlyList<ParsedImportRow> Parse(string csv)
    {
        var rows = ReadRows(csv);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("CSV content is empty.");
        }

        var header = rows[0].Select(NormalizeHeader).ToArray();
        if (header.Length != RequiredHeader.Length || !header.SequenceEqual(RequiredHeader))
        {
            throw new InvalidOperationException("CSV header must be: date,description,amount,direction,source account,external reference,notes.");
        }

        var parsed = new List<ParsedImportRow>();
        for (var index = 1; index < rows.Count; index++)
        {
            var rowNumber = index + 1;
            var row = rows[index];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (row.Count != RequiredHeader.Length)
            {
                throw new InvalidOperationException($"Row {rowNumber} has {row.Count} columns; expected {RequiredHeader.Length}.");
            }

            if (!DateOnly.TryParse(row[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new InvalidOperationException($"Row {rowNumber} has an invalid date.");
            }

            var description = row[1].Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                throw new InvalidOperationException($"Row {rowNumber} requires a description.");
            }

            if (!decimal.TryParse(row[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            {
                throw new InvalidOperationException($"Row {rowNumber} has an invalid positive amount.");
            }

            if (!Enum.TryParse<TransactionDirection>(row[3], true, out var direction))
            {
                throw new InvalidOperationException($"Row {rowNumber} has an invalid direction.");
            }

            parsed.Add(new ParsedImportRow(
                rowNumber,
                date,
                description,
                decimal.Round(amount, 2),
                direction,
                NullIfWhiteSpace(row[4]),
                NullIfWhiteSpace(row[5]),
                NullIfWhiteSpace(row[6])));
        }

        return parsed;
    }

    public static string NormalizeForDuplicateMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var character in value.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadRows(string csv)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var character = csv[i];
            if (character == '"')
            {
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if ((character == '\n' || character == '\r') && !inQuotes)
            {
                if (character == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(character);
            }
        }

        row.Add(field.ToString());
        if (row.Count > 1 || !string.IsNullOrWhiteSpace(row[0]))
        {
            rows.Add(row);
        }

        return rows;
    }

    private static string NormalizeHeader(string value) =>
        value.Trim().ToLowerInvariant();

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
