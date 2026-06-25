using Clinkon1C.Core;

namespace Clinkon1C.Modules.Licenses;

public class LicenseEntry
{
    public string Name               { get; set; } = "";
    public string LicenseType        { get; set; } = "";
    public string AssociationType    { get; set; } = ""; // Computer / HardwareProtectionKey
    public string GenerationDate     { get; set; } = "";
    public string ProductCode        { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
}

public class ActivateParams
{
    public string Company     { get; set; } = "";
    public string FirstName   { get; set; } = "";
    public string MiddleName  { get; set; } = "";
    public string LastName    { get; set; } = "";
    public string Email       { get; set; } = "";
    public string Country     { get; set; } = "";
    public string ZipCode     { get; set; } = "";
    public string Town        { get; set; } = "";
    public string Street      { get; set; } = "";
    public string House       { get; set; } = "";
    public string Serial      { get; set; } = "";
    public string Pin         { get; set; } = "";
    public string PreviousPin { get; set; } = "";
}

public class LicensesModule
{
    public string Name => "Лицензии";

    private List<LicenseEntry> _entries = new List<LicenseEntry>();
    public IReadOnlyList<LicenseEntry> Entries => _entries;

    // ── Загрузка ──────────────────────────────────────────────────────────────

    public void Refresh()
    {
        _entries.Clear();

        var (code, output) = RingHelper.RunLicense("list");
        if (code != 0 || string.IsNullOrWhiteSpace(output))
        {
            Logger.Warn($"LicensesModule: list → exit {code}: {output}");
            return;
        }

        foreach (var line in output.Split('\n'))
        {
            var name = line.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var entry = new LicenseEntry { Name = name };
            FillDetails(entry);
            _entries.Add(entry);
        }

        Logger.Info($"LicensesModule: {_entries.Count} лицензий загружено");
    }

    private static void FillDetails(LicenseEntry entry)
    {
        try
        {
            var (code, info) = RingHelper.RunLicense($"info --name \"{entry.Name}\"");
            if (code != 0) return;

            bool inTech = false;
            foreach (var line in info.Split('\n'))
            {
                var t = line.Trim();
                if (t == "TechnicalInfo:") { inTech = true; continue; }
                if (!inTech) continue;

                var sep = t.IndexOf(':');
                if (sep < 0) continue;
                var key = t.Substring(0, sep).Trim();
                var val = t.Substring(sep + 1).Trim();

                switch (key)
                {
                    case "LicenseType":                      entry.LicenseType        = val; break;
                    case "LicenseAssociationType":           entry.AssociationType    = val; break;
                    case "LicenseGenerationDate":            entry.GenerationDate     = val; break;
                    case "ProductCode":                      entry.ProductCode        = val; break;
                    case "DistributionKitRegistrationNumber": entry.RegistrationNumber = val; break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"LicensesModule.FillDetails [{entry.Name}]: {ex.Message}");
        }
    }

    // ── Операции ──────────────────────────────────────────────────────────────

    public string GetFullInfo(string name)
    {
        var (_, output) = RingHelper.RunLicense($"info --name \"{name}\"");
        return string.IsNullOrWhiteSpace(output) ? "(нет данных)" : output;
    }

    public (bool Ok, string Message) Validate(string name)
    {
        var (code, output) = RingHelper.RunLicense($"validate --name \"{name}\"");
        return (code == 0, output);
    }

    public (bool Ok, string Message) Remove(string name)
    {
        var (code, output) = RingHelper.RunLicense($"remove -name \"{name}\"");
        return (code == 0, output);
    }

    public (bool Ok, string Message) Activate(ActivateParams p)
    {
        var args = new System.Text.StringBuilder("activate");

        // Компания или ФИО (хотя бы одно обязательно)
        if (!string.IsNullOrEmpty(p.Company))
            args.Append($" --company \"{Esc(p.Company)}\"");
        if (!string.IsNullOrEmpty(p.FirstName))
            args.Append($" --first-name \"{Esc(p.FirstName)}\"");
        if (!string.IsNullOrEmpty(p.MiddleName))
            args.Append($" --middle-name \"{Esc(p.MiddleName)}\"");
        if (!string.IsNullOrEmpty(p.LastName))
            args.Append($" --last-name \"{Esc(p.LastName)}\"");
        if (!string.IsNullOrEmpty(p.Email))
            args.Append($" --email \"{Esc(p.Email)}\"");

        args.Append($" --country \"{Esc(p.Country)}\"");
        args.Append($" --zip-code \"{Esc(p.ZipCode)}\"");
        args.Append($" --town \"{Esc(p.Town)}\"");
        args.Append($" --street \"{Esc(p.Street)}\"");
        args.Append($" --house \"{Esc(p.House)}\"");
        args.Append($" --serial \"{Esc(p.Serial)}\"");
        args.Append($" --pin \"{Esc(p.Pin)}\"");
        if (!string.IsNullOrEmpty(p.PreviousPin))
            args.Append($" --previous-pin \"{Esc(p.PreviousPin)}\"");

        var (code, output) = RingHelper.RunLicense(args.ToString());
        return (code == 0, string.IsNullOrWhiteSpace(output) ? (code == 0 ? "Успешно" : "Ошибка") : output);
    }

    // Экранируем кавычки в значениях параметров
    private static string Esc(string s) => s.Replace("\"", "\\\"");
}
