using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace Dashboard.Services;

public class AuthDbService
{
    private readonly string _connStr;

    public AuthDbService(IConfiguration config, IWebHostEnvironment env)
    {
        var dbPath = Path.Combine(env.ContentRootPath, "auth.db");
        _connStr = $"Data Source={dbPath}";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = Open();

        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = """
            CREATE TABLE IF NOT EXISTS users (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                hash     TEXT NOT NULL,
                salt     TEXT NOT NULL,
                created  TEXT NOT NULL
            )
            """;
        cmd1.ExecuteNonQuery();

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = """
            CREATE TABLE IF NOT EXISTS tokens (
                token    TEXT PRIMARY KEY,
                user_id  INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                username TEXT NOT NULL,
                created  TEXT NOT NULL,
                expires  TEXT NOT NULL
            )
            """;
        cmd2.ExecuteNonQuery();
    }

    public async Task<(bool ok, string error)> RegisterAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            return (false, "Логін повинен містити щонайменше 3 символи");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return (false, "Пароль повинен містити щонайменше 6 символів");

        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Hash(password, salt);
        var saltB64 = Convert.ToBase64String(salt);

        await using var conn = await OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users (username, hash, salt, created)
            VALUES ($u, $h, $s, $c)
            """;
        cmd.Parameters.AddWithValue("$u", username.Trim());
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$s", saltB64);
        cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
        try
        {
            await cmd.ExecuteNonQueryAsync();
            return (true, "");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // UNIQUE constraint
        {
            return (false, "Такий логін вже існує");
        }
    }

    public async Task<(bool ok, string token, string error)> LoginAsync(string username, string password)
    {
        await using var conn = await OpenAsync();

        // fetch user
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, hash, salt FROM users WHERE username = $u COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$u", username.Trim());
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync())
            return (false, "", "Невірний логін або пароль");

        long userId = rdr.GetInt64(0);
        string storedHash = rdr.GetString(1);
        byte[] salt = Convert.FromBase64String(rdr.GetString(2));
        await rdr.CloseAsync();

        if (Hash(password, salt) != storedHash)
            return (false, "", "Невірний логін або пароль");

        // generate token
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var expires = DateTime.UtcNow.AddDays(30);

        await using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO tokens (token, user_id, username, created, expires)
            VALUES ($t, $uid, $u, $c, $e)
            """;
        ins.Parameters.AddWithValue("$t", rawToken);
        ins.Parameters.AddWithValue("$uid", userId);
        ins.Parameters.AddWithValue("$u", username.Trim());
        ins.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
        ins.Parameters.AddWithValue("$e", expires.ToString("o"));
        await ins.ExecuteNonQueryAsync();

        return (true, rawToken, "");
    }

    public async Task<(bool valid, string username)> ValidateTokenAsync(string token)
    {
        await using var conn = await OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT username, expires FROM tokens WHERE token = $t";
        cmd.Parameters.AddWithValue("$t", token);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return (false, "");

        string username = rdr.GetString(0);
        var expires = DateTime.Parse(rdr.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind);
        return expires > DateTime.UtcNow ? (true, username) : (false, "");
    }

    public async Task RevokeTokenAsync(string token)
    {
        await using var conn = await OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tokens WHERE token = $t";
        cmd.Parameters.AddWithValue("$t", token);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string Hash(string password, byte[] salt) =>
        Convert.ToBase64String(
            Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32));

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connStr);
        conn.Open();
        return conn;
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();
        return conn;
    }
}
