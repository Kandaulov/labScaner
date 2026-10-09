using System.Security.Cryptography;
using LabScaner.Infrastructure.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabScaner.Web.Pages.Groups;

/// <summary>Импорт списков групп из Excel: загрузка → предпросмотр с ошибками по строкам → подтверждение.</summary>
[RequestSizeLimit(XlsxReader.MaxFileSize + (64 * 1024))]
public sealed class ImportModel(GroupImportService import) : PageModel
{
    public ImportPreview? Preview { get; private set; }

    public ImportResult? Result { get; private set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            Error = "Выберите файл .xlsx.";
            return Page();
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            Error = "Нужен файл Excel в формате .xlsx. Из Яндекс Таблиц и Google Таблиц: «Скачать как → Microsoft Excel (.xlsx)».";
            return Page();
        }

        if (file.Length > XlsxReader.MaxFileSize)
        {
            Error = "Файл больше 5 МБ — для списка групп это слишком много. Проверьте, тот ли файл.";
            return Page();
        }

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;
            Preview = await import.PreviewAsync(buffer, Path.GetFileName(file.FileName));
            if (Preview.Sheets.Count == 0)
            {
                Error = "В книге нет видимых листов.";
                Preview = null;
            }
        }
        catch (InvalidDataException ex)
        {
            Error = ex.Message;
        }
        catch (System.Xml.XmlException)
        {
            Error = "Файл повреждён или это не книга Excel.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(string payload, string[]? groups)
    {
        if (groups is null || groups.Length == 0)
        {
            Error = "Не выбрано ни одной группы — нечего импортировать.";
            return Page();
        }

        try
        {
            Result = await import.ApplyAsync(payload, groups);
        }
        catch (CryptographicException)
        {
            Error = "Предпросмотр устарел или повреждён. Загрузите файл ещё раз.";
        }

        return Page();
    }
}
