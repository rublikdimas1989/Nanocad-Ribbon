namespace NcTestRibbon.Net
{
  using System;
  using System.IO;
  using System.Net.Http;
  using System.Text.Json;
  using System.Threading;
  using System.Threading.Tasks;
  using NcTestRibbon.Configuration;

  /// <summary>
  /// Multipart upload of DXF only to solver api/v1/datasets
  /// (matches: curl -F "dxf=@drawing_map.dxf" …/api/v1/datasets).
  /// Avoids System.Net.Http.Json (MissingMethodException under nanoCAD host).
  /// </summary>
  public static class MapUploadClient
  {
    public const string StatusOk = "Передача данных завершена";
    public const string StatusFail = "Ошибка передачи данных";

    private static readonly object Gate = new object();
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    };

    private static HttpClient _client;
    private static string _clientBase;

    public static event Action<string> StatusChanged;

    public static string LastStatus { get; private set; } = string.Empty;

    public static void PublishStatus(string message)
    {
      LastStatus = message ?? string.Empty;
      try { StatusChanged?.Invoke(LastStatus); }
      catch { }
    }

    private sealed class DatasetResponse
    {
      public string DatasetId { get; set; }
    }

    /// <summary>
    /// POST multipart field <c>dxf</c> only (no terrain).
    /// </summary>
    public static async Task<string> UploadMapAsync(string dxfPath, CancellationToken ct = default)
    {
      if (string.IsNullOrWhiteSpace(dxfPath) || !File.Exists(dxfPath))
      {
        throw new FileNotFoundException("DXF не найден.", dxfPath);
      }

      HttpClient client = GetClient();
      using var form = new MultipartFormDataContent();
      form.Add(new StreamContent(File.OpenRead(dxfPath)), "dxf", Path.GetFileName(dxfPath));

      using HttpResponseMessage response = await client.PostAsync("api/v1/datasets", form, ct).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();
      string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
      ct.ThrowIfCancellationRequested();
      DatasetResponse dataset = JsonSerializer.Deserialize<DatasetResponse>(json ?? string.Empty, JsonOptions);
      string id = dataset?.DatasetId;
      if (string.IsNullOrWhiteSpace(id))
      {
        throw new InvalidDataException("Решатель не вернул datasetId.");
      }

      return id.Trim();
    }

    private static HttpClient GetClient()
    {
      NetConf conf = NetConf.Load();
      string baseAddr = NetConf.BuildBaseAddress(conf.Host);
      lock (Gate)
      {
        if (_client != null && string.Equals(_clientBase, baseAddr, StringComparison.OrdinalIgnoreCase))
        {
          return _client;
        }

        _client?.Dispose();
        _client = new HttpClient
        {
          BaseAddress = new Uri(baseAddr),
          Timeout = TimeSpan.FromMinutes(10)
        };
        _clientBase = baseAddr;
        return _client;
      }
    }
  }
}
