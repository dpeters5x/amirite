using System.Reflection;
using AmIRite.Web.Data;
using AmIRite.Web.Models;
using Dapper;

namespace AmIRite.Web.Services;

public class QuestionService(IDbConnectionFactory db)
{
    // -- Question selection (weighted random draw without replacement) --

    public async Task<IReadOnlyList<Question>> SelectForRoundAsync(string sessionId, int count)
    {
        using var conn = db.Create();

        // Questions eligible: active, at least one category with weight > 0 in this session,
        // and not already used in a prior round of this session.
        var eligible = (await conn.QueryAsync<(int Id, string Text, double MaxWeight)>(
            """
            SELECT q.id, q.text,
                   MAX(sc.weight) AS max_weight
            FROM questions q
            JOIN question_categories qc ON qc.question_id = q.id
            JOIN session_categories sc
              ON sc.session_id = @sessionId AND sc.category_id = qc.category_id AND sc.weight > 0
            WHERE q.active = 1
              AND q.id NOT IN (
                  SELECT rq.question_id
                  FROM round_questions rq
                  JOIN rounds r ON r.id = rq.round_id
                  WHERE r.session_id = @sessionId
              )
            GROUP BY q.id, q.text
            """,
            new { sessionId })).ToList();

        if (eligible.Count == 0) return Array.Empty<Question>();

        // Weighted random draw without replacement
        var selected = new List<(int Id, string Text)>();
        var pool = eligible.Select(e => (e.Id, e.Text, Weight: e.MaxWeight)).ToList();

        for (var i = 0; i < count && pool.Count > 0; i++)
        {
            var totalWeight = pool.Sum(p => p.Weight);
            var roll = Random.Shared.NextDouble() * totalWeight;
            var cumulative = 0.0;

            for (var j = 0; j < pool.Count; j++)
            {
                cumulative += pool[j].Weight;
                if (roll <= cumulative)
                {
                    selected.Add((pool[j].Id, pool[j].Text));
                    pool.RemoveAt(j);
                    break;
                }
            }
        }

        // Hydrate full Question objects
        if (selected.Count == 0) return Array.Empty<Question>();

        var ids = selected.Select(s => s.Id).ToList();
        var questions = (await conn.QueryAsync<Question>(
            $"SELECT * FROM questions WHERE id IN ({string.Join(",", ids)})")).ToList();

        // Preserve selection order
        return selected
            .Select(s => questions.First(q => q.Id == s.Id))
            .ToList();
    }

    // -- Category helpers --

    public async Task<IEnumerable<Category>> GetActiveCategoriesAsync()
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Category>(
            "SELECT * FROM categories WHERE active = 1 ORDER BY name");
    }

    public async Task<IEnumerable<Category>> GetAllCategoriesAsync()
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Category>("SELECT * FROM categories ORDER BY name");
    }

    public async Task<IEnumerable<Preset>> GetActivePresetsAsync()
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Preset>(
            "SELECT * FROM presets WHERE active = 1 ORDER BY sort_order");
    }

    public async Task<IEnumerable<(int PresetId, int CategoryId)>> GetPresetCategoriesAsync()
    {
        using var conn = db.Create();
        var rows = await conn.QueryAsync("SELECT preset_id, category_id FROM preset_categories");
        return rows.Select(r => ((int)r.preset_id, (int)r.category_id));
    }

    // -- Admin CRUD --

    public async Task<Question?> GetByIdAsync(int id)
    {
        using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<Question>(
            "SELECT * FROM questions WHERE id = @id", new { id });
    }

    public async Task<IEnumerable<Question>> GetAllAsync()
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Question>("SELECT * FROM questions ORDER BY created_at DESC");
    }

    public async Task<int> CreateAsync(string text, IEnumerable<int> categoryIds)
    {
        using var conn = db.Create();
        var id = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO questions (text) VALUES (@text) RETURNING id", new { text });
        foreach (var catId in categoryIds)
            await conn.ExecuteAsync(
                "INSERT OR IGNORE INTO question_categories (question_id, category_id) VALUES (@qid, @cid)",
                new { qid = id, cid = catId });
        return id;
    }

    public async Task UpdateAsync(int id, string text, IEnumerable<int> categoryIds)
    {
        using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE questions SET text = @text, updated_at = @now WHERE id = @id",
            new { text, now = DateTime.UtcNow, id });
        await conn.ExecuteAsync("DELETE FROM question_categories WHERE question_id = @id", new { id });
        foreach (var catId in categoryIds)
            await conn.ExecuteAsync(
                "INSERT OR IGNORE INTO question_categories (question_id, category_id) VALUES (@qid, @cid)",
                new { qid = id, cid = catId });
    }

    public async Task SetActiveAsync(int id, bool active)
    {
        using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE questions SET active = @active WHERE id = @id", new { active, id });
    }

    public async Task<IEnumerable<int>> GetCategoryIdsForQuestionAsync(int questionId)
    {
        using var conn = db.Create();
        return await conn.QueryAsync<int>(
            "SELECT category_id FROM question_categories WHERE question_id = @qid", new { qid = questionId });
    }

    // -- Import --

    public async Task BulkImportAsync(IEnumerable<(string Text, string[] CategoryNames)> rows)
    {
        using var conn = db.Create();
        var allCats = (await conn.QueryAsync<Category>("SELECT * FROM categories")).ToList();

        foreach (var (text, catNames) in rows)
        {
            var id = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO questions (text) VALUES (@text) RETURNING id", new { text });

            foreach (var catName in catNames)
            {
                var cat = allCats.FirstOrDefault(c =>
                    string.Equals(c.Name, catName.Trim(), StringComparison.OrdinalIgnoreCase));

                if (cat == null)
                {
                    var catId = await conn.ExecuteScalarAsync<int>(
                        "INSERT INTO categories (name) VALUES (@name) RETURNING id",
                        new { name = catName.Trim() });
                    cat = new Category { Id = catId, Name = catName.Trim() };
                    allCats.Add(cat);
                }

                await conn.ExecuteAsync(
                    "INSERT OR IGNORE INTO question_categories (question_id, category_id) VALUES (@qid, @cid)",
                    new { qid = id, cid = cat.Id });
            }
        }
    }

    // -- Seed questions from the embedded resource --

    public async Task SeedQuestionsAsync()
    {
        using var conn = db.Create();
        var existingCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM questions");
        if (existingCount > 0) return; // already seeded

        var resourceName = Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("questions-seed.txt"));

        if (resourceName == null) return;

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var entries = ParseQuestionFile(lines);
        await BulkImportAsync(entries);
    }

    private static IEnumerable<(string Text, string[] CategoryNames)> ParseQuestionFile(string[] lines)
    {
        var categories = Array.Empty<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith('#'))
            {
                categories = line[1..].Split(',').Select(c => c.Trim()).ToArray();
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                yield return (line.Trim(), categories);
            }
        }
    }
}
