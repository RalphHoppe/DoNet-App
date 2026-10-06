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
/// A context is created per operation rather than kept open. Desktop apps that hold one
/// context for the process lifetime accumulate tracked entities and start returning
/// stale data; the cost of opening is paid once at warm-up, during the welcome screen.
/// </remarks>
public sealed class DoNetDbContext : DbContext
{
    private readonly string _path;
    private readonly string _key;

    public DoNetDbContext(string path, string key)
    {
        _path = path;
        _key = key;
    }

    public DbSet<Person> People => Set<Person>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,

            // Password makes Microsoft.Data.Sqlite issue PRAGMA key on open, which is
            // what actually engages SQLCipher. Without it the same provider silently
            // creates a perfectly readable database.
            Password = _key,

            // Pooling off: a pooled connection would be handed back out still keyed,
            // which would outlive Lock().
            Pooling = false,
        };

        options.UseSqlite(builder.ToString());
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
