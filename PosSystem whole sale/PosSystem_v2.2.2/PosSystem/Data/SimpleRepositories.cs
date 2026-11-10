using System.Data;

namespace PosSystem.Data;

public static class SettingsRepository
{

    public static async Task<Dictionary<string, string>> GetAllAsync()
    {
        await using var cn = await Db.OpenAsync();
        var rows = await cn.QueryAsync<(string SettingKey, string? SettingValue)>("SELECT SettingKey, SettingValue FROM dbo.AppSettings");
        return rows.ToDictionary(r => r.SettingKey, r => r.SettingValue ?? "", StringComparer.OrdinalIgnoreCase);
    }

    public static async Task SaveAsync(IDictionary<string, string> values)
    {
        await Db.InTransactionAsync(async (cn, tx) =>
        {
            foreach (var kv in values)
            {
                var n = await cn.ExecuteAsync("UPDATE dbo.AppSettings SET SettingValue = @v, DateModified = GETDATE() WHERE SettingKey = @k",
                    new { k = kv.Key, v = kv.Value }, tx);
                if (n == 0)
                    await cn.ExecuteAsync("INSERT dbo.AppSettings(SettingKey, SettingValue) VALUES(@k, @v)", new { k = kv.Key, v = kv.Value }, tx);
            }
            return 0;
        });
    }
}

public static class ReportRepository
{

    public static async Task<DataTable> RunAsync(string procedure, IDictionary<string, object?> parameters)
    {
        if (!procedure.StartsWith("rpt_", StringComparison.Ordinal)) throw new InvalidOperationException("Only report procedures can be executed.");
        await using var cn = await Db.OpenAsync();
        var p = new DynamicParameters();
        foreach (var kv in parameters) p.Add("@" + kv.Key, kv.Value);
        using var reader = await cn.ExecuteReaderAsync("dbo." + procedure, p, commandType: CommandType.StoredProcedure, commandTimeout: 180);
        var table = new DataTable();
        table.Load(reader);
        return table;
    }
}

/// <summary>Generic CRUD for every metadata-driven master. SQL identifiers come only from MasterCatalog.</summary>
public static class MasterRepository
{

    public static async Task<DataTable> ListAsync(MasterDefinition def, string? search)
    {
        await using var cn = await Db.OpenAsync();
        using var reader = await cn.ExecuteReaderAsync(def.ListSql, new { Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim() });
        var t = new DataTable();
        t.Load(reader);
        return t;
    }

    public static async Task<Dictionary<string, object?>> GetAsync(MasterDefinition def, int id)
    {
        await using var cn = await Db.OpenAsync();
        var row = (IDictionary<string, object>?)await cn.QueryFirstOrDefaultAsync($"SELECT * FROM dbo.[{def.Table}] WHERE [{def.KeyColumn}] = @id", new { id });
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (row != null) foreach (var kv in row) result[kv.Key] = kv.Value is DBNull ? null : kv.Value;
        return result;
    }

    public static async Task<int> SaveAsync(MasterDefinition def, int id, IDictionary<string, object?> values, int byUser)
    {
        var p = new DynamicParameters();
        var cols = new List<string>();
        var vals = new List<object?>();
        int i = 0;
        foreach (var f in def.Fields)
        {
            values.TryGetValue(f.Column, out var raw);
            object? v = Normalize(f, raw);
            if (f.Required && (v == null || (v is string s && s.Length == 0)))
                throw new AppException($"{f.Label} is required.");
            if (v is string str && str.Length > f.MaxLength)
                throw new AppException($"{f.Label} can have at most {f.MaxLength} characters.");
            p.Add("@p" + i, v);
            vals.Add(v);
            cols.Add(f.Column);
            i++;
        }

        await using var cn = await Db.OpenAsync();

        // unique checks
        for (int k = 0; k < def.Fields.Count; k++)
        {
            var f = def.Fields[k];
            if (!f.Unique) continue;
            var v = vals[k];
            if (v == null || (v is string sv && sv.Length == 0)) continue;
            var del = def.SoftDeleteColumn == null ? "" : $" AND ISNULL([{def.SoftDeleteColumn}],0) = 0";
            var n = await cn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{def.Table}] WHERE [{f.Column}] = @v AND [{def.KeyColumn}] <> @id{del}", new { v, id });
            if (n > 0) throw new AppException($"{f.Label} '{v}' already exists.");
        }

