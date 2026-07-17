using Microsoft.Data.SqlClient;

namespace Navislamia.Tools.ExportFieldProps;

internal static class Sql
{
    public static SqlConnection Open(string server, string database)
    {
        var connection = new SqlConnection(
            $"Server={server};Database={database};Integrated Security=true;TrustServerCertificate=true");
        connection.Open();
        return connection;
    }
}

/// <summary>
/// Reads FieldPropResource, mirroring the reference server's ObjectMgr::LoadFieldPropResource.
/// Drop tables, use_count, regen_time and life_time are deliberately not read: no spawned warp gate
/// uses them.
/// </summary>
internal static class FieldPropResource
{
    private const int LimitDeva = 0x4;
    private const int LimitAsura = 0x8;
    private const int LimitGaia = 0x10;
    private const int LimitFighter = 0x400;
    private const int LimitHunter = 0x800;
    private const int LimitMagician = 0x1000;
    private const int LimitSummoner = 0x2000;

    private const string Query = """
        SELECT id, activate_id, casting_time, limit_min_level, limit_max_level,
               limit_deva, limit_asura, limit_gaia,
               limit_fighter, limit_hunter, limit_magician, limit_summoner, limit_job,
               activation_condition, activation_value1, activation_value2,
               activation2_condition, activation2_value1, activation2_value2,
               ISNULL(script_text, '') AS script_text
        FROM FieldPropResource
        """;

    public static Dictionary<int, PropTemplate> Read(string server, string database)
    {
        var templates = new Dictionary<int, PropTemplate>();

        using var connection = Sql.Open(server, database);
        using var command = new SqlCommand(Query, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var template = new PropTemplate
            {
                Id = Int(reader, "id"),
                ActivateSkillId = Int(reader, "activate_id"),

                // Every duration column in this data is in seconds; the reference loader reads
                // casting_time as GetUInt32() * 100 to reach ar_time ticks.
                CastingTime = Int(reader, "casting_time") * 100,

                MinLevel = Int(reader, "limit_min_level"),
                MaxLevel = Int(reader, "limit_max_level"),
                LimitJobId = Int(reader, "limit_job"),
                Script = reader["script_text"] as string ?? string.Empty
            };

            template.Limit = Limit(reader);
            AddActivation(template, Int(reader, "activation_condition"),
                Int(reader, "activation_value1"), Int(reader, "activation_value2"));
            AddActivation(template, Int(reader, "activation2_condition"),
                Int(reader, "activation2_value1"), Int(reader, "activation2_value2"));

            templates[template.Id] = template;
        }

        return templates;
    }

    private static void AddActivation(PropTemplate template, int condition, int value1, int value2)
    {
        if (condition == 0)
            return;

        template.Activations.Add(new PropActivation
        {
            Condition = condition,
            Value1 = value1,
            Value2 = value2
        });
    }

    private static int Limit(SqlDataReader reader)
    {
        var limit = 0;
        if (Int(reader, "limit_deva") != 0) limit |= LimitDeva;
        if (Int(reader, "limit_asura") != 0) limit |= LimitAsura;
        if (Int(reader, "limit_gaia") != 0) limit |= LimitGaia;
        if (Int(reader, "limit_fighter") != 0) limit |= LimitFighter;
        if (Int(reader, "limit_hunter") != 0) limit |= LimitHunter;
        if (Int(reader, "limit_magician") != 0) limit |= LimitMagician;
        if (Int(reader, "limit_summoner") != 0) limit |= LimitSummoner;
        return limit;
    }

    private static int Int(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }
}

/// <summary>
/// Reads the raid start position of each dungeon. This is what enter_dungeon warps to, which is an
/// approximation: the original Lua is not available, and the real entry models opening hours, party
/// and guild requirements that nothing here reproduces.
/// </summary>
internal static class DungeonResource
{
    private const string Query = """
        SELECT id, MAX(raid_start_pos_x) AS x, MAX(raid_start_pos_y) AS y
        FROM DungeonResource
        WHERE raid_start_pos_x > 0 AND raid_start_pos_y > 0
        GROUP BY id
        """;

    public static Dictionary<int, DungeonStart> Read(string server, string database)
    {
        var dungeons = new Dictionary<int, DungeonStart>();

        using var connection = Sql.Open(server, database);
        using var command = new SqlCommand(Query, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var dungeon = new DungeonStart
            {
                Id = Convert.ToInt32(reader["id"]),
                X = Convert.ToInt32(reader["x"]),
                Y = Convert.ToInt32(reader["y"])
            };

            dungeons[dungeon.Id] = dungeon;
        }

        return dungeons;
    }
}
