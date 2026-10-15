using System.Globalization;
using System.Text.Json;
using DTNTB.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DTNTB.Infrastructure.Services
{
    /// <summary>
    /// Đổi toạ độ ra địa chỉ. Cấu hình trong appsettings, mục "Geocoding":
    ///   Provider = "None"      : không gọi dịch vụ nào (mặc định), địa chỉ để trống.
    ///   Provider = "Nominatim" : máy chủ tương thích OpenStreetMap Nominatim (BaseUrl, ví dụ https://nominatim.openstreetmap.org).
    ///   Provider = "Custom"    : dịch vụ bất kỳ trả JSON (ví dụ mapVNPT):
    ///                            UrlTemplate có {lat} {lng} {key}, AddressJsonPath trỏ tới trường địa chỉ,
    ///                            ví dụ "results[0].formatted_address".
    /// Mọi lỗi đều trả null để không chặn việc điểm danh.
    /// </summary>
    public class ReverseGeocodingService : IReverseGeocodingService
    {
        // Máy chủ Nominatim công cộng chỉ cho phép tối đa 1 request/giây cho mỗi ứng dụng
        // (https://operations.osmfoundation.org/policies/nominatim/). Giới hạn chung toàn tiến trình.
        private static readonly SemaphoreSlim NominatimGate = new(1, 1);
        private static DateTime _lastNominatimCallUtc = DateTime.MinValue;

        // Máy chủ không ra được Internet (hoặc dịch vụ bản đồ sập): sau một lần lỗi kết nối/quá giờ thì
        // tạm ngừng gọi trong một khoảng, để điểm danh không phải chờ hết thời gian chờ ở mỗi lượt.
        private static readonly TimeSpan PauseAfterFailure = TimeSpan.FromMinutes(5);
        private static long _pausedUntilUtcTicks;

        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ReverseGeocodingService> _logger;
        private readonly string _provider;
        private readonly string _baseUrl;
        private readonly string _urlTemplate;
        private readonly string _addressJsonPath;
        private readonly string _apiKey;
        private readonly string _language;

        public ReverseGeocodingService(
            HttpClient httpClient,
            IMemoryCache cache,
            IConfiguration config,
            ILogger<ReverseGeocodingService> logger)
        {
            _httpClient = httpClient;
            _cache = cache;
            _logger = logger;
            _provider = (config["Geocoding:Provider"] ?? "None").Trim();
            _baseUrl = (config["Geocoding:BaseUrl"] ?? string.Empty).Trim().TrimEnd('/');
            _urlTemplate = (config["Geocoding:UrlTemplate"] ?? string.Empty).Trim();
            _addressJsonPath = (config["Geocoding:AddressJsonPath"] ?? string.Empty).Trim();
            _apiKey = config["Geocoding:ApiKey"] ?? string.Empty;
            _language = string.IsNullOrWhiteSpace(config["Geocoding:Language"]) ? "vi" : config["Geocoding:Language"]!.Trim();

            var userAgent = config["Geocoding:UserAgent"];
            if (!string.IsNullOrWhiteSpace(userAgent) && _httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            }
        }

        public async Task<string?> GetAddressAsync(double latitude, double longitude, CancellationToken ct = default)
        {
            if (_provider.Equals("None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(_provider))
                return null;

            // Làm tròn ~11 m để các lượt điểm danh gần nhau dùng chung kết quả, giảm số lần gọi dịch vụ.
            var cacheKey = $"geo:{Math.Round(latitude, 4).ToString(CultureInfo.InvariantCulture)}:{Math.Round(longitude, 4).ToString(CultureInfo.InvariantCulture)}";
            if (_cache.TryGetValue(cacheKey, out string? cached)) return cached;
            if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _pausedUntilUtcTicks)) return null;

            try
            {
                string? address = _provider.ToUpperInvariant() switch
                {
                    "NOMINATIM" => await QueryNominatimAsync(latitude, longitude, ct),
                    "CUSTOM" => await QueryCustomAsync(latitude, longitude, ct),
                    _ => null
                };

                address = Normalize(address);
                if (address != null)
                {
                    _cache.Set(cacheKey, address, TimeSpan.FromHours(12));
                }
                return address;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                if (ex is HttpRequestException or TaskCanceledException)
                {
                    Interlocked.Exchange(ref _pausedUntilUtcTicks, DateTime.UtcNow.Add(PauseAfterFailure).Ticks);
                    _logger.LogWarning(ex,
                        "Không kết nối được dịch vụ địa chỉ (provider {Provider}); tạm ngừng tra địa chỉ {Minutes} phút. " +
                        "Nếu máy chủ không có Internet, đặt Geocoding__Provider=None.", _provider, PauseAfterFailure.TotalMinutes);
                }
                else
                {
                    _logger.LogWarning(ex, "Không đổi được toạ độ {Lat},{Lng} ra địa chỉ (provider {Provider}).", latitude, longitude, _provider);
                }
                return null;
            }
        }

        private async Task<string?> QueryNominatimAsync(double lat, double lng, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_baseUrl)) return null;

            var url = $"{_baseUrl}/reverse?format=jsonv2&zoom=18&addressdetails=1" +
                      $"&lat={lat.ToString(CultureInfo.InvariantCulture)}&lon={lng.ToString(CultureInfo.InvariantCulture)}" +
                      $"&accept-language={Uri.EscapeDataString(_language)}";

            HttpResponseMessage response;
            await NominatimGate.WaitAsync(ct);
            try
            {
                var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - _lastNominatimCallUtc);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                response = await _httpClient.GetAsync(url, ct);
            }
            finally
            {
                _lastNominatimCallUtc = DateTime.UtcNow;
                NominatimGate.Release();
            }

            using var _ = response;
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("address", out var addr) && addr.ValueKind == JsonValueKind.Object)
            {
                // Ghép gọn theo thứ tự: số nhà + đường, thôn/xóm, xã/phường, huyện/quận, tỉnh/thành.
                string? Get(params string[] keys)
                {
                    foreach (var k in keys)
                        if (addr.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                            return v.GetString()!.Trim();
                    return null;
                }

                var street = string.Join(" ", new[] { Get("house_number"), Get("road") }.Where(s => !string.IsNullOrEmpty(s)));
                var parts = new List<string?>
                {
                    string.IsNullOrWhiteSpace(street) ? null : street,
                    Get("hamlet", "neighbourhood", "quarter", "isolated_dwelling"),
                    Get("village", "suburb", "town"),
                    Get("county", "city_district", "district"),
                    Get("city", "state", "province")
                };

                var compact = string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(compact)) return compact;
            }

            return root.TryGetProperty("display_name", out var display) ? StripCountry(display.GetString()) : null;
        }

        private async Task<string?> QueryCustomAsync(double lat, double lng, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_urlTemplate) || string.IsNullOrEmpty(_addressJsonPath)) return null;

            var url = _urlTemplate
                .Replace("{lat}", lat.ToString(CultureInfo.InvariantCulture))
                .Replace("{lng}", lng.ToString(CultureInfo.InvariantCulture))
                .Replace("{key}", Uri.EscapeDataString(_apiKey));

            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var node = SelectPath(doc.RootElement, _addressJsonPath);
            return node is { ValueKind: JsonValueKind.String } ? StripCountry(node.Value.GetString()) : null;
        }

        /// <summary>Đi theo đường dẫn dạng "results[0].formatted_address".</summary>
        private static JsonElement? SelectPath(JsonElement root, string path)
        {
            JsonElement current = root;
            foreach (var rawSegment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var segment = rawSegment;
                int? index = null;
                var bracket = segment.IndexOf('[');
                if (bracket >= 0 && segment.EndsWith(']')
                    && int.TryParse(segment[(bracket + 1)..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    index = parsed;
                    segment = segment[..bracket];
                }

                if (!string.IsNullOrEmpty(segment))
                {
                    if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current)) return null;
                }

                if (index.HasValue)
                {
                    if (current.ValueKind != JsonValueKind.Array || current.GetArrayLength() <= index.Value) return null;
                    current = current[index.Value];
                }
            }
            return current;
        }

        private static string? StripCountry(string? address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            var parts = address.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.Equals("Việt Nam", StringComparison.OrdinalIgnoreCase)
                         && !p.Equals("Vietnam", StringComparison.OrdinalIgnoreCase)
                         && !(p.Length is >= 5 and <= 6 && p.All(char.IsDigit))); // bỏ mã bưu chính
            return string.Join(", ", parts);
        }

        private static string? Normalize(string? address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            var clean = new string(address.Where(c => !char.IsControl(c)).ToArray()).Trim();
            return clean.Length > 500 ? clean[..500] : clean;
        }
    }
}
