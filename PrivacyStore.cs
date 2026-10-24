using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
namespace OffWorkWidget
{
    public sealed class PrivacyStore : IDisposable
    {
        const int Rounds = 210000;
        IntPtr db;
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OffWorkWidget.salary.v1");
        public PrivacyStore(string path, decimal initialSalary)
        {
            try
            {
                if (path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                Check(Native.sqlite3_open_v2(Utf8(path), out db, 6, IntPtr.Zero));
                Check(Native.sqlite3_busy_timeout(db, 3000));
                Execute("PRAGMA secure_delete=ON");
                Execute("CREATE TABLE IF NOT EXISTS privacy (id INTEGER PRIMARY KEY CHECK(id=1), salt TEXT NOT NULL, hash TEXT NOT NULL, rounds TEXT NOT NULL, salary TEXT NOT NULL)");
                if (Read() == null) Execute("INSERT INTO privacy(id,salt,hash,rounds,salary) VALUES(1,'','','',?)", Seal(initialSalary));
                LoadSalary(); // A damaged database must not fall back to an unlocked default.
            }
            catch { Dispose(); throw; }
        }
        public bool HasPassword { get { return ReadRequired()[1].Length != 0; } }
        public decimal LoadSalary()
        {
            var clear = ProtectedData.Unprotect(Convert.FromBase64String(ReadRequired()[3]), Entropy, DataProtectionScope.CurrentUser);
            try { return decimal.Parse(Encoding.UTF8.GetString(clear), CultureInfo.InvariantCulture); }
            finally { Array.Clear(clear,0,clear.Length); }
        }
        public void SaveSalary(decimal value)
        {
            if (value < 0 || value > 100000000) throw new ArgumentOutOfRangeException("value");
            if (LoadSalary() != value) Execute("UPDATE privacy SET salary=? WHERE id=1", Seal(value));
        }
        public bool Verify(string password)
        {
            var row = ReadRequired(); if (row[1].Length == 0) return false;
            int rounds = int.Parse(row[2], CultureInfo.InvariantCulture);
            if (rounds < 100000 || rounds > 2000000) throw new InvalidDataException("密码数据无效。");
            byte[] actual = Derive(password, Convert.FromBase64String(row[0]), rounds), expected = Convert.FromBase64String(row[1]);
            try
            {
                int difference = actual.Length ^ expected.Length;
                for(int i=0; i<Math.Min(actual.Length,expected.Length); i++) difference |= actual[i] ^ expected[i];
                return difference == 0;
            }
            finally { Array.Clear(actual,0,actual.Length); Array.Clear(expected,0,expected.Length); }
        }
        public static void ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 6 || password.Length > 128) throw new ArgumentException("密码长度须为 6–128 位，不能全为空格。");
        }
        public void SetInitialPassword(string password)
        {
            if (HasPassword) throw new InvalidOperationException("已设置密码，请验证原密码后修改。");
            WritePassword(password, false);
        }
        public void ChangePassword(string oldPassword, string newPassword)
        {
            if (!Verify(oldPassword)) throw new ArgumentException("原密码不正确。");
            WritePassword(newPassword, false);
        }
        public void ResetPasswordAndSalary(string password) { WritePassword(password, true); }
        void WritePassword(string password, bool resetSalary)
        {
            ValidatePassword(password);
            byte[] salt = new byte[32]; using(var random = RandomNumberGenerator.Create()) random.GetBytes(salt);
            byte[] hash = Derive(password,salt,Rounds);
            try
            {
                string sealedSalary = resetSalary ? Seal(0) : ReadRequired()[3];
                Execute("BEGIN IMMEDIATE");
                try
                {
                    Execute("UPDATE privacy SET salt=?,hash=?,rounds=?,salary=? WHERE id=1", Convert.ToBase64String(salt), Convert.ToBase64String(hash), Rounds.ToString(CultureInfo.InvariantCulture), sealedSalary);
                    Execute("COMMIT");
                }
                catch { Execute("ROLLBACK"); throw; }
            }
            finally { Array.Clear(hash,0,hash.Length); }
        }
        static byte[] Derive(string password, byte[] salt, int rounds)
        {
            using(var derive = new Rfc2898DeriveBytes(password,salt,rounds,HashAlgorithmName.SHA256)) return derive.GetBytes(32);
        }
        static string Seal(decimal value)
        {
            byte[] clear = Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture));
            try { return Convert.ToBase64String(ProtectedData.Protect(clear,Entropy,DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(clear,0,clear.Length); }
        }
        string[] ReadRequired() { var row = Read(); if(row == null) throw new InvalidDataException("隐私数据缺失。"); return row; }
        string[] Read()
        {
            IntPtr stmt = Prepare("SELECT salt,hash,rounds,salary FROM privacy WHERE id=1");
            try
            {
                int result = Native.sqlite3_step(stmt); if(result == 101) return null; if(result != 100) Check(result);
                var row = new string[4];
                for(int i=0;i<4;i++) { int length = Native.sqlite3_column_bytes(stmt,i); byte[] bytes = new byte[length]; Marshal.Copy(Native.sqlite3_column_text(stmt,i),bytes,0,length); row[i] = Encoding.UTF8.GetString(bytes); }
                return row;
            }
            finally { Native.sqlite3_finalize(stmt); }
        }
        IntPtr Prepare(string sql)
        {
            IntPtr stmt; Check(Native.sqlite3_prepare_v2(db,Utf8(sql),-1,out stmt,IntPtr.Zero)); return stmt;
        }
        void Execute(string sql, params string[] values)
        {
            IntPtr stmt = Prepare(sql);
            try
            {
                for(int i=0;i<values.Length;i++) { byte[] text = Utf8(values[i]); Check(Native.sqlite3_bind_text(stmt,i+1,text,text.Length-1,new IntPtr(-1))); }
                int result = Native.sqlite3_step(stmt); if(result != 101 && result != 100) Check(result);
            }
            finally { Native.sqlite3_finalize(stmt); }
        }
        static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text + "\0"); }
        static void Check(int result) { if(result != 0) throw new IOException("隐私数据库操作失败，错误码：" + result); }
        public void Dispose() { if(db != IntPtr.Zero) { Native.sqlite3_close(db); db = IntPtr.Zero; } }
        static class Native
        {
            const string Library = "winsqlite3.dll";
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_close(IntPtr db);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_busy_timeout(IntPtr db,int milliseconds);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int length,out IntPtr stmt,IntPtr tail);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_bind_text(IntPtr stmt,int index,byte[] value,int length,IntPtr destructor);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_step(IntPtr stmt);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(IntPtr stmt);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_text(IntPtr stmt,int index);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_column_bytes(IntPtr stmt,int index);
        }
    }
}
