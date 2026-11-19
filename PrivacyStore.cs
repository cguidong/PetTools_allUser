using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
namespace OffWorkWidget
{
    internal readonly record struct EffectParameters(int Count, double Speed, double Size, int Opacity, double Wind)
    {
        internal static EffectParameters GetDefaults(int effect) => effect switch
        {
            1 => new(7, 1, 1, 100, 0),
            2 => new(24, .8, 1, 80, .1),
            3 => new(32, .8, 1, 85, .2),
            5 => new(16, .9, 1, 88, .4),
            7 => new(20, .9, 1, 88, .6),
            8 => new(20, .7, 1, 90, 0),
            9 => new(24, .8, 1, 75, .2),
            10 => new(34, 1.2, 1, 75, -.3),
            11 => new(8, 1, 1, 92, 0),
            12 => new(28, 1, 1, 90, .2),
            13 => new(22, .8, 1, 82, 0),
            14 => new(22, .8, 1, 86, .3),
            15 => new(18, 1, 1, 72, 0),
            16 => new(9, .9, 1, 92, 0),
            17 => new(18, 1, 1, 90, 0),
            18 => new(6, .8, 1, 85, .1),
            19 => new(24, 1, 1, 90, 0),
            20 => new(9, .85, 1, 94, 0),
            21 => new(6, .85, 1, 93, .2),
            22 => new(12, .75, 1, 94, .3),
            _ => throw new ArgumentOutOfRangeException(nameof(effect))
        };

        internal bool IsValid => Count is >= 6 and <= 55 && Speed is >= .3 and <= 2.2 &&
            Size is >= .5 and <= 1.8 && Opacity is >= 20 and <= 100 && Wind is >= -1.5 and <= 1.5;
    }

