namespace NcTestRibbon.Net
{
  using System;
  using System.IO;
  using System.Net.Http;
  using System.Text;
  using System.Text.Json;
  using System.Text.Json.Serialization;
  using System.Threading;
  using System.Threading.Tasks;
  using NcTestRibbon.Configuration;

  /// <summary>
  /// POST BUOS site-search JSON to the solver (path from net.conf BuosSearchPath).
  /// Avoids System.Net.Http.Json under nanoCAD host.
  /// </summary>
  public static class BuosSearchClient
  {
    private static readonly object Gate = new object();
    private static readonly JsonSerializerOptions JsonWrite = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      WriteIndented = false
    };

    private static HttpClient _client;
    private static string _clientBase;

    public sealed class SearchBoundsDto
    {
      public double MinX { get; set; }
      public double MinY { get; set; }
      public double MaxX { get; set; }
      public double MaxY { get; set; }
    }

    public sealed class SiteDto
    {
      public double WidthM { get; set; }
      public double LengthM { get; set; }
      public double[] RotationAnglesDeg { get; set; }
    }

    public sealed class SearchDto
    {
      public double CenterStepM { get; set; }
      public double TerrainSampleStepM { get; set; }
    }

    public sealed class ConstraintsDto
    {
      public double RiverBufferM { get; set; }
      public string[] RiverLayers { get; set; }
      public string[] ForbiddenLayers { get; set; }
      public string PlacementLayer { get; set; }
    }

    public sealed class BuosSearchRequest
    {
      public string DatasetId { get; set; }
      public SearchBoundsDto SearchBounds { get; set; }
      public SiteDto Site { get; set; }
      public SearchDto Search { get; set; }
      public ConstraintsDto Constraints { get; set; }
    }

    /// <summary>
    /// Build request from net.conf + geometry.conf + largest BUOS footprint.
    /// </summary>
    public static BuosSearchRequest BuildRequest(NetConf net, GeometryConf geo, double widthM, double lengthM)
    {
      if (net == null)
      {
        throw new ArgumentNullException(nameof(net));
      }

      if (geo == null || !geo.HasAoi)
      {
        throw new InvalidOperationException("Задайте область интереса (прямоугольник AOI) перед расчётом БУОС.");
      }

      if (string.IsNullOrWhiteSpace(net.DatasetId))
      {
        throw new InvalidOperationException(
          "В net.conf нет DatasetId. Сначала нажмите «Обновить карту на сервере» (успешная загрузка DXF).");
      }

      if (widthM <= 0 || lengthM <= 0 || !double.IsFinite(widthM) || !double.IsFinite(lengthM))
      {
        throw new InvalidOperationException(
          "Нет размеров БУОС. Откройте «Исходные данные» → БУОС и загрузите каталог.");
      }

      return new BuosSearchRequest
      {
        DatasetId = net.DatasetId.Trim(),
        SearchBounds = new SearchBoundsDto
        {
          MinX = geo.MinX,
          MinY = geo.MinY,
          MaxX = geo.MaxX,
          MaxY = geo.MaxY
        },
        Site = new SiteDto
        {
          WidthM = widthM,
          LengthM = lengthM,
          RotationAnglesDeg = net.ParseRotationAngles()
        },
        Search = new SearchDto
        {
          CenterStepM = net.CenterStepM > 0 ? net.CenterStepM : 50,
          TerrainSampleStepM = net.TerrainSampleStepM > 0 ? net.TerrainSampleStepM : 10
        },
        Constraints = new ConstraintsDto
        {
          RiverBufferM = geo.BuosRiverBufferM,
          RiverLayers = net.ParseLayerList(net.RiverLayers),
          ForbiddenLayers = net.ParseLayerList(net.ForbiddenLayers),
          PlacementLayer = string.IsNullOrWhiteSpace(net.BuosPlacementLayer)
            ? "Размещение БУОС"
            : net.BuosPlacementLayer.Trim()
        }
      };
    }

    /// <summary>
    /// POST search JSON; returns raw response body (may be empty).
    /// </summary>
    public static async Task<string> SearchAsync(BuosSearchRequest request, CancellationToken ct = default)
    {
      if (request == null)
      {
        throw new ArgumentNullException(nameof(request));
      }

      NetConf net = NetConf.Load();
      string path = string.IsNullOrWhiteSpace(net.BuosSearchPath)
        ? NetConf.DefaultBuosSearchPath
        : net.BuosSearchPath.Trim();

      string json = JsonSerializer.Serialize(request, JsonWrite);
      HttpClient client = GetClient();
      using var content = new StringContent(json, Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await client.PostAsync(path, content, ct).ConfigureAwait(false);
      string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
      ct.ThrowIfCancellationRequested();
      if (!response.IsSuccessStatusCode)
      {
        string snippet = Truncate(body, 400);
        throw new InvalidDataException(
          "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase
          + " @ " + path
          + (string.IsNullOrWhiteSpace(snippet) ? string.Empty : (": " + snippet)));
      }

      return body ?? string.Empty;
    }

    private static string Truncate(string text, int max)
    {
      if (string.IsNullOrEmpty(text) || text.Length <= max)
      {
        return text ?? string.Empty;
      }

      return text.Substring(0, max) + "…";
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
          Timeout = TimeSpan.FromMinutes(30)
        };
        _clientBase = baseAddr;
        return _client;
      }
    }
  }
}
