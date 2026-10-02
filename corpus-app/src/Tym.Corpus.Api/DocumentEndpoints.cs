using Microsoft.AspNetCore.Http;

namespace Tym.Corpus.Api;

public static class DocumentEndpoints
{
    public static IResult Analyze(DocumentAnalyzeRequest? request, DocumentAnalyzer analyzer)
    {
        var error = DocumentAnalyzer.ValidateRequest(request);
        return error is not null ? Results.BadRequest(error) : Results.Ok(analyzer.Analyze(request!));
    }

    /// <summary>Invalid edited exports return HTTP 200 with valid=false and structured diagnostics.</summary>
    public static IResult Validate(AnnotatedDocument? document) => Results.Ok(DocumentValidator.Validate(document));
}
