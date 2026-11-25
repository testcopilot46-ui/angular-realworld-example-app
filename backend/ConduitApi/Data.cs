using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ConduitApi;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? Bio { get; set; }
    public string? Image { get; set; }
    public List<Follow> Followers { get; set; } = [];
}

public class Follow
{
    public int FollowerId { get; set; }
    public User Follower { get; set; } = null!;
    public int FolloweeId { get; set; }
    public User Followee { get; set; } = null!;
}

public class Article
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;
    public List<ArticleTag> Tags { get; set; } = [];
    public List<Favorite> Favorites { get; set; } = [];
}

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<ArticleTag> Articles { get; set; } = [];
}

public class ArticleTag
{
    public int ArticleId { get; set; }
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
    public int Position { get; set; }
}

public class Favorite
{
    public int ArticleId { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}

public class Comment
{
    public int Id { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int ArticleId { get; set; }
    public Article Article { get; set; } = null!;
    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;
}

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ArticleTag> ArticleTags => Set<ArticleTag>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Comment> Comments => Set<Comment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.Properties<DateTime>().HaveConversion<UtcConverter>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(u => u.Username).HasMaxLength(100);
            e.Property(u => u.Email).HasMaxLength(256);
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
        });

        b.Entity<Follow>(e =>
        {
            e.HasKey(f => new { f.FollowerId, f.FolloweeId });
            e.HasOne(f => f.Follower).WithMany().HasForeignKey(f => f.FollowerId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(f => f.Followee).WithMany(u => u.Followers).HasForeignKey(f => f.FolloweeId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Article>(e =>
        {
            e.Property(a => a.Slug).HasMaxLength(300);
            e.HasIndex(a => a.Slug).IsUnique();
            e.HasIndex(a => a.CreatedAt);
            e.HasOne(a => a.Author).WithMany().HasForeignKey(a => a.AuthorId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Tag>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(100);
            e.HasIndex(t => t.Name).IsUnique();
        });

        b.Entity<ArticleTag>(e =>
        {
            e.HasKey(t => new { t.ArticleId, t.TagId });
            e.HasOne<Article>().WithMany(a => a.Tags).HasForeignKey(t => t.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Tag).WithMany(t => t.Articles).HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Favorite>(e =>
        {
            e.HasKey(f => new { f.ArticleId, f.UserId });
            e.HasOne<Article>().WithMany(a => a.Favorites).HasForeignKey(f => f.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Comment>(e =>
        {
            e.HasOne(c => c.Article).WithMany().HasForeignKey(c => c.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Author).WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private class UtcConverter() : ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
