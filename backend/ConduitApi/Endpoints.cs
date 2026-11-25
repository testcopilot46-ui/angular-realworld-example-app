using System.Linq.Expressions;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ConduitApi;

public static partial class Endpoints
{
    private const int NoUser = -1;

    public static void MapConduitApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/users", Register);
        api.MapPost("/users/login", Login);
        api.MapGet("/user", GetCurrentUser);
        api.MapPut("/user", UpdateCurrentUser);

        api.MapGet("/profiles/{username}", GetProfile);
        api.MapPost("/profiles/{username}/follow", (string username, HttpContext ctx, AppDb db, TokenService ts, IWebHostEnvironment env) => SetFollow(username, true, ctx, db, ts, env));
        api.MapDelete("/profiles/{username}/follow", (string username, HttpContext ctx, AppDb db, TokenService ts, IWebHostEnvironment env) => SetFollow(username, false, ctx, db, ts, env));

        api.MapGet("/articles", ListArticles);
        api.MapGet("/articles/feed", Feed);
        api.MapPost("/articles", CreateArticle);
        api.MapGet("/articles/{slug}", GetArticle);
        api.MapPut("/articles/{slug}", UpdateArticle);
        api.MapDelete("/articles/{slug}", DeleteArticle);
        api.MapPost("/articles/{slug}/favorite", (string slug, HttpContext ctx, AppDb db, TokenService ts, IWebHostEnvironment env) => SetFavorite(slug, true, ctx, db, ts, env));
        api.MapDelete("/articles/{slug}/favorite", (string slug, HttpContext ctx, AppDb db, TokenService ts, IWebHostEnvironment env) => SetFavorite(slug, false, ctx, db, ts, env));

        api.MapGet("/articles/{slug}/comments", ListComments);
        api.MapPost("/articles/{slug}/comments", AddComment);
        api.MapDelete("/articles/{slug}/comments/{id}", DeleteComment);

