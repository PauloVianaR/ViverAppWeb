using System.Net;

namespace ViverApp.Api.Features.Identity;

public sealed record AdministratorWelcomePayload(string FullName, string ClinicName, string AccessUrl, DateTime ExpiresAtUtc);
public sealed record AdministratorWelcomeContent(string Subject, string TextBody, string HtmlBody);

public static class AdministratorWelcomeEmail
{
    public static AdministratorWelcomeContent Render(AdministratorWelcomePayload payload)
    {
        if (!Uri.TryCreate(payload.AccessUrl, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Scheme != Uri.UriSchemeHttps && !(uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp))
            throw new InvalidOperationException("identity_welcome_url_invalid");
        var name = WebUtility.HtmlEncode(payload.FullName);
        var clinic = WebUtility.HtmlEncode(payload.ClinicName);
        var url = WebUtility.HtmlEncode(uri.AbsoluteUri);
        var text = $"Olá, {payload.FullName}!\n\nSeu cadastro no ViverApp foi realizado pela equipe de {payload.ClinicName} e já está disponível para acesso.\n\nPara definir sua senha com segurança:\n1. Acesse {uri.AbsoluteUri}\n2. Informe o e-mail que recebeu esta mensagem e clique em \"Esqueci a senha\".\n3. Solicite o código por e-mail, confirme-o e escolha sua nova senha.\n\nNenhuma senha foi criada ou enviada pela equipe. Não compartilhe seus códigos de verificação. Se seu perfil exigir autenticação em duas etapas, siga também as orientações exibidas no acesso.\n\nSe não reconhece este cadastro, entre em contato com a clínica.\n\nEquipe {payload.ClinicName}\nViverApp — cuidado e organização em um só lugar.";
        var html = $$"""
            <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;background:#f2f5f9;font-family:Arial,sans-serif;color:#172b4d;line-height:1.6">
            <table role="presentation" style="width:100%;border-collapse:collapse"><tr><td style="padding:28px 12px">
            <table role="presentation" style="width:100%;max-width:600px;margin:auto;background:white;border-radius:16px;overflow:hidden;border-collapse:collapse">
            <tr><td style="padding:26px 30px;background:#13304a;color:white"><strong style="font-size:25px;letter-spacing:2px">VIVER</strong><br><span>{{clinic}}</span></td></tr>
            <tr><td style="padding:30px"><p style="color:#087c70;font-size:13px;font-weight:bold;letter-spacing:1px">SEU ACESSO ESTÁ PRONTO</p>
            <h1 style="font-size:25px;line-height:1.3">Boas-vindas, {{name}}!</h1>
            <p>A equipe de <strong>{{clinic}}</strong> realizou seu cadastro no ViverApp. Agora você pode definir sua senha e acessar os recursos disponíveis para seu perfil.</p>
            <div style="background:#edf8f5;padding:18px;border-radius:10px"><strong>Comece em três passos</strong><ol style="padding-left:22px;margin-bottom:0">
            <li>Abra a página de acesso pelo botão abaixo.</li><li>Informe este e-mail e clique em <strong>Esqueci a senha</strong>.</li><li>Receba o código por e-mail e escolha sua nova senha.</li></ol></div>
            <p style="margin:28px 0"><a href="{{url}}" style="display:inline-block;background:#087c70;color:white;text-decoration:none;padding:13px 24px;border-radius:8px;font-weight:bold">Acessar o ViverApp</a></p>
            <p style="font-size:14px">Sua conta foi criada <strong>sem senha</strong>. Somente você definirá sua senha após confirmar o código. Nunca compartilhe seus códigos de verificação. Perfis administrativos também seguem a autenticação em duas etapas.</p>
            <p style="font-size:14px;color:#52637b">Se não reconhece este cadastro, entre em contato com a clínica. Se o botão não abrir, use:<br><a href="{{url}}" style="color:#087c70;word-break:break-all">{{url}}</a></p>
            <p>Atenciosamente,<br><strong>Equipe {{clinic}}</strong></p></td></tr>
            <tr><td style="padding:18px 30px;background:#f7f9fc;color:#52637b;font-size:12px">ViverApp · Cuidado e organização em um só lugar.<br>Esta mensagem não contém senha nem documentos clínicos.</td></tr>
            </table></td></tr></table></body></html>
            """;
        return new("Seu cadastro no ViverApp está pronto", text, html);
    }
}
