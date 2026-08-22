using Npgsql;
using NUnit.Framework;

namespace Content.DiscordBot.Tests;

[TestFixture]
public sealed class ConfigurationLoaderTests
{
    [Test]
    public void PostgreSqlUriIsConvertedToNpgsqlConnectionString()
    {
        var normalized = ConfigurationLoader.NormalizePostgresConnectionString(
            "postgresql://court%20user:p%40ss@db.example:5544/ss14%20court?sslmode=Require");
        var builder = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Multiple(() =>
        {
            Assert.That(builder.Host, Is.EqualTo("db.example"));
            Assert.That(builder.Port, Is.EqualTo(5544));
            Assert.That(builder.Database, Is.EqualTo("ss14 court"));
            Assert.That(builder.Username, Is.EqualTo("court user"));
            Assert.That(builder.Password, Is.EqualTo("p@ss"));
            Assert.That(builder.SslMode, Is.EqualTo(SslMode.Require));
        });
    }

    [Test]
    public void NativeConnectionStringIsUnchanged()
    {
        const string connectionString = "Host=localhost;Database=ss14;Username=rucm";

        Assert.That(ConfigurationLoader.NormalizePostgresConnectionString(connectionString),
            Is.EqualTo(connectionString));
    }
}
