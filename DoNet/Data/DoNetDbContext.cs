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

    public DbSet<Website> Websites => Set<Website>();

    /// <summary>Payment methods the user added themselves. The six built-ins are in code.</summary>
    public DbSet<PaymentMethodOption> PaymentMethodOptions => Set<PaymentMethodOption>();

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

            // No explicit index on Id. An INTEGER PRIMARY KEY in SQLite *is* the table's
            // rowid, so ordering and seeking by it are already free; adding an index
            // would create a second B-tree holding the same keys and charge every insert
            // and delete to maintain it. The directory's only other access pattern is a
            // LIKE '%term%' search, which no index can serve.
        });

        model.Entity<Website>(entity =>
        {
            entity.ToTable("Websites");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Id).ValueGeneratedOnAdd();

            // The selected methods are one delimited column; see Website for why a
            // join table would be the wrong trade here.
            entity.Property(w => w.PaymentMethodsRaw);
        });

        model.Entity<PaymentMethodOption>(entity =>
        {
            entity.ToTable("PaymentMethodOptions");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Id).ValueGeneratedOnAdd();
            entity.Property(o => o.Name).IsRequired();

            // Unlike the record tables, this one is looked up by name on every save,
            // so the index earns its keep.
            entity.HasIndex(o => o.Name);
        });
    }
}
