namespace NcTestRibbon.Net
{
  using System;
  using System.IO;
  using System.Net.Http;
  using System.Text.Json;
  using System.Threading;
  using System.Threading.Tasks;
  using NcTestRibbon.Cad;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Shell;

  /// <summary>
  /// Multipart upload of DXF + terrain DEM to solver api/v1/datasets.
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

    public static async Task<string> UploadMapAsync(string dxfPath, string terrainPath, CancellationToken ct = default)
    {
      if (string.IsNullOrWhiteSpace(dxfPath) || !File.Exists(dxfPath))
      {
        throw new FileNotFoundException("DXF не найден.", dxfPath);
      }

      if (string.IsNullOrWhiteSpace(terrainPath) || !File.Exists(terrainPath))
      {
        throw new FileNotFoundException("Карта высот (terrain) не найдена.", terrainPath);
      }

      HttpClient client = GetClient();
      using var form = new MultipartFormDataContent();
      form.Add(new StreamContent(File.OpenRead(dxfPath)), "dxf", Path.GetFileName(dxfPath));
      form.Add(new StreamContent(File.OpenRead(terrainPath)), "terrain", Path.GetFileName(terrainPath));

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

    /// <summary>
    /// Resolve DEM/Height path: Maps Height_geo*, else paths.json Background if it looks like a height raster.
    /// </summary>
    public static bool TryResolveTerrainPath(out string path, out string error)
    {
      path = null;
      error = null;
      try
      {
        string maps = CadMapRaster.FindMapsFile(CadMapRaster.Kind.Height);
        if (!string.IsNullOrEmpty(maps) && File.Exists(maps))
        {
          path = maps;
          return true;
        }

        AppPaths paths = AppPaths.Load();
        string bg = paths.Background;
        if (!string.IsNullOrWhiteSpace(bg) && File.Exists(bg))
        {
          string name = Path.GetFileName(bg) ?? string.Empty;
          if (name.IndexOf("Height", StringComparison.OrdinalIgnoreCase) >= 0
              || name.IndexOf("DEM", StringComparison.OrdinalIgnoreCase) >= 0
              || name.IndexOf("высот", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            path = bg;
            return true;
          }
        }

        // Also accept raw Height.tif under Maps if colorized geo is missing.
        string raw = Path.Combine(TracingCache.MapsDir, "Height.tif");
        if (File.Exists(raw))
        {
          path = raw;
          return true;
        }

        string raw2 = Path.Combine(TracingCache.MapsDir, "Height.tiff");
        if (File.Exists(raw2))
        {
          path = raw2;
          return true;
        }

        error = "Не найдена карта высот (terrain). Положите Height_geo.tif в Maps или укажите Height в paths.json / Картография.";
        return false;
      }
      catch (Exception ex)
      {
        error = "Не удалось найти карту высот: " + ex.Message;
        return false;
      }
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
