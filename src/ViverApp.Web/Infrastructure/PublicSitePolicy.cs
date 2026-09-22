using System.Xml.Linq;
using ViverApp.Security;

namespace ViverApp.Web;

public sealed class PublicSitePolicy(IConfiguration configuration, IHostEnvironment environment)
{
    public const string LegalRevision = LegalDocumentVersions.Current;
    public const string CanonicalOrigin = "https://viveralmenara.com";

    // Institutional approval is explicit in code; environment and configuration remain separate publication gates.
    private const bool LegalTextReviewedAndPublished = true;

    private static readonly string[] PublicPaths =
    [
        "/", "/sobre", "/contato", "/perguntas-frequentes",
        "/termos", "/privacidade", "/cookies", "/acessibilidade",
    ];

    public bool CanIndex => environment.IsProduction()
        && LegalTextReviewedAndPublished
        && configuration.GetValue("PublicSite:EnableIndexing", false);

    public static bool IsPublicPage(PathString path) =>
        PublicPaths.Contains(path.Value?.TrimEnd('/').ToLowerInvariant() is { Length: > 0 } value ? value : "/");

    public string Canonical(string path) => CanonicalOrigin + path;

    public string RobotsText => CanIndex
        ? $"User-agent: *\nAllow: /\nDisallow: /api/\nDisallow: /_blazor/\nSitemap: {CanonicalOrigin}/sitemap.xml\n"
        : "User-agent: *\nDisallow: /\n";

    public string SecurityText => $"Contact: mailto:{ClinicPublicIdentity.SecurityEmail}\n"
        + $"Expires: 2027-09-21T23:59:59Z\n"
        + $"Canonical: {CanonicalOrigin}/.well-known/security.txt\n"
        + $"Policy: {CanonicalOrigin}/contato#seguranca\n"
        + "Preferred-Languages: pt-BR, en\n";

    public string SitemapXml()
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "urlset", PublicPaths.Select(path =>
                new XElement(ns + "url", new XElement(ns + "loc", Canonical(path))))));
        return document.Declaration + Environment.NewLine + document;
    }

    public static void ApplyHeaders(HttpContext context, bool canIndex)
    {
        var isPublic = IsPublicPage(context.Request.Path);
        if (!canIndex || !isPublic)
            context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";

        if (!isPublic && !Path.HasExtension(context.Request.Path.Value))
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
        }
    }
}
