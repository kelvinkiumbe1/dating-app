using System.Text.Json;

namespace ConsoleMatch;

public static class Storage
{
    private static readonly string PathToState =
        Path.Combine(AppContext.BaseDirectory, "consolematch-state.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppState Load() => LoadFrom(PathToState);

    public static void Save(AppState state) => SaveTo(PathToState, state);

    public static void Reset()
    {
        if (File.Exists(PathToState)) File.Delete(PathToState);
    }

    // Path-explicit overloads keep the round-trip testable without touching the real save file.
    public static AppState LoadFrom(string path)
    {
        if (!File.Exists(path)) return new AppState();
        try
        {
            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(path)) ?? new AppState();
        }
        catch (JsonException)
        {
            return new AppState(); // corrupt state file — start fresh
        }
    }

    public static void SaveTo(string path, AppState state) =>
        File.WriteAllText(path, JsonSerializer.Serialize(state, Options));
}
