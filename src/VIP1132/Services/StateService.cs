using System.IO;
using System.Text.Json;
using VIP1132.Models;

namespace VIP1132.Services;

public sealed class StateService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VIP1132");

    public string StatePath => Path.Combine(DataDirectory, "state.json");

    public async Task<AppState> LoadAsync()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                await using var stream = File.OpenRead(StatePath);
                return await JsonSerializer.DeserializeAsync<AppState>(stream, JsonOptions) ?? new AppState();
            }
        }
        catch
        {
            // A recoverable state file should never prevent the utility from opening.
        }
        return new AppState();
    }

    public async Task SaveAsync(AppState state)
    {
        Directory.CreateDirectory(DataDirectory);
        state.LastUpdatedUtc = DateTimeOffset.UtcNow;
        var temp = StatePath + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions);
        File.Move(temp, StatePath, true);
    }
}
