using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LabScaner.Core.Teaching;

namespace LabScaner.Infrastructure.Import;

/// <summary>
/// Абзацы DOCX для деления заданий (ADR-029): текст, уровень заголовка, жирность, уровень списка.
/// Читается сам XML документа (как и XLSX) — без оформления, картинок и колонтитулов. Строки таблиц
/// превращаются в абзацы «ячейка | ячейка». Поддерживаются обычный и «строгий» (Strict) формат Office Open XML.
/// </summary>
public static partial class DocxReader
{
    public const long MaxFileSize = 10 * 1024 * 1024;

    private static readonly XNamespace _transitional = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace _strict = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private static readonly XmlReaderSettings _xml = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    /// <exception cref="InvalidDataException">Файл не является документом DOCX.</exception>
    public static IReadOnlyList<DocParagraph> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("Файл не похож на документ Word (.docx). Файлы .doc сохраните как .docx.");
        }

        using (zip)
        {
            var document = Load(zip, "word/document.xml") ?? throw new InvalidDataException("В файле нет документа Word (word/document.xml).");
            var w = document.Root!.Name.Namespace == _strict ? _strict : _transitional;
            var styles = new StyleMap(Load(zip, "word/styles.xml"), w);
            var body = document.Root!.Element(w + "body") ?? throw new InvalidDataException("Документ Word пуст.");

            var result = new List<DocParagraph>();
            Collect(body, w, styles, result);
            return result;
        }
    }

    private static void Collect(XElement container, XNamespace w, StyleMap styles, List<DocParagraph> result)
    {
        foreach (var element in container.Elements())
        {
            if (element.Name == w + "p")
            {
                result.Add(Paragraph(element, w, styles));
            }
            else if (element.Name == w + "tbl")
            {
                foreach (var row in element.Elements(w + "tr"))
                {
                    var cells = row.Elements(w + "tc")
                        .Select(c => string.Join(" ", c.Descendants(w + "p").Select(p => TextOf(p, w).Trim()).Where(t => t.Length > 0)))
                        .ToList();
                    if (cells.Any(c => c.Length > 0))
                    {
                        result.Add(new DocParagraph(string.Join(" | ", cells)));
                    }
                }
            }
            else if (element.Name == w + "sdt")
            {
                var content = element.Element(w + "sdtContent");
                if (content is not null)
                {
                    Collect(content, w, styles, result);
                }
            }
        }
    }

    private static DocParagraph Paragraph(XElement p, XNamespace w, StyleMap styles)
    {
        var pPr = p.Element(w + "pPr");
        var styleId = Val(pPr?.Element(w + "pStyle"), w);
        var style = styles.Resolve(styleId);

        var outline = Int(Val(pPr?.Element(w + "outlineLvl"), w)) ?? style.OutlineLevel;
        var heading = outline is >= 0 and < 9 ? outline.Value + 1 : style.HeadingLevel;

        var numPr = pPr?.Element(w + "numPr");
        int listLevel;
        if (numPr is not null)
        {
            var numId = Int(Val(numPr.Element(w + "numId"), w));
            listLevel = numId == 0 ? -1 : Int(Val(numPr.Element(w + "ilvl"), w)) ?? style.ListLevel ?? 0;
        }
        else
        {
            listLevel = style.ListLevel ?? -1;
        }

        var runs = p.Descendants(w + "r").Where(r => r.Elements(w + "t").Any(t => t.Value.Trim().Length > 0)).ToList();
        var bold = runs.Count > 0 && runs.All(r => IsBold(r.Element(w + "rPr"), w) ?? style.Bold);

        return new DocParagraph(TextOf(p, w), heading, bold, heading > 0 ? -1 : listLevel);
    }

    private static string TextOf(XElement p, XNamespace w)
    {
        var sb = new StringBuilder();
        foreach (var node in p.Descendants())
        {
            if (node.Name == w + "t")
            {
                sb.Append(node.Value);
            }
            else if (node.Name == w + "tab")
            {
                sb.Append(' ');
            }
            else if (node.Name == w + "br" || node.Name == w + "cr")
            {
                sb.Append(' ');
            }
            else if (node.Name == w + "noBreakHyphen")
            {
                sb.Append('-');
            }
        }

        return sb.ToString();
    }

    private static bool? IsBold(XElement? rPr, XNamespace w)
    {
        var b = rPr?.Element(w + "b");
        if (b is null)
        {
            return null;
        }

        var val = Val(b, w);
        return val is null || val is "1" or "true" or "on";
    }

    private static string? Val(XElement? element, XNamespace w) => (string?)element?.Attribute(w + "val");

    private static int? Int(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        if (entry.Length > 50 * 1024 * 1024)
        {
            throw new InvalidDataException("Документ слишком большой.");
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, _xml);
        return XDocument.Load(reader);
    }

    private sealed record ResolvedStyle(int HeadingLevel, int? OutlineLevel, int? ListLevel, bool Bold);

    /// <summary>Стили абзацев с наследованием (basedOn): заголовок, уровень структуры, список, жирность.</summary>
    private sealed class StyleMap
    {
        private static readonly ResolvedStyle _none = new(0, null, null, false);
        private readonly Dictionary<string, XElement> _styles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ResolvedStyle> _cache = new(StringComparer.Ordinal);
        private readonly XNamespace _w;
        private readonly string? _defaultId;

        public StyleMap(XDocument? styles, XNamespace w)
        {
            _w = w;
            foreach (var style in styles?.Root?.Elements(w + "style") ?? [])
            {
                if ((string?)style.Attribute(w + "type") is "paragraph" && (string?)style.Attribute(w + "styleId") is { } id)
                {
                    _styles[id] = style;
                    if ((string?)style.Attribute(w + "default") is "1" or "true")
                    {
                        _defaultId = id;
                    }
                }
            }
        }

        public ResolvedStyle Resolve(string? id) => Resolve(id ?? _defaultId, depth: 0);

        private ResolvedStyle Resolve(string? id, int depth)
        {
            if (id is null || depth > 10 || !_styles.TryGetValue(id, out var style))
            {
                return _none;
            }

            if (_cache.TryGetValue(id, out var cached))
            {
                return cached;
            }

            var parent = Resolve(Val(style.Element(_w + "basedOn"), _w), depth + 1);
            var pPr = style.Element(_w + "pPr");
            var name = Val(style.Element(_w + "name"), _w) ?? string.Empty;
            var nameMatch = HeadingName().Match(name);
            var outline = Int(Val(pPr?.Element(_w + "outlineLvl"), _w)) ?? parent.OutlineLevel;
            var heading = outline is >= 0 and < 9 ? outline.Value + 1
                : nameMatch.Success ? int.Parse(nameMatch.Groups[1].Value, CultureInfo.InvariantCulture)
                : parent.HeadingLevel;

            var numPr = pPr?.Element(_w + "numPr");
            var list = numPr is null ? parent.ListLevel : Int(Val(numPr.Element(_w + "ilvl"), _w)) ?? 0;
            var bold = IsBold(style.Element(_w + "rPr"), _w) ?? parent.Bold;

            var resolved = new ResolvedStyle(heading, outline, list, bold);
            _cache[id] = resolved;
            return resolved;
        }
    }

    [GeneratedRegex(@"^(?:heading|заголовок)\s*(\d)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingName();
}
