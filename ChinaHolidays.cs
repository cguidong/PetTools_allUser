using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace OffWorkWidget
{
    internal sealed class ChinaHolidayCalendar
    {
        private const int MaximumBytes = 256 * 1024;
        private readonly object gate = new object();
        private readonly string cacheFolder;
        private readonly SortedDictionary<int, Dictionary<DateTime, bool>> years = new SortedDictionary<int, Dictionary<DateTime, bool>>();
        private readonly Dictionary<int, DateTime> freshness = new Dictionary<int, DateTime>();
        private readonly Dictionary<int, Task<bool>> pending = new Dictionary<int, Task<bool>>();
        private readonly Dictionary<int, string> notices = new Dictionary<int, string>();
        private Dictionary<DateTime, bool> snapshot = new Dictionary<DateTime, bool>();

        internal ChinaHolidayCalendar(string cacheFolder)
        {
            this.cacheFolder = Path.GetFullPath(cacheFolder);
            // Past-year arrangements can never apply again; ignore them so the
            // loaded calendar only carries the current and future years.
            int currentYear = DateTime.Now.Year;
            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach(string name in assembly.GetManifestResourceNames())
            {
                int year;
                if(!TryFileYear(name, out year) || year < currentYear) continue;
                try
                {
                    using(Stream stream = assembly.GetManifestResourceStream(name))
                        LoadJson(year, ReadLimited(stream));
                }
                catch(IOException) { }
                catch(DecoderFallbackException) { }
            }
            if(!Directory.Exists(this.cacheFolder)) return;
            try
            {
                foreach(string path in Directory.GetFiles(this.cacheFolder, "china-holidays-????.json"))
                {
                    int year;
                    if(!TryFileYear(Path.GetFileName(path), out year)) continue;
                    if(year < currentYear)
                    {
                        // Past-year caches can never apply again; remove them too.
                        try { File.Delete(path); } catch(IOException) { } catch(UnauthorizedAccessException) { }
                        continue;
                    }
                    try
                    {
                        if(new FileInfo(path).Length > MaximumBytes) continue;
                        if(LoadJson(year, File.ReadAllText(path, Encoding.UTF8)))
                            freshness[year] = File.GetLastWriteTimeUtc(path);
                    }
                    catch(IOException) { }
                    catch(UnauthorizedAccessException) { }
                }
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }

        internal IEnumerable<DateTime> KnownDates { get { lock(gate) return new List<DateTime>(snapshot.Keys); } }
        internal int RuleCount { get { lock(gate) return snapshot.Count; } }
        internal bool HasYear(int year) { lock(gate) return years.ContainsKey(year); }
        internal bool TryGetWorkday(DateTime date, out bool workday)
        {
            lock(gate) return snapshot.TryGetValue(date.Date, out workday);
        }
        internal string Status(int year)
        {
            lock(gate)
            {
                string notice;
                if(pending.ContainsKey(year)) return year + " 年节假日更新中…";
                if(notices.TryGetValue(year, out notice)) return notice;
                return years.ContainsKey(year) ? year + " 年节假日已就绪" : year + " 年暂无已公布安排，按每周规则执行";
            }
        }

        // Validated snapshots never alter the user's own date adjustments.
        internal bool LoadJson(int year, string json)
        {
            Dictionary<DateTime, bool> rules;
            if(!ValidateJson(year, json, out rules)) return false;
            lock(gate)
            {
                years[year] = rules;
                var next = new Dictionary<DateTime, bool>();
                foreach(var source in years)
                    foreach(var day in source.Value) next[day.Key] = day.Value;
                snapshot = next;
                notices.Remove(year);
            }
            return true;
        }

        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

        internal Task<bool> RefreshAsync(int year, bool force)
        {
            if(year < 1900 || year > 2200) return Task.FromResult(false);
            lock(gate)
            {
                Task<bool> running;
                if(pending.TryGetValue(year, out running)) return running;
                DateTime saved;
                DateTime now = DateTime.UtcNow;
                if(!force && years.ContainsKey(year) && freshness.TryGetValue(year, out saved) && saved <= now && now - saved < TimeSpan.FromDays(7))
                    return Task.FromResult(true);
                var task = Task.Run(async () =>
                {
                    try { return await RefreshInternalAsync(year); }
                    finally { lock(gate) pending.Remove(year); }
                });
                pending[year] = task;
                return task;
            }
        }

        private async Task<bool> RefreshInternalAsync(int year)
        {
            string[] sources = {
                "https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/" + year + ".json",
                "https://cdn.jsdelivr.net/gh/NateScarlet/holiday-cn@master/" + year + ".json"
            };
            foreach (string source in sources)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, source);
                    request.Headers.TryAddWithoutValidation("User-Agent", "OffWorkWidget-HolidayCalendar/1.0");
                    using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode) continue;
                    if (response.Content.Headers.ContentLength > MaximumBytes) continue;
                    string json;
                    using (Stream stream = await response.Content.ReadAsStreamAsync())
                    {
                        json = ReadLimited(stream);
                    }
                    Dictionary<DateTime, bool> rules;
                    if (!ValidateJson(year, json, out rules)) continue;
                    bool cached = await Task.Run(() => WriteCache(year, json));
                    LoadJson(year, json);
                    lock (gate)
                    {
                        freshness[year] = DateTime.UtcNow;
                        notices[year] = year + (cached ? " 年节假日已更新" : " 年节假日已更新，缓存未保存");
                    }
                    return true;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                catch (DecoderFallbackException) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            lock (gate)
                notices[year] = years.ContainsKey(year) ? year + " 年更新失败，继续使用已载入安排" : year + " 年暂无可用安排，按每周规则执行";
            return false;
        }

        private bool WriteCache(int year, string json)
        {
            string temporary = null;
            try
            {
                Directory.CreateDirectory(cacheFolder);
                string target = Path.Combine(cacheFolder, "china-holidays-" + year + ".json");
                temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, json, new UTF8Encoding(true));
                if(File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
                return true;
            }
            catch(IOException) { return false; }
            catch(UnauthorizedAccessException) { return false; }
            finally
            {
                if(temporary != null && File.Exists(temporary))
                {
                    try { File.Delete(temporary); }
                    catch(IOException) { }
                    catch(UnauthorizedAccessException) { }
                }
            }
        }

        private static string ReadLimited(Stream stream)
        {
            using(var buffer = new MemoryStream())
            {
                byte[] chunk = new byte[4096];
                int count;
                while((count = stream.Read(chunk, 0, chunk.Length)) != 0)
                {
                    if(buffer.Length + count > MaximumBytes) throw new IOException("节假日数据过大");
                    buffer.Write(chunk, 0, count);
                }
                return new UTF8Encoding(false, true).GetString(buffer.ToArray()).TrimStart('\uFEFF');
            }
        }

        private static bool TryFileYear(string file, out int year)
        {
            year = 0;
            return file.Length == 24 && file.StartsWith("china-holidays-", StringComparison.Ordinal) && file.EndsWith(".json", StringComparison.Ordinal) &&
                Int32.TryParse(file.Substring(15, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year) && year >= 1900 && year <= 2200;
        }

        private static bool ValidateJson(int year, string json, out Dictionary<DateTime, bool> rules)
        {
            rules = null;
            if(year < 1900 || year > 2200 || String.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumBytes) return false;
            try
            {
                using(var validation = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json.TrimStart('\uFEFF')), System.Xml.XmlDictionaryReaderQuotas.Max))
                {
                    while(validation.Read())
                    {
                        if(validation.NodeType != System.Xml.XmlNodeType.Element) continue;
                        string type = validation.GetAttribute("type");
                        if(validation.LocalName == "isOffDay" && type != "boolean") return false;
                        if(validation.LocalName == "year" && type != "number") return false;
                    }
                }
                CalendarData data;
                using(var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.TrimStart('\uFEFF'))))
                    data = (CalendarData)new DataContractJsonSerializer(typeof(CalendarData)).ReadObject(stream);
                if(data == null || data.Year != year || data.Papers == null || data.Papers.Length == 0 || data.Days == null || data.Days.Length == 0) return false;
                foreach(string paper in data.Papers)
                {
                    Uri uri;
                    if(!Uri.TryCreate(paper, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                        !(uri.Host == "gov.cn" || uri.Host.EndsWith(".gov.cn", StringComparison.OrdinalIgnoreCase))) return false;
                }
                var validated = new Dictionary<DateTime, bool>();
                foreach(CalendarDay day in data.Days)
                {
                    DateTime date;
                    if(day == null || String.IsNullOrWhiteSpace(day.Name) || !DateTime.TryParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
                        (date.Year != year && (date.Year != year - 1 || date.Month != 12)) || validated.ContainsKey(date)) return false;
                    validated.Add(date, !day.IsOffDay);
                }
                rules = validated;
                return true;
            }
            catch(SerializationException) { return false; }
            catch(ArgumentException) { return false; }
            catch(System.Xml.XmlException) { return false; }
        }

        [DataContract]
        private sealed class CalendarData
        {
            [DataMember(Name = "year", IsRequired = true)] public int Year { get; set; }
            [DataMember(Name = "papers", IsRequired = true)] public string[] Papers { get; set; }
            [DataMember(Name = "days", IsRequired = true)] public CalendarDay[] Days { get; set; }
        }
        [DataContract]
        private sealed class CalendarDay
        {
            [DataMember(Name = "name", IsRequired = true)] public string Name { get; set; }
            [DataMember(Name = "date", IsRequired = true)] public string Date { get; set; }
            [DataMember(Name = "isOffDay", IsRequired = true)] public bool IsOffDay { get; set; }
        }
    }
}