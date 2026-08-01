using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace KnowledgeAssistant.Tests.Integration.Harness;

/// <summary>
/// Builds the request bodies the endpoints accept.
/// </summary>
/// <remarks>
/// The PDFs are generated rather than committed as binary fixtures. A generated
/// document states its own content in the code that asserts against it — "three
/// pages, forty-five lines each, every line naming its page" — where a checked-in
/// file would be an opaque blob whose properties live in a comment that nothing
/// verifies.
/// </remarks>
internal static class TestContent
{
    /// <summary>Builds a real PDF with text on every page.</summary>
    public static byte[] Pdf(int pages = 3, int linesPerPage = 45)
    {
        using var builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);

        for (int page = 1; page <= pages; page++)
        {
            PdfPageBuilder pageBuilder = builder.AddPage(595, 842);
            double y = 800;

            for (int line = 0; line < linesPerPage; line++)
            {
                pageBuilder.AddText(
                    $"Page{page}Line{line} the quick brown fox jumps over the lazy dog repeatedly",
                    10,
                    new PdfPoint(40, y),
                    font);

                y -= 14;
            }
        }

        return builder.Build();
    }

    /// <summary>Builds a structurally valid PDF that contains no text at all.</summary>
    public static byte[] TextFreePdf()
    {
        using var builder = new PdfDocumentBuilder();
        builder.AddPage(595, 842);
        return builder.Build();
    }

    /// <summary>Wraps bytes as the <c>file</c> field of a multipart form.</summary>
    public static MultipartFormDataContent Upload(
        byte[] content,
        string fileName = "report.pdf",
        string contentType = "application/pdf")
    {
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        // The multipart form is built by hand rather than by a client library so
        // the field name is explicit: the model binds on "file", and a rename
        // there must fail these tests rather than silently bind null.
        return new MultipartFormDataContent { { fileContent, "file", fileName } };
    }

    /// <summary>Builds a JSON body, including shapes a typed client could not send.</summary>
    public static StringContent Json(string raw) =>
        new(raw, Encoding.UTF8, "application/json");

    /// <summary>Serialises a question request.</summary>
    public static StringContent Question(string question, int? topK = null)
    {
        string json = JsonSerializer.Serialize(new { question, topK });
        return Json(json);
    }
}
