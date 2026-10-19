using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

if (args is ["--self-test"])
{
    const string secure = "Server=tcp:authored.database.windows.net,1433;Database=authored;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False";
    var checks = 0;
    foreach (var candidate in new[] { secure, secure.Replace("Encrypt=True", "Encrypt=False"),
        secure.Replace("TrustServerCertificate=False", "TrustServerCertificate=True"),
        secure.Replace("Active Directory Managed Identity", "Active Directory Default") })
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", Args = [] });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TYM_AUTH_ENABLED"] = "true", ["TYM_AUTH_SQL_CONNECTION"] = candidate,
            ["TYM_PUBLIC_API_URL"] = "https://api.example.test", ["TYM_PUBLIC_UI_URL"] = "https://ui.example.test"
        });
        try
        {
            var settings = builder.AddAccounts();
            if (candidate != secure) throw new Exception("Unsafe production SQL configuration accepted.");
            if (new SqlConnectionStringBuilder(settings.SqlConnectionString).Pooling) throw new Exception("Idle pooling enabled.");
            await using var app = builder.Build();
            // Mapping must not contact SQL: public reading and diagrams are independent of SQL availability.
            await app.MapAccounts();
            await using var scope = app.Services.CreateAsyncScope();
            var model = scope.ServiceProvider.GetRequiredService<AccountDb>();
            var schema = model.Database.GenerateCreateScript();
            if (model.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer"
                || !schema.Contains("CREATE TABLE [DataProtectionKeys]") || !schema.Contains("CREATE TABLE [AspNetUsers]"))
                throw new Exception("SQL identity/key schema missing.");
        }
        catch (InvalidOperationException) when (candidate != secure) { }
        checks++;
    }
    Console.WriteLine($"SQL account schema, offline startup and {checks} production transport/identity/pooling checks passed.");
    return;
}

if (args.Length != 4 || !Guid.TryParse(args[2], out var principalId) || args[3] is not ("initialize" or "check"))
    throw new ArgumentException("Usage: Tym.AccountSetup <SQL hostname> <database> <runtime principal GUID> initialize|check. Supply a short-lived administrator SQL access token on stdin.");

var connectionString = new SqlConnectionStringBuilder
{
    DataSource = "tcp:" + args[0] + ",1433", InitialCatalog = args[1],
    Encrypt = true, TrustServerCertificate = false, ConnectTimeout = 60, Pooling = false
};
await using var connection = new SqlConnection(connectionString.ToString())
    { AccessToken = (await Console.In.ReadToEndAsync()).Trim() };
var options = new DbContextOptionsBuilder<AccountDb>().UseSqlServer(connection,
    sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(10), null)).Options;
await using var db = new AccountDb(options);
if (args[3] == "initialize")
{
    await db.Database.EnsureCreatedAsync();
    // A contained SID avoids Graph lookup permissions and grants no schema/server administration.
    var sid = Convert.ToHexString(principalId.ToByteArray());
    await connection.OpenAsync();
    await using var grant = connection.CreateCommand();
    grant.CommandText = $"""
        IF DATABASE_PRINCIPAL_ID(N'tym-api-serban') IS NULL
            CREATE USER [tym-api-serban] WITH SID = 0x{sid}, TYPE = E;
        GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [tym-api-serban];
        """;
    await grant.ExecuteNonQueryAsync();
}
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
{
    connected = await db.Database.CanConnectAsync(),
    users = await db.Users.CountAsync(),
    confirmed = await db.Users.CountAsync(user => user.EmailConfirmed),
    protection_keys = await db.DataProtectionKeys.CountAsync()
}));
