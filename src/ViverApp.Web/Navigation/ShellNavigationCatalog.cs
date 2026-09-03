namespace ViverApp.Web.Navigation;

public enum ShellProfile
{
    Public,
    Patient,
    Doctor,
    Manager,
    Administrator,
}

public sealed record ShellNavigationItem(string Label, string Href, string Icon);

public sealed record ShellDefinition(
    ShellProfile Profile,
    string Label,
    string Context,
    IReadOnlyList<ShellNavigationItem> Items);

public static class ShellNavigationCatalog
{
    private static readonly ShellDefinition Public = new(
        ShellProfile.Public,
        "Visitante",
        "Centro Médico Viver",
        [
            new("Início", "/", "home"),
            new("Recursos", "/#recursos", "sparkles"),
        ]);

    private static readonly ShellDefinition Patient = new(
        ShellProfile.Patient,
        "Paciente",
        "Meu cuidado",
        [
            new("Início", "/paciente", "home"),
            new("Agendar", "/paciente#agendar", "plus"),
            new("Agenda", "/paciente#agenda", "calendar"),
            new("Pagamentos", "/paciente#pagamentos", "card"),
            new("Perfil", "/paciente#perfil", "user"),
        ]);

    private static readonly ShellDefinition Doctor = new(
        ShellProfile.Doctor,
        "Médico",
        "Área profissional",
        [
            new("Início", "/medico", "home"),
            new("Agenda", "/medico#agenda", "calendar"),
            new("Pacientes", "/medico#pacientes", "users"),
            new("Histórico", "/medico#historico", "history"),
            new("Perfil", "/medico#perfil", "user"),
        ]);

    private static readonly ShellDefinition Manager = new(
        ShellProfile.Manager,
        "Gestor",
        "Gestão clínica",
        [
            new("Visão geral", "/gestao", "home"),
            new("Agenda", "/gestao#agenda", "calendar"),
            new("Pacientes", "/gestao#pacientes", "users"),
            new("Cadastros", "/gestao/cadastros", "settings"),
            new("Perfil", "/gestao#perfil", "user"),
        ]);

    private static readonly ShellDefinition Administrator = new(
        ShellProfile.Administrator,
        "Administrador",
        "Administração",
        [
            new("Visão geral", "/administracao", "home"),
            new("Clínica", "/administracao#clinica", "clinic"),
            new("Consultas", "/administracao#consultas", "calendar"),
            new("Indicadores", "/administracao#indicadores", "chart"),
            new("Usuários", "/administracao#usuarios", "users"),
        ]);

    public static ShellDefinition Resolve(string absolutePath)
    {
        var path = string.IsNullOrWhiteSpace(absolutePath) ? "/" : absolutePath;
        if (MatchesArea(path, "/administracao"))
        {
            return Administrator;
        }

        if (MatchesArea(path, "/gestao"))
        {
            return Manager;
        }

        if (MatchesArea(path, "/medico"))
        {
            return Doctor;
        }

        if (MatchesArea(path, "/paciente"))
        {
            return Patient;
        }

        return Public;
    }

    private static bool MatchesArea(string path, string area) =>
        string.Equals(path, area, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith($"{area}/", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ShellDefinition> AllProfiles { get; } =
        [Patient, Doctor, Manager, Administrator];
}
