using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace LabScaner.Infrastructure.Import;

/// <summary>Лист таблицы: строки по номерам, ячейки по столбцам (A = 0).</summary>
public sealed record XlsxSheet(string Name, IReadOnlyList<IReadOnlyList<string?>> Rows);

/// <summary>
/// Минимальное чтение значений ячеек из XLSX — без стилей, формул и форматирования.
/// Стили не разбираются вовсе, поэтому файлы Яндекс Таблиц с нестандартными границами (`solid`)
/// читаются без ошибок (см. tests/fixtures/groups).
/// </summary>
public static class XlsxReader
{
    public const long MaxFileSize = 5 * 1024 * 1024;

    private static readonly XNamespace _main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace _rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace _pkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XmlReaderSettings _xml = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    /// <exception cref="InvalidDataException">Файл не является книгой XLSX.</exception>
    public static IReadOnlyList<XlsxSheet> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("Файл не похож на книгу Excel (.xlsx).");
        }

        using (zip)
        {
            var workbook = Load(zip, "xl/workbook.xml") ?? throw new InvalidDataException("В файле нет книги Excel (xl/workbook.xml).");
            var rels = Load(zip, "xl/_rels/workbook.xml.rels");
            var targets = rels?.Root?.Elements(_pkgRel + "Relationship")
                .ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!, StringComparer.Ordinal)
                ?? [];
            var shared = ReadSharedStrings(zip);

            var result = new List<XlsxSheet>();
            foreach (var sheet in workbook.Root!.Element(_main + "sheets")?.Elements(_main + "sheet") ?? [])
            {
                if ((string?)sheet.Attribute("state") is "hidden" or "veryHidden")
                {
                    continue;
                }

                var name = (string?)sheet.Attribute("name") ?? string.Empty;
                var id = (string?)sheet.Attribute(_rel + "id");
                if (id is null || !targets.TryGetValue(id, out var target))
                {
                    continue;
                }

                var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                var xml = Load(zip, path);
                if (xml is not null)
                {
                    result.Add(new XlsxSheet(name, ReadRows(xml, shared)));
                }
            }

            return result;
        }
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var xml = Load(zip, "xl/sharedStrings.xml");
        if (xml is null)
        {
            return [];
        }

        // Строка может быть из нескольких фрагментов форматированного текста (<r><t>…</t></r>).
        return [.. xml.Root!.Elements(_main + "si").Select(si => string.Concat(si.Descendants(_main + "t").Select(t => t.Value)))];
    }

    private static List<IReadOnlyList<string?>> ReadRows(XDocument sheet, List<string> shared)
    {
        var rows = new SortedDictionary<int, List<string?>>();
        var data = sheet.Root!.Element(_main + "sheetData");
        var nextRow = 1;
        foreach (var row in data?.Elements(_main + "row") ?? [])
        {
            var rowNumber = int.TryParse((string?)row.Attribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : nextRow;
            nextRow = rowNumber + 1;
            var cells = new List<string?>();
            var nextCol = 0;
            foreach (var c in row.Elements(_main + "c"))
            {
                var col = ColumnIndex((string?)c.Attribute("r")) ?? nextCol;
                nextCol = col + 1;
                while (cells.Count <= col)
                {
                    cells.Add(null);
                }

                cells[col] = CellValue(c, shared);
            }

            rows[rowNumber] = cells;
        }

        // Пропущенные строки — пустые, чтобы индекс совпадал с номером строки в Excel.
        var result = new List<IReadOnlyList<string?>>();
        var last = rows.Count == 0 ? 0 : rows.Keys.Max();
        for (var i = 1; i <= last; i++)
        {
            result.Add(rows.TryGetValue(i, out var cells) ? cells : []);
        }

        return result;
    }

    private static string? CellValue(XElement cell, List<string> shared)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(_main + "t").Select(t => t.Value));
        }

        var value = cell.Element(_main + "v")?.Value;
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return type switch
        {
            "s" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < shared.Count => shared[i],
            "b" => value == "1" ? "ИСТИНА" : "ЛОЖЬ",
            _ => value,
        };
    }

    /// <summary>«C12» → 2.</summary>
    private static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        var index = 0;
        var letters = 0;
        foreach (var ch in reference)
        {
            if (!char.IsAsciiLetter(ch))
            {
                break;
            }

            index = (index * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
            letters++;
        }

        return letters == 0 ? null : index - 1;
    }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        if (entry.Length > 50 * 1024 * 1024)
        {
            throw new InvalidDataException("Лист слишком большой.");
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, _xml);
        return XDocument.Load(reader);
    }
}