        var extras = id == 0 ? def.InsertExtras : def.UpdateExtras;
        foreach (var kv in extras)
        {
            var name = "@x" + cols.Count;
            p.Add(name, kv.Value switch { "@User" => byUser, "@Now" => DateTime.Now, _ => kv.Value });
            cols.Add(kv.Key);
        }

        if (id == 0)
        {
            var names = Enumerable.Range(0, def.Fields.Count).Select(k => "@p" + k)
                .Concat(Enumerable.Range(def.Fields.Count, cols.Count - def.Fields.Count).Select(k => "@x" + k)).ToList();
            if (def.KeyIsIdentity)
            {
                var sql = $"INSERT dbo.[{def.Table}] ({string.Join(",", cols.Select(c => $"[{c}]"))}) VALUES ({string.Join(",", names)}); SELECT CAST(SCOPE_IDENTITY() AS INT);";
                return await cn.ExecuteScalarAsync<int>(sql, p);
            }
            var newId = await cn.ExecuteScalarAsync<int>($"SELECT ISNULL(MAX([{def.KeyColumn}]),0) + 1 FROM dbo.[{def.Table}]");
            p.Add("@newId", newId);
            await cn.ExecuteAsync($"INSERT dbo.[{def.Table}] ([{def.KeyColumn}],{string.Join(",", cols.Select(c => $"[{c}]"))}) VALUES (@newId,{string.Join(",", names)})", p);
            return newId;
        }

        var sets = new List<string>();
        for (int k = 0; k < cols.Count; k++)
            sets.Add($"[{cols[k]}] = " + (k < def.Fields.Count ? "@p" + k : "@x" + k));
        p.Add("@id", id);
        await cn.ExecuteAsync($"UPDATE dbo.[{def.Table}] SET {string.Join(", ", sets)} WHERE [{def.KeyColumn}] = @id", p);
        return id;
    }

    public static async Task DeleteAsync(MasterDefinition def, int id, int byUser)
    {
        await using var cn = await Db.OpenAsync();
        try
        {
            if (def.SoftDeleteColumn != null)
                await cn.ExecuteAsync($"UPDATE dbo.[{def.Table}] SET [{def.SoftDeleteColumn}] = 1 WHERE [{def.KeyColumn}] = @id", new { id });
            else
                await cn.ExecuteAsync($"DELETE FROM dbo.[{def.Table}] WHERE [{def.KeyColumn}] = @id", new { id });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 547)
        {
            throw new AppException("This record is used by other records and cannot be deleted.");
        }
    }

    public static Task<IReadOnlyList<LookupItem>> LookupAsync(string sql) => LookupRepository.QueryLookupAsync(sql);

    private static object? Normalize(MasterField f, object? raw)
    {
        switch (f.Kind)
        {
            case FieldKind.Bool:
                return raw is bool b ? b : raw != null && bool.TryParse(raw.ToString(), out var bb) && bb;
            case FieldKind.Number:
                if (raw == null || string.IsNullOrWhiteSpace(raw.ToString())) return f.Required ? null : 0m;
                if (decimal.TryParse(raw.ToString(), out var d)) return d;
                throw new AppException($"{f.Label} must be a number.");
            case FieldKind.Lookup:
                if (raw == null) return null;
                if (raw is LookupItem li) return li.Id;
                return int.TryParse(raw.ToString(), out var n) ? n : null;
            default:
                var s = raw?.ToString()?.Trim();
                return string.IsNullOrEmpty(s) ? null : s;
        }
    }
}