        api.MapGet("/tags", ListTags);
        api.MapPost("/users/logout", Logout);
    }

    // ---------- Users ----------

    private static async Task<IResult> Register(HttpContext ctx, AppDb db, TokenService ts, IPasswordHasher<User> hasher, IWebHostEnvironment env)
    {
        var o = await ReadBody(ctx, "user");
        if (o is null) return InvalidBody();

        var username = Str(o, "username")?.Trim();
        var email = Str(o, "email")?.Trim();
        var password = Str(o, "password");

        var errors = new Errors();
        if (string.IsNullOrWhiteSpace(username)) errors.Add("username", "can't be blank");
        else if (username.Length > 100) errors.Add("username", "is too long (maximum is 100 characters)");
        else if (!System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-zA-Z0-9_-]+$")) errors.Add("username", "can only contain letters, numbers, underscores, and hyphens");
        ValidateEmail(email, errors);
        ValidatePassword(password, errors);
        if (errors.Any) return errors.Result(422);

        if (await db.Users.AnyAsync(u => u.Username == username)) errors.Add("username", "has already been taken");
        if (await db.Users.AnyAsync(u => u.Email == email)) errors.Add("email", "has already been taken");
        if (errors.Any) return errors.Result(409);

        var user = new User { Username = username!, Email = email! };
        user.PasswordHash = hasher.HashPassword(user, password!);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Handle race condition: another request created the user between our check and insert
            db.Entry(user).State = EntityState.Detached;
            
            // Re-check which field conflicts
            if (await db.Users.AnyAsync(u => u.Username == username))
                errors.Add("username", "has already been taken");
            if (await db.Users.AnyAsync(u => u.Email == email))
                errors.Add("email", "has already been taken");
            
            if (errors.Any) return errors.Result(409);
            
            // If no specific field conflicts, rethrow
            throw;
        }

        var token = ts.Create(user);
        TokenService.SetJwtCookie(ctx, token, env);
        return Results.Json(new { user = ToDto(user) }, statusCode: 201);
    }

    private static async Task<IResult> Login(HttpContext ctx, AppDb db, TokenService ts, IPasswordHasher<User> hasher, IWebHostEnvironment env)
    {
        var o = await ReadBody(ctx, "user");
        if (o is null) return InvalidBody();

        var email = Str(o, "email")?.Trim();
        var password = Str(o, "password");

        var errors = new Errors();
        ValidateEmail(email, errors);
        if (string.IsNullOrEmpty(password)) errors.Add("password", "can't be blank");
        if (errors.Any) return errors.Result(422);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        var check = user is null ? PasswordVerificationResult.Failed : hasher.VerifyHashedPassword(user, user.PasswordHash, password!);
        if (check == PasswordVerificationResult.Failed) return Error(401, "credentials", "invalid");

        if (check == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user!.PasswordHash = hasher.HashPassword(user, password!);
            await db.SaveChangesAsync();
        }

        var token = ts.Create(user!);
        TokenService.SetJwtCookie(ctx, token, env);
        return Results.Ok(new { user = ToDto(user!) });
    }

    private static IResult Logout(HttpContext ctx)
    {
        TokenService.ClearJwtCookie(ctx);
        return Results.Ok(new { });
    }

    private static async Task<IResult> GetCurrentUser(HttpContext ctx, AppDb db, TokenService ts)
    {
        var (user, err) = await RequireUser(ctx, db, ts);
        if (user is null) return err!;
        return Results.Ok(new { user = ToDto(user) });
    }

    private static async Task<IResult> UpdateCurrentUser(HttpContext ctx, AppDb db, TokenService ts, IPasswordHasher<User> hasher)
    {
        var (user, err) = await RequireUser(ctx, db, ts);
        if (user is null) return err!;

        var o = await ReadBody(ctx, "user");
        if (o is null) return InvalidBody();

        var errors = new Errors();
        string? username = null, email = null, password = null;

        if (o.ContainsKey("username"))
        {
            username = Str(o, "username")?.Trim();
            if (string.IsNullOrWhiteSpace(username)) errors.Add("username", "can't be blank");
            else if (username.Length > 100) errors.Add("username", "is too long (maximum is 100 characters)");
            else if (!System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-zA-Z0-9_-]+$")) errors.Add("username", "can only contain letters, numbers, underscores, and hyphens");
        }
        if (o.ContainsKey("email"))
        {
            email = Str(o, "email")?.Trim();
            ValidateEmail(email, errors);
        }
        if (o.ContainsKey("password"))
        {
            password = Str(o, "password");
            ValidatePassword(password, errors);
        }
        if (o.ContainsKey("bio"))
        {
            var bio = Str(o, "bio");
            if (bio != null && bio.Length > 1000) errors.Add("bio", "is too long (maximum is 1000 characters)");
        }
        if (o.ContainsKey("image"))
        {
            var image = Str(o, "image");
            if (image != null && image.Length > 500) errors.Add("image", "is too long (maximum is 500 characters)");
        }
        if (errors.Any) return errors.Result(422);

        if (username is not null && await db.Users.AnyAsync(u => u.Username == username && u.Id != user.Id))
            errors.Add("username", "has already been taken");
        if (email is not null && await db.Users.AnyAsync(u => u.Email == email && u.Id != user.Id))
            errors.Add("email", "has already been taken");
        if (errors.Any) return errors.Result(409);

        if (username is not null) user.Username = username;
        if (email is not null) user.Email = email;
        if (password is not null) user.PasswordHash = hasher.HashPassword(user, password);
        if (o.ContainsKey("bio")) user.Bio = NullIfEmpty(Str(o, "bio"));
        if (o.ContainsKey("image")) user.Image = NullIfEmpty(Str(o, "image"));
        await db.SaveChangesAsync();

        return Results.Ok(new { user = ToDto(user) });
    }

    // ---------- Profiles ----------

    private static async Task<IResult> GetProfile(string username, HttpContext ctx, AppDb db, TokenService ts)
    {
        var me = await OptionalUserId(ctx, db, ts);
        var profile = await db.Users.Where(u => u.Username == username).Select(ProfileOf(me)).FirstOrDefaultAsync();
        return profile is null ? Error(404, "profile", "not found") : Results.Ok(new { profile });
    }

    private static async Task<IResult> SetFollow(string username, bool follow, HttpContext ctx, AppDb db, TokenService ts, IWebHostEnvironment env)
    {
        var (me, err) = await RequireUser(ctx, db, ts);
        if (me is null) return err!;

        var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (target is null) return Error(404, "profile", "not found");

        var existing = await db.Follows.FindAsync(me.Id, target.Id);
        if (follow && existing is null && target.Id != me.Id) db.Follows.Add(new Follow { FollowerId = me.Id, FolloweeId = target.Id });
        if (!follow && existing is not null) db.Follows.Remove(existing);
        await db.SaveChangesAsync();

        var profile = await db.Users.Where(u => u.Id == target.Id).Select(ProfileOf(me.Id)).FirstAsync();
        return Results.Ok(new { profile });
    }

    // ---------- Articles ----------

    private static async Task<IResult> ListArticles(HttpContext ctx, AppDb db, TokenService ts, string? tag, string? author, string? favorited, int? limit, int? offset)
    {
        var me = await OptionalUserId(ctx, db, ts);
        var q = db.Articles.AsQueryable();
        if (!string.IsNullOrEmpty(tag)) q = q.Where(a => a.Tags.Any(t => t.Tag.Name == tag));
        if (!string.IsNullOrEmpty(author)) q = q.Where(a => a.Author.Username == author);
        if (!string.IsNullOrEmpty(favorited)) q = q.Where(a => a.Favorites.Any(f => f.User.Username == favorited));
        return await Page(q, me, limit, offset);
    }

    private static async Task<IResult> Feed(HttpContext ctx, AppDb db, TokenService ts, int? limit, int? offset)
    {
        var (me, err) = await RequireUser(ctx, db, ts);
        if (me is null) return err!;
        var q = db.Articles.Where(a => a.Author.Followers.Any(f => f.FollowerId == me.Id));
        return await Page(q, me.Id, limit, offset);
    }

    private static async Task<IResult> GetArticle(string slug, HttpContext ctx, AppDb db, TokenService ts)
    {
        var me = await OptionalUserId(ctx, db, ts);
        var article = await LoadArticle(db, slug, me);
        return article is null ? ArticleNotFound() : Results.Ok(new { article });
    }

    private static async Task<IResult> CreateArticle(HttpContext ctx, AppDb db, TokenService ts)
    {
        var (me, err) = await RequireUser(ctx, db, ts);
        if (me is null) return err!;

        var o = await ReadBody(ctx, "article");
        if (o is null) return InvalidBody();

        var errors = new Errors();
        var title = Str(o, "title")?.Trim();
        var description = Str(o, "description")?.Trim();
        var body = Str(o, "body");
        if (string.IsNullOrWhiteSpace(title)) errors.Add("title", "can't be blank");
        else if (title.Length > 200) errors.Add("title", "is too long (maximum is 200 characters)");
        if (string.IsNullOrWhiteSpace(description)) errors.Add("description", "can't be blank");
        else if (description.Length > 500) errors.Add("description", "is too long (maximum is 500 characters)");
        if (string.IsNullOrWhiteSpace(body)) errors.Add("body", "can't be blank");
        else if (body.Length > 10000) errors.Add("body", "is too long (maximum is 10000 characters)");
        var tags = o.ContainsKey("tagList") && o["tagList"] is not null ? ReadTags(o, errors) : [];
        if (errors.Any) return errors.Result(422);

        // Deduplicate tags case-insensitively to avoid DB conflicts
        if (tags != null)
        {
            tags = tags.GroupBy(t => t.ToLowerInvariant()).Select(g => g.First()).ToList();
        }

        var now = Now();
        var article = new Article
        {
            Slug = await UniqueSlug(db, title!),
            Title = title!,
            Description = description!,
            Body = body!,
            CreatedAt = now,
            UpdatedAt = now,
            AuthorId = me.Id,
        };
        await SetTags(db, article, tags!);
        db.Articles.Add(article);

        // Retry on unique constraint violation (concurrent slug or tag conflicts)
        await WithDbRetryAsync(async () =>
        {
            await db.SaveChangesAsync();
            return true;
        });

        return Results.Json(new { article = await LoadArticle(db, article.Slug, me.Id) }, statusCode: 201);
    }

    private static async Task<IResult> UpdateArticle(string slug, HttpContext ctx, AppDb db, TokenService ts)
    {
        var (me, err) = await RequireUser(ctx, db, ts);
        if (me is null) return err!;

        var article = await db.Articles.Include(a => a.Tags).FirstOrDefaultAsync(a => a.Slug == slug);
        if (article is null) return ArticleNotFound();
        if (article.AuthorId != me.Id) return Error(403, "article", "forbidden");

        var o = await ReadBody(ctx, "article");
        if (o is null) return InvalidBody();

        var errors = new Errors();
        string? title = null, description = null, body = null;
        List<string>? tags = null;
        if (o.ContainsKey("title"))
        {
            title = Str(o, "title")?.Trim();
            if (string.IsNullOrWhiteSpace(title)) errors.Add("title", "can't be blank");
        }
        if (o.ContainsKey("description"))
        {
            description = Str(o, "description")?.Trim();
            if (string.IsNullOrWhiteSpace(description)) errors.Add("description", "can't be blank");
        }
        if (o.ContainsKey("body"))
        {
            body = Str(o, "body");
            if (string.IsNullOrWhiteSpace(body)) errors.Add("body", "can't be blank");
        }
        if (o.ContainsKey("tagList"))
        {
            tags = ReadTags(o, errors);
            // Deduplicate tags case-insensitively
            if (tags != null)
            {
                tags = tags.GroupBy(t => t.ToLowerInvariant()).Select(g => g.First()).ToList();
            }
        }
        if (errors.Any) return errors.Result(422);

        if (title is not null) article.Title = title;
        if (description is not null) article.Description = description;
        if (body is not null) article.Body = body;
        if (tags is not null)
        {
            article.Tags.Clear();
            await SetTags(db, article, tags);
        }
        article.UpdatedAt = Now();
        await db.SaveChangesAsync();

        return Results.Ok(new { article = await LoadArticle(db, article.Slug, me.Id) });
    }

    private static async Task<IResult> DeleteArticle(string slug, HttpContext ctx, AppDb db, TokenService ts)
    {
        var (me, err) = await RequireUser(ctx, db, ts);
        if (me is null) return err!;

        var article = await db.Articles.FirstOrDefaultAsync(a => a.Slug == slug);
        if (article is null) return ArticleNotFound();
        if (article.AuthorId != me.Id) return Error(403, "article", "forbidden");

        db.Articles.Remove(article);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> SetFavorite(string slug, bool favorite, HttpContext ctx, AppDb db, TokenService ts, IWebHostEnvironment env)
    {
        var (me, err) = await RequireUser(ctx, db, ts);
        if (me is null) return err!;

        var article = await db.Articles.FirstOrDefaultAsync(a => a.Slug == slug);
        if (article is null) return ArticleNotFound();

        var existing = await db.Favorites.FindAsync(article.Id, me.Id);
        if (favorite && existing is null) db.Favorites.Add(new Favorite { ArticleId = article.Id, UserId = me.Id });
        if (!favorite && existing is not null) db.Favorites.Remove(existing);
        await db.SaveChangesAsync();

        return Results.Ok(new { article = await LoadArticle(db, slug, me.Id) });
    }

    // ---------- Comments ----------

    private static async Task<IResult> ListComments(string slug, HttpContext ctx, AppDb db, TokenService ts)
    {
        var me = await OptionalUserId(ctx, db, ts);
        var articleId = await db.Articles.Where(a => a.Slug == slug).Select(a => (int?)a.Id).FirstOrDefaultAsync();
        if (articleId is null) return ArticleNotFound();

        var comments = await db.Comments.Where(c => c.ArticleId == articleId).OrderBy(c => c.Id).Select(CommentOf(me)).ToListAsync();
        return Results.Ok(new { comments });
    }

    private static async Task<IResult> AddComment(string slug, HttpContext ctx, AppDb db, TokenService ts)
    {
        var (me, err) = await RequireUser(ctx, db, ts);
        if (me is null) return err!;

        var articleId = await db.Articles.Where(a => a.Slug == slug).Select(a => (int?)a.Id).FirstOrDefaultAsync();
        if (articleId is null) return ArticleNotFound();

        var o = await ReadBody(ctx, "comment");
        if (o is null) return InvalidBody();
        var body = Str(o, "body");
        if (string.IsNullOrWhiteSpace(body)) return Error(422, "body", "can't be blank");

        var now = Now();
        var comment = new Comment { Body = body, CreatedAt = now, UpdatedAt = now, ArticleId = articleId.Value, AuthorId = me.Id };
        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        var dto = await db.Comments.Where(c => c.Id == comment.Id).Select(CommentOf(me.Id)).FirstAsync();
        return Results.Json(new { comment = dto }, statusCode: 201);
    }

    private static async Task<IResult> DeleteComment(string slug, string id, HttpContext ctx, AppDb db, TokenService ts)
    {
        var (me, err) = await RequireUser(ctx, db, ts);
        if (me is null) return err!;

        var articleId = await db.Articles.Where(a => a.Slug == slug).Select(a => (int?)a.Id).FirstOrDefaultAsync();
        if (articleId is null) return ArticleNotFound();

        var comment = int.TryParse(id, out var commentId)
            ? await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId && c.ArticleId == articleId)
            : null;
        if (comment is null) return Error(404, "comment", "not found");
        if (comment.AuthorId != me.Id) return Error(403, "comment", "forbidden");

        db.Comments.Remove(comment);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---------- Tags ----------

    private static async Task<IResult> ListTags(AppDb db)
    {
        var tags = await db.Tags
            .Where(t => t.Articles.Any())
            .OrderByDescending(t => t.Articles.Count)
            .ThenBy(t => t.Name)
            .Select(t => t.Name)
            .ToListAsync();
        return Results.Ok(new { tags });
    }

    // ---------- Helpers ----------

    private static async Task<(User? user, IResult? error)> RequireUser(HttpContext ctx, AppDb db, TokenService ts)
    {
        var token = TokenService.ReadToken(ctx);
        if (token is null) return (null, Error(401, "token", "is missing"));
        var id = ts.Validate(token);
        var user = id is null ? null : await db.Users.FindAsync(id.Value);
        return user is null ? (null, Error(401, "token", "is invalid")) : (user, null);
    }

    private static async Task<int> OptionalUserId(HttpContext ctx, AppDb db, TokenService ts)
    {
        var token = TokenService.ReadToken(ctx);
        var id = token is null ? null : ts.Validate(token);
        return id is not null && await db.Users.AnyAsync(u => u.Id == id) ? id.Value : NoUser;
    }

    private static async Task<IResult> Page(IQueryable<Article> q, int me, int? limit, int? offset)
    {
        var count = await q.CountAsync();
        var articles = await q
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip(Math.Max(offset ?? 0, 0))
            .Take(Math.Clamp(limit ?? 20, 0, 100))
            .Select(ArticleOf(me, withBody: false))
            .ToListAsync();
        return Results.Ok(new { articles, articlesCount = count });
    }

    private static Task<ArticleDto?> LoadArticle(AppDb db, string slug, int me) =>
        db.Articles.Where(a => a.Slug == slug).Select(ArticleOf(me, withBody: true)).FirstOrDefaultAsync();

    private static Expression<Func<Article, ArticleDto>> ArticleOf(int me, bool withBody) => a => new ArticleDto(
        a.Slug,
        a.Title,
        a.Description,
        withBody ? a.Body : null,
        a.Tags.OrderBy(t => t.Position).Select(t => t.Tag.Name).ToList(),
        a.CreatedAt,
        a.UpdatedAt,
        a.Favorites.Any(f => f.UserId == me),
        a.Favorites.Count,
        new ProfileDto(a.Author.Username, a.Author.Bio, a.Author.Image, a.Author.Followers.Any(f => f.FollowerId == me)));

    private static Expression<Func<User, ProfileDto>> ProfileOf(int me) =>
        u => new ProfileDto(u.Username, u.Bio, u.Image, u.Followers.Any(f => f.FollowerId == me));

    private static Expression<Func<Comment, CommentDto>> CommentOf(int me) => c => new CommentDto(
        c.Id,
        c.CreatedAt,
        c.UpdatedAt,
        c.Body,
        new ProfileDto(c.Author.Username, c.Author.Bio, c.Author.Image, c.Author.Followers.Any(f => f.FollowerId == me)));

    private static List<string>? ReadTags(JsonObject o, Errors errors)
    {
        if (o["tagList"] is not JsonArray arr || arr.Any(t => t is not JsonValue v || !v.TryGetValue<string>(out _)))
        {
            errors.Add("tagList", "must be an array of strings");
            return null;
        }
        
        var tags = arr.Select(t => t!.GetValue<string>().Trim()).Where(t => t.Length > 0).Distinct().ToList();
        
        // Validate tag lengths
        foreach (var tag in tags)
        {
            if (tag.Length > 100)
            {
                errors.Add("tagList", $"tag '{tag.Substring(0, Math.Min(20, tag.Length))}...' is too long (maximum is 100 characters)");
            }
        }
        
        return tags;
    }

    private static async Task SetTags(AppDb db, Article article, List<string> names)
    {
        var existing = await db.Tags.Where(t => names.Contains(t.Name)).ToListAsync();
        for (var i = 0; i < names.Count; i++)
        {
            var tag = existing.FirstOrDefault(t => string.Equals(t.Name, names[i], StringComparison.OrdinalIgnoreCase)) ?? new Tag { Name = names[i] };
            if (article.Tags.Any(at => at.Tag == tag)) continue;
            article.Tags.Add(new ArticleTag { Tag = tag, Position = i });
        }
    }

    private static async Task<string> UniqueSlug(AppDb db, string title)
    {
        var baseSlug = NonSlugChars().Replace(title.ToLowerInvariant(), "-").Trim('-');
        if (baseSlug.Length == 0) baseSlug = "article";
        if (baseSlug.Length > 200) baseSlug = baseSlug[..200].TrimEnd('-');

        var slug = baseSlug;
        // Reserve "feed" to avoid conflict with feed endpoint
        if (slug == "feed")
        {
            slug = $"{baseSlug}-article";
        }
        while (await db.Articles.AnyAsync(a => a.Slug == slug))
            slug = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..6]}";
        return slug;
    }

    private static void ValidateEmail(string? email, Errors errors)
    {
        if (string.IsNullOrWhiteSpace(email)) errors.Add("email", "can't be blank");
        else if (email.Length > 256) errors.Add("email", "is too long (maximum is 256 characters)");
        else if (!EmailPattern().IsMatch(email)) errors.Add("email", "is invalid");
    }

    private static void ValidatePassword(string? password, Errors errors)
    {
        if (string.IsNullOrEmpty(password)) errors.Add("password", "can't be blank");
        else if (password.Length < 8) errors.Add("password", "is too short (minimum is 8 characters)");
        else if (password.Length > 256) errors.Add("password", "is too long (maximum is 256 characters)");
    }

    private static async Task<JsonObject?> ReadBody(HttpContext ctx, string root)
    {
        try
        {
            var node = await JsonNode.ParseAsync(ctx.Request.Body);
            return node?[root] as JsonObject;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static DateTime Now()
    {
        var ticks = DateTime.UtcNow.Ticks;
        return new DateTime(ticks - ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    private static UserDto ToDto(User u) => new(u.Username, u.Email, u.Bio, u.Image);

    /// <summary>
    /// Execute an operation with retry logic for database unique constraint violations.
    /// Retries up to 3 times with exponential backoff for transient conflicts.
    /// </summary>
    private static async Task<T> WithDbRetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex) && attempt < maxRetries - 1)
            {
                // Wait with exponential backoff before retry
                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)));
            }
        }
        // Final attempt - let the exception bubble up
        return await operation();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // Check for SQL Server unique constraint violation (error 2601 or 2627)
        var innerException = ex.InnerException as Microsoft.Data.SqlClient.SqlException;
        if (innerException != null)
        {
            return innerException.Number == 2601 || innerException.Number == 2627;
        }
        return false;
    }

    private static IResult Error(int status, string field, string message) =>
        Results.Json(new { errors = new Dictionary<string, string[]> { [field] = [message] } }, statusCode: status);

    private static IResult ArticleNotFound() => Error(404, "article", "not found");

    private static IResult InvalidBody() => Error(422, "body", "is invalid JSON or missing the root object");

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    private class Errors
    {
        private readonly Dictionary<string, List<string>> _errors = [];
        public bool Any => _errors.Count > 0;
        public void Add(string field, string message)
        {
            if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
            list.Add(message);
        }
        public IResult Result(int status) => Results.Json(new { errors = _errors }, statusCode: status);
    }
}
