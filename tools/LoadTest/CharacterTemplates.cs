using Navislamia.Game.Network.Clients;
using Npgsql;

namespace Navislamia.LoadTest;

/// <summary>
/// The look a bot's character is created with. A real client sends model ids its creation screen offers, and an
/// id it does not know could break the client of a player watching the bots, so the appearance is copied from
/// the characters already in Telecaster, one per race (Gaia 3, Deva 4, Asura 5). Bots rotate through the races,
/// which also spreads them over the three starting points of the Island of Trainees.
/// </summary>
public sealed class CharacterTemplates
{
    private readonly List<LobbyCharacterInfo> _templates;

    private CharacterTemplates(List<LobbyCharacterInfo> templates) => _templates = templates;

    public int Count => _templates.Count;

    public bool FromDatabase { get; private init; }

    public LobbyCharacterInfo For(int index, string name)
    {
        var template = _templates[index % _templates.Count];
        return new LobbyCharacterInfo
        {
            Sex = template.Sex,
            Race = template.Race,
            ModelId = (int[])template.ModelId.Clone(),
            HairColorIndex = template.HairColorIndex,
            HairColorRGB = template.HairColorRGB,
            HideEquipFlag = 0,
            TextureID = template.TextureID,
            SkinColor = template.SkinColor,
            Name = name,
            CreateTime = string.Empty,
            DeleteTime = string.Empty,
        };
    }

    public static async Task<CharacterTemplates> LoadAsync(string? telecaster, string prefix)
    {
        var found = new List<LobbyCharacterInfo>();
        if (telecaster is not null)
        {
            try
            {
                await using var connection = new NpgsqlConnection(telecaster);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("""
                    SELECT DISTINCT ON ("Race") "Race", "Sex", "Models", "HairColorIndex", "HairColorRgb", "TextureId", "SkinColor"
                    FROM "Characters"
                    WHERE "AccountName" NOT LIKE @prefix AND "Models" IS NOT NULL AND array_length("Models", 1) = 5
                    ORDER BY "Race", "Id"
                    """, connection);
                command.Parameters.AddWithValue("prefix", prefix + "%");
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    found.Add(new LobbyCharacterInfo
                    {
                        Race = reader.GetInt32(0),
                        Sex = reader.GetInt32(1),
                        ModelId = (int[])reader.GetValue(2),
                        HairColorIndex = reader.GetInt32(3),
                        HairColorRGB = unchecked((uint)reader.GetInt32(4)),
                        TextureID = reader.GetInt32(5),
                        SkinColor = unchecked((uint)reader.GetInt32(6)),
                    });
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Apparence : Telecaster illisible ({exception.Message}), apparence par défaut.");
            }
        }

        if (found.Count > 0) return new CharacterTemplates(found) { FromDatabase = true };

        // The model ids of the repository's own character tests: a fallback for an empty Telecaster only.
        return new CharacterTemplates(new List<LobbyCharacterInfo>
        {
            new() { Race = 4, Sex = 2, ModelId = new[] { 101, 205, 301, 401, 501 } },
            new() { Race = 5, Sex = 2, ModelId = new[] { 101, 205, 301, 401, 501 } },
            new() { Race = 3, Sex = 2, ModelId = new[] { 101, 205, 301, 401, 501 } },
        });
    }
}
