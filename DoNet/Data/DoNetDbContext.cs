using DoNet.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DoNet.Data;

/// <summary>
/// The encrypted person database.
/// </summary>
/// <remarks>
/// SQLCipher encrypts the whole file - pages, indexes, the schema and therefore the
/// column names too. That is why the entity below can use readable property names: the
/// requirement that column names not be legible is met by the file encryption, not by
/// obfuscating the schema. An attacker with the file and no key sees random bytes.
///
/// The context does not own its connection. <see cref="DoNet.Services.PersonDirectoryService"/>
/// opens and keys one connection for the unlocked session and hands it in here, because
/// SQLCipher runs 256,000 rounds of PBKDF2 on every open and paying that per context -
/// which is what happens if the context builds its own connection from a path and a key -
/// makes the app feel broken. A short-lived context over a long-lived connection keeps
/// the change-tracker clean without re-deriving the key.
/// </remarks>
public sealed class DoNetDbContext : DbContext
{
    private readonly SqliteConnection _connection;

    /// <param name="connection">
    /// An open, keyed connection owned by the caller. EF will not close or dispose a
    /// connection it did not open, so the handle survives this context.
    /// </param>
    public DoNetDbContext(SqliteConnection connection)
    {
        _connection = connection;
    }

    public DbSet<Person> People => Set<Person>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite(_connection);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Person>(entity =>
        {
            entity.ToTable("People");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedOnAdd();

            // Indexed because the directory sorts by it on every page.
            entity.HasIndex(p => p.Id);
        });
    }
}
