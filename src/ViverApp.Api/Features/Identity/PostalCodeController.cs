using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ViverApp.Security;

namespace ViverApp.Api.Features.Identity;

[ApiController]
[Route("api/v1/registration/postal-code")]
public sealed class PostalCodeController(IHttpClientFactory httpClientFactory) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.PublicFormRateLimit)]
    [HttpGet("{postalCode}")]
    public async Task<ActionResult<PostalCodeResponse>> Get(string postalCode, CancellationToken cancellationToken)
    {
        if (postalCode.Length != 8 || postalCode.Any(character => !char.IsAsciiDigit(character)))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(postalCode)] = ["Informe um CEP com oito dígitos."],
            }));
        }

        try
        {
            var client = httpClientFactory.CreateClient("PostalCodeLookup");
            var result = await client.GetFromJsonAsync<ViaCepResponse>($"ws/{postalCode}/json/", cancellationToken);
            if (result is null || result.Error)
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "CEP não encontrado." });
            }

            return Ok(new PostalCodeResponse(
                result.Street ?? string.Empty,
                result.Complement,
                result.District ?? string.Empty,
                result.City ?? string.Empty,
                result.State ?? string.Empty));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "A consulta de CEP está temporariamente indisponível. Preencha o endereço manualmente.");
        }
    }

    private sealed record ViaCepResponse(
        [property: JsonPropertyName("logradouro")] string? Street,
        [property: JsonPropertyName("complemento")] string? Complement,
        [property: JsonPropertyName("bairro")] string? District,
        [property: JsonPropertyName("localidade")] string? City,
        [property: JsonPropertyName("uf")] string? State,
        [property: JsonPropertyName("erro")] bool Error = false);
}

public sealed record PostalCodeResponse(string Street, string? Complement, string District, string City, string StateCode);