    public sealed class PrivacyStore : IDisposable
    {
        const int Rounds = 210000;
        IntPtr db;
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OffWorkWidget.salary.v1");
        public PrivacyStore(string path, decimal initialSalary)
        {
            try
            {
                if (path != ":memory:" && Path.GetDirectoryName(Path.GetFullPath(path)) is string directory)
                    Directory.CreateDirectory(directory);
                Check(Native.sqlite3_open_v2(Utf8(path), out db, 6, IntPtr.Zero));
                Check(Native.sqlite3_busy_timeout(db, 3000));
                Execute("PRAGMA secure_delete=ON");
                Execute("CREATE TABLE IF NOT EXISTS privacy (id INTEGER PRIMARY KEY CHECK(id=1), salt TEXT NOT NULL, hash TEXT NOT NULL, rounds TEXT NOT NULL, salary TEXT NOT NULL)");
                Execute("CREATE TABLE IF NOT EXISTS privacy_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                // Existing cumulative coins become spendable once, only when the balance is first created.
                Execute("INSERT OR IGNORE INTO privacy_meta(key,value) SELECT 'coins_balance',value FROM privacy_meta WHERE key='coins_total'");
                Execute("INSERT OR IGNORE INTO privacy_meta(key,value) VALUES('coins_balance','0')");
                if (Read() == null) Execute("INSERT OR IGNORE INTO privacy(id,salt,hash,rounds,salary) VALUES(1,'','','',?)", Seal(initialSalary));
                LoadSalary(); // A damaged database must not fall back to an unlocked default.
                RefundRetiredEffects();
            }
            catch { Dispose(); throw; }
        }
        public int CoinCount => ReadMetaInt("coins_total");
        public int CoinBalance => ReadMetaInt("coins_balance");
        internal const int CoinBalanceLimit = 9999;
        internal event Action? BalanceChanged;
        internal event Action? EffectUnlockedChanged;
        internal void NotifyBalanceChanged() => BalanceChanged?.Invoke();
        public int SelectedEffect => ReadMetaInt("effect_selected");
        static int ShopPrice(int effect) => effect switch
        {
            1 => 100, 2 or 8 => 150, 3 or 5 or 7 or 12 or 13 or 14 or 15 => 300, 9 or 11 => 500, 10 => 200, 16 => 1000, 17 => 260, 18 => 300, 19 => 520, 20 => 600, 21 => 640, 22 => 780, _ => 0
        };
        public static int EffectPrice(int effect) => ShopPrice(effect) is > 0 and var price ? price : throw new ArgumentOutOfRangeException(nameof(effect));
        internal bool AllShopEffectsUnlocked()
        {
            for (int effect = 1; effect <= 22; effect++)
                if (ShopPrice(effect) > 0 && !EffectUnlocked(effect)) return false;
            return true;
        }

        public bool EffectUnlocked(int effect) => effect == 0 || (ShopPrice(effect) > 0 && ReadMetaInt("effect_unlocked_" + effect) == 1);
        internal event Action? EffectParametersChanged;
        internal EffectParameters GetEffectParameters(int effect)
        {
            var defaults = EffectParameters.GetDefaults(effect);
            var json = ReadMeta("effect_parameters_" + effect);
            if (json == null) return defaults;
            try
            {
                var value = System.Text.Json.JsonSerializer.Deserialize<EffectParameters>(json);
                return value.IsValid ? value : defaults;
            }
            catch (System.Text.Json.JsonException) { return defaults; }
        }
        internal void SetEffectParameters(int effect, EffectParameters value)
        {
            var defaults = EffectParameters.GetDefaults(effect);
            if (!EffectUnlocked(effect) || !value.IsValid) throw new ArgumentOutOfRangeException(nameof(value));
            string key = "effect_parameters_" + effect;
            if (value == defaults) Execute("DELETE FROM privacy_meta WHERE key=?", key);
            else Execute("INSERT OR REPLACE INTO privacy_meta(key,value) VALUES(?,?)", key, System.Text.Json.JsonSerializer.Serialize(value));
            EffectParametersChanged?.Invoke();
        }
        void RefundRetiredEffects()
        {
            Execute("BEGIN IMMEDIATE");
            try
            {
                int refund = (ReadMetaInt("effect_unlocked_4") == 1 ? 200 : 0) + (ReadMetaInt("effect_unlocked_6") == 1 ? 500 : 0);
                if (refund > 0)
                {
                    WriteMetaInt("effect_unlocked_4", 0); WriteMetaInt("effect_unlocked_6", 0);
                    int pending = ReadMetaInt("retired_effect_refund_pending");
                    WriteMetaInt("retired_effect_refund_pending", checked(pending + refund));
                }
                int selected = ReadMetaInt("effect_selected");
                if (selected == 4 || selected == 6) WriteMetaInt("effect_selected", 0);
                ApplyPendingEffectRefund();
                Execute("COMMIT");
            }
            catch { Execute("ROLLBACK"); throw; }
        }
        // Preserve any refund that cannot fit in the integer balance until coins are spent.
        void ApplyPendingEffectRefund()
        {
            int pending = ReadMetaInt("retired_effect_refund_pending");
            if (pending == 0) return;
            int balance = CoinBalance, credit = Math.Min(pending, Math.Max(0, CoinBalanceLimit - balance));
            if (credit == 0) return;
            WriteMetaInt("coins_balance", balance + credit);
            WriteMetaInt("retired_effect_refund_pending", pending - credit);
        }
        public int AddCoin() => AddCoins(1);

        internal int AddCoins(int amount)
        {
            int count;
            Execute("BEGIN IMMEDIATE");
            try
            {
                count = CoinCount;
                int balance = CoinBalance;
                count = (int)Math.Min(int.MaxValue, (long)count + amount);
                WriteMetaInt("coins_total", count);
                if (balance < CoinBalanceLimit) WriteMetaInt("coins_balance", (int)Math.Min(CoinBalanceLimit, (long)balance + amount));
                Execute("COMMIT");
            }
            catch { Execute("ROLLBACK"); throw; }
            BalanceChanged?.Invoke();
            return count;
        }
        public bool UnlockEffect(int effect)
        {
            int price = EffectPrice(effect);
            Execute("BEGIN IMMEDIATE");
            try
            {
                if (EffectUnlocked(effect)) { Execute("COMMIT"); return true; }
                int balance = CoinBalance;
                if (balance < price) { Execute("COMMIT"); return false; }
                WriteMetaInt("coins_balance", balance - price);
                WriteMetaInt("effect_unlocked_" + effect, 1);
                ApplyPendingEffectRefund();
                Execute("COMMIT");
            }
            catch { Execute("ROLLBACK"); throw; }
            BalanceChanged?.Invoke();
            EffectUnlockedChanged?.Invoke();
            return true;
        }
        // Result and payment are committed together so reopening today never charges twice.
        internal bool DrawDailyWord(DateTime localDate, Settings settings, out bool drawn)
        {
            int day = localDate.Year * 10000 + localDate.Month * 100 + localDate.Day;
            drawn = false;
            Execute("BEGIN IMMEDIATE");
            try
            {
                if (ReadMetaInt("fortune_day") == day && ReadMetaInt("fortune_result") > 0)
                {
                    Execute("COMMIT");
                    return true;
                }
                int balance = CoinBalance;
                if (balance < 10) { Execute("COMMIT"); return false; }
                int result = DailyWord.Pick(localDate, settings);
                WriteMetaInt("coins_balance", balance - 10);
                WriteMetaInt("fortune_result", result);
                WriteMetaInt("fortune_day", day);
                ApplyPendingEffectRefund();
                Execute("COMMIT");
                drawn = true;
            }
            catch { Execute("ROLLBACK"); throw; }
            BalanceChanged?.Invoke();
            return true;
        }
        internal int TodayWord(DateTime localDate)
        {
            int day = localDate.Year * 10000 + localDate.Month * 100 + localDate.Day;
            return ReadMetaInt("fortune_day") == day ? ReadMetaInt("fortune_result") : 0;
        }
        public void SelectEffect(int effect)
        {
            if (!EffectUnlocked(effect)) throw new InvalidOperationException("请先解锁特效。");
            WriteMetaInt("effect_selected", effect);
            EffectParametersChanged?.Invoke();
        }
        public bool HasPassword { get { return ReadRequired()[1].Length != 0; } }
        public decimal LoadSalary()
        {
            var clear = NativeDataProtection.Unprotect(Convert.FromBase64String(ReadRequired()[3]), Entropy);
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
            return VerifyRow(password, ReadRequired());
        }
        static bool VerifyRow(string password, string[] row)
        {
            if (row[1].Length == 0) return false;
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
        // Only PBKDF2 runs on the pool; database access resumes on the calling UI context.
        public int UnlockFailures => ReadMetaInt("unlock_failures");
        public async Task<bool> VerifyAsync(string password, CancellationToken cancellationToken = default)
        {
            var row = ReadRequired();
            cancellationToken.ThrowIfCancellationRequested();
            bool verified = await Task.Run(() => VerifyRow(password, row), cancellationToken);
            Execute("BEGIN IMMEDIATE");
            try
            {
                EnsurePasswordState(row, cancellationToken);
                WriteMetaInt("unlock_failures", verified ? 0 : Math.Min(UnlockFailures, 998) + 1);
                Execute("COMMIT");
                return verified;
            }
            catch { Execute("ROLLBACK"); throw; }
        }
        public async Task SetInitialPasswordAsync(string password, CancellationToken cancellationToken = default)
        {
            var row = ReadRequired();
            if (row[1].Length != 0) throw new InvalidOperationException("已设置密码，请验证原密码后修改。");
            await WritePasswordAsync(password, false, row, cancellationToken);
        }
        public async Task ChangePasswordAsync(string oldPassword, string newPassword, CancellationToken cancellationToken = default)
        {
            var row = ReadRequired();
            cancellationToken.ThrowIfCancellationRequested();
            bool verified = await Task.Run(() => VerifyRow(oldPassword, row), cancellationToken);
            EnsurePasswordState(row, cancellationToken);
            if (!verified) throw new ArgumentException("原密码不正确。");
            await WritePasswordAsync(newPassword, false, row, cancellationToken);
        }
        public Task ResetPasswordAndSalaryAsync(string password, CancellationToken cancellationToken = default)
        {
            return WritePasswordAsync(password, true, ReadRequired(), cancellationToken);
        }
        async Task WritePasswordAsync(string password, bool resetSalary, string[] original, CancellationToken cancellationToken)
        {
            ValidatePassword(password);
            cancellationToken.ThrowIfCancellationRequested();
            byte[] salt = RandomNumberGenerator.GetBytes(32);
            byte[]? hash = null;
            try
            {
                hash = await Task.Run(() => Derive(password, salt, Rounds), cancellationToken);
                CommitPassword(salt, hash, resetSalary, original, cancellationToken);
            }
            finally
            {
                if (hash != null) Array.Clear(hash, 0, hash.Length);
                Array.Clear(salt, 0, salt.Length);
            }
        }
        void EnsurePasswordState(string[] original, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = ReadRequired();
            for (int i = 0; i < 3; i++)
                if (current[i] != original[i]) throw new InvalidOperationException("密码状态已变化，请重新操作。");
        }
        public static void ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 4 || password.Length > 20) throw new ArgumentException("密码长度须为 4–20 位，不能全为空格。");
        }
        public void SetInitialPassword(string password)
        {
            var row = ReadRequired();
            if (row[1].Length != 0) throw new InvalidOperationException("已设置密码，请验证原密码后修改。");
            WritePassword(password, false, row);
        }
        public void ChangePassword(string oldPassword, string newPassword)
        {
            var row = ReadRequired();
            if (!VerifyRow(oldPassword, row)) throw new ArgumentException("原密码不正确。");
            WritePassword(newPassword, false, row);
        }
        public void ResetPasswordAndSalary(string password) { WritePassword(password, true, ReadRequired()); }
        void WritePassword(string password, bool resetSalary, string[] original)
        {
            ValidatePassword(password);
            byte[] salt = RandomNumberGenerator.GetBytes(32);
            byte[]? hash = null;
            try
            {
                hash = Derive(password, salt, Rounds);
                CommitPassword(salt, hash, resetSalary, original, CancellationToken.None);
            }
            finally
            {
                if (hash != null) Array.Clear(hash, 0, hash.Length);
                Array.Clear(salt, 0, salt.Length);
            }
        }
        void CommitPassword(byte[] salt, byte[] hash, bool resetSalary, string[] original, CancellationToken cancellationToken)
        {
            Execute("BEGIN IMMEDIATE");
            try
            {
                EnsurePasswordState(original, cancellationToken);
                Execute("UPDATE privacy SET salt=?,hash=?,rounds=? WHERE id=1",
                    Convert.ToBase64String(salt), Convert.ToBase64String(hash), Rounds.ToString(CultureInfo.InvariantCulture));
                if (resetSalary) Execute("UPDATE privacy SET salary=? WHERE id=1", Seal(0));
                WriteMetaInt("unlock_failures", 0);
                Execute("COMMIT");
            }
            catch { Execute("ROLLBACK"); throw; }
        }
        static byte[] Derive(string password, byte[] salt, int rounds)
        {
            using(var derive = new Rfc2898DeriveBytes(password,salt,rounds,HashAlgorithmName.SHA256)) return derive.GetBytes(32);
        }
        static string Seal(decimal value)
        {
            byte[] clear = Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture));
            try { return Convert.ToBase64String(NativeDataProtection.Protect(clear,Entropy)); }
            finally { Array.Clear(clear,0,clear.Length); }
        }
        string[] ReadRequired() { var row = Read(); if(row == null) throw new InvalidDataException("隐私数据缺失。"); return row; }
        string[]? Read()
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
            if (db == IntPtr.Zero) throw new ObjectDisposedException(nameof(PrivacyStore));
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
        int ReadMetaInt(string key) => int.TryParse(ReadMeta(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? Math.Max(0, value) : 0;
        string? ReadMeta(string key)
        {
            IntPtr stmt = Prepare("SELECT value FROM privacy_meta WHERE key=?");
            try
            {
                byte[] text = Utf8(key);
                Check(Native.sqlite3_bind_text(stmt, 1, text, text.Length - 1, new IntPtr(-1)));
                int result = Native.sqlite3_step(stmt);
                if (result == 101) return null;
                if (result != 100) Check(result);
                int length = Native.sqlite3_column_bytes(stmt, 0);
                byte[] bytes = new byte[length];
                Marshal.Copy(Native.sqlite3_column_text(stmt, 0), bytes, 0, length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally { Native.sqlite3_finalize(stmt); }
        }
        void WriteMetaInt(string key, int value)
        {
            Execute("INSERT OR REPLACE INTO privacy_meta(key,value) VALUES(?,?)",
                key, value.ToString(CultureInfo.InvariantCulture));
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
