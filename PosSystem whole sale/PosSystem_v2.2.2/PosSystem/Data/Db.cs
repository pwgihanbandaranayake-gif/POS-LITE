using System.Data;

namespace PosSystem.Data;

/// <summary>
/// Database helper. The connection string is loaded once at startup from appsettings.json.
/// Every repository uses Db.OpenAsync() or Db.InTransactionAsync().
/// </summary>
public static class Db
{
    public static string ConnectionString { get; set; } = "";

    public static async Task<SqlConnection> OpenAsync()
    {
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        return cn;
    }

    /// <summary>Runs the work inside one SQL transaction. Commit on success, rollback on any error.</summary>
    public static async Task<T> InTransactionAsync<T>(Func<SqlConnection, SqlTransaction, Task<T>> work)
    {
        await using var cn = await OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var result = await work(cn, tx);
            await tx.CommitAsync();
            return result;
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { /* connection may already be broken */ }
            throw;
        }
    }

    /// <summary>Next document number, e.g. MAIN-261009-0001 (see usp_GetNextDocNo).</summary>
    public static async Task<string> NextDocNoAsync(SqlConnection cn, SqlTransaction tx, string docType, int locationId)
    {
        var p = new DynamicParameters();
        p.Add("@DocType", docType);
        p.Add("@LocationID", locationId);
        p.Add("@Date", DateTime.Now);
        p.Add("@DocNo", dbType: DbType.String, size: 50, direction: ParameterDirection.Output);
        await cn.ExecuteAsync("dbo.usp_GetNextDocNo", p, tx, commandType: CommandType.StoredProcedure);
        return p.Get<string>("@DocNo");
    }

    /// <summary>Quick connection test used on the login screen.</summary>
    public static async Task<bool> CanConnectAsync()
    {
        try { await using var cn = await OpenAsync(); return true; }
        catch { return false; }
    }
}
