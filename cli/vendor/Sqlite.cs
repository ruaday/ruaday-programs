using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Concurrent;

namespace RuaFind.Core;

// Windows 10+ includes this SQLite engine. No NuGet/native DLL payload is needed.
public sealed class Sqlite : IDisposable
{
    private IntPtr handle;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Writers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim writer;
    private bool ownsWriter;
    public Sqlite(string path)
    {
        writer = Writers.GetOrAdd(System.IO.Path.GetFullPath(path), _ => new(1, 1));
        Check(sqlite3_open_v2(Utf8(path), out handle, 0x00000001 | 0x00000040 | 0x00010000, IntPtr.Zero));
        sqlite3_busy_timeout(handle, 5000);
    }
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + '\0');
    public void Check(int code) { if (code == 9) throw new OperationCanceledException(); if (code != 0) throw new InvalidOperationException($"SQLite ({code}/{sqlite3_extended_errcode(handle)}): {Marshal.PtrToStringUTF8(sqlite3_errmsg(handle))}"); }
    public void Interrupt() { if (handle != IntPtr.Zero) sqlite3_interrupt(handle); }
    public void Execute(string sql)
    {
        var begin = sql.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase);
        if (begin) { writer.Wait(); ownsWriter = true; }
        try { using var statement = Prepare(sql); statement.Step(); }
        catch { if (begin) ReleaseWriter(); throw; }
        if (ownsWriter && sqlite3_get_autocommit(handle) != 0) ReleaseWriter();
    }
    private void ReleaseWriter() { if (ownsWriter) { ownsWriter = false; writer.Release(); } }
    private int Step(IntPtr statement, bool writes)
    {
        // SQLite allows one writer. Queue writers inside the process instead of
        // letting a fast background loop repeatedly win its busy-timeout race.
        var acquire = writes && !ownsWriter;
        if (acquire) writer.Wait();
        try { return sqlite3_step(statement); }
        finally { if (acquire) writer.Release(); }
    }
    public Statement Prepare(string sql)
    {
        Check(sqlite3_prepare_v2(handle, Utf8(sql), -1, out var statement, IntPtr.Zero));
        return new Statement(this, statement);
    }
    public void Dispose() { if (handle != IntPtr.Zero) { sqlite3_close_v2(handle); handle = IntPtr.Zero; } ReleaseWriter(); }
    public sealed class Statement(Sqlite owner, IntPtr handle) : IDisposable
    {
        private readonly bool writes = sqlite3_stmt_readonly(handle) == 0;
        public Statement Bind(int index, string? value) { owner.Check(value is null ? sqlite3_bind_null(handle, index) : sqlite3_bind_text16(handle, index, value, checked(value.Length * 2), new IntPtr(-1))); return this; }
        public Statement Bind(int index, long value) { owner.Check(sqlite3_bind_int64(handle, index, value)); return this; }
        public bool Step() { var code = owner.Step(handle, writes); if (code == 100) return true; if (code != 101) owner.Check(code); return false; }
        public long Long(int index) => sqlite3_column_int64(handle, index);
        public string Text(int index) => Marshal.PtrToStringUTF8(sqlite3_column_text(handle, index)) ?? "";
        public void Reset() { owner.Check(sqlite3_reset(handle)); sqlite3_clear_bindings(handle); }
        public void Dispose() => sqlite3_finalize(handle);
    }
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_extended_errcode(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_interrupt(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int size, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_stmt_readonly(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_get_autocommit(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_reset(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_clear_bindings(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int sqlite3_bind_text16(IntPtr statement, int index, [MarshalAs(UnmanagedType.LPWStr)] string value, int length, IntPtr destructor);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_null(IntPtr statement, int index);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr statement, int column);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
}
